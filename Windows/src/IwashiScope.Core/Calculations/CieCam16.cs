using IwashiScope.Core.Models;

namespace IwashiScope.Core.Calculations;

public enum ColorAppearanceError
{
    InvalidViewingConditions,
    InvalidStimulus,
    OutsideModelDomain,
    InsufficientSpectrum,
}

public sealed class ColorAppearanceException(ColorAppearanceError error) : Exception(error switch
{
    ColorAppearanceError.InvalidViewingConditions => "The color appearance viewing conditions are invalid.",
    ColorAppearanceError.InvalidStimulus => "The colorimetric input for color appearance is invalid.",
    ColorAppearanceError.OutsideModelDomain => "This color is outside the appearance model's calculation domain.",
    _ => "There is insufficient spectral coverage for this comparison.",
})
{
    public ColorAppearanceError Error { get; } = error;
}

/// <summary>White XYZ and Yb use Yw = 100. LA is absolute luminance, not XYZ Y.</summary>
public sealed record AppearanceViewingConditions(
    Vector3 WhiteXyz,
    double AdaptingLuminance = 20,
    double BackgroundLuminanceFactor = 20,
    double SurroundF = 1,
    double SurroundC = 0.69,
    double SurroundNc = 1,
    double? AdaptationDegreeOverride = null);

public sealed record AppearanceCorrelates(
    double Lightness,
    double Brightness,
    double Chroma,
    double Colorfulness,
    double Saturation,
    double? Hue)
{
    public static readonly AppearanceCorrelates Black = new(0, 0, 0, 0, 0, null);
}

/// <summary>
/// CIE 248:2022, Gao et al. doi:10.1002/col.22959 Appendix A.
/// Uses the CIECAM16 response extensions, not the older CAM16 (2017) response.
/// Matches the macOS implementation and its independent Colour 0.4.7 fixtures.
/// </summary>
public sealed class CieCam16
{
    public AppearanceViewingConditions Conditions { get; }
    public double AdaptationDegree { get; }
    private readonly Vector3 _gains;
    private readonly double _luminanceFactor;
    private readonly double _luminanceRoot;
    private readonly double _nbb;
    private readonly double _z;
    private readonly double _whiteResponse;
    private readonly double _chromaFactor;
    private readonly double _upperResponseLimit;

    public CieCam16(AppearanceViewingConditions conditions)
    {
        var white = conditions.WhiteXyz;
        if (white is not { IsFinite: true } || white.First <= 0 || white.Third <= 0 ||
            Math.Abs(white.Second - 100) >= 1e-9 ||
            !Finite(conditions.AdaptingLuminance, conditions.BackgroundLuminanceFactor,
                conditions.SurroundF, conditions.SurroundC, conditions.SurroundNc) ||
            conditions.AdaptingLuminance <= 0 || conditions.BackgroundLuminanceFactor <= 0 ||
            conditions.SurroundF <= 0 || conditions.SurroundC <= 0 || conditions.SurroundNc <= 0)
        {
            throw new ColorAppearanceException(ColorAppearanceError.InvalidViewingConditions);
        }
        var degree = conditions.AdaptationDegreeOverride ?? Math.Clamp(
            conditions.SurroundF * (1 - Math.Exp((-conditions.AdaptingLuminance - 42) / 92) / 3.6), 0, 1);
        var coneWhite = ConeResponse(white);
        if (!double.IsFinite(degree) || degree is < 0 or > 1 ||
            coneWhite.First <= 0 || coneWhite.Second <= 0 || coneWhite.Third <= 0)
        {
            throw new ColorAppearanceException(ColorAppearanceError.InvalidViewingConditions);
        }
        var gains = Map(coneWhite, value => degree * 100 / value + 1 - degree);
        var adaptedWhite = Product(coneWhite, gains);
        var la = conditions.AdaptingLuminance;
        var k4 = Math.Pow(1 / (5 * la + 1), 4);
        var fl = 0.2 * k4 * 5 * la + 0.1 * Math.Pow(1 - k4, 2) * Math.Cbrt(5 * la);
        var n = conditions.BackgroundLuminanceFactor / 100;
        var nbb = 0.725 * Math.Pow(n, -0.2);
        // The adopted white uses the original compression (Appendix A, step 0).
        var compressedWhite = Map(adaptedWhite, value => Response(value, fl) + 0.1);
        var aw = AchromaticResponse(compressedWhite, nbb);
        if (!Finite(fl, aw) || fl <= 0 || aw <= 0)
        {
            throw new ColorAppearanceException(ColorAppearanceError.InvalidViewingConditions);
        }
        Conditions = conditions;
        AdaptationDegree = degree;
        _gains = gains;
        _luminanceFactor = fl;
        _luminanceRoot = Math.Pow(fl, 0.25);
        _nbb = nbb;
        _z = 1.48 + Math.Sqrt(n);
        _whiteResponse = aw;
        _chromaFactor = Math.Pow(1.64 - Math.Pow(0.29, n), 0.73);
        _upperResponseLimit = Math.Max(150,
            Math.Max(adaptedWhite.First, Math.Max(adaptedWhite.Second, adaptedWhite.Third)));
    }

