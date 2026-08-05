using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IwashiScope.Core.Models;

public enum AveragingProgressTier
{
    Insufficient,
    Minimum,
    Recommended,
    Sufficient,
}

public enum AveragingConvergenceTier
{
    HighVariation,
    Converging,
    Stable,
    SufficientlyStable,
}

public sealed record AveragingConvergence(
    double Relative95Uncertainty,
    AveragingConvergenceTier Tier)
{
    public double Relative95UncertaintyPercent => Relative95Uncertainty * 100;

    public double Progress
    {
        get
        {
            const double highVariationBoundary = 0.03;
            const double sufficientlyStableBoundary = 0.0075;
            var raw = (highVariationBoundary - Relative95Uncertainty) /
                      (highVariationBoundary - sufficientlyStableBoundary);
            return Math.Clamp(raw, 0, 1);
        }
    }
}

public enum AveragingSampleDecisionKind
{
    Accepted,
    Outlier,
    Incompatible,
}

public sealed record AveragingSampleDecision
{
    public required AveragingSampleDecisionKind Kind { get; init; }
    public double? Distance { get; init; }
    public double? Threshold { get; init; }
    public string? Reason { get; init; }

    public static AveragingSampleDecision Accepted() =>
        new() { Kind = AveragingSampleDecisionKind.Accepted };

    public static AveragingSampleDecision Outlier(double distance, double threshold) =>
        new()
        {
            Kind = AveragingSampleDecisionKind.Outlier,
            Distance = distance,
            Threshold = threshold,
        };

    public static AveragingSampleDecision Incompatible(string reason) =>
        new() { Kind = AveragingSampleDecisionKind.Incompatible, Reason = reason };
}

public sealed record SpectrumAnalysisRequest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public int ProtocolVersion { get; init; } = 3;
    public string Command { get; init; } = "analyzeSpectrum";
    public required string RequestId { get; init; }
    public required MeasurementMode Mode { get; init; }
    public required int SampleCount { get; init; }
    public required SpectrumAnalysisPayload Spectrum { get; init; }

    public byte[] FramedData()
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
        if ((ulong)payload.Length > uint.MaxValue)
        {
            throw new InvalidDataException("The averaged spectrum payload is too large.");
        }

        var frame = new byte[sizeof(uint) + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)payload.Length);
        payload.CopyTo(frame.AsSpan(sizeof(uint)));
        return frame;
    }
}

public sealed record SpectrumAnalysisPayload
{
    public required double StartNm { get; init; }
    public required double EndNm { get; init; }
    public required double Norm { get; init; }
    public double? PracticalStartNm { get; init; }
    public double? PracticalEndNm { get; init; }
    public IReadOnlyList<double> Values { get; init; } = [];
}

public sealed class AveragingMeasurementAccumulator
{
    public const int OutlierDetectionMinimumCount = 6;
    public const int MinimumOutputCount = 6;
    public const int RecommendedCount = 10;
    public const int SufficientCount = 15;
    public const int MaximumCount = 20;

    private const int RelaxedSampleCount = 6;
    private const double RelaxedNormalizedShapeDifference = 0.05;
    private const double StandardNormalizedShapeDifference = 0.03;
    private const double MaximumRelativeLevelDifference = 0.15;
    private const double MinimumRobustSigma = 0.001;
    private const double RobustSigmaMultiplier = 6.0;
    private const double ComparisonTolerance = 1e-7;
    private const double MinimumRms = 1e-12;
    private const double ConvergenceHighVariationBoundary = 0.03;
    private const double ConvergenceStableBoundary = 0.015;
    private const double ConvergenceSufficientBoundary = 0.0075;

    private static readonly double[] StudentTCritical95 =
    [
        2.570582, 2.446912, 2.364624, 2.306004, 2.262157,
        2.228139, 2.200985, 2.178813, 2.160369, 2.144787,
        2.131450, 2.119905, 2.109816, 2.100922, 2.093024,
    ];

    private sealed record RetainedSample(int SequenceNumber, SpotMeasurement Measurement);

    private sealed record OutlierEvaluation(
        double ShapeDistance,
        double ShapeThreshold,
        double LevelDifference)
    {
        public bool IsOutlier =>
            ShapeDistance > ShapeThreshold ||
            LevelDifference > MaximumRelativeLevelDifference;

        public (double Distance, double Threshold) DecisionValues =>
            LevelDifference > MaximumRelativeLevelDifference
                ? (LevelDifference, MaximumRelativeLevelDifference)
                : (ShapeDistance, ShapeThreshold);
    }

    private sealed record OutlierClassification(
        IReadOnlyList<RetainedSample> RetainedSamples,
        IReadOnlyDictionary<int, OutlierEvaluation> Evaluations);

