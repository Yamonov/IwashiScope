import Foundation

enum SpectrumPeakAnnotation {
    static func text(
        measurementName: String?,
        measurement: SpotMeasurement?,
        peak: SpectralSample
    ) -> String {
        let peakText = "Peak "
            + peak.value.formatted(
                .number.precision(.fractionLength(2))
            )
            + " @ "
            + peak.wavelength.formatted(
                .number.precision(.fractionLength(1))
            )
            + " nm"
        var components: [String] = []
        if let measurementName {
            components.append(measurementName)
        }
        if let measurement {
            switch measurement.mode {
            case .reflectance:
                if let lab = measurement.lab,
                   [lab.first, lab.second, lab.third].allSatisfy(\.isFinite) {
                    components.append(labText(lab))
                }
            case .ambient:
                if let lux = measurement.lux, lux.isFinite {
                    components.append(
                        lux.formatted(
                            .number.precision(.fractionLength(1))
                        ) + " lx"
                    )
                }
                appendCCT(of: measurement, to: &components)
            case .emissive:
                if let luminance = measurement.xyz?.second,
                   luminance.isFinite {
                    components.append(
                        luminance.formatted(
                            .number.precision(.fractionLength(1))
                        ) + " cd/m²"
                    )
                }
                appendCCT(of: measurement, to: &components)
            }
        }
        components.append(peakText)
        return components.joined(separator: "　")
    }

    private static func labText(_ lab: Vector3) -> String {
        "Lab:"
            + lab.first.formatted(
                .number.precision(.fractionLength(1))
            )
            + "/"
            + lab.second.formatted(
                .number.precision(.fractionLength(1))
            )
            + "/"
            + lab.third.formatted(
                .number.precision(.fractionLength(1))
            )
    }

    private static func appendCCT(
        of measurement: SpotMeasurement,
        to components: inout [String]
    ) {
        guard let cct = measurement.cct, cct.isFinite else { return }
        components.append(
            "CCT "
                + cct.formatted(
                    .number
                        .grouping(.never)
                        .precision(.fractionLength(0))
                )
                + " K"
        )
    }
}
