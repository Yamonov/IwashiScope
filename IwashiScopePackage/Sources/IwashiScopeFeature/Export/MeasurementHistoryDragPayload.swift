import Foundation
import UniformTypeIdentifiers

struct MeasurementHistoryDragPayload: Codable, Sendable {
    static let contentType = UTType.plainText

    let entryIDs: [MeasurementHistoryEntry.ID]

    static func encode(
        entryIDs: Set<MeasurementHistoryEntry.ID>
    ) throws -> Data {
        try JSONEncoder().encode(
            Self(entryIDs: entryIDs.sorted { $0.uuidString < $1.uuidString })
        )
    }

    static func decode(_ data: Data) throws -> Self {
        try JSONDecoder().decode(Self.self, from: data)
    }
}
