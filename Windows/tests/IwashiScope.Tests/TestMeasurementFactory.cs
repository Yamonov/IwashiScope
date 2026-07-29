using IwashiScope.Core.Models;

namespace IwashiScope.Tests;

internal static class TestMeasurementFactory
{
    public static SpotMeasurement Create(
        MeasurementMode mode,
        int seed = 1,
        DateTimeOffset? capturedAt = null)
    {
        var random = new Random(seed);
        const double start = 360;
        const double end = 780;
        var values = Enumerable.Range(0, 85)
            .Select(index =>
            {
                var wavelength = start + index * 5;
                var peak = mode == MeasurementMode.Reflectance ? 545 : 455;
                var width = mode == MeasurementMode.Reflectance ? 95 : 45;
                var baseline = mode == MeasurementMode.Reflectance ? 0.18 : 0.05;
                var amplitude = mode == MeasurementMode.Reflectance ? 0.62 : 0.95;
                var value = baseline +
                    amplitude * Math.Exp(-Math.Pow(wavelength - peak, 2) / (2 * width * width)) +
                    (random.NextDouble() - 0.5) * 0.015;
                return new SpectralSample(index, wavelength, Math.Max(0, value));
            })
            .ToArray();
        var highest = values.MaxBy(sample => sample.Value)!;

        var cri = mode.IsLighting()
            ? new CriResult
            {
                Ra = 96.4,
                R9 = 92.1,
                Individual = Enumerable.Range(1, 15)
                    .ToDictionary(index => index, index => 97.8 - index * 0.38),
                Caution = false,
            }
            : null;
        var bins = Enumerable.Range(1, 16)
            .Select(index =>
            {
                var angle = 2 * Math.PI * (index - 1) / 16;
                var reference = new Vector3(55, Math.Cos(angle) * 28, Math.Sin(angle) * 28);
                var test = new Vector3(
                    55 + Math.Sin(angle * 2) * 1.2,
                    Math.Cos(angle) * (27 + Math.Sin(angle) * 2),
                    Math.Sin(angle) * (27 + Math.Cos(angle) * 2));
                return new Tm30HueBin(index, reference, test);
            })
            .ToArray();
        var samples = Enumerable.Range(1, 99)
            .Select(index =>
            {
                var angle = 2 * Math.PI * (index - 1) / 99;
                var radius = 12 + index % 7 * 3;
                return new Tm30EvaluationSample(
                    index,
                    new Vector3(35 + index % 55, Math.Cos(angle) * radius, Math.Sin(angle) * radius),
                    new Vector3(
                        35 + index % 55 + Math.Sin(angle * 3),
                        Math.Cos(angle) * (radius + 1.2),
                        Math.Sin(angle) * (radius - 0.8)));
            })
            .ToArray();
        var tm30 = mode.IsLighting()
            ? new Tm30Result
            {
                FidelityIndex = 95.8,
                GamutIndex = 101.2,
                Cct = 5024,
                Duv = 0.0012,
                Status = Tm30Status.Valid,
                HueBins = bins,
                EvaluationSamples = samples,
            }
            : null;

        return new SpotMeasurement
        {
            CapturedAt = capturedAt ?? DateTimeOffset.UtcNow,
            Mode = mode,
            SpectrumStart = start,
            SpectrumEnd = end,
            PracticalSpectrumRange = new WavelengthRange(400, 700),
            DeclaredStepCount = values.Length,
            Spectrum = values,
            PeakValue = highest.Value,
            PeakWavelength = highest.Wavelength,
            Xyz = mode == MeasurementMode.Reflectance
                ? new Vector3(41.25, 44.82, 28.11)
                : new Vector3(95.4, 100, 82.7),
            Lab = mode == MeasurementMode.Reflectance
                ? new Vector3(72.8, -18.4, 32.7)
                : new Vector3(100, -0.1, 0.2),
            LabWhitePoint = "D50",
            Lux = mode.IsLighting() ? 1850 : null,
            Cct = mode.IsLighting() ? 5024 : null,
            Duv = mode.IsLighting() ? 0.0012 : null,
            SuggestedEv100 = mode.IsLighting() ? 10.5 : null,
            ClosestPlanckian = mode.IsLighting() ? new TemperatureMatch(4998, 1.2) : null,
            ClosestDaylight = mode.IsLighting() ? new TemperatureMatch(5030, 0.9) : null,
            Cri = cri,
            Tlci = mode.IsLighting() ? new TlciResult(96.7, false) : null,
            Tm30 = tm30,
        };
    }
}
