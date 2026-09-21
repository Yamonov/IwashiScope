import Foundation
import SwiftUI

struct ReflectanceIlluminantColorComparisonView: View {
    private static let patchHeight = 200.0
    private static let minimumPatchWidth = 100.0
    private static let arrowWidth = 28.0

    @State private var showsCalculationDetails = false
    @Binding var method: ReflectanceAppearanceMethod

    let measurement: SpotMeasurement
    let source: IlluminantSpectrumDefinition?

    private let referenceLab: Vector3?
    private let comparison: ReflectanceIlluminantColorComparisonResult?
    private let computationError: String?
    private let exceedsSRGB: Bool

    init(
        measurement: SpotMeasurement,
        source: IlluminantSpectrumDefinition?,
        method: Binding<ReflectanceAppearanceMethod>
    ) {
        self.measurement = measurement
        self.source = source
        self._method = method
        var calculatedReferenceLab: Vector3?
        var calculatedComparison: ReflectanceIlluminantColorComparisonResult?
        var calculationError: String?
        do {
            calculatedReferenceLab = try ReflectanceIlluminantColorComparisonCalculator.referenceLab(for: measurement)
            if let source {
                let result = try ReflectanceIlluminantColorComparisonCalculator.compare(
                    measurement: measurement, source: source, method: method.wrappedValue
                )
                calculatedComparison = result
                // A selected source may have narrower coverage. Use the common-range
                // baseline for both the displayed patch and its numerical differences.
                calculatedReferenceLab = result.referenceLab
            }
        } catch {
            calculationError = error.localizedDescription
        }
        referenceLab = calculatedReferenceLab
        comparison = calculatedComparison
        computationError = calculationError
        exceedsSRGB = [calculatedReferenceLab, calculatedComparison?.simulatedLab]
            .compactMap { $0 }
            .contains {
                LabColorConverter.displaySRGB(lab: $0, whitePoint: "D50")?.isOutOfGamut == true
            }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            header

            comparisonRow
                .padding(.vertical, 2)

            Text(explanation)
                .font(.caption)
                .foregroundStyle(.secondary)

            if let computationError {
                Text(computationError)
                    .font(.caption)
                    .foregroundStyle(.orange)
                    .accessibilityIdentifier("appearance-calculation-error")
            }
            if exceedsSRGB {
                Text("sRGB色域外の予測値があります。画面での再現はディスプレイの色域に依存します。")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            if let comparison {
                DisclosureGroup("計算条件", isExpanded: $showsCalculationDetails) {
                    ReflectanceAppearanceDetailsView(comparison: comparison)
                        .padding(.top, 4)
                }
                .font(.caption)
                .accessibilityIdentifier("appearance-calculation-details")
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .contain)
        .accessibilityLabel("選択光源による色の見え方比較")
        .accessibilityIdentifier("reflectance-illuminant-color-comparison")
    }

    private var header: some View {
        ViewThatFits(in: .horizontal) {
            HStack(alignment: .firstTextBaseline, spacing: 16) {
                Label("色の見え方比較", systemImage: "square.split.2x1")
                    .font(.headline)
                methodPicker
            }
            VStack(alignment: .leading, spacing: 8) {
                Label("色の見え方比較", systemImage: "square.split.2x1")
                    .font(.headline)
                methodPicker
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var methodPicker: some View {
        Picker("色順応", selection: $method) {
            ForEach(ReflectanceAppearanceMethod.allCases) { method in
                Text(method.title).tag(method)
            }
        }
        .pickerStyle(.menu)
        .frame(maxWidth: 300)
        .disabled(source == nil)
        .accessibilityLabel("色の見え方の計算方式")
        .accessibilityIdentifier("appearance-method-picker")
    }

    private var comparisonRow: some View {
        HStack(alignment: .top, spacing: 16) {
            patchColumn(title: String(localized: "基準（D50・再計算）")) {
                colorPatch(
                    lab: referenceLab,
                    whitePoint: "D50",
                    label: "D50基準の再計算色"
                )
                .accessibilityIdentifier("appearance-reference-patch")
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
                    unavailablePatch(
                        message: source == nil ? "光源を選択してください" : "色を計算できません",
                        accessibilityLabel: source == nil
                            ? "光源を選択してください"
                            : "選択光源の色を計算できません"
                    )
                }
            }
            .accessibilityIdentifier("appearance-simulated-patch")

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
            unavailablePatch(
                message: "色を計算できません",
                accessibilityLabel: "色を計算できません"
            )
        }
    }

    private func unavailablePatch(
        message: LocalizedStringKey,
        accessibilityLabel: LocalizedStringKey
    ) -> some View {
        RoundedRectangle(cornerRadius: 6)
            .fill(.secondary.opacity(0.08))
            .overlay {
                Text(message)
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
            .accessibilityLabel(accessibilityLabel)
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
        return source.displayName
    }

    private var deltaTitle: String {
        String(localized: "予測色差")
    }

    private var explanation: String {
        guard source != nil else {
            return String(localized: "参考光源を選択すると、選択光源下の見え方とD50基準との差を表示します。")
        }
        switch method {
        case .unadapted:
            return String(localized: "色順応を適用せず、選択光源の色味を含む反射光をD50基準で表示します。")
        case .bradford:
            return String(localized: "Bradford変換で選択光源の白色点をD50へ完全に合わせた従来方式です。")
        case .ciecam16:
            return String(localized: "基準白の輝度をそろえた想定条件で、明るさ・色相・彩度を予測します。計算後の明るさや彩度は固定していません。")
        }
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
