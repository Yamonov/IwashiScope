/*
 SPDX-FileCopyrightText: 2026 Yamonov
 SPDX-License-Identifier: AGPL-3.0-only
*/
using IwashiScope.Core.History;
using IwashiScope.Core.Models;

namespace IwashiScope.Core.Calculations;

public enum UserIlluminantSlot
{
    User1,
    User2,
    User3,
}

public static class UserIlluminantSlots
{
    public static string Title(UserIlluminantSlot slot, bool japanese) => slot switch
    {
        UserIlluminantSlot.User1 => japanese ? "User定義１" : "User Defined 1",
        UserIlluminantSlot.User2 => japanese ? "User定義２" : "User Defined 2",
        _ => japanese ? "User定義３" : "User Defined 3",
    };
}

public enum IlluminantSpectrumOriginKind
{
    Cie,
    User,
}

public sealed record IlluminantSpectrumDefinition
{
    public required IlluminantSpectrumOriginKind OriginKind { get; init; }
    public CieReferenceIlluminant? CieIlluminant { get; init; }
    public UserIlluminantSlot? UserSlot { get; init; }
    public required string DisplayName { get; init; }
    public string? UserName { get; init; }
    public DateTimeOffset? MeasuredAt { get; init; }
    public IReadOnlyList<SpectralSample> Samples { get; init; } = [];

    public static IlluminantSpectrumDefinition Cie(CieReferenceIlluminant illuminant) =>
        new()
        {
            OriginKind = IlluminantSpectrumOriginKind.Cie,
            CieIlluminant = illuminant,
            DisplayName = CieReferenceIlluminants.RawValue(illuminant),
            Samples = CieReferenceIlluminants.Samples(illuminant),
        };

    public static IlluminantSpectrumDefinition? User(
        UserIlluminantSlot slot,
        MeasurementHistoryEntry entry,
        bool japanese)
    {
        var samples = NormalizeUserSamples(entry.Measurement.Spectrum);
        return samples.Count < 2
            ? null
            : new IlluminantSpectrumDefinition
            {
                OriginKind = IlluminantSpectrumOriginKind.User,
                UserSlot = slot,
                DisplayName = UserIlluminantSlots.Title(slot, japanese),
                UserName = MeasurementHistoryEntry.NormalizeName(entry.Name),
                MeasuredAt = entry.Measurement.CapturedAt,
                Samples = samples,
            };
    }

    public static IReadOnlyList<SpectralSample> NormalizeUserSamples(
        IReadOnlyList<SpectralSample> samples)
    {
        var ordered = samples
            .Where(sample =>
                double.IsFinite(sample.Wavelength) &&
                double.IsFinite(sample.Value) &&
                sample.Value >= 0)
            .OrderBy(sample => sample.Wavelength)
            .ToArray();
        if (ordered.Length < 2 ||
            ordered.Zip(ordered.Skip(1)).Any(pair =>
                pair.First.Wavelength >= pair.Second.Wavelength))
        {
            return [];
        }
        return ordered;
    }
}

public sealed record ReflectanceIlluminantSpectrumResult(
    IlluminantSpectrumDefinition? SelectedSource,
    IReadOnlyList<SpectralSample> MeasuredReflectance,
    IReadOnlyList<SpectralSample> Illuminant,
    IReadOnlyList<SpectralSample> ReflectedLight,
    double WavelengthStart,
    double WavelengthEnd,
    bool RequiresUvWarning)
{
    public double AutomaticUpperBound
    {
        get
        {
            var maximum = MeasuredReflectance
                .Concat(Illuminant)
                .Concat(ReflectedLight)
                .Where(sample => double.IsFinite(sample.Value))
                .Select(sample => sample.Value)
                .DefaultIfEmpty(100)
                .Max();
            return Math.Max(100, maximum * 1.08);
        }
    }
}

public static class ReflectanceIlluminantSpectrumCalculator
{
    private const double SupportedStart = 380;
    private const double SupportedEnd = 730;

