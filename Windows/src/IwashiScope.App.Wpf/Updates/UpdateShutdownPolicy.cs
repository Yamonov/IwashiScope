namespace IwashiScope.App.Wpf.Updates;

internal static class UpdateShutdownPolicy
{
    public static bool CanShutdown(bool hasUnsavedChanges, bool isBusy) =>
        !hasUnsavedChanges && !isBusy;
}
