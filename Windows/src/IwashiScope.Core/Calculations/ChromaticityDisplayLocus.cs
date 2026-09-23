using IwashiScope.Core.Models;

namespace IwashiScope.Core.Calculations;

/// <summary>Uniform-grid, shape-preserving cubic Hermite interpolation for display only. No extrapolation.</summary>
public sealed class DisplaySpectralInterpolator
{
    private readonly double[] _values, _slopes;
    private readonly double _start, _interval;
    public DisplaySpectralInterpolator(IReadOnlyList<double> values, double start, double interval)
    {
        if (values.Count < 2 || values.Any(v => !double.IsFinite(v)) || !double.IsFinite(start) || !double.IsFinite(interval) || interval <= 0)
            throw new ArgumentException("Invalid display interpolation data.");
        _values = values.ToArray(); _start = start; _interval = interval; _slopes = new double[values.Count];
        var differences = values.Zip(values.Skip(1)).Select(p => p.Second - p.First).ToArray();
        if (differences.Length == 1) { _slopes[0] = _slopes[1] = differences[0]; return; }
        _slopes[0] = EndSlope(differences[0], differences[1]);
        _slopes[^1] = EndSlope(differences[^1], differences[^2]);
        for (var i = 1; i < values.Count - 1; i++)
            if (SameSign(differences[i - 1], differences[i])) _slopes[i] = 2 / (1 / differences[i - 1] + 1 / differences[i]);
    }
    public double? Value(double wavelength)
    {
        var position = (wavelength - _start) / _interval;
        if (!double.IsFinite(position) || position < 0 || position > _values.Length - 1) return null;
        var i = (int)Math.Floor(position); var t = position - i;
        if (t == 0) return _values[i];
        var t2 = t * t; var t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * _values[i] + (t3 - 2 * t2 + t) * _slopes[i]
            + (-2 * t3 + 3 * t2) * _values[i + 1] + (t3 - t2) * _slopes[i + 1];
    }
    private static bool SameSign(double a, double b) => (a > 0 && b > 0) || (a < 0 && b < 0);
    private static double EndSlope(double adjacent, double next)
    {
        var candidate = (3 * adjacent - next) / 2;
        if (!SameSign(candidate, adjacent)) return 0;
        return !SameSign(adjacent, next) && Math.Abs(candidate) > 3 * Math.Abs(adjacent) ? 3 * adjacent : candidate;
    }
}

public static class ChromaticityDisplayLocus
{
    public sealed record Sample(double Wavelength, Vector3 Lms, Vector3 XyzF, Vector3 Xyz1931, PhysiologicalPoint Point);
    public static IReadOnlyList<Sample> Samples { get; } = Create();
    public static IReadOnlyList<PhysiologicalPoint> Points { get; } = Samples.Select(s => s.Point).ToArray();
    private static Sample[] Create()
    {
        var l = new DisplaySpectralInterpolator(Cie2006LmsData.Long, 390, 5);
        var m = new DisplaySpectralInterpolator(Cie2006LmsData.Medium, 390, 5);
        var s = new DisplaySpectralInterpolator(Cie2006LmsData.Short, 390, 5);
        var x = new DisplaySpectralInterpolator(ColorRenderingReferenceData.XBar, ColorRenderingReferenceData.StartWavelength, ColorRenderingReferenceData.Interval);
        var y = new DisplaySpectralInterpolator(ColorRenderingReferenceData.YBar, ColorRenderingReferenceData.StartWavelength, ColorRenderingReferenceData.Interval);
        var z = new DisplaySpectralInterpolator(ColorRenderingReferenceData.ZBar, ColorRenderingReferenceData.StartWavelength, ColorRenderingReferenceData.Interval);
        return Enumerable.Range(390, 441).Select(w =>
        {
            var lms = new Vector3(l.Value(w)!.Value, m.Value(w)!.Value, s.Value(w) ?? 0);
            var xyz = Cie2006Chromaticity.XyzF(lms);
            return new Sample(w, lms, xyz, new(x.Value(w)!.Value, y.Value(w)!.Value, z.Value(w)!.Value), Cie2006Chromaticity.Point(xyz)!.Value);
        }).ToArray();
    }
}
