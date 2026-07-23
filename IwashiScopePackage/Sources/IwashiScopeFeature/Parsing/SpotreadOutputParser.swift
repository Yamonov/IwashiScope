import Foundation

enum SpotreadEvent: Equatable {
    case instrumentIdentity(SpotreadInstrumentIdentity)
    case calibrationStarted
    case calibrationPrompt(CalibrationPrompt)
    case calibrationComplete
    case savedReadingPrompt
    case measurementStarted
    case measurement(SpotMeasurement)
    case measurementPrompt
    case recoverableIssue(SpotreadIssue)
    case configurationIssue(SpotreadIssue)
    case fatalIssue(SpotreadIssue)
    case warning(SpotreadNotice)
}

struct SpotreadOutputParser {
    private static let protocolVersion = 2

    private let mode: MeasurementMode
    private var buffer = ""
    private var hasFinished = false

    init(mode: MeasurementMode) {
        self.mode = mode
    }

    var bufferedCharacterCount: Int {
        buffer.count
    }

    mutating func consume(_ chunk: String) -> [SpotreadEvent] {
        guard chunk.isEmpty == false, hasFinished == false else { return [] }
        buffer.append(chunk)

        var events: [SpotreadEvent] = []
        while let newline = buffer.firstIndex(of: "\n") {
            var line = String(buffer[..<newline])
            buffer.removeSubrange(...newline)
            if line.last == "\r" {
                line.removeLast()
            }
            events.append(contentsOf: decode(line))
        }
        return events
    }

    mutating func finish() -> [SpotreadEvent] {
        guard hasFinished == false else { return [] }
        hasFinished = true

        let finalLine = buffer.trimmingCharacters(in: .whitespacesAndNewlines)
        buffer.removeAll(keepingCapacity: false)
        guard finalLine.isEmpty == false else { return [] }
        return decode(finalLine)
    }

    private func decode(_ line: String) -> [SpotreadEvent] {
        guard line.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty == false else {
            return []
        }

        do {
            let record = try JSONDecoder().decode(ProtocolRecord.self, from: Data(line.utf8))
            guard record.protocolVersion == Self.protocolVersion else {
                return [
                    .fatalIssue(
                        .fatal(
                            rawText: "spotread JSON protocol version \(record.protocolVersion) is not supported"
                        )
                    )
                ]
            }
            return try events(from: record, rawText: line)
        } catch {
            return [.configurationIssue(.outputParsingFailure(rawText: line))]
        }
    }

    private func events(
        from record: ProtocolRecord,
        rawText: String
    ) throws -> [SpotreadEvent] {
        switch record.event {
        case "hello":
            return []

        case "instrument":
            return [
                .instrumentIdentity(
                    SpotreadInstrumentIdentity(
                        name: record.name,
                        serialNumber: record.serialNumber
                    )
                )
            ]

        case "state":
            switch try record.required(record.state, named: "state") {
            case "ready":
                return [.measurementPrompt]
            case "measurementStarted":
                return [.measurementStarted]
            case "savedReadingPrompt":
                return [.savedReadingPrompt]
            default:
                throw ProtocolValidationError.invalidValue("state")
            }

        case "calibration":
            return try calibrationEvents(from: record, rawText: rawText)

        case "issue":
            return try issueEvents(from: record, rawText: rawText)

        case "measurement":
            return [.measurement(try measurement(from: record))]

        default:
            throw ProtocolValidationError.invalidValue("event")
        }
    }

    private func calibrationEvents(
        from record: ProtocolRecord,
        rawText: String
    ) throws -> [SpotreadEvent] {
        switch try record.required(record.phase, named: "phase") {
        case "started":
            return [.calibrationStarted]

        case "prompt":
            return [
                .calibrationPrompt(
                    calibrationPrompt(from: record, rawText: rawText)
                )
            ]

        case "completed", "skipped":
            return [.calibrationComplete]

        case "failed":
            let reason = record.reason ?? "Unknown calibration failure"
            return [
                .recoverableIssue(
                    .calibrationFailure(reason: reason, rawText: rawText)
                )
            ]

        case "aborted":
            return [
                .recoverableIssue(
                    .calibrationFailure(
                        reason: "Calibration was aborted",
                        rawText: rawText
                    )
                )
            ]

        default:
            throw ProtocolValidationError.invalidValue("phase")
        }
    }

