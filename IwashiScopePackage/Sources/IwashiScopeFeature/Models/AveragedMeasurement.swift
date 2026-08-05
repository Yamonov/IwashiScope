import Foundation

enum AveragingProgressTier: Equatable, Sendable {
    case insufficient
    case minimum
    case recommended
    case sufficient
}

enum AveragingConvergenceTier: String, Codable, Equatable, Sendable {
    case highVariation
    case converging
    case stable
    case sufficientlyStable

    var localizedName: String {
        switch self {
        case .highVariation:
            String(localized: "変動大")
        case .converging:
            String(localized: "収束中")
        case .stable:
            String(localized: "安定")
        case .sufficientlyStable:
            String(localized: "十分に安定")
        }
    }
}

struct AveragingConvergence: Codable, Equatable, Sendable {
    let relative95Uncertainty: Double
    let tier: AveragingConvergenceTier

    var relative95UncertaintyPercent: Double {
        relative95Uncertainty * 100
    }

    /// Maps the useful 3.0%...0.75% interval to a continuous progress bar.
    var progress: Double {
        let highVariationBoundary = 0.03
        let sufficientlyStableBoundary = 0.0075
        let rawProgress = (highVariationBoundary - relative95Uncertainty)
            / (highVariationBoundary - sufficientlyStableBoundary)
        return min(max(rawProgress, 0), 1)
    }
}

enum AveragingSampleDecision: Equatable, Sendable {
    case accepted
    case outlier(distance: Double, threshold: Double)
    case incompatible(reason: String)
}

enum AveragingMeasurementError: LocalizedError, Equatable {
    case noMeasurements
    case invalidSpectrum
    case payloadTooLarge

    var errorDescription: String? {
        switch self {
        case .noMeasurements:
            String(localized: "平均化できる測定がありません。")
        case .invalidSpectrum:
            String(localized: "平均スペクトルを作成できませんでした。")
        case .payloadTooLarge:
            String(localized: "平均スペクトルのデータ量が大きすぎます。")
        }
    }
}

struct SpectrumAnalysisRequest: Encodable, Equatable, Sendable {
    private static let maximumPayloadLength = Int(UInt32.max)

    let protocolVersion = 3
    let command = "analyzeSpectrum"
    let requestId: String
    let mode: MeasurementMode
    let sampleCount: Int
    let spectrum: Spectrum

    struct Spectrum: Encodable, Equatable, Sendable {
        let startNm: Double
        let endNm: Double
        let norm: Double
        let practicalStartNm: Double?
        let practicalEndNm: Double?
        let values: [Double]
    }

    func framedData() throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        let payload = try encoder.encode(self)
        guard payload.count <= Self.maximumPayloadLength else {
            throw AveragingMeasurementError.payloadTooLarge
        }

        var payloadLength = UInt32(payload.count).bigEndian
        var frame = withUnsafeBytes(of: &payloadLength) { Data($0) }
        frame.append(payload)
        return frame
    }
}

struct AveragingMeasurementAccumulator: Equatable, Sendable {
    static let outlierDetectionMinimumCount = 6
    static let minimumOutputCount = 6
    static let recommendedCount = 10
    static let sufficientCount = 15
    static let maximumCount = 20

    /// The first six valid measurements use the wider shape tolerance. They
    /// are evaluated together when the sixth measurement arrives, allowing an
    /// outlier in the initial provisional set to be removed retrospectively.
    private static let relaxedSampleCount = 6
    private static let relaxedNormalizedShapeDifference = 0.05
    private static let standardNormalizedShapeDifference = 0.03
    private static let maximumRelativeLevelDifference = 0.15
    private static let minimumRobustSigma = 0.001
    private static let robustSigmaMultiplier = 6.0
    private static let comparisonTolerance = 1e-7
    private static let minimumRMS = 1e-12
    private static let convergenceHighVariationBoundary = 0.03
    private static let convergenceStableBoundary = 0.015
    private static let convergenceSufficientBoundary = 0.0075
    private static let studentTCritical95: [Double] = [
        2.570582, 2.446912, 2.364624, 2.306004, 2.262157,
        2.228139, 2.200985, 2.178813, 2.160369, 2.144787,
        2.131450, 2.119905, 2.109816, 2.100922, 2.093024,
    ]

    private struct RetainedSample: Equatable, Sendable {
        let sequenceNumber: Int
        let measurement: SpotMeasurement
    }

    private struct OutlierEvaluation: Equatable, Sendable {
        let shapeDistance: Double
        let shapeThreshold: Double
        let levelDifference: Double

        var isOutlier: Bool {
            shapeDistance > shapeThreshold
                || levelDifference > AveragingMeasurementAccumulator.maximumRelativeLevelDifference
        }

