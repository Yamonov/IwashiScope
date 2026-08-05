using IwashiScope.Core.History;
using IwashiScope.Core.Models;
using IwashiScope.Core.Session;
using IwashiScope.Infrastructure.Windows.Logging;
using IwashiScope.Infrastructure.Windows.Process;

namespace IwashiScope.Infrastructure.Windows.Session;

public enum AveragingOperationPhase
{
    Inactive,
    Collecting,
    WaitingForSpectrumInput,
    AnalyzingSpectrum,
}

public sealed record AveragingStatusMessage(string Japanese, string English);

public sealed class MeasurementSessionController : IAsyncDisposable
{
    private readonly Func<MeasurementMode, ProcessLaunchSpec> _launchSpec;
    private readonly Func<SessionTimeoutKind, TimeSpan> _timeoutFor;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private SpotreadProcessSession? _process;
    private long _generation;
    private bool _intentionalStop;
    private bool _recoveryScheduled;
    private bool _awaitingRecoveryConfirmation;
    private long _savedReadingResponseGeneration = -1;
    private CancellationTokenSource? _watchdog;
    private SpectrumAnalysisRequest? _pendingSpectrumAnalysisRequest;
    private bool _shouldAutomaticallyOutputAverage;

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
    public bool SupportsSpectrumAnalysis { get; private set; }
    public SpotMeasurement? LatestMeasurement { get; private set; }
    public AveragingMeasurementAccumulator AveragingAccumulator { get; private set; } = new();
    public AveragingOperationPhase AveragingPhase { get; private set; } =
        AveragingOperationPhase.Inactive;
    public AveragingStatusMessage? AveragingMessage { get; private set; }
    public bool IsAveragingMeasurement => AveragingPhase != AveragingOperationPhase.Inactive;
    public bool IsCollectingAveragingMeasurements =>
        AveragingPhase == AveragingOperationPhase.Collecting;
    public bool IsFinalizingAveragingMeasurement =>
        AveragingPhase is AveragingOperationPhase.WaitingForSpectrumInput or
            AveragingOperationPhase.AnalyzingSpectrum;
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
            var preservesAveraging = State.Mode == mode && IsAveragingMeasurement;
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
            _intentionalStop = false;
            _recoveryScheduled = false;
            _awaitingRecoveryConfirmation = false;
            CalibrationCompleted = false;
            SupportsSpectrumAnalysis = false;
            _pendingSpectrumAnalysisRequest = null;
            _shouldAutomaticallyOutputAverage = false;
            if (preservesAveraging)
            {
                AveragingPhase = AveragingOperationPhase.Collecting;
                AveragingMessage = Message(
                    "spotreadの再起動後も採用済みの測定を保持しています。キャリブレーション完了後に続けられます。",
                    "Accepted readings were retained after restarting spotread. Continue after calibration.");
            }
            else
            {
                ResetAveragingMeasurement(null);
                LatestMeasurement = null;
            }
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

    public void StartAveragingMeasurement()
    {
        if (State.Phase != MeasurementSessionPhase.Ready ||
            !SupportsSpectrumAnalysis ||
            AveragingPhase != AveragingOperationPhase.Inactive)
        {
            return;
        }

        History.DeselectAll(State.Mode);
        AveragingAccumulator = new AveragingMeasurementAccumulator();
        AveragingPhase = AveragingOperationPhase.Collecting;
        AveragingMessage = Message(
            "通常どおり測定してください。最初の5回は暫定値とし、6回目で初回を含めて異常値を判定します。",
            "Measure normally. The first five readings are provisional; the sixth rechecks all readings, including the first, for outliers.");
        LatestMeasurement = null;
        _pendingSpectrumAnalysisRequest = null;
        _shouldAutomaticallyOutputAverage = false;
        Changed?.Invoke();
    }

