using System.Text.Json;
using System.Text.Json.Serialization;
using IwashiScope.Core.Calculations;
using IwashiScope.Core.Models;
using IwashiScope.Core.Session;

namespace IwashiScope.Protocol;

public sealed class SpotreadOutputParser
{
    public const int SupportedProtocolVersion = 3;
    public const string SupportedImplementation = "IwashiScope spot reader";
    public const int SupportedImplementationVersion = 1;
    public const string SupportedArgyllVersion = "3.5.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    private readonly MeasurementMode _mode;
    private readonly Utf8JsonLineStream _stream = new();
    private readonly Func<DateTimeOffset> _clock;
    private bool _hasAcceptedHello;
    private bool _finished;

    public SpotreadOutputParser(
        MeasurementMode mode,
        Func<DateTimeOffset>? clock = null)
    {
        _mode = mode;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public int BufferedCharacterCount => _stream.BufferedCharacterCount;
    public bool HasAcceptedHello => _hasAcceptedHello;

    public IReadOnlyList<SpotreadEvent> Consume(ReadOnlySpan<byte> bytes)
    {
        if (_finished)
        {
            return [];
        }
        return DecodeLines(_stream.Consume(bytes));
    }

    public IReadOnlyList<SpotreadEvent> Consume(string chunk)
    {
        if (_finished || string.IsNullOrEmpty(chunk))
        {
            return [];
        }
        return DecodeLines(_stream.Consume(chunk));
    }

    public IReadOnlyList<SpotreadEvent> Finish()
    {
        if (_finished)
        {
            return [];
        }
        _finished = true;
        return DecodeLines(_stream.Finish());
    }

    private IReadOnlyList<SpotreadEvent> DecodeLines(IEnumerable<string> lines)
    {
        var events = new List<SpotreadEvent>();
        foreach (var line in lines)
        {
            events.AddRange(Decode(line));
        }
        return events;
    }

    private IReadOnlyList<SpotreadEvent> Decode(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return [];
        }

        ProtocolRecord record;
        try
        {
            record = JsonSerializer.Deserialize<ProtocolRecord>(line, JsonOptions)
                ?? throw new JsonException("Record is null.");
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException or InvalidDataException)
        {
            return [ConfigurationIssue("outputParsingFailure", exception.Message, line)];
        }

        if (record.ProtocolVersion != SupportedProtocolVersion)
        {
            return
            [
                Fatal(
                    "protocolVersion",
                    $"spotread JSON protocol version {record.ProtocolVersion} is not supported.",
                    line),
            ];
        }

        if (record.Event == "hello")
        {
            if (_hasAcceptedHello)
            {
                return [Fatal("duplicateHello", "iwashiscope-spotread sent a duplicate hello event.", line)];
            }
            if (record.Implementation != SupportedImplementation ||
                record.ImplementationVersion != SupportedImplementationVersion ||
                record.ArgyllVersion != SupportedArgyllVersion)
            {
                return
                [
                    Fatal(
                        "implementationMismatch",
                        "The measurement command is not the supported IwashiScope spot reader.",
                        line),
                ];
            }

            _hasAcceptedHello = true;
            return
            [
                new HelloAcceptedEvent(
                    record.ProtocolVersion,
                    record.Implementation,
                    record.ImplementationVersion.Value,
                    record.ArgyllVersion),
            ];
        }

        if (!_hasAcceptedHello)
        {
            return
            [
                Fatal(
                    "missingHello",
                    "iwashiscope-spotread did not send its hello event first.",
                    line),
            ];
        }

        try
        {
            return Events(record, line);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or FormatException or OverflowException)
        {
            return [ConfigurationIssue("outputParsingFailure", exception.Message, line)];
        }
    }

    private IReadOnlyList<SpotreadEvent> Events(ProtocolRecord record, string rawText) =>
        record.Event switch
        {
            "instrument" =>
            [
                new InstrumentIdentityEvent(
                    new SpotreadInstrumentIdentity(record.Name, record.SerialNumber)),
            ],
            "state" => StateEvents(Required(record.State, "state")),
            "calibration" => CalibrationEvents(record, rawText),
            "issue" => IssueEvents(record, rawText),
            "measurement" => [new MeasurementCompletedEvent(Measurement(record))],
            _ => throw new InvalidDataException($"Invalid event '{record.Event}'."),
        };

