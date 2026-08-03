using System.Globalization;
using System.Reflection;
using IwashiScope.Core.Models;

namespace IwashiScope.Core.Calculations;

public sealed record MunsellNotation(
    double? Hue,
    string? HueDesignator,
    double Value,
    double Chroma)
{
    public string Formatted => Hue is { } hue && HueDesignator is { } designator
        ? $"{Number(hue)}{designator} {Number(Value)}/{Number(Chroma)}"
        : $"N {Number(Value)}";

    private static string Number(double value)
    {
        var rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        return Math.Abs(rounded - Math.Round(rounded)) < 0.000_001
            ? Math.Round(rounded).ToString("0", CultureInfo.InvariantCulture)
            : rounded.ToString("0.0", CultureInfo.InvariantCulture);
    }
}

public static class MunsellConverter
{
    private readonly record struct Tristimulus(double X, double Y, double Z);
    private readonly record struct ColorimetrySample(
        double Wavelength,
        double Illuminant,
        double XBar,
        double YBar,
        double ZBar);
    private readonly record struct ReflectancePoint(double Wavelength, double Value);
    private readonly record struct IntegrationPoint(
        double Wavelength,
        double X,
        double Y,
        double Z,
        double Normalization);
    private readonly record struct LabPoint(double L, double A, double B)
    {
        public double Chroma => Math.Sqrt(A * A + B * B);
        public bool IsFinite => double.IsFinite(L) && double.IsFinite(A) && double.IsFinite(B);
    }
    private sealed record RenotationSample(
        double AstmHue,
        double Value,
        double Chroma,
        LabPoint Lab);

    private static readonly Tristimulus IlluminantCWhite =
        new(0.9807059717, 1, 1.1822494939);
    private static readonly Lazy<IReadOnlyList<RenotationSample>> RenotationSamples =
        new(LoadRenotationSamples);
    private static readonly Lazy<IReadOnlyList<ColorimetrySample>> ColorimetrySamples =
        new(LoadColorimetrySamples);
    private const int NeighborCount = 12;
    private const double NeutralChromaThreshold = 0.5;

    public static MunsellNotation? Convert(IReadOnlyList<SpectralSample> reflectanceSpectrum)
    {
        var xyz = IlluminantCXYZ(reflectanceSpectrum);
        if (xyz is null || RenotationSamples.Value.Count == 0)
        {
            return null;
        }

        var normalizedXyz = new Tristimulus(
            xyz.First / 100,
            xyz.Second / 100,
            xyz.Third / 100);
        var target = LabFromXyz(normalizedXyz, IlluminantCWhite);
        if (!target.IsFinite || !xyz.IsFinite)
        {
            return null;
        }

        var value = MunsellValue(xyz.Second);
        var nearbyByValue = RenotationSamples.Value
            .Where(sample => Math.Abs(sample.Value - value) <= 1.1)
            .ToArray();
        var pool = nearbyByValue.Length > 0 ? nearbyByValue : RenotationSamples.Value;
        var neighbors = pool
            .Select(sample =>
            {
                var deltaL = sample.Lab.L - target.L;
                var deltaA = sample.Lab.A - target.A;
                var deltaB = sample.Lab.B - target.B;
                return (
                    Sample: sample,
                    SquaredDistance: deltaL * deltaL + deltaA * deltaA + deltaB * deltaB);
            })
            .OrderBy(candidate => candidate.SquaredDistance)
            .Take(NeighborCount)
            .ToArray();
        if (neighbors.Length == 0)
        {
            return null;
        }

        var sine = 0d;
        var cosine = 0d;
        var chromaScale = 0d;
        var totalWeight = 0d;
        foreach (var neighbor in neighbors)
        {
            var weight = 1 / (neighbor.SquaredDistance + 0.000_001);
            var angle = neighbor.Sample.AstmHue * 2 * Math.PI / 100;
            sine += weight * Math.Sin(angle);
            cosine += weight * Math.Cos(angle);
            chromaScale += weight * neighbor.Sample.Chroma /
                Math.Max(neighbor.Sample.Lab.Chroma, 0.000_001);
            totalWeight += weight;
        }
        if (!double.IsFinite(totalWeight) || totalWeight <= 0)
        {
            return null;
        }

        var chroma = Math.Clamp(target.Chroma * chromaScale / totalWeight, 0, 50);
        if (chroma < NeutralChromaThreshold)
        {
            return new MunsellNotation(null, null, value, 0);
        }

        var astmHue = Math.Atan2(sine, cosine) * 100 / (2 * Math.PI);
        if (astmHue <= 0)
        {
            astmHue += 100;
        }
        var (prefix, designator) = HueNotation(astmHue);
        return new MunsellNotation(prefix, designator, value, chroma);
    }

