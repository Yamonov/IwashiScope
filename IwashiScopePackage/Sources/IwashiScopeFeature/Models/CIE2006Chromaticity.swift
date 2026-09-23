import Foundation

struct PhysiologicalChromaticityPoint: Equatable, Sendable {
    let x: Double
    let y: Double
    var isFinite: Bool { x.isFinite && y.isFinite }
}

enum PhysiologicalCone: String, CaseIterable, Identifiable, Sendable {
    case long = "L"
    case medium = "M"
    case short = "S"
    var id: Self { self }

    var label: String {
        switch self {
        case .long: "L (P)"
        case .medium: "M (D)"
        case .short: "S (T)"
        }
    }

    var sensitivities: [Double] {
        switch self {
        case .long: CIE2006LMSData.long
        case .medium: CIE2006LMSData.medium
        case .short: CIE2006LMSData.short
        }
    }

    var unitResponse: Vector3 {
        switch self {
        case .long: Vector3(first: 1, second: 0, third: 0)
        case .medium: Vector3(first: 0, second: 1, third: 0)
        case .short: Vector3(first: 0, second: 0, third: 1)
        }
    }
}

enum CIE2006ChromaticityError: Error, Equatable, LocalizedError {
    case invalidSpectrum
    case insufficientSpectrum
    case zeroSignal

    var errorDescription: String? {
        switch self {
        case .invalidSpectrum:
            String(localized: "色度を計算できないスペクトルです。")
        case .insufficientSpectrum:
            String(localized: "色度の計算に必要な反射スペクトルがありません。")
        case .zeroSignal:
            String(localized: "反射光がゼロのため、色度を定義できません。")
        }
    }
}

/// CIE 2006 2° energy fundamentals → CIE 2015 XYZ_F, not CIE 1931 XYZ.
/// Matrix: CIE 170-2:2015; Stockman (2019), "Cone fundamentals and CIE standards", Eq. 4.
/// Uses the original tabulated channel scales, never CIE2006LMSReference's plot normalization.
enum CIE2006Chromaticity {
    struct LocusSample: Equatable, Sendable {
        let wavelength: Double
        let point: PhysiologicalChromaticityPoint
    }

    struct LineSegment: Equatable, Sendable {
        let start: PhysiologicalChromaticityPoint
        let end: PhysiologicalChromaticityPoint
    }

    struct MeasurementResult: Equatable, Sendable {
        let lms: Vector3
        /// Perfect D50 reflector over the SAME measured wavelength range and Y_F=100 scale.
        let whiteLMS: Vector3
        let xyzF: Vector3
        let point: PhysiologicalChromaticityPoint
        let wavelengthRange: WavelengthRange
    }

    static let xRange = 0.0...0.8
    static let yRange = 0.0...0.9

    static func xyzF(from lms: Vector3) -> Vector3 {
        Vector3(
            first: 1.94735469 * lms.first - 1.41445123 * lms.second + 0.36476327 * lms.third,
            second: 0.68990272 * lms.first + 0.34832189 * lms.second,
            third: 1.93485343 * lms.third
        )
    }

    static func point(from xyzF: Vector3) -> PhysiologicalChromaticityPoint? {
        let sum = xyzF.first + xyzF.second + xyzF.third
        // A negative sum is valid for a virtual copunctal point (not a physical stimulus).
        guard [xyzF.first, xyzF.second, xyzF.third, sum].allSatisfy(\.isFinite), sum != 0 else {
            return nil
        }
        let point = PhysiologicalChromaticityPoint(x: xyzF.first / sum, y: xyzF.second / sum)
        return point.isFinite ? point : nil
    }

    static func spectralLMS(at wavelength: Double) -> Vector3? {
        guard wavelength.isFinite, (390.0...830.0).contains(wavelength) else { return nil }
        return Vector3(
            first: sensitivity(CIE2006LMSData.long, at: wavelength),
            second: sensitivity(CIE2006LMSData.medium, at: wavelength),
            third: sensitivity(CIE2006LMSData.short, at: wavelength)
        )
    }

    static let spectralLocus: [LocusSample] = stride(from: 390.0, through: 830.0, by: 5.0).compactMap {
        wavelength in
        guard let lms = spectralLMS(at: wavelength), let point = point(from: xyzF(from: lms)) else {
            return nil
        }
        return LocusSample(wavelength: wavelength, point: point)
    }

    static func copunctalPoint(for cone: PhysiologicalCone) -> PhysiologicalChromaticityPoint {
        let xyz = xyzF(from: cone.unitResponse)
        let sum = xyz.first + xyz.second + xyz.third
        return PhysiologicalChromaticityPoint(x: xyz.first / sum, y: xyz.second / sum)
    }

    /// Infinite confusion line clipped to the chart's rectangle. The view additionally
    /// clips it to the spectral locus, so virtual copunctal points need not be on screen.
    static func confusionLine(
        through point: PhysiologicalChromaticityPoint, cone: PhysiologicalCone
    ) -> LineSegment? {
        guard point.isFinite else { return nil }
        let pole = copunctalPoint(for: cone)
        let dx = pole.x - point.x
        let dy = pole.y - point.y
        guard hypot(dx, dy) > 1e-12 else { return nil }
        var lower = -Double.infinity
        var upper = Double.infinity
        for (origin, direction, range) in [(point.x, dx, xRange), (point.y, dy, yRange)] {
            if abs(direction) < 1e-12 {
                guard range.contains(origin) else { return nil }
            } else {
                let t1 = (range.lowerBound - origin) / direction
                let t2 = (range.upperBound - origin) / direction
                lower = max(lower, min(t1, t2))
                upper = min(upper, max(t1, t2))
            }
        }
        guard lower.isFinite, upper.isFinite, lower < upper else { return nil }
        return LineSegment(
            start: .init(x: point.x + lower * dx, y: point.y + lower * dy),
            end: .init(x: point.x + upper * dx, y: point.y + upper * dy)
        )
    }

