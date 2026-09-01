import Foundation
import SwiftUI

struct ReflectanceIlluminantColorComparisonView: View {
    private static let patchHeight = 200.0
    private static let minimumPatchWidth = 100.0
    private static let arrowWidth = 28.0

    @State private var appliesChromaticAdaptation = true

    let measurement: SpotMeasurement
    let source: IlluminantSpectrumDefinition?

    private var comparison: ReflectanceIlluminantColorComparisonResult? {
        ReflectanceIlluminantColorComparisonCalculator.result(
            for: measurement,
            source: source,
            appliesChromaticAdaptation: appliesChromaticAdaptation
        )
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            header

            comparisonRow
                .padding(.vertical, 2)

            Text(explanation)
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .contain)
        .accessibilityLabel("選択光源による色の見え方比較")
        .accessibilityIdentifier("reflectance-illuminant-color-comparison")
    }

    private var header: some View {
        HStack(alignment: .firstTextBaseline, spacing: 16) {
            Label("色の見え方比較", systemImage: "square.split.2x1")
                .font(.headline)

            Toggle("色順応を適用", isOn: $appliesChromaticAdaptation)
                .toggleStyle(.checkbox)
                .disabled(source == nil)
                .help("Bradford色順応変換で選択光源の白色点をD50へ合わせます")
                .accessibilityHint("オンにすると選択光源の白色点をD50へ合わせます")
                .accessibilityIdentifier("chromatic-adaptation-toggle")
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var comparisonRow: some View {
        HStack(alignment: .top, spacing: 16) {
            patchColumn(title: "計測値（D50）") {
                colorPatch(
                    lab: measurement.lab,
                    whitePoint: measurement.labWhitePoint,
                    label: "計測値の色"
                )
            }

            VStack(spacing: 8) {
                Text(" ")
                    .font(.callout.weight(.semibold))
                    .accessibilityHidden(true)
                Image(systemName: "arrow.right")
                    .font(.title2.weight(.semibold))
                    .foregroundStyle(.secondary)
                    .frame(
                        width: Self.arrowWidth,
                        height: Self.patchHeight
                    )
                    .accessibilityHidden(true)
            }
            .frame(width: Self.arrowWidth)

            patchColumn(title: simulatedPatchTitle) {
                if let comparison {
                    colorPatch(
                        lab: comparison.simulatedLab,
                        whitePoint: "D50",
                        label: simulatedPatchTitle
                    )
                } else {
                    unavailablePatch
                }
            }

            VStack(alignment: .leading, spacing: 8) {
                Text(deltaTitle)
                    .font(.callout.weight(.semibold))
                    .lineLimit(1)
                deltaMetrics
            }
            .fixedSize(horizontal: true, vertical: false)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func patchColumn<Content: View>(
        title: String,
        @ViewBuilder content: () -> Content
    ) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title)
                .font(.callout.weight(.semibold))
                .lineLimit(1)
            content()
        }
        .frame(
            minWidth: Self.minimumPatchWidth,
            maxWidth: .infinity,
            alignment: .leading
        )
        .layoutPriority(1)
    }

    @ViewBuilder
    private func colorPatch(
        lab: Vector3?,
        whitePoint: String?,
        label: String
    ) -> some View {
        if let lab,
           let managedColor = LabColorConverter.managedColor(
               lab: lab,
               whitePoint: whitePoint
           ) {
            RoundedRectangle(cornerRadius: 6)
                .fill(Color(cgColor: managedColor))
                .overlay {
                    RoundedRectangle(cornerRadius: 6)
                        .strokeBorder(.secondary.opacity(0.45), lineWidth: 1)
                }
                .frame(
                    maxWidth: .infinity,
                    minHeight: Self.patchHeight,
                    maxHeight: Self.patchHeight
                )
                .accessibilityElement(children: .ignore)
                .accessibilityLabel(label)
                .accessibilityValue(labAccessibilityValue(lab))
        } else {
            unavailablePatch
        }
    }

    private var unavailablePatch: some View {
        RoundedRectangle(cornerRadius: 6)
            .fill(.secondary.opacity(0.08))
            .overlay {
                Text(source == nil ? "参考光源を選択" : "色を計算できません")
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                    .padding()
            }
            .overlay {
                RoundedRectangle(cornerRadius: 6)
                    .strokeBorder(.secondary.opacity(0.30), lineWidth: 1)
            }
            .frame(
                maxWidth: .infinity,
                minHeight: Self.patchHeight,
                maxHeight: Self.patchHeight
            )
            .accessibilityElement(children: .ignore)
            .accessibilityLabel(
                source == nil
                    ? "比較する参考光源が選択されていません"
                    : "選択光源の色を計算できません"
            )
    }

    private var deltaMetrics: some View {
        Grid(alignment: .leading, horizontalSpacing: 10, verticalSpacing: 8) {
            deltaMetric(label: "ΔE00", formattedValue: formattedDeltaE(\.deltaE2000))
            deltaMetric(label: "ΔE76", formattedValue: formattedDeltaE(\.deltaE76))
            deltaMetric(label: "ΔL*", formattedValue: formattedSignedDelta(\.deltaL))
            deltaMetric(label: "Δa*", formattedValue: formattedSignedDelta(\.deltaA))
            deltaMetric(label: "Δb*", formattedValue: formattedSignedDelta(\.deltaB))
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 12)
        .frame(
            height: Self.patchHeight,
            alignment: .topLeading
        )
        .fixedSize(horizontal: true, vertical: false)
        .background(.secondary.opacity(0.06), in: .rect(cornerRadius: 6))
        .overlay {
            RoundedRectangle(cornerRadius: 6)
                .strokeBorder(.secondary.opacity(0.25), lineWidth: 1)
        }
        .accessibilityElement(children: .combine)
    }

    private func deltaMetric(
        label: String,
        formattedValue: String
    ) -> some View {
        GridRow(alignment: .firstTextBaseline) {
            Text(label)
                .font(.caption)
                .foregroundStyle(.secondary)
            Text(formattedValue)
                .font(.body.monospacedDigit().weight(.semibold))
        }
    }

    private var simulatedPatchTitle: String {
        guard let source else { return "選択光源" }
        return appliesChromaticAdaptation
            ? "\(source.displayName)・色順応"
            : "\(source.displayName)・光源白を保持"
    }

    private var deltaTitle: String {
        appliesChromaticAdaptation ? "色順応後の差" : "光源白保持時の差"
    }

    private var explanation: String {
        guard source != nil else {
            return "参考光源を選択すると、選択光源下の見え方と計測値との差を表示します。"
        }
        return appliesChromaticAdaptation
            ? "Bradford色順応変換で選択光源の白色点をD50へ合わせ、D50 LabでΔEを計算しています。"
            : "色順応を適用せず、選択光源の白色を保持したD50 LabでΔEを計算しています。"
    }

    private func formatDeltaE(_ value: Double) -> String {
        value.formatted(.number.precision(.fractionLength(2)))
    }

    private func formatSignedDelta(_ value: Double) -> String {
        value.formatted(
            .number
                .sign(strategy: .always())
                .precision(.fractionLength(2))
        )
    }

    private func formattedDeltaE(
        _ keyPath: KeyPath<ReflectanceIlluminantColorComparisonResult, Double>
    ) -> String {
        comparison.map { formatDeltaE($0[keyPath: keyPath]) } ?? "—"
    }

    private func formattedSignedDelta(
        _ keyPath: KeyPath<ReflectanceIlluminantColorComparisonResult, Double>
    ) -> String {
        comparison.map { formatSignedDelta($0[keyPath: keyPath]) } ?? "—"
    }

    private func labAccessibilityValue(_ lab: Vector3) -> String {
        let lightness = lab.first.formatted(.number.precision(.fractionLength(2)))
        let a = lab.second.formatted(.number.precision(.fractionLength(2)))
        let b = lab.third.formatted(.number.precision(.fractionLength(2)))
        return "Lスター \(lightness)、aスター \(a)、bスター \(b)"
    }
}