    private static IReadOnlyList<SpotreadEvent> StateEvents(string state) => state switch
    {
        "ready" => [new MeasurementPromptEvent()],
        "measurementStarted" => [new MeasurementStartedEvent()],
        "savedReadingPrompt" => [new SavedReadingPromptEvent()],
        _ => throw new InvalidDataException($"Invalid state '{state}'."),
    };

    private static IReadOnlyList<SpotreadEvent> CalibrationEvents(
        ProtocolRecord record,
        string rawText) =>
        Required(record.Phase, "phase") switch
        {
            "started" => [new CalibrationStartedEvent()],
            "prompt" =>
            [
                new CalibrationPromptEvent(
                    new CalibrationPrompt
                    {
                        Condition = record.Condition ?? "unknown",
                        Identifier = record.Identifier,
                        RequiresUserConfirmation = record.RequiresConfirmation ?? true,
                        AllowsSkip = record.Optional ?? false,
                        RawText = rawText,
                    }),
            ],
            "completed" => [new CalibrationCompletedEvent(false)],
            "skipped" => [new CalibrationCompletedEvent(true)],
            "failed" =>
            [
                new RecoverableIssueEvent(
                    Issue(
                        SpotreadIssueKind.CalibrationFailure,
                        SpotreadRecoveryAction.RetryCalibration,
                        "calibrationFailure",
                        record.Reason ?? "Unknown calibration failure",
                        rawText)),
            ],
            "aborted" =>
            [
                new RecoverableIssueEvent(
                    Issue(
                        SpotreadIssueKind.CalibrationFailure,
                        SpotreadRecoveryAction.RetryCalibration,
                        "calibrationAborted",
                        "Calibration was aborted.",
                        rawText)),
            ],
            _ => throw new InvalidDataException($"Invalid calibration phase '{record.Phase}'."),
        };

    private static IReadOnlyList<SpotreadEvent> IssueEvents(
        ProtocolRecord record,
        string rawText)
    {
        var code = Required(record.Code, "issue code");
        var reason = record.Reason ?? code;
        return code switch
        {
            "misread" =>
            [
                new RecoverableIssueEvent(
                    Issue(
                        MisreadKind(reason),
                        SpotreadRecoveryAction.ResumeMeasurementLoop,
                        code,
                        reason,
                        rawText)),
            ],
            "communicationFailure" =>
            [
                new RecoverableIssueEvent(
                    Issue(
                        SpotreadIssueKind.CommunicationFailure,
                        SpotreadRecoveryAction.ResumeMeasurementLoop,
                        code,
                        reason,
                        rawText)),
            ],
            "userStopped" =>
            [
                new RecoverableIssueEvent(
                    Issue(
                        SpotreadIssueKind.UserStopped,
                        SpotreadRecoveryAction.ResumeMeasurementLoop,
                        code,
                        reason,
                        rawText)),
            ],
            "wrongConfiguration" =>
            [
                new ConfigurationIssueEvent(
                    Issue(
                        SpotreadIssueKind.WrongConfiguration,
                        SpotreadRecoveryAction.AcknowledgeConfiguration,
                        code,
                        reason,
                        rawText)),
            ],
            "operationFailure" =>
            [
                new RecoverableIssueEvent(
                    Issue(
                        SpotreadIssueKind.OperationFailure,
                        SpotreadRecoveryAction.RetryOperation,
                        code,
                        reason,
                        rawText)),
            ],
            "needsCalibration" => [new CalibrationStartedEvent()],
            "fatalInstrumentError" => [Fatal(code, reason, rawText)],
            _ => throw new InvalidDataException($"Invalid issue code '{code}'."),
        };
    }

