import Foundation

enum ColorAppearanceError: Error, Equatable, LocalizedError, Sendable {
    case invalidViewingConditions
    case invalidStimulus
    case outsideModelDomain
    case insufficientSpectrum

    var errorDescription: String? {
        switch self {
        case .invalidViewingConditions: String(localized: "色の見え方の観察条件を計算できません。")
        case .invalidStimulus: String(localized: "色の見え方を計算する測色値が不正です。")
        case .outsideModelDomain: String(localized: "この色は色の見え方モデルの計算範囲外です。")
        case .insufficientSpectrum: String(localized: "比較に必要な波長範囲のスペクトルが不足しています。")
        }
    }
}

/// White XYZ and Yb use the Yw = 100 scale. LA is an absolute luminance, not XYZ Y.
struct AppearanceViewingConditions: Equatable, Sendable {
    let whiteXYZ: Vector3
    var adaptingLuminance: Double = 20
    var backgroundLuminanceFactor: Double = 20
    var surroundF: Double = 1
    var surroundC: Double = 0.69
    var surroundNc: Double = 1
    var adaptationDegreeOverride: Double?
}

struct AppearanceCorrelates: Equatable, Sendable {
    let lightness: Double
    let brightness: Double
    let chroma: Double
    let colorfulness: Double
    let saturation: Double
    /// Undefined for achromatic colours, including black.
    let hue: Double?

    static let black = Self(
        lightness: 0, brightness: 0, chroma: 0,
        colorfulness: 0, saturation: 0, hue: nil
    )
}

/// CIE 248:2022, as specified in Gao et al., doi:10.1002/col.22959, Appendix A.
/// Includes the CIECAM16 linear extensions of the response function (not CAM16 2017).
struct CIECAM16: Sendable {
    let conditions: AppearanceViewingConditions
    let adaptationDegree: Double

    private let gains: Vector3
    private let luminanceFactor: Double
    private let luminanceRoot: Double
    private let nbb: Double
    private let z: Double
    private let whiteResponse: Double
    private let chromaFactor: Double
    private let upperResponseLimit: Double

    init(conditions: AppearanceViewingConditions) throws {
        let white = conditions.whiteXYZ
        guard [white.first, white.second, white.third,
               conditions.adaptingLuminance, conditions.backgroundLuminanceFactor,
               conditions.surroundF, conditions.surroundC, conditions.surroundNc]
            .allSatisfy(\.isFinite),
              white.first > 0, abs(white.second - 100) < 1e-9, white.third > 0,
              conditions.adaptingLuminance > 0,
              conditions.backgroundLuminanceFactor > 0,
              conditions.surroundF > 0, conditions.surroundC > 0,
              conditions.surroundNc > 0 else {
            throw ColorAppearanceError.invalidViewingConditions
        }
        let degree = conditions.adaptationDegreeOverride ?? min(1, max(0,
            conditions.surroundF * (1 - exp((-conditions.adaptingLuminance - 42) / 92) / 3.6)
        ))
        guard degree.isFinite, (0...1).contains(degree) else {
            throw ColorAppearanceError.invalidViewingConditions
        }
        let coneWhite = Self.coneResponse(white)
        guard coneWhite.first > 0, coneWhite.second > 0, coneWhite.third > 0 else {
            throw ColorAppearanceError.invalidViewingConditions
        }
        let gains = Self.map(coneWhite) { degree * 100 / $0 + 1 - degree }
        let adaptedWhite = Self.product(coneWhite, gains)
        let la = conditions.adaptingLuminance
        let k4 = pow(1 / (5 * la + 1), 4)
        let fl = 0.2 * k4 * 5 * la + 0.1 * pow(1 - k4, 2) * cbrt(5 * la)
        let n = conditions.backgroundLuminanceFactor / 100
        let nbb = 0.725 * pow(n, -0.2)
        // The adopted white uses the original compression, per Appendix A step 0.
        let compressedWhite = Self.map(adaptedWhite) { Self.response($0, fl: fl) + 0.1 }
        let aw = Self.achromaticResponse(compressedWhite, nbb: nbb)
        guard fl.isFinite, fl > 0, aw.isFinite, aw > 0 else {
            throw ColorAppearanceError.invalidViewingConditions
        }
        self.conditions = conditions
        adaptationDegree = degree
        self.gains = gains
        luminanceFactor = fl
        luminanceRoot = pow(fl, 0.25)
        self.nbb = nbb
        z = 1.48 + sqrt(n)
        whiteResponse = aw
        chromaFactor = pow(1.64 - pow(0.29, n), 0.73)
        upperResponseLimit = max(150, adaptedWhite.first, adaptedWhite.second, adaptedWhite.third)
    }

