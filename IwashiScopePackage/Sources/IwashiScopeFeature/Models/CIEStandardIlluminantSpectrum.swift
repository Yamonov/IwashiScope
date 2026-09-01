import Foundation

enum CIEIlluminantCategory: String, CaseIterable, Identifiable, Sendable {
    case standardAndDaylight
    case indoorDaylight
    case fluorescentFL
    case fluorescentFL3
    case highPressureDischarge
    case led
    case calibration

    var id: String { rawValue }

    var title: String {
        switch self {
        case .standardAndDaylight: "標準・昼光"
        case .indoorDaylight: "屋内昼光"
        case .fluorescentFL: "蛍光ランプ FL"
        case .fluorescentFL3: "蛍光ランプ FL3"
        case .highPressureDischarge: "高圧放電ランプ"
        case .led: "LED"
        case .calibration: "校正用参考スペクトル"
        }
    }
}

enum CIEReferenceIlluminant: String, CaseIterable, Identifiable, Sendable {
    case a = "A"
    case c = "C"
    case d50 = "D50"
    case d55 = "D55"
    case d65 = "D65"
    case d75 = "D75"
    case id50 = "ID50"
    case id65 = "ID65"
    case fl1 = "FL1"
    case fl2 = "FL2"
    case fl3 = "FL3"
    case fl4 = "FL4"
    case fl5 = "FL5"
    case fl6 = "FL6"
    case fl7 = "FL7"
    case fl8 = "FL8"
    case fl9 = "FL9"
    case fl10 = "FL10"
    case fl11 = "FL11"
    case fl12 = "FL12"
    case fl3_1 = "FL3.1"
    case fl3_2 = "FL3.2"
    case fl3_3 = "FL3.3"
    case fl3_4 = "FL3.4"
    case fl3_5 = "FL3.5"
    case fl3_6 = "FL3.6"
    case fl3_7 = "FL3.7"
    case fl3_8 = "FL3.8"
    case fl3_9 = "FL3.9"
    case fl3_10 = "FL3.10"
    case fl3_11 = "FL3.11"
    case fl3_12 = "FL3.12"
    case fl3_13 = "FL3.13"
    case fl3_14 = "FL3.14"
    case fl3_15 = "FL3.15"
    case hp1 = "HP1"
    case hp2 = "HP2"
    case hp3 = "HP3"
    case hp4 = "HP4"
    case hp5 = "HP5"
    case ledB1 = "LED-B1"
    case ledB2 = "LED-B2"
    case ledB3 = "LED-B3"
    case ledB4 = "LED-B4"
    case ledB5 = "LED-B5"
    case ledBH1 = "LED-BH1"
    case ledRGB1 = "LED-RGB1"
    case ledV1 = "LED-V1"
    case ledV2 = "LED-V2"
    case l41 = "L41"

    var id: String { rawValue }

    var category: CIEIlluminantCategory {
        switch self {
        case .a, .c, .d50, .d55, .d65, .d75:
            .standardAndDaylight
        case .id50, .id65:
            .indoorDaylight
        case .fl1, .fl2, .fl3, .fl4, .fl5, .fl6,
             .fl7, .fl8, .fl9, .fl10, .fl11, .fl12:
            .fluorescentFL
        case .fl3_1, .fl3_2, .fl3_3, .fl3_4, .fl3_5,
             .fl3_6, .fl3_7, .fl3_8, .fl3_9, .fl3_10,
             .fl3_11, .fl3_12, .fl3_13, .fl3_14, .fl3_15:
            .fluorescentFL3
        case .hp1, .hp2, .hp3, .hp4, .hp5:
            .highPressureDischarge
        case .ledB1, .ledB2, .ledB3, .ledB4, .ledB5,
             .ledBH1, .ledRGB1, .ledV1, .ledV2:
            .led
        case .l41:
            .calibration
        }
    }

    var displayName: String {
        switch self {
        case .a: "A（白熱電球）"
        case .c: "C（旧昼光）"
        case .d50: "D50（5000 K 昼光）"
        case .d55: "D55（5500 K 昼光）"
        case .d65: "D65（6500 K 昼光）"
        case .d75: "D75（7500 K 昼光）"
        case .id50: "ID50（屋内昼光）"
        case .id65: "ID65（屋内昼光）"
        case .l41: "L41（測光器校正用）"
        default: rawValue
        }
    }

    var samples: [SpectralSample] {
        CIEReferenceIlluminantSpectrum.samples(for: self)
    }
}

enum CIEStandardIlluminant: String, CaseIterable, Sendable {
    case d50 = "D50"
    case d65 = "D65"

    var samples: [SpectralSample] {
        CIEStandardIlluminantSpectrum.samples(for: self)
    }
}

/// CIE illuminant and reference spectra sampled every 5 nm from 380...730 nm.
enum CIEReferenceIlluminantSpectrum {
    static let startWavelength = 380.0
    static let endWavelength = 730.0
    static let interval = 5.0

    static func samples(for illuminant: CIEReferenceIlluminant) -> [SpectralSample] {
        guard let values = CIEReferenceIlluminantData.valuesByIlluminant[illuminant] else {
            return []
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
