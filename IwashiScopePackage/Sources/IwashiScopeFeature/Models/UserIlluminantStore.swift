import Foundation
import Observation

enum UserIlluminantSlot: String, CaseIterable, Codable, Identifiable, Sendable {
    case user1
    case user2
    case user3

    var id: String { rawValue }

    var title: String {
        switch self {
        case .user1: "User定義１"
        case .user2: "User定義２"
        case .user3: "User定義３"
        }
    }
}

struct UserIlluminantSpectrum: Equatable, Sendable {
    let name: String?
    let measuredAt: Date
    let samples: [SpectralSample]

    init?(
        name: String? = nil,
        measuredAt: Date,
        samples: [SpectralSample]
    ) {
        let orderedSamples = samples
            .filter {
                $0.wavelength.isFinite
                    && $0.value.isFinite
                    && $0.value >= 0
            }
            .sorted { $0.wavelength < $1.wavelength }
        guard orderedSamples.count >= 2,
              zip(orderedSamples, orderedSamples.dropFirst()).allSatisfy({ pair in
                  pair.0.wavelength < pair.1.wavelength
              }) else {
            return nil
        }
        self.name = Self.normalizedName(name)
        self.measuredAt = measuredAt
        self.samples = orderedSamples
    }

    private static func normalizedName(_ name: String?) -> String? {
        guard let name else { return nil }
        let normalized = name
            .replacingOccurrences(of: "\r\n", with: " ")
            .replacingOccurrences(of: "\n", with: " ")
            .replacingOccurrences(of: "\r", with: " ")
            .trimmingCharacters(in: .whitespacesAndNewlines)
        return normalized.isEmpty ? nil : normalized
    }
}

enum IlluminantSpectrumOrigin: Equatable, Sendable {
    case cie(CIEReferenceIlluminant)
    case user(UserIlluminantSlot)
}

struct IlluminantSpectrumDefinition: Equatable, Identifiable, Sendable {
    let origin: IlluminantSpectrumOrigin
    let displayName: String
    let userName: String?
    let measuredAt: Date?
    let samples: [SpectralSample]

    var id: String {
        switch origin {
        case let .cie(illuminant): "cie-\(illuminant.rawValue)"
        case let .user(slot): "user-\(slot.rawValue)"
        }
    }

    init(cie illuminant: CIEReferenceIlluminant) {
        origin = .cie(illuminant)
        displayName = illuminant.rawValue
        userName = nil
        measuredAt = nil
        samples = illuminant.samples
    }

    init(slot: UserIlluminantSlot, spectrum: UserIlluminantSpectrum) {
        origin = .user(slot)
        displayName = slot.title
        userName = spectrum.name
        measuredAt = spectrum.measuredAt
        samples = spectrum.samples
    }
}

@MainActor
@Observable
final class UserIlluminantStore {
    @ObservationIgnored private let historyStore: MeasurementHistoryStore

    init(historyStore: MeasurementHistoryStore) {
        self.historyStore = historyStore
    }

    var availableSlots: Set<UserIlluminantSlot> {
        historyStore.availableUserIlluminantSlots
    }

    func hasSpectrum(for slot: UserIlluminantSlot) -> Bool {
        historyStore.userIlluminantEntry(for: slot) != nil
    }

    func source(for slot: UserIlluminantSlot) -> IlluminantSpectrumDefinition? {
        guard let entry = historyStore.userIlluminantEntry(for: slot),
              let spectrum = UserIlluminantSpectrum(
                  name: entry.name,
                  measuredAt: entry.measurement.capturedAt,
                  samples: entry.measurement.spectrum
              ) else {
            return nil
        }
        return IlluminantSpectrumDefinition(slot: slot, spectrum: spectrum)
    }
}
