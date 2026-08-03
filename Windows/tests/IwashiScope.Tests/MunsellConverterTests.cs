using IwashiScope.Core.Calculations;
using IwashiScope.Core.Models;

namespace IwashiScope.Tests;

public sealed class MunsellConverterTests
{
    [Fact]
    public void FlatReflectanceIntegratesToIlluminantCXyz()
    {
        var xyz = MunsellConverter.IlluminantCXYZ(FlatSpectrum(100));

        Assert.NotNull(xyz);
        Assert.InRange(xyz.First, 98.00, 98.10);
        Assert.InRange(xyz.Second, 99.999_999, 100.000_001);
        Assert.InRange(xyz.Third, 118.12, 118.22);
    }

    [Fact]
    public void NeutralReflectanceUsesNeutralMunsellNotation()
    {
        var notation = MunsellConverter.Convert(FlatSpectrum(19.271844));

        Assert.NotNull(notation);
        Assert.Null(notation.Hue);
        Assert.Null(notation.HueDesignator);
        Assert.Equal("N 5", notation.Formatted);
    }

    [Fact]
    public void ChromaticReflectanceProducesHueValueAndChroma()
    {
        var spectrum = Enumerable.Range(0, 71)
            .Select(index =>
            {
                var wavelength = 380d + index * 5;
                var redBand = 60 * Math.Exp(-Math.Pow((wavelength - 610) / 50, 2));
                return new SpectralSample(index, wavelength, 10 + redBand);
            })
            .ToArray();

        var notation = MunsellConverter.Convert(spectrum);

        Assert.NotNull(notation);
        Assert.NotNull(notation.Hue);
        Assert.NotNull(notation.HueDesignator);
        Assert.True(notation.Value > 0);
        Assert.True(notation.Chroma >= 0.5);
    }

    [Fact]
    public void UnusableSpectrumDoesNotProduceMunsellNotation()
    {
        Assert.Null(MunsellConverter.Convert(
        [
            new SpectralSample(0, 380, double.NaN),
            new SpectralSample(1, 730, 20),
        ]));
    }

    private static IReadOnlyList<SpectralSample> FlatSpectrum(double value) =>
    [
        new SpectralSample(0, 380, value),
        new SpectralSample(1, 730, value),
    ];
}
