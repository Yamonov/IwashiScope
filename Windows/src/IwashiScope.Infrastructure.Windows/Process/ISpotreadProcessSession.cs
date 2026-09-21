using IwashiScope.Core.Session;
using IwashiScope.Infrastructure.Windows.Logging;

namespace IwashiScope.Infrastructure.Windows.Process;

internal interface ISpotreadProcessSession : IAsyncDisposable
{
    event Action<SpotreadEvent>? EventReceived;
    event Action<SpotreadLogEntry>? LogReceived;
    event Action<SpotreadProcessExit>? Exited;
    bool IsRunning { get; }
    string ExecutablePath { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task SendAsync(string command, CancellationToken cancellationToken = default);
    Task SendBytesAsync(ReadOnlyMemory<byte> data, string description,
        CancellationToken cancellationToken = default);
}
