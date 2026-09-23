using System.IO;
using System.Xml.Linq;
using IwashiScope.App.Wpf.ViewModels;
using IwashiScope.Core.Calculations;
using IwashiScope.Infrastructure.Windows.Storage;

namespace IwashiScope.Tests;

public sealed class IlluminantSpectrumCaptionTests
{
    [Fact]
    public async Task CaptionTracksIlluminantLmsAndLanguageWithoutChangingAdaptationExplanation()
    {
        // All persistence belongs to this unique test directory, never the user's stores.
        // Keep the small files available to finish any asynchronous settings write.
        var root = Path.Combine(Path.GetTempPath(), "IwashiScope-caption-" + Guid.NewGuid().ToString("N"));
        var model = new MainWindowViewModel(new SettingsStore(Path.Combine(root, "settings.json")),
            new MeasurementHistoryPersistenceStore(Path.Combine(root, "history.json")));
        var changes = new List<string?>();
        model.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        try
        {
            Assert.Equal("縦軸：分光反射率（%）", model.IlluminantSpectrumScaleDescription);
            var adaptation = model.ChromaticAdaptationExplanation;
            model.ShowIlluminantLms = true;
            Assert.Contains(nameof(MainWindowViewModel.IlluminantSpectrumScaleDescription), changes);
            Assert.Equal("縦軸：分光反射率（%）／グレー：CIE 2006 LMS（2°・エネルギー基準）", model.IlluminantSpectrumScaleDescription);
            Assert.Equal(adaptation, model.ChromaticAdaptationExplanation);
            Assert.DoesNotContain('\n', model.IlluminantSpectrumScaleDescription);

            changes.Clear();
            model.SelectedCieIlluminantOption = new(CieReferenceIlluminant.D50, "D50");
            Assert.Contains(nameof(MainWindowViewModel.IlluminantSpectrumScaleDescription), changes);
            Assert.StartsWith("黒：分光反射率（%）／黄：ピーク100", model.IlluminantSpectrumScaleDescription);
            Assert.Contains("／グレー：", model.IlluminantSpectrumScaleDescription);

            model.Language = "en";
            Assert.StartsWith("Black: spectral reflectance (%)", model.IlluminantSpectrumScaleDescription);
            Assert.Contains(" / Gray: CIE 2006 LMS (2°, energy)", model.IlluminantSpectrumScaleDescription);
            Assert.DoesNotContain("LMS", model.ChromaticAdaptationExplanation);
            model.ShowIlluminantLms = false;
            Assert.DoesNotContain("Gray:", model.IlluminantSpectrumScaleDescription);
            model.SelectedCieIlluminantOption = new(null, "None");
            Assert.Equal("Vertical axis: spectral reflectance (%)", model.IlluminantSpectrumScaleDescription);
        }
        finally { await model.DisposeAsync(); }
    }

    [Fact]
    public void CaptionBelongsBetweenLegendAndSpectrumChart()
    {
        var root = FindRepositoryRoot();
        var xml = XDocument.Load(Path.Combine(root, "Windows", "src", "IwashiScope.App.Wpf", "MainWindow.xaml"));
        var caption = Assert.Single(xml.Descendants(), e => (string?)e.Attribute("Text") == "{Binding IlluminantSpectrumScaleDescription}");
        var siblings = caption.Parent!.Elements().ToList();
        var index = siblings.IndexOf(caption);
        Assert.Equal("WrapPanel", siblings[index - 1].Name.LocalName);
        Assert.Equal("reflectance-illuminant-legend", (string?)siblings[index - 1].Attribute("AutomationProperties.AutomationId"));
        Assert.Equal("ReflectanceIlluminantChart", siblings[index + 1].Name.LocalName);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Windows", "src", "IwashiScope.App.Wpf", "MainWindow.xaml")))
                return directory.FullName;
        throw new DirectoryNotFoundException("IwashiScope test source root was not found.");
    }
}
