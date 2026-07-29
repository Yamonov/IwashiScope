using IwashiScope.Core.Session;

namespace IwashiScope.App.Wpf.ViewModels;

public static class SessionErrorPresentation
{
    public static string Resolve(
        SpotreadIssue? currentIssue,
        string? operationError = null) =>
        currentIssue?.Reason ??
        operationError ??
        string.Empty;
}
