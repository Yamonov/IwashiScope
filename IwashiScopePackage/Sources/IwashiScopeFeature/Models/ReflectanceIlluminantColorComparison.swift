import Foundation

enum ReflectanceAppearanceMethod: String, CaseIterable, Identifiable, Sendable {
    case unadapted
    case bradford
    case ciecam16

    var id: String { rawValue }

    var title: String {
        switch self {
        case .unadapted: String(localized: "順応なし")
        case .bradford: String(localized: "従来方式（Bradford）")
        case .ciecam16: String(localized: "CIECAM16（標準）")
        }
    }
}

struct ReflectanceIlluminantColorComparisonResult: Equatable, Sendable {
    let source: IlluminantSpectrumDefinition
    let method: ReflectanceAppearanceMethod
    /// Original measurement, retained for diagnostics. Never overwritten with the prediction.
    let measuredLab: Vector3?
    let referenceLab: Vector3
    let referenceXYZ: Vector3
    let referenceWhiteXYZ: Vector3
    let sourceXYZ: Vector3
    let sourceWhiteXYZ: Vector3
    let simulatedXYZ: Vector3
    let simulatedLab: Vector3
    let referenceAppearance: AppearanceCorrelates?
    let sourceAppearance: AppearanceCorrelates?
    let referenceAdaptationDegree: Double?
    let sourceAdaptationDegree: Double?
    let wavelengthRange: ClosedRange<Double>

    var deltaL: Double { simulatedLab.first - referenceLab.first }
    var deltaA: Double { simulatedLab.second - referenceLab.second }
    var deltaB: Double { simulatedLab.third - referenceLab.third }
    var deltaE76: Double { CIEColorDifference.deltaE76(referenceLab, simulatedLab) }
    var deltaE2000: Double { CIEColorDifference.deltaE2000(referenceLab, simulatedLab) }

    var illuminant: CIEReferenceIlluminant? {
        guard case let .cie(illuminant) = source.origin else { return nil }
        return illuminant
    }
}

enum ReflectanceIlluminantColorComparisonCalculator {
    /// ICC PCS reference, also used by existing callers of the Lab utilities.
    static let d50White = Vector3(first: 96.42, second: 100, third: 82.49)

    /// The baseline remains available before a comparison illuminant is selected.
    static func referenceLab(for measurement: SpotMeasurement) throws -> Vector3 {
        guard measurement.mode == .reflectance else {
            throw ColorAppearanceError.invalidStimulus
        }
        guard let reference = SpectralTristimulusIntegrator.integrate(
            reflectance: measurement.spectrum,
            illuminant: CIEReferenceIlluminant.d50.samples
        ) else {
            throw ColorAppearanceError.insufficientSpectrum
        }
        guard let lab = CIELabColorimetry.lab(from: reference.objectXYZ, white: reference.whiteXYZ) else {
            throw ColorAppearanceError.outsideModelDomain
        }
        return lab
    }

