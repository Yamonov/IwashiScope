using System.Configuration;
using System.Data;
using System.Windows;
using IwashiScope.App.Wpf.Updates;

namespace IwashiScope.App.Wpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    internal WinSparkleUpdater Updater { get; } = new();

    protected override void OnExit(ExitEventArgs e)
    {
        Updater.Dispose();
        base.OnExit(e);
    }
}
