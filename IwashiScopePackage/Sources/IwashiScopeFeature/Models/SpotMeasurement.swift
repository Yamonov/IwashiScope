import Foundation

struct SpectralSample: Identifiable, Codable, Equatable, Sendable {
    let id: Int
    let wavelength: Double
    let value: Double
}

struct WavelengthRange: Codable, Equatable, Sendable {
    let start: Double
    let end: Double

    var closedRange: ClosedRange<Double> {
        start...end
    }
}

struct Vector3: Codable, Equatable, Sendable {
    let first: Double
    let second: Double
    let third: Double
}

struct TemperatureMatch: Codable, Equatable, Sendable {
    let kelvin: Double
    let deltaE2000: Double
}

struct CRIResult: Codable, Equatable, Sendable {
    let ra: Double
    let r9: Double?
    let individual: [Int: Double]
    let caution: Bool
}

struct TLCIResult: Codable, Equatable, Sendable {
    let qa: Double
    let caution: Bool
}

enum TM30Status: String, Codable, Equatable, Sendable {
    case valid
    case caution
}

struct TM30HueBin: Identifiable, Codable, Equatable, Sendable {
    let index: Int
    let referenceJab: Vector3
    let testJab: Vector3

    var id: Int { index }
}

struct TM30EvaluationSample: Identifiable, Codable, Equatable, Sendable {
    let index: Int
    let referenceJab: Vector3
    let testJab: Vector3

    var id: Int { index }
}

struct TM30Result: Codable, Equatable, Sendable {
    let fidelityIndex: Double
    let gamutIndex: Double
    let cct: Double
    let duv: Double
    let status: TM30Status
    let hueBins: [TM30HueBin]
    let evaluationSamples: [TM30EvaluationSample]

    var caution: Bool { status == .caution }
}

struct MonochromeResult: Codable, Equatable, Sendable {
    let y: Double
    let lStar: Double
}

enum LightingMetricIssue: String, Codable, Hashable, Sendable {
    case invalidCCT
    case invalidPlanckianTemperature
    case invalidDaylightTemperature
}

struct SpotMeasurement: Codable, Equatable, Sendable {
    let capturedAt: Date
    let mode: MeasurementMode
    let spectrumStart: Double
    let spectrumEnd: Double
    let practicalSpectrumRange: WavelengthRange?
    let declaredStepCount: Int
    let spectrum: [SpectralSample]
    let peakValue: Double?
    let peakWavelength: Double?
    let xyz: Vector3?
    let lab: Vector3?
    let labWhitePoint: String?
    let monochrome: MonochromeResult?
    let lux: Double?
    let cct: Double?
    let duv: Double?
    let suggestedEV100: Double?
    let closestPlanckian: TemperatureMatch?
    let closestDaylight: TemperatureMatch?
    let lightingMetricIssues: Set<LightingMetricIssue>
    let cri: CRIResult?
    let tlci: TLCIResult?
    let tm30: TM30Result?

    init(
        capturedAt: Date,
        mode: MeasurementMode,
        spectrumStart: Double,
        spectrumEnd: Double,
        practicalSpectrumRange: WavelengthRange? = nil,
        declaredStepCount: Int,
        spectrum: [SpectralSample],
        peakValue: Double?,
        peakWavelength: Double?,
        xyz: Vector3?,
        lab: Vector3?,
        labWhitePoint: String?,
        monochrome: MonochromeResult?,
        lux: Double?,
        cct: Double?,
        duv: Double?,
        suggestedEV100: Double?,
        closestPlanckian: TemperatureMatch?,
        closestDaylight: TemperatureMatch?,
        lightingMetricIssues: Set<LightingMetricIssue>,
        cri: CRIResult?,
        tlci: TLCIResult?,
        tm30: TM30Result?
    ) {
        self.capturedAt = capturedAt
        self.mode = mode
        self.spectrumStart = spectrumStart
        self.spectrumEnd = spectrumEnd
        self.practicalSpectrumRange = practicalSpectrumRange
        self.declaredStepCount = declaredStepCount
        self.spectrum = spectrum
        self.peakValue = peakValue
        self.peakWavelength = peakWavelength
        self.xyz = xyz
        self.lab = lab
        self.labWhitePoint = labWhitePoint
        self.monochrome = monochrome
        self.lux = lux
        self.cct = cct
        self.duv = duv
        self.suggestedEV100 = suggestedEV100
        self.closestPlanckian = closestPlanckian
        self.closestDaylight = closestDaylight
        self.lightingMetricIssues = lightingMetricIssues
        self.cri = cri
        self.tlci = tlci
        self.tm30 = tm30
    }

    var validatedPracticalSpectrumRange: WavelengthRange? {
        guard let practicalSpectrumRange,
              practicalSpectrumRange.start.isFinite,
              practicalSpectrumRange.end.isFinite,
              practicalSpectrumRange.start <= practicalSpectrumRange.end,
              practicalSpectrumRange.start >= spectrumStart,
              practicalSpectrumRange.end <= spectrumEnd else {
            return nil
        }
        return practicalSpectrumRange
    }
}

struct CalibrationPrompt: Equatable, Sendable {
    let title: String
    let instruction: String
    let systemImage: String
    let requiresUserConfirmation: Bool
    let allowsSkip: Bool
    let rawText: String
}
