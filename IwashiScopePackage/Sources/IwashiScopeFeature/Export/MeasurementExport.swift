import Foundation

struct MeasurementExportOptions: Equatable, Sendable {
    var includesSwatch = false
    var includesSpectrumImage = false
    var usesPracticalSpectrumRange = false
    var includesD50Reference = false
    var includesD65Reference = false
    var includesCRIImage = false
    var includesTM30Image = false
    var includesCSV = false

    static func defaults(for mode: MeasurementMode) -> Self {
        var options = Self()
        if mode == .reflectance {
            options.includesSwatch = true
        } else {
            options.includesSpectrumImage = true
        }
        return options
    }

    static func dragDefaults(for mode: MeasurementMode) -> Self {
        var options = defaults(for: mode)
        if mode != .reflectance {
            options.includesCRIImage = true
            options.includesTM30Image = true
        }
        return options
    }
}

struct MeasurementExportAvailability: Equatable, Sendable {
    let selectionCount: Int
    let hasLab: Bool
    let hasSpectrum: Bool
    let hasCRI: Bool
    let hasTM30: Bool

    init(entries: [MeasurementHistoryEntry]) {
        selectionCount = entries.count
        hasLab = entries.contains { $0.measurement.lab != nil }
        hasSpectrum = entries.contains { $0.measurement.spectrum.isEmpty == false }
        hasCRI = entries.contains { $0.measurement.cri != nil }
        hasTM30 = entries.contains { $0.measurement.tm30 != nil }
    }

    func canExport(
        mode: MeasurementMode,
        options: MeasurementExportOptions
    ) -> Bool {
        guard selectionCount > 0 else { return false }

        if mode == .reflectance {
            return (options.includesSwatch && hasLab)
                || (options.includesSpectrumImage && hasSpectrum)
                || (options.includesCSV && hasSpectrum)
        }

        return (options.includesSpectrumImage && hasSpectrum)
            || (options.includesCRIImage && hasCRI)
            || (options.includesTM30Image && hasTM30)
            || options.includesCSV
    }
}

struct MeasurementExportFile: Equatable, Sendable {
    let name: String
    let data: Data
}

struct MeasurementHistoryDragExportRequest: Sendable {
    let mode: MeasurementMode
    let entries: [MeasurementHistoryEntry]
    let orderedEntries: [MeasurementHistoryEntry]
    let usesPracticalSpectrumRange: Bool

    init(
        mode: MeasurementMode,
        entries: [MeasurementHistoryEntry],
        orderedEntries: [MeasurementHistoryEntry],
        usesPracticalSpectrumRange: Bool = false
    ) {
        self.mode = mode
        self.entries = entries
        self.orderedEntries = orderedEntries
        self.usesPracticalSpectrumRange = usesPracticalSpectrumRange
    }
}

enum MeasurementExportError: LocalizedError {
    case noSelection
    case noExportableData
    case imageRenderingFailed
    case pngEncodingFailed

    var errorDescription: String? {
        switch self {
        case .noSelection:
            String(localized: "書き出す履歴カードを1枚以上選択してください。")
        case .noExportableData:
            String(localized: "選択した項目に書き出せる測定データがありません。")
        case .imageRenderingFailed:
            String(localized: "グラフ画像を生成できませんでした。")
        case .pngEncodingFailed:
            String(localized: "グラフ画像をPNGへ変換できませんでした。")
        }
    }
}

enum MeasurementExportFileNamer {
    static func combinedSwatchFileName(
        for entries: [MeasurementHistoryEntry],
        orderedEntries: [MeasurementHistoryEntry]
    ) -> String {
        let baseNames = baseNames(
            for: entries,
            orderedEntries: orderedEntries
        )
        guard let firstEntry = entries.first,
              let firstBaseName = baseNames[firstEntry.id] else {
            return "001-Swatch.ase"
        }
        if entries.count == 1 {
            return "\(firstBaseName)-Swatch.ase"
        }
        return "\(firstBaseName)-Selected-Swatches.ase"
    }

    static func baseNames(
        for entries: [MeasurementHistoryEntry],
        orderedEntries: [MeasurementHistoryEntry]
    ) -> [MeasurementHistoryEntry.ID: String] {
        let orderedIndices = Dictionary(
            uniqueKeysWithValues: orderedEntries.enumerated().map {
                ($0.element.id, $0.offset)
            }
        )
        let digits = max(3, String(max(orderedEntries.count, 1)).count)
        var result: [MeasurementHistoryEntry.ID: String] = [:]

        for entry in entries {
            let sequence = (orderedIndices[entry.id] ?? 0) + 1
            let sequencePrefix = String(format: "%0*d", digits, sequence)
            guard let name = entry.name.map(sanitizedComponent),
                  name.isEmpty == false else {
                result[entry.id] = sequencePrefix
                continue
            }
            result[entry.id] = "\(sequencePrefix)-\(name)"
        }

        return result
    }