    private func issueEvents(
        from record: ProtocolRecord,
        rawText: String
    ) throws -> [SpotreadEvent] {
        let code = try record.required(record.code, named: "code")
        let reason = record.reason ?? code

        switch code {
        case "misread":
            return [.recoverableIssue(.misread(reason: reason, rawText: rawText))]
        case "communicationFailure":
            return [.recoverableIssue(.communicationFailure(rawText: rawText))]
        case "userStopped":
            return [.recoverableIssue(.userStopped(rawText: rawText))]
        case "wrongConfiguration":
            return [
                .configurationIssue(
                    .wrongConfiguration(reason: reason, rawText: rawText)
                )
            ]
        case "operationFailure":
            return [
                .recoverableIssue(
                    .operationFailure(reason: reason, rawText: rawText)
                )
            ]
        case "needsCalibration":
            return [.calibrationStarted]
        case "fatalInstrumentError":
            return [.fatalIssue(.fatal(rawText: reason))]
        default:
            throw ProtocolValidationError.invalidValue("issue code")
        }
    }

    private func measurement(from record: ProtocolRecord) throws -> SpotMeasurement {
        let recordedMode = try record.required(record.mode, named: "mode")
        guard recordedMode == mode.rawValue else {
            throw ProtocolValidationError.invalidValue("measurement mode")
        }

        let spectrumPayload = record.spectrum
        let spectrumValues = spectrumPayload?.values ?? []
        let spectrumStart = spectrumPayload?.startNm ?? 0
        let spectrumEnd = spectrumPayload?.endNm ?? 0
        let spectrum = Self.spectralSamples(
            values: spectrumValues,
            start: spectrumStart,
            end: spectrumEnd
        )
        let peak = spectrum.max { lhs, rhs in lhs.value < rhs.value }

        let xyz = try record.xyz.map(Self.vector3(from:))
        let lab = try record.lab.map(Self.vector3(from:))
        let monochrome = record.monochrome.map {
            MonochromeResult(y: $0.y, lStar: $0.lStar)
        }
        guard (xyz != nil && lab != nil) || monochrome != nil else {
            throw ProtocolValidationError.missingValue("XYZ/Lab or monochrome")
        }

        let issues = Set(
            (record.lightingMetricIssues ?? []).compactMap(LightingMetricIssue.init(rawValue:))
        )
        guard issues.count == (record.lightingMetricIssues ?? []).count else {
            throw ProtocolValidationError.invalidValue("lightingMetricIssues")
        }

        var individualCRI: [Int: Double] = [:]
        if let values = record.cri?.individual {
            guard values.count == 14 else {
                throw ProtocolValidationError.invalidCount("CRI individual values")
            }
            for (offset, value) in values.enumerated() {
                individualCRI[offset + 1] = value
            }
        }
        if record.cri != nil,
           spectrum.isEmpty == false,
           let cct = record.cct,
           let r15 = ColorRenderingIndexCalculator.r15(spectrum: spectrum, cct: cct) {
            individualCRI[15] = r15
        }

        let cri = record.cri.map {
            CRIResult(
                ra: $0.ra,
                r9: individualCRI[9],
                individual: individualCRI,
                caution: $0.caution
            )
        }
        let tlci = record.tlci.map {
            TLCIResult(qa: $0.qa, caution: $0.caution)
        }
        let tm30 = try record.tm30.flatMap(Self.tm30Result(from:))

        return SpotMeasurement(
            capturedAt: Date(),
            mode: mode,
            spectrumStart: spectrumStart,
            spectrumEnd: spectrumEnd,
            declaredStepCount: spectrum.count,
            spectrum: spectrum,
            peakValue: peak?.value,
            peakWavelength: peak?.wavelength,
            xyz: xyz,
            lab: lab,
            labWhitePoint: record.labWhitePoint,
            monochrome: monochrome,
            lux: record.lux,
            cct: record.cct,
            duv: record.duv,
            suggestedEV100: record.suggestedEV100,
            closestPlanckian: record.closestPlanckian.map {
                TemperatureMatch(kelvin: $0.kelvin, deltaE2000: $0.deltaE2000)
            },
            closestDaylight: record.closestDaylight.map {
                TemperatureMatch(kelvin: $0.kelvin, deltaE2000: $0.deltaE2000)
            },
            lightingMetricIssues: issues,
            cri: cri,
            tlci: tlci,
            tm30: tm30
        )
    }