    public AppearanceCorrelates Forward(Vector3 xyz)
    {
        if (xyz is not { IsFinite: true } || xyz.Second < 0)
        {
            throw new ColorAppearanceException(ColorAppearanceError.InvalidStimulus);
        }
        if (xyz == new Vector3(0, 0, 0)) return AppearanceCorrelates.Black;
        var adapted = Product(ConeResponse(xyz), _gains);
        var compressed = Map(adapted, CompressedResponse);
        var a = compressed.First - 12 * compressed.Second / 11 + compressed.Third / 11;
        var b = (compressed.First + compressed.Second - 2 * compressed.Third) / 9;
        var angle = Math.Atan2(b, a);
        var hue = (angle * 180 / Math.PI + 360) % 360;
        var achromatic = AchromaticResponse(compressed, _nbb);
        var denominator = compressed.First + compressed.Second + 21 * compressed.Third / 20;
        if (achromatic <= 0 || denominator <= 0)
        {
            throw new ColorAppearanceException(ColorAppearanceError.OutsideModelDomain);
        }
        var j = 100 * Math.Pow(achromatic / _whiteResponse, Conditions.SurroundC * _z);
        var q = 4 / Conditions.SurroundC * Math.Sqrt(j / 100) * (_whiteResponse + 4) * _luminanceRoot;
        var eccentricity = (Math.Cos(angle + 2) + 3.8) / 4;
        var t = 50_000 / 13.0 * Conditions.SurroundNc * _nbb
            * eccentricity * Hypot(a, b) / denominator;
        var c = Math.Pow(t, 0.9) * Math.Sqrt(j / 100) * _chromaFactor;
        var m = c * _luminanceRoot;
        var s = 100 * Math.Sqrt(m / q);
        if (!Finite(j, q, c, m, s, hue))
        {
            throw new ColorAppearanceException(ColorAppearanceError.OutsideModelDomain);
        }
        return new AppearanceCorrelates(j, q, c, m, s, c > 1e-10 ? hue : null);
    }

    public Vector3 Inverse(AppearanceCorrelates appearance) =>
        Inverse(appearance.Brightness, appearance.Colorfulness, appearance.Hue);

    /// <summary>Reproduces the source's Q, M and h as one set in these conditions.</summary>
    public Vector3 Inverse(double brightness, double colorfulness, double? hue)
    {
        var q = brightness;
        var m = colorfulness;
        if (!Finite(q, m) || q < 0 || m < 0 || hue is { } h && !double.IsFinite(h))
        {
            throw new ColorAppearanceException(ColorAppearanceError.InvalidStimulus);
        }
        if (q == 0)
        {
            if (m != 0) throw new ColorAppearanceException(ColorAppearanceError.OutsideModelDomain);
            return new Vector3(0, 0, 0);
        }
        if (m > 1e-10 && hue is null)
        {
            throw new ColorAppearanceException(ColorAppearanceError.InvalidStimulus);
        }
        var rootJ = q * Conditions.SurroundC / (4 * (_whiteResponse + 4) * _luminanceRoot);
        var c = m / _luminanceRoot;
        var t = m <= 1e-10 ? 0 : Math.Pow(c / (rootJ * _chromaFactor), 1 / 0.9);
        var angle = (hue ?? 0) * Math.PI / 180;
        var eccentricity = (Math.Cos(angle + 2) + 3.8) / 4;
        var achromatic = _whiteResponse * Math.Pow(rootJ * rootJ, 1 / (Conditions.SurroundC * _z));
        var p2 = achromatic / _nbb + 0.305;
        var p1 = 50_000 / 13.0 * Conditions.SurroundNc * _nbb * eccentricity;
        var denominator = 23 * p1 + t * (11 * Math.Cos(angle) + 108 * Math.Sin(angle));
        if (!double.IsFinite(denominator) || denominator <= 0)
        {
            throw new ColorAppearanceException(ColorAppearanceError.OutsideModelDomain);
        }
        var radius = 23 * p2 * t / denominator;
        var a = radius * Math.Cos(angle);
        var b = radius * Math.Sin(angle);
        var compressed = new Vector3(
            (460 * p2 + 451 * a + 288 * b) / 1403,
            (460 * p2 - 891 * a - 261 * b) / 1403,
            (460 * p2 - 220 * a - 6300 * b) / 1403);
        var adapted = Map(compressed, InverseCompressedResponse);
        var cones = new Vector3(adapted.First / _gains.First,
            adapted.Second / _gains.Second, adapted.Third / _gains.Third);
        var xyz = XyzFromCones(cones);
        if (!xyz.IsFinite) throw new ColorAppearanceException(ColorAppearanceError.OutsideModelDomain);
        return xyz;
    }

