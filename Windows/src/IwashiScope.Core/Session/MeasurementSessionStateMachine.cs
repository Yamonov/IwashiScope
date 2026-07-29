using IwashiScope.Core.Models;

namespace IwashiScope.Core.Session;

public enum MeasurementSessionPhase
{
    Idle,
    Launching,
    CalibrationRecommended,
    AwaitingCalibrationSetup,
    WaitingForInstrument,
    Calibrating,
    Ready,
    Measuring,
    RetryAvailable,
    ConfigurationRequired,
    Recovering,
    Workspace,
    Stopped,
    Failed,
}

public enum SessionTimeoutKind
{
    Launch,
    Calibration,
    Measurement,
    Recovery,
}

public static class MeasurementSessionTimeouts
{
    public static TimeSpan For(SessionTimeoutKind kind) => kind switch
    {
        SessionTimeoutKind.Launch => TimeSpan.FromSeconds(30),
        SessionTimeoutKind.Calibration => TimeSpan.FromSeconds(90),
        SessionTimeoutKind.Measurement => TimeSpan.FromSeconds(60),
        SessionTimeoutKind.Recovery => TimeSpan.FromSeconds(15),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

public sealed class MeasurementSessionStateMachine
{
    public MeasurementSessionStateMachine(MeasurementMode mode)
    {
        Mode = mode;
    }

    public MeasurementMode Mode { get; private set; }
    public MeasurementSessionPhase Phase { get; private set; } = MeasurementSessionPhase.Idle;
    public SpotreadIssue? CurrentIssue { get; private set; }
    public SpotreadInstrumentIdentity? Instrument { get; private set; }
    public CalibrationPrompt? CurrentCalibrationPrompt { get; private set; }
    public int RecoveryAttempts { get; private set; }

    public void Start(MeasurementMode mode)
    {
        Mode = mode;
        Phase = MeasurementSessionPhase.Launching;
        CurrentIssue = null;
        Instrument = null;
        CurrentCalibrationPrompt = null;
    }

    public void Apply(SpotreadEvent @event)
    {
        switch (@event)
        {
            case HelloAcceptedEvent:
                Phase = MeasurementSessionPhase.WaitingForInstrument;
                break;
            case InstrumentIdentityEvent identity:
                Instrument = identity.Identity;
                if (Phase is MeasurementSessionPhase.Launching or MeasurementSessionPhase.WaitingForInstrument)
                {
                    Phase = MeasurementSessionPhase.WaitingForInstrument;
                }
                break;
            case CalibrationStartedEvent:
                Phase = MeasurementSessionPhase.Calibrating;
                CurrentIssue = null;
                break;
            case CalibrationPromptEvent:
                Phase = MeasurementSessionPhase.AwaitingCalibrationSetup;
                CurrentCalibrationPrompt = ((CalibrationPromptEvent)@event).Prompt;
                break;
            case CalibrationCompletedEvent:
                Phase = MeasurementSessionPhase.WaitingForInstrument;
                CurrentIssue = null;
                CurrentCalibrationPrompt = null;
                break;
            case MeasurementPromptEvent:
                Phase = MeasurementSessionPhase.Ready;
                CurrentIssue = null;
                break;
            case MeasurementStartedEvent:
                Phase = MeasurementSessionPhase.Measuring;
                CurrentIssue = null;
                break;
            case MeasurementCompletedEvent:
                Phase = MeasurementSessionPhase.Workspace;
                CurrentIssue = null;
                break;
            case SavedReadingPromptEvent:
                Phase = MeasurementSessionPhase.WaitingForInstrument;
                break;
            case RecoverableIssueEvent issue:
                CurrentIssue = issue.Issue;
                Phase = MeasurementSessionPhase.RetryAvailable;
                break;
            case ConfigurationIssueEvent issue:
                CurrentIssue = issue.Issue;
                Phase = MeasurementSessionPhase.ConfigurationRequired;
                break;
            case FatalIssueEvent issue:
                CurrentIssue = issue.Issue;
                Phase = MeasurementSessionPhase.Failed;
                break;
        }
    }

    public bool TryBeginAutomaticRecovery()
    {
        if (RecoveryAttempts >= 1)
        {
            Phase = MeasurementSessionPhase.Failed;
            return false;
        }

        RecoveryAttempts++;
        Phase = MeasurementSessionPhase.Recovering;
        return true;
    }

    public void Timeout(SessionTimeoutKind timeout)
    {
        CurrentIssue = new SpotreadIssue
        {
            Kind = timeout == SessionTimeoutKind.Calibration
                ? SpotreadIssueKind.CalibrationFailure
                : SpotreadIssueKind.OperationFailure,
            RecoveryAction = SpotreadRecoveryAction.Restart,
            Code = $"{timeout.ToString().ToLowerInvariant()}Timeout",
            Reason = $"{timeout} timed out.",
            RawText = string.Empty,
        };
        Phase = MeasurementSessionPhase.RetryAvailable;
    }

    public void Stop()
    {
        Phase = MeasurementSessionPhase.Stopped;
    }
}
