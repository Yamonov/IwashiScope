using IwashiScope.App.Wpf.Layout;
using IwashiScope.Core.Models;
using IwashiScope.Core.Session;
using IwashiScope.Core.Workspace;
using IwashiScope.Infrastructure.Windows.Logging;
using IwashiScope.Infrastructure.Windows.Process;
using IwashiScope.Infrastructure.Windows.Session;

namespace IwashiScope.Tests;

public sealed class ConnectionCancellationTests
{
    [Theory]
    [InlineData(MeasurementMode.Reflectance, false)]
    [InlineData(MeasurementMode.Reflectance, true)]
    [InlineData(MeasurementMode.Ambient, false)]
    [InlineData(MeasurementMode.Ambient, true)]
    [InlineData(MeasurementMode.Emissive, false)]
    [InlineData(MeasurementMode.Emissive, true)]
    public async Task ControllerStopsStartupWithoutRecoveryAndCanRestart(
        MeasurementMode mode, bool sendHandshake)
    {
        var processes = new List<ControlledProcess>();
        await using var controller = new MeasurementSessionController(
            _ => new ProcessLaunchSpec { FileName = "unused-test-helper.exe" },
            mode, _ => TimeSpan.FromHours(1),
            (_, _, _) =>
            {
                var process = new ControlledProcess();
                processes.Add(process);
                return process;
            });
        var entry = controller.History.Add(TestMeasurementFactory.Create(mode));
        await controller.StartAsync(mode);
        var original = Assert.Single(processes);
        if (sendHandshake)
        {
            original.Emit(new HelloAcceptedEvent(3, "IwashiScope spot reader", 1, "3.5.0"));
        }
        Assert.True(controller.CanCancelConnection);
        await controller.CancelConnectionAsync();
        await controller.CancelConnectionAsync();
        Assert.Equal(1, original.StopCount);
        Assert.True(original.Disposed);
        Assert.False(controller.IsRunning);
        Assert.Equal(MeasurementSessionPhase.ConnectionCancelled, controller.State.Phase);
        Assert.Null(controller.State.CurrentIssue);
        Assert.Equal(0, controller.State.RecoveryAttempts);
        Assert.Equal(entry.Id, Assert.Single(controller.History.AcquisitionOrder).Id);

        await controller.StartAsync(mode);
        Assert.Equal(2, processes.Count);
        processes[1].Emit(new MeasurementPromptEvent());
        original.Emit(new MeasurementCompletedEvent(TestMeasurementFactory.Create(mode)));
        original.SignalUnexpectedExit();
        Assert.True(controller.IsRunning);
        Assert.Equal(MeasurementSessionPhase.Ready, controller.State.Phase);
        Assert.Null(controller.State.CurrentIssue);
        Assert.Equal(entry.Id, Assert.Single(controller.History.AcquisitionOrder).Id);
        await controller.CancelConnectionAsync();
        Assert.Equal(0, processes[1].StopCount);
        Assert.Equal(MeasurementSessionPhase.Ready, controller.State.Phase);
    }

    [Theory]
    [InlineData(MeasurementMode.Reflectance)]
    [InlineData(MeasurementMode.Ambient)]
    [InlineData(MeasurementMode.Emissive)]
    public void CancellationIsNeutralAndRejectsDrainedStartupEvents(MeasurementMode mode)
    {
        var state = new MeasurementSessionStateMachine(mode);
        state.Start(mode);
        state.Apply(new HelloAcceptedEvent(3, "IwashiScope spot reader", 1, "3.5.0"));
        Assert.Equal(MeasurementSessionPhase.Launching, state.Phase);
        Assert.True(state.CanCancelConnection);
        Assert.True(state.CancelConnection());
        Assert.False(state.CancelConnection());

        state.Apply(new MeasurementPromptEvent());
        state.Apply(new FatalIssueEvent(new SpotreadIssue
        {
            Kind = SpotreadIssueKind.FatalFailure,
            RecoveryAction = SpotreadRecoveryAction.Restart,
            Code = "lateExit",
            Reason = "Drained output after cancellation",
            RawText = string.Empty,
        }));
        state.Timeout(SessionTimeoutKind.Launch);
        Assert.False(state.TryBeginAutomaticRecovery());
        Assert.Equal(MeasurementSessionPhase.ConnectionCancelled, state.Phase);
        Assert.False(state.CanCancelConnection);
        Assert.Null(state.CurrentIssue);
        Assert.Null(state.CurrentCalibrationPrompt);

        state.Start(mode);
        Assert.True(state.CanCancelConnection);
        state.Apply(new MeasurementPromptEvent());
        Assert.Equal(MeasurementSessionPhase.Ready, state.Phase);
        Assert.Null(state.CurrentIssue);
    }

    [Fact]
    public void CancellationCannotInterruptCalibrationOrMeasurement()
    {
        var state = new MeasurementSessionStateMachine(MeasurementMode.Reflectance);
        Assert.False(state.CancelConnection());
        state.Start(MeasurementMode.Reflectance);
        state.Apply(new CalibrationStartedEvent());
        Assert.False(state.CancelConnection());
        Assert.Equal(MeasurementSessionPhase.Calibrating, state.Phase);
        state.Apply(new MeasurementPromptEvent());
        Assert.False(state.CancelConnection());
        state.Apply(new MeasurementStartedEvent());
        Assert.False(state.CancelConnection());
        Assert.Equal(MeasurementSessionPhase.Measuring, state.Phase);
    }

    [Fact]
    public void CancelledConnectionReturnsSidebarToMeasurementValues()
    {
        var tabs = new MeasurementSidebarTabCoordinator();
        Assert.Equal(MeasurementSidebarTab.SpotreadLog,
            tabs.Observe(MeasurementSessionPhase.Launching, false, false,
                MeasurementSidebarTab.MeasurementValues));
        Assert.Equal(MeasurementSidebarTab.MeasurementValues,
            tabs.Observe(MeasurementSessionPhase.ConnectionCancelled, false, false,
                MeasurementSidebarTab.SpotreadLog));
        Assert.Equal(MeasurementSidebarTab.SpotreadLog,
            tabs.Observe(MeasurementSessionPhase.Launching, false, false,
                MeasurementSidebarTab.MeasurementValues));
    }

    private sealed class ControlledProcess : ISpotreadProcessSession
    {
        public event Action<SpotreadEvent>? EventReceived;
        public event Action<SpotreadLogEntry>? LogReceived;
        public event Action<SpotreadProcessExit>? Exited;
        public bool IsRunning { get; private set; }
        public string ExecutablePath => "unused-test-helper.exe";
        public int StopCount { get; private set; }
        public bool Disposed { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = true;
            LogReceived?.Invoke(new SpotreadLogEntry(
                DateTimeOffset.UtcNow, SpotreadLogKind.Lifecycle, "Test startup"));
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            IsRunning = false;
            Emit(new MeasurementPromptEvent());
            Emit(new MeasurementCompletedEvent(TestMeasurementFactory.Create(MeasurementMode.Reflectance)));
            SignalUnexpectedExit();
            return Task.CompletedTask;
        }
        public Task SendAsync(string command, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SendBytesAsync(ReadOnlyMemory<byte> data, string description,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() { Disposed = true; IsRunning = false; return ValueTask.CompletedTask; }
        public void Emit(SpotreadEvent value) => EventReceived?.Invoke(value);
        public void SignalUnexpectedExit() => Exited?.Invoke(new SpotreadProcessExit(7, false, null));
    }
}
