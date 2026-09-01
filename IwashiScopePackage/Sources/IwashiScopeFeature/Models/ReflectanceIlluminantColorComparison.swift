import Foundation

struct ReflectanceIlluminantColorComparisonResult: Equatable, Sendable {
    let source: IlluminantSpectrumDefinition
    let appliesChromaticAdaptation: Bool
    let measuredLab: Vector3
    let simulatedXYZ: Vector3
    let sourceWhiteXYZ: Vector3
    let simulatedLab: Vector3
    let deltaL: Double
    let deltaA: Double
    let deltaB: Double
    let deltaE76: Double
    let deltaE2000: Double

    var illuminant: CIEReferenceIlluminant? {
        guard case let .cie(illuminant) = source.origin else { return nil }
        return illuminant
    }
}

enum ReflectanceIlluminantColorComparisonCalculator {
    /// ICC profile connection-space D50, scaled so Y = 100.
    static let d50White = Vector3(first: 96.42, second: 100, third: 82.49)

    static func result(
        for measurement: SpotMeasurement?,
        illuminant: CIEReferenceIlluminant?,
        appliesChromaticAdaptation: Bool
    ) -> ReflectanceIlluminantColorComparisonResult? {
        result(
            for: measurement,
            source: illuminant.map {
                IlluminantSpectrumDefinition(cie: $0)
            },
            appliesChromaticAdaptation: appliesChromaticAdaptation
        )
    }

    static func result(
        for measurement: SpotMeasurement?,
        source: IlluminantSpectrumDefinition?,
        appliesChromaticAdaptation: Bool
    ) -> ReflectanceIlluminantColorComparisonResult? {
        guard let measurement,
              measurement.mode == .reflectance,
              let source,
              let measuredLab = measurement.lab,
              [measuredLab.first, measuredLab.second, measuredLab.third]
                .allSatisfy(\.isFinite) else {
            return nil
        }
        if let whitePoint = measurement.labWhitePoint,
           whitePoint.localizedCaseInsensitiveContains("D50") == false {
            return nil
        }

        guard let integration = integrate(
            reflectance: measurement.spectrum,
            illuminant: source.samples
        ) else {
            return nil
        }

        let simulatedXYZ: Vector3
        if appliesChromaticAdaptation {
            guard let adaptedXYZ = BradfordChromaticAdaptation.adapt(
                integration.objectXYZ,
                sourceWhite: integration.whiteXYZ,
                destinationWhite: d50White
            ) else {
                return nil
            }
            simulatedXYZ = adaptedXYZ
        } else {
            simulatedXYZ = integration.objectXYZ
        }

        guard let simulatedLab = CIELabColorimetry.lab(
            from: simulatedXYZ,
            white: d50White
        ) else {
            return nil
        }

        return ReflectanceIlluminantColorComparisonResult(
            source: source,
            appliesChromaticAdaptation: appliesChromaticAdaptation,
            measuredLab: measuredLab,
            simulatedXYZ: simulatedXYZ,
            sourceWhiteXYZ: integration.whiteXYZ,
            simulatedLab: simulatedLab,
            deltaL: simulatedLab.first - measuredLab.first,
            deltaA: simulatedLab.second - measuredLab.second,
            deltaB: simulatedLab.third - measuredLab.third,
            deltaE76: CIEColorDifference.deltaE76(measuredLab, simulatedLab),
            deltaE2000: CIEColorDifference.deltaE2000(measuredLab, simulatedLab)
        )
    }

    private struct IntegrationResult {
        let objectXYZ: Vector3
        let whiteXYZ: Vector3
    }

    private static func integrate(
        reflectance: [SpectralSample],
        illuminant: [SpectralSample]
    ) -> IntegrationResult? {
        let orderedReflectance = reflectance
            .filter {
                $0.wavelength.isFinite && $0.value.isFinite
            }
            .sorted { $0.wavelength < $1.wavelength }
        let orderedIlluminant = illuminant
            .filter {
                $0.wavelength.isFinite
                    && $0.value.isFinite
                    && $0.value >= 0
            }
            .sorted { $0.wavelength < $1.wavelength }
        guard orderedReflectance.count >= 2,
              orderedIlluminant.count >= 2 else {
            return nil
        }

        var whiteX = 0.0
        var whiteY = 0.0
        var whiteZ = 0.0
        var objectX = 0.0
        var objectY = 0.0
        var objectZ = 0.0
        var usedSampleCount = 0

        for wavelength in stride(from: 380.0, through: 730.0, by: 5.0) {
            guard let source = interpolatedValue(
                      in: orderedIlluminant,
                      at: wavelength
                  ),
                  let observer = observerValues(at: wavelength),
                  let reflectanceValue = interpolatedValue(
                      in: orderedReflectance,
                      at: wavelength
                  ) else {
                continue
            }

            let scaledReflectance = max(0, reflectanceValue) / 100
            whiteX += source * observer.x
            whiteY += source * observer.y
            whiteZ += source * observer.z
            objectX += source * scaledReflectance * observer.x
            objectY += source * scaledReflectance * observer.y
            objectZ += source * scaledReflectance * observer.z
            usedSampleCount += 1
        }

        guard usedSampleCount >= 2,
              whiteY.isFinite,
              whiteY > 1e-12 else {
            return nil
        }

        let normalization = 100 / whiteY
        let whiteXYZ = Vector3(
            first: whiteX * normalization,
            second: 100,
            third: whiteZ * normalization
        )
        let objectXYZ = Vector3(
            first: objectX * normalization,
            second: objectY * normalization,
            third: objectZ * normalization
        )
        guard [
            whiteXYZ.first, whiteXYZ.second, whiteXYZ.third,
            objectXYZ.first, objectXYZ.second, objectXYZ.third,
        ].allSatisfy(\.isFinite) else {
            return nil
        }
        return IntegrationResult(objectXYZ: objectXYZ, whiteXYZ: whiteXYZ)
    }

    private static func observerValues(
        at wavelength: Double
    ) -> (x: Double, y: Double, z: Double)? {
        let indexValue = (
            wavelength - ColorRenderingReferenceData.startWavelength
        ) / ColorRenderingReferenceData.interval
        let index = Int(indexValue.rounded())
        guard abs(indexValue - Double(index)) < 1e-9,
              ColorRenderingReferenceData.xBar.indices.contains(index),
              ColorRenderingReferenceData.yBar.indices.contains(index),
              ColorRenderingReferenceData.zBar.indices.contains(index) else {
            return nil
        }
        return (
            ColorRenderingReferenceData.xBar[index],
            ColorRenderingReferenceData.yBar[index],
            ColorRenderingReferenceData.zBar[index]
        )
    }

    private static func interpolatedValue(
        in samples: [SpectralSample],
        at wavelength: Double
    ) -> Double? {
        guard let first = samples.first,
              let last = samples.last,
              wavelength >= first.wavelength,
              wavelength <= last.wavelength else {
            return nil
        }
        if wavelength == first.wavelength { return first.value }
        if wavelength == last.wavelength { return last.value }

        var lowerIndex = 0
        var upperIndex = samples.count - 1
        while lowerIndex + 1 < upperIndex {
            let midpoint = (lowerIndex + upperIndex) / 2
            if samples[midpoint].wavelength <= wavelength {
                lowerIndex = midpoint
            } else {
                upperIndex = midpoint
            }
        }

        let lower = samples[lowerIndex]
        let upper = samples[upperIndex]
        let width = upper.wavelength - lower.wavelength
        guard width > 0 else { return nil }
        let fraction = (wavelength - lower.wavelength) / width
        return lower.value + ((upper.value - lower.value) * fraction)
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
