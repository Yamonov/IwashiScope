using IwashiScope.Core.Models;
using static IwashiScope.Core.Calculations.ChromaticityMath;
namespace IwashiScope.Core.Calculations;

/// <summary>Decorative coordinate mapping ONLY. Not an observer conversion or the physical band palette.</summary>
public sealed class ChromaticityIllustrationBackground
{
    public const double RelativeLuminance = 0.20;
    public const double Opacity = 0.38;
    public PhysiologicalPoint WhitePoint => ChromaticityBackgroundPalette.Shared.White.Point;
    public Vector3 Neutral { get; }
    private readonly Vector3 _x, _y, _z;
    public ChromaticityIllustrationBackground(ChromaticityRenderingSpace space = ChromaticityRenderingSpace.Srgb,
        Func<Vector3, Vector3>? linearTransform = null)
    {
        var white = ChromaticityBackgroundPalette.Shared.White.XyzF;
        var sourceWhite = new Vector3(white.First / white.Second, 1, white.Third / white.Second);
        var d65 = new Vector3(0.3127 / 0.3290, 1, (1 - 0.3127 - 0.3290) / 0.3290);
        var transform = linearTransform ?? (rgb => ChromaticityDisplayColorTransform.FromSrgb(rgb, space));
        Vector3 Column(Vector3 coordinate) => transform(
            ChromaticityDisplayColorTransform.XyzToSrgb(BradfordChromaticAdaptation.Adapt(coordinate, sourceWhite, d65)!));
        _x = Column(new(1, 0, 0)); _y = Column(new(0, 1, 0)); _z = Column(new(0, 0, 1));
        Neutral = Scale(Combine(_x, _y, _z, sourceWhite), RelativeLuminance);
    }
    public Vector3? ColorAt(PhysiologicalPoint point)
    {
        if (!point.IsFinite || point.X < 0 || point.X > 0.8 || point.Y <= 0 || point.Y > 0.9) return null;
        var xyz = new Vector3(point.X / point.Y, 1, (1 - point.X - point.Y) / point.Y);
        var candidate = Scale(Combine(_x, _y, _z, xyz), RelativeLuminance);
        var delta = Add(candidate, Scale(Neutral, -1));
        static double Distance(double d, double n) => d >= 0 ? d / (1 - n) : -d / n;
        var r = Distance(delta.First, Neutral.First); var g = Distance(delta.Second, Neutral.Second); var b = Distance(delta.Third, Neutral.Third);
        if (!double.IsFinite(r) || !double.IsFinite(g) || !double.IsFinite(b)) return null;
        var inverse = 1 / Math.Max(1, Math.Max(r, Math.Max(g, b)));
        static double Eighth(double v) { var s = v * v; var f = s * s; return f * f; }
        var sum = Eighth(inverse) + Eighth(r * inverse) + Eighth(g * inverse) + Eighth(b * inverse);
        var alpha = inverse / Math.Sqrt(Math.Sqrt(Math.Sqrt(sum)));
        return new(Math.Clamp(Neutral.First + alpha * delta.First, 0, 1), Math.Clamp(Neutral.Second + alpha * delta.Second, 0, 1), Math.Clamp(Neutral.Third + alpha * delta.Third, 0, 1));
    }
    public double IllustrativeLuminance(Vector3 rgb) => Solve(_x, _y, _z, rgb).Second;
    public float[]? Raster(int width = 1024, int height = 1152)
    {
        if (width < 1 || height < 1 || width > 4096 || height > 4096) return null;
        var pixels = new float[width * height * 4];
        for (var row = 0; row < height; row++)
        for (var column = 0; column < width; column++)
        {
            var point = new PhysiologicalPoint((column + 0.5) / width * 0.8, (1 - (row + 0.5) / height) * 0.9);
            if (ColorAt(point) is not { } rgb) return null;
            var i = (row * width + column) * 4;
            pixels[i] = (float)rgb.First; pixels[i + 1] = (float)rgb.Second; pixels[i + 2] = (float)rgb.Third; pixels[i + 3] = 1;
        }
        return pixels;
    }
}
