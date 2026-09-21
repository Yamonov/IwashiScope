import Foundation

/// Shared, relative-Y reflectance integration; never uses a chart's clipped display range.
enum SpectralTristimulusIntegrator {
    struct IntegrationResult {
        let objectXYZ: Vector3
        let whiteXYZ: Vector3
        let wavelengths: [Double]
    }

    static func integrate(
        reflectance: [SpectralSample],
        illuminant: [SpectralSample],
        wavelengths: [Double]? = nil
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
              orderedIlluminant.count >= 2,
              zip(orderedReflectance, orderedReflectance.dropFirst()).allSatisfy({ $0.wavelength < $1.wavelength }),
              zip(orderedIlluminant, orderedIlluminant.dropFirst()).allSatisfy({ $0.wavelength < $1.wavelength }) else {
            return nil
        }

        var whiteX = 0.0
        var whiteY = 0.0
        var whiteZ = 0.0
        var objectX = 0.0
        var objectY = 0.0
        var objectZ = 0.0
        var usedWavelengths: [Double] = []

        for wavelength in wavelengths ?? Array(stride(from: 380.0, through: 730.0, by: 5.0)) {
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
            usedWavelengths.append(wavelength)
        }

        guard usedWavelengths.count >= 2,
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
        return IntegrationResult(objectXYZ: objectXYZ, whiteXYZ: whiteXYZ, wavelengths: usedWavelengths)
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