    public static ReflectanceIlluminantSpectrumResult? Calculate(
        SpotMeasurement? measurement,
        IlluminantSpectrumDefinition? source,
        WavelengthRange? requestedDisplayRange = null)
    {
        if (measurement is null || measurement.Mode != MeasurementMode.Reflectance)
        {
            return null;
        }

        var displayStart = Math.Max(requestedDisplayRange?.Start ?? SupportedStart, SupportedStart);
        var displayEnd = Math.Min(requestedDisplayRange?.End ?? SupportedEnd, SupportedEnd);
        if (displayStart > displayEnd)
        {
            return null;
        }

        var measured = measurement.Spectrum
            .Where(sample =>
                double.IsFinite(sample.Wavelength) &&
                double.IsFinite(sample.Value) &&
                sample.Wavelength >= displayStart &&
                sample.Wavelength <= displayEnd)
            .OrderBy(sample => sample.Wavelength)
            .ToArray();
        if (measured.Length == 0)
        {
            return null;
        }

        var wavelengthStart = measured[0].Wavelength;
        var wavelengthEnd = measured[^1].Wavelength > wavelengthStart
            ? measured[^1].Wavelength
            : wavelengthStart + 1;
        if (source is null)
        {
            return new ReflectanceIlluminantSpectrumResult(
                null,
                measured,
                [],
                [],
                wavelengthStart,
                wavelengthEnd,
                false);
        }

        var peak = source.Samples
            .Where(sample => double.IsFinite(sample.Value))
            .Select(sample => sample.Value)
            .DefaultIfEmpty(0)
            .Max();
        if (peak <= 0)
        {
            return null;
        }

        var normalized = source.Samples
            .OrderBy(sample => sample.Wavelength)
            .Select(sample => sample with { Value = Math.Max(0, sample.Value) / peak * 100 })
            .ToArray();
        var visibleIlluminant = normalized
            .Where(sample => sample.Wavelength >= wavelengthStart && sample.Wavelength <= wavelengthEnd)
            .ToArray();
        var reflected = measured
            .Select(sample =>
            {
                var illuminantValue = Interpolate(normalized, sample.Wavelength);
                return illuminantValue is null
                    ? null
                    : sample with
                    {
                        Value = illuminantValue.Value * Math.Max(0, sample.Value) / 100,
                    };
            })
            .Where(sample => sample is not null)
            .Cast<SpectralSample>()
            .ToArray();
        return visibleIlluminant.Length == 0 || reflected.Length == 0
            ? null
            : new ReflectanceIlluminantSpectrumResult(
                source,
                measured,
                visibleIlluminant,
                reflected,
                wavelengthStart,
                wavelengthEnd,
                true);
    }

    internal static double? Interpolate(
        IReadOnlyList<SpectralSample> samples,
        double wavelength)
    {
        if (samples.Count == 0 ||
            wavelength < samples[0].Wavelength ||
            wavelength > samples[^1].Wavelength)
        {
            return null;
        }
        if (wavelength == samples[0].Wavelength)
        {
            return samples[0].Value;
        }
        if (wavelength == samples[^1].Wavelength)
        {
            return samples[^1].Value;
        }

        var lowerIndex = 0;
        var upperIndex = samples.Count - 1;
        while (lowerIndex + 1 < upperIndex)
        {
            var midpoint = (lowerIndex + upperIndex) / 2;
            if (samples[midpoint].Wavelength <= wavelength)
            {
                lowerIndex = midpoint;
            }
            else
            {
                upperIndex = midpoint;
            }
        }

        var lower = samples[lowerIndex];
        var upper = samples[upperIndex];
        var width = upper.Wavelength - lower.Wavelength;
        if (width <= 0)
        {
            return null;
        }
        var fraction = (wavelength - lower.Wavelength) / width;
        return lower.Value + (upper.Value - lower.Value) * fraction;
    }
}

public enum ReflectanceAppearanceMethod
{
    Unadapted,
    Bradford,
    CieCam16,
}

