import Foundation
import UniformTypeIdentifiers

enum MeasurementHistoryDragItemProvider {
    static let aseFileName = "IwashiScope-Lab-Swatches.ase"

    @MainActor
    static func make(
        entryIDs: Set<MeasurementHistoryEntry.ID>,
        swatches: [AdobeLabSwatch],
        exportRequest: MeasurementHistoryDragExportRequest? = nil
    ) -> NSItemProvider {
        let preparedExport = exportRequest.map(prepareExport)
        let provider: NSItemProvider
        if exportRequest?.mode != .reflectance,
           case .success(let folderURL) = preparedExport,
           let folderProvider = NSItemProvider(contentsOf: folderURL) {
            provider = folderProvider
        } else {
            provider = NSItemProvider()
        }

        // The plain-text representation is consumed only by IwashiScope's
        // reordering drop targets. Own-process visibility prevents Finder
        // from treating the card as a dragged text clipping.
        provider.registerDataRepresentation(
            forTypeIdentifier: MeasurementHistoryDragPayload.contentType.identifier,
            visibility: .ownProcess
        ) { completionHandler in
            do {
                completionHandler(
                    try MeasurementHistoryDragPayload.encode(entryIDs: entryIDs),
                    nil
                )
            } catch {
                completionHandler(nil, error)
            }
            return nil
        }

        if let exportRequest, let preparedExport {
            if exportRequest.mode == .reflectance {
                registerReflectanceSwatch(
                    on: provider,
                    preparedExport: preparedExport
                )
            }
        }

        if swatches.isEmpty == false, exportRequest == nil {
            provider.suggestedName = aseFileName
            provider.registerFileRepresentation(
                for: .adobeSwatchExchange,
                visibility: .all,
                openInPlace: false
            ) { completionHandler in
                let progress = Progress(totalUnitCount: 1)

                do {
                    let data = try AdobeSwatchExchangeEncoder.encode(swatches: swatches)
                    let temporaryDirectory = FileManager.default.temporaryDirectory
                        .appendingPathComponent(
                            "IwashiScope-Drag-\(UUID().uuidString)",
                            isDirectory: true
                        )
                    try FileManager.default.createDirectory(
                        at: temporaryDirectory,
                        withIntermediateDirectories: true
                    )
                    let fileURL = temporaryDirectory
                        .appendingPathComponent(aseFileName, isDirectory: false)
                    try data.write(to: fileURL, options: .atomic)
                    progress.completedUnitCount = 1
                    completionHandler(fileURL, false, nil)
                } catch {
                    completionHandler(nil, false, error)
                }

                return progress
            }
        }

        return provider
    }

    private static func registerReflectanceSwatch(
        on provider: NSItemProvider,
        preparedExport: Result<URL, any Error>
    ) {
        provider.suggestedName = try? preparedExport.get().lastPathComponent
        provider.registerFileRepresentation(
            for: .adobeSwatchExchange,
            visibility: .all,
            openInPlace: false
        ) { completionHandler in
            let progress = Progress(totalUnitCount: 1)
            complete(preparedExport, progress: progress, using: completionHandler)
            return progress
        }
    }

    @MainActor
    private static func prepareExport(
        request: MeasurementHistoryDragExportRequest
    ) -> Result<URL, any Error> {
        Result {
            let files = try MeasurementExporter.dragFiles(request: request)
            guard files.isEmpty == false else {
                throw MeasurementExportError.noExportableData
            }

            let temporaryRoot = FileManager.default.temporaryDirectory
                .appendingPathComponent(
                    "IwashiScope-Drag-\(UUID().uuidString)",
                    isDirectory: true
                )
            try FileManager.default.createDirectory(
                at: temporaryRoot,
                withIntermediateDirectories: true
            )

            if request.mode == .reflectance {
                guard files.count == 1,
                      let swatchFile = files.first,
                      swatchFile.name.hasSuffix(".ase") else {
                    throw MeasurementExportError.noExportableData
                }
                let fileURL = temporaryRoot.appendingPathComponent(
                    swatchFile.name,
                    isDirectory: false
                )
                try swatchFile.data.write(to: fileURL, options: .atomic)
                return fileURL
            }

            let folderURL = temporaryRoot.appendingPathComponent(
                exportFolderName(for: request),
                isDirectory: true
            )
            try FileManager.default.createDirectory(
                at: folderURL,
                withIntermediateDirectories: true
            )
            for file in files {
                try file.data.write(
                    to: folderURL.appendingPathComponent(file.name),
                    options: .atomic
                )
            }
            return folderURL
        }
    }

    private static func complete(
        _ preparedExport: Result<URL, any Error>,
        progress: Progress,
        using completionHandler: @escaping (URL?, Bool, (any Error)?) -> Void
    ) {
        switch preparedExport {
        case .success(let url):
            progress.completedUnitCount = 1
            completionHandler(url, false, nil)
        case .failure(let error):
            completionHandler(nil, false, error)
        }
    }

    private static func exportFolderName(
        for request: MeasurementHistoryDragExportRequest
    ) -> String {
        guard request.entries.count == 1,
              let entry = request.entries.first,
              let baseName = MeasurementExportFileNamer.baseNames(
                  for: request.entries,
                  orderedEntries: request.orderedEntries
              )[entry.id] else {
            return "IwashiScope-Export"
        }
        return "\(baseName)-Export"
    }
}
