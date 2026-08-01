using IwashiScope.Core.Models;

namespace IwashiScope.Core.Calculations;

public sealed record RgbColor(double Red, double Green, double Blue, bool IsOutOfGamut = false)
{
    public byte RedByte => ToByte(Red);
    public byte GreenByte => ToByte(Green);
    public byte BlueByte => ToByte(Blue);
    public string Hex => $"#{RedByte:X2}{GreenByte:X2}{BlueByte:X2}";
    public string RgbDescription => $"R {RedByte}  G {GreenByte}  B {BlueByte}";

    private static byte ToByte(double value) =>
        (byte)Math.Round(Math.Clamp(value, 0, 1) * 255, MidpointRounding.AwayFromZero);
}

public sealed record LabColorConversion(
    RgbColor Srgb,
    RgbColor AdobeRgb,
    RgbColor DisplayP3);

public static class LabColorConverter
{
    private const double GamutTolerance = 0.0005;

    public static RgbColor D50LabToSrgb(Vector3 lab) =>
        Convert(lab, "D50").Srgb;

    public static LabColorConversion Convert(Vector3 lab, string? whitePoint = "D50")
    {
        if (!lab.IsFinite)
        {
            throw new ArgumentException("Lab components must be finite.", nameof(lab));
        }

        var fy = (lab.First + 16) / 116;
        var fx = fy + lab.Second / 500;
        var fz = fy - lab.Third / 200;
        var isD65 = whitePoint?.Contains("D65", StringComparison.OrdinalIgnoreCase) == true;
        var sourceWhite = isD65
            ? new Vector3(95.047, 100, 108.883)
            : new Vector3(96.4212, 100, 82.5188);
        var sourceX = sourceWhite.First * PivotInverse(fx) / 100;
        var sourceY = sourceWhite.Second * PivotInverse(fy) / 100;
        var sourceZ = sourceWhite.Third * PivotInverse(fz) / 100;

        // Bradford D50 -> D65 adaptation. A D65 source stays in its native white point.
        var x = isD65
            ? sourceX
            : 0.9555766 * sourceX + -0.0230393 * sourceY + 0.0631636 * sourceZ;
        var y = isD65
            ? sourceY
            : -0.0282895 * sourceX + 1.0099416 * sourceY + 0.0210077 * sourceZ;
        var z = isD65
            ? sourceZ
            : 0.0122982 * sourceX + -0.0204830 * sourceY + 1.3299098 * sourceZ;

        var linearR = 3.2404542 * x + -1.5371385 * y + -0.4985314 * z;
        var linearG = -0.9692660 * x + 1.8760108 * y + 0.0415560 * z;
        var linearB = 0.0556434 * x + -0.2040259 * y + 1.0572252 * z;

        var adobeLinearR = 2.0413690 * x - 0.5649464 * y - 0.3446944 * z;
        var adobeLinearG = -0.9692660 * x + 1.8760108 * y + 0.0415560 * z;
        var adobeLinearB = 0.0134474 * x - 0.1183897 * y + 1.0154096 * z;

        // Display P3 uses D65 and the sRGB transfer function, but has its own
        // primaries. Keep the extended linear values for an independent gamut
        // test before clipping the values used for display.
        var displayP3LinearR =
            2.493496911941425 * x - 0.931383617919124 * y - 0.402710784450717 * z;
        var displayP3LinearG =
            -0.829488969561575 * x + 1.762664060318346 * y + 0.023624685841943 * z;
        var displayP3LinearB =
            0.035845830243784 * x - 0.076172389268041 * y + 0.956884524007687 * z;

        return new LabColorConversion(
            new RgbColor(
                Math.Clamp(SrgbGamma(linearR), 0, 1),
                Math.Clamp(SrgbGamma(linearG), 0, 1),
                Math.Clamp(SrgbGamma(linearB), 0, 1),
                IsOutOfGamut(linearR, linearG, linearB)),
            new RgbColor(
                Math.Clamp(AdobeGamma(adobeLinearR), 0, 1),
                Math.Clamp(AdobeGamma(adobeLinearG), 0, 1),
                Math.Clamp(AdobeGamma(adobeLinearB), 0, 1),
                IsOutOfGamut(adobeLinearR, adobeLinearG, adobeLinearB)),
            new RgbColor(
                Math.Clamp(SrgbGamma(displayP3LinearR), 0, 1),
                Math.Clamp(SrgbGamma(displayP3LinearG), 0, 1),
                Math.Clamp(SrgbGamma(displayP3LinearB), 0, 1),
                IsOutOfGamut(
                    displayP3LinearR,
                    displayP3LinearG,
                    displayP3LinearB)));
    }

    private static double PivotInverse(double value)
    {
        const double delta = 6.0 / 29.0;
        return value > delta
            ? value * value * value
            : 3 * delta * delta * (value - 4.0 / 29.0);
    }

