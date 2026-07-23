import Charts
import SwiftUI

struct SpectrumChartView: View {
    let mode: MeasurementMode
    let measurement: SpotMeasurement?
    let calibrationCompleted: Bool
    let showsReferenceControls: Bool

    @State private var showsD50Reference = false
    @State private var showsD65Reference = false

    init(
        mode: MeasurementMode,
        measurement: SpotMeasurement?,
        calibrationCompleted: Bool,
        showsReferenceControls: Bool = true,
        initialShowsD50Reference: Bool = false,
        initialShowsD65Reference: Bool = false
    ) {
        self.mode = mode
        self.measurement = measurement
        self.calibrationCompleted = calibrationCompleted
        self.showsReferenceControls = showsReferenceControls
        _showsD50Reference = State(initialValue: initialShowsD50Reference)
        _showsD65Reference = State(initialValue: initialShowsD65Reference)
    }

    private var samples: [SpectralSample] {
        measurement?.spectrum ?? []
    }

    private var d50ReferenceSamples: [SpectralSample] {
        guard showsD50Reference else { return [] }
        return SpectrumOverlayNormalizer.scale(
            referenceSamples: CIEStandardIlluminant.d50.samples,
            to: samples
        )
    }

    private var d65ReferenceSamples: [SpectralSample] {
        guard showsD65Reference else { return [] }
        return SpectrumOverlayNormalizer.scale(
            referenceSamples: CIEStandardIlluminant.d65.samples,
            to: samples
        )
    }

    private var yUpperBound: Double {
        let maximum = (samples + d50ReferenceSamples + d65ReferenceSamples)
            .lazy
            .map(\.value)
            .max() ?? 1
        return max(1, maximum * 1.08)
    }

    var body: some View {
        GroupBox {
            VStack(alignment: .leading, spacing: 10) {
                if mode != .reflectance {
                    if showsReferenceControls {
                        referenceControls
                        Divider()
                    } else if showsD50Reference || showsD65Reference {
                        exportedReferenceLegend
                        Divider()
                    }
                }

                if samples.isEmpty {
                    emptyState
                } else {
                    chart
                }
            }
        }
        .frame(maxWidth: .infinity)
        .accessibilityIdentifier("spectrum-group")
    }