    private double CompressedResponse(double value)
    {
        const double lower = 0.26;
        if (value <= lower) return Response(lower, _luminanceFactor) * value / lower + 0.1;
        if (value >= _upperResponseLimit)
            return Response(_upperResponseLimit, _luminanceFactor)
                + ResponseDerivative(_upperResponseLimit) * (value - _upperResponseLimit) + 0.1;
        return Response(value, _luminanceFactor) + 0.1;
    }

    private double InverseCompressedResponse(double compressed)
    {
        var value = compressed - 0.1;
        var lower = Response(0.26, _luminanceFactor);
        var upper = Response(_upperResponseLimit, _luminanceFactor);
        if (value <= lower) return value * 0.26 / lower;
        if (value >= upper) return _upperResponseLimit + (value - upper) / ResponseDerivative(_upperResponseLimit);
        return 100 / _luminanceFactor * Math.Pow(27.13 * value / (400 - value), 1 / 0.42);
    }

    private double ResponseDerivative(double value)
    {
        var scaled = _luminanceFactor * value / 100;
        return 400 * 0.42 * 27.13 * _luminanceFactor / 100 * Math.Pow(scaled, -0.58)
            / Math.Pow(27.13 + Math.Pow(scaled, 0.42), 2);
    }

    private static double Response(double value, double fl)
    {
        var powered = Math.Pow(fl * Math.Abs(value) / 100, 0.42);
        return (value < 0 ? -1 : 1) * 400 * powered / (27.13 + powered);
    }

    private static double AchromaticResponse(Vector3 v, double nbb) =>
        (2 * v.First + v.Second + v.Third / 20 - 0.305) * nbb;

    public static Vector3 ConeResponse(Vector3 v) => new(
        0.401288 * v.First + 0.650173 * v.Second - 0.051461 * v.Third,
        -0.250268 * v.First + 1.204414 * v.Second + 0.045854 * v.Third,
        -0.002079 * v.First + 0.048952 * v.Second + 0.953127 * v.Third);

    private static Vector3 XyzFromCones(Vector3 v) => new(
        1.8620678550872327 * v.First - 1.0112546305316843 * v.Second + 0.14918677544445175 * v.Third,
        0.3875265432361372 * v.First + 0.6214474419314753 * v.Second - 0.008973985167612518 * v.Third,
        -0.01584149884933386 * v.First - 0.03412293802851557 * v.Second + 1.0499644368778496 * v.Third);

    private static Vector3 Product(Vector3 a, Vector3 b) =>
        new(a.First * b.First, a.Second * b.Second, a.Third * b.Third);
    private static Vector3 Map(Vector3 v, Func<double, double> operation) =>
        new(operation(v.First), operation(v.Second), operation(v.Third));
    private static bool Finite(params double[] values) => values.All(double.IsFinite);
    private static double Hypot(double x, double y)
    {
        var maximum = Math.Max(Math.Abs(x), Math.Abs(y));
        return maximum == 0 ? 0 : maximum * Math.Sqrt(1 + Math.Pow(Math.Min(Math.Abs(x), Math.Abs(y)) / maximum, 2));
    }
}
