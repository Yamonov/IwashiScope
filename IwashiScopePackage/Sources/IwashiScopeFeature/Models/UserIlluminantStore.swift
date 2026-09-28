import Foundation
import Observation

enum UserIlluminantSlot: String, CaseIterable, Codable, Identifiable, Sendable {
    case user1
    case user2
    case user3
    case user4
    case user5
    case user6
    case user7
    case user8
    case user9
    case user10
    case user11
    case user12
    case user13
    case user14
    case user15
    case user16
    case user17
    case user18
    case user19
    case user20
    case user21
    case user22
    case user23
    case user24
    case user25
    case user26
    case user27
    case user28
    case user29
    case user30
    case user31
    case user32
    case user33
    case user34
    case user35
    case user36
    case user37
    case user38
    case user39
    case user40
    case user41
    case user42
    case user43
    case user44
    case user45
    case user46
    case user47
    case user48
    case user49
    case user50
    case user51
    case user52
    case user53
    case user54
    case user55
    case user56
    case user57
    case user58
    case user59
    case user60
    case user61
    case user62
    case user63
    case user64
    case user65
    case user66
    case user67
    case user68
    case user69
    case user70
    case user71
    case user72
    case user73
    case user74
    case user75
    case user76
    case user77
    case user78
    case user79
    case user80
    case user81
    case user82
    case user83
    case user84
    case user85
    case user86
    case user87
    case user88
    case user89
    case user90
    case user91
    case user92
    case user93
    case user94
    case user95
    case user96
    case user97
    case user98
    case user99
    case user100

    var id: String { rawValue }
    var number: Int { Int(rawValue.dropFirst(4))! }
    var title: String { "ユーザー定義光源\(number)" }
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