    func forward(_ xyz: Vector3) throws -> AppearanceCorrelates {
        guard Self.isFinite(xyz), xyz.second >= 0 else {
            throw ColorAppearanceError.invalidStimulus
        }
        if xyz.first == 0, xyz.second == 0, xyz.third == 0 { return .black }
        let adapted = Self.product(Self.coneResponse(xyz), gains)
        let compressed = Self.map(adapted) { compressedResponse($0) }
        let a = compressed.first - 12 * compressed.second / 11 + compressed.third / 11
        let b = (compressed.first + compressed.second - 2 * compressed.third) / 9
        let opponentMagnitude = hypot(a, b)
        let angle = atan2(b, a)
        let hue = (angle * 180 / .pi + 360).truncatingRemainder(dividingBy: 360)
        let achromatic = Self.achromaticResponse(compressed, nbb: nbb)
        let denominator = compressed.first + compressed.second + 21 * compressed.third / 20
        guard achromatic > 0, denominator > 0 else {
            throw ColorAppearanceError.outsideModelDomain
        }
        let j = 100 * pow(achromatic / whiteResponse, conditions.surroundC * z)
        let q = 4 / conditions.surroundC * sqrt(j / 100) * (whiteResponse + 4) * luminanceRoot
        let eccentricity = (cos(angle + 2) + 3.8) / 4
        let t = 50_000 / 13.0 * conditions.surroundNc * nbb
            * eccentricity * opponentMagnitude / denominator
        let c = pow(t, 0.9) * sqrt(j / 100) * chromaFactor
        let m = c * luminanceRoot
        let s = 100 * sqrt(m / q)
        guard [j, q, c, m, s, hue].allSatisfy(\.isFinite) else {
            throw ColorAppearanceError.outsideModelDomain
        }
        return AppearanceCorrelates(
            lightness: j, brightness: q, chroma: c,
            colorfulness: m, saturation: s, hue: c > 1e-10 ? hue : nil
        )
    }

    /// Reproduce Q, M and h together under these viewing conditions.
    func inverse(_ appearance: AppearanceCorrelates) throws -> Vector3 {
        try inverse(
            brightness: appearance.brightness,
            colorfulness: appearance.colorfulness,
            hue: appearance.hue
        )
    }

    func inverse(brightness q: Double, colorfulness m: Double, hue: Double?) throws -> Vector3 {
        guard q.isFinite, m.isFinite, q >= 0, m >= 0,
              hue?.isFinite != false else {
            throw ColorAppearanceError.invalidStimulus
        }
        if q == 0 {
            guard m == 0 else { throw ColorAppearanceError.outsideModelDomain }
            return Vector3(first: 0, second: 0, third: 0)
        }
        guard m <= 1e-10 || hue != nil else { throw ColorAppearanceError.invalidStimulus }
        let rootJ = q * conditions.surroundC / (4 * (whiteResponse + 4) * luminanceRoot)
        let c = m / luminanceRoot
        let t = m <= 1e-10 ? 0 : pow(c / (rootJ * chromaFactor), 1 / 0.9)
        let angle = (hue ?? 0) * .pi / 180
        let eccentricity = (cos(angle + 2) + 3.8) / 4
        let achromatic = whiteResponse * pow(rootJ * rootJ, 1 / (conditions.surroundC * z))
        let p2 = achromatic / nbb + 0.305
        let p1 = 50_000 / 13.0 * conditions.surroundNc * nbb * eccentricity
        // Algebraic solution of the opponent/achromatic equations; no hue-axis divisions.
        let denominator = 23 * p1 + t * (11 * cos(angle) + 108 * sin(angle))
        guard denominator > 0, denominator.isFinite else {
            throw ColorAppearanceError.outsideModelDomain
        }
        let radius = 23 * p2 * t / denominator
        let a = radius * cos(angle)
        let b = radius * sin(angle)
        let compressed = Vector3(
            first: (460 * p2 + 451 * a + 288 * b) / 1403,
            second: (460 * p2 - 891 * a - 261 * b) / 1403,
            third: (460 * p2 - 220 * a - 6300 * b) / 1403
        )
        let adapted = Self.map(compressed) { inverseCompressedResponse($0) }
        let cones = Vector3(
            first: adapted.first / gains.first,
            second: adapted.second / gains.second,
            third: adapted.third / gains.third
        )
        let xyz = Self.xyz(fromCones: cones)
        guard Self.isFinite(xyz) else { throw ColorAppearanceError.outsideModelDomain }
        return xyz
    }