        var decisionValues: (distance: Double, threshold: Double) {
            if levelDifference > AveragingMeasurementAccumulator.maximumRelativeLevelDifference {
                return (
                    levelDifference,
                    AveragingMeasurementAccumulator.maximumRelativeLevelDifference
                )
            }
            return (shapeDistance, shapeThreshold)
        }
    }

    private struct OutlierClassification {
        let retainedSamples: [RetainedSample]
        let evaluations: [Int: OutlierEvaluation]
    }

    private var retainedSamples: [RetainedSample] = []
    private var spectralGridReference: SpotMeasurement?
    private var nextSequenceNumber = 1
    private(set) var measurementAttemptCount = 0
    private(set) var outlierCount = 0
    private(set) var invalidMeasurementCount = 0
    private(set) var lastRetrospectivelyRejectedCount = 0

    var rejectedCount: Int {
        outlierCount + invalidMeasurementCount
    }

    var acceptedMeasurements: [SpotMeasurement] {
        retainedSamples.map(\.measurement)
    }

    var acceptedCount: Int {
        retainedSamples.count
    }

    var latestAcceptedMeasurement: SpotMeasurement? {
        retainedSamples.last?.measurement
    }

    var hasStartedOutlierDetection: Bool {
        nextSequenceNumber > Self.outlierDetectionMinimumCount
    }

    var convergence: AveragingConvergence? {
        guard acceptedCount >= Self.outlierDetectionMinimumCount,
              let firstMeasurement = acceptedMeasurements.first,
              acceptedMeasurements.allSatisfy({
                  Self.hasMatchingSpectralGrid($0, firstMeasurement)
              }) else {
            return nil
        }

        let sampleCount = acceptedCount
        let spectrumPointCount = firstMeasurement.spectrum.count
        var means = Array(repeating: 0.0, count: spectrumPointCount)
        for measurement in acceptedMeasurements {
            for (index, sample) in measurement.spectrum.enumerated() {
                means[index] += sample.value
            }
        }
        let sampleDivisor = Double(sampleCount)
        means = means.map { $0 / sampleDivisor }

        let meanRMS = Self.rootMeanSquare(means)
        guard meanRMS > Self.minimumRMS else { return nil }

        var varianceSums = Array(repeating: 0.0, count: spectrumPointCount)
        for measurement in acceptedMeasurements {
            for (index, sample) in measurement.spectrum.enumerated() {
                let difference = sample.value - means[index]
                varianceSums[index] += difference * difference
            }
        }
        let degreesOfFreedom = Double(sampleCount - 1)
        let standardErrors = varianceSums.map {
            sqrt(max($0 / degreesOfFreedom, 0) / sampleDivisor)
        }
        let standardErrorRMS = Self.rootMeanSquare(standardErrors)
        let criticalValueIndex = min(
            max(sampleCount, Self.outlierDetectionMinimumCount),
            Self.maximumCount
        ) - Self.outlierDetectionMinimumCount
        let relative95Uncertainty = Self.studentTCritical95[criticalValueIndex]
            * standardErrorRMS / meanRMS
        guard relative95Uncertainty.isFinite else { return nil }

        let tier: AveragingConvergenceTier
        if relative95Uncertainty > Self.convergenceHighVariationBoundary {
            tier = .highVariation
        } else if relative95Uncertainty > Self.convergenceStableBoundary {
            tier = .converging
        } else if relative95Uncertainty > Self.convergenceSufficientBoundary {
            tier = .stable
        } else if acceptedCount >= Self.sufficientCount {
            tier = .sufficientlyStable
        } else {
            tier = .stable
        }

        return AveragingConvergence(
            relative95Uncertainty: relative95Uncertainty,
            tier: tier
        )
    }

    var canOutputAverage: Bool {
        acceptedCount >= Self.minimumOutputCount
    }

    var hasReachedMaximum: Bool {
        acceptedCount >= Self.maximumCount
    }

    var progressTier: AveragingProgressTier {
        switch acceptedCount {
        case ..<Self.minimumOutputCount:
            .insufficient
        case ..<Self.recommendedCount:
            .minimum
        case ..<Self.sufficientCount:
            .recommended
        default:
            .sufficient
        }
    }