public sealed record ReflectanceIlluminantColorComparisonResult(
    IlluminantSpectrumDefinition Source,
    ReflectanceAppearanceMethod Method,
    Vector3? MeasuredLab,
    Vector3 ReferenceLab,
    Vector3 ReferenceXyz,
    Vector3 ReferenceWhiteXyz,
    Vector3 SourceXyz,
    Vector3 SourceWhiteXyz,
    Vector3 SimulatedXyz,
    Vector3 SimulatedLab,
    AppearanceCorrelates? ReferenceAppearance,
    AppearanceCorrelates? SourceAppearance,
    double? ReferenceAdaptationDegree,
    double? SourceAdaptationDegree,
    WavelengthRange WavelengthRange)
{
    public double DeltaL => SimulatedLab.First - ReferenceLab.First;
    public double DeltaA => SimulatedLab.Second - ReferenceLab.Second;
    public double DeltaB => SimulatedLab.Third - ReferenceLab.Third;
    public double DeltaE76 => CieColorDifference.DeltaE76(ReferenceLab, SimulatedLab);
    public double DeltaE2000 => CieColorDifference.DeltaE2000(ReferenceLab, SimulatedLab);
}

public static class ReflectanceIlluminantColorComparisonCalculator
{
    public static readonly Vector3 D50White = new(96.42, 100, 82.49);

    public static Vector3 ReferenceLab(SpotMeasurement measurement)
    {
        if (measurement.Mode != MeasurementMode.Reflectance)
            throw new ColorAppearanceException(ColorAppearanceError.InvalidStimulus);
        var reference = SpectralTristimulusIntegrator.Integrate(
            measurement.Spectrum, CieReferenceIlluminants.Samples(CieReferenceIlluminant.D50))
            ?? throw new ColorAppearanceException(ColorAppearanceError.InsufficientSpectrum);
        return CieLabColorimetry.Lab(reference.ObjectXyz, reference.WhiteXyz)
            ?? throw new ColorAppearanceException(ColorAppearanceError.OutsideModelDomain);
    }

    public static ReflectanceIlluminantColorComparisonResult Compare(
        SpotMeasurement measurement,
        IlluminantSpectrumDefinition source,
        ReflectanceAppearanceMethod method)
    {
        if (measurement.Mode != MeasurementMode.Reflectance)
            throw new ColorAppearanceException(ColorAppearanceError.InvalidStimulus);
        var selected = SpectralTristimulusIntegrator.Integrate(measurement.Spectrum, source.Samples)
            ?? throw new ColorAppearanceException(ColorAppearanceError.InsufficientSpectrum);
        var reference = SpectralTristimulusIntegrator.Integrate(
            measurement.Spectrum, CieReferenceIlluminants.Samples(CieReferenceIlluminant.D50),
            selected.Wavelengths);
        if (reference is null || !reference.Wavelengths.SequenceEqual(selected.Wavelengths))
            throw new ColorAppearanceException(ColorAppearanceError.InsufficientSpectrum);

        Vector3 simulated;
        AppearanceCorrelates? sourceAppearance = null;
        AppearanceCorrelates? referenceAppearance = null;
        double? sourceDegree = null;
        double? referenceDegree = null;
        switch (method)
        {
            case ReflectanceAppearanceMethod.Unadapted:
                simulated = selected.ObjectXyz;
                break;
            case ReflectanceAppearanceMethod.Bradford:
                simulated = BradfordChromaticAdaptation.Adapt(
                    selected.ObjectXyz, selected.WhiteXyz, reference.WhiteXyz)
                    ?? throw new ColorAppearanceException(ColorAppearanceError.OutsideModelDomain);
                break;
            case ReflectanceAppearanceMethod.CieCam16:
                var sourceModel = new CieCam16(new AppearanceViewingConditions(selected.WhiteXyz));
                var referenceModel = new CieCam16(new AppearanceViewingConditions(reference.WhiteXyz));
                sourceAppearance = sourceModel.Forward(selected.ObjectXyz);
                referenceAppearance = referenceModel.Forward(reference.ObjectXyz);
                sourceDegree = sourceModel.AdaptationDegree;
                referenceDegree = referenceModel.AdaptationDegree;
                simulated = referenceModel.Inverse(sourceAppearance);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(method));
        }

