namespace IwashiScope.App.Wpf.Updates;

internal static class UpdateShutdownPolicy
{
    public static bool CanShutdown(bool isBusy) => !isBusy;
}