    static func compare(
        measurement: SpotMeasurement,
        source: IlluminantSpectrumDefinition,
        method: ReflectanceAppearanceMethod
    ) throws -> ReflectanceIlluminantColorComparisonResult {
        guard measurement.mode == .reflectance else {
            throw ColorAppearanceError.invalidStimulus
        }
        guard let selected = SpectralTristimulusIntegrator.integrate(
            reflectance: measurement.spectrum, illuminant: source.samples
        ), let reference = SpectralTristimulusIntegrator.integrate(
            reflectance: measurement.spectrum,
            illuminant: CIEReferenceIlluminant.d50.samples,
            wavelengths: selected.wavelengths
        ), reference.wavelengths == selected.wavelengths,
        let first = selected.wavelengths.first, let last = selected.wavelengths.last else {
            throw ColorAppearanceError.insufficientSpectrum
        }

        let simulatedXYZ: Vector3
        var sourceAppearance: AppearanceCorrelates?
        var referenceAppearance: AppearanceCorrelates?
        var sourceDegree: Double?
        var referenceDegree: Double?
        switch method {
        case .unadapted:
            simulatedXYZ = selected.objectXYZ
        case .bradford:
            guard let adapted = BradfordChromaticAdaptation.adapt(
                selected.objectXYZ,
                sourceWhite: selected.whiteXYZ,
                destinationWhite: reference.whiteXYZ
            ) else {
                throw ColorAppearanceError.outsideModelDomain
            }
            simulatedXYZ = adapted
        case .ciecam16:
            let sourceModel = try CIECAM16(conditions: .init(whiteXYZ: selected.whiteXYZ))
            let referenceModel = try CIECAM16(conditions: .init(whiteXYZ: reference.whiteXYZ))
            let appearance = try sourceModel.forward(selected.objectXYZ)
            sourceAppearance = appearance
            referenceAppearance = try referenceModel.forward(reference.objectXYZ)
            sourceDegree = sourceModel.adaptationDegree
            referenceDegree = referenceModel.adaptationDegree
            // Preserve the selected illuminant's predicted Q/M/h as one set.
            simulatedXYZ = try referenceModel.inverse(appearance)
        }
        // Both patches and their deltas use the same integrated D50 white. This avoids
        // mixing the instrument's integration/PCS rounding with the new prediction.
        guard let referenceLab = CIELabColorimetry.lab(from: reference.objectXYZ, white: reference.whiteXYZ),
              let simulatedLab = CIELabColorimetry.lab(from: simulatedXYZ, white: reference.whiteXYZ) else {
            throw ColorAppearanceError.outsideModelDomain
        }

        return ReflectanceIlluminantColorComparisonResult(
            source: source, method: method, measuredLab: measurement.lab,
            referenceLab: referenceLab, referenceXYZ: reference.objectXYZ,
            referenceWhiteXYZ: reference.whiteXYZ,
            sourceXYZ: selected.objectXYZ, sourceWhiteXYZ: selected.whiteXYZ,
            simulatedXYZ: simulatedXYZ, simulatedLab: simulatedLab,
            referenceAppearance: referenceAppearance, sourceAppearance: sourceAppearance,
            referenceAdaptationDegree: referenceDegree, sourceAdaptationDegree: sourceDegree,
            wavelengthRange: first...last
        )
    }
}

enum CIELabColorimetry {
    private static let epsilon = 216.0 / 24_389.0
    private static let kappa = 24_389.0 / 27.0

    static func lab(from xyz: Vector3, white: Vector3) -> Vector3? {
        guard [
            xyz.first, xyz.second, xyz.third,
            white.first, white.second, white.third,
        ].allSatisfy(\.isFinite),
        white.first > 0,
        white.second > 0,
        white.third > 0 else {
            return nil
        }

        let fx = pivot(xyz.first / white.first)
        let fy = pivot(xyz.second / white.second)
        let fz = pivot(xyz.third / white.third)
        let lab = Vector3(
            first: (116 * fy) - 16,
            second: 500 * (fx - fy),
            third: 200 * (fy - fz)
        )
        guard [lab.first, lab.second, lab.third].allSatisfy(\.isFinite) else {
            return nil
        }
        return lab
    }

    private static func pivot(_ value: Double) -> Double {
        value > epsilon
            ? pow(value, 1.0 / 3.0)
            : ((kappa * value) + 16) / 116
    }
}

enum BradfordChromaticAdaptation {
    private static let matrix = [
        [0.8951, 0.2664, -0.1614],
        [-0.7502, 1.7135, 0.0367],
        [0.0389, -0.0685, 1.0296],
    ]
    private static let inverseMatrix = [
        [0.9869929054667123, -0.14705425642099013, 0.15996265166373122],
        [0.4323052697233945, 0.5183602715367776, 0.049291228212855594],
        [-0.008528664575177328, 0.04004282165408487, 0.9684866957875502],
    ]

    static func adapt(
        _ xyz: Vector3,
        sourceWhite: Vector3,
        destinationWhite: Vector3
    ) -> Vector3? {
        let sourceCone = multiply(matrix, sourceWhite)
        let destinationCone = multiply(matrix, destinationWhite)
        let objectCone = multiply(matrix, xyz)
        guard [
            sourceCone.first, sourceCone.second, sourceCone.third,
            destinationCone.first, destinationCone.second, destinationCone.third,
            objectCone.first, objectCone.second, objectCone.third,
        ].allSatisfy(\.isFinite),
        abs(sourceCone.first) > 1e-12,
        abs(sourceCone.second) > 1e-12,
        abs(sourceCone.third) > 1e-12 else {
            return nil
        }

        let adaptedCone = Vector3(
            first: objectCone.first * destinationCone.first / sourceCone.first,
            second: objectCone.second * destinationCone.second / sourceCone.second,
            third: objectCone.third * destinationCone.third / sourceCone.third
        )
        let adapted = multiply(inverseMatrix, adaptedCone)
        guard [adapted.first, adapted.second, adapted.third].allSatisfy(\.isFinite) else {
            return nil
        }
        return adapted
    }