    private List<RetainedSample> _retainedSamples = [];
    private SpotMeasurement? _spectralGridReference;
    private int _nextSequenceNumber = 1;

    public int MeasurementAttemptCount { get; private set; }
    public int OutlierCount { get; private set; }
    public int InvalidMeasurementCount { get; private set; }
    public int LastRetrospectivelyRejectedCount { get; private set; }
    public int RejectedCount => OutlierCount + InvalidMeasurementCount;
    public IReadOnlyList<SpotMeasurement> AcceptedMeasurements =>
        _retainedSamples.Select(sample => sample.Measurement).ToArray();
    public int AcceptedCount => _retainedSamples.Count;
    public SpotMeasurement? LatestAcceptedMeasurement =>
        _retainedSamples.LastOrDefault()?.Measurement;
    public bool HasStartedOutlierDetection =>
        _nextSequenceNumber > OutlierDetectionMinimumCount;
    public bool CanOutputAverage => AcceptedCount >= MinimumOutputCount;
    public bool HasReachedMaximum => AcceptedCount >= MaximumCount;

    public AveragingProgressTier ProgressTier => AcceptedCount switch
    {
        < MinimumOutputCount => AveragingProgressTier.Insufficient,
        < RecommendedCount => AveragingProgressTier.Minimum,
        < SufficientCount => AveragingProgressTier.Recommended,
        _ => AveragingProgressTier.Sufficient,
    };

    public AveragingConvergence? Convergence
    {
        get
        {
            if (AcceptedCount < OutlierDetectionMinimumCount ||
                AcceptedMeasurements.FirstOrDefault() is not { } first ||
                !AcceptedMeasurements.All(measurement =>
                    HasMatchingSpectralGrid(measurement, first)))
            {
                return null;
            }

            var sampleCount = AcceptedCount;
            var means = new double[first.Spectrum.Count];
            foreach (var measurement in AcceptedMeasurements)
            {
                for (var index = 0; index < measurement.Spectrum.Count; index++)
                {
                    means[index] += measurement.Spectrum[index].Value;
                }
            }
            for (var index = 0; index < means.Length; index++)
            {
                means[index] /= sampleCount;
            }

            var meanRms = RootMeanSquare(means);
            if (meanRms <= MinimumRms)
            {
                return null;
            }

            var varianceSums = new double[first.Spectrum.Count];
            foreach (var measurement in AcceptedMeasurements)
            {
                for (var index = 0; index < measurement.Spectrum.Count; index++)
                {
                    var difference = measurement.Spectrum[index].Value - means[index];
                    varianceSums[index] += difference * difference;
                }
            }

            var degreesOfFreedom = sampleCount - 1d;
            var standardErrors = varianceSums
                .Select(sum => Math.Sqrt(Math.Max(sum / degreesOfFreedom, 0) / sampleCount))
                .ToArray();
            var standardErrorRms = RootMeanSquare(standardErrors);
            var criticalValueIndex =
                Math.Clamp(sampleCount, OutlierDetectionMinimumCount, MaximumCount) -
                OutlierDetectionMinimumCount;
            var relative95Uncertainty =
                StudentTCritical95[criticalValueIndex] * standardErrorRms / meanRms;
            if (!double.IsFinite(relative95Uncertainty))
            {
                return null;
            }

            var tier = relative95Uncertainty switch
            {
                > ConvergenceHighVariationBoundary => AveragingConvergenceTier.HighVariation,
                > ConvergenceStableBoundary => AveragingConvergenceTier.Converging,
                > ConvergenceSufficientBoundary => AveragingConvergenceTier.Stable,
                _ when AcceptedCount >= SufficientCount =>
                    AveragingConvergenceTier.SufficientlyStable,
                _ => AveragingConvergenceTier.Stable,
            };
            return new AveragingConvergence(relative95Uncertainty, tier);
        }
    }

