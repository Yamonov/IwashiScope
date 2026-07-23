import Foundation

enum CIEStandardIlluminant: String, CaseIterable, Sendable {
    case d50 = "D50"
    case d65 = "D65"

    var samples: [SpectralSample] {
        CIEStandardIlluminantSpectrum.samples(for: self)
    }
}

/// CIE standard illuminant data sampled every 5 nm from the official 1 nm tables.
///
/// D50: CIE 2022, DOI 10.25039/CIE.DS.etgmuqt5
/// D65: CIE 2019, DOI 10.25039/CIE.DS.hjfjmt59
/// Source data license: CC BY-SA 4.0.
enum CIEStandardIlluminantSpectrum {
    static let startWavelength = 380.0
    static let endWavelength = 730.0
    static let interval = 5.0
    static let normalizationWavelength = 560.0

    static func samples(for illuminant: CIEStandardIlluminant) -> [SpectralSample] {
        let values = switch illuminant {
        case .d50: d50Values
        case .d65: d65Values
        }

        return values.enumerated().map { index, value in
            SpectralSample(
                id: index,
                wavelength: startWavelength + (Double(index) * interval),
                value: value
            )
        }
    }

    private static let d50Values: [Double] = [
        24.4875, 27.179, 29.8706, 39.5894, 49.3081, 52.9104, 56.5128, 58.2733,
        60.0338, 58.9256, 57.8175, 66.3212, 74.8249, 81.036, 87.2472, 88.9297,
        90.6122, 90.9902, 91.3681, 93.2383, 95.1085, 93.5356, 91.9627, 93.8432,
        95.7237, 96.1685, 96.6133, 96.8712, 97.129, 99.614, 102.099, 101.427,
        100.755, 101.536, 102.317, 101.158, 100, 98.8675, 97.735, 98.3265,
        98.918, 96.2084, 93.4988, 95.5933, 97.6878, 98.4784, 99.2691, 99.1553,
        99.0415, 97.3816, 95.7218, 97.2895, 98.8572, 97.2622, 95.6672, 96.9285,
        98.1898, 100.597, 103.003, 101.068, 99.133, 93.257, 87.3809, 89.4922,
        91.6035, 92.246, 92.8886, 84.8715, 76.8544, 81.6828, 86.5112,
    ]

    private static let d65Values: [Double] = [
        49.9755, 52.3118, 54.6482, 68.7015, 82.7549, 87.1204, 91.486, 92.4589,
        93.4318, 90.057, 86.6823, 95.7736, 104.865, 110.936, 117.008, 117.41,
        117.812, 116.336, 114.861, 115.392, 115.923, 112.367, 108.811, 109.082,
        109.354, 108.578, 107.802, 106.296, 104.79, 106.239, 107.689, 106.047,
        104.405, 104.225, 104.046, 102.023, 100, 98.1671, 96.3342, 96.0611,
        95.788, 92.2368, 88.6856, 89.3459, 90.0062, 89.8026, 89.5991, 88.6489,
        87.6987, 85.4936, 83.2886, 83.4939, 83.6992, 81.863, 80.0268, 80.1207,
        80.2146, 81.2462, 82.2778, 80.281, 78.2842, 74.0027, 69.7213, 70.6652,
        71.6091, 72.979, 74.349, 67.9765, 61.604, 65.7448, 69.8856,
    ]
}

enum SpectrumOverlayNormalizer {
    static func scale(
        referenceSamples: [SpectralSample],
        to measuredSamples: [SpectralSample],
        at wavelength: Double = CIEStandardIlluminantSpectrum.normalizationWavelength
    ) -> [SpectralSample] {
        let orderedMeasurement = measuredSamples.sorted { $0.wavelength < $1.wavelength }
        let orderedReference = referenceSamples.sorted { $0.wavelength < $1.wavelength }

        guard let measuredValue = interpolatedValue(in: orderedMeasurement, at: wavelength),
              let referenceValue = interpolatedValue(in: orderedReference, at: wavelength),
              measuredValue.isFinite,
              referenceValue.isFinite,
              measuredValue > 0,
              referenceValue > 0 else {
            return []
        }

        let scale = measuredValue / referenceValue
        return orderedReference.map { sample in
            SpectralSample(
                id: sample.id,
                wavelength: sample.wavelength,
                value: sample.value * scale
            )
        }
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
