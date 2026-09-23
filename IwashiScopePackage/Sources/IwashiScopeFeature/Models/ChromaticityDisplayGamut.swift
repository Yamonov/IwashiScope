import CoreGraphics
import Foundation

/// Fit toward a neutral of the SAME reference luminance. Both endpoints therefore
/// have the same P/D/T scalar response; clipping changes chroma, not that response.
struct ChromaticityDisplayGamut {
    struct Sample {
        let linearRGB: Vector3
        let chromaScale: Double
    }

    let whiteRGB: Vector3

    init?(linearColorSpace: CGColorSpace) {
        guard linearColorSpace.model == .rgb,
              let palette = ChromaticityBackgroundPalette.shared,
              let sourceSpace = CGColorSpace(name: CGColorSpace.extendedLinearSRGB) else { return nil }
        let v = palette.white.linearRGB
        guard let source = CGColor(colorSpace: sourceSpace, components: [v.first, v.second, v.third, 1]),
              let converted = source.converted(to: linearColorSpace, intent: .relativeColorimetric, options: nil),
              let c = converted.components, c.count == 4,
              c.prefix(3).allSatisfy({ $0.isFinite && $0 > 0 && $0 <= 1 + 1e-6 }) else { return nil }
        whiteRGB = .init(first: min(1, c[0]), second: min(1, c[1]), third: min(1, c[2]))
    }

    func sample(candidate: Vector3, relativeLuminance: Double) -> Sample? {
        guard relativeLuminance.isFinite, (0...1).contains(relativeLuminance),
              [candidate.first, candidate.second, candidate.third].allSatisfy(\.isFinite) else { return nil }
        let neutral = [whiteRGB.first, whiteRGB.second, whiteRGB.third].map { $0 * relativeLuminance }
        let channels = [candidate.first, candidate.second, candidate.third]
        var alpha = 1.0
        for index in 0..<3 {
            if channels[index] < 0 {
                alpha = min(alpha, neutral[index] / (neutral[index] - channels[index]))
            } else if channels[index] > 1 {
                alpha = min(alpha, (1 - neutral[index]) / (channels[index] - neutral[index]))
            }
        }
        let rgb = (0..<3).map { index in
            min(1, max(0, neutral[index] + alpha * (channels[index] - neutral[index])))
        }
        return Sample(linearRGB: .init(first: rgb[0], second: rgb[1], third: rgb[2]), chromaScale: alpha)
    }
}
