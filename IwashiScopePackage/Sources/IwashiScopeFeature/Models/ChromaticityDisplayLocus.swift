import Foundation

/// Display-only, smoothly sampled spectral boundary. The measured LMS/XYZ_F integration
/// continues to use the original tables and linear interpolation in CIE2006Chromaticity.
enum ChromaticityDisplayLocus {
    struct Sample: Sendable {
        let wavelength: Double
        let lms: Vector3
        let xyzF: Vector3
        let xyz1931: Vector3
        let point: PhysiologicalChromaticityPoint
    }

    private static let long = DisplaySpectralInterpolator(values: CIE2006LMSData.long, start: 390, interval: 5)
    private static let medium = DisplaySpectralInterpolator(values: CIE2006LMSData.medium, start: 390, interval: 5)
    private static let short = DisplaySpectralInterpolator(values: CIE2006LMSData.short, start: 390, interval: 5)
    private static let xBar = observer(ColorRenderingReferenceData.xBar)
    private static let yBar = observer(ColorRenderingReferenceData.yBar)
    private static let zBar = observer(ColorRenderingReferenceData.zBar)

    /// Includes every original 5 nm knot exactly. Only the added display samples are interpolated.
    static let samples: [Sample] = {
        var result: [Sample] = []
        for wavelength in stride(from: 390.0, through: 830.0, by: 1.0) {
            guard let lms = Self.lms(at: wavelength), let x = xBar?.value(at: wavelength),
                  let y = yBar?.value(at: wavelength), let z = zBar?.value(at: wavelength) else { return [] }
            let xyzF = CIE2006Chromaticity.xyzF(from: lms)
            guard let point = CIE2006Chromaticity.point(from: xyzF) else { return [] }
            result.append(Sample(wavelength: wavelength, lms: lms, xyzF: xyzF,
                                 xyz1931: Vector3(first: x, second: y, third: z), point: point))
        }
        return result
    }()

    static let points = samples.map(\.point)

    static func lms(at wavelength: Double) -> Vector3? {
        guard let l = long?.value(at: wavelength), let m = medium?.value(at: wavelength) else { return nil }
        // No invented S-cone tail beyond the official 615 nm table limit.
        return Vector3(first: l, second: m, third: short?.value(at: wavelength) ?? 0)
    }

    private static func observer(_ values: [Double]) -> DisplaySpectralInterpolator? {
        DisplaySpectralInterpolator(values: values, start: ColorRenderingReferenceData.startWavelength,
                                    interval: ColorRenderingReferenceData.interval)
    }
}

/// Shape-preserving cubic Hermite interpolation (PCHIP), for uniformly spaced display data.
/// Fritsch–Butland harmonic interior slopes and one-sided endpoint slopes; no extrapolation.
/// https://docs.scipy.org/doc/scipy/reference/generated/scipy.interpolate.PchipInterpolator.html
struct DisplaySpectralInterpolator: Sendable {
    private let values: [Double]
    private let slopes: [Double]
    private let start: Double
    private let interval: Double

    init?(values: [Double], start: Double, interval: Double) {
        guard values.count >= 2, values.allSatisfy(\.isFinite), start.isFinite,
              interval.isFinite, interval > 0 else { return nil }
        let differences = zip(values, values.dropFirst()).map { $1 - $0 }
        var slopes = [Double](repeating: 0, count: values.count)
        if differences.count == 1 {
            slopes = [differences[0], differences[0]]
        } else {
            slopes[0] = Self.endSlope(differences[0], differences[1])
            slopes[values.count - 1] = Self.endSlope(differences[differences.count - 1], differences[differences.count - 2])
            for index in 1..<(values.count - 1) {
                let before = differences[index - 1], after = differences[index]
                if Self.sameSign(before, after) {
                    // Grid spacing is constant, so the weighted harmonic mean simplifies.
                    slopes[index] = 2 / (1 / before + 1 / after)
                }
            }
        }
        self.values = values
        self.slopes = slopes
        self.start = start
        self.interval = interval
    }

    func value(at wavelength: Double) -> Double? {
        guard wavelength.isFinite else { return nil }
        let position = (wavelength - start) / interval
        guard position >= 0, position <= Double(values.count - 1) else { return nil }
        let index = Int(position.rounded(.down))
        let t = position - Double(index)
        if t == 0 { return values[index] }
        let t2 = t * t, t3 = t2 * t
        return (2 * t3 - 3 * t2 + 1) * values[index]
            + (t3 - 2 * t2 + t) * slopes[index]
            + (-2 * t3 + 3 * t2) * values[index + 1]
            + (t3 - t2) * slopes[index + 1]
    }

    private static func sameSign(_ a: Double, _ b: Double) -> Bool {
        (a > 0 && b > 0) || (a < 0 && b < 0)
    }

    private static func endSlope(_ adjacent: Double, _ next: Double) -> Double {
        let candidate = (3 * adjacent - next) / 2
        guard sameSign(candidate, adjacent) else { return 0 }
        if !sameSign(adjacent, next), abs(candidate) > 3 * abs(adjacent) { return 3 * adjacent }
        return candidate
    }
}
