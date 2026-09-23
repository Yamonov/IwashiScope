import CoreGraphics
import Foundation

/// Display mapping only. LMS, coordinates and candidate amplitudes never pass back through it.
struct ChromaticityDisplayColorTransform {
    static let intent = CGColorRenderingIntent.relativeColorimetric
    private let sourceSpace = CGColorSpace(name: CGColorSpace.extendedLinearSRGB)
    private let displayLinearSpace: CGColorSpace?
    let destinationSpace: CGColorSpace?

    init(destinationSpace: CGColorSpace?) {
        self.destinationSpace = destinationSpace
        displayLinearSpace = destinationSpace.flatMap { CGColorSpaceCreateExtendedLinearized($0) }
    }

    func color(for linearRGB: Vector3) -> CGColor? {
        guard [linearRGB.first, linearRGB.second, linearRGB.third].allSatisfy(\.isFinite),
              let sourceSpace,
              let source = CGColor(colorSpace: sourceSpace,
                                   components: [linearRGB.first, linearRGB.second, linearRGB.third, 1]) else { return nil }
        // Before attachment to a window, preserve the tagged source for native color management.
        guard let destinationSpace else { return source }
        // Work in the display's primaries, not a fixed sRGB gamut. The extended linear
        // space prevents ColorSync from clipping a bright blue to equal red/blue first.
        guard let displayLinearSpace,
              let displayColor = source.converted(to: displayLinearSpace, intent: Self.intent, options: nil),
              let components = displayColor.components, components.count == 4,
              components.allSatisfy(\.isFinite) else {
            // Non-matrix ICC profiles cannot be linearized by Core Graphics. Keep their
            // native colorimetric path; do not substitute a different display profile.
            return source.converted(to: destinationSpace, intent: Self.intent, options: nil)
        }
        let linear = Vector3(first: components[0], second: components[1], third: components[2])
        if [linear.first, linear.second, linear.third].allSatisfy({ $0 >= -1e-7 && $0 <= 1 + 1e-7 }) {
            // Already reproducible: preserve the original colorimetric conversion exactly.
            return source.converted(to: destinationSpace, intent: Self.intent, options: nil)
        }
        guard let fitted = Self.limitDisplayBrightness(linear),
              let color = CGColor(colorSpace: displayLinearSpace,
                                  components: [fitted.first, fitted.second, fitted.third, 1]) else { return nil }
        return color.converted(to: destinationSpace, intent: Self.intent, options: nil)
    }

    /// The boundary color is fixed first (unavailable negative primaries are zero).
    /// Its positive primary ratio never changes as the response-matched amplitude grows.
    /// Only the shared brightness gain stops at the display's maximum; no per-channel
    /// upper clipping, global gamut compression, or brightening of dim candidates.
    static func limitDisplayBrightness(_ linear: Vector3) -> Vector3? {
        guard [linear.first, linear.second, linear.third].allSatisfy(\.isFinite) else { return nil }
        let r = max(0, linear.first), g = max(0, linear.second), b = max(0, linear.third)
        let limit = max(1, r, g, b)
        return Vector3(first: r / limit, second: g / limit, third: b / limit)
    }
}
