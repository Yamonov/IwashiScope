using System.Buffers.Binary;
using System.Text.Json;
using IwashiScope.Core.Models;

namespace IwashiScope.Tests;

public sealed class AveragedMeasurementTests
{
    [Fact]
    public void SixthReadingReevaluatesAndRejectsTheFirstOutlier()
    {
        var accumulator = new AveragingMeasurementAccumulator();
        var baseline = TestMeasurementFactory.Create(MeasurementMode.Ambient, 10);
        var brightFirst = Scaled(baseline, 1.30);

        Assert.Equal(
            AveragingSampleDecisionKind.Accepted,
            accumulator.Add(brightFirst).Kind);
        for (var index = 0; index < 4; index++)
        {
            Assert.Equal(
                AveragingSampleDecisionKind.Accepted,
                accumulator.Add(Scaled(baseline, 1 + index * 0.001)).Kind);
        }

        var sixth = accumulator.Add(Scaled(baseline, 0.999));

        Assert.Equal(AveragingSampleDecisionKind.Accepted, sixth.Kind);
        Assert.Equal(5, accumulator.AcceptedCount);
        Assert.Equal(6, accumulator.MeasurementAttemptCount);
        Assert.Equal(1, accumulator.OutlierCount);
        Assert.Equal(1, accumulator.LastRetrospectivelyRejectedCount);
        Assert.DoesNotContain(
            accumulator.AcceptedMeasurements,
            measurement => ReferenceEquals(measurement, brightFirst));
    }

    [Fact]
    public void ConvergenceAndProgressBeginAtSixAcceptedReadings()
    {
        var accumulator = new AveragingMeasurementAccumulator();
        var baseline = TestMeasurementFactory.Create(MeasurementMode.Ambient, 30);

        for (var index = 0; index < 5; index++)
        {
            accumulator.Add(Scaled(baseline, 1 + index * 0.001));
        }
        Assert.Null(accumulator.Convergence);
        Assert.False(accumulator.CanOutputAverage);

        accumulator.Add(Scaled(baseline, 1.002));

        Assert.True(accumulator.CanOutputAverage);
        Assert.NotNull(accumulator.Convergence);
        Assert.InRange(accumulator.Convergence!.Progress, 0, 1);
        Assert.Equal(AveragingProgressTier.Minimum, accumulator.ProgressTier);
    }

    [Fact]
    public void AnalysisRequestIsBigEndianFramedAndContainsTheAveragedSpectrum()
    {
        var accumulator = new AveragingMeasurementAccumulator();
        var baseline = TestMeasurementFactory.Create(MeasurementMode.Reflectance, 41);
        accumulator.Add(Scaled(baseline, 1.00));
        accumulator.Add(Scaled(baseline, 0.98));

        var request = accumulator.MakeAnalysisRequest("request-1");
        var frame = request.FramedData();
        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(0, 4));

        Assert.Equal((uint)(frame.Length - 4), payloadLength);
        using var document = JsonDocument.Parse(frame.AsMemory(4));
        var root = document.RootElement;
        Assert.Equal(3, root.GetProperty("protocolVersion").GetInt32());
        Assert.Equal("analyzeSpectrum", root.GetProperty("command").GetString());
        Assert.Equal("request-1", root.GetProperty("requestId").GetString());
        Assert.Equal("reflectance", root.GetProperty("mode").GetString());
        Assert.Equal(2, root.GetProperty("sampleCount").GetInt32());
        Assert.Equal(100, root.GetProperty("spectrum").GetProperty("norm").GetDouble());
        Assert.Equal(
            baseline.Spectrum.Count,
            root.GetProperty("spectrum").GetProperty("values").GetArrayLength());
    }

    [Fact]
    public void InvalidSpectralGridIncrementsActualCountWithoutBeingAccepted()
    {
        var accumulator = new AveragingMeasurementAccumulator();
        var first = TestMeasurementFactory.Create(MeasurementMode.Reflectance, 50);
        accumulator.Add(first);
        var incompatible = first with
        {
            SpectrumEnd = first.SpectrumEnd - 5,
        };

        var decision = accumulator.Add(incompatible);

        Assert.Equal(AveragingSampleDecisionKind.Incompatible, decision.Kind);
        Assert.Equal(1, accumulator.AcceptedCount);
        Assert.Equal(2, accumulator.MeasurementAttemptCount);
        Assert.Equal(1, accumulator.InvalidMeasurementCount);
    }

    private static SpotMeasurement Scaled(SpotMeasurement measurement, double scale)
    {
        var spectrum = measurement.Spectrum
            .Select(sample => sample with { Value = sample.Value * scale })
            .ToArray();
        var peak = spectrum.MaxBy(sample => sample.Value)!;
        return measurement with
        {
            Spectrum = spectrum,
            PeakValue = peak.Value,
            PeakWavelength = peak.Wavelength,
        };
    }
}
