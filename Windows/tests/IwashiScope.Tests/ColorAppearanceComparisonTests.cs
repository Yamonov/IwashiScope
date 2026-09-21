using System.Reflection;
using System.Windows.Media;
using IwashiScope.App.Wpf.ViewModels;
using IwashiScope.Core.Calculations;
using IwashiScope.Core.Models;

namespace IwashiScope.Tests;

public sealed class ColorAppearanceComparisonTests
{
    public static IEnumerable<object[]> Illuminants => Enum.GetValues<CieReferenceIlluminant>().Select(value => new object[] { value });

    [Theory]
    [InlineData(ReflectanceAppearanceMethod.Unadapted)]
    [InlineData(ReflectanceAppearanceMethod.Bradford)]
    [InlineData(ReflectanceAppearanceMethod.CieCam16)]
    public void D50MatchesItsRecalculatedBaselineWithoutChangingTheOriginal(ReflectanceAppearanceMethod method)
    {
        var measurement = Flat(40) with { Lab = new Vector3(1, 2, 3) };
        var result = ReflectanceIlluminantColorComparisonCalculator.Compare(
            measurement, IlluminantSpectrumDefinition.Cie(CieReferenceIlluminant.D50), method);
        Assert.InRange(result.DeltaE76, 0, 1e-8);
        Assert.InRange(result.DeltaE2000, 0, 1e-8);
        Assert.Equal(measurement.Lab, result.MeasuredLab);
        Assert.NotEqual(result.MeasuredLab, result.ReferenceLab);
        Assert.Equal(new Vector3(1, 2, 3), measurement.Lab);
    }

    [Fact]
    public void WarmLightLeavesAResidualCastOnWhiteInStandardCieCam16()
    {
        var result = ReflectanceIlluminantColorComparisonCalculator.Compare(
            Flat(100), IlluminantSpectrumDefinition.Cie(CieReferenceIlluminant.A), ReflectanceAppearanceMethod.CieCam16);
        Assert.True(result.SimulatedLab.Third > 1);
        Assert.True(result.DeltaE2000 > 1);
        Assert.InRange(result.SourceAdaptationDegree!.Value, 0, 0.999999);
        Assert.Equal(result.ReferenceAdaptationDegree, result.SourceAdaptationDegree);
        var reproduced = new CieCam16(new(result.ReferenceWhiteXyz)).Forward(result.SimulatedXyz);
        Assert.InRange(Math.Abs(reproduced.Brightness - result.SourceAppearance!.Brightness), 0, 1e-8);
        Assert.InRange(Math.Abs(reproduced.Colorfulness - result.SourceAppearance.Colorfulness), 0, 1e-8);
        Assert.InRange(Math.Abs(reproduced.Saturation - result.SourceAppearance.Saturation), 0, 1e-8);
    }

    [Theory]
    [MemberData(nameof(Illuminants))]
    public void EveryBundledIlluminantSupportsAChromaticMaterial(CieReferenceIlluminant illuminant)
    {
        var measurement = Flat(50) with
        {
            Spectrum = Enumerable.Range(0, 71).Select(i => new SpectralSample(i, 380 + i * 5,
                10 + 70 * Math.Exp(-Math.Pow((380 + i * 5 - 620d) / 65, 2)))).ToArray(),
        };
        var result = ReflectanceIlluminantColorComparisonCalculator.Compare(
            measurement, IlluminantSpectrumDefinition.Cie(illuminant), ReflectanceAppearanceMethod.CieCam16);
        Assert.True(double.IsFinite(result.DeltaE2000));
        Assert.True(double.IsFinite(result.SourceAppearance!.Colorfulness));
        Assert.True(double.IsFinite(result.SourceAppearance.Saturation));
    }

    [Fact]
    public void ChangingMethodLeavesPhysicalXyzAndSpectrumUnchanged()
    {
        var measurement = Flat(60);
        var original = measurement.Spectrum.ToArray();
        var results = Enum.GetValues<ReflectanceAppearanceMethod>().Select(method =>
            ReflectanceIlluminantColorComparisonCalculator.Compare(measurement,
                IlluminantSpectrumDefinition.Cie(CieReferenceIlluminant.LEDB1), method)).ToArray();
        Assert.All(results, result => Assert.Equal(results[0].SourceXyz, result.SourceXyz));
        Assert.Equal(original, measurement.Spectrum);
    }