    @discardableResult
    mutating func add(_ measurement: SpotMeasurement) -> AveragingSampleDecision {
        lastRetrospectivelyRejectedCount = 0
        measurementAttemptCount += 1
        guard acceptedCount < Self.maximumCount else {
            return .incompatible(
                reason: String(localized: "平均化測定は最大20回です。")
            )
        }
        guard Self.hasUsableSpectrum(measurement) else {
            invalidMeasurementCount += 1
            return .incompatible(
                reason: String(localized: "有効なスペクトル値を取得できなかったため、平均回数には含めませんでした。")
            )
        }
        if let spectralGridReference,
           Self.hasMatchingSpectralGrid(measurement, spectralGridReference) == false {
            invalidMeasurementCount += 1
            return .incompatible(
                reason: String(localized: "波長範囲またはデータ点数が一致しないため、平均回数には含めませんでした。")
            )
        }
        if spectralGridReference == nil {
            spectralGridReference = measurement
        }

        let candidate = RetainedSample(
            sequenceNumber: nextSequenceNumber,
            measurement: measurement
        )
        nextSequenceNumber += 1

        guard candidate.sequenceNumber >= Self.outlierDetectionMinimumCount else {
            retainedSamples.append(candidate)
            return .accepted
        }

        let previousSequenceNumbers = Set(retainedSamples.map(\.sequenceNumber))
        let classification = Self.classifyOutliers(in: retainedSamples + [candidate])
        let retainedSequenceNumbers = Set(
            classification.retainedSamples.map(\.sequenceNumber)
        )
        let candidateWasAccepted = retainedSequenceNumbers.contains(candidate.sequenceNumber)
        lastRetrospectivelyRejectedCount = previousSequenceNumbers.subtracting(
            retainedSequenceNumbers
        ).count
        outlierCount += lastRetrospectivelyRejectedCount
            + (candidateWasAccepted ? 0 : 1)
        retainedSamples = classification.retainedSamples

        guard candidateWasAccepted == false else {
            return .accepted
        }
        let evaluation = classification.evaluations[candidate.sequenceNumber]
        let values = evaluation?.decisionValues ?? (.infinity, 0)
        return .outlier(distance: values.distance, threshold: values.threshold)
    }

    func makeAnalysisRequest(requestID: String) throws -> SpectrumAnalysisRequest {
        guard let first = acceptedMeasurements.first else {
            throw AveragingMeasurementError.noMeasurements
        }
        guard acceptedMeasurements.allSatisfy(Self.hasUsableSpectrum),
              acceptedMeasurements.allSatisfy({
                  Self.hasMatchingSpectralGrid($0, first)
              }) else {
            throw AveragingMeasurementError.invalidSpectrum
        }

        var averagedValues = Array(repeating: 0.0, count: first.spectrum.count)
        for measurement in acceptedMeasurements {
            for (index, sample) in measurement.spectrum.enumerated() {
                averagedValues[index] += sample.value
            }
        }
        let divisor = Double(acceptedMeasurements.count)
        averagedValues = averagedValues.map { $0 / divisor }
        guard averagedValues.allSatisfy(\.isFinite) else {
            throw AveragingMeasurementError.invalidSpectrum
        }

        let practicalRange = commonPracticalSpectrumRange
        return SpectrumAnalysisRequest(
            requestId: requestID,
            mode: first.mode,
            sampleCount: acceptedMeasurements.count,
            spectrum: .init(
                startNm: first.spectrumStart,
                endNm: first.spectrumEnd,
                norm: first.mode == .reflectance ? 100 : 1,
                practicalStartNm: practicalRange?.start,
                practicalEndNm: practicalRange?.end,
                values: averagedValues
            )
        )
    }

    private var commonPracticalSpectrumRange: WavelengthRange? {
        let ranges = acceptedMeasurements.compactMap(\.validatedPracticalSpectrumRange)
        guard ranges.count == acceptedMeasurements.count,
              let first = ranges.first else {
            return nil
        }
        let start = ranges.dropFirst().reduce(first.start) { max($0, $1.start) }
        let end = ranges.dropFirst().reduce(first.end) { min($0, $1.end) }
        guard start <= end else { return nil }
        return WavelengthRange(start: start, end: end)
    }

    private static func classifyOutliers(
        in samples: [RetainedSample]
    ) -> OutlierClassification {
        var activeSamples = samples
        var recordedEvaluations: [Int: OutlierEvaluation] = [:]

        for _ in 0..<samples.count {
            let evaluations = evaluateOutliers(in: activeSamples)
            recordedEvaluations.merge(evaluations) { _, new in new }
            let survivors = activeSamples.filter {
                evaluations[$0.sequenceNumber]?.isOutlier == false
            }
            guard survivors.count != activeSamples.count,
                  survivors.isEmpty == false else {
                if survivors.isEmpty == false {
                    activeSamples = survivors
                }
                break
            }
            activeSamples = survivors
        }

        return OutlierClassification(
            retainedSamples: activeSamples,
            evaluations: recordedEvaluations
        )
    }