    public AveragingSampleDecision Add(SpotMeasurement measurement)
    {
        LastRetrospectivelyRejectedCount = 0;
        MeasurementAttemptCount++;
        if (AcceptedCount >= MaximumCount)
        {
            return AveragingSampleDecision.Incompatible(
                "Averaging measurement is limited to 20 accepted readings.");
        }
        if (!HasUsableSpectrum(measurement))
        {
            InvalidMeasurementCount++;
            return AveragingSampleDecision.Incompatible(
                "The reading had no usable spectrum and was not included in the average.");
        }
        if (_spectralGridReference is not null &&
            !HasMatchingSpectralGrid(measurement, _spectralGridReference))
        {
            InvalidMeasurementCount++;
            return AveragingSampleDecision.Incompatible(
                "The wavelength range or sample count did not match and was not included in the average.");
        }
        _spectralGridReference ??= measurement;

        var candidate = new RetainedSample(_nextSequenceNumber, measurement);
        _nextSequenceNumber++;
        if (candidate.SequenceNumber < OutlierDetectionMinimumCount)
        {
            _retainedSamples.Add(candidate);
            return AveragingSampleDecision.Accepted();
        }

        var previousSequenceNumbers = _retainedSamples
            .Select(sample => sample.SequenceNumber)
            .ToHashSet();
        var classification = ClassifyOutliers([.. _retainedSamples, candidate]);
        var retainedSequenceNumbers = classification.RetainedSamples
            .Select(sample => sample.SequenceNumber)
            .ToHashSet();
        var candidateWasAccepted = retainedSequenceNumbers.Contains(candidate.SequenceNumber);
        LastRetrospectivelyRejectedCount = previousSequenceNumbers
            .Except(retainedSequenceNumbers)
            .Count();
        OutlierCount += LastRetrospectivelyRejectedCount + (candidateWasAccepted ? 0 : 1);
        _retainedSamples = classification.RetainedSamples.ToList();

        if (candidateWasAccepted)
        {
            return AveragingSampleDecision.Accepted();
        }
        var evaluation = classification.Evaluations.GetValueOrDefault(candidate.SequenceNumber);
        var values = evaluation?.DecisionValues ?? (double.PositiveInfinity, 0d);
        return AveragingSampleDecision.Outlier(values.Item1, values.Item2);
    }

    public SpectrumAnalysisRequest MakeAnalysisRequest(string requestId)
    {
        if (AcceptedMeasurements.FirstOrDefault() is not { } first)
        {
            throw new InvalidDataException("There are no accepted readings to average.");
        }
        if (!AcceptedMeasurements.All(HasUsableSpectrum) ||
            !AcceptedMeasurements.All(measurement =>
                HasMatchingSpectralGrid(measurement, first)))
        {
            throw new InvalidDataException("The averaged spectrum could not be created.");
        }

        var averagedValues = new double[first.Spectrum.Count];
        foreach (var measurement in AcceptedMeasurements)
        {
            for (var index = 0; index < measurement.Spectrum.Count; index++)
            {
                averagedValues[index] += measurement.Spectrum[index].Value;
            }
        }
        for (var index = 0; index < averagedValues.Length; index++)
        {
            averagedValues[index] /= AcceptedCount;
        }
        if (averagedValues.Any(value => !double.IsFinite(value)))
        {
            throw new InvalidDataException("The averaged spectrum contained a non-finite value.");
        }

        var practicalRange = CommonPracticalSpectrumRange();
        return new SpectrumAnalysisRequest
        {
            RequestId = requestId,
            Mode = first.Mode,
            SampleCount = AcceptedCount,
            Spectrum = new SpectrumAnalysisPayload
            {
                StartNm = first.SpectrumStart,
                EndNm = first.SpectrumEnd,
                Norm = first.Mode == MeasurementMode.Reflectance ? 100 : 1,
                PracticalStartNm = practicalRange?.Start,
                PracticalEndNm = practicalRange?.End,
                Values = averagedValues,
            },
        };
    }

    private WavelengthRange? CommonPracticalSpectrumRange()
    {
        var ranges = AcceptedMeasurements
            .Select(measurement => measurement.ValidatedPracticalSpectrumRange)
            .ToArray();
        if (ranges.Length != AcceptedCount || ranges.Any(range => range is null))
        {
            return null;
        }

        var start = ranges.Max(range => range!.Start);
        var end = ranges.Min(range => range!.End);
        return start <= end ? new WavelengthRange(start, end) : null;
    }

    private static OutlierClassification ClassifyOutliers(
        IReadOnlyList<RetainedSample> samples)
    {
        var activeSamples = samples.ToList();
        var recordedEvaluations = new Dictionary<int, OutlierEvaluation>();
        for (var iteration = 0; iteration < samples.Count; iteration++)
        {
            var evaluations = EvaluateOutliers(activeSamples);
            foreach (var pair in evaluations)
            {
                recordedEvaluations[pair.Key] = pair.Value;
            }
            var survivors = activeSamples
                .Where(sample => !evaluations[sample.SequenceNumber].IsOutlier)
                .ToList();
            if (survivors.Count == activeSamples.Count || survivors.Count == 0)
            {
                if (survivors.Count > 0)
                {
                    activeSamples = survivors;
                }
                break;
            }
            activeSamples = survivors;
        }
        return new OutlierClassification(activeSamples, recordedEvaluations);
    }

