import SwiftUI

struct AveragingMeasurementHistoryStackView: View {
    private static let cardSize = MeasurementHistoryCardMetrics.size
    private static let visibleBackCardCount = 3

    let mode: MeasurementMode
    let measurement: SpotMeasurement?
    let count: Int

    var body: some View {
        ZStack(alignment: .topLeading) {
            ForEach((1...Self.visibleBackCardCount).reversed(), id: \.self) { index in
                RoundedRectangle(cornerRadius: 10)
                    .fill(Color(nsColor: .controlBackgroundColor))
                    .frame(width: Self.cardSize.width, height: Self.cardSize.height)
                    .overlay {
                        RoundedRectangle(cornerRadius: 10)
                            .strokeBorder(Color.secondary.opacity(0.28), lineWidth: 1)
                    }
                    .offset(x: Double(index) * 4, y: Double(index) * 3)
            }

            AveragingMeasurementHistoryCardFace(
                mode: mode,
                measurement: measurement
            )
            .overlay(alignment: .bottom) {
                Text("平均化測定中")
                    .font(.system(size: 9, weight: .semibold))
                    .foregroundStyle(.white)
                    .padding(.horizontal, 6)
                    .padding(.vertical, 3)
                    .background(.black.opacity(0.62), in: .capsule)
                    .padding(.bottom, 5)
            }
        }
        .frame(
            width: Self.cardSize.width,
            height: Self.cardSize.height + Double(Self.visibleBackCardCount) * 3,
            alignment: .topLeading
        )
        .overlay(alignment: .topTrailing) {
            AverageMeasurementHistoryBadge(text: "\(count)回")
                .offset(x: 3, y: -5)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("平均化測定中、採用 \(count)回")
        .accessibilityHint("平均値の出力後、このスタックは平均測定の履歴1件に置き換わります。")
    }
}

struct AverageMeasurementHistoryBadge: View {
    let text: String

    var body: some View {
        Text(text)
            .font(.caption2.monospacedDigit().weight(.bold))
            .foregroundStyle(.white)
            .padding(.horizontal, 6)
            .padding(.vertical, 3)
            .background(Color.accentColor, in: .capsule)
            .overlay {
                Capsule()
                    .strokeBorder(.white.opacity(0.75), lineWidth: 1)
            }
            .shadow(color: .black.opacity(0.22), radius: 2, y: 1)
    }
}

private struct AveragingMeasurementHistoryCardFace: View {
    let mode: MeasurementMode
    let measurement: SpotMeasurement?

    var body: some View {
        Group {
            if mode == .reflectance {
                reflectanceFace
            } else {
                lightingFace
            }
        }
        .frame(
            width: MeasurementHistoryCardMetrics.size.width,
            height: MeasurementHistoryCardMetrics.size.height
        )
        .background(Color.secondary.opacity(0.055))
        .overlay {
            RoundedRectangle(cornerRadius: 10)
                .strokeBorder(Color.accentColor.opacity(0.8), lineWidth: 2)
        }
        .compositingGroup()
        .clipShape(.rect(cornerRadius: 10))
    }

    private var reflectanceFace: some View {
        VStack(spacing: 0) {
            Rectangle()
                .fill(reflectanceColor)
                .frame(height: MeasurementHistoryCardMetrics.colorAreaHeight)

            HStack(spacing: 5) {
                labMetric("L*", measurement?.lab?.first)
                labMetric("a*", measurement?.lab?.second)
                labMetric("b*", measurement?.lab?.third)
            }
            .padding(.horizontal, 6)
            .padding(.vertical, 3)
        }
    }

    private var lightingFace: some View {
        VStack(spacing: 0) {
            AveragingSpectrumThumbnail(samples: measurement?.spectrum ?? [])
                .frame(height: 70)

            Divider()

            HStack(spacing: 6) {
                lightingMetric("Ra", measurement?.cri?.ra)
                lightingMetric("Rf", measurement?.tm30?.fidelityIndex)
            }
            .padding(.horizontal, 7)
            .frame(maxHeight: .infinity)
        }
    }

    private var reflectanceColor: Color {
        guard let measurement,
              let lab = measurement.lab,
              let conversion = LabColorConverter.convert(
                  lab: lab,
                  whitePoint: measurement.labWhitePoint
              ) else {
            return Color.secondary.opacity(0.15)
        }
        return Color(cgColor: conversion.managedColor)
    }

    private func labMetric(_ label: String, _ value: Double?) -> some View {
        VStack(alignment: .leading, spacing: 1) {
            Text(label)
                .font(.caption2)
                .foregroundStyle(.secondary)
            Text(value?.formatted(.number.precision(.fractionLength(1))) ?? "—")
                .font(.caption2.monospacedDigit())
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func lightingMetric(_ label: String, _ value: Double?) -> some View {
        VStack(spacing: 0) {
            Text(label)
                .font(.caption2)
                .foregroundStyle(.secondary)
            Text(value?.formatted(.number.precision(.fractionLength(1))) ?? "—")
                .font(.caption.monospacedDigit().weight(.medium))
        }
        .frame(maxWidth: .infinity)
    }
}

private struct AveragingSpectrumThumbnail: View {
    let samples: [SpectralSample]

    var body: some View {
        GeometryReader { geometry in
            ZStack {
                SpectrumChartStyle.gradient.opacity(0.16)

                if samples.isEmpty {
                    Image(systemName: "waveform.path.ecg")
                        .foregroundStyle(.secondary)
                } else {
                    Path { path in
                        let maximumValue = max(
                            samples.lazy.map(\.value).max() ?? 0,
                            1e-12
                        )
                        let denominator = Double(max(samples.count - 1, 1))
                        for (index, sample) in samples.enumerated() {
                            let x = geometry.size.width * Double(index) / denominator
                            let normalizedValue = max(0, sample.value / maximumValue)
                            let y = geometry.size.height * (1 - normalizedValue)
                            let point = CGPoint(x: x, y: y)
                            if index == 0 {
                                path.move(to: point)
                            } else {
                                path.addLine(to: point)
                            }
                        }
                    }
                    .stroke(.primary.opacity(0.72), lineWidth: 1.25)
                    .padding(5)
                }
            }
        }
        .accessibilityHidden(true)
    }
}
