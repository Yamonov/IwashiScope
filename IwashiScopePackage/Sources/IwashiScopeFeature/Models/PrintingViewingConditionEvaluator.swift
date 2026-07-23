import Foundation

struct PrintingViewingConditionEvaluation: Equatable, Sendable {
    enum Status: Equatable, Sendable {
        case meets
        case caution
        case doesNotMeet
        case fails
        case unavailable
    }

    enum IlluminanceClassification: Equatable, Sendable {
        case printComparison
        case displayComparison
        case generalOffice
        case tooDark
        case tooBright
        case unavailable
    }

    struct IndexedColorRenderingValue: Equatable, Sendable {
        let index: Int
        let value: Double
    }

    let mode: MeasurementMode
    let correlatedColorTemperature: Double?
    let chromaticityDistance: Double?
    let chromaticityStatus: Status
    let averageColorRenderingIndex: Double?
    let averageColorRenderingStatus: Status
    let minimumSpecialColorRenderingIndex: IndexedColorRenderingValue?
    let specialColorRenderingStatus: Status
    let illuminance: Double?
    let illuminanceClassification: IlluminanceClassification
    let illuminanceStatus: Status
    let lightSourceStatus: Status
    let viewingConditionStatus: Status

    var requiresAmbientIlluminanceMeasurement: Bool {
        mode == .emissive
    }
}

enum PrintingViewingConditionEvaluator {
    static let standardName = "JSPST-1998"
    static let targetCorrelatedColorTemperature = 5_000.0
    static let targetUPrime = 0.2091
    static let targetVPrime = 0.4881
    static let maximumChromaticityDistance = 0.004
    static let minimumAverageColorRenderingIndex = 95.0
    static let minimumSpecialColorRenderingIndex = 90.0
    static let illuminanceRange = 1_500.0 ... 2_500.0
    static let displayComparisonIlluminanceRange = 375.0 ... 625.0
    static let minimumGeneralOfficeIlluminance = 300.0

    static func evaluate(_ measurement: SpotMeasurement) -> PrintingViewingConditionEvaluation {
        let chromaticityDistance = measurement.xyz.flatMap(chromaticityDistanceFromD50)
        let chromaticityStatus = status(
            for: chromaticityDistance,
            satisfies: { $0 <= maximumChromaticityDistance }
        )

        let averageColorRenderingIndex = measurement.cri?.ra
        let averageColorRenderingStatus = status(
            for: averageColorRenderingIndex,
            satisfies: { $0 >= minimumAverageColorRenderingIndex }
        )

        let minimumSpecialColorRenderingIndex = measurement.cri.flatMap(minimumSpecialColorRenderingIndex)
        let specialColorRenderingStatus = status(
            for: minimumSpecialColorRenderingIndex?.value,
            satisfies: { $0 >= Self.minimumSpecialColorRenderingIndex }
        )

        let illuminanceClassification: PrintingViewingConditionEvaluation.IlluminanceClassification
        if measurement.mode == .ambient {
            illuminanceClassification = classifyIlluminance(measurement.lux)
        } else {
            illuminanceClassification = .unavailable
        }
        let illuminanceStatus = status(for: illuminanceClassification)

        let lightSourceStatus = aggregate([
            chromaticityStatus,
            averageColorRenderingStatus,
            specialColorRenderingStatus,
        ])
        let viewingConditionStatus = aggregate([
            lightSourceStatus,
            illuminanceStatus,
        ])

        return PrintingViewingConditionEvaluation(
            mode: measurement.mode,
            correlatedColorTemperature: measurement.cct,
            chromaticityDistance: chromaticityDistance,
            chromaticityStatus: chromaticityStatus,
            averageColorRenderingIndex: averageColorRenderingIndex,
            averageColorRenderingStatus: averageColorRenderingStatus,
            minimumSpecialColorRenderingIndex: minimumSpecialColorRenderingIndex,
            specialColorRenderingStatus: specialColorRenderingStatus,
            illuminance: measurement.lux,
            illuminanceClassification: illuminanceClassification,
            illuminanceStatus: illuminanceStatus,
            lightSourceStatus: lightSourceStatus,
            viewingConditionStatus: viewingConditionStatus
        )
    }

    private static func chromaticityDistanceFromD50(_ xyz: Vector3) -> Double? {
        let denominator = xyz.first + (15 * xyz.second) + (3 * xyz.third)
        guard denominator.isFinite,
              abs(denominator) > 1e-12 else {
            return nil
        }

        let uPrime = (4 * xyz.first) / denominator
        let vPrime = (9 * xyz.second) / denominator
        guard uPrime.isFinite, vPrime.isFinite else {
            return nil
        }

        return hypot(uPrime - targetUPrime, vPrime - targetVPrime)
    }

    private static func minimumSpecialColorRenderingIndex(
        _ result: CRIResult
    ) -> PrintingViewingConditionEvaluation.IndexedColorRenderingValue? {
        let indices = 9 ... 15
        guard indices.allSatisfy({ result.individual[$0]?.isFinite == true }) else {
            return nil
        }

        return indices
            .compactMap { index in
                result.individual[index].map {
                    PrintingViewingConditionEvaluation.IndexedColorRenderingValue(index: index, value: $0)
                }
            }
            .min { $0.value < $1.value }
    }

    private static func classifyIlluminance(
        _ illuminance: Double?
    ) -> PrintingViewingConditionEvaluation.IlluminanceClassification {
        guard let illuminance, illuminance.isFinite else {
            return .unavailable
        }
        if illuminance < minimumGeneralOfficeIlluminance {
            return .tooDark
        }
        if displayComparisonIlluminanceRange.contains(illuminance) {
            return .displayComparison
        }
        if illuminance > illuminanceRange.lowerBound,
           illuminance < illuminanceRange.upperBound {
            return .printComparison
        }
        if illuminance >= illuminanceRange.upperBound {
            return .tooBright
        }
        return .generalOffice
    }

    private static func status(
        for classification: PrintingViewingConditionEvaluation.IlluminanceClassification
    ) -> PrintingViewingConditionEvaluation.Status {
        switch classification {
        case .printComparison:
            return .meets
        case .displayComparison, .generalOffice:
            return .caution
        case .tooDark, .tooBright:
            return .fails
        case .unavailable:
            return .unavailable
        }
    }

    private static func status(
        for value: Double?,
        satisfies: (Double) -> Bool
    ) -> PrintingViewingConditionEvaluation.Status {
        guard let value, value.isFinite else {
            return .unavailable
        }
        return satisfies(value) ? .meets : .doesNotMeet
    }

    private static func aggregate(
        _ statuses: [PrintingViewingConditionEvaluation.Status]
    ) -> PrintingViewingConditionEvaluation.Status {
        if statuses.contains(.fails) {
            return .fails
        }
        if statuses.contains(.doesNotMeet) {
            return .doesNotMeet
        }
        if statuses.contains(.unavailable) {
            return .unavailable
        }
        if statuses.contains(.caution) {
            return .caution
        }
        if statuses.allSatisfy({ $0 == .meets }) {
            return .meets
        }
        return .unavailable
    }
}