    private func compressedResponse(_ value: Double) -> Double {
        let lower = 0.26
        if value <= lower {
            return Self.response(lower, fl: luminanceFactor) * value / lower + 0.1
        }
        if value >= upperResponseLimit {
            return Self.response(upperResponseLimit, fl: luminanceFactor)
                + responseDerivative(at: upperResponseLimit) * (value - upperResponseLimit) + 0.1
        }
        return Self.response(value, fl: luminanceFactor) + 0.1
    }

    private func inverseCompressedResponse(_ compressed: Double) -> Double {
        let value = compressed - 0.1
        let lower = Self.response(0.26, fl: luminanceFactor)
        let upper = Self.response(upperResponseLimit, fl: luminanceFactor)
        if value <= lower { return value * 0.26 / lower }
        if value >= upper {
            return upperResponseLimit + (value - upper) / responseDerivative(at: upperResponseLimit)
        }
        return 100 / luminanceFactor * pow(27.13 * value / (400 - value), 1 / 0.42)
    }

    private func responseDerivative(at value: Double) -> Double {
        let scaled = luminanceFactor * value / 100
        return 400 * 0.42 * 27.13 * luminanceFactor / 100 * pow(scaled, -0.58)
            / pow(27.13 + pow(scaled, 0.42), 2)
    }

    private static func response(_ value: Double, fl: Double) -> Double {
        let powered = pow(fl * abs(value) / 100, 0.42)
        return (value < 0 ? -1 : 1) * 400 * powered / (27.13 + powered)
    }

    private static func achromaticResponse(_ v: Vector3, nbb: Double) -> Double {
        (2 * v.first + v.second + v.third / 20 - 0.305) * nbb
    }

    static func coneResponse(_ v: Vector3) -> Vector3 {
        Vector3(
            first: 0.401288 * v.first + 0.650173 * v.second - 0.051461 * v.third,
            second: -0.250268 * v.first + 1.204414 * v.second + 0.045854 * v.third,
            third: -0.002079 * v.first + 0.048952 * v.second + 0.953127 * v.third
        )
    }

    private static func xyz(fromCones v: Vector3) -> Vector3 {
        Vector3(
            first: 1.8620678550872327 * v.first - 1.0112546305316843 * v.second + 0.14918677544445175 * v.third,
            second: 0.3875265432361372 * v.first + 0.6214474419314753 * v.second - 0.008973985167612518 * v.third,
            third: -0.01584149884933386 * v.first - 0.03412293802851557 * v.second + 1.0499644368778496 * v.third
        )
    }

    private static func product(_ a: Vector3, _ b: Vector3) -> Vector3 {
        Vector3(first: a.first * b.first, second: a.second * b.second, third: a.third * b.third)
    }

    private static func map(_ v: Vector3, _ operation: (Double) -> Double) -> Vector3 {
        Vector3(first: operation(v.first), second: operation(v.second), third: operation(v.third))
    }

    private static func isFinite(_ v: Vector3) -> Bool {
        v.first.isFinite && v.second.isFinite && v.third.isFinite
    }
}
