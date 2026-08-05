using IwashiScope.Core.Models;

namespace IwashiScope.Core.Session;

public abstract record SpotreadEvent;

public sealed record HelloAcceptedEvent(
    int ProtocolVersion,
    string Implementation,
    int ImplementationVersion,
    string ArgyllVersion,
    IReadOnlySet<string>? Capabilities = null) : SpotreadEvent;

public sealed record InstrumentIdentityEvent(SpotreadInstrumentIdentity Identity) : SpotreadEvent;
public sealed record CalibrationStartedEvent : SpotreadEvent;
public sealed record CalibrationCompletedEvent(bool Skipped) : SpotreadEvent;
public sealed record SavedReadingPromptEvent : SpotreadEvent;
public sealed record MeasurementStartedEvent : SpotreadEvent;
public sealed record SpectrumAnalysisInputReadyEvent : SpotreadEvent;
public sealed record SpectrumAnalysisStartedEvent : SpotreadEvent;
public sealed record SpectrumAnalysisFailedEvent(string Reason) : SpotreadEvent;
public sealed record MeasurementCompletedEvent(SpotMeasurement Measurement) : SpotreadEvent;
public sealed record MeasurementPromptEvent : SpotreadEvent;

public sealed record CalibrationPrompt
{
    public required string Condition { get; init; }
    public string? Identifier { get; init; }
    public bool RequiresUserConfirmation { get; init; } = true;
    public bool AllowsSkip { get; init; }
    public required string RawText { get; init; }
}

public sealed record CalibrationPromptEvent(CalibrationPrompt Prompt) : SpotreadEvent;

public enum SpotreadIssueKind
{
    UserStopped,
    SensorSaturated,
    InconsistentReading,
    MeasurementFailure,
    CommunicationFailure,
    WrongConfiguration,
    OutputParsingFailure,
    CalibrationFailure,
    OperationFailure,
    FatalFailure,
}

public enum SpotreadRecoveryAction
{
    ResumeMeasurementLoop,
    RetryCalibration,
    RetryOperation,
    AcknowledgeConfiguration,
    Restart,
}

public sealed record SpotreadIssue
{
    public required SpotreadIssueKind Kind { get; init; }
    public required SpotreadRecoveryAction RecoveryAction { get; init; }
    public required string Code { get; init; }
    public required string Reason { get; init; }
    public required string RawText { get; init; }
}

public sealed record RecoverableIssueEvent(SpotreadIssue Issue) : SpotreadEvent;
public sealed record ConfigurationIssueEvent(SpotreadIssue Issue) : SpotreadEvent;
public sealed record FatalIssueEvent(SpotreadIssue Issue) : SpotreadEvent;
public sealed record WarningEvent(string Message, string RawText) : SpotreadEvent;
