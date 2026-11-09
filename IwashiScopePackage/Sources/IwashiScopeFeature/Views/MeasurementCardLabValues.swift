import SwiftUI

struct MeasurementCardLabValues: View {
    let lab: Vector3?
    let deltaE: (ciede2000: Double, cie76: Double)?

    init(lab: Vector3?, deltaE: (ciede2000: Double, cie76: Double)? = nil) {
        self.lab = lab
        self.deltaE = deltaE
    }

    var body: some View {
        Grid(horizontalSpacing: 4, verticalSpacing: 1) {
            GridRow {
                metric("L*", lab?.first)
                metric("a*", lab?.second)
                metric("b*", lab?.third)
                if let deltaE {
                    metric("ΔE00", deltaE.ciede2000, digits: 2)
                        .padding(.leading, 10)
                    metric("ΔE76", deltaE.cie76, digits: 2)
                }
            }
        }
        .padding(.horizontal, 6)
        .padding(.vertical, 3)
    }

    private func metric(_ label: String, _ value: Double?, digits: Int = 1) -> some View {
        VStack(alignment: .leading, spacing: 1) {
            Text(label)
                .font(.caption2)
                .foregroundStyle(.secondary)
            Text(value?.formatted(.number.precision(.fractionLength(digits))) ?? "—")
                .font(.caption.monospacedDigit())
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}