    private SpotMeasurement Measurement(ProtocolRecord record)
    {
        var recordedMode = Required(record.Mode, "measurement mode");
        if (recordedMode != _mode.ProtocolName())
        {
            throw new InvalidDataException(
                $"Measurement mode '{recordedMode}' does not match '{_mode.ProtocolName()}'.");
        }

        var spectrumPayload = record.Spectrum;
        var spectrumValues = spectrumPayload?.Values ?? [];
        var spectrumStart = spectrumPayload?.StartNm ?? 0;
        var spectrumEnd = spectrumPayload?.EndNm ?? 0;
        var spectrum = SpectralSamples(spectrumValues, spectrumStart, spectrumEnd);
        var practical = PracticalRange(spectrumPayload, spectrumStart, spectrumEnd);
        var peak = spectrum.MaxBy(sample => sample.Value);

        var xyz = record.Xyz is null ? null : Vector(record.Xyz);
        var lab = record.Lab is null ? null : Vector(record.Lab);
        var monochrome = record.Monochrome is null
            ? null
            : new MonochromeResult(record.Monochrome.Y, record.Monochrome.LStar);
        if (!((xyz is not null && lab is not null) || monochrome is not null))
        {
            throw new InvalidDataException("Measurement needs XYZ/Lab or monochrome.");
        }

        var lightingIssues = new HashSet<LightingMetricIssue>();
        foreach (var issue in record.LightingMetricIssues ?? [])
        {
            lightingIssues.Add(issue switch
            {
                "invalidCCT" => LightingMetricIssue.InvalidCct,
                "invalidPlanckianTemperature" => LightingMetricIssue.InvalidPlanckianTemperature,
                "invalidDaylightTemperature" => LightingMetricIssue.InvalidDaylightTemperature,
                _ => throw new InvalidDataException($"Unknown lighting metric issue '{issue}'."),
            });
        }

        CriResult? cri = null;
        if (record.Cri is { } criPayload)
        {
            if (criPayload.Individual.Count != 14)
            {
                throw new InvalidDataException("CRI needs exactly 14 individual values.");
            }
            var individual = criPayload.Individual
                .Select((value, index) => new KeyValuePair<int, double>(index + 1, value))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            cri = new CriResult
            {
                Ra = criPayload.Ra,
                R9 = individual[9],
                Individual = individual,
                Caution = criPayload.Caution,
            };
        }

        var tm30 = record.Tm30 is null ? null : Tm30(record.Tm30);
        var measurement = new SpotMeasurement
        {
            CapturedAt = _clock(),
            Mode = _mode,
            SpectrumStart = spectrumStart,
            SpectrumEnd = spectrumEnd,
            PracticalSpectrumRange = practical,
            DeclaredStepCount = spectrum.Count,
            Spectrum = spectrum,
            PeakValue = peak?.Value,
            PeakWavelength = peak?.Wavelength,
            Xyz = xyz,
            Lab = lab,
            LabWhitePoint = record.LabWhitePoint,
            Monochrome = monochrome,
            Lux = record.Lux,
            Cct = record.Cct,
            Duv = record.Duv,
            SuggestedEv100 = record.SuggestedEv100,
            ClosestPlanckian = record.ClosestPlanckian is null
                ? null
                : new TemperatureMatch(
                    record.ClosestPlanckian.Kelvin,
                    record.ClosestPlanckian.DeltaE2000),
            ClosestDaylight = record.ClosestDaylight is null
                ? null
                : new TemperatureMatch(
                    record.ClosestDaylight.Kelvin,
                    record.ClosestDaylight.DeltaE2000),
            LightingMetricIssues = lightingIssues,
            Cri = cri,
            Tlci = record.Tlci is null
                ? null
                : new TlciResult(record.Tlci.Qa, record.Tlci.Caution),
            Tm30 = tm30,
        };
        return ColorRenderingIndexCalculator.AddR15(measurement);
    }

    private static Tm30Result? Tm30(Tm30Payload payload)
    {
        if (payload.Status == "error")
        {
            return null;
        }
        if (payload.Rf is null || payload.Rg is null ||
            payload.Cct is null || payload.Duv is null ||
            payload.Bins is not { Count: 16 } ||
            payload.Samples is not { Count: 99 })
        {
            throw new InvalidDataException("TM-30 needs Rf/Rg/CCT/Duv, 16 bins and 99 samples.");
        }

        var status = payload.Status switch
        {
            "valid" => Tm30Status.Valid,
            "caution" => Tm30Status.Caution,
            _ => throw new InvalidDataException($"Invalid TM-30 status '{payload.Status}'."),
        };
        var bins = payload.Bins.Select((bin, offset) =>
        {
            if (bin.Index != offset + 1)
            {
                throw new InvalidDataException("TM-30 hue-bin indices must be ordered 1...16.");
            }
            return new Tm30HueBin(bin.Index, Vector(bin.ReferenceJab), Vector(bin.TestJab));
        }).ToArray();
        var samples = payload.Samples.Select((sample, offset) =>
        {
            if (sample.Index != offset + 1)
            {
                throw new InvalidDataException(
                    "TM-30 evaluation-sample indices must be ordered 1...99.");
            }
            return new Tm30EvaluationSample(
                sample.Index,
                Vector(sample.ReferenceJab),
                Vector(sample.TestJab));
        }).ToArray();
        return new Tm30Result
        {
            FidelityIndex = payload.Rf.Value,
            GamutIndex = payload.Rg.Value,
            Cct = payload.Cct.Value,
            Duv = payload.Duv.Value,
            Status = status,
            HueBins = bins,
            EvaluationSamples = samples,
        };
    }