    private static IReadOnlyDictionary<int, OutlierEvaluation> EvaluateOutliers(
        IReadOnlyList<RetainedSample> samples)
    {
        var center = PointwiseMedian(samples);
        var centerRms = RootMeanSquare(center);
        var rawMetrics = samples.Select(sample =>
        {
            var metrics = NormalizedShapeAndLevelDifference(
                sample.Measurement.Spectrum.Select(value => value.Value).ToArray(),
                center,
                centerRms);
            return (sample.SequenceNumber, metrics.ShapeDistance, metrics.LevelDifference);
        }).ToArray();
        var finiteShapeDistances = rawMetrics
            .Select(metrics => metrics.ShapeDistance)
            .Where(double.IsFinite)
            .ToArray();
        var baselineMedian = Median(finiteShapeDistances);
        var medianAbsoluteDeviation = Median(
            finiteShapeDistances.Select(value => Math.Abs(value - baselineMedian)).ToArray());
        var robustSigma = Math.Max(1.4826 * medianAbsoluteDeviation, MinimumRobustSigma);
        var adaptiveShapeThreshold = baselineMedian + RobustSigmaMultiplier * robustSigma;

        return rawMetrics.ToDictionary(
            metrics => metrics.SequenceNumber,
            metrics =>
            {
                var fixedThreshold = metrics.SequenceNumber <= RelaxedSampleCount
                    ? RelaxedNormalizedShapeDifference
                    : StandardNormalizedShapeDifference;
                return new OutlierEvaluation(
                    metrics.ShapeDistance,
                    Math.Max(fixedThreshold, adaptiveShapeThreshold),
                    metrics.LevelDifference);
            });
    }

    private static double[] PointwiseMedian(IReadOnlyList<RetainedSample> samples)
    {
        var sampleCount = samples.FirstOrDefault()?.Measurement.Spectrum.Count ?? 0;
        return Enumerable.Range(0, sampleCount)
            .Select(index => Median(samples
                .Select(sample => sample.Measurement.Spectrum[index].Value)
                .ToArray()))
            .ToArray();
    }

    private static (double ShapeDistance, double LevelDifference)
        NormalizedShapeAndLevelDifference(
            IReadOnlyList<double> values,
            IReadOnlyList<double> center,
            double centerRms)
    {
        if (values.Count != center.Count || values.Count == 0)
        {
            return (double.PositiveInfinity, double.PositiveInfinity);
        }
        var valuesRms = RootMeanSquare(values);
        if (centerRms <= MinimumRms)
        {
            return valuesRms <= MinimumRms
                ? (0, 0)
                : (double.PositiveInfinity, double.PositiveInfinity);
        }
        var levelRatio = valuesRms / centerRms;
        if (valuesRms <= MinimumRms)
        {
            return (double.PositiveInfinity, Math.Abs(levelRatio - 1));
        }

        var alignmentScale = centerRms / valuesRms;
        var squaredDifference = 0d;
        for (var index = 0; index < values.Count; index++)
        {
            var difference = values[index] * alignmentScale - center[index];
            squaredDifference += difference * difference;
        }
        var rmsDifference = Math.Sqrt(squaredDifference / values.Count);
        return (rmsDifference / centerRms, Math.Abs(levelRatio - 1));
    }

    private static double RootMeanSquare(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }
        return Math.Sqrt(values.Sum(value => value * value) / values.Count);
    }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }
        var sorted = values.Order().ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2
            : sorted[middle];
    }

    private static bool HasUsableSpectrum(SpotMeasurement measurement) =>
        measurement.Spectrum.Count >= 2 &&
        double.IsFinite(measurement.SpectrumStart) &&
        double.IsFinite(measurement.SpectrumEnd) &&
        measurement.SpectrumStart < measurement.SpectrumEnd &&
        measurement.Spectrum.All(sample =>
            double.IsFinite(sample.Wavelength) && double.IsFinite(sample.Value));

    private static bool HasMatchingSpectralGrid(
        SpotMeasurement left,
        SpotMeasurement right) =>
        left.Mode == right.Mode &&
        left.Spectrum.Count == right.Spectrum.Count &&
        ApproximatelyEqual(left.SpectrumStart, right.SpectrumStart) &&
        ApproximatelyEqual(left.SpectrumEnd, right.SpectrumEnd) &&
        left.Spectrum.Zip(right.Spectrum)
            .All(pair => ApproximatelyEqual(pair.First.Wavelength, pair.Second.Wavelength));

    private static bool ApproximatelyEqual(double left, double right) =>
        Math.Abs(left - right) <= ComparisonTolerance;
}
