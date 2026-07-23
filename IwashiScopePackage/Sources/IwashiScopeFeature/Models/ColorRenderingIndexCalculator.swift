/*
 The CIE 1995 CRI calculation in this file is adapted from ArgyllCMS 3.5.0
 xicc/xspect.c, function icx_CIE1995_CRI(), written by Graeme W. Gill,
 Copyright (C) 2000–2006. The original portions are licensed under
 GPL-2.0-or-later; see Argyll_V3.5.0/License2.txt.

 Adapted for IwashiScope by Yamonov on 2026-07-23: limited the calculation to
 R15, converted it to Swift value types, resampled measured spectra to 5 nm,
 and added finite-value and wavelength-range validation. IwashiScope's
 modifications are licensed under AGPL-3.0-only. The combined IwashiScope
 distribution is provided under AGPL-3.0-only.
*/

import Foundation

/// Computes the special color rendering index R15 from a measured illuminant.
///
/// R1–R14 remain the values reported by spotread. R15 is evaluated with the
/// CIE 13.3 / CIE 1995 test-color method using TCS15 (JIS Asian skin color).
enum ColorRenderingIndexCalculator {
    private struct XYZ {
        let x: Double
        let y: Double
        let z: Double

        func divided(by divisor: Double) -> XYZ {
            XYZ(x: x / divisor, y: y / divisor, z: z / divisor)
        }
    }

    private struct YUV {
        let y: Double
        let u: Double
        let v: Double
    }

    private struct YCD {
        let y: Double
        let c: Double
        let d: Double
    }

    private struct WUV {
        let w: Double
        let u: Double
        let v: Double
    }

    private static let wavelengths = (0..<ColorRenderingReferenceData.sampleCount).map {
        ColorRenderingReferenceData.startWavelength + (Double($0) * ColorRenderingReferenceData.interval)
    }

    static func r15(spectrum: [SpectralSample], cct: Double) -> Double? {
        let orderedSpectrum = spectrum.sorted { $0.wavelength < $1.wavelength }

        guard orderedSpectrum.count >= 2,
              let firstWavelength = orderedSpectrum.first?.wavelength,
              let lastWavelength = orderedSpectrum.last?.wavelength,
              firstWavelength <= 400,
              lastWavelength >= 700,
              cct.isFinite,
              cct >= 1,
              cct <= 25_000,
              let referenceIlluminant = referenceIlluminant(cct: cct) else {
            return nil
        }

        // spotread high-resolution output is normally spaced at about 3.33 nm.
        // Resample it to the 5 nm CIE table and extend its end values across the
        // small unmeasured observer tails, as recommended for truncated spectra.
        let testIlluminant = wavelengths.map {
            interpolatedValue(in: orderedSpectrum, at: $0)
        }

        guard let testWhiteRaw = tristimulus(illuminant: testIlluminant),
              let referenceWhiteRaw = tristimulus(illuminant: referenceIlluminant),
              testWhiteRaw.y > 1e-12,
              referenceWhiteRaw.y > 1e-12 else {
            return nil
        }

        let testWhite = testWhiteRaw.divided(by: testWhiteRaw.y)
        let referenceWhite = referenceWhiteRaw.divided(by: referenceWhiteRaw.y)

        guard let testWhiteYUV = cie1960YUV(from: testWhite),
              let referenceWhiteYUV = cie1960YUV(from: referenceWhite),
              let testWhiteYCD = ycd(from: testWhiteYUV),
              let referenceWhiteYCD = ycd(from: referenceWhiteYUV),
              abs(testWhiteYCD.c) > 1e-12,
              abs(testWhiteYCD.d) > 1e-12 else {
            return nil
        }

        let cAdaptation = referenceWhiteYCD.c / testWhiteYCD.c
        let dAdaptation = referenceWhiteYCD.d / testWhiteYCD.d

        guard let referenceColorRaw = tristimulus(
            illuminant: referenceIlluminant,
            reflectance: ColorRenderingReferenceData.tcs15
        ),
        let testColorRaw = tristimulus(
            illuminant: testIlluminant,
            reflectance: ColorRenderingReferenceData.tcs15
        ) else {
            return nil
        }

        let referenceColor = referenceColorRaw.divided(by: referenceWhiteRaw.y)
        let testColor = testColorRaw.divided(by: testWhiteRaw.y)

        guard let referenceWUV = cie1964WUV(from: referenceColor, white: referenceWhite),
              let testColorYUV = cie1960YUV(from: testColor),
              let testColorYCD = ycd(from: testColorYUV) else {
            return nil
        }

        let adaptationDenominator = 16.518
            + (1.481 * testColorYCD.c * cAdaptation)
            - (testColorYCD.d * dAdaptation)

        guard abs(adaptationDenominator) > 1e-12 else {
            return nil
        }

        let adaptedTestColor = YUV(
            y: testColorYUV.y,
            u: (
                10.872
                    + (0.404 * testColorYCD.c * cAdaptation)
                    - (4 * testColorYCD.d * dAdaptation)
            ) / adaptationDenominator,
            v: 5.520 / adaptationDenominator
        )

        guard let testWUV = cie1964WUV(from: adaptedTestColor, white: referenceWhite) else {
            return nil
        }

        let deltaW = referenceWUV.w - testWUV.w
        let deltaU = referenceWUV.u - testWUV.u
        let deltaV = referenceWUV.v - testWUV.v
        let deltaE = sqrt((deltaW * deltaW) + (deltaU * deltaU) + (deltaV * deltaV))
        let value = 100 - (4.6 * deltaE)

        return value.isFinite ? value : nil
    }

