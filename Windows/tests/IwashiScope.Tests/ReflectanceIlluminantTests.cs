using IwashiScope.Core.Calculations;
using IwashiScope.Core.Models;

namespace IwashiScope.Tests;

public sealed class ReflectanceIlluminantTests
{
    [Fact]
    public void CatalogContainsAllFiftyOfficialReferenceSpectra()
    {
        Assert.Equal(50, Enum.GetValues<CieReferenceIlluminant>().Length);
        foreach (var illuminant in Enum.GetValues<CieReferenceIlluminant>())
        {
            var samples = CieReferenceIlluminants.Samples(illuminant);
            Assert.Equal(71, samples.Count);
            Assert.Equal(380, samples[0].Wavelength);
            Assert.Equal(730, samples[^1].Wavelength);
            Assert.All(samples, sample =>
            {
                Assert.True(double.IsFinite(sample.Value));
                Assert.True(sample.Value >= 0);
            });
        }
        Assert.Equal(9.7951, CieReferenceIlluminants.Samples(CieReferenceIlluminant.A)[0].Value);
        Assert.Equal(300, CieReferenceIlluminants.Samples(CieReferenceIlluminant.FL3_15)[0].Value);
        Assert.Equal(0.001919, CieReferenceIlluminants.Samples(CieReferenceIlluminant.L41)[0].Value);
    }

    [Fact]
    public void SelectedIlluminantIsPeakNormalizedAndMultipliedByReflectance()
    {
        var result = ReflectanceIlluminantSpectrumCalculator.Calculate(
            Measurement([380, 560, 730], [50, 50, 50]),
            IlluminantSpectrumDefinition.Cie(CieReferenceIlluminant.D50));

        Assert.NotNull(result);
        Assert.Equal(100, result.Illuminant.Max(sample => sample.Value), 10);
        Assert.Equal(3, result.ReflectedLight.Count);
        Assert.True(result.RequiresUvWarning);
        foreach (var reflected in result.ReflectedLight)
        {
            var source = result.Illuminant.Single(sample =>
                sample.Wavelength == reflected.Wavelength);
            Assert.Equal(source.Value * 0.5, reflected.Value, 10);
        }
    }

    [Fact]
    public void PracticalRangeCropsEveryDisplayedSeries()
    {
        var result = ReflectanceIlluminantSpectrumCalculator.Calculate(
            Measurement([380, 420, 560, 700, 730], [10, 20, 30, 40, 50]),
            IlluminantSpectrumDefinition.Cie(CieReferenceIlluminant.D65),
            new WavelengthRange(420, 700));

        Assert.NotNull(result);
        Assert.Equal([420d, 560d, 700d], result.MeasuredReflectance.Select(x => x.Wavelength));
        Assert.All(result.Illuminant, sample => Assert.InRange(sample.Wavelength, 420, 700));
        Assert.Equal([420d, 560d, 700d], result.ReflectedLight.Select(x => x.Wavelength));
    }

    [Fact]
    public void BradfordAdaptationMapsD65WhiteToD50White()
    {
        var measurement = Measurement(
            [380, 730],
            [100, 100],
            new Vector3(100, 0, 0));
        var source = IlluminantSpectrumDefinition.Cie(CieReferenceIlluminant.D65);

        var adapted = ReflectanceIlluminantColorComparisonCalculator.Calculate(
            measurement,
            source,
            true);
        var unadapted = ReflectanceIlluminantColorComparisonCalculator.Calculate(
            measurement,
            source,
            false);

        Assert.NotNull(adapted);
        Assert.NotNull(unadapted);
        Assert.True(adapted.DeltaE2000 < 0.000001);
        Assert.True(adapted.DeltaE76 < 0.000001);
        Assert.True(unadapted.DeltaE2000 > 5);
    }

    [Fact]
    public void ColorDifferenceMatchesReferenceValues()
    {
        var first = new Vector3(50, 2.6772, -79.7751);
        var second = new Vector3(50, 0, -82.7485);
        Assert.InRange(CieColorDifference.DeltaE2000(first, second), 2.04245, 2.04255);
        Assert.Equal(
            13,
            CieColorDifference.DeltaE76(
                new Vector3(50, 2, -3),
                new Vector3(54, -1, 9)),
            10);
    }

    private static SpotMeasurement Measurement(
        double[] wavelengths,
        double[] values,
        Vector3? lab = null)
    {
        var samples = wavelengths.Zip(values)
            .Select((pair, index) => new SpectralSample(index, pair.First, pair.Second))
            .ToArray();
        return new SpotMeasurement
        {
            CapturedAt = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000),
            Mode = MeasurementMode.Reflectance,
            SpectrumStart = wavelengths.Min(),
            SpectrumEnd = wavelengths.Max(),
            DeclaredStepCount = samples.Length,
            Spectrum = samples,
            PeakValue = values.Max(),
            Lab = lab,
            LabWhitePoint = "D50",
        };
    }
}