    private static func multiply(
        _ matrix: [[Double]],
        _ vector: Vector3
    ) -> Vector3 {
        Vector3(
            first: matrix[0][0] * vector.first
                + matrix[0][1] * vector.second
                + matrix[0][2] * vector.third,
            second: matrix[1][0] * vector.first
                + matrix[1][1] * vector.second
                + matrix[1][2] * vector.third,
            third: matrix[2][0] * vector.first
                + matrix[2][1] * vector.second
                + matrix[2][2] * vector.third
        )
    }
}

enum CIEColorDifference {
    static func deltaE76(_ first: Vector3, _ second: Vector3) -> Double {
        hypot(
            hypot(first.first - second.first, first.second - second.second),
            first.third - second.third
        )
    }

    /// CIEDE2000 with unit parametric factors, following Sharma, Wu, and Dalal (2005).
    static func deltaE2000(_ first: Vector3, _ second: Vector3) -> Double {
        let pi = 3.14159265358979
        let radiansToDegrees = 180 / pi
        let degreesToRadians = pi / 180

        let c1ab = hypot(first.second, first.third)
        let c2ab = hypot(second.second, second.third)
        let meanCab = (c1ab + c2ab) / 2
        let meanCab7 = pow(meanCab, 7)
        let g = 0.5 * (1 - sqrt(meanCab7 / (meanCab7 + 6_103_515_625)))
        let a1Prime = (1 + g) * first.second
        let a2Prime = (1 + g) * second.second
        let c1Prime = hypot(a1Prime, first.third)
        let c2Prime = hypot(a2Prime, second.third)
        let h1Prime = hueDegrees(a: a1Prime, b: first.third, radiansToDegrees: radiansToDegrees)
        let h2Prime = hueDegrees(a: a2Prime, b: second.third, radiansToDegrees: radiansToDegrees)

        let deltaLPrime = second.first - first.first
        let deltaCPrime = c2Prime - c1Prime
        let deltaHueDegrees: Double
        if c1Prime < 1e-9 || c2Prime < 1e-9 {
            deltaHueDegrees = 0
        } else {
            var difference = h2Prime - h1Prime
            if difference > 180 {
                difference -= 360
            } else if difference < -180 {
                difference += 360
            }
            deltaHueDegrees = difference
        }
        let deltaHPrime = 2 * sqrt(c1Prime * c2Prime)
            * sin(0.5 * deltaHueDegrees * degreesToRadians)

        let meanLPrime = (first.first + second.first) / 2
        let meanCPrime = (c1Prime + c2Prime) / 2
        let meanHPrime: Double
        if c1Prime < 1e-9 || c2Prime < 1e-9 {
            meanHPrime = h1Prime + h2Prime
        } else {
            var sum = h1Prime + h2Prime
            if abs(h1Prime - h2Prime) > 180 {
                sum += sum < 360 ? 360 : -360
            }
            meanHPrime = sum / 2
        }

        let t = 1
            - 0.17 * cos((meanHPrime - 30) * degreesToRadians)
            + 0.24 * cos((2 * meanHPrime) * degreesToRadians)
            + 0.32 * cos((3 * meanHPrime + 6) * degreesToRadians)
            - 0.20 * cos((4 * meanHPrime - 63) * degreesToRadians)
        let lightnessOffsetSquared = pow(meanLPrime - 50, 2)
        let sL = 1 + (0.015 * lightnessOffsetSquared)
            / sqrt(20 + lightnessOffsetSquared)
        let sC = 1 + 0.045 * meanCPrime
        let sH = 1 + 0.015 * meanCPrime * t
        let hueRotation = 30 * exp(-pow((meanHPrime - 275) / 25, 2))
        let meanCPrime7 = pow(meanCPrime, 7)
        let rC = 2 * sqrt(meanCPrime7 / (meanCPrime7 + 6_103_515_625))
        let rT = -sin((2 * hueRotation) * degreesToRadians) * rC

        let lightnessTerm = deltaLPrime / sL
        let chromaTerm = deltaCPrime / sC
        let hueTerm = deltaHPrime / sH
        let squaredDifference = pow(lightnessTerm, 2)
            + pow(chromaTerm, 2)
            + pow(hueTerm, 2)
            + rT * chromaTerm * hueTerm
        return sqrt(max(0, squaredDifference))
    }

    private static func hueDegrees(
        a: Double,
        b: Double,
        radiansToDegrees: Double
    ) -> Double {
        guard hypot(a, b) >= 1e-9 else { return 0 }
        let angle = atan2(b, a) * radiansToDegrees
        return angle < 0 ? angle + 360 : angle
    }
}
