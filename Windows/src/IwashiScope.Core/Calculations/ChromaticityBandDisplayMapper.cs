using IwashiScope.Core.Models;
using static IwashiScope.Core.Calculations.ChromaticityMath;
namespace IwashiScope.Core.Calculations;

/// <summary>Preserves type-specific q after display-gamut fitting; raw retained-cone signals remain untouched.</summary>
public sealed class ChromaticityBandDisplayMapper
{
    public sealed record DisplaySample(Vector3 LinearRgb, Vector3 ModelLms, double ChromaScale);
    public ChromaticityBrightnessReference Reference { get; }
    public ChromaticityDisplayGamut Gamut { get; }
    public ChromaticityRenderingSpace Space { get; }
    public Vector3 GrayLinearRgb { get; }
    private readonly Func<Vector3, Vector3> _linearTransform;
    public ChromaticityBandDisplayMapper(ChromaticityBrightnessReference reference, ChromaticityRenderingSpace space = ChromaticityRenderingSpace.Srgb,
        Func<Vector3, Vector3>? linearTransform = null)
    {
        _linearTransform = linearTransform ?? (rgb => ChromaticityDisplayColorTransform.FromSrgb(rgb, space));
        Reference = reference; Space = space; Gamut = new(space, _linearTransform);
        GrayLinearRgb = Scale(Gamut.WhiteRgb, reference.RelativeLuminance);
    }
    public DisplaySample? Sample(ChromaticityConfusionBand.Stop stop)
    {
        var white = ChromaticityBrightnessReference.PaletteWhiteLms;
        if (!stop.Point.IsFinite || !stop.LinearRgb.IsFinite || ChromaticityBrightnessReference.Response(stop.Lms, white, Reference.Mode) is not { } response) return null;
        var q = Reference.RelativeLuminance;
        if (q == 0) return new(GrayLinearRgb, new(0, 0, 0), 0);
        if (response <= 0) return null;
        var gain = q / response;
        var candidate = _linearTransform(Scale(stop.LinearRgb, gain));
        if (Gamut.Map(candidate, q) is not { } mapped) return null;
        return new(mapped.LinearRgb, Add(Scale(stop.Lms, gain * mapped.ChromaScale), Scale(white, q * (1 - mapped.ChromaScale))), mapped.ChromaScale);
    }
    public float[]? Raster(ChromaticityConfusionBand.Section section, int samples = 4096)
    {
        if (samples < 2 || samples > 16384) return null;
        var pixels = new float[samples * 4];
        for (var i = 0; i < samples; i++)
        {
            if (section.SampleAt(i / (samples - 1d)) is not { } stop || Sample(stop) is not { } color) return null;
            pixels[i * 4] = (float)color.LinearRgb.First; pixels[i * 4 + 1] = (float)color.LinearRgb.Second;
            pixels[i * 4 + 2] = (float)color.LinearRgb.Third; pixels[i * 4 + 3] = 1;
        }
        return pixels;
    }
}
