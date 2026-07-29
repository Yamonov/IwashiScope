using IwashiScope.Core.History;
using IwashiScope.Core.Models;
using IwashiScope.Core.Session;
using IwashiScope.Infrastructure.Windows.Logging;
using IwashiScope.Infrastructure.Windows.Process;

namespace IwashiScope.Infrastructure.Windows.Session;

public sealed class MeasurementSessionController : IAsyncDisposable
{
    private readonly Func<MeasurementMode, ProcessLaunchSpec> _launchSpec;
    private readonly Func<SessionTimeoutKind, TimeSpan> _timeoutFor;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private SpotreadProcessSession? _process;
    private long _generation;
    private bool _intentionalStop;
    private bool _recoveryScheduled;
    private long _savedReadingResponseGeneration = -1;
    private CancellationTokenSource? _watchdog;

    public MeasurementSessionController(
        Func<MeasurementMode, ProcessLaunchSpec> launchSpec,
        MeasurementMode initialMode = MeasurementMode.Reflectance,
        Func<SessionTimeoutKind, TimeSpan>? timeoutFor = null)
    {
        _launchSpec = launchSpec;
        _timeoutFor = timeoutFor ?? MeasurementSessionTimeouts.For;
        State = new MeasurementSessionStateMachine(initialMode);
    }

    public MeasurementSessionStateMachine State { get; private set; }
    public MeasurementHistory History { get; } = new();
    public BoundedLogBuffer Log { get; } = new();
    public int InstrumentIndex { get; set; } = 1;
    public bool CalibrationCompleted { get; private set; }
    public bool IsRunning => _process?.IsRunning == true;
    public string? ExecutablePath => _process?.ExecutablePath;

    public event Action? Changed;
    public event Action<SpotreadEvent>? EventReceived;

