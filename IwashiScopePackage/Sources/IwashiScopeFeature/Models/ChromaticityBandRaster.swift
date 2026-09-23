import CoreGraphics
import Foundation

/// A uniformly sampled 32-bit-float color strip and its equal-luminance neutral.
struct ChromaticityBandRaster {
    static let defaultSampleCount = 4096
    let image: CGImage
    let firstColor: CGColor
    let lastColor: CGColor
    let grayColor: CGColor

    init?(section: ChromaticityConfusionBand.Section, reference: ChromaticityBrightnessReference,
          destinationSpace: CGColorSpace?, sampleCount: Int = Self.defaultSampleCount) {
        guard (2...16384).contains(sampleCount), let destinationSpace,
              let mapper = ChromaticityBandDisplayMapper(reference: reference, destinationSpace: destinationSpace) else {
            // Do not silently substitute sRGB for an unavailable/unsupported display.
            return nil
        }
        let space = mapper.displayLinearSpace
        var pixels = [Float]()
        pixels.reserveCapacity(sampleCount * 4)
        var first: Vector3?
        var last: Vector3?
        for index in 0..<sampleCount {
            guard let stop = section.sample(at: Double(index) / Double(sampleCount - 1)),
                  let sample = mapper.sample(for: stop) else { return nil }
            let rgb = sample.linearRGB
            let values = [Float(rgb.first), Float(rgb.second), Float(rgb.third), Float(1)]
            guard values.allSatisfy(\.isFinite) else { return nil }
            pixels.append(contentsOf: values)
            if first == nil { first = rgb }
            last = rgb
        }
        let bytes = pixels.withUnsafeBytes { Data($0) }
        guard let first, let last,
              let startColor = CGColor(colorSpace: space, components: [first.first, first.second, first.third, 1]),
              let endColor = CGColor(colorSpace: space, components: [last.first, last.second, last.third, 1]),
              let provider = CGDataProvider(data: bytes as CFData),
              let image = CGImage(
                width: sampleCount, height: 1, bitsPerComponent: 32, bitsPerPixel: 128, bytesPerRow: sampleCount * 16,
                space: space, bitmapInfo: CGBitmapInfo(alpha: .premultipliedLast, component: .float, byteOrder: .order32Little),
                provider: provider, decode: nil, shouldInterpolate: true, intent: .relativeColorimetric
              ) else { return nil }
        self.image = image
        firstColor = startColor
        lastColor = endColor
        grayColor = mapper.grayColor
    }
}
