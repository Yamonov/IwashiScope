import CoreGraphics
import Foundation

struct RGBColorValue: Equatable, Sendable {
    let red: Double
    let green: Double
    let blue: Double
    let isOutOfGamut: Bool

    var red8Bit: Int { quantized(red) }
    var green8Bit: Int { quantized(green) }
    var blue8Bit: Int { quantized(blue) }

    var hex: String {
        String(format: "#%02X%02X%02X", red8Bit, green8Bit, blue8Bit)
    }

    private func quantized(_ component: Double) -> Int {
        Int((component.clamped(to: 0...1) * 255).rounded())
    }
}

struct LabColorConversion {
    /// The source Lab color is retained so Core Graphics can color-manage it for the active display.
    let managedColor: CGColor
    let sRGB: RGBColorValue
    let adobeRGB: RGBColorValue
    let displayP3: RGBColorValue
}

enum LabColorConverter {
    private static let gamutTolerance = 0.0005

    static func convert(lab: Vector3, whitePoint: String?) -> LabColorConversion? {
        let components = [lab.first, lab.second, lab.third]
        guard components.allSatisfy(\.isFinite) else { return nil }
        let labComponents = components.map { CGFloat($0) } + [CGFloat(1)]

        guard let labColorSpace = labColorSpace(for: whitePoint),
              let managedColor = CGColor(
                colorSpace: labColorSpace,
                components: labComponents
              ),
              let extendedSRGBSpace = CGColorSpace(name: CGColorSpace.extendedSRGB),
              let extendedSRGBColor = managedColor.converted(
                to: extendedSRGBSpace,
                intent: .relativeColorimetric,
                options: nil
              ),
              let extendedSRGB = rgbComponents(of: extendedSRGBColor),
              let extendedDisplayP3Space = CGColorSpace(name: CGColorSpace.extendedDisplayP3),
              let extendedDisplayP3Color = managedColor.converted(
                to: extendedDisplayP3Space,
                intent: .relativeColorimetric,
                options: nil
              ),
              let extendedDisplayP3 = rgbComponents(of: extendedDisplayP3Color),
              let adobeRGBSpace = CGColorSpace(name: CGColorSpace.adobeRGB1998),
              let adobeRGBColor = managedColor.converted(
                to: adobeRGBSpace,
                intent: .relativeColorimetric,
                options: nil
              ),
              let adobeRGB = rgbComponents(of: adobeRGBColor),
              let extendedLinearSRGBSpace = CGColorSpace(name: CGColorSpace.extendedLinearSRGB),
              let extendedLinearSRGBColor = managedColor.converted(
                to: extendedLinearSRGBSpace,
                intent: .relativeColorimetric,
                options: nil
              ),
              let extendedLinearSRGB = rgbComponents(of: extendedLinearSRGBColor) else {
            return nil
        }

        let linearAdobeRGB = convertLinearSRGBToLinearAdobeRGB(extendedLinearSRGB)
        return LabColorConversion(
            managedColor: managedColor,
            sRGB: RGBColorValue(
                red: extendedSRGB.red.clamped(to: 0...1),
                green: extendedSRGB.green.clamped(to: 0...1),
                blue: extendedSRGB.blue.clamped(to: 0...1),
                isOutOfGamut: isOutOfGamut(extendedSRGB)
            ),
            adobeRGB: RGBColorValue(
                red: adobeRGB.red.clamped(to: 0...1),
                green: adobeRGB.green.clamped(to: 0...1),
                blue: adobeRGB.blue.clamped(to: 0...1),
                isOutOfGamut: isOutOfGamut(linearAdobeRGB)
            ),
            displayP3: RGBColorValue(
                red: extendedDisplayP3.red.clamped(to: 0...1),
                green: extendedDisplayP3.green.clamped(to: 0...1),
                blue: extendedDisplayP3.blue.clamped(to: 0...1),
                isOutOfGamut: isOutOfGamut(extendedDisplayP3)
            )
        )
    }

    private static func labColorSpace(for whitePoint: String?) -> CGColorSpace? {
        if whitePoint?.localizedCaseInsensitiveContains("D65") == true {
            return CGColorSpace(
                labWhitePoint: [0.95047, 1, 1.08883],
                blackPoint: [0, 0, 0],
                range: [-128, 127, -128, 127]
            )
        }

        // spotread reports D50 Lab. Generic Lab is ColorSync's managed D50 Lab space.
        return CGColorSpace(name: CGColorSpace.genericLab)
    }

    private static func rgbComponents(of color: CGColor) -> RGBComponents? {
        guard let components = color.components, components.count >= 3 else { return nil }
        let values = components.prefix(3).map(Double.init)
        guard values.allSatisfy(\.isFinite) else { return nil }
        return RGBComponents(red: values[0], green: values[1], blue: values[2])
    }

    private static func isOutOfGamut(_ components: RGBComponents) -> Bool {
        [components.red, components.green, components.blue].contains {
            $0 < -gamutTolerance || $0 > 1 + gamutTolerance
        }
    }

    private static func convertLinearSRGBToLinearAdobeRGB(
        _ srgb: RGBComponents
    ) -> RGBComponents {
        // Both RGB spaces use D65. Convert through CIE XYZ using their published matrices;
        // retaining extended components makes Adobe RGB gamut testing possible before clipping.
        let x = 0.4124564 * srgb.red + 0.3575761 * srgb.green + 0.1804375 * srgb.blue
        let y = 0.2126729 * srgb.red + 0.7151522 * srgb.green + 0.0721750 * srgb.blue
        let z = 0.0193339 * srgb.red + 0.1191920 * srgb.green + 0.9503041 * srgb.blue

        return RGBComponents(
            red: 2.0413690 * x - 0.5649464 * y - 0.3446944 * z,
            green: -0.9692660 * x + 1.8760108 * y + 0.0415560 * z,
            blue: 0.0134474 * x - 0.1183897 * y + 1.0154096 * z
        )
    }
}

private struct RGBComponents {
    let red: Double
    let green: Double
    let blue: Double
}

private extension Double {
    func clamped(to range: ClosedRange<Double>) -> Double {
        min(max(self, range.lowerBound), range.upperBound)
    }
}
