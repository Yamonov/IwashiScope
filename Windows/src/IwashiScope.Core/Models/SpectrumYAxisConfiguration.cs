namespace IwashiScope.Core.Models;

public enum SpectrumYAxisMode
{
    Automatic,
    Fixed,
}

public sealed record SpectrumYAxisScale(
    double UpperBound,
    IReadOnlyList<double> TickValues);

public readonly record struct SpectrumYAxisConfiguration(
    SpectrumYAxisMode Mode,
    double FixedUpperBound)
{
    public const double MinimumUpperBound = 10;
    public const double MaximumUpperBound = 500;
    public const double Step = 10;

    public static SpectrumYAxisConfiguration ForMeasurementMode(MeasurementMode mode) => mode switch
    {
        MeasurementMode.Reflectance => new(SpectrumYAxisMode.Fixed, 100),
        MeasurementMode.Ambient => new(SpectrumYAxisMode.Automatic, 200),
        MeasurementMode.Emissive => new(SpectrumYAxisMode.Automatic, 200),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public SpectrumYAxisConfiguration Normalize()
    {
        var value = double.IsFinite(FixedUpperBound)
            ? FixedUpperBound
            : MinimumUpperBound;
        var snapped = Math.Round(value / Step, MidpointRounding.AwayFromZero) * Step;
        return this with
        {
            FixedUpperBound = Math.Clamp(snapped, MinimumUpperBound, MaximumUpperBound),
        };
    }

    public double ResolveUpperBound(double maximumValue)
        => ResolveScale(maximumValue).UpperBound;

    public SpectrumYAxisScale ResolveScale(double maximumValue)
    {
        var normalized = Normalize();
        if (normalized.Mode == SpectrumYAxisMode.Fixed)
        {
            var step = normalized.FixedUpperBound / 5;
            return new SpectrumYAxisScale(
                normalized.FixedUpperBound,
                Enumerable.Range(0, 6).Select(index => index * step).ToArray());
        }

        var requiredUpperBound = double.IsFinite(maximumValue) && maximumValue > 0
            ? Math.Max(1, maximumValue * 1.08)
            : 1;
        var candidate = NiceAutomaticScale(requiredUpperBound);
        return new SpectrumYAxisScale(
            candidate.UpperBound,
            Enumerable.Range(0, candidate.IntervalCount + 1)
                .Select(index => index * candidate.Step)
                .ToArray());
    }

    private static NiceScaleCandidate NiceAutomaticScale(double requiredUpperBound)
    {
        var maximumExponent = Math.Max(
            1,
            (int)Math.Ceiling(Math.Log10(requiredUpperBound)) + 1);
        var candidates = new List<NiceScaleCandidate>();
        for (var exponent = 0; exponent <= maximumExponent; exponent++)
        {
            var magnitude = Math.Pow(10, exponent);
            foreach (var multiplier in new[] { 1.0, 2.0, 2.5, 5.0 })
            {
                var step = multiplier * magnitude;
                if (!double.IsFinite(step) || step < 1 || step != Math.Truncate(step))
                {
                    continue;
                }

                var intervalCountValue = Math.Ceiling(requiredUpperBound / step);
                if (intervalCountValue is < 1 or > int.MaxValue)
                {
                    continue;
                }
                var intervalCount = (int)intervalCountValue;
                candidates.Add(new NiceScaleCandidate(
                    step,
                    intervalCount,
                    step * intervalCount));
            }
        }

        return candidates
            .OrderBy(candidate => candidate.IntervalCount is >= 3 and <= 6 ? 0 : 1)
            .ThenBy(candidate => Math.Abs(candidate.IntervalCount - 5))
            .ThenBy(candidate => candidate.UpperBound - requiredUpperBound)
            .ThenBy(candidate => candidate.Step)
            .First();
    }

    private sealed record NiceScaleCandidate(
        double Step,
        int IntervalCount,
        double UpperBound);
}