    private static func referenceIlluminant(cct: Double) -> [Double]? {
        if cct < 5_000 {
            return planckianIlluminant(cct: cct)
        }

        return daylightIlluminant(cct: cct)
    }

    private static func planckianIlluminant(cct: Double) -> [Double] {
        let secondRadiationConstant = 1.4388e-2
        let normalizationWavelength = 560e-9
        let normalization = pow(normalizationWavelength, -5)
            / (exp(secondRadiationConstant / (normalizationWavelength * cct)) - 1)

        return wavelengths.map { wavelength in
            let wavelengthInMeters = wavelength * 1e-9
            let spectralPower = pow(wavelengthInMeters, -5)
                / (exp(secondRadiationConstant / (wavelengthInMeters * cct)) - 1)
            return spectralPower / normalization
        }
    }

    private static func daylightIlluminant(cct: Double) -> [Double]? {
        guard cct >= 5_000, cct <= 25_000 else {
            return nil
        }

        let x: Double
        if cct < 7_000 {
            x = (-4.6070e9 / pow(cct, 3))
                + (2.9678e6 / pow(cct, 2))
                + (0.09911e3 / cct)
                + 0.244063
        } else {
            x = (-2.0064e9 / pow(cct, 3))
                + (1.9018e6 / pow(cct, 2))
                + (0.24748e3 / cct)
                + 0.237040
        }

        let y = (-3 * x * x) + (2.87 * x) - 0.275
        let denominator = (0.25539 * x) - (0.73217 * y) + 0.02387

        guard abs(denominator) > 1e-12 else {
            return nil
        }

        let m1 = ((-1.77861 * x) + (5.90757 * y) - 1.34674) / denominator
        let m2 = ((-31.44464 * x) + (30.06400 * y) + 0.03638) / denominator

        return ColorRenderingReferenceData.daylightS0.indices.map { index in
            ColorRenderingReferenceData.daylightS0[index]
                + (m1 * ColorRenderingReferenceData.daylightS1[index])
                + (m2 * ColorRenderingReferenceData.daylightS2[index])
        }
    }

    private static func tristimulus(
        illuminant: [Double],
        reflectance: [Double]? = nil
    ) -> XYZ? {
        guard illuminant.count == ColorRenderingReferenceData.sampleCount,
              reflectance == nil || reflectance?.count == ColorRenderingReferenceData.sampleCount else {
            return nil
        }

        var x = 0.0
        var y = 0.0
        var z = 0.0

        for index in illuminant.indices {
            let reflectedPower = illuminant[index] * (reflectance?[index] ?? 1)
            x += reflectedPower * ColorRenderingReferenceData.xBar[index]
            y += reflectedPower * ColorRenderingReferenceData.yBar[index]
            z += reflectedPower * ColorRenderingReferenceData.zBar[index]
        }

        let result = XYZ(x: x, y: y, z: z)
        return x.isFinite && y.isFinite && z.isFinite ? result : nil
    }

    private static func cie1960YUV(from xyz: XYZ) -> YUV? {
        let denominator = xyz.x + (15 * xyz.y) + (3 * xyz.z)
        guard abs(denominator) > 1e-12 else {
            return nil
        }

        return YUV(
            y: xyz.y,
            u: (4 * xyz.x) / denominator,
            v: (6 * xyz.y) / denominator
        )
    }

    private static func ycd(from yuv: YUV) -> YCD? {
        guard abs(yuv.v) > 1e-12 else {
            return nil
        }

        return YCD(
            y: yuv.y,
            c: (4 - yuv.u - (10 * yuv.v)) / yuv.v,
            d: ((1.708 * yuv.v) - (1.481 * yuv.u) + 0.404) / yuv.v
        )
    }

    private static func cie1964WUV(from xyz: XYZ, white: XYZ) -> WUV? {
        guard let colorYUV = cie1960YUV(from: xyz) else {
            return nil
        }
        return cie1964WUV(from: colorYUV, white: white)
    }

    private static func cie1964WUV(from yuv: YUV, white: XYZ) -> WUV? {
        guard let whiteYUV = cie1960YUV(from: white),
              whiteYUV.y > 1e-12,
              yuv.y >= 0 else {
            return nil
        }

        let w = (25 * pow((yuv.y * 100) / whiteYUV.y, 1.0 / 3.0)) - 17
        return WUV(
            w: w,
            u: 13 * w * (yuv.u - whiteYUV.u),
            v: 13 * w * (yuv.v - whiteYUV.v)
        )
    }

    private static func interpolatedValue(
        in samples: [SpectralSample],
        at wavelength: Double
    ) -> Double {
        guard let first = samples.first, let last = samples.last else {
            return 0
        }

        if wavelength <= first.wavelength {
            return first.value
        }
        if wavelength >= last.wavelength {
            return last.value
        }

        var lowerIndex = 0
        var upperIndex = samples.count - 1

        while upperIndex - lowerIndex > 1 {
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
        guard width > 0 else {
            return lower.value
        }

        let fraction = (wavelength - lower.wavelength) / width
        return lower.value + ((upper.value - lower.value) * fraction)
    }
}
