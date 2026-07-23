import Foundation

struct SpectralSample: Identifiable, Codable, Equatable, Sendable {
    let id: Int
    let wavelength: Double
    let value: Double
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
}

struct CalibrationPrompt: Equatable, Sendable {
    let title: String
    let instruction: String
    let systemImage: String
    let requiresUserConfirmation: Bool
    let allowsSkip: Bool
    let rawText: String
}
