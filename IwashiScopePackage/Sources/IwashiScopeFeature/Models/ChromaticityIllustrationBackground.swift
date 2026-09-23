import CoreGraphics
import Foundation

/// Decorative chart background only. A single white-anchored coordinate transform
/// avoids the radial seams of spectral-fan interpolation. This is NOT a colorimetric
/// CIE2015-to-CIE1931 observer conversion and never feeds measurement or P/D/T bands.
struct ChromaticityIllustrationBackground {
    static let relativeLuminance = 0.20
    let colorSpace: CGColorSpace
    let whitePoint: PhysiologicalChromaticityPoint
    let neutral: Vector3
    private let xColumn: Vector3
    private let yColumn: Vector3
    private let zColumn: Vector3
    private let determinant: Double

    init?(destinationSpace: CGColorSpace) {
        guard destinationSpace.model == .rgb,
              let linearSpace = CGColorSpaceCreateExtendedLinearized(destinationSpace),
              let sourceSpace = CGColorSpace(name: CGColorSpace.extendedLinearSRGB),
              let palette = ChromaticityBackgroundPalette.shared else { return nil }
        let white = palette.white.xyzF
        let sourceWhite = Vector3(first: white.first/white.second, second: 1, third: white.third/white.second)
        let d65 = Vector3(first: 0.3127/0.3290, second: 1, third: (1-0.3127-0.3290)/0.3290)

        func column(_ coordinate: Vector3) -> Vector3? {
            // White alignment in the illustration's coordinate model, not a claim
            // that XYZ_F values can be used as measured CIE1931 tristimulus values.
            guard let xyz = BradfordChromaticAdaptation.adapt(coordinate, sourceWhite: sourceWhite,
                                                               destinationWhite: d65) else { return nil }
            let rgb: [CGFloat] = [
                (12831.0/3959)*xyz.first - (329.0/214)*xyz.second - (1974.0/3959)*xyz.third,
                (-851781.0/878810)*xyz.first + (1648619.0/878810)*xyz.second + (36519.0/878810)*xyz.third,
                (705.0/12673)*xyz.first - (2585.0/12673)*xyz.second + (705.0/667)*xyz.third,
                1
            ]
            guard let source = CGColor(colorSpace: sourceSpace, components: rgb),
                  let converted = source.converted(to: linearSpace, intent: .relativeColorimetric, options: nil),
                  let c = converted.components, c.count == 4, c.allSatisfy(\.isFinite) else { return nil }
            return .init(first: c[0], second: c[1], third: c[2])
        }
        guard let x = column(.init(first: 1, second: 0, third: 0)),
              let y = column(.init(first: 0, second: 1, third: 0)),
              let z = column(.init(first: 0, second: 0, third: 1)) else { return nil }
        let determinant = Self.dot(x, Self.cross(y, z))
        guard determinant.isFinite, abs(determinant) > 1e-12 else { return nil }
        let n = Self.combine(x, y, z, sourceWhite, gain: Self.relativeLuminance)
        guard [n.first, n.second, n.third].allSatisfy({ $0.isFinite && $0 > 0 && $0 < 1 }) else { return nil }
        xColumn = x
        yColumn = y
        zColumn = z
        self.determinant = determinant
        neutral = n
        whitePoint = palette.white.point
        colorSpace = linearSpace
    }

