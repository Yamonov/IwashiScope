using IwashiScope.App.Wpf.Layout;
using IwashiScope.Core.Models;
using IwashiScope.Core.Session;
using IwashiScope.Core.Workspace;

namespace IwashiScope.Tests;

public sealed class MeasurementSidebarTabCoordinatorTests
{
    [Theory]
    [InlineData(MeasurementMode.Reflectance)]
    [InlineData(MeasurementMode.Ambient)]
    [InlineData(MeasurementMode.Emissive)]
    public void LiveCalibrationTransitionsFromLogToMeasurementsOnce(
        MeasurementMode mode)
    {
        Assert.True(Enum.IsDefined(mode));
        var coordinator = new MeasurementSidebarTabCoordinator();
        var tab = MeasurementSidebarTab.MeasurementValues;

        tab = coordinator.Observe(
            MeasurementSessionPhase.Launching,
            calibrationCompleted: false,
            isBrowsingRestoredWorkspace: false,
            tab);
        Assert.Equal(MeasurementSidebarTab.SpotreadLog, tab);

        tab = coordinator.Observe(
            MeasurementSessionPhase.AwaitingCalibrationSetup,
            calibrationCompleted: false,
            isBrowsingRestoredWorkspace: false,
            tab);
        Assert.Equal(MeasurementSidebarTab.SpotreadLog, tab);

        tab = coordinator.Observe(
            MeasurementSessionPhase.WaitingForInstrument,
            calibrationCompleted: true,
            isBrowsingRestoredWorkspace: false,
            tab);
        Assert.Equal(MeasurementSidebarTab.MeasurementValues, tab);

        // A user's later tab choice is not overwritten by an ordinary reading.
        tab = MeasurementSidebarTab.SpotreadLog;
        tab = coordinator.Observe(
            MeasurementSessionPhase.Measuring,
            calibrationCompleted: true,
            isBrowsingRestoredWorkspace: false,
            tab);
        tab = coordinator.Observe(
            MeasurementSessionPhase.Ready,
            calibrationCompleted: true,
            isBrowsingRestoredWorkspace: false,
            tab);
        Assert.Equal(MeasurementSidebarTab.SpotreadLog, tab);
    }

    [Fact]
    public void RecalibrationAndRecoveryReturnToLogUntilCompletion()
    {
        var coordinator = new MeasurementSidebarTabCoordinator();
        var tab = coordinator.Observe(
            MeasurementSessionPhase.Launching,
            calibrationCompleted: false,
            isBrowsingRestoredWorkspace: false,
            MeasurementSidebarTab.MeasurementValues);
        tab = coordinator.Observe(
            MeasurementSessionPhase.Ready,
            calibrationCompleted: true,
            isBrowsingRestoredWorkspace: false,
            tab);

        tab = coordinator.Observe(
            MeasurementSessionPhase.Calibrating,
            calibrationCompleted: false,
            isBrowsingRestoredWorkspace: false,
            tab);
        Assert.Equal(MeasurementSidebarTab.SpotreadLog, tab);
        tab = coordinator.Observe(
            MeasurementSessionPhase.WaitingForInstrument,
            calibrationCompleted: true,
            isBrowsingRestoredWorkspace: false,
            tab);
        Assert.Equal(MeasurementSidebarTab.MeasurementValues, tab);

        tab = coordinator.Observe(
            MeasurementSessionPhase.Recovering,
            calibrationCompleted: false,
            isBrowsingRestoredWorkspace: false,
            tab);
        Assert.Equal(MeasurementSidebarTab.SpotreadLog, tab);
    }

    [Theory]
    [InlineData(MeasurementSidebarTab.MeasurementValues)]
    [InlineData(MeasurementSidebarTab.SpotreadLog)]
    public void RestoredWorkspaceSelectionIsNeverOverwritten(
        MeasurementSidebarTab restoredTab)
    {
        var coordinator = new MeasurementSidebarTabCoordinator();

        var selected = coordinator.Observe(
            MeasurementSessionPhase.Launching,
            calibrationCompleted: false,
            isBrowsingRestoredWorkspace: true,
            restoredTab);
        selected = coordinator.Observe(
            MeasurementSessionPhase.Calibrating,
            calibrationCompleted: false,
            isBrowsingRestoredWorkspace: true,
            selected);
        selected = coordinator.Observe(
            MeasurementSessionPhase.Ready,
            calibrationCompleted: true,
            isBrowsingRestoredWorkspace: true,
            selected);

        Assert.Equal(restoredTab, selected);
    }
}
