using IwashiScope.Core.Models;

namespace IwashiScope.Core.Calculations;

public readonly record struct PhysiologicalPoint(double X, double Y)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}

public enum PhysiologicalCone { Long, Medium, Short }
public enum ConfusionColorMode { C, P, D, T }
public enum ChromaticityError { InsufficientSpectrum, InvalidSpectrum, ZeroSignal }
public sealed record ChromaticitySegment(PhysiologicalPoint Start, PhysiologicalPoint End)
{
    public PhysiologicalPoint At(double t) => new(Start.X + t * (End.X - Start.X), Start.Y + t * (End.Y - Start.Y));
}
public sealed record PhysiologicalMeasurement(Vector3 Lms, Vector3 WhiteLms, Vector3 XyzF, PhysiologicalPoint Point, WavelengthRange Range);

/// <summary>Separate D50 reference display path. Existing measurement XYZ/Lab and appearance calculations are unchanged.</summary>
public static class Cie2006Chromaticity
{
    public static IReadOnlyList<double> Sensitivities(PhysiologicalCone cone) => cone switch
    {
        PhysiologicalCone.Long => Cie2006LmsData.Long,
        PhysiologicalCone.Medium => Cie2006LmsData.Medium,
        _ => Cie2006LmsData.Short,
    };

    // CIE 170-2:2015 / Stockman (2019), Eq. 4. Raw channel gains are retained.
    public static Vector3 XyzF(Vector3 lms) => new(
        1.94735469 * lms.First - 1.41445123 * lms.Second + 0.36476327 * lms.Third,
        0.68990272 * lms.First + 0.34832189 * lms.Second, 1.93485343 * lms.Third);

    public static PhysiologicalPoint? Point(Vector3 xyz)
    {
        var sum = xyz.First + xyz.Second + xyz.Third;
        if (!xyz.IsFinite || !double.IsFinite(sum) || sum == 0) return null;
        var result = new PhysiologicalPoint(xyz.First / sum, xyz.Second / sum);
        return result.IsFinite ? result : null;
    }

    public static double Sensitivity(IReadOnlyList<double> values, double wavelength)
    {
        var position = (wavelength - Cie2006LmsData.FirstWavelength) / Cie2006LmsData.Interval;
        if (!double.IsFinite(position) || position < 0 || position > values.Count - 1) return 0;
        var i = (int)Math.Floor(position);
        return values[i] + (values[Math.Min(i + 1, values.Count - 1)] - values[i]) * (position - i);
    }

    public static Vector3? SpectralLms(double wavelength) => double.IsFinite(wavelength) && wavelength >= 390 && wavelength <= 830
        ? new(Sensitivity(Cie2006LmsData.Long, wavelength), Sensitivity(Cie2006LmsData.Medium, wavelength), Sensitivity(Cie2006LmsData.Short, wavelength)) : null;

    public static PhysiologicalPoint CopunctalPoint(PhysiologicalCone cone) => Point(XyzF(cone switch
    {
        PhysiologicalCone.Long => new(1, 0, 0), PhysiologicalCone.Medium => new(0, 1, 0), _ => new(0, 0, 1),
    }))!.Value;

    public static ChromaticitySegment? ConfusionLine(PhysiologicalPoint point, PhysiologicalCone cone)
    {
        if (!point.IsFinite) return null;
        var pole = CopunctalPoint(cone);
        var dx = pole.X - point.X; var dy = pole.Y - point.Y;
        if (Math.Sqrt(dx * dx + dy * dy) <= 1e-12) return null;
        var lower = double.NegativeInfinity; var upper = double.PositiveInfinity;
        foreach (var (origin, direction, maximum) in new[] { (point.X, dx, 0.8), (point.Y, dy, 0.9) })
        {
            if (Math.Abs(direction) < 1e-12) { if (origin < 0 || origin > maximum) return null; }
            else
            {
                var t1 = -origin / direction; var t2 = (maximum - origin) / direction;
                lower = Math.Max(lower, Math.Min(t1, t2)); upper = Math.Min(upper, Math.Max(t1, t2));
            }
        }
        return double.IsFinite(lower) && double.IsFinite(upper) && lower < upper
            ? new(new(point.X + lower * dx, point.Y + lower * dy), new(point.X + upper * dx, point.Y + upper * dy)) : null;
    }

