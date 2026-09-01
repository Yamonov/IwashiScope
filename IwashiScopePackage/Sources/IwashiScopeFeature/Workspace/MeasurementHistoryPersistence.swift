import Foundation

struct MeasurementHistoryPersistence: Sendable {
    static let currentFormatVersion = 1

    let fileURL: URL?

    static var applicationSupport: Self {
        let fileManager = FileManager.default
        let baseDirectory = fileManager.urls(
            for: .applicationSupportDirectory,
            in: .userDomainMask
        ).first ?? fileManager.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support", isDirectory: true)

        return Self(
            fileURL: baseDirectory
                .appendingPathComponent("IwashiScope", isDirectory: true)
                .appendingPathComponent("MeasurementHistory.json", isDirectory: false)
        )
    }

    static let disabled = Self(fileURL: nil)

    func load() throws -> MeasurementHistorySnapshot? {
        guard let fileURL else { return nil }

        let fileManager = FileManager.default
        guard fileManager.fileExists(atPath: fileURL.path) else {
            return nil
        }

        let data = try Data(contentsOf: fileURL)
        let archive = try Self.decoder.decode(
            PersistedMeasurementHistoryArchive.self,
            from: data
        )
        try archive.validate()
        return archive.history
    }

    func save(_ history: MeasurementHistorySnapshot) throws {
        guard let fileURL else { return }

        let archive = PersistedMeasurementHistoryArchive(history: history)
        try archive.validate()

        let fileManager = FileManager.default
        try fileManager.createDirectory(
            at: fileURL.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )
        let data = try Self.encoder.encode(archive)
        try data.write(to: fileURL, options: .atomic)
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

private struct PersistedMeasurementHistoryArchive: Codable, Equatable, Sendable {
    let formatVersion: Int
    let savedAt: Date
    let history: MeasurementHistorySnapshot

    init(
        formatVersion: Int = MeasurementHistoryPersistence.currentFormatVersion,
        savedAt: Date = Date(),
        history: MeasurementHistorySnapshot
    ) {
        self.formatVersion = formatVersion
        self.savedAt = savedAt
        self.history = history
    }

    func validate() throws {
        guard formatVersion == MeasurementHistoryPersistence.currentFormatVersion else {
            throw MeasurementHistoryPersistenceError.unsupportedFormatVersion(formatVersion)
        }
        try history.validate()
    }
}

enum MeasurementHistoryPersistenceError: Error, Equatable, LocalizedError, Sendable {
    case unsupportedFormatVersion(Int)

    var errorDescription: String? {
        switch self {
        case .unsupportedFormatVersion(let version):
            String(
                localized: "保存された測定履歴の形式バージョン（\(version)）には対応していません。"
            )
        }
    }
}

actor MeasurementHistoryPersistenceWriter {
    private let persistence: MeasurementHistoryPersistence

    init(persistence: MeasurementHistoryPersistence) {
        self.persistence = persistence
    }

    func save(_ history: MeasurementHistorySnapshot) -> String? {
        do {
            try persistence.save(history)
            return nil
        } catch {
            return error.localizedDescription
        }
    }
}
