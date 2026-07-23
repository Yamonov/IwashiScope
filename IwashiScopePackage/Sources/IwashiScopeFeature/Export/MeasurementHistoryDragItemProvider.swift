import Foundation
import UniformTypeIdentifiers

enum MeasurementHistoryDragItemProvider {
    static let aseFileName = "IwashiScope-Lab-Swatches.ase"

    static func make(
        internalIdentifier: String,
        swatches: [AdobeLabSwatch],
        exportRequest: MeasurementHistoryDragExportRequest? = nil
    ) -> NSItemProvider {
        let provider = NSItemProvider()
        let internalIdentifierData = Data(internalIdentifier.utf8)

        // The plain-text identifier is only for IwashiScope's insertion drop
        // zones. Hiding it from other processes prevents Finder from treating
        // the card as a dragged text clipping.
        provider.registerDataRepresentation(
            forTypeIdentifier: UTType.plainText.identifier,
            visibility: .ownProcess
        ) { completionHandler in
            completionHandler(internalIdentifierData, nil)
            return nil
        }

        if let exportRequest {
            if exportRequest.mode == .reflectance {
                registerReflectanceSwatch(
                    on: provider,
                    request: exportRequest
                )
            } else {
                registerExportFolder(
                    on: provider,
                    request: exportRequest
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
        request: MeasurementHistoryDragExportRequest
    ) {
        let swatchEntries = request.entries.filter {
            $0.measurement.lab != nil
        }
        let fileName = MeasurementExportFileNamer.combinedSwatchFileName(
            for: swatchEntries,
            orderedEntries: request.orderedEntries
        )
        provider.suggestedName = fileName
        provider.registerFileRepresentation(
            for: .adobeSwatchExchange,
            visibility: .all,
            openInPlace: false
        ) { completionHandler in
            let progress = Progress(totalUnitCount: 1)
            let completion = MeasurementHistoryFileRepresentationCompletion(
                completionHandler
            )

            Task { @MainActor in
                do {
                    let files = try MeasurementExporter.dragFiles(
                        request: request
                    )
                    guard files.count == 1,
                          let swatchFile = files.first,
                          swatchFile.name.hasSuffix(".ase") else {
                        throw MeasurementExportError.noExportableData
                    }

                    let temporaryDirectory = FileManager.default.temporaryDirectory
                        .appendingPathComponent(
                            "IwashiScope-Drag-\(UUID().uuidString)",
                            isDirectory: true
                        )
                    try FileManager.default.createDirectory(
                        at: temporaryDirectory,
                        withIntermediateDirectories: true
                    )
                    let fileURL = temporaryDirectory.appendingPathComponent(
                        swatchFile.name,
                        isDirectory: false
                    )
                    try swatchFile.data.write(to: fileURL, options: .atomic)

                    progress.completedUnitCount = 1
                    completion.call(fileURL, false, nil)
                } catch {
                    completion.call(nil, false, error)
                }
            }

            return progress
        }
    }

    private static func registerExportFolder(
        on provider: NSItemProvider,
        request: MeasurementHistoryDragExportRequest
    ) {
        let folderName = exportFolderName(for: request)
        provider.suggestedName = folderName
        provider.registerFileRepresentation(
            for: .folder,
            visibility: .all,
            openInPlace: false
        ) { completionHandler in
            let progress = Progress(totalUnitCount: 1)
            let completion = MeasurementHistoryFileRepresentationCompletion(
                completionHandler
            )

            Task { @MainActor in
                do {
                    let files = try MeasurementExporter.dragFiles(
                        request: request
                    )
                    guard files.isEmpty == false else {
                        throw MeasurementExportError.noExportableData
                    }

                    let temporaryRoot = FileManager.default.temporaryDirectory
                        .appendingPathComponent(
                            "IwashiScope-Drag-\(UUID().uuidString)",
                            isDirectory: true
                        )
                    let folderURL = temporaryRoot.appendingPathComponent(
                        folderName,
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

                    progress.completedUnitCount = 1
                    completion.call(folderURL, false, nil)
                } catch {
                    completion.call(nil, false, error)
                }
            }

            return progress
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

private final class MeasurementHistoryFileRepresentationCompletion: @unchecked Sendable {
    private let handler: (URL?, Bool, (any Error)?) -> Void

    init(_ handler: @escaping (URL?, Bool, (any Error)?) -> Void) {
        self.handler = handler
    }

    func call(
        _ url: URL?,
        _ isInPlace: Bool,
        _ error: (any Error)?
    ) {
        handler(url, isInPlace, error)
    }
}
