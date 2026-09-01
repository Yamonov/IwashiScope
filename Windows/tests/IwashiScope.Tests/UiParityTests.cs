using System.Windows;
using IwashiScope.App.Wpf.Layout;
using IwashiScope.Core.Models;

namespace IwashiScope.Tests;

public sealed class UiParityTests
{
    [Theory]
    [InlineData(MeasurementMode.Reflectance, true, false, true, false)]
    [InlineData(MeasurementMode.Ambient, false, true, false, true)]
    [InlineData(MeasurementMode.Emissive, true, true, false, true)]
    public void PerModeVisibilityMatchesMacPresentation(
        MeasurementMode mode,
        bool showsSrgb,
        bool showsLightingTabs,
        bool showsReflectanceHistory,
        bool showsStandards)
    {
        var profile = UiParityProfiles.For(mode);

        Assert.Equal(showsSrgb, profile.Shows(UiParitySection.SrgbEncoding));
        Assert.Equal(
            showsLightingTabs,
            profile.Shows(UiParitySection.LightingRenderingTabs));
        Assert.Equal(
            showsReflectanceHistory,
            profile.Shows(UiParitySection.ReflectanceHistory));
        Assert.Equal(showsStandards, profile.Shows(UiParitySection.JspstEvaluation));
        Assert.Equal(showsStandards, profile.Shows(UiParitySection.Iso3664Evaluation));
        Assert.Equal(showsSrgb, profile.Shows(UiParitySection.AdobeRgbEncoding));
        Assert.Equal(showsSrgb, profile.Shows(UiParitySection.DisplayP3Encoding));
        Assert.True(profile.Shows(UiParitySection.DetailedLog));
    }

    [Fact]
    public void SectionOrderMatchesMacViewComposition()
    {
        Assert.Equal(
            [
                UiParitySection.Spectrum,
                UiParitySection.ReflectanceIlluminantComparison,
            ],
            UiParityProfiles.For(MeasurementMode.Reflectance).CentralSections);
        Assert.Equal(
            [
                UiParitySection.Spectrum,
                UiParitySection.ReferenceSpectrumControls,
                UiParitySection.LightingRenderingTabs,
            ],
            UiParityProfiles.For(MeasurementMode.Ambient).CentralSections);
        Assert.Equal(
            [
                UiParitySection.MeasurementHeader,
                UiParitySection.ColorimetricValues,
                UiParitySection.LightingInformation,
                UiParitySection.JspstEvaluation,
                UiParitySection.Iso3664Evaluation,
                UiParitySection.CriTlciSummary,
            ],
            UiParityProfiles.For(MeasurementMode.Ambient).MeasurementValueSections);
    }

    [Theory]
    [InlineData(380, 440)] // 860 px minimum window minus the macOS-width right pane and margins
    [InlineData(960, 440)]
    [InlineData(1400, 600)]
    public void Tm30LayoutStaysInsideAvailableBounds(double width, double height)
    {
        var layout = Tm30Layout.Calculate(width, height);
        var bounds = new Rect(0, 0, width, height);

        foreach (var rectangle in layout.Rectangles)
        {
            Assert.True(rectangle.Width > 0);
            Assert.True(rectangle.Height > 0);
            Assert.True(bounds.Contains(rectangle.TopLeft));
            Assert.True(bounds.Contains(rectangle.BottomRight));
        }

        Assert.Equal(layout.Vector.Width, layout.Vector.Height, precision: 8);
        Assert.False(layout.Vector.IntersectsWith(layout.Scores));
        Assert.False(layout.Vector.IntersectsWith(layout.RfRg));
        Assert.False(layout.Vector.IntersectsWith(layout.Samples));
        Assert.False(layout.Scores.IntersectsWith(layout.RfRg));
        Assert.False(layout.Scores.IntersectsWith(layout.Samples));
        Assert.False(layout.RfRg.IntersectsWith(layout.Samples));
    }
}
