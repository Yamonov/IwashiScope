using IwashiScope.App.Wpf.Localization;
using IwashiScope.App.Wpf.ViewModels;
using IwashiScope.Core.Session;

namespace IwashiScope.Tests;

public sealed class CalibrationPromptPresentationTests
{
    [Fact]
    public void SensorPositionUsesMacPresentationInsteadOfRawJson()
    {
        var localization = new LocalizationCatalog();
        var prompt = new CalibrationPrompt
        {
            Condition = "sensorCalibrationPosition",
            Identifier = "0\uFFFD\uFFFDӨ",
            RequiresUserConfirmation = true,
            RawText = "{\"event\":\"calibration\"}",
        };

        var presentation = CalibrationPromptPresentations.For(prompt, localization);

        Assert.Equal("測定器を校正位置へ", presentation.Title);
        Assert.Equal(
            "センサーのダイヤルをキャリブレーション位置に合わせてください。",
            presentation.Instruction);
        Assert.DoesNotContain("{", presentation.Instruction);
        Assert.DoesNotContain('\uFFFD', presentation.Instruction);
    }

    [Fact]
    public void EnglishPresentationUsesCatalogWording()
    {
        var localization = new LocalizationCatalog();
        localization.SetLanguage("en");
        var prompt = new CalibrationPrompt
        {
            Condition = "sensorCalibrationPosition",
            RequiresUserConfirmation = true,
            RawText = string.Empty,
        };

        var presentation = CalibrationPromptPresentations.For(prompt, localization);

        Assert.Equal("Move the Instrument to the Calibration Position", presentation.Title);
        Assert.Equal(
            "Set the sensor dial to the calibration position.",
            presentation.Instruction);
    }
}
