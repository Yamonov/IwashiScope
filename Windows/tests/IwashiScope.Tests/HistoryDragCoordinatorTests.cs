using IwashiScope.App.Wpf.Export;
using IwashiScope.Core.History;
using IwashiScope.Core.Models;

namespace IwashiScope.Tests;

public sealed class HistoryDragCoordinatorTests
{
    [Fact]
    public async Task RapidMovesAndNestedOlePumpCannotStartAnotherDrag()
    {
        var coordinator = new HistoryDragCoordinator();
        var prepared = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        int preparations = 0, drags = 0;
        Task<bool>? nested = null;
        coordinator.Arm();
        var first = coordinator.TryRunAsync(_ => { preparations++; return prepared.Task; }, () => true, _ =>
        {
            Assert.True(coordinator.IsBusy);
            Assert.True(coordinator.IsDragging);
            drags++;
            // DoDragDrop pumps dispatcher messages synchronously before it returns.
            nested = coordinator.TryRunAsync(_ => Task.FromResult(2), () => true, _ => drags++);
            Assert.True(nested.IsCompletedSuccessfully);
        });
        for (int i = 0; i < 100; i++)
            Assert.False(await coordinator.TryRunAsync(_ => { preparations++; return Task.FromResult(2); }, () => true, _ => drags++));
        var idle = coordinator.WhenIdle;
        Assert.False(idle.IsCompleted);
        prepared.SetResult(1);
        Assert.True(await first);
        Assert.False(await nested!);
        await idle;
        Assert.Equal(1, preparations);
        Assert.Equal(1, drags);
        Assert.False(coordinator.IsBusy);
        Assert.False(await coordinator.TryRunAsync(_ => Task.FromResult(3), () => true, _ => drags++));
        coordinator.Arm();
        Assert.True(await coordinator.TryRunAsync(_ => Task.FromResult(4), () => true, _ => drags++));
        Assert.Equal(2, drags);
    }

    [Fact]
    public async Task ReleaseOrEscapeDuringPreparationPreventsDelayedOleStart()
    {
        var coordinator = new HistoryDragCoordinator();
        var prepared = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        coordinator.Arm();
        var pending = coordinator.TryRunAsync(ct => { token = ct; return prepared.Task; }, () => true,
            _ => throw new Exception("Canceled gesture must not enter OLE."));
        coordinator.Cancel();
        Assert.True(token.IsCancellationRequested);
        prepared.SetResult(1); // Even a writer which completes despite cancellation cannot start a drag.
        Assert.False(await pending);
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task NewPressWhileOldPreparationUnwindsDoesNotReuseTheOldGesture()
    {
        var coordinator = new HistoryDragCoordinator();
        var prepared = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.Arm();
        var pending = coordinator.TryRunAsync(_ => prepared.Task, () => true, _ => Assert.Fail("Stale gesture."));
        coordinator.Arm();
        Assert.True(coordinator.IsCancellationRequested);
        prepared.SetResult(1);
        Assert.False(await pending);
        Assert.False(await coordinator.TryRunAsync(_ => Task.FromResult(1), () => true, _ => Assert.Fail("Needs a fresh press.")));
        coordinator.Arm();
        Assert.True(await coordinator.TryRunAsync(_ => Task.FromResult(1), () => true, _ => { }));
    }

    [Fact]
    public async Task FocusModeSelectionOrButtonChangesAreRecheckedAfterAwait()
    {
        var coordinator = new HistoryDragCoordinator();
        var prepared = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool eligible = true;
        coordinator.Arm();
        var pending = coordinator.TryRunAsync(_ => prepared.Task, () => eligible, _ => Assert.Fail("Ineligible operation."));
        eligible = false;
        prepared.SetResult(1);
        Assert.False(await pending);
    }

    [Fact]
    public async Task CancellationDuringOleKeepsTheGateClosedUntilOleReturns()
    {
        var coordinator = new HistoryDragCoordinator();
        Task<bool>? nested = null;
        coordinator.Arm();
        await coordinator.TryRunAsync(_ => Task.FromResult(1), () => true, _ =>
        {
            coordinator.Cancel();
            Assert.True(coordinator.IsCancellationRequested);
            Assert.True(coordinator.IsBusy);
            Assert.False(coordinator.WhenIdle.IsCompleted);
            nested = coordinator.TryRunAsync(_ => Task.FromResult(2), () => true, _ => Assert.Fail("Nested OLE."));
            Assert.True(nested.IsCompletedSuccessfully);
        });
        Assert.False(await nested!);
        Assert.True(coordinator.WhenIdle.IsCompleted);
    }

    [Fact]
    public async Task PreparationFailureReleasesGateAndIdleWaiters()
    {
        var coordinator = new HistoryDragCoordinator();
        var prepared = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.Arm();
        var pending = coordinator.TryRunAsync(_ => prepared.Task, () => true, _ => Assert.Fail("Failed export."));
        var idle = coordinator.WhenIdle;
        prepared.SetException(new IOException("Test failure"));
        await Assert.ThrowsAsync<IOException>(() => pending);
        await idle;
        Assert.False(coordinator.IsBusy);
        coordinator.Arm();
        Assert.True(await coordinator.TryRunAsync(_ => Task.FromResult(1), () => true, _ => { }));
    }

    [Fact]
    public async Task CancellationExceptionIsHandledAndNativeFailureReleasesGate()
    {
        var coordinator = new HistoryDragCoordinator();
        coordinator.Arm();
        Assert.False(await coordinator.TryRunAsync<int>(ct =>
        {
            coordinator.Cancel();
            return Task.FromCanceled<int>(ct);
        }, () => true, _ => Assert.Fail("Canceled export.")));
        coordinator.Arm();
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.TryRunAsync(_ => Task.FromResult(1), () => true,
            _ => throw new InvalidOperationException("Native drag failure")));
        Assert.False(coordinator.IsBusy);
        Assert.False(coordinator.IsDragging);
    }

    [Theory]
    [InlineData(MeasurementMode.Reflectance)]
    [InlineData(MeasurementMode.Ambient)]
    [InlineData(MeasurementMode.Emissive)]
    public void DeferredReorderUsesDraggedIdsNotTheLiveSelection(MeasurementMode mode)
    {
        var history = new MeasurementHistory();
        var a = history.Add(TestMeasurementFactory.Create(mode), "A");
        var b = history.Add(TestMeasurementFactory.Create(mode), "B");
        var c = history.Add(TestMeasurementFactory.Create(mode), "C");
        var captured = new HashSet<Guid> { a.Id };
        history.SelectExclusive(b.Id);
        history.MoveEntriesBefore(mode, captured, c.Id);
        Assert.Equal(new[] { a.Id, c.Id, b.Id }, history.Ordered(mode).Select(entry => entry.Id));
        Assert.Equal(b.Id, history.ActiveIdFor(mode));
        history.MoveEntriesBefore(mode, captured, a.Id); // Self-drop is a no-op.
        Assert.Equal(a.Id, history.Ordered(mode)[0].Id);
        history.MoveEntriesBefore(mode, captured, Guid.NewGuid()); // Target disappeared.
        Assert.Equal(a.Id, history.Ordered(mode)[0].Id);
        history.MoveEntriesBefore(mode, captured, null);
        Assert.Equal(new[] { c.Id, b.Id, a.Id }, history.Ordered(mode).Select(entry => entry.Id));
    }
}