    public static Vector3? IlluminantCXYZ(IReadOnlyList<SpectralSample> reflectanceSpectrum)
    {
        var reflectancePoints = NormalizedReflectancePoints(reflectanceSpectrum);
        var references = ColorimetrySamples.Value;
        if (reflectancePoints.Count < 2 || references.Count == 0)
        {
            return null;
        }

        var lowerBound = Math.Max(
            reflectancePoints[0].Wavelength,
            references[0].Wavelength);
        var upperBound = Math.Min(
            reflectancePoints[^1].Wavelength,
            references[^1].Wavelength);
        if (upperBound - lowerBound < 1)
        {
            return null;
        }

        var wavelengths = new List<double> { lowerBound };
        wavelengths.AddRange(references
            .Select(sample => sample.Wavelength)
            .Where(wavelength => wavelength > lowerBound && wavelength < upperBound));
        wavelengths.Add(upperBound);

        var xIntegral = 0d;
        var yIntegral = 0d;
        var zIntegral = 0d;
        var normalizationIntegral = 0d;
        IntegrationPoint? previous = null;
        foreach (var wavelength in wavelengths)
        {
            var reflectance = InterpolateReflectance(reflectancePoints, wavelength);
            var reference = InterpolateColorimetry(wavelength);
            if (reflectance is null || reference is null)
            {
                return null;
            }

            var scaledReflectance = Math.Max(reflectance.Value / 100, 0);
            var current = new IntegrationPoint(
                wavelength,
                scaledReflectance * reference.Value.Illuminant * reference.Value.XBar,
                scaledReflectance * reference.Value.Illuminant * reference.Value.YBar,
                scaledReflectance * reference.Value.Illuminant * reference.Value.ZBar,
                reference.Value.Illuminant * reference.Value.YBar);
            if (previous is { } prior)
            {
                var interval = current.Wavelength - prior.Wavelength;
                xIntegral += interval * (prior.X + current.X) / 2;
                yIntegral += interval * (prior.Y + current.Y) / 2;
                zIntegral += interval * (prior.Z + current.Z) / 2;
                normalizationIntegral += interval
                    * (prior.Normalization + current.Normalization) / 2;
            }
            previous = current;
        }

        if (!double.IsFinite(normalizationIntegral) || normalizationIntegral <= 0)
        {
            return null;
        }
        var scale = 100 / normalizationIntegral;
        var result = new Vector3(
            xIntegral * scale,
            yIntegral * scale,
            zIntegral * scale);
        return result.IsFinite ? result : null;
    }

    private static IReadOnlyList<ReflectancePoint> NormalizedReflectancePoints(
        IReadOnlyList<SpectralSample> spectrum)
    {
        var sorted = spectrum
            .Where(sample => double.IsFinite(sample.Wavelength) && double.IsFinite(sample.Value))
            .OrderBy(sample => sample.Wavelength);
        var points = new List<ReflectancePoint>();
        foreach (var sample in sorted)
        {
            var point = new ReflectancePoint(sample.Wavelength, sample.Value);
            if (points.Count > 0 &&
                Math.Abs(points[^1].Wavelength - point.Wavelength) < 0.000_000_001)
            {
                points[^1] = point;
            }
            else
            {
                points.Add(point);
            }
        }
        return points;
    }

    private static double? InterpolateReflectance(
        IReadOnlyList<ReflectancePoint> points,
        double wavelength)
    {
        if (points.Count == 0 ||
            wavelength < points[0].Wavelength ||
            wavelength > points[^1].Wavelength)
        {
            return null;
        }
        var lower = 0;
        var upper = points.Count - 1;
        while (lower + 1 < upper)
        {
            var midpoint = (lower + upper) / 2;
            if (points[midpoint].Wavelength <= wavelength)
            {
                lower = midpoint;
            }
            else
            {
                upper = midpoint;
            }
        }
        var first = points[lower];
        if (Math.Abs(first.Wavelength - wavelength) < 0.000_000_001)
        {
            return first.Value;
        }
        var second = points[upper];
        var interval = second.Wavelength - first.Wavelength;
        if (interval <= 0)
        {
            return first.Value;
        }
        var fraction = (wavelength - first.Wavelength) / interval;
        return first.Value + fraction * (second.Value - first.Value);
    }

    private static ColorimetrySample? InterpolateColorimetry(double wavelength)
    {
        var samples = ColorimetrySamples.Value;
        if (samples.Count == 0 ||
            wavelength < samples[0].Wavelength ||
            wavelength > samples[^1].Wavelength)
        {
            return null;
        }
        var position = wavelength - samples[0].Wavelength;
        var lowerIndex = Math.Clamp((int)Math.Floor(position), 0, samples.Count - 1);
        var lower = samples[lowerIndex];
        if (lowerIndex == samples.Count - 1 ||
            Math.Abs(lower.Wavelength - wavelength) < 0.000_000_001)
        {
            return lower;
        }
        var upper = samples[lowerIndex + 1];
        var fraction = (wavelength - lower.Wavelength)
            / (upper.Wavelength - lower.Wavelength);
        return new ColorimetrySample(
            wavelength,
            lower.Illuminant + fraction * (upper.Illuminant - lower.Illuminant),
            lower.XBar + fraction * (upper.XBar - lower.XBar),
            lower.YBar + fraction * (upper.YBar - lower.YBar),
            lower.ZBar + fraction * (upper.ZBar - lower.ZBar));
    }

