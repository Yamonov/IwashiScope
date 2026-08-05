using System.Diagnostics;
using IwashiScope.Core.Models;
using IwashiScope.Core.Session;
using IwashiScope.Infrastructure.Windows.Logging;
using IwashiScope.Protocol;

namespace IwashiScope.Infrastructure.Windows.Process;

public sealed record SpotreadProcessExit(
    int ExitCode,
    bool WasRequested,
    Exception? Failure);

public sealed class SpotreadProcessSession : IAsyncDisposable
{
    private readonly ProcessLaunchSpec _launchSpec;
    private readonly MeasurementMode _mode;
    private readonly int _instrumentIndex;
    private readonly SpotreadOutputParser _parser;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _inputGate = new(1, 1);
    private System.Diagnostics.Process? _process;
    private WindowsJobObject? _job;
    private Task? _stdoutTask;
    private Task? _stderrTask;
    private Task? _exitTask;
    private bool _stopRequested;

    public SpotreadProcessSession(
        ProcessLaunchSpec launchSpec,
        MeasurementMode mode,
        int instrumentIndex = 1)
    {
        _launchSpec = launchSpec;
        _mode = mode;
        _instrumentIndex = instrumentIndex;
        _parser = new SpotreadOutputParser(mode);
    }

    public event Action<SpotreadEvent>? EventReceived;
    public event Action<SpotreadLogEntry>? LogReceived;
    public event Action<SpotreadProcessExit>? Exited;

