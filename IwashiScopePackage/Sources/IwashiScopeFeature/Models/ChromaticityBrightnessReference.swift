import Foundation

/// A white-relative reference signal, not a prediction of subjective brightness.
/// P uses M, D uses L, T uses the existing CIE2015 L/M luminance combination.
struct ChromaticityBrightnessReference: Equatable, Sendable {
    let mode: ConfusionColorMode
    let requestedLuminance: Double
    let relativeLuminance: Double

    var isDisplayLimited: Bool { requestedLuminance > 1 }

    init?(lms: Vector3, whiteLMS: Vector3, mode: ConfusionColorMode) {
        guard mode != .none,
              let value = Self.response(lms, relativeTo: whiteLMS, mode: mode) else { return nil }
        self.mode = mode
        requestedLuminance = value
        relativeLuminance = min(1, value)
    }

    static func response(_ lms: Vector3, relativeTo white: Vector3, mode: ConfusionColorMode) -> Double? {
        guard [lms.first, lms.second, lms.third, white.first, white.second, white.third]
            .allSatisfy({ $0.isFinite && $0 >= 0 }) else { return nil }
        let numerator: Double
        let denominator: Double
        switch mode {
        case .none: return nil
        case .protan: (numerator, denominator) = (lms.second, white.second)
        case .deutan: (numerator, denominator) = (lms.first, white.first)
        case .tritan:
            numerator = CIE2006Chromaticity.xyzF(from: lms).second
            denominator = CIE2006Chromaticity.xyzF(from: white).second
        }
        guard denominator > 0 else { return nil }
        let value = numerator / denominator
        return value.isFinite ? value : nil
    }

    /// The display palette has a fixed full-range D50 white. Keep it distinct from
    /// the measurement's same-range white; normalization bridges these two units.
    static let paletteWhiteLMS: Vector3? = {
        guard let palette = ChromaticityBackgroundPalette.shared else { return nil }
        let value = ChromaticityConfusionBand.lms(fromXYZF: palette.white.xyzF)
        let gain = 100 / palette.white.xyzF.second
        return .init(first: value.first * gain, second: value.second * gain, third: value.third * gain)
    }()

    /// Useful for diagrams of synthetic LMS data with the palette's own white.
    static func forBand(_ band: ChromaticityConfusionBand) -> Self? {
        guard let white = paletteWhiteLMS else { return nil }
        return Self(lms: band.referenceLMS, whiteLMS: white, mode: band.colorMode)
    }
}
