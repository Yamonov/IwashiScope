using IwashiScope.Core.Models;
using static IwashiScope.Core.Calculations.ChromaticityMath;
namespace IwashiScope.Core.Calculations;

/// <summary>Named numeric rendering spaces, not the actual Windows monitor ICC profile.</summary>
public enum ChromaticityRenderingSpace { Srgb, DisplayP3 }
public static class ChromaticityDisplayColorTransform
{
    public static Vector3 XyzToSrgb(Vector3 xyz) => new(
        (12831d / 3959) * xyz.First - (329d / 214) * xyz.Second - (1974d / 3959) * xyz.Third,
        (-851781d / 878810) * xyz.First + (1648619d / 878810) * xyz.Second + (36519d / 878810) * xyz.Third,
        (705d / 12673) * xyz.First - (2585d / 12673) * xyz.Second + (705d / 667) * xyz.Third);
    public static Vector3 FromSrgb(Vector3 rgb, ChromaticityRenderingSpace space)
    {
        if (space == ChromaticityRenderingSpace.Srgb) return rgb;
        var xyz = Solve(XyzToSrgb(new(1, 0, 0)), XyzToSrgb(new(0, 1, 0)), XyzToSrgb(new(0, 0, 1)), rgb);
        // Display P3 D65 primaries. Linear transform deliberately retains unbounded candidates.
        return Solve(new(0.4865709486482162, 0.2289745640697488, 0), new(0.26566769316909306, 0.6917385218365064, 0.04511338185890264),
            new(0.1982172852343625, 0.079286914093745, 1.043944368900976), xyz);
    }
}
public sealed class ChromaticityDisplayGamut
{
    public sealed record Sample(Vector3 LinearRgb, double ChromaScale);
    public Vector3 WhiteRgb { get; }
    public ChromaticityDisplayGamut(ChromaticityRenderingSpace space, Func<Vector3, Vector3>? linearTransform = null)
    {
        var white = linearTransform != null ? linearTransform(ChromaticityBackgroundPalette.Shared.White.LinearRgb)
            : ChromaticityDisplayColorTransform.FromSrgb(ChromaticityBackgroundPalette.Shared.White.LinearRgb, space);
        if (!white.IsFinite || new[] { white.First, white.Second, white.Third }.Any(c => c <= 0 || c > 1 + 1e-6))
            throw new ArgumentException("Unsupported rendering white.");
        WhiteRgb = new(Math.Min(1, white.First), Math.Min(1, white.Second), Math.Min(1, white.Third));
    }
    public Sample? Map(Vector3 candidate, double q)
    {
        if (!candidate.IsFinite || !double.IsFinite(q) || q < 0 || q > 1) return null;
        var neutral = Scale(WhiteRgb, q); var alpha = 1d;
        for (var i = 0; i < 3; i++)
        {
            var c = Component(candidate, i); var n = Component(neutral, i);
            if (c < 0) alpha = Math.Min(alpha, n / (n - c));
            else if (c > 1) alpha = Math.Min(alpha, (1 - n) / (c - n));
        }
        double Channel(int i) => Math.Clamp(Component(neutral, i) + alpha * (Component(candidate, i) - Component(neutral, i)), 0, 1);
        return new(new(Channel(0), Channel(1), Channel(2)), alpha);
    }
}