    private static func evaluateOutliers(
        in samples: [RetainedSample]
    ) -> [Int: OutlierEvaluation] {
        let center = pointwiseMedian(of: samples)
        let centerRMS = rootMeanSquare(center)
        let rawMetrics = samples.map { sample in
            let metrics = normalizedShapeAndLevelDifference(
                sample.measurement.spectrum.map(\.value),
                center: center,
                centerRMS: centerRMS
            )
            return (
                sequenceNumber: sample.sequenceNumber,
                shapeDistance: metrics.shapeDistance,
                levelDifference: metrics.levelDifference
            )
        }
        let finiteShapeDistances = rawMetrics.map(\.shapeDistance).filter(\.isFinite)
        let baselineMedian = median(finiteShapeDistances)
        let medianAbsoluteDeviation = median(
            finiteShapeDistances.map { abs($0 - baselineMedian) }
        )
        let robustSigma = max(
            1.4826 * medianAbsoluteDeviation,
            minimumRobustSigma
        )
        let adaptiveShapeThreshold = baselineMedian
            + robustSigmaMultiplier * robustSigma

        return Dictionary(uniqueKeysWithValues: rawMetrics.map { metrics in
            let fixedShapeThreshold = metrics.sequenceNumber <= relaxedSampleCount
                ? relaxedNormalizedShapeDifference
                : standardNormalizedShapeDifference
            return (
                metrics.sequenceNumber,
                OutlierEvaluation(
                    shapeDistance: metrics.shapeDistance,
                    shapeThreshold: max(
                        fixedShapeThreshold,
                        adaptiveShapeThreshold
                    ),
                    levelDifference: metrics.levelDifference
                )
            )
        })
    }

    private static func pointwiseMedian(of samples: [RetainedSample]) -> [Double] {
        guard let sampleCount = samples.first?.measurement.spectrum.count else {
            return []
        }
        return (0..<sampleCount).map { sampleIndex in
            median(
                samples.map { $0.measurement.spectrum[sampleIndex].value }
            )
        }
    }

    private static func normalizedShapeAndLevelDifference(
        _ values: [Double],
        center: [Double],
        centerRMS: Double
    ) -> (shapeDistance: Double, levelDifference: Double) {
        guard values.count == center.count, values.isEmpty == false else {
            return (.infinity, .infinity)
        }
        let valuesRMS = rootMeanSquare(values)
        guard centerRMS > minimumRMS else {
            return valuesRMS <= minimumRMS
                ? (0, 0)
                : (.infinity, .infinity)
        }
        let levelRatio = valuesRMS / centerRMS
        guard valuesRMS > minimumRMS else {
            return (.infinity, abs(levelRatio - 1))
        }

        let alignmentScale = centerRMS / valuesRMS
        let squaredDifference = zip(values, center).reduce(0.0) { result, pair in
            let difference = pair.0 * alignmentScale - pair.1
            return result + difference * difference
        }
        let rmsDifference = sqrt(squaredDifference / Double(values.count))
        return (
            rmsDifference / centerRMS,
            abs(levelRatio - 1)
        )
    }

    private static func rootMeanSquare(_ values: [Double]) -> Double {
        guard values.isEmpty == false else { return 0 }
        let meanSquare = values.reduce(0.0) { $0 + $1 * $1 }
            / Double(values.count)
        return sqrt(meanSquare)
    }

    private static func median(_ values: [Double]) -> Double {
        guard values.isEmpty == false else { return 0 }
        let sorted = values.sorted()
        let middle = sorted.count / 2
        if sorted.count.isMultiple(of: 2) {
            return (sorted[middle - 1] + sorted[middle]) / 2
        }
        return sorted[middle]
    }

    private static func hasUsableSpectrum(_ measurement: SpotMeasurement) -> Bool {
        measurement.spectrum.count >= 2
            && measurement.spectrumStart.isFinite
            && measurement.spectrumEnd.isFinite
            && measurement.spectrumStart < measurement.spectrumEnd
            && measurement.spectrum.allSatisfy {
                $0.wavelength.isFinite && $0.value.isFinite
            }
    }

    private static func hasMatchingSpectralGrid(
        _ lhs: SpotMeasurement,
        _ rhs: SpotMeasurement
    ) -> Bool {
        guard lhs.mode == rhs.mode,
              lhs.spectrum.count == rhs.spectrum.count,
              approximatelyEqual(lhs.spectrumStart, rhs.spectrumStart),
              approximatelyEqual(lhs.spectrumEnd, rhs.spectrumEnd) else {
            return false
        }
        return zip(lhs.spectrum, rhs.spectrum).allSatisfy { pair in
            approximatelyEqual(pair.0.wavelength, pair.1.wavelength)
        }
    }

    private static func approximatelyEqual(_ lhs: Double, _ rhs: Double) -> Bool {
        abs(lhs - rhs) <= comparisonTolerance
    }
}