    public static PhysiologicalMeasurement? Measure(SpotMeasurement? measurement, out ChromaticityError? error)
    {
        error = ChromaticityError.InsufficientSpectrum;
        if (measurement?.Mode != MeasurementMode.Reflectance || measurement.Spectrum.Count < 2) return null;
        error = ChromaticityError.InvalidSpectrum;
        if (measurement.Spectrum.Any(s => !double.IsFinite(s.Wavelength) || !double.IsFinite(s.Value))) return null;
        var reflectance = measurement.Spectrum.OrderBy(s => s.Wavelength).Select(s => s with { Value = Math.Max(0, s.Value) / 100 }).ToArray();
        if (reflectance.Zip(reflectance.Skip(1)).Any(p => p.First.Wavelength >= p.Second.Wavelength)) return null;
        var illuminant = CieReferenceIlluminants.Samples(CieReferenceIlluminant.D50);
        var lower = Math.Max(390, Math.Max(reflectance[0].Wavelength, illuminant[0].Wavelength));
        var upper = Math.Min(830, Math.Min(reflectance[^1].Wavelength, illuminant[^1].Wavelength));
        if (lower >= upper) { error = ChromaticityError.InsufficientSpectrum; return null; }
        var integrals = Enum.GetValues<PhysiologicalCone>().Select(c => Integrate(c, reflectance, illuminant, lower, upper)).ToArray();
        var whiteY = XyzF(new(integrals[0].White, integrals[1].White, integrals[2].White)).Second;
        if (!double.IsFinite(whiteY) || whiteY <= 0) return null;
        var normalization = 100 / whiteY;
        var lms = new Vector3(integrals[0].Object * normalization, integrals[1].Object * normalization, integrals[2].Object * normalization);
        var xyz = XyzF(lms);
        if (!xyz.IsFinite) return null;
        if (xyz.First == 0 && xyz.Second == 0 && xyz.Third == 0) { error = ChromaticityError.ZeroSignal; return null; }
        if (Point(xyz) is not { } point) return null;
        error = null;
        var whiteLms = new Vector3(integrals[0].White * normalization, integrals[1].White * normalization, integrals[2].White * normalization);
        return new(lms, whiteLms, xyz, point, new(lower, upper));
    }

    private static (double Object, double White) Integrate(PhysiologicalCone cone, IReadOnlyList<SpectralSample> reflectance,
        IReadOnlyList<SpectralSample> illuminant, double lower, double upper)
    {
        var values = Sensitivities(cone);
        upper = Math.Min(upper, 390 + (values.Count - 1) * 5);
        if (lower >= upper) return (0, 0);
        var nodes = new[] { lower, upper }.Concat(reflectance.Select(s => s.Wavelength)).Concat(illuminant.Select(s => s.Wavelength))
            .Concat(Enumerable.Range(0, values.Count).Select(i => 390d + i * 5)).Where(w => w >= lower && w <= upper).Distinct().Order().ToArray();
        double obj = 0, white = 0;
        for (var i = 0; i < nodes.Length - 1; i++)
        {
            var a = nodes[i]; var b = nodes[i + 1];
            // The product of three linear factors is cubic: Simpson is exact between merged knots.
            foreach (var (w, weight) in new[] { (a, 1d), ((a + b) / 2, 4d), (b, 1d) })
            {
                var weighted = Interpolate(illuminant, w) * Sensitivity(values, w) * (b - a) * weight / 6;
                white += weighted; obj += weighted * Interpolate(reflectance, w);
            }
        }
        return (obj, white);
    }

    internal static double Interpolate(IReadOnlyList<SpectralSample> values, double w)
    {
        var lower = 0; var upper = values.Count - 1;
        while (lower + 1 < upper) { var mid = (lower + upper) / 2; if (values[mid].Wavelength <= w) lower = mid; else upper = mid; }
        var fraction = (w - values[lower].Wavelength) / (values[upper].Wavelength - values[lower].Wavelength);
        return values[lower].Value + (values[upper].Value - values[lower].Value) * fraction;
    }
}

/// <summary>Peak-normalized display curves. Never used for colorimetry or y-axis scale selection.</summary>
public static class Cie2006LmsReference
{
    public const double StrokeOpacity = 0.4;
    public sealed record Curve(PhysiologicalCone Cone, IReadOnlyList<SpectralSample> Samples);
    public static readonly IReadOnlyList<Curve> FullCurves = Enum.GetValues<PhysiologicalCone>().Select(c =>
    {
        var values = Cie2006Chromaticity.Sensitivities(c); var peak = values.Max();
        return new Curve(c, values.Select((v, i) => new SpectralSample(i, 390 + i * 5, v / peak)).ToArray());
    }).ToArray();

    public static IReadOnlyList<Curve> Curves(double start, double end)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start >= end) return [];
        return FullCurves.Select(c =>
        {
            var lower = Math.Max(start, c.Samples[0].Wavelength); var upper = Math.Min(end, c.Samples[^1].Wavelength);
            return lower >= upper ? null : new Curve(c.Cone, new[] { lower }.Concat(c.Samples.Where(s => s.Wavelength > lower && s.Wavelength < upper).Select(s => s.Wavelength)).Append(upper)
                .Select((w, i) => new SpectralSample(i, w, Cie2006Chromaticity.Interpolate(c.Samples, w))).ToArray());
        }).OfType<Curve>().ToArray();
    }
}