    private func calibrationPrompt(
        from record: ProtocolRecord,
        rawText: String
    ) -> CalibrationPrompt {
        let condition = record.condition ?? "unknown"
        let identifier = record.identifier
        let allowsSkip = record.optional ?? false
        let requiresConfirmation = record.requiresConfirmation ?? true
        let presentation = Self.calibrationPresentation(
            condition: condition,
            identifier: identifier
        )

        return CalibrationPrompt(
            title: presentation.title,
            instruction: presentation.instruction,
            systemImage: presentation.systemImage,
            requiresUserConfirmation: requiresConfirmation,
            allowsSkip: allowsSkip,
            rawText: rawText
        )
    }

    private static func calibrationPresentation(
        condition: String,
        identifier: String?
    ) -> (title: String, instruction: String, systemImage: String) {
        switch condition {
        case "reflectiveWhite", "reflectiveWhiteClick":
            let suffix = identifier.map { "（S/N \($0)）" } ?? ""
            return (
                "白色基準でキャリブレーション",
                "測定器を付属の白色基準\(suffix)に置いてください。",
                "square.fill"
            )
        case "sensorCalibrationPosition":
            return (
                "測定器を校正位置へ",
                "センサーのダイヤルをキャリブレーション位置に合わせてください。",
                "dial.medium"
            )
        case "ambientDark":
            return (
                "環境光アダプターを遮光",
                "環境光アダプターを取り付け、キャップで遮光してください。",
                "sun.max.trianglebadge.exclamationmark"
            )
        case "emissiveDark":
            return (
                "測定器を遮光",
                "測定器にキャップを付けるか、暗い面または校正基準に置いてください。",
                "moon.fill"
            )
        case "reflectiveDark":
            return (
                "暗部キャリブレーション",
                "測定器をライトトラップに置くか、周囲の面から離して遮光してください。",
                "moon.stars.fill"
            )
        case "glossBlack":
            return (
                "黒色光沢基準でキャリブレーション",
                "測定器を黒色光沢基準に置いてください。",
                "circle.lefthalf.filled"
            )
        case "transmissiveWhite", "userOperatedTransmissiveWhite":
            return (
                "透過白基準でキャリブレーション",
                "測定器を透過白基準光源に置き、光路を安定させてください。",
                "sun.max.fill"
            )
        case "transmissiveDark", "userOperatedTransmissiveDark":
            return (
                "透過暗部キャリブレーション",
                "適切な遮光材で透過光路を完全に遮ってください。",
                "rectangle.slash"
            )
        case "userOperatedReflectiveWhite":
            return (
                "反射白基準でキャリブレーション",
                "測定器の手順に従って反射白基準を測定してください。",
                "square.fill"
            )
        case "changeFilter":
            return (
                "測定器のフィルターを変更",
                identifier.map { "測定器のフィルターを「\($0)」に変更してください。" }
                    ?? "spotreadが指定するフィルターへ変更してください。",
                "camera.filters"
            )
        case "emissiveWhite":
            return (
                "白色パッチを測定",
                "測定器を100%の白色パッチに置いてください。",
                "display"
            )
        case "emissive80Percent":
            return (
                "白色パッチを測定",
                "測定器を80%の白色パッチに置いてください。",
                "display"
            )
        case "emissiveGrey", "emissiveGreyDarker", "emissiveGreyLighter":
            return (
                "グレーパッチを測定",
                "測定器の表示に従い、指定された明るさのグレーパッチを表示してください。",
                "display"
            )
        case "message":
            return (
                "キャリブレーションの準備",
                identifier ?? "測定器の表示に従って準備してください。",
                "scope"
            )
        default:
            return (
                "キャリブレーションの準備",
                "測定器の表示に従って準備してください。",
                "scope"
            )
        }
    }

    private static func spectralSamples(
        values: [Double],
        start: Double,
        end: Double
    ) -> [SpectralSample] {
        let denominator = max(values.count - 1, 1)
        return values.enumerated().map { offset, value in
            let wavelength = values.count == 1
                ? start
                : start + Double(offset) * (end - start) / Double(denominator)
            return SpectralSample(id: offset, wavelength: wavelength, value: value)
        }
    }

