using IwashiScope.Core.Session;
using IwashiScope.Core.Workspace;

namespace IwashiScope.App.Wpf.Layout;

public sealed class MeasurementSidebarTabCoordinator
{
    private MeasurementSessionPhase _previousPhase = MeasurementSessionPhase.Idle;
    private bool _previousCalibrationCompleted;
    private bool _awaitingCalibrationCompletion;

    public MeasurementSidebarTab Observe(
        MeasurementSessionPhase phase,
        bool calibrationCompleted,
        bool isBrowsingRestoredWorkspace,
        MeasurementSidebarTab currentTab)
    {
        if (isBrowsingRestoredWorkspace)
        {
            Remember(phase, calibrationCompleted);
            return currentTab;
        }

        var phaseChanged = phase != _previousPhase;
        if (phaseChanged && BeginsCalibrationOrRecovery(phase))
        {
            _awaitingCalibrationCompletion = true;
            currentTab = MeasurementSidebarTab.SpotreadLog;
        }

        if (_awaitingCalibrationCompletion &&
            calibrationCompleted &&
            !_previousCalibrationCompleted)
        {
            _awaitingCalibrationCompletion = false;
            currentTab = MeasurementSidebarTab.MeasurementValues;
        }

        Remember(phase, calibrationCompleted);
        return currentTab;
    }

    private void Remember(
        MeasurementSessionPhase phase,
        bool calibrationCompleted)
    {
        _previousPhase = phase;
        _previousCalibrationCompleted = calibrationCompleted;
    }

    private static bool BeginsCalibrationOrRecovery(MeasurementSessionPhase phase) =>
        phase is MeasurementSessionPhase.Launching or
            MeasurementSessionPhase.CalibrationRecommended or
            MeasurementSessionPhase.AwaitingCalibrationSetup or
            MeasurementSessionPhase.Calibrating or
            MeasurementSessionPhase.Recovering;
}
