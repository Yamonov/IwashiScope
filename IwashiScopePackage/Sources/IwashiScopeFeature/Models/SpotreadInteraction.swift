import Foundation

enum SpotreadInteractionDirection: Equatable, Sendable {
    case input
    case output
    case lifecycle

    var label: String {
        switch self {
        case .input:
            "APP → spotread"
        case .output:
            "spotread → APP"
        case .lifecycle:
            String(localized: "プロセス")
        }
    }
}

enum SpotreadInputDeliveryState: Equatable, Sendable {
    case pending
    case sent
    case failed

    var label: String {
        switch self {
        case .pending:
            String(localized: "送信中")
        case .sent:
            String(localized: "送信済み")
        case .failed:
            String(localized: "送信失敗")
        }
    }
}

struct SpotreadInteraction: Identifiable, Sendable {
    let id: UUID
    let timestamp: Date
    let sessionID: UUID?
    let direction: SpotreadInteractionDirection
    var content: String
    var inputDeliveryState: SpotreadInputDeliveryState?

    init(
        id: UUID = UUID(),
        timestamp: Date = Date(),
        sessionID: UUID? = nil,
        direction: SpotreadInteractionDirection,
        content: String,
        inputDeliveryState: SpotreadInputDeliveryState? = nil
    ) {
        self.id = id
        self.timestamp = timestamp
        self.sessionID = sessionID
        self.direction = direction
        self.content = content
        self.inputDeliveryState = inputDeliveryState
    }

    var displayContent: String {
        guard direction == .input else { return content }

        return content.unicodeScalars.map { scalar in
            switch scalar.value {
            case 0x09:
                "<TAB>"
            case 0x0A:
                "<LF>"
            case 0x0D:
                "<CR>"
            case 0x1B:
                "<ESC>"
            case 0x20:
                "<SPACE>"
            case 0x00...0x1F, 0x7F:
                String(format: "<0x%02X>", scalar.value)
            default:
                String(scalar)
            }
        }
        .joined()
    }
}
