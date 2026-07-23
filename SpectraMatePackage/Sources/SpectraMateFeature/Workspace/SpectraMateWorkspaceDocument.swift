import Foundation
import SwiftUI
import UniformTypeIdentifiers

extension UTType {
    static let spectraMateWorkspace = UTType(filenameExtension: "spectramate")
        ?? UTType(
            exportedAs: "com.yamonov.spectramate.workspace",
            conformingTo: .json
        )
}

struct MeasurementHistorySnapshot: Codable, Equatable, Sendable {
    let entries: [Entry]
    let modes: [ModeState]

    struct Entry: Codable, Equatable, Sendable {
        let id: UUID
        let name: String?
        let measurement: SpotMeasurement
        let instrumentIdentity: SpotreadInstrumentIdentity?
    }

    struct ModeState: Codable, Equatable, Sendable {
        let mode: MeasurementMode
        let presentationOrder: [UUID]
        let selectedEntryIDs: Set<UUID>
        let activeEntryID: UUID?
        let selectionAnchorID: UUID?
    }

    func validate() throws {
        let entryIDs = entries.map(\.id)
        guard Set(entryIDs).count == entryIDs.count else {
            throw SpectraMateWorkspaceFileError.duplicateHistoryEntry
        }

        let modeValues = modes.map(\.mode)
        guard Set(modeValues).count == modeValues.count,
              Set(modeValues) == Set(MeasurementMode.allCases) else {
            throw SpectraMateWorkspaceFileError.invalidModeStates
        }

        for modeState in modes {
            let modeEntryIDs = entries
                .filter { $0.measurement.mode == modeState.mode }
                .map(\.id)
            let modeEntryIDSet = Set(modeEntryIDs)

            guard modeState.presentationOrder.count == modeEntryIDs.count,
                  Set(modeState.presentationOrder) == modeEntryIDSet,
                  modeState.selectedEntryIDs.isSubset(of: modeEntryIDSet) else {
                throw SpectraMateWorkspaceFileError.invalidModeState(modeState.mode)
            }

            if modeState.selectedEntryIDs.isEmpty {
                guard modeState.activeEntryID == nil,
                      modeState.selectionAnchorID == nil else {
                    throw SpectraMateWorkspaceFileError.invalidModeState(modeState.mode)
                }
            } else {
                guard let activeEntryID = modeState.activeEntryID,
                      modeState.selectedEntryIDs.contains(activeEntryID),
                      let selectionAnchorID = modeState.selectionAnchorID,
                      modeEntryIDSet.contains(selectionAnchorID) else {
                    throw SpectraMateWorkspaceFileError.invalidModeState(modeState.mode)
                }
            }
        }
    }
}

struct SpectraMateWorkspaceState: Codable, Equatable, Sendable {
    let selectedMode: MeasurementMode?
    let selectedSidebarTab: MeasurementSidebarTab
    let history: MeasurementHistorySnapshot

    var hasContent: Bool {
        history.entries.isEmpty == false
    }

    func validate() throws {
        try history.validate()
    }
}

struct SpectraMateWorkspaceArchive: Codable, Equatable, Sendable {
    static let currentFormatVersion = 1

    let formatVersion: Int
    let savedAt: Date
    let workspace: SpectraMateWorkspaceState

    init(
        formatVersion: Int = Self.currentFormatVersion,
        workspace: SpectraMateWorkspaceState,
        savedAt: Date = Date()
    ) {
        self.formatVersion = formatVersion
        self.savedAt = savedAt
        self.workspace = workspace
    }

    func validate() throws {
        guard formatVersion == Self.currentFormatVersion else {
            throw SpectraMateWorkspaceFileError.unsupportedFormatVersion(formatVersion)
        }
        try workspace.validate()
    }
}

enum SpectraMateWorkspaceFileError: Error, Equatable, LocalizedError {
    case missingFileContents
    case unsupportedFormatVersion(Int)
    case duplicateHistoryEntry
    case invalidModeStates
    case invalidModeState(MeasurementMode)

    var errorDescription: String? {
        switch self {
        case .missingFileContents:
            "ワークスペースファイルの内容を読み取れませんでした。"
        case .unsupportedFormatVersion(let version):
            "このワークスペースファイルの形式バージョン（\(version)）には対応していません。"
        case .duplicateHistoryEntry:
            "ワークスペースファイルに重複した測定履歴が含まれています。"
        case .invalidModeStates:
            "ワークスペースファイルの測定モード情報が壊れています。"
        case .invalidModeState(let mode):
            "\(mode.title)ワークスペースの履歴情報が壊れています。"
        }
    }
}

struct SpectraMateWorkspaceDocument: FileDocument {
    static let readableContentTypes: [UTType] = [.spectraMateWorkspace]

    let archive: SpectraMateWorkspaceArchive

    init(
        workspace: SpectraMateWorkspaceState,
        savedAt: Date = Date()
    ) throws {
        let archive = SpectraMateWorkspaceArchive(
            workspace: workspace,
            savedAt: savedAt
        )
        try archive.validate()
        self.archive = archive
    }

    init(configuration: ReadConfiguration) throws {
        guard let data = configuration.file.regularFileContents else {
            throw SpectraMateWorkspaceFileError.missingFileContents
        }
        try self.init(data: data)
    }

    init(data: Data) throws {
        let archive = try Self.decoder.decode(SpectraMateWorkspaceArchive.self, from: data)
        try archive.validate()
        self.archive = archive
    }

    func fileWrapper(configuration _: WriteConfiguration) throws -> FileWrapper {
        return FileWrapper(
            regularFileWithContents: try encodedData()
        )
    }

    func encodedData() throws -> Data {
        try archive.validate()
        return try Self.encoder.encode(archive)
    }

    private static var encoder: JSONEncoder {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        return encoder
    }

    private static var decoder: JSONDecoder {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return decoder
    }
}