    public async Task FinishOrCancelAveragingMeasurementAsync(
        CancellationToken cancellationToken = default)
    {
        if (AveragingPhase != AveragingOperationPhase.Collecting)
        {
            return;
        }
        if (!AveragingAccumulator.CanOutputAverage)
        {
            ResetAveragingMeasurement(
                Message(
                    "平均化測定を終了しました。途中の測定は履歴へ保存していません。",
                    "Averaging ended. The incomplete readings were not saved to history."));
            Changed?.Invoke();
            return;
        }
        if (State.Phase != MeasurementSessionPhase.Ready)
        {
            return;
        }
        await BeginSpectrumAnalysisAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task BeginCalibrationAsync(CancellationToken cancellationToken = default)
    {
        CalibrationCompleted = false;
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
        if (@event is HelloAcceptedEvent hello)
        {
            SupportsSpectrumAnalysis =
                hello.Capabilities?.Contains("spectrumAnalysisV1") == true;
            if (_awaitingRecoveryConfirmation)
            {
                _awaitingRecoveryConfirmation = false;
                Log.Append(
                    new SpotreadLogEntry(
                        DateTimeOffset.UtcNow,
                        SpotreadLogKind.Lifecycle,
                        "Automatic recovery completed; spotread handshake succeeded."));
            }
        }
        if (@event is CalibrationStartedEvent)
        {
            CalibrationCompleted = false;
        }
        else if (@event is CalibrationCompletedEvent)
        {
            CalibrationCompleted = true;
        }

        switch (@event)
        {
            case SpectrumAnalysisInputReadyEvent:
                _ = SendPendingSpectrumAnalysisRequestAsync(generation);
                break;

            case SpectrumAnalysisStartedEvent
                when _pendingSpectrumAnalysisRequest is not null &&
                     AveragingPhase == AveragingOperationPhase.WaitingForSpectrumInput:
                AveragingPhase = AveragingOperationPhase.AnalyzingSpectrum;
                AveragingMessage = Message(
                    "平均スペクトルから測色値と演色評価値を再計算しています。",
                    "Recalculating colorimetric and color-rendering values from the averaged spectrum.");
                break;

            case SpectrumAnalysisFailedEvent failed:
                _pendingSpectrumAnalysisRequest = null;
                _shouldAutomaticallyOutputAverage = false;
                if (IsAveragingMeasurement)
                {
                    AveragingPhase = AveragingOperationPhase.Collecting;
                    AveragingMessage = Message(
                        $"平均値を再計算できませんでした。採用済みの測定は保持しています。spotread: {failed.Reason}",
                        $"The average could not be recalculated. Accepted readings were retained. spotread: {failed.Reason}");
                }
                break;

            case MeasurementCompletedEvent measurement:
                HandleMeasurement(measurement.Measurement);
                break;

            case MeasurementPromptEvent
                when _shouldAutomaticallyOutputAverage &&
                     AveragingPhase == AveragingOperationPhase.Collecting &&
                     State.Phase == MeasurementSessionPhase.Ready:
                _shouldAutomaticallyOutputAverage = false;
                _ = BeginSpectrumAnalysisAsync(CancellationToken.None);
                break;
        }

        RefreshWatchdog(generation);
        EventReceived?.Invoke(@event);
        Changed?.Invoke();
        if (@event is SavedReadingPromptEvent &&
            Interlocked.Read(ref _savedReadingResponseGeneration) != generation)
        {
            Interlocked.Exchange(ref _savedReadingResponseGeneration, generation);
            _ = IgnoreSavedReadingAsync();
        }
    }

    private void HandleMeasurement(SpotMeasurement measurement)
    {
        LatestMeasurement = measurement;

        if (measurement.AveragedMeasurement is { } averagedMetadata)
        {
            if (_pendingSpectrumAnalysisRequest is not { } request ||
                averagedMetadata.RequestId != request.RequestId ||
                averagedMetadata.SampleCount != request.SampleCount)
            {
                State.Apply(
                    new ConfigurationIssueEvent(
                        new SpotreadIssue
                        {
                            Kind = SpotreadIssueKind.OutputParsingFailure,
                            RecoveryAction = SpotreadRecoveryAction.AcknowledgeConfiguration,
                            Code = "averagedSpectrumResponseMismatch",
                            Reason = "The averaged-spectrum response did not match its request.",
                            RawText = string.Empty,
                        }));
                return;
            }

            var convergence = AveragingAccumulator.Convergence;
            var finalizedMetadata = averagedMetadata with
            {
                MeasurementCount = AveragingAccumulator.MeasurementAttemptCount,
                OutlierCount = AveragingAccumulator.OutlierCount,
                Relative95UncertaintyPercent = convergence?.Relative95UncertaintyPercent,
                ConvergenceTier = convergence?.Tier,
            };
            var finalizedMeasurement = measurement with
            {
                AveragedMeasurement = finalizedMetadata,
            };
            LatestMeasurement = finalizedMeasurement;
            History.Add(finalizedMeasurement, instrumentIdentity: State.Instrument);
            _pendingSpectrumAnalysisRequest = null;
            _shouldAutomaticallyOutputAverage = false;
            ResetAveragingMeasurement(
                Message(
                    $"{averagedMetadata.SampleCount}回の平均スペクトルを履歴へ追加しました。",
                    $"Added the {averagedMetadata.SampleCount}-reading averaged spectrum to history."));
            return;
        }

        if (AveragingPhase != AveragingOperationPhase.Collecting)
        {
            History.Add(measurement, instrumentIdentity: State.Instrument);
            return;
        }

        var decision = AveragingAccumulator.Add(measurement);
        switch (decision.Kind)
        {
            case AveragingSampleDecisionKind.Accepted:
                if (AveragingAccumulator.LastRetrospectivelyRejectedCount > 0)
                {
                    AveragingMessage = Message(
                        $"異常値です。過去の測定{AveragingAccumulator.LastRetrospectivelyRejectedCount}件を平均対象から除外しました。現在{AveragingAccumulator.AcceptedCount}回を採用しています。続けて測定できます。",
                        $"Outlier detected. Removed {AveragingAccumulator.LastRetrospectivelyRejectedCount} earlier reading(s). {AveragingAccumulator.AcceptedCount} readings are accepted; you can continue measuring.");
                }
                else
                {
                    AveragingMessage = AveragingProgressMessage();
                }
                if (AveragingAccumulator.HasReachedMaximum)
                {
                    _shouldAutomaticallyOutputAverage = true;
                    AveragingMessage = Message(
                        "20回に達しました。平均スペクトルを自動で再計算します。",
                        "Twenty accepted readings reached. The averaged spectrum will now be recalculated automatically.");
                }
                break;

            case AveragingSampleDecisionKind.Outlier:
                if (AveragingAccumulator.LastRetrospectivelyRejectedCount > 0)
                {
                    AveragingMessage = Message(
                        $"異常値です。今回の測定と過去の測定{AveragingAccumulator.LastRetrospectivelyRejectedCount}件を平均対象から除外しました。現在{AveragingAccumulator.AcceptedCount}回を採用しています。続けて測定できます。",
                        $"Outlier detected. Excluded this reading and {AveragingAccumulator.LastRetrospectivelyRejectedCount} earlier reading(s). {AveragingAccumulator.AcceptedCount} readings are accepted; you can continue measuring.");
                }
                else
                {
                    AveragingMessage = Message(
                        "異常値です。平均回数には含めませんでした。続けて測定できます。",
                        "This reading is an outlier and was not counted. You can continue measuring.");
                }
                break;

            case AveragingSampleDecisionKind.Incompatible:
                AveragingMessage = Message(
                    "有効なスペクトルを取得できなかったため、平均回数には含めませんでした。続けて測定できます。",
                    decision.Reason ??
                    "The reading was not compatible with the accepted spectra and was not counted.");
                break;
        }
    }

    private AveragingStatusMessage AveragingProgressMessage()
    {
        var acceptedCount = AveragingAccumulator.AcceptedCount;
        if (!AveragingAccumulator.HasStartedOutlierDetection)
        {
            return Message(
                $"{acceptedCount}回を暫定採用しました。6回目で初回を含めて異常値を再判定します。",
                $"Provisionally accepted {acceptedCount} reading(s). The sixth reading rechecks all readings, including the first, for outliers.");
        }

        return acceptedCount switch
        {
            < AveragingMeasurementAccumulator.MinimumOutputCount => Message(
                $"現在{acceptedCount}回を採用しています。平均値の出力には6回以上必要です。",
                $"{acceptedCount} readings are accepted. At least six are required to output an average."),
            < AveragingMeasurementAccumulator.RecommendedCount => Message(
                $"{acceptedCount}回を採用しました。平均値を出力できます。10回以上を推奨します。",
                $"{acceptedCount} readings are accepted. You can output an average; 10 or more are recommended."),
            < AveragingMeasurementAccumulator.SufficientCount => Message(
                $"{acceptedCount}回を採用しました。安定した平均値です。15回以上で十分な回数になります。",
                $"{acceptedCount} readings are accepted and the average is stable. Fifteen or more are sufficient."),
            _ => Message(
                $"{acceptedCount}回を採用しました。十分な平均回数です。20回で自動出力します。",
                $"{acceptedCount} readings are accepted, which is sufficient. Output starts automatically at 20."),
        };
    }

    private async Task BeginSpectrumAnalysisAsync(CancellationToken cancellationToken)
    {
        if (State.Phase != MeasurementSessionPhase.Ready ||
            AveragingPhase != AveragingOperationPhase.Collecting ||
            !AveragingAccumulator.CanOutputAverage ||
            !SupportsSpectrumAnalysis)
        {
            return;
        }

        try
        {
            var request = AveragingAccumulator.MakeAnalysisRequest(Guid.NewGuid().ToString());
            _pendingSpectrumAnalysisRequest = request;
            AveragingPhase = AveragingOperationPhase.WaitingForSpectrumInput;
            AveragingMessage = Message(
                "平均スペクトルをspotreadへ送る準備をしています。",
                "Preparing to send the averaged spectrum to spotread.");
            State.Apply(new MeasurementStartedEvent());
            Changed?.Invoke();
            RefreshWatchdog(_generation);

            // Argyll's Windows pipe reader consumes control commands as lines.
            // Send 0x1D with CR/LF, then wait for InputReady before sending the
            // separate length-prefixed binary spectrum frame.
            await SendAsync("\u001D", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _pendingSpectrumAnalysisRequest = null;
            _shouldAutomaticallyOutputAverage = false;
            AveragingPhase = AveragingOperationPhase.Collecting;
            AveragingMessage = Message(
                $"平均スペクトルの解析を開始できませんでした。{exception.Message}",
                $"The averaged-spectrum analysis could not start. {exception.Message}");
            State.Apply(
                new FatalIssueEvent(
                    new SpotreadIssue
                    {
                        Kind = SpotreadIssueKind.OperationFailure,
                        RecoveryAction = SpotreadRecoveryAction.Restart,
                        Code = "spectrumAnalysisSendFailure",
                        Reason = exception.Message,
                        RawText = string.Empty,
                    }));
            Changed?.Invoke();
        }
    }

    private async Task SendPendingSpectrumAnalysisRequestAsync(long generation)
    {
        if (generation != _generation ||
            _pendingSpectrumAnalysisRequest is not { } request ||
            AveragingPhase != AveragingOperationPhase.WaitingForSpectrumInput)
        {
            return;
        }

        try
        {
            var frame = request.FramedData();
            var process = _process ??
                throw new InvalidOperationException("spotread is not running.");
            await process.SendBytesAsync(
                    frame,
                    $"averaged spectrum: {request.SampleCount} readings ({frame.Length} bytes)")
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _pendingSpectrumAnalysisRequest = null;
            AveragingPhase = AveragingOperationPhase.Collecting;
            AveragingMessage = Message(
                $"平均スペクトルをspotreadへ送信できませんでした。{exception.Message}",
                $"The averaged spectrum could not be sent to spotread. {exception.Message}");
            State.Apply(
                new FatalIssueEvent(
                    new SpotreadIssue
                    {
                        Kind = SpotreadIssueKind.OperationFailure,
                        RecoveryAction = SpotreadRecoveryAction.Restart,
                        Code = "spectrumAnalysisFrameFailure",
                        Reason = exception.Message,
                        RawText = string.Empty,
                    }));
            Changed?.Invoke();
        }
    }

    private void ResetAveragingMeasurement(AveragingStatusMessage? message)
    {
        AveragingAccumulator = new AveragingMeasurementAccumulator();
        AveragingPhase = AveragingOperationPhase.Inactive;
        AveragingMessage = message;
        _pendingSpectrumAnalysisRequest = null;
        _shouldAutomaticallyOutputAverage = false;
    }

    private static AveragingStatusMessage Message(string japanese, string english) =>
        new(japanese, english);

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

        CalibrationCompleted = false;
        Log.Append(
            new SpotreadLogEntry(
                DateTimeOffset.UtcNow,
                SpotreadLogKind.Lifecycle,
                "Automatic recovery started after spotread exited unexpectedly."));
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
                _awaitingRecoveryConfirmation = true;
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

            CalibrationCompleted = false;
            Log.Append(
                new SpotreadLogEntry(
                    DateTimeOffset.UtcNow,
                    SpotreadLogKind.Lifecycle,
                    $"Automatic recovery started after {kind} timeout."));
            Changed?.Invoke();
            await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
            _recoveryScheduled = false;
            _awaitingRecoveryConfirmation = true;
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