    private static double SrgbGamma(double value) =>
        value <= 0.0031308
            ? 12.92 * value
            : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055;

    private static double AdobeGamma(double value) =>
        Math.CopySign(Math.Pow(Math.Abs(value), 1 / 2.19921875), value);

    private static bool IsOutOfGamut(params double[] components) =>
        components.Any(component =>
            component < -GamutTolerance || component > 1 + GamutTolerance);
}

public static class Tm30SampleFidelity
{
    public const double ScalingFactor = 7.54;

    public static double? Score(Vector3 reference, Vector3 test)
    {
        if (!reference.IsFinite || !test.IsFinite)
        {
            return null;
        }

        var deltaJ = test.First - reference.First;
        var deltaA = test.Second - reference.Second;
        var deltaB = test.Third - reference.Third;
        var deltaE = Math.Sqrt(deltaJ * deltaJ + deltaA * deltaA + deltaB * deltaB);
        var untransformed = Math.Max(100 - ScalingFactor * deltaE, 0);
        var transformed = 10 * Softplus(untransformed / 10);
        return Math.Clamp(transformed, 0, 100);
    }

    private static double Softplus(double value) =>
        value > 30 ? value : Math.Log(1 + Math.Exp(value));
}

public enum EvaluationStatus
{
    Meets,
    Caution,
    DoesNotMeet,
    Fails,
    Unavailable,
}

public enum IlluminanceClassification
{
    PrintComparison,
    DisplayComparison,
    GeneralOffice,
    TooDark,
    TooBright,
    Unavailable,
}

public sealed record PrintingViewingConditionEvaluation(
    double? ChromaticityDistance,
    EvaluationStatus ChromaticityStatus,
    double? AverageCri,
    EvaluationStatus AverageCriStatus,
    KeyValuePair<int, double>? MinimumSpecialCri,
    EvaluationStatus SpecialCriStatus,
    double? Illuminance,
    IlluminanceClassification IlluminanceClassification,
    EvaluationStatus IlluminanceStatus,
    EvaluationStatus LightSourceStatus,
    EvaluationStatus SummaryStatus);

public static class PrintingViewingConditionEvaluator
{
    public const string StandardName = "JSPST-1998";
    public const double TargetUPrime = 0.2091;
    public const double TargetVPrime = 0.4881;

    public static PrintingViewingConditionEvaluation Evaluate(SpotMeasurement measurement)
    {
        var chromaticityDistance = measurement.Xyz is { } xyz
            ? ChromaticityDistanceFromD50(xyz)
            : null;
        var chromaticityStatus = Status(chromaticityDistance, value => value <= 0.004);
        var averageCri = measurement.Cri?.Ra;
        var averageStatus = Status(averageCri, value => value >= 95);
        var minimumSpecial = MinimumSpecial(measurement.Cri);
        var specialStatus = Status(minimumSpecial?.Value, value => value >= 90);
        var classification = measurement.Mode == MeasurementMode.Ambient
            ? ClassifyIlluminance(measurement.Lux)
            : IlluminanceClassification.Unavailable;
        var illuminanceStatus = classification switch
        {
            IlluminanceClassification.PrintComparison => EvaluationStatus.Meets,
            IlluminanceClassification.DisplayComparison or IlluminanceClassification.GeneralOffice =>
                EvaluationStatus.Caution,
            IlluminanceClassification.TooDark or IlluminanceClassification.TooBright =>
                EvaluationStatus.Fails,
            _ => EvaluationStatus.Unavailable,
        };
        var lightSource = Aggregate(chromaticityStatus, averageStatus, specialStatus);
        return new PrintingViewingConditionEvaluation(
            chromaticityDistance,
            chromaticityStatus,
            averageCri,
            averageStatus,
            minimumSpecial,
            specialStatus,
            measurement.Lux,
            classification,
            illuminanceStatus,
            lightSource,
            Aggregate(lightSource, illuminanceStatus));
    }

    private static double? ChromaticityDistanceFromD50(Vector3 xyz)
    {
        var denominator = xyz.First + 15 * xyz.Second + 3 * xyz.Third;
        if (!double.IsFinite(denominator) || Math.Abs(denominator) <= 1e-12)
        {
            return null;
        }

        var uPrime = 4 * xyz.First / denominator;
        var vPrime = 9 * xyz.Second / denominator;
        var deltaU = uPrime - TargetUPrime;
        var deltaV = vPrime - TargetVPrime;
        return double.IsFinite(uPrime) && double.IsFinite(vPrime)
            ? Math.Sqrt(deltaU * deltaU + deltaV * deltaV)
            : null;
    }

    private static KeyValuePair<int, double>? MinimumSpecial(CriResult? cri)
    {
        if (cri is null || Enumerable.Range(9, 7).Any(index =>
                !cri.Individual.TryGetValue(index, out var value) || !double.IsFinite(value)))
        {
            return null;
        }

        return Enumerable.Range(9, 7)
            .Select(index => new KeyValuePair<int, double>(index, cri.Individual[index]))
            .MinBy(pair => pair.Value);
    }