    private static func vector3(from values: [Double]) throws -> Vector3 {
        guard values.count == 3 else {
            throw ProtocolValidationError.invalidCount("three-component vector")
        }
        return Vector3(first: values[0], second: values[1], third: values[2])
    }

    private static func tm30Result(from payload: TM30Payload) throws -> TM30Result? {
        guard payload.status != "error" else { return nil }
        guard let fidelityIndex = payload.rf,
              let gamutIndex = payload.rg,
              let cct = payload.cct,
              let duv = payload.duv,
              let bins = payload.bins,
              bins.count == 16,
              let samples = payload.samples,
              samples.count == 99 else {
            throw ProtocolValidationError.missingValue("TM-30 values")
        }
        let status: TM30Status
        switch payload.status {
        case "valid": status = .valid
        case "caution": status = .caution
        default: throw ProtocolValidationError.invalidValue("TM-30 status")
        }

        let hueBins = try bins.enumerated().map { offset, bin in
            guard bin.index == offset + 1 else {
                throw ProtocolValidationError.invalidValue("TM-30 hue-bin index")
            }
            return TM30HueBin(
                index: bin.index,
                referenceJab: try vector3(from: bin.referenceJab),
                testJab: try vector3(from: bin.testJab)
            )
        }
        let evaluationSamples = try samples.enumerated().map { offset, sample in
            guard sample.index == offset + 1 else {
                throw ProtocolValidationError.invalidValue("TM-30 evaluation-sample index")
            }
            return TM30EvaluationSample(
                index: sample.index,
                referenceJab: try vector3(from: sample.referenceJab),
                testJab: try vector3(from: sample.testJab)
            )
        }
        return TM30Result(
            fidelityIndex: fidelityIndex,
            gamutIndex: gamutIndex,
            cct: cct,
            duv: duv,
            status: status,
            hueBins: hueBins,
            evaluationSamples: evaluationSamples
        )
    }
}

private struct ProtocolRecord: Decodable {
    let protocolVersion: Int
    let event: String
    let argyllVersion: String?
    let name: String?
    let serialNumber: String?
    let state: String?
    let phase: String?
    let condition: String?
    let identifierType: String?
    let identifier: String?
    let optional: Bool?
    let requiresConfirmation: Bool?
    let errorCode: Int?
    let reason: String?
    let code: String?
    let rawCode: Int?
    let recovery: String?
    let mode: String?
    let readingIndex: Int?
    let spectrum: SpectrumPayload?
    let xyz: [Double]?
    let lab: [Double]?
    let labWhitePoint: String?
    let monochrome: MonochromePayload?
    let lux: Double?
    let cct: Double?
    let duv: Double?
    let suggestedEV100: Double?
    let closestPlanckian: TemperaturePayload?
    let closestDaylight: TemperaturePayload?
    let lightingMetricIssues: [String]?
    let cri: CRIPayload?
    let tlci: TLCIPayload?
    let tm30: TM30Payload?

    func required<Value>(_ value: Value?, named name: String) throws -> Value {
        guard let value else {
            throw ProtocolValidationError.missingValue(name)
        }
        return value
    }
}

private struct SpectrumPayload: Decodable {
    let startNm: Double
    let endNm: Double
    let values: [Double]
}

private struct MonochromePayload: Decodable {
    let y: Double
    let lStar: Double
}

private struct TemperaturePayload: Decodable {
    let kelvin: Double
    let deltaE2000: Double
}

private struct CRIPayload: Decodable {
    let ra: Double
    let individual: [Double]
    let caution: Bool
}

private struct TLCIPayload: Decodable {
    let qa: Double
    let caution: Bool
}

private struct TM30Payload: Decodable {
    let status: String
    let rf: Double?
    let rg: Double?
    let cct: Double?
    let duv: Double?
    let bins: [TM30HueBinPayload]?
    let samples: [TM30EvaluationSamplePayload]?
}

private struct TM30HueBinPayload: Decodable {
    let index: Int
    let referenceJab: [Double]
    let testJab: [Double]
}

private struct TM30EvaluationSamplePayload: Decodable {
    let index: Int
    let referenceJab: [Double]
    let testJab: [Double]
}

private enum ProtocolValidationError: Error {
    case missingValue(String)
    case invalidValue(String)
    case invalidCount(String)
}
