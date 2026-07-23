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
/// The unmodified CSV and metadata are in ThirdParty/CIE. The generated
/// 5 nm arrays are in CIEStandardIlluminantData.generated.swift.
enum CIEStandardIlluminantSpectrum {
    static let startWavelength = 380.0
    static let endWavelength = 730.0
    static let interval = 5.0
    static let normalizationWavelength = 560.0

    static func samples(for illuminant: CIEStandardIlluminant) -> [SpectralSample] {
        let values = switch illuminant {
        case .d50: CIEStandardIlluminantData.d50Values
        case .d65: CIEStandardIlluminantData.d65Values
        }

        return values.enumerated().map { index, value in
            SpectralSample(
                id: index,
                wavelength: startWavelength + (Double(index) * interval),
                value: value
            )
        }
    }

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