    [Fact]
    public void RelativeSpdScaleDoesNotChangeAppearanceAndCoverageIsNotExtrapolated()
    {
        var measurement = Flat(50);
        var source = IlluminantSpectrumDefinition.Cie(CieReferenceIlluminant.A);
        var scaled = source with { Samples = source.Samples.Select(s => s with { Value = s.Value * 7 }).ToArray() };
        var first = ReflectanceIlluminantColorComparisonCalculator.Compare(measurement, source, ReflectanceAppearanceMethod.CieCam16);
        var second = ReflectanceIlluminantColorComparisonCalculator.Compare(measurement, scaled, ReflectanceAppearanceMethod.CieCam16);
        Assert.InRange(CieColorDifference.DeltaE2000(first.SimulatedLab, second.SimulatedLab), 0, 1e-8);
        var partial = source with { Samples = [new(0, 500, 100), new(1, 600, 100)] };
        var limited = ReflectanceIlluminantColorComparisonCalculator.Compare(measurement, partial, ReflectanceAppearanceMethod.CieCam16);
        Assert.Equal(new WavelengthRange(500, 600), limited.WavelengthRange);
        Assert.InRange(Math.Abs(limited.ReferenceLab.Second), 0, 1e-9);
        Assert.InRange(Math.Abs(limited.ReferenceLab.Third), 0, 1e-9);
    }

    [Fact]
    public void IdenticalWhiteDifferentSpectraCanChangeTheMaterialColor()
    {
        var adjustments = new Dictionary<double, double>
        { [420] = -40, [480] = 31.83558926084189, [550] = -6.617313628937351, [620] = 6.084286713386721 };
        var samples = Enumerable.Range(0, 71).Select(i => new SpectralSample(i, 380 + i * 5, 50)).ToArray();
        var source = IlluminantSpectrumDefinition.Cie(CieReferenceIlluminant.D50) with { Samples = samples };
        var other = source with { Samples = samples.Select(s => s with { Value = s.Value + adjustments.GetValueOrDefault(s.Wavelength) }).ToArray() };
        var material = Flat(50) with { Spectrum = samples.Select(s => s with { Value = s.Wavelength >= 560 ? 90 : 10 }).ToArray() };
        var a = ReflectanceIlluminantColorComparisonCalculator.Compare(material, source, ReflectanceAppearanceMethod.CieCam16);
        var b = ReflectanceIlluminantColorComparisonCalculator.Compare(material, other, ReflectanceAppearanceMethod.CieCam16);
        Assert.InRange(Math.Abs(a.SourceWhiteXyz.First - b.SourceWhiteXyz.First), 0, 1e-9);
        Assert.InRange(Math.Abs(a.SourceWhiteXyz.Third - b.SourceWhiteXyz.Third), 0, 1e-9);
        Assert.True(CieColorDifference.DeltaE2000(a.SimulatedLab, b.SimulatedLab) > 0.01);
    }

    [Fact]
    public void DuplicateAndMissingSamplesAreRejectedExplicitly()
    {
        var missing = Flat(50) with { Spectrum = [] };
        var duplicate = Flat(50) with { Spectrum = [new(0, 500, 50), new(1, 500, 50)] };
        foreach (var measurement in new[] { missing, duplicate })
        {
            var error = Assert.Throws<ColorAppearanceException>(() => ReflectanceIlluminantColorComparisonCalculator.ReferenceLab(measurement));
            Assert.Equal(ColorAppearanceError.InsufficientSpectrum, error.Error);
        }
    }

    [Fact]
    public void ViewModelKeepsReferenceVisibleBeforeAndAfterIlluminantSelection()
    {
        // Do not initialize/dispose the live application's settings or persistence store.
        // These setters exercise calculation/notification only and do not write settings.
        var model = new MainWindowViewModel();
        typeof(MainWindowViewModel).GetProperty(nameof(MainWindowViewModel.ActiveMeasurement))!.SetValue(model, Flat(60));
        Assert.True(model.UsePracticalRange);
        Assert.Equal(SpectrumYAxisMode.Automatic, model.YAxisConfiguration.Mode);
        Assert.Equal(ReflectanceAppearanceMethod.CieCam16, model.AppearanceMethod);
        Assert.True(model.HasReflectanceMeasurement);
        Assert.False(model.ShowsReferencePatchPlaceholder);
        Assert.False(model.HasReflectanceColorComparison);
        Assert.Equal("—", model.DeltaE00Text);
        var baseline = ((SolidColorBrush)model.MeasuredReflectancePatchBrush).Color;
        model.SelectedCieIlluminantOption = new(CieReferenceIlluminant.A, "A");
        Assert.True(model.HasReflectanceColorComparison);
        var adapted = model.ReflectanceColorComparisonResult!.SimulatedLab;
        model.AppearanceMethod = ReflectanceAppearanceMethod.Unadapted;
        Assert.NotEqual(adapted, model.ReflectanceColorComparisonResult!.SimulatedLab);
        model.SelectedCieIlluminantOption = new(null, "None");
        Assert.False(model.HasReflectanceColorComparison);
        Assert.False(model.ShowsReferencePatchPlaceholder);
        Assert.Equal(baseline, ((SolidColorBrush)model.MeasuredReflectancePatchBrush).Color);
        Assert.Equal("—", model.DeltaE00Text);
    }

    private static SpotMeasurement Flat(double value) => TestMeasurementFactory.Create(MeasurementMode.Reflectance) with
    {
        SpectrumStart = 380, SpectrumEnd = 730, DeclaredStepCount = 2,
        Spectrum = [new(0, 380, value), new(1, 730, value)],
    };
}