    /// D50 baseline only. No selected illuminant, adaptation, RGB or instrument XYZ enters this path.
    static func measuredResult(for measurement: SpotMeasurement) throws -> MeasurementResult {
        guard measurement.mode == .reflectance, measurement.spectrum.count >= 2 else {
            throw CIE2006ChromaticityError.insufficientSpectrum
        }
        guard measurement.spectrum.allSatisfy({ $0.wavelength.isFinite && $0.value.isFinite }) else {
            throw CIE2006ChromaticityError.invalidSpectrum
        }
        // Preserve the measurement; floor negative noise only in this integration's working copy.
        let reflectance = measurement.spectrum.sorted { $0.wavelength < $1.wavelength }.map {
            SpectralSample(id: $0.id, wavelength: $0.wavelength, value: max(0, $0.value) / 100)
        }
        guard zip(reflectance, reflectance.dropFirst()).allSatisfy({ $0.wavelength < $1.wavelength }) else {
            throw CIE2006ChromaticityError.invalidSpectrum
        }
        let illuminant = CIEReferenceIlluminant.d50.samples
        let lower = max(390, reflectance[0].wavelength, illuminant[0].wavelength)
        let upper = min(830, reflectance[reflectance.count - 1].wavelength, illuminant[illuminant.count - 1].wavelength)
        guard lower < upper else { throw CIE2006ChromaticityError.insufficientSpectrum }

        let integrals = PhysiologicalCone.allCases.map {
            integrate(cone: $0, reflectance: reflectance, illuminant: illuminant, range: lower...upper)
        }
        let white = Vector3(first: integrals[0].white, second: integrals[1].white, third: integrals[2].white)
        let whiteY = xyzF(from: white).second
        guard whiteY.isFinite, whiteY > 0 else { throw CIE2006ChromaticityError.invalidSpectrum }
        let normalization = 100 / whiteY
        let lms = Vector3(
            first: integrals[0].object * normalization,
            second: integrals[1].object * normalization,
            third: integrals[2].object * normalization
        )
        let xyz = xyzF(from: lms)
        guard [xyz.first, xyz.second, xyz.third].allSatisfy(\.isFinite) else {
            throw CIE2006ChromaticityError.invalidSpectrum
        }
        guard xyz.first != 0 || xyz.second != 0 || xyz.third != 0 else {
            throw CIE2006ChromaticityError.zeroSignal
        }
        guard let point = point(from: xyz) else { throw CIE2006ChromaticityError.invalidSpectrum }
        let whiteLMS = Vector3(first: white.first * normalization, second: white.second * normalization,
                               third: white.third * normalization)
        return MeasurementResult(lms: lms, whiteLMS: whiteLMS, xyzF: xyz, point: point,
                                 wavelengthRange: .init(start: lower, end: upper))
    }

    private static func integrate(
        cone: PhysiologicalCone, reflectance: [SpectralSample], illuminant: [SpectralSample],
        range: ClosedRange<Double>
    ) -> (object: Double, white: Double) {
        let values = cone.sensitivities
        let upper = min(range.upperBound, CIE2006LMSData.firstWavelength + Double(values.count - 1) * CIE2006LMSData.interval)
        let lower = range.lowerBound
        guard lower < upper else { return (0, 0) }
        // CIE metadata specifies linear interpolation and zero extrapolation of each
        // fundamental. Integrating S only through 615 nm avoids inventing its missing tail.
        let nodes = Set(
            [lower, upper] + reflectance.map(\.wavelength) + illuminant.map(\.wavelength)
                + values.indices.map { CIE2006LMSData.firstWavelength + Double($0) * CIE2006LMSData.interval }
        ).filter { $0 >= lower && $0 <= upper }.sorted()
        var object = 0.0
        var white = 0.0
        for (a, b) in zip(nodes, nodes.dropFirst()) {
            let midpoint = (a + b) / 2
            // Between knots all three factors are linear. Simpson's rule integrates
            // their cubic product without altering the original tabulated channel gains.
            for (wavelength, weight) in [(a, 1.0), (midpoint, 4.0), (b, 1.0)] {
                let weightedSource = interpolated(illuminant, at: wavelength)
                    * sensitivity(values, at: wavelength) * (b - a) * weight / 6
                white += weightedSource
                object += weightedSource * interpolated(reflectance, at: wavelength)
            }
        }
        return (object, white)
    }

    private static func sensitivity(_ values: [Double], at wavelength: Double) -> Double {
        let index = (wavelength - CIE2006LMSData.firstWavelength) / CIE2006LMSData.interval
        guard index >= 0, index <= Double(values.count - 1) else { return 0 }
        let lower = Int(index.rounded(.down))
        let upper = min(lower + 1, values.count - 1)
        return values[lower] + (values[upper] - values[lower]) * (index - Double(lower))
    }

    /// Callers have validated order and clipped to the shared measured/source range.
    private static func interpolated(_ samples: [SpectralSample], at wavelength: Double) -> Double {
        var lower = 0
        var upper = samples.count - 1
        while lower + 1 < upper {
            let middle = (lower + upper) / 2
            if samples[middle].wavelength <= wavelength { lower = middle } else { upper = middle }
        }
        let fraction = (wavelength - samples[lower].wavelength) / (samples[upper].wavelength - samples[lower].wavelength)
        return samples[lower].value + (samples[upper].value - samples[lower].value) * fraction
    }
}
