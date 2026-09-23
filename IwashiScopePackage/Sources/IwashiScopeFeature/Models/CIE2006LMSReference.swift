import Foundation

/// Display-only reference. Never used by the colorimetric or appearance calculations.
enum CIE2006LMSReference {
    enum Cone: String, CaseIterable, Identifiable, Sendable {
        case long = "L"
        case medium = "M"
        case short = "S"

        var id: Self { self }

        var sourceValues: [Double] {
            switch self {
            case .long: CIE2006LMSData.long
            case .medium: CIE2006LMSData.medium
            case .short: CIE2006LMSData.short
            }
        }
    }

    struct Curve: Identifiable, Equatable, Sendable {
        let cone: Cone
        let samples: [SpectralSample]
        var id: Cone { cone }
    }

    static let strokeOpacity = 0.4 // 60% transparent, not 60% opaque.

    /// Normalize before cropping, so changing the practical area cannot change a curve's shape/gain.
    static let fullCurves: [Curve] = Cone.allCases.map { cone in
        let values = cone.sourceValues
        let peak = values.max() ?? 1
        return Curve(cone: cone, samples: values.enumerated().map { index, value in
            SpectralSample(
                id: index,
                wavelength: CIE2006LMSData.firstWavelength + Double(index) * CIE2006LMSData.interval,
                value: value / peak
            )
        })
    }

    static func curves(in displayRange: ClosedRange<Double>) -> [Curve] {
        guard displayRange.lowerBound.isFinite, displayRange.upperBound.isFinite,
              displayRange.lowerBound < displayRange.upperBound else { return [] }
        return fullCurves.compactMap { curve in
            guard let first = curve.samples.first, let last = curve.samples.last else { return nil }
            let lower = max(displayRange.lowerBound, first.wavelength)
            let upper = min(displayRange.upperBound, last.wavelength)
            guard lower < upper else { return nil }
            let wavelengths = [lower] + curve.samples.lazy
                .filter { $0.wavelength > lower && $0.wavelength < upper }
                .map(\.wavelength) + [upper]
            let samples = wavelengths.enumerated().map { index, wavelength in
                SpectralSample(id: index, wavelength: wavelength,
                               value: interpolatedValue(at: wavelength, in: curve.samples))
            }
            return Curve(cone: curve.cone, samples: samples)
        }
    }

    private static func interpolatedValue(at wavelength: Double, in samples: [SpectralSample]) -> Double {
        let index = (wavelength - CIE2006LMSData.firstWavelength) / CIE2006LMSData.interval
        let lower = min(max(0, Int(index.rounded(.down))), samples.count - 1)
        let upper = min(lower + 1, samples.count - 1)
        return samples[lower].value + (samples[upper].value - samples[lower].value) * (index - Double(lower))
    }
}
