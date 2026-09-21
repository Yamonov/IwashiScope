import Foundation

@MainActor
enum MeasurementHistoryDateExporter {
    static func canExport(_ group: MeasurementHistoryDateGroup, mode: MeasurementMode) -> Bool {
        guard group.entries.isEmpty == false,
              group.entries.allSatisfy({ $0.measurement.mode == mode }) else { return false }
        if mode == .reflectance {
            return group.entries.allSatisfy { $0.measurement.lab != nil }
        }
        return MeasurementExportAvailability(entries: group.entries).canExport(
            mode: mode, options: .dragDefaults(for: mode)
        )
    }

    static func swatchFile(
        for group: MeasurementHistoryDateGroup,
        orderedEntries: [MeasurementHistoryEntry]
    ) throws -> MeasurementExportFile {
        guard canExport(group, mode: .reflectance) else {
            throw MeasurementExportError.noExportableData
        }
        let names = MeasurementExportFileNamer.baseNames(for: group.entries, orderedEntries: orderedEntries)
        let swatches = group.entries.compactMap { entry -> AdobeLabSwatch? in
            guard let lab = entry.measurement.lab, let name = names[entry.id] else { return nil }
            return AdobeLabSwatch(name: name, lab: lab)
        }
        return MeasurementExportFile(
            name: "\(group.exportName).ase",
            data: try AdobeSwatchExchangeEncoder.encode(swatches: swatches, groupName: group.exportName)
        )
    }

    @discardableResult
    static func exportLightingGroup(
        _ group: MeasurementHistoryDateGroup,
        orderedEntries: [MeasurementHistoryEntry],
        mode: MeasurementMode,
        usesPracticalSpectrumRange: Bool,
        yAxisConfiguration: SpectrumYAxisConfiguration,
        to parentDirectory: URL
    ) throws -> URL {
        guard mode != .reflectance, canExport(group, mode: mode) else {
            throw MeasurementExportError.noExportableData
        }
        // Share the drag-export options and writer, including collision-safe names.
        var options = MeasurementExportOptions.dragDefaults(for: mode)
        options.usesPracticalSpectrumRange = usesPracticalSpectrumRange
        options.spectrumYAxisConfiguration = yAxisConfiguration
        let directory = parentDirectory.appendingPathComponent(group.exportName, isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try MeasurementExporter.export(
            entries: group.entries, orderedEntries: orderedEntries, mode: mode,
            options: options, to: directory
        )
        return directory
    }
}
