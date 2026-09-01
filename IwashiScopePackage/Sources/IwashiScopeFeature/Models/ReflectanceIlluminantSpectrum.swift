import Foundation

enum ReflectanceIlluminantSelection: Hashable, Sendable {
    case none
    case cie(CIEReferenceIlluminant)

    var illuminant: CIEReferenceIlluminant? {
        switch self {
        case .none: nil
        case let .cie(illuminant): illuminant
        }
    }
}

enum ReflectanceIlluminantSourceKind: String, CaseIterable, Identifiable, Sendable {
    case cie
    case user1
    case user2
    case user3

    var id: String { rawValue }

    var title: String {
        switch self {
        case .cie: "CIE参考光源"
        case .user1: UserIlluminantSlot.user1.title
        case .user2: UserIlluminantSlot.user2.title
        case .user3: UserIlluminantSlot.user3.title
        }
    }

    var userSlot: UserIlluminantSlot? {
        switch self {
        case .cie: nil
        case .user1: .user1
        case .user2: .user2
        case .user3: .user3
        }
    }

    func isAvailable(
        userSlots: Set<UserIlluminantSlot>
    ) -> Bool {
        guard let userSlot else { return true }
        return userSlots.contains(userSlot)
    }
}

struct ReflectanceIlluminantSpectrumResult: Equatable, Sendable {
    let selectedSource: IlluminantSpectrumDefinition?
    let measuredReflectance: [SpectralSample]
    let illuminant: [SpectralSample]
    let reflectedLight: [SpectralSample]
    let wavelengthRange: ClosedRange<Double>
    let requiresUVWarning: Bool

    var selectedIlluminant: CIEReferenceIlluminant? {
        guard case let .cie(illuminant) = selectedSource?.origin else {
            return nil
        }
        return illuminant
    }

    var automaticUpperBound: Double {
        let maximumValue = [measuredReflectance, illuminant, reflectedLight]
            .lazy
            .flatMap { $0 }
            .map(\.value)
            .filter(\.isFinite)
            .max() ?? 100
        return max(100, maximumValue * 1.08)
    }
}

enum ReflectanceIlluminantSpectrumCalculator {
    private static let supportedVisibleRange = 380.0...730.0

    static func result(
        for measurement: SpotMeasurement?,
        illuminant selectedIlluminant: CIEReferenceIlluminant?,
        displayRange requestedDisplayRange: ClosedRange<Double>? = nil
    ) -> ReflectanceIlluminantSpectrumResult? {
        result(
            for: measurement,
            source: selectedIlluminant.map {
                IlluminantSpectrumDefinition(cie: $0)
            },
            displayRange: requestedDisplayRange
        )
    }

    static func result(
        for measurement: SpotMeasurement?,
        source selectedSource: IlluminantSpectrumDefinition?,
        displayRange requestedDisplayRange: ClosedRange<Double>? = nil
    ) -> ReflectanceIlluminantSpectrumResult? {
        guard let measurement,
              measurement.mode == .reflectance else {
            return nil
        }

        let requestedRange = requestedDisplayRange ?? supportedVisibleRange
        let displayStart = max(
            requestedRange.lowerBound,
            supportedVisibleRange.lowerBound
        )
        let displayEnd = min(
            requestedRange.upperBound,
            supportedVisibleRange.upperBound
        )
        guard displayStart <= displayEnd else { return nil }
        let displayRange = displayStart...displayEnd

        let measuredReflectance = measurement.spectrum
            .filter { sample in
                sample.wavelength.isFinite
                    && sample.value.isFinite
                    && displayRange.contains(sample.wavelength)
            }
            .sorted { $0.wavelength < $1.wavelength }
        guard let first = measuredReflectance.first,
              let last = measuredReflectance.last else {
            return nil
        }

        let wavelengthRange = last.wavelength > first.wavelength
            ? first.wavelength...last.wavelength
            : first.wavelength...(first.wavelength + 1)

        guard let selectedSource else {
            return ReflectanceIlluminantSpectrumResult(
                selectedSource: nil,
                measuredReflectance: measuredReflectance,
                illuminant: [],
                reflectedLight: [],
                wavelengthRange: wavelengthRange,
                requiresUVWarning: false
            )
        }

        let source = selectedSource.samples
        guard let peak = source.lazy.map(\.value).filter(\.isFinite).max(),
              peak > 0 else {
            return nil
        }

        let normalizedIlluminant = source.map { sample in
            SpectralSample(
                id: sample.id,
                wavelength: sample.wavelength,
                value: max(0, sample.value) / peak * 100
            )
        }
        let visibleIlluminant = normalizedIlluminant.filter {
            wavelengthRange.contains($0.wavelength)
        }
        let reflectedLight = measuredReflectance.compactMap { sample -> SpectralSample? in
            guard let illuminantValue = interpolatedValue(
                in: normalizedIlluminant,
                at: sample.wavelength
            ) else {
                return nil
            }
            return SpectralSample(
                id: sample.id,
                wavelength: sample.wavelength,
                value: illuminantValue * max(0, sample.value) / 100
            )
        }
        guard visibleIlluminant.isEmpty == false,
              reflectedLight.isEmpty == false else {
            return nil
        }

        return ReflectanceIlluminantSpectrumResult(
            selectedSource: selectedSource,
            measuredReflectance: measuredReflectance,
            illuminant: visibleIlluminant,
            reflectedLight: reflectedLight,
            wavelengthRange: wavelengthRange,
            requiresUVWarning: true
        )
    }

    private static func interpolatedValue(
        in samples: [SpectralSample],
        at wavelength: Double
    ) -> Double? {
        guard let first = samples.first,
              let last = samples.last,
              wavelength >= first.wavelength,
              wavelength <= last.wavelength else {
            return nil
        }
        if wavelength == first.wavelength { return first.value }
        if wavelength == last.wavelength { return last.value }

        var lowerIndex = 0
        var upperIndex = samples.count - 1
        while lowerIndex + 1 < upperIndex {
            let midpoint = (lowerIndex + upperIndex) / 2
            if samples[midpoint].wavelength <= wavelength {
                lowerIndex = midpoint
            } else {
                upperIndex = midpoint
            }
        }

        let lower = samples[lowerIndex]
        let upper = samples[upperIndex]
        let width = upper.wavelength - lower.wavelength
        guard width > 0 else { return nil }
        let fraction = (wavelength - lower.wavelength) / width
        return lower.value + ((upper.value - lower.value) * fraction)
    }
}
