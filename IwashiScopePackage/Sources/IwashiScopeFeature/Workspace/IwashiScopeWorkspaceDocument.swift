import Foundation
import SwiftUI
import UniformTypeIdentifiers

extension UTType {
    static let iwashiScopeWorkspace = UTType(filenameExtension: "iwashiscope")
        ?? UTType(
            exportedAs: "com.yamonov.iwashiscope.workspace",
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
            throw IwashiScopeWorkspaceFileError.duplicateHistoryEntry
        }

        let modeValues = modes.map(\.mode)
        guard Set(modeValues).count == modeValues.count,
              Set(modeValues) == Set(MeasurementMode.allCases) else {
            throw IwashiScopeWorkspaceFileError.invalidModeStates
        }

        for modeState in modes {
            let modeEntryIDs = entries
                .filter { $0.measurement.mode == modeState.mode }
                .map(\.id)
            let modeEntryIDSet = Set(modeEntryIDs)

            guard modeState.presentationOrder.count == modeEntryIDs.count,
                  Set(modeState.presentationOrder) == modeEntryIDSet,
                  modeState.selectedEntryIDs.isSubset(of: modeEntryIDSet) else {
                throw IwashiScopeWorkspaceFileError.invalidModeState(modeState.mode)
            }

            if modeState.selectedEntryIDs.isEmpty {
                guard modeState.activeEntryID == nil,
                      modeState.selectionAnchorID == nil else {
                    throw IwashiScopeWorkspaceFileError.invalidModeState(modeState.mode)
                }
            } else {
                guard let activeEntryID = modeState.activeEntryID,
                      modeState.selectedEntryIDs.contains(activeEntryID),
                      let selectionAnchorID = modeState.selectionAnchorID,
                      modeEntryIDSet.contains(selectionAnchorID) else {
                    throw IwashiScopeWorkspaceFileError.invalidModeState(modeState.mode)
                }
            }
        }
    }
}

struct IwashiScopeWorkspaceState: Codable, Equatable, Sendable {
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

struct IwashiScopeWorkspaceArchive: Codable, Equatable, Sendable {
    static let currentFormatVersion = 1

    let formatVersion: Int
    let savedAt: Date
    let workspace: IwashiScopeWorkspaceState

    init(
        formatVersion: Int = Self.currentFormatVersion,
        workspace: IwashiScopeWorkspaceState,
        savedAt: Date = Date()
    ) {
        self.formatVersion = formatVersion
        self.savedAt = savedAt
        self.workspace = workspace
    }

    func validate() throws {
        guard formatVersion == Self.currentFormatVersion else {
            throw IwashiScopeWorkspaceFileError.unsupportedFormatVersion(formatVersion)
        }
        try workspace.validate()
    }
}

enum IwashiScopeWorkspaceFileError: Error, Equatable, LocalizedError {
    case missingFileContents
    case unsupportedFormatVersion(Int)
    case duplicateHistoryEntry
    case invalidModeStates
    case invalidModeState(MeasurementMode)

    var errorDescription: String? {
        switch self {
        case .missingFileContents:
            String(localized: "ワークスペースファイルの内容を読み取れませんでした。")
        case .unsupportedFormatVersion(let version):
            String(localized: "このワークスペースファイルの形式バージョン（\(version)）には対応していません。")
        case .duplicateHistoryEntry:
            String(localized: "ワークスペースファイルに重複した測定履歴が含まれています。")
        case .invalidModeStates:
            String(localized: "ワークスペースファイルの測定モード情報が壊れています。")
        case .invalidModeState(let mode):
            String(localized: "\(mode.title)ワークスペースの履歴情報が壊れています。")
        }
    }
}

struct IwashiScopeWorkspaceDocument: FileDocument {
    static let readableContentTypes: [UTType] = [.iwashiScopeWorkspace]

    let archive: IwashiScopeWorkspaceArchive

    init(
        workspace: IwashiScopeWorkspaceState,
        savedAt: Date = Date()
    ) throws {
        let archive = IwashiScopeWorkspaceArchive(
            workspace: workspace,
            savedAt: savedAt
        )
        try archive.validate()
        self.archive = archive
    }

    init(configuration: ReadConfiguration) throws {
        guard let data = configuration.file.regularFileContents else {
            throw IwashiScopeWorkspaceFileError.missingFileContents
        }
        try self.init(data: data)
    }

    init(data: Data) throws {
        let archive = try Self.decoder.decode(IwashiScopeWorkspaceArchive.self, from: data)
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