    private var referenceControls: some View {
        HStack(spacing: 14) {
            Text("基準分光分布")
                .foregroundStyle(.secondary)

            Toggle(isOn: $showsD50Reference) {
                ReferenceSpectrumToggleLabel(title: "ISO 3664 D50", color: .orange)
            }
            .toggleStyle(.checkbox)
            .help("ISO 3664の観察条件で用いるCIE標準光源D50を重ねます")

            Toggle(isOn: $showsD65Reference) {
                ReferenceSpectrumToggleLabel(title: "ISO 3668 D65", color: .blue)
            }
            .toggleStyle(.checkbox)
            .help("ISO 3668の標準ブースで用いるCIE標準光源D65を重ねます")

            Spacer(minLength: 8)

            if showsD50Reference || showsD65Reference {
                Text("560 nmで測定値に合わせて表示")
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
        }
        .font(.caption)
        .frame(maxWidth: .infinity)
    }

    private var exportedReferenceLegend: some View {
        HStack(spacing: 14) {
            Text("基準分光分布")
                .foregroundStyle(.secondary)

            if showsD50Reference {
                ReferenceSpectrumToggleLabel(
                    title: "ISO 3664 D50",
                    color: .orange
                )
            }
            if showsD65Reference {
                ReferenceSpectrumToggleLabel(
                    title: "ISO 3668 D65",
                    color: .blue
                )
            }
            Spacer()
            Text("560 nmで測定値に合わせて表示")
                .foregroundStyle(.secondary)
        }
        .font(.caption)
        .frame(maxWidth: .infinity)
    }

    private var chart: some View {
        Chart {
            ForEach(samples) { sample in
                AreaMark(
                    x: .value("波長（nm）", sample.wavelength),
                    yStart: .value("基準", 0.0),
                    yEnd: .value("スペクトル値", sample.value)
                )
                .interpolationMethod(.linear)
                .foregroundStyle(SpectrumChartStyle.gradient)
                .alignsMarkStylesWithPlotArea()

                LineMark(
                    x: .value("波長（nm）", sample.wavelength),
                    y: .value("スペクトル値", sample.value),
                    series: .value("系列", "測定値")
                )
                .interpolationMethod(.linear)
                .lineStyle(.init(lineWidth: 2, lineCap: .round, lineJoin: .round))
                .foregroundStyle(Color.gray)
            }

            ForEach(d50ReferenceSamples) { sample in
                LineMark(
                    x: .value("波長（nm）", sample.wavelength),
                    y: .value("D50基準分光分布", sample.value),
                    series: .value("系列", "ISO 3664 D50")
                )
                .interpolationMethod(.linear)
                .lineStyle(.init(lineWidth: 2, lineCap: .round, lineJoin: .round))
                .foregroundStyle(Color.orange)
            }

            ForEach(d65ReferenceSamples) { sample in
                LineMark(
                    x: .value("波長（nm）", sample.wavelength),
                    y: .value("D65基準分光分布", sample.value),
                    series: .value("系列", "ISO 3668 D65")
                )
                .interpolationMethod(.linear)
                .lineStyle(.init(lineWidth: 2, lineCap: .round, lineJoin: .round, dash: [6, 4]))
                .foregroundStyle(Color.blue)
            }
        }
        .chartXScale(domain: (measurement?.spectrumStart ?? 380)...(measurement?.spectrumEnd ?? 730))
        .chartYScale(domain: 0...yUpperBound)
        .chartXAxis {
            AxisMarks(values: .stride(by: 50)) {
                AxisGridLine()
                AxisTick()
                AxisValueLabel()
            }
        }
        .chartYAxis {
            AxisMarks(position: .leading)
        }
        .chartXAxisLabel("(nm)", position: .bottom, alignment: .trailing, spacing: 0)
        .chartPlotStyle { plotArea in
            plotArea
                .background(SpectrumChartStyle.gradient.opacity(0.17))
                .compositingGroup()
                .clipShape(.rect(cornerRadius: 6))
        }
        .frame(height: 400)
        .accessibilityLabel("スペクトル分布グラフ")
        .accessibilityHint("波長ごとの測定値と、選択したD50またはD65の基準分光分布を表示します")
        .overlay(alignment: .topTrailing) {
            if let peakValue = measurement?.peakValue,
               let peakWavelength = measurement?.peakWavelength {
                Text("Peak \(peakValue.formatted(.number.precision(.fractionLength(2)))) @ \(peakWavelength.formatted(.number.precision(.fractionLength(1)))) nm")
                    .font(.caption.monospacedDigit())
                    .padding(.horizontal, 8)
                    .padding(.vertical, 5)
                    .background {
                        Capsule()
                            .fill(.ultraThinMaterial)
                            .opacity(0.5)
                    }
                    .padding(8)
            }
        }
    }

    private var emptyState: some View {
        ZStack {
            SpectrumChartStyle.gradient.opacity(0.12)
            ContentUnavailableView(
                calibrationCompleted ? "測定結果はまだありません" : "キャリブレーション待ち",
                systemImage: "waveform.path.ecg",
                description: Text(
                    calibrationCompleted
                        ? "測定すると、ここへスペクトルを表示します。"
                        : "測定器のキャリブレーションを完了してください。"
                )
            )
        }
        .compositingGroup()
        .clipShape(.rect(cornerRadius: 8))
        .frame(height: 400)
    }

}

enum SpectrumChartStyle {
    static var gradient: LinearGradient {
        LinearGradient(
            gradient: Gradient(stops: [
                .init(color: Color(red: 0.42, green: 0.18, blue: 0.85), location: 0.00),
                .init(color: Color(red: 0.16, green: 0.30, blue: 0.95), location: 0.17),
                .init(color: Color(red: 0.05, green: 0.72, blue: 0.95), location: 0.31),
                .init(color: Color(red: 0.12, green: 0.82, blue: 0.35), location: 0.45),
                .init(color: Color(red: 0.94, green: 0.88, blue: 0.12), location: 0.58),
                .init(color: Color(red: 1.00, green: 0.47, blue: 0.08), location: 0.72),
                .init(color: Color(red: 0.95, green: 0.08, blue: 0.12), location: 0.84),
                .init(color: Color(red: 0.55, green: 0.00, blue: 0.04), location: 1.00),
            ]),
            startPoint: .leading,
            endPoint: .trailing
        )
    }
}

private struct ReferenceSpectrumToggleLabel: View {
    let title: String
    let color: Color

    var body: some View {
        HStack(spacing: 4) {
            Capsule()
                .fill(color)
                .frame(width: 16, height: 3)
                .accessibilityHidden(true)

            Text(title)
        }
    }
}
