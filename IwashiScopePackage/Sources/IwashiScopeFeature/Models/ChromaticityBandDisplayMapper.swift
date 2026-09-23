import CoreGraphics
import Foundation

/// Display-only equal-reference-luminance mapping. Raw LMS and plot geometry never
/// receive these changes. This replaces the former 0.5...2x Y_F display limiter.
struct ChromaticityBandDisplayMapper {
    struct DisplaySample {
        let linearRGB: Vector3
        let modelLMS: Vector3
        let chromaScale: Double
    }

    let reference: ChromaticityBrightnessReference
    let gamut: ChromaticityDisplayGamut
    let displayLinearSpace: CGColorSpace
    let grayColor: CGColor
    let grayLinearRGB: Vector3
    private let sourceSpace: CGColorSpace
    private let destinationSpace: CGColorSpace
    private let paletteWhite: Vector3

    init?(reference: ChromaticityBrightnessReference, destinationSpace: CGColorSpace) {
        guard let source = CGColorSpace(name: CGColorSpace.extendedLinearSRGB),
              let linear = CGColorSpaceCreateExtendedLinearized(destinationSpace),
              let gamut = ChromaticityDisplayGamut(linearColorSpace: linear),
              let white = ChromaticityBrightnessReference.paletteWhiteLMS else { return nil }
        let gray = Self.scale(gamut.whiteRGB, reference.relativeLuminance)
        guard let grayColor = CGColor(colorSpace: linear, components: [gray.first, gray.second, gray.third, 1]) else {
            return nil
        }
        self.reference = reference
        self.gamut = gamut
        displayLinearSpace = linear
        grayLinearRGB = gray
        self.grayColor = grayColor
        paletteWhite = white
        sourceSpace = source
        self.destinationSpace = destinationSpace
    }

    func sample(for stop: ChromaticityConfusionBand.Stop) -> DisplaySample? {
        guard stop.point.isFinite,
              [stop.linearRGB.first, stop.linearRGB.second, stop.linearRGB.third].allSatisfy(\.isFinite),
              let response = ChromaticityBrightnessReference.response(
                stop.lms, relativeTo: paletteWhite, mode: reference.mode
              ) else { return nil }
        if reference.relativeLuminance == 0 {
            return DisplaySample(linearRGB: grayLinearRGB, modelLMS: .init(first: 0, second: 0, third: 0),
                                 chromaScale: 0)
        }
        guard response > 0 else { return nil }
        // Use the measurement's own white for the target and the palette's own white
        // for display encoding. A neutral reflector must stay neutral in all types.
        let gain = reference.relativeLuminance / response
        let candidate = Self.scale(stop.linearRGB, gain)
        guard let source = CGColor(colorSpace: sourceSpace,
                                   components: [candidate.first, candidate.second, candidate.third, 1]),
              let converted = source.converted(to: displayLinearSpace, intent: .relativeColorimetric, options: nil),
              let c = converted.components, c.count == 4, c.allSatisfy(\.isFinite),
              let mapped = gamut.sample(candidate: .init(first: c[0], second: c[1], third: c[2]),
                                        relativeLuminance: reference.relativeLuminance) else { return nil }
        let colorLMS = Self.scale(stop.lms, gain * mapped.chromaScale)
        let grayLMS = Self.scale(paletteWhite, reference.relativeLuminance * (1 - mapped.chromaScale))
        let modelLMS = Vector3(first: colorLMS.first + grayLMS.first,
                               second: colorLMS.second + grayLMS.second,
                               third: colorLMS.third + grayLMS.third)
        return DisplaySample(linearRGB: mapped.linearRGB, modelLMS: modelLMS, chromaScale: mapped.chromaScale)
    }

    func color(for stop: ChromaticityConfusionBand.Stop) -> CGColor? {
        guard let sample = sample(for: stop) else { return nil }
        let rgb = sample.linearRGB
        return CGColor(colorSpace: displayLinearSpace, components: [rgb.first, rgb.second, rgb.third, 1])?
            .converted(to: destinationSpace, intent: .relativeColorimetric, options: nil)
    }

    private static func scale(_ value: Vector3, _ gain: Double) -> Vector3 {
        .init(first: value.first * gain, second: value.second * gain, third: value.third * gain)
    }
}
