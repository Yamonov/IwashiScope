import Foundation

@MainActor
enum MeasurementExporter {
    static func export(
        entries: [MeasurementHistoryEntry],
        orderedEntries: [MeasurementHistoryEntry],
        mode: MeasurementMode,
        options: MeasurementExportOptions,
        to directoryURL: URL
    ) throws {
        guard entries.isEmpty == false else {
            throw MeasurementExportError.noSelection
        }

        let baseNames = MeasurementExportFileNamer.baseNames(
            for: entries,
            orderedEntries: orderedEntries
        )
        var reservedNames = Set(
            (try? FileManager.default.contentsOfDirectory(
                atPath: directoryURL.path
            ))?.map { $0.lowercased() } ?? []
        )
        var wroteFile = false
        var perEntryOptions = options
        perEntryOptions.includesSwatch = false

        if let swatchFile = try combinedSwatchFile(
            entries: entries,
            orderedEntries: orderedEntries,
            mode: mode,
            options: options,
            baseNames: baseNames
        ) {
            let fileName = availableFileName(
                swatchFile.name,
                reservedNames: reservedNames
            )
            try swatchFile.data.write(
                to: directoryURL.appendingPathComponent(
                    fileName,
                    isDirectory: false
                ),
                options: .atomic
            )
            reservedNames.insert(fileName.lowercased())
            wroteFile = true
        }

        for entry in entries {
            guard let desiredBaseName = baseNames[entry.id] else { continue }
            let uniqueBaseName = availableBaseName(
                desiredBaseName,
                entry: entry,
                mode: mode,
                options: perEntryOptions,
                reservedNames: reservedNames
            )
            let files = try files(
                for: entry,
                baseName: uniqueBaseName,
                mode: mode,
                options: perEntryOptions
            )
            for file in files {
                let destinationURL = directoryURL.appendingPathComponent(
                    file.name,
                    isDirectory: false
                )
                try file.data.write(to: destinationURL, options: .atomic)
                reservedNames.insert(file.name.lowercased())
                wroteFile = true
            }
        }

        guard wroteFile else {
            throw MeasurementExportError.noExportableData
        }
    }

    static func dragFiles(
        request: MeasurementHistoryDragExportRequest
    ) throws -> [MeasurementExportFile] {
        guard request.entries.isEmpty == false else {
            throw MeasurementExportError.noSelection
        }

        var options = MeasurementExportOptions.dragDefaults(for: request.mode)
        options.usesPracticalSpectrumRange =
            request.usesPracticalSpectrumRange
        options.spectrumYAxisConfiguration =
            request.spectrumYAxisConfiguration
        let baseNames = MeasurementExportFileNamer.baseNames(
            for: request.entries,
            orderedEntries: request.orderedEntries
        )
        var perEntryOptions = options
        perEntryOptions.includesSwatch = false
        var exportFiles: [MeasurementExportFile] = []

        if let swatchFile = try combinedSwatchFile(
            entries: request.entries,
            orderedEntries: request.orderedEntries,
            mode: request.mode,
            options: options,
            baseNames: baseNames
        ) {
            exportFiles.append(swatchFile)
        }

        exportFiles.append(contentsOf: try request.entries.flatMap {
            entry -> [MeasurementExportFile] in
            guard let baseName = baseNames[entry.id] else { return [] }
            return try files(
                for: entry,
                baseName: baseName,
                mode: request.mode,
                options: perEntryOptions
            )
        })
        return exportFiles
    }

