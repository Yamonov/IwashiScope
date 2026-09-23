using IwashiScope.Core.Models;
using static IwashiScope.Core.Calculations.ChromaticityMath;
namespace IwashiScope.Core.Calculations;

/// <summary>White-relative reference scalar, not a prediction of subjective brightness.</summary>
public sealed record ChromaticityBrightnessReference(ConfusionColorMode Mode, double RequestedLuminance)
{
    public double RelativeLuminance => Math.Min(1, RequestedLuminance);
    public bool IsDisplayLimited => RequestedLuminance > 1;
    public static ChromaticityBrightnessReference? Create(Vector3 lms, Vector3 white, ConfusionColorMode mode) =>
        Response(lms, white, mode) is { } value ? new(mode, value) : null;
    public static double? Response(Vector3 lms, Vector3 white, ConfusionColorMode mode)
    {
        if (!Nonnegative(lms) || !Nonnegative(white) || mode == ConfusionColorMode.C) return null;
        var numerator = mode switch { ConfusionColorMode.P => lms.Second, ConfusionColorMode.D => lms.First, _ => Cie2006Chromaticity.XyzF(lms).Second };
        var denominator = mode switch { ConfusionColorMode.P => white.Second, ConfusionColorMode.D => white.First, _ => Cie2006Chromaticity.XyzF(white).Second };
        var value = numerator / denominator;
        return denominator > 0 && double.IsFinite(value) ? value : null;
    }
    public static Vector3 PaletteWhiteLms { get; } = Scale(ChromaticityConfusionBand.LmsFromXyzF(ChromaticityBackgroundPalette.Shared.White.XyzF),
        100 / ChromaticityBackgroundPalette.Shared.White.XyzF.Second);
}