    private static (double Prefix, string Designator) HueNotation(double astmHue)
    {
        string[] designators = ["R", "YR", "Y", "GY", "G", "BG", "B", "PB", "P", "RP"];
        var normalized = astmHue % 100;
        if (normalized <= 0) normalized += 100;
        var segment = (int)Math.Floor(normalized / 10);
        var prefix = normalized - segment * 10;
        if (prefix < 0.000_001)
        {
            segment = (segment + designators.Length - 1) % designators.Length;
            prefix = 10;
        }
        else
        {
            segment %= designators.Length;
        }
        return (prefix, designators[segment]);
    }

    private static double MunsellValue(double luminance)
    {
        var target = Math.Clamp(luminance, 0, 100);
        var lower = 0d;
        var upper = 10d;
        for (var iteration = 0; iteration < 60; iteration++)
        {
            var candidate = (lower + upper) / 2;
            if (AstmLuminance(candidate) < target)
            {
                lower = candidate;
            }
            else
            {
                upper = candidate;
            }
        }
        return (lower + upper) / 2;
    }

    private static double AstmLuminance(double value) =>
        0.00081939 * Math.Pow(value, 5) -
        0.020484 * Math.Pow(value, 4) +
        0.23352 * Math.Pow(value, 3) -
        0.22533 * Math.Pow(value, 2) +
        1.1914 * value;

    private static LabPoint LabFromXyz(Tristimulus xyz, Tristimulus white)
    {
        var fx = LabPivot(xyz.X / white.X);
        var fy = LabPivot(xyz.Y / white.Y);
        var fz = LabPivot(xyz.Z / white.Z);
        return new LabPoint(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    private static double LabPivot(double value)
    {
        const double epsilon = 216.0 / 24389.0;
        const double kappa = 24389.0 / 27.0;
        return value > epsilon ? Math.Cbrt(value) : (kappa * value + 16) / 116;
    }

    private static IReadOnlyList<RenotationSample> LoadRenotationSamples()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("IwashiScope.MunsellRenotationAll.csv");
        if (stream is null)
        {
            return [];
        }
        using var reader = new StreamReader(stream);
        _ = reader.ReadLine();
        var samples = new List<RenotationSample>(4_995);
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split(',');
            if (fields.Length != 6 ||
                !double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var chroma) ||
                !double.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !double.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var rawY) ||
                Math.Abs(y) <= 0.000_000_001 ||
                AstmHue(fields[0]) is not { } astmHue)
            {
                continue;
            }

            var luminance = rawY * 0.975 / 100;
            var xyz = new Tristimulus(
                x * luminance / y,
                luminance,
                (1 - x - y) * luminance / y);
            var lab = LabFromXyz(xyz, IlluminantCWhite);
            if (!lab.IsFinite || lab.Chroma <= 0.000_001)
            {
                continue;
            }
            samples.Add(new RenotationSample(astmHue, value, chroma, lab));
        }
        return samples;
    }

    private static double? AstmHue(string label)
    {
        string[] designators = ["BG", "GY", "YR", "RP", "PB", "B", "G", "Y", "R", "P"];
        var codes = new Dictionary<string, int>
        {
            ["BG"] = 2, ["GY"] = 4, ["YR"] = 6, ["RP"] = 8, ["PB"] = 10,
            ["B"] = 1, ["G"] = 3, ["Y"] = 5, ["R"] = 7, ["P"] = 9,
        };
        var designator = designators.FirstOrDefault(candidate => label.EndsWith(candidate, StringComparison.Ordinal));
        if (designator is null ||
            !double.TryParse(
                label[..^designator.Length],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var prefix))
        {
            return null;
        }
        var hue = 10 * ((7 - codes[designator] + 10) % 10) + prefix;
        return Math.Abs(hue) < 0.000_001 ? 100 : hue;
    }

    private static IReadOnlyList<ColorimetrySample> LoadColorimetrySamples()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("IwashiScope.MunsellColorimetryCIE1931.csv");
        if (stream is null)
        {
            return [];
        }
        using var reader = new StreamReader(stream);
        _ = reader.ReadLine();
        var samples = new List<ColorimetrySample>(421);
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split(',');
            if (fields.Length != 5 ||
                !double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var wavelength) ||
                !double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var illuminant) ||
                !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var xBar) ||
                !double.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var yBar) ||
                !double.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var zBar))
            {
                continue;
            }
            samples.Add(new ColorimetrySample(
                wavelength,
                illuminant,
                xBar,
                yBar,
                zBar));
        }
        return samples.OrderBy(sample => sample.Wavelength).ToArray();
    }
}