    private static IReadOnlyList<SpectralSample> SpectralSamples(
        IReadOnlyList<double> values,
        double start,
        double end)
    {
        var denominator = Math.Max(values.Count - 1, 1);
        return values.Select((value, index) =>
            new SpectralSample(
                index,
                values.Count == 1
                    ? start
                    : start + index * (end - start) / denominator,
                value)).ToArray();
    }

    private static WavelengthRange? PracticalRange(
        SpectrumPayload? payload,
        double spectrumStart,
        double spectrumEnd)
    {
        if (payload is null ||
            (payload.PracticalStartNm is null && payload.PracticalEndNm is null))
        {
            return null;
        }
        if (payload.PracticalStartNm is null || payload.PracticalEndNm is null)
        {
            throw new InvalidDataException("Both practical spectrum boundaries are required.");
        }

        var range = new WavelengthRange(
            payload.PracticalStartNm.Value,
            payload.PracticalEndNm.Value);
        if (!range.IsValidWithin(spectrumStart, spectrumEnd))
        {
            throw new InvalidDataException("Invalid practical spectrum range.");
        }
        return range;
    }

    private static Vector3 Vector(IReadOnlyList<double> values)
    {
        if (values.Count != 3)
        {
            throw new InvalidDataException("A color vector needs exactly three components.");
        }
        var result = new Vector3(values[0], values[1], values[2]);
        if (!result.IsFinite)
        {
            throw new InvalidDataException("Color vector components must be finite.");
        }
        return result;
    }

    private static SpotreadIssueKind MisreadKind(string reason)
    {
        var normalized = reason.ToLowerInvariant();
        if (normalized.Contains("saturated", StringComparison.Ordinal))
        {
            return SpotreadIssueKind.SensorSaturated;
        }
        if (normalized.Contains("inconsistent", StringComparison.Ordinal))
        {
            return SpotreadIssueKind.InconsistentReading;
        }
        return SpotreadIssueKind.MeasurementFailure;
    }

    private static T Required<T>(T? value, string name) where T : class =>
        value ?? throw new InvalidDataException($"Missing {name}.");

    private static SpotreadIssue Issue(
        SpotreadIssueKind kind,
        SpotreadRecoveryAction recovery,
        string code,
        string reason,
        string rawText) =>
        new()
        {
            Kind = kind,
            RecoveryAction = recovery,
            Code = code,
            Reason = reason,
            RawText = rawText,
        };

    private static ConfigurationIssueEvent ConfigurationIssue(
        string code,
        string reason,
        string rawText) =>
        new(
            Issue(
                SpotreadIssueKind.OutputParsingFailure,
                SpotreadRecoveryAction.AcknowledgeConfiguration,
                code,
                reason,
                rawText));

    private static FatalIssueEvent Fatal(string code, string reason, string rawText) =>
        new(
            Issue(
                SpotreadIssueKind.FatalFailure,
                SpotreadRecoveryAction.Restart,
                code,
                reason,
                rawText));
}