    public bool IsRunning => _process is { HasExited: false };
    public int? ProcessId => _process is { HasExited: false } process ? process.Id : null;
    public string ExecutablePath => _launchSpec.FileName;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_process is not null)
        {
            throw new InvalidOperationException("This process session has already been started.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _launchSpec.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = SpotreadPipeProtocol.InputEncoding,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = _launchSpec.WorkingDirectory ??
                Path.GetDirectoryName(Path.GetFullPath(_launchSpec.FileName)) ??
                Environment.CurrentDirectory,
        };
        foreach (var argument in _mode.SpotreadArguments(_instrumentIndex))
        {
            startInfo.ArgumentList.Add(argument);
        }
        foreach (var pair in _launchSpec.Environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }
        startInfo.Environment["ARGYLL_NOT_INTERACTIVE"] = "1";

        var process = new System.Diagnostics.Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };
        Log(SpotreadLogKind.Lifecycle, $"Launching {DisplayCommand(startInfo)}");
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Process.Start returned false.");
            }
            _process = process;
            try
            {
                _job = WindowsJobObject.CreateKillOnClose();
                _job.Assign(process);
            }
            catch (Exception exception)
            {
                Log(
                    SpotreadLogKind.Lifecycle,
                    $"Job Object assignment failed; process-tree kill remains enabled: {exception.Message}");
                _job?.Dispose();
                _job = null;
            }

            Log(SpotreadLogKind.Lifecycle, $"Started PID {process.Id}.");
            _stdoutTask = ReadStdoutAsync(process, _lifetime.Token);
            _stderrTask = ReadStderrAsync(process, _lifetime.Token);
            _exitTask = ObserveExitAsync(process);
            return Task.CompletedTask;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public async Task SendAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var process = _process;
        if (process is null || process.HasExited)
        {
            throw new InvalidOperationException("spotread is not running.");
        }

        var display = command == " " ? "<SPACE>" : command;
        // Argyll's Windows non-interactive console reader consumes commands
        // from a pipe as lines. A lone command byte remains buffered while it
        // waits for CR/LF, so terminate every protocol command explicitly.
        var transportCommand = SpotreadPipeProtocol.FrameCommand(command);
        Log(SpotreadLogKind.InputPending, display);
        await _inputGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await process.StandardInput.WriteAsync(
                    transportCommand.AsMemory(),
                    cancellationToken)
                .ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            Log(SpotreadLogKind.InputSent, display);
        }
        catch (Exception exception)
        {
            Log(SpotreadLogKind.InputFailed, $"{display}: {exception.Message}");
            throw;
        }
        finally
        {
            _inputGate.Release();
        }
    }

    public async Task SendBytesAsync(
        ReadOnlyMemory<byte> data,
        string description,
        CancellationToken cancellationToken = default)
    {
        if (data.IsEmpty)
        {
            throw new ArgumentException("Binary input cannot be empty.", nameof(data));
        }
        var process = _process;
        if (process is null || process.HasExited)
        {
            throw new InvalidOperationException("spotread is not running.");
        }

        Log(SpotreadLogKind.InputPending, description);
        await _inputGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Flush the StreamWriter before writing the length-prefixed binary
            // frame directly to the same standard-input pipe.
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            await process.StandardInput.BaseStream
                .WriteAsync(data, cancellationToken)
                .ConfigureAwait(false);
            await process.StandardInput.BaseStream
                .FlushAsync(cancellationToken)
                .ConfigureAwait(false);
            Log(SpotreadLogKind.InputSent, description);
        }
        catch (Exception exception)
        {
            Log(SpotreadLogKind.InputFailed, $"{description}: {exception.Message}");
            throw;
        }
        finally
        {
            _inputGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _stopRequested = true;
        var process = _process;
        if (process is null)
        {
            return;
        }

        if (!process.HasExited)
        {
            try
            {
                await SendAsync("q", cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // A closed input pipe is expected during abnormal termination.
            }

            try
            {
                using var grace = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                grace.CancelAfter(TimeSpan.FromMilliseconds(250));
                await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (!process.HasExited)
                {
                    Log(SpotreadLogKind.Lifecycle, "Graceful stop timed out; killing process tree.");
                    process.Kill(entireProcessTree: true);
                }
            }
        }

        if (_exitTask is not null)
        {
            await _exitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch
        {
            // Dispose must still release the Job Object.
        }
        _lifetime.Cancel();
        _job?.Dispose();
        _process?.Dispose();
        _inputGate.Dispose();
        _lifetime.Dispose();
    }

    private async Task ReadStdoutAsync(
        System.Diagnostics.Process process,
        CancellationToken cancellationToken)
    {
        var stream = process.StandardOutput.BaseStream;
        var readBuffer = new byte[4096];
        using var pending = new MemoryStream();
        Task<int>? pendingRead = null;
        var batchStarted = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                pendingRead ??= stream
                    .ReadAsync(readBuffer, cancellationToken)
                    .AsTask();
                if (pending.Length == 0)
                {
                    var count = await pendingRead.ConfigureAwait(false);
                    pendingRead = null;
                    if (count == 0)
                    {
                        break;
                    }
                    pending.Write(readBuffer, 0, count);
                    batchStarted.Restart();
                    continue;
                }

                var remaining = TimeSpan.FromMilliseconds(100) - batchStarted.Elapsed;
                if (remaining > TimeSpan.Zero &&
                    await Task.WhenAny(pendingRead, Task.Delay(remaining, cancellationToken))
                        .ConfigureAwait(false) == pendingRead)
                {
                    var count = await pendingRead.ConfigureAwait(false);
                    pendingRead = null;
                    if (count == 0)
                    {
                        EmitProtocolBatch(pending.ToArray());
                        pending.SetLength(0);
                        break;
                    }
                    pending.Write(readBuffer, 0, count);
                    continue;
                }

                EmitProtocolBatch(pending.ToArray());
                pending.SetLength(0);
            }

            if (pending.Length > 0)
            {
                EmitProtocolBatch(pending.ToArray());
            }
            foreach (var @event in _parser.Finish())
            {
                Log(SpotreadLogKind.Protocol, @event.GetType().Name);
                EventReceived?.Invoke(@event);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            EventReceived?.Invoke(
                new FatalIssueEvent(
                    new SpotreadIssue
                    {
                        Kind = SpotreadIssueKind.OutputParsingFailure,
                        RecoveryAction = SpotreadRecoveryAction.Restart,
                        Code = "stdoutReadFailure",
                        Reason = exception.Message,
                        RawText = string.Empty,
                    }));
        }
    }

    private void EmitProtocolBatch(byte[] data)
    {
        foreach (var @event in _parser.Consume(data))
        {
            Log(SpotreadLogKind.Protocol, @event.GetType().Name);
            EventReceived?.Invoke(@event);
        }
    }

    private async Task ReadStderrAsync(
        System.Diagnostics.Process process,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                   is { } line)
            {
                Log(SpotreadLogKind.StandardError, line);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ObserveExitAsync(System.Diagnostics.Process process)
    {
        Exception? failure = null;
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (_stdoutTask is not null)
            {
                await _stdoutTask.ConfigureAwait(false);
            }
            if (_stderrTask is not null)
            {
                await _stderrTask.ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            var exitCode = process.HasExited ? process.ExitCode : -1;
            Log(SpotreadLogKind.Lifecycle, $"PID {process.Id} exited with code {exitCode}.");
            Exited?.Invoke(new SpotreadProcessExit(exitCode, _stopRequested, failure));
        }
    }

    private void Log(SpotreadLogKind kind, string message) =>
        LogReceived?.Invoke(new SpotreadLogEntry(DateTimeOffset.UtcNow, kind, message));

    private static string DisplayCommand(ProcessStartInfo info)
    {
        var arguments = info.ArgumentList.Select(argument =>
            argument.Contains(' ') ? $"\"{argument}\"" : argument);
        return $"{info.FileName} {string.Join(" ", arguments)}";
    }
}
