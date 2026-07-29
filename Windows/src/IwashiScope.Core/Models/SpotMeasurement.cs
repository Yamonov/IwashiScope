using System.Text.Json.Serialization;

namespace IwashiScope.Core.Models;

public sealed record SpectralSample(int Id, double Wavelength, double Value);

public sealed record WavelengthRange(double Start, double End)
{
    public bool IsValidWithin(double spectrumStart, double spectrumEnd) =>
        double.IsFinite(Start) &&
        double.IsFinite(End) &&
        Start <= End &&
        Start >= spectrumStart &&
        End <= spectrumEnd;

    public bool Contains(double wavelength) => wavelength >= Start && wavelength <= End;
}

public sealed record Vector3(double First, double Second, double Third)
{
    public bool IsFinite =>
        double.IsFinite(First) &&
        double.IsFinite(Second) &&
        double.IsFinite(Third);
}

public sealed record TemperatureMatch(double Kelvin, double DeltaE2000);

public sealed record CriResult
{
    public required double Ra { get; init; }
    public double? R9 { get; init; }
    public IReadOnlyDictionary<int, double> Individual { get; init; } =
        new Dictionary<int, double>();
    public bool Caution { get; init; }
}

public sealed record TlciResult(double Qa, bool Caution);

public enum Tm30Status
{
    Valid,
    Caution,
}

public sealed record Tm30HueBin(int Index, Vector3 ReferenceJab, Vector3 TestJab);

public sealed record Tm30EvaluationSample(int Index, Vector3 ReferenceJab, Vector3 TestJab);

public sealed record Tm30Result
{
    public required double FidelityIndex { get; init; }
    public required double GamutIndex { get; init; }
    public required double Cct { get; init; }
    public required double Duv { get; init; }
    public required Tm30Status Status { get; init; }
    public IReadOnlyList<Tm30HueBin> HueBins { get; init; } = [];
    public IReadOnlyList<Tm30EvaluationSample> EvaluationSamples { get; init; } = [];
    public bool Caution => Status == Tm30Status.Caution;
}

public sealed record MonochromeResult(double Y, double LStar);

public enum LightingMetricIssue
{
    InvalidCct,
    InvalidPlanckianTemperature,
    InvalidDaylightTemperature,
}

public sealed record SpotMeasurement
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    public required MeasurementMode Mode { get; init; }
    public required double SpectrumStart { get; init; }
    public required double SpectrumEnd { get; init; }
    public WavelengthRange? PracticalSpectrumRange { get; init; }
    public required int DeclaredStepCount { get; init; }
    public IReadOnlyList<SpectralSample> Spectrum { get; init; } = [];
    public double? PeakValue { get; init; }
    public double? PeakWavelength { get; init; }
    public Vector3? Xyz { get; init; }
    public Vector3? Lab { get; init; }
    public string? LabWhitePoint { get; init; }
    public MonochromeResult? Monochrome { get; init; }
    public double? Lux { get; init; }
    public double? Cct { get; init; }
    public double? Duv { get; init; }
    [JsonPropertyName("suggestedEV100")]
    public double? SuggestedEv100 { get; init; }
    public TemperatureMatch? ClosestPlanckian { get; init; }
    public TemperatureMatch? ClosestDaylight { get; init; }
    public HashSet<LightingMetricIssue> LightingMetricIssues { get; init; } = [];
    public CriResult? Cri { get; init; }
    public TlciResult? Tlci { get; init; }
    public Tm30Result? Tm30 { get; init; }

    [JsonIgnore]
    public WavelengthRange? ValidatedPracticalSpectrumRange =>
        PracticalSpectrumRange?.IsValidWithin(SpectrumStart, SpectrumEnd) == true
            ? PracticalSpectrumRange
            : null;

    public IReadOnlyList<SpectralSample> DisplaySpectrum(bool usePracticalRange)
    {
        if (!usePracticalRange || ValidatedPracticalSpectrumRange is not { } range)
        {
            return Spectrum;
        }

        return Spectrum.Where(sample => range.Contains(sample.Wavelength)).ToArray();
    }
}

public sealed record SpotreadInstrumentIdentity(string? Name, string? SerialNumber)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? "Unknown instrument"
        : Name;
}