        var referenceLab = CieLabColorimetry.Lab(reference.ObjectXyz, reference.WhiteXyz)
            ?? throw new ColorAppearanceException(ColorAppearanceError.OutsideModelDomain);
        var simulatedLab = CieLabColorimetry.Lab(simulated, reference.WhiteXyz)
            ?? throw new ColorAppearanceException(ColorAppearanceError.OutsideModelDomain);
        return new ReflectanceIlluminantColorComparisonResult(
            source, method, measurement.Lab, referenceLab, reference.ObjectXyz,
            reference.WhiteXyz, selected.ObjectXyz, selected.WhiteXyz,
            simulated, simulatedLab, referenceAppearance, sourceAppearance,
            referenceDegree, sourceDegree,
            new WavelengthRange(selected.Wavelengths[0], selected.Wavelengths[^1]));
    }
}

public static class CieLabColorimetry
{
    private const double Epsilon = 216.0 / 24_389.0;
    private const double Kappa = 24_389.0 / 27.0;

    public static Vector3? Lab(Vector3 xyz, Vector3 white)
    {
        if (!xyz.IsFinite || !white.IsFinite ||
            white.First <= 0 || white.Second <= 0 || white.Third <= 0)
        {
            return null;
        }
        var fx = Pivot(xyz.First / white.First);
        var fy = Pivot(xyz.Second / white.Second);
        var fz = Pivot(xyz.Third / white.Third);
        var lab = new Vector3(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
        return lab.IsFinite ? lab : null;
    }

    private static double Pivot(double value) => value > Epsilon
        ? Math.Pow(value, 1.0 / 3.0)
        : (Kappa * value + 16) / 116;
}

public static class BradfordChromaticAdaptation
{
    private static readonly double[,] Matrix =
    {
        { 0.8951, 0.2664, -0.1614 },
        { -0.7502, 1.7135, 0.0367 },
        { 0.0389, -0.0685, 1.0296 },
    };
    private static readonly double[,] InverseMatrix =
    {
        { 0.9869929054667123, -0.14705425642099013, 0.15996265166373122 },
        { 0.4323052697233945, 0.5183602715367776, 0.049291228212855594 },
        { -0.008528664575177328, 0.04004282165408487, 0.9684866957875502 },
    };

    public static Vector3? Adapt(Vector3 xyz, Vector3 sourceWhite, Vector3 destinationWhite)
    {
        var sourceCone = Multiply(Matrix, sourceWhite);
        var destinationCone = Multiply(Matrix, destinationWhite);
        var objectCone = Multiply(Matrix, xyz);
        if (!sourceCone.IsFinite || !destinationCone.IsFinite || !objectCone.IsFinite ||
            Math.Abs(sourceCone.First) <= 1e-12 ||
            Math.Abs(sourceCone.Second) <= 1e-12 ||
            Math.Abs(sourceCone.Third) <= 1e-12)
        {
            return null;
        }
        var adaptedCone = new Vector3(
            objectCone.First * destinationCone.First / sourceCone.First,
            objectCone.Second * destinationCone.Second / sourceCone.Second,
            objectCone.Third * destinationCone.Third / sourceCone.Third);
        var adapted = Multiply(InverseMatrix, adaptedCone);
        return adapted.IsFinite ? adapted : null;
    }

    private static Vector3 Multiply(double[,] matrix, Vector3 vector) => new(
        matrix[0, 0] * vector.First + matrix[0, 1] * vector.Second + matrix[0, 2] * vector.Third,
        matrix[1, 0] * vector.First + matrix[1, 1] * vector.Second + matrix[1, 2] * vector.Third,
        matrix[2, 0] * vector.First + matrix[2, 1] * vector.Second + matrix[2, 2] * vector.Third);
}

public static class CieColorDifference
{
    public static double DeltaE76(Vector3 first, Vector3 second)
    {
        var deltaL = first.First - second.First;
        var deltaA = first.Second - second.Second;
        var deltaB = first.Third - second.Third;
        return Math.Sqrt(deltaL * deltaL + deltaA * deltaA + deltaB * deltaB);
    }