    private static IlluminanceClassification ClassifyIlluminance(double? lux)
    {
        if (lux is not { } value || !double.IsFinite(value))
        {
            return IlluminanceClassification.Unavailable;
        }
        if (value < 300) return IlluminanceClassification.TooDark;
        if (value is >= 375 and <= 625) return IlluminanceClassification.DisplayComparison;
        if (value is >= 1500 and <= 2500) return IlluminanceClassification.PrintComparison;
        if (value > 2500) return IlluminanceClassification.TooBright;
        return IlluminanceClassification.GeneralOffice;
    }

    private static EvaluationStatus Status(double? value, Func<double, bool> condition) =>
        value is { } number && double.IsFinite(number)
            ? condition(number) ? EvaluationStatus.Meets : EvaluationStatus.DoesNotMeet
            : EvaluationStatus.Unavailable;

    private static EvaluationStatus Aggregate(params EvaluationStatus[] values)
    {
        if (values.Contains(EvaluationStatus.Fails)) return EvaluationStatus.Fails;
        if (values.Contains(EvaluationStatus.DoesNotMeet)) return EvaluationStatus.DoesNotMeet;
        if (values.Contains(EvaluationStatus.Unavailable)) return EvaluationStatus.Unavailable;
        if (values.Contains(EvaluationStatus.Caution)) return EvaluationStatus.Caution;
        return values.All(value => value == EvaluationStatus.Meets)
            ? EvaluationStatus.Meets
            : EvaluationStatus.Unavailable;
    }
}

public enum Iso3664IlluminanceCondition
{
    P3,
    P4,
    Outside,
    Unavailable,
}

public sealed record Iso3664NumericEvaluation(
    double? FidelityIndex,
    EvaluationStatus FidelityStatus,
    double? AverageCri,
    EvaluationStatus AverageCriStatus,
    double? Illuminance,
    Iso3664IlluminanceCondition IlluminanceCondition,
    EvaluationStatus IlluminanceStatus,
    EvaluationStatus RenderingStatus,
    EvaluationStatus SummaryStatus);

public static class Iso3664NumericEvaluator
{
    public const string StandardName = "ISO 3664:2025";

    public static Iso3664NumericEvaluation Evaluate(SpotMeasurement measurement)
    {
        var fidelity = measurement.Tm30?.FidelityIndex;
        var fidelityStatus = Status(fidelity, value => value >= 95);
        var averageCri = measurement.Cri?.Ra;
        var criStatus = Status(averageCri, value => value > 90);
        var rendering = Aggregate(fidelityStatus, criStatus);
        var condition = measurement.Mode == MeasurementMode.Ambient
            ? Classify(measurement.Lux)
            : Iso3664IlluminanceCondition.Unavailable;
        var illuminanceStatus = condition switch
        {
            Iso3664IlluminanceCondition.P3 or Iso3664IlluminanceCondition.P4 =>
                EvaluationStatus.Meets,
            Iso3664IlluminanceCondition.Outside => EvaluationStatus.DoesNotMeet,
            _ => EvaluationStatus.Unavailable,
        };
        var summary = measurement.Mode == MeasurementMode.Ambient
            ? Aggregate(rendering, illuminanceStatus)
            : rendering;
        return new Iso3664NumericEvaluation(
            fidelity,
            fidelityStatus,
            averageCri,
            criStatus,
            measurement.Lux,
            condition,
            illuminanceStatus,
            rendering,
            summary);
    }

    private static Iso3664IlluminanceCondition Classify(double? value) =>
        value switch
        {
            >= 1500 and <= 2500 => Iso3664IlluminanceCondition.P3,
            >= 375 and <= 625 => Iso3664IlluminanceCondition.P4,
            double number when double.IsFinite(number) => Iso3664IlluminanceCondition.Outside,
            _ => Iso3664IlluminanceCondition.Unavailable,
        };

    private static EvaluationStatus Status(double? value, Func<double, bool> condition) =>
        value is { } number && double.IsFinite(number)
            ? condition(number) ? EvaluationStatus.Meets : EvaluationStatus.DoesNotMeet
            : EvaluationStatus.Unavailable;

    private static EvaluationStatus Aggregate(params EvaluationStatus[] values)
    {
        if (values.Contains(EvaluationStatus.Fails)) return EvaluationStatus.Fails;
        if (values.Contains(EvaluationStatus.DoesNotMeet)) return EvaluationStatus.DoesNotMeet;
        if (values.Contains(EvaluationStatus.Unavailable)) return EvaluationStatus.Unavailable;
        if (values.Contains(EvaluationStatus.Caution)) return EvaluationStatus.Caution;
        return values.All(value => value == EvaluationStatus.Meets)
            ? EvaluationStatus.Meets
            : EvaluationStatus.Unavailable;
    }
}
