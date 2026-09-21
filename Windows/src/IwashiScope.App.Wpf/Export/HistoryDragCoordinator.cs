namespace IwashiScope.App.Wpf.Export;

/// <summary>
/// UI-thread-only lifecycle: one mouse-down may start one preparation/OLE operation.
/// Keep the gate closed across await AND the nested message loop in DoDragDrop.
/// </summary>
internal sealed class HistoryDragCoordinator
{
    private bool _armed;
    private CancellationTokenSource? _cancellation;
    private TaskCompletionSource? _idle;

    public bool IsBusy => _cancellation is not null;
    public bool IsDragging { get; private set; }
    public bool IsCancellationRequested => _cancellation?.IsCancellationRequested == true;
    public Task WhenIdle => _idle?.Task ?? Task.CompletedTask;

    public void Arm()
    {
        Cancel();
        // A new press cannot overlap an operation that is still unwinding.
        _armed = !IsBusy;
    }

    public void Cancel()
    {
        _armed = false;
        _cancellation?.Cancel();
    }

    public async Task<bool> TryRunAsync<T>(
        Func<CancellationToken, Task<T>> prepare,
        Func<bool> canStart,
        Action<T> drag)
    {
        if (!_armed || IsBusy) return false;
        _armed = false;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _idle = idle;
        try
        {
            var data = await prepare(cancellation.Token);
            if (cancellation.IsCancellationRequested || !canStart()) return false;
            IsDragging = true;
            drag(data);
            return true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            IsDragging = false;
            _cancellation = null;
            _idle = null;
            idle.TrySetResult();
        }
    }
}
