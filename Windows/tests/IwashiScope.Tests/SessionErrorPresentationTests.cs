using IwashiScope.App.Wpf.ViewModels;
using IwashiScope.Core.Models;
using IwashiScope.Core.Session;

namespace IwashiScope.Tests;

public sealed class SessionErrorPresentationTests
{
    [Fact]
    public void RecoverableReadingErrorDisappearsWhenMeasurementLoopResumes()
    {
        var state = new MeasurementSessionStateMachine(MeasurementMode.Reflectance);
        state.Start(MeasurementMode.Reflectance);
        state.Apply(
            new RecoverableIssueEvent(
                new SpotreadIssue
                {
                    Kind = SpotreadIssueKind.InconsistentReading,
                    RecoveryAction = SpotreadRecoveryAction.ResumeMeasurementLoop,
                    Code = "misread",
                    Reason = "Reading is incorrect",
                    RawText = "Reading is incorrect",
                }));

        Assert.Equal(
            "Reading is incorrect",
            SessionErrorPresentation.Resolve(state.CurrentIssue));

        state.Apply(new MeasurementPromptEvent());

        Assert.Equal(MeasurementSessionPhase.Ready, state.Phase);
        Assert.Null(state.CurrentIssue);
        Assert.Empty(SessionErrorPresentation.Resolve(state.CurrentIssue));
    }

    [Fact]
    public void OperationErrorIsKeptOnlyWhenThereIsNoSessionIssue()
    {
        var issue = new SpotreadIssue
        {
            Kind = SpotreadIssueKind.MeasurementFailure,
            RecoveryAction = SpotreadRecoveryAction.RetryOperation,
            Code = "measurementFailed",
            Reason = "Session issue",
            RawText = string.Empty,
        };

        Assert.Equal(
            "Session issue",
            SessionErrorPresentation.Resolve(issue, "Transport exception"));
        Assert.Equal(
            "Transport exception",
            SessionErrorPresentation.Resolve(null, "Transport exception"));
    }

    [Fact]
    public void SuccessfulAutomaticRecoveryClearsThePreviousIssueFromTheUi()
    {
        var state = new MeasurementSessionStateMachine(MeasurementMode.Reflectance);
        state.Start(MeasurementMode.Reflectance);
        state.Timeout(SessionTimeoutKind.Recovery);

        Assert.NotNull(state.CurrentIssue);
        Assert.True(state.TryBeginAutomaticRecovery());

        state.Apply(new HelloAcceptedEvent(3, "iwashiscope-spotread", 1, "3.5.0"));

        Assert.Equal(MeasurementSessionPhase.WaitingForInstrument, state.Phase);
        Assert.Null(state.CurrentIssue);
        Assert.Empty(SessionErrorPresentation.Resolve(state.CurrentIssue));
    }
}