    func color(at point: PhysiologicalChromaticityPoint) -> Vector3? {
        guard point.isFinite, CIE2006Chromaticity.xRange.contains(point.x),
              point.y > 0, CIE2006Chromaticity.yRange.contains(point.y) else { return nil }
        // Fill the complete rectangular texture. The view clips it to the unchanged
        // locus once, avoiding transparent cracks or double-antialiased boundaries.
        let xyz = Vector3(first: point.x/point.y, second: 1,
                          third: (1-point.x-point.y)/point.y)
        let candidate = Self.combine(xColumn, yColumn, zColumn, xyz, gain: Self.relativeLuminance)
        let delta = Vector3(first: candidate.first-neutral.first, second: candidate.second-neutral.second,
                            third: candidate.third-neutral.third)
        func distance(_ offset: Double, _ center: Double) -> Double {
            offset >= 0 ? offset/(1-center) : -offset/center
        }
        let r = distance(delta.first, neutral.first)
        let g = distance(delta.second, neutral.second)
        let b = distance(delta.third, neutral.third)
        guard r.isFinite, g.isFinite, b.isFinite else { return nil }
        // Smooth common chroma gain: alpha = (1 + r^8 + g^8 + b^8)^(-1/8).
        // It eases BEFORE a channel reaches the gamut boundary and stays smooth when
        // the limiting channel changes. Hard min-of-channel limits create slope kinks.
        // Both endpoints have the same illustration Y_F, so the common gain retains it.
        // Scaling first avoids overflow near y=0; it does not change the formula.
        let inverseScale = 1/max(1, r, g, b)
        let sum = Self.eighthPower(inverseScale) + Self.eighthPower(r*inverseScale)
            + Self.eighthPower(g*inverseScale) + Self.eighthPower(b*inverseScale)
        let alpha = inverseScale/sqrt(sqrt(sqrt(sum)))
        // Only floating-point boundary noise is bounded here.
        return .init(
            first: min(1, max(0, neutral.first + alpha*delta.first)),
            second: min(1, max(0, neutral.second + alpha*delta.second)),
            third: min(1, max(0, neutral.third + alpha*delta.third))
        )
    }

    /// Diagnostic inverse of this illustration model only; not a measurement API.
    func illustrativeLuminance(of rgb: Vector3) -> Double {
        Self.dot(xColumn, Self.cross(rgb, zColumn))/determinant
    }

    func image(width: Int, height: Int) -> CGImage? {
        guard (1...4096).contains(width), (1...4096).contains(height) else { return nil }
        var pixels = [Float](repeating: 0, count: width*height*4)
        for row in 0..<height {
            let y = (1-(Double(row)+0.5)/Double(height))*CIE2006Chromaticity.yRange.upperBound
            for column in 0..<width {
                let x = (Double(column)+0.5)/Double(width)*CIE2006Chromaticity.xRange.upperBound
                guard let rgb = color(at: .init(x: x, y: y)) else { continue }
                let index = (row*width+column)*4
                pixels[index] = Float(rgb.first)
                pixels[index+1] = Float(rgb.second)
                pixels[index+2] = Float(rgb.third)
                pixels[index+3] = 1
            }
        }
        let data = pixels.withUnsafeBytes { Data($0) }
        guard let provider = CGDataProvider(data: data as CFData) else { return nil }
        return CGImage(width: width, height: height, bitsPerComponent: 32, bitsPerPixel: 128,
                       bytesPerRow: width*16, space: colorSpace,
                       bitmapInfo: CGBitmapInfo(alpha: .premultipliedLast, component: .float, byteOrder: .order32Little),
                       provider: provider, decode: nil, shouldInterpolate: true, intent: .relativeColorimetric)
    }

    private static func combine(_ x: Vector3, _ y: Vector3, _ z: Vector3, _ v: Vector3, gain: Double) -> Vector3 {
        .init(first: gain*(x.first*v.first+y.first*v.second+z.first*v.third),
              second: gain*(x.second*v.first+y.second*v.second+z.second*v.third),
              third: gain*(x.third*v.first+y.third*v.second+z.third*v.third))
    }
    private static func eighthPower(_ value: Double) -> Double {
        let square = value*value
        let fourth = square*square
        return fourth*fourth
    }
    private static func dot(_ a: Vector3, _ b: Vector3) -> Double {
        a.first*b.first + a.second*b.second + a.third*b.third
    }
    private static func cross(_ a: Vector3, _ b: Vector3) -> Vector3 {
        .init(first: a.second*b.third-a.third*b.second, second: a.third*b.first-a.first*b.third,
              third: a.first*b.second-a.second*b.first)
    }
}