    public async Task StartAsync(
        MeasurementMode mode,
        CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
            _intentionalStop = false;
            _recoveryScheduled = false;
            CalibrationCompleted = false;
            State = new MeasurementSessionStateMachine(mode);
            State.Start(mode);
            Changed?.Invoke();
            await StartCoreAsync(mode, cancellationToken).ConfigureAwait(false);
            RefreshWatchdog(_generation);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task MeasureAsync(CancellationToken cancellationToken = default)
    {
        State.Apply(new MeasurementStartedEvent());
        Changed?.Invoke();
        RefreshWatchdog(_generation);
        await SendAsync(" ", cancellationToken).ConfigureAwait(false);
    }

    public async Task BeginCalibrationAsync(CancellationToken cancellationToken = default)
    {
        State.Apply(new CalibrationStartedEvent());
        Changed?.Invoke();
        RefreshWatchdog(_generation);
        await SendAsync("k", cancellationToken).ConfigureAwait(false);
    }

    public async Task ConfirmCalibrationAsync(CancellationToken cancellationToken = default)
    {
        State.Apply(new CalibrationStartedEvent());
        Changed?.Invoke();
        RefreshWatchdog(_generation);
        await SendAsync(" ", cancellationToken).ConfigureAwait(false);
    }

    public Task SkipCalibrationAsync(CancellationToken cancellationToken = default) =>
        SendAsync("S", cancellationToken);

    public Task IgnoreSavedReadingAsync(CancellationToken cancellationToken = default) =>
        SendAsync("N", cancellationToken);

    public async Task RetryAsync(CancellationToken cancellationToken = default)
    {
        var command = State.CurrentIssue?.RecoveryAction switch
        {
            SpotreadRecoveryAction.ResumeMeasurementLoop => "\n",
            SpotreadRecoveryAction.RetryCalibration => "\n",
            SpotreadRecoveryAction.RetryOperation => "\n",
            SpotreadRecoveryAction.AcknowledgeConfiguration => "\n",
            SpotreadRecoveryAction.Restart => null,
            _ => null,
        };
        if (command is null)
        {
            await StartAsync(State.Mode, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await SendAsync(command, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _intentionalStop = true;
            CancelWatchdog();
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
            State.Stop();
            Changed?.Invoke();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifecycle.Dispose();
    }

    private async Task StartCoreAsync(
        MeasurementMode mode,
        CancellationToken cancellationToken)
    {
        var generation = ++_generation;
        var process = new SpotreadProcessSession(_launchSpec(mode), mode, InstrumentIndex);
        _process = process;
        process.LogReceived += Log.Append;
        process.EventReceived += @event => OnEvent(generation, @event);
        process.Exited += exit => _ = OnExitAsync(generation, exit);
        try
        {
            await process.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            State.Apply(
                new FatalIssueEvent(
                    new SpotreadIssue
                    {
                        Kind = SpotreadIssueKind.FatalFailure,
                        RecoveryAction = SpotreadRecoveryAction.Restart,
                        Code = "launchFailure",
                        Reason = exception.Message,
                        RawText = string.Empty,
                    }));
            Changed?.Invoke();
            throw;
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        var process = _process;
        _process = null;
        if (process is not null)
        {
            await process.StopAsync(cancellationToken).ConfigureAwait(false);
            await process.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void OnEvent(long generation, SpotreadEvent @event)
    {
        if (generation != _generation)
        {
            return;
        }

        State.Apply(@event);
        if (@event is CalibrationCompletedEvent)
        {
            CalibrationCompleted = true;
        }
        RefreshWatchdog(generation);
        if (@event is MeasurementCompletedEvent measurement)
        {
            History.Add(measurement.Measurement, instrumentIdentity: State.Instrument);
        }
        EventReceived?.Invoke(@event);
        Changed?.Invoke();
        if (@event is SavedReadingPromptEvent &&
            Interlocked.Read(ref _savedReadingResponseGeneration) != generation)
        {
            Interlocked.Exchange(ref _savedReadingResponseGeneration, generation);
            _ = IgnoreSavedReadingAsync();
        }
    }

    private async Task OnExitAsync(long generation, SpotreadProcessExit exit)
    {
        if (generation != _generation || exit.WasRequested || _intentionalStop)
        {
            return;
        }
        if (_recoveryScheduled)
        {
            return;
        }
        _recoveryScheduled = true;

        if (!State.TryBeginAutomaticRecovery())
        {
            State.Apply(
                new FatalIssueEvent(
                    new SpotreadIssue
                    {
                        Kind = SpotreadIssueKind.FatalFailure,
                        RecoveryAction = SpotreadRecoveryAction.Restart,
                        Code = "recoveryExhausted",
                        Reason = $"spotread exited with code {exit.ExitCode}.",
                        RawText = string.Empty,
                    }));
            Changed?.Invoke();
            return;
        }

        Changed?.Invoke();
        RefreshWatchdog(generation);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400)).ConfigureAwait(false);
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                if (generation != _generation || _intentionalStop)
                {
                    return;
                }
                if (_process is not null)
                {
                    await _process.DisposeAsync().ConfigureAwait(false);
                }
                _recoveryScheduled = false;
                await StartCoreAsync(State.Mode, CancellationToken.None).ConfigureAwait(false);
                RefreshWatchdog(_generation);
            }
            finally
            {
                _lifecycle.Release();
            }
        }
        catch (Exception exception)
        {
            State.Apply(
                new FatalIssueEvent(
                    new SpotreadIssue
                    {
                        Kind = SpotreadIssueKind.FatalFailure,
                        RecoveryAction = SpotreadRecoveryAction.Restart,
                        Code = "recoveryFailure",
                        Reason = exception.Message,
                        RawText = string.Empty,
                    }));
            Changed?.Invoke();
        }
    }

    private async Task SendAsync(string command, CancellationToken cancellationToken)
    {
        var process = _process ?? throw new InvalidOperationException("spotread is not running.");
        await process.SendAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private void RefreshWatchdog(long generation)
    {
        CancelWatchdog();
        var kind = State.Phase switch
        {
            MeasurementSessionPhase.Launching => SessionTimeoutKind.Launch,
            MeasurementSessionPhase.Calibrating => SessionTimeoutKind.Calibration,
            MeasurementSessionPhase.Measuring => SessionTimeoutKind.Measurement,
            MeasurementSessionPhase.Recovering => SessionTimeoutKind.Recovery,
            _ => (SessionTimeoutKind?)null,
        };
        if (kind is null)
        {
            return;
        }

        var phase = State.Phase;
        var watchdog = new CancellationTokenSource();
        _watchdog = watchdog;
        _ = WatchdogAsync(
            generation,
            phase,
            kind.Value,
            watchdog.Token);
    }

    private async Task WatchdogAsync(
        long generation,
        MeasurementSessionPhase phase,
        SessionTimeoutKind kind,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_timeoutFor(kind), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (generation != _generation ||
                phase != State.Phase ||
                _intentionalStop ||
                cancellationToken.IsCancellationRequested)
            {
                return;
            }

            State.Timeout(kind);
            Changed?.Invoke();
            _recoveryScheduled = true;
            if (!State.TryBeginAutomaticRecovery())
            {
                await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
                Changed?.Invoke();
                return;
            }

            Changed?.Invoke();
            await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
            _recoveryScheduled = false;
            await StartCoreAsync(State.Mode, CancellationToken.None).ConfigureAwait(false);
            RefreshWatchdog(_generation);
        }
        catch (Exception exception)
        {
            State.Apply(
                new FatalIssueEvent(
                    new SpotreadIssue
                    {
                        Kind = SpotreadIssueKind.FatalFailure,
                        RecoveryAction = SpotreadRecoveryAction.Restart,
                        Code = "watchdogRecoveryFailure",
                        Reason = exception.Message,
                        RawText = string.Empty,
                    }));
            Changed?.Invoke();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private void CancelWatchdog()
    {
        var watchdog = Interlocked.Exchange(ref _watchdog, null);
        if (watchdog is null)
        {
            return;
        }
        watchdog.Cancel();
        watchdog.Dispose();
    }
}
