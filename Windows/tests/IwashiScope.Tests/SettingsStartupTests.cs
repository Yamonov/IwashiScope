using IwashiScope.App.Wpf.ViewModels;
using IwashiScope.Infrastructure.Windows.Storage;

namespace IwashiScope.Tests;

public sealed class SettingsStartupTests
{
    [Theory]
    [InlineData("{\"language\":null,\"instrumentIndex\":0}", false)]
    [InlineData("{\"language\":\"en\",\"instrumentIndex\":2}", true)]
    public async Task MissingLanguageOrLockedSettingsDoNotPreventStartup(string json, bool lockSettings)
    {
        var directory = Directory.CreateTempSubdirectory("IwashiScopeSettings");
        var settingsPath = Path.Combine(directory.FullName, "settings.json");
        try
        {
            await File.WriteAllTextAsync(settingsPath, json);
            using var locked = lockSettings
                ? new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.None)
                : null;
            await using var model = new MainWindowViewModel(
                new SettingsStore(settingsPath),
                new MeasurementHistoryPersistenceStore(Path.Combine(directory.FullName, "Default.iwashiscope")));
            await model.InitializeAsync();
            Assert.Equal("ja", model.Language);
            Assert.Equal(1, model.InstrumentIndex);
            Assert.Equal(lockSettings, model.HasError);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