    static func sanitizedComponent(_ value: String) -> String {
        let forbidden = CharacterSet(charactersIn: "/:")
            .union(.controlCharacters)
            .union(.illegalCharacters)
        let scalars = value.unicodeScalars.map { scalar -> Character in
            forbidden.contains(scalar) ? "-" : Character(String(scalar))
        }
        var result = String(scalars)
            .replacingOccurrences(
                of: "\\s+",
                with: " ",
                options: .regularExpression
            )
            .replacingOccurrences(
                of: "-+",
                with: "-",
                options: .regularExpression
            )
            .trimmingCharacters(in: CharacterSet.whitespacesAndNewlines.union(
                CharacterSet(charactersIn: ".-")
            ))

        while result.utf8.count > 160 {
            result.removeLast()
        }
        return result.trimmingCharacters(
            in: CharacterSet.whitespacesAndNewlines.union(
                CharacterSet(charactersIn: ".-")
            )
        )
    }
}

enum MeasurementExportCSVEncoder {
    static func spectrum(_ measurement: SpotMeasurement) -> Data {
        var rows = [["wavelength_nm", "value"]]
        rows.append(contentsOf: measurement.spectrum.map { sample in
            [number(sample.wavelength), number(sample.value)]
        })
        return Data(csv(rows: rows).utf8)
    }

    static func lighting(_ measurement: SpotMeasurement) -> Data {
        var rows = [[
            "section",
            "item",
            "wavelength_nm",
            "value",
            "reference_J",
            "reference_a",
            "reference_b",
            "test_J",
            "test_a",
            "test_b",
        ]]

        rows.append([
            "metadata",
            "captured_at",
            "",
            ISO8601DateFormatter().string(from: measurement.capturedAt),
            "", "", "", "", "", "",
        ])
        rows.append([
            "metadata",
            "mode",
            "",
            measurement.mode.rawValue,
            "", "", "", "", "", "",
        ])

        for sample in measurement.spectrum {
            rows.append([
                "spectrum",
                String(sample.id),
                number(sample.wavelength),
                number(sample.value),
                "", "", "", "", "", "",
            ])
        }

        if let cri = measurement.cri {
            rows.append(metricRow(section: "CRI", item: "Ra", value: cri.ra))
            for index in cri.individual.keys.sorted() {
                guard let value = cri.individual[index] else { continue }
                rows.append(
                    metricRow(
                        section: "CRI",
                        item: "R\(index)",
                        value: value
                    )
                )
            }
            if cri.individual[9] == nil, let r9 = cri.r9 {
                rows.append(metricRow(section: "CRI", item: "R9", value: r9))
            }
            rows.append([
                "CRI", "caution", "", cri.caution ? "true" : "false",
                "", "", "", "", "", "",
            ])
        }

        if let tm30 = measurement.tm30 {
            rows.append(
                metricRow(
                    section: "TM-30-15",
                    item: "Rf",
                    value: tm30.fidelityIndex
                )
            )
            rows.append(
                metricRow(
                    section: "TM-30-15",
                    item: "Rg",
                    value: tm30.gamutIndex
                )
            )
            rows.append(
                metricRow(
                    section: "TM-30-15",
                    item: "CCT",
                    value: tm30.cct
                )
            )
            rows.append(
                metricRow(
                    section: "TM-30-15",
                    item: "Duv",
                    value: tm30.duv
                )
            )
            rows.append([
                "TM-30-15", "status", "", tm30.status.rawValue,
                "", "", "", "", "", "",
            ])

            for bin in tm30.hueBins.sorted(by: { $0.index < $1.index }) {
                rows.append(
                    colorPairRow(
                        section: "TM-30-15 hue bin",
                        item: String(bin.index),
                        reference: bin.referenceJab,
                        test: bin.testJab
                    )
                )
            }

            for sample in tm30.evaluationSamples.sorted(by: { $0.index < $1.index }) {
                rows.append(
                    colorPairRow(
                        section: "TM-30-15 evaluation sample",
                        item: String(sample.index),
                        reference: sample.referenceJab,
                        test: sample.testJab
                    )
                )
            }
        }

        return Data(csv(rows: rows).utf8)
    }

    private static func metricRow(
        section: String,
        item: String,
        value: Double
    ) -> [String] {
        [section, item, "", number(value), "", "", "", "", "", ""]
    }

    private static func colorPairRow(
        section: String,
        item: String,
        reference: Vector3,
        test: Vector3
    ) -> [String] {
        [
            section,
            item,
            "",
            "",
            number(reference.first),
            number(reference.second),
            number(reference.third),
            number(test.first),
            number(test.second),
            number(test.third),
        ]
    }

    private static func number(_ value: Double) -> String {
        String(
            format: "%.12g",
            locale: Locale(identifier: "en_US_POSIX"),
            value
        )
    }

    private static func csv(rows: [[String]]) -> String {
        rows
            .map { $0.map(escapedField).joined(separator: ",") }
            .joined(separator: "\r\n")
            + "\r\n"
    }

    private static func escapedField(_ value: String) -> String {
        guard value.contains(",")
                || value.contains("\"")
                || value.contains("\r")
                || value.contains("\n") else {
            return value
        }
        return "\"\(value.replacingOccurrences(of: "\"", with: "\"\""))\""
    }
}