internal sealed record ProtocolRecord
{
    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; init; }
    [JsonPropertyName("event")]
    public required string Event { get; init; }
    [JsonPropertyName("implementation")]
    public string? Implementation { get; init; }
    [JsonPropertyName("implementationVersion")]
    public int? ImplementationVersion { get; init; }
    [JsonPropertyName("argyllVersion")]
    public string? ArgyllVersion { get; init; }
    [JsonPropertyName("name")]
    public string? Name { get; init; }
    [JsonPropertyName("serialNumber")]
    public string? SerialNumber { get; init; }
    [JsonPropertyName("state")]
    public string? State { get; init; }
    [JsonPropertyName("phase")]
    public string? Phase { get; init; }
    [JsonPropertyName("condition")]
    public string? Condition { get; init; }
    [JsonPropertyName("identifier")]
    public string? Identifier { get; init; }
    [JsonPropertyName("optional")]
    public bool? Optional { get; init; }
    [JsonPropertyName("requiresConfirmation")]
    public bool? RequiresConfirmation { get; init; }
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
    [JsonPropertyName("code")]
    public string? Code { get; init; }
    [JsonPropertyName("mode")]
    public string? Mode { get; init; }
    [JsonPropertyName("spectrum")]
    public SpectrumPayload? Spectrum { get; init; }
    [JsonPropertyName("xyz")]
    public IReadOnlyList<double>? Xyz { get; init; }
    [JsonPropertyName("lab")]
    public IReadOnlyList<double>? Lab { get; init; }
    [JsonPropertyName("labWhitePoint")]
    public string? LabWhitePoint { get; init; }
    [JsonPropertyName("monochrome")]
    public MonochromePayload? Monochrome { get; init; }
    [JsonPropertyName("lux")]
    public double? Lux { get; init; }
    [JsonPropertyName("cct")]
    public double? Cct { get; init; }
    [JsonPropertyName("duv")]
    public double? Duv { get; init; }
    [JsonPropertyName("suggestedEV100")]
    public double? SuggestedEv100 { get; init; }
    [JsonPropertyName("closestPlanckian")]
    public TemperaturePayload? ClosestPlanckian { get; init; }
    [JsonPropertyName("closestDaylight")]
    public TemperaturePayload? ClosestDaylight { get; init; }
    [JsonPropertyName("lightingMetricIssues")]
    public IReadOnlyList<string>? LightingMetricIssues { get; init; }
    [JsonPropertyName("cri")]
    public CriPayload? Cri { get; init; }
    [JsonPropertyName("tlci")]
    public TlciPayload? Tlci { get; init; }
    [JsonPropertyName("tm30")]
    public Tm30Payload? Tm30 { get; init; }
}

internal sealed record SpectrumPayload
{
    [JsonPropertyName("startNm")]
    public double StartNm { get; init; }
    [JsonPropertyName("endNm")]
    public double EndNm { get; init; }
    [JsonPropertyName("practicalStartNm")]
    public double? PracticalStartNm { get; init; }
    [JsonPropertyName("practicalEndNm")]
    public double? PracticalEndNm { get; init; }
    [JsonPropertyName("values")]
    public IReadOnlyList<double> Values { get; init; } = [];
}

internal sealed record MonochromePayload
{
    [JsonPropertyName("y")]
    public double Y { get; init; }
    [JsonPropertyName("lStar")]
    public double LStar { get; init; }
}

internal sealed record TemperaturePayload
{
    [JsonPropertyName("kelvin")]
    public double Kelvin { get; init; }
    [JsonPropertyName("deltaE2000")]
    public double DeltaE2000 { get; init; }
}

internal sealed record CriPayload
{
    [JsonPropertyName("ra")]
    public double Ra { get; init; }
    [JsonPropertyName("individual")]
    public IReadOnlyList<double> Individual { get; init; } = [];
    [JsonPropertyName("caution")]
    public bool Caution { get; init; }
}

internal sealed record TlciPayload
{
    [JsonPropertyName("qa")]
    public double Qa { get; init; }
    [JsonPropertyName("caution")]
    public bool Caution { get; init; }
}

internal sealed record Tm30Payload
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }
    [JsonPropertyName("rf")]
    public double? Rf { get; init; }
    [JsonPropertyName("rg")]
    public double? Rg { get; init; }
    [JsonPropertyName("cct")]
    public double? Cct { get; init; }
    [JsonPropertyName("duv")]
    public double? Duv { get; init; }
    [JsonPropertyName("bins")]
    public IReadOnlyList<Tm30HueBinPayload>? Bins { get; init; }
    [JsonPropertyName("samples")]
    public IReadOnlyList<Tm30SamplePayload>? Samples { get; init; }
}

internal sealed record Tm30HueBinPayload
{
    [JsonPropertyName("index")]
    public int Index { get; init; }
    [JsonPropertyName("referenceJab")]
    public IReadOnlyList<double> ReferenceJab { get; init; } = [];
    [JsonPropertyName("testJab")]
    public IReadOnlyList<double> TestJab { get; init; } = [];
}

internal sealed record Tm30SamplePayload
{
    [JsonPropertyName("index")]
    public int Index { get; init; }
    [JsonPropertyName("referenceJab")]
    public IReadOnlyList<double> ReferenceJab { get; init; } = [];
    [JsonPropertyName("testJab")]
    public IReadOnlyList<double> TestJab { get; init; } = [];
}