    private static func files(
        for entry: MeasurementHistoryEntry,
        baseName: String,
        mode: MeasurementMode,
        options: MeasurementExportOptions
    ) throws -> [MeasurementExportFile] {
        let measurement = entry.measurement
        var files: [MeasurementExportFile] = []

        if options.includesSpectrumImage,
           measurement.spectrum.isEmpty == false {
            files.append(
                MeasurementExportFile(
                    name: "\(baseName)-Spectrum.png",
                    data: try MeasurementExportImageRenderer.spectrumPNG(
                        measurement: measurement,
                        measurementName: entry.name,
                        usesPracticalSpectrumRange:
                            options.usesPracticalSpectrumRange,
                        yAxisConfiguration:
                            options.spectrumYAxisConfiguration,
                        includesD50Reference: mode == .reflectance
                            ? false
                            : options.includesD50Reference,
                        includesD65Reference: mode == .reflectance
                            ? false
                            : options.includesD65Reference
                    )
                )
            )
        }

        if mode == .reflectance {
            if options.includesCSV,
               measurement.spectrum.isEmpty == false {
                files.append(
                    MeasurementExportFile(
                        name: "\(baseName)-Spectrum.csv",
                        data: MeasurementExportCSVEncoder.spectrum(measurement)
                    )
                )
            }
            return files
        }

        if options.includesCRIImage, measurement.cri != nil {
            files.append(
                MeasurementExportFile(
                    name: "\(baseName)-CRI.png",
                    data: try MeasurementExportImageRenderer.criPNG(
                        measurement: measurement
                    )
                )
            )
        }

        if options.includesTM30Image, measurement.tm30 != nil {
            files.append(
                MeasurementExportFile(
                    name: "\(baseName)-TM-30-15.png",
                    data: try MeasurementExportImageRenderer.tm30PNG(
                        measurement: measurement
                    )
                )
            )
        }

        if options.includesCSV {
            files.append(
                MeasurementExportFile(
                    name: "\(baseName)-Measurement.csv",
                    data: MeasurementExportCSVEncoder.lighting(measurement)
                )
            )
        }

        return files
    }

    private static func combinedSwatchFile(
        entries: [MeasurementHistoryEntry],
        orderedEntries: [MeasurementHistoryEntry],
        mode: MeasurementMode,
        options: MeasurementExportOptions,
        baseNames: [MeasurementHistoryEntry.ID: String]
    ) throws -> MeasurementExportFile? {
        guard mode == .reflectance, options.includesSwatch else {
            return nil
        }

        let swatchEntries = entries.filter { $0.measurement.lab != nil }
        let swatches = swatchEntries.compactMap { entry -> AdobeLabSwatch? in
            guard let lab = entry.measurement.lab,
                  let name = baseNames[entry.id] else {
                return nil
            }
            return AdobeLabSwatch(name: name, lab: lab)
        }
        guard swatches.isEmpty == false else { return nil }

        return MeasurementExportFile(
            name: MeasurementExportFileNamer.combinedSwatchFileName(
                for: swatchEntries,
                orderedEntries: orderedEntries
            ),
            data: try AdobeSwatchExchangeEncoder.encode(swatches: swatches)
        )
    }

    private static func availableFileName(
        _ desiredFileName: String,
        reservedNames: Set<String>
    ) -> String {
        guard reservedNames.contains(desiredFileName.lowercased()) else {
            return desiredFileName
        }

        let pathExtension = (desiredFileName as NSString).pathExtension
        let stem = (desiredFileName as NSString).deletingPathExtension
        var counter = 2
        while true {
            let candidate = pathExtension.isEmpty
                ? "\(stem)-\(counter)"
                : "\(stem)-\(counter).\(pathExtension)"
            if reservedNames.contains(candidate.lowercased()) == false {
                return candidate
            }
            counter += 1
        }
    }

    private static func availableBaseName(
        _ desiredBaseName: String,
        entry: MeasurementHistoryEntry,
        mode: MeasurementMode,
        options: MeasurementExportOptions,
        reservedNames: Set<String>
    ) -> String {
        let suffixes = fileSuffixes(
            entry: entry,
            mode: mode,
            options: options
        )
        var candidate = desiredBaseName
        var counter = 2

        while suffixes.contains(where: {
            reservedNames.contains("\(candidate)\($0)".lowercased())
        }) {
            candidate = "\(desiredBaseName)-\(counter)"
            counter += 1
        }
        return candidate
    }

    private static func fileSuffixes(
        entry: MeasurementHistoryEntry,
        mode: MeasurementMode,
        options: MeasurementExportOptions
    ) -> [String] {
        let measurement = entry.measurement
        var suffixes: [String] = []

        if options.includesSpectrumImage,
           measurement.spectrum.isEmpty == false {
            suffixes.append("-Spectrum.png")
        }
        if mode == .reflectance {
            if options.includesCSV,
               measurement.spectrum.isEmpty == false {
                suffixes.append("-Spectrum.csv")
            }
            return suffixes
        }
        if options.includesCRIImage, measurement.cri != nil {
            suffixes.append("-CRI.png")
        }
        if options.includesTM30Image, measurement.tm30 != nil {
            suffixes.append("-TM-30-15.png")
        }
        if options.includesCSV {
            suffixes.append("-Measurement.csv")
        }
        return suffixes
    }
}