    public static double DeltaE2000(Vector3 first, Vector3 second)
    {
        const double pi = 3.14159265358979;
        const double radiansToDegrees = 180 / pi;
        const double degreesToRadians = pi / 180;
        var c1ab = Math.Sqrt(first.Second * first.Second + first.Third * first.Third);
        var c2ab = Math.Sqrt(second.Second * second.Second + second.Third * second.Third);
        var meanCab = (c1ab + c2ab) / 2;
        var meanCab7 = Math.Pow(meanCab, 7);
        var g = 0.5 * (1 - Math.Sqrt(meanCab7 / (meanCab7 + 6_103_515_625)));
        var a1Prime = (1 + g) * first.Second;
        var a2Prime = (1 + g) * second.Second;
        var c1Prime = Math.Sqrt(a1Prime * a1Prime + first.Third * first.Third);
        var c2Prime = Math.Sqrt(a2Prime * a2Prime + second.Third * second.Third);
        var h1Prime = HueDegrees(a1Prime, first.Third, radiansToDegrees);
        var h2Prime = HueDegrees(a2Prime, second.Third, radiansToDegrees);
        var deltaLPrime = second.First - first.First;
        var deltaCPrime = c2Prime - c1Prime;
        double deltaHueDegrees;
        if (c1Prime < 1e-9 || c2Prime < 1e-9)
        {
            deltaHueDegrees = 0;
        }
        else
        {
            deltaHueDegrees = h2Prime - h1Prime;
            if (deltaHueDegrees > 180) deltaHueDegrees -= 360;
            else if (deltaHueDegrees < -180) deltaHueDegrees += 360;
        }
        var deltaHPrime = 2 * Math.Sqrt(c1Prime * c2Prime) *
            Math.Sin(0.5 * deltaHueDegrees * degreesToRadians);
        var meanLPrime = (first.First + second.First) / 2;
        var meanCPrime = (c1Prime + c2Prime) / 2;
        double meanHPrime;
        if (c1Prime < 1e-9 || c2Prime < 1e-9)
        {
            meanHPrime = h1Prime + h2Prime;
        }
        else
        {
            var sum = h1Prime + h2Prime;
            if (Math.Abs(h1Prime - h2Prime) > 180) sum += sum < 360 ? 360 : -360;
            meanHPrime = sum / 2;
        }
        var t = 1
            - 0.17 * Math.Cos((meanHPrime - 30) * degreesToRadians)
            + 0.24 * Math.Cos(2 * meanHPrime * degreesToRadians)
            + 0.32 * Math.Cos((3 * meanHPrime + 6) * degreesToRadians)
            - 0.20 * Math.Cos((4 * meanHPrime - 63) * degreesToRadians);
        var lightnessOffsetSquared = Math.Pow(meanLPrime - 50, 2);
        var sL = 1 + 0.015 * lightnessOffsetSquared / Math.Sqrt(20 + lightnessOffsetSquared);
        var sC = 1 + 0.045 * meanCPrime;
        var sH = 1 + 0.015 * meanCPrime * t;
        var hueRotation = 30 * Math.Exp(-Math.Pow((meanHPrime - 275) / 25, 2));
        var meanCPrime7 = Math.Pow(meanCPrime, 7);
        var rC = 2 * Math.Sqrt(meanCPrime7 / (meanCPrime7 + 6_103_515_625));
        var rT = -Math.Sin(2 * hueRotation * degreesToRadians) * rC;
        var lightnessTerm = deltaLPrime / sL;
        var chromaTerm = deltaCPrime / sC;
        var hueTerm = deltaHPrime / sH;
        var squared = lightnessTerm * lightnessTerm +
            chromaTerm * chromaTerm + hueTerm * hueTerm + rT * chromaTerm * hueTerm;
        return Math.Sqrt(Math.Max(0, squared));
    }

    private static double HueDegrees(double a, double b, double radiansToDegrees)
    {
        if (Math.Sqrt(a * a + b * b) < 1e-9) return 0;
        var angle = Math.Atan2(b, a) * radiansToDegrees;
        return angle < 0 ? angle + 360 : angle;
    }
}
