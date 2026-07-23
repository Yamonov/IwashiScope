import SwiftUI

struct PrintingViewingConditionEvaluationView: View {
    private let evaluation: PrintingViewingConditionEvaluation

    init(measurement: SpotMeasurement) {
        evaluation = PrintingViewingConditionEvaluator.evaluate(measurement)
    }

    var body: some View {
        GroupBox("印刷学会基準（JSPST-1998）") {
            VStack(alignment: .leading, spacing: 10) {
                summary

                Divider()

                PrintingCriterionRow(
                    label: "相関色温度",
                    value: formattedTemperature,
                    requirement: "目標 \(format(PrintingViewingConditionEvaluator.targetCorrelatedColorTemperature, digits: 0)) K（許容幅規定なし）",
                    status: nil
                )
                PrintingCriterionRow(
                    label: "D50色度差 Δu′v′",
                    value: formattedChromaticityDistance,
                    requirement: "基準 ≤ \(format(PrintingViewingConditionEvaluator.maximumChromaticityDistance, digits: 5))",
                    status: evaluation.chromaticityStatus
                )
                PrintingCriterionRow(
                    label: "平均演色評価数 Ra",
                    value: formattedAverageColorRenderingIndex,
                    requirement: "基準 ≥ \(format(PrintingViewingConditionEvaluator.minimumAverageColorRenderingIndex, digits: 1))",
                    status: evaluation.averageColorRenderingStatus
                )
                PrintingCriterionRow(
                    label: "最小Ri（R9〜R15）",
                    value: formattedMinimumSpecialColorRenderingIndex,
                    requirement: "基準 ≥ \(format(PrintingViewingConditionEvaluator.minimumSpecialColorRenderingIndex, digits: 1))",
                    status: evaluation.specialColorRenderingStatus
                )
                PrintingCriterionRow(
                    label: "作業面照度",
                    value: formattedIlluminance,
                    requirement: illuminanceRequirement,
                    status: evaluation.illuminanceStatus
                )
            }
            .padding(.top, 4)
        }
    }

    private var summary: some View {
        VStack(alignment: .leading, spacing: 4) {
            Label(summaryTitle, systemImage: summaryStatus.systemImage)
                .font(.headline)
                .foregroundStyle(summaryStatus.color)

            Text(summaryDetail)
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .accessibilityElement(children: .combine)
    }

    private var summaryStatus: PrintingViewingConditionEvaluation.Status {
        evaluation.mode == .ambient
            ? evaluation.viewingConditionStatus
            : evaluation.lightSourceStatus
    }

    private var summaryTitle: String {
        PrintingViewingConditionSummaryText.title(for: evaluation)
    }

    private var summaryDetail: String {
        if evaluation.requiresAmbientIlluminanceMeasurement {
            "D50色度と演色性を評価しています。作業面照度は環境光モードで測定してください。"
        } else if evaluation.illuminanceClassification == .displayComparison {
            "D50色度と演色性を評価しています。作業面照度はディスプレイとの比較に適しています。"
        } else if evaluation.illuminanceClassification == .tooDark {
            "作業面照度が一般的な事務作業の300 lxを下回っています。"
        } else {
            "D50色度、演色性、作業面照度の測定可能な数値項目を評価しています。"
        }
    }

    private var formattedTemperature: String {
        guard let cct = evaluation.correlatedColorTemperature else {
            return "データなし"
        }
        return "\(format(cct, digits: 0)) K"
    }

    private var formattedChromaticityDistance: String {
        guard let distance = evaluation.chromaticityDistance else {
            return "データなし"
        }
        return format(distance, digits: 5)
    }

    private var formattedAverageColorRenderingIndex: String {
        guard let ra = evaluation.averageColorRenderingIndex else {
            return "データなし"
        }
        return format(ra, digits: 1)
    }

    private var formattedMinimumSpecialColorRenderingIndex: String {
        guard let minimum = evaluation.minimumSpecialColorRenderingIndex else {
            return "データなし"
        }
        return format(minimum.value, digits: 1)
    }

    private var formattedIlluminance: String {
        if evaluation.requiresAmbientIlluminanceMeasurement {
            return "環境光モードで測定"
        }
        guard let illuminance = evaluation.illuminance else {
            return "データなし"
        }
        return "\(format(illuminance, digits: 0)) lx"
    }

    private var formattedIlluminanceRange: String {
        let range = PrintingViewingConditionEvaluator.illuminanceRange
        return "\(format(range.lowerBound, digits: 0))〜\(format(range.upperBound, digits: 0)) lx"
    }

    private var illuminanceRequirement: String {
        switch evaluation.illuminanceClassification {
        case .displayComparison:
            "ディスプレイとの比較に適している"
        case .generalOffice, .tooDark:
            "事務所衛生基準規則：一般的な事務作業 300 lx以上"
        case .printComparison, .tooBright, .unavailable:
            "基準 \(formattedIlluminanceRange)"
        }
    }

    private func format(_ value: Double, digits: Int) -> String {
        value.formatted(.number.precision(.fractionLength(digits)))
    }
}

enum PrintingViewingConditionSummaryText {
    static func title(for evaluation: PrintingViewingConditionEvaluation) -> String {
        let summaryStatus = evaluation.mode == .ambient
            ? evaluation.viewingConditionStatus
            : evaluation.lightSourceStatus

        if evaluation.mode == .ambient,
           summaryStatus != .meets,
           summaryStatus != .unavailable {
            switch evaluation.illuminanceClassification {
            case .tooDark:
                return "数値基準外（労働安全衛生規則に不適）"
            case .displayComparison, .generalOffice, .tooBright:
                return "数値基準外（印刷物同士の比較に不適）"
            case .printComparison, .unavailable:
                break
            }
        }

        return switch (evaluation.mode, summaryStatus) {
        case (.ambient, .meets):
            "数値基準に適合"
        case (.ambient, .caution),
             (.ambient, .doesNotMeet),
             (.ambient, .fails):
            "数値基準外"
        case (.ambient, .unavailable):
            "数値基準を判定できません"
        case (.emissive, .meets):
            "光源の数値基準に適合"
        case (.emissive, .caution),
             (.emissive, .doesNotMeet),
             (.emissive, .fails):
            "光源の数値基準外"
        case (.emissive, .unavailable):
            "光源の数値基準を判定できません"
        case (.reflectance, _):
            "数値基準を判定できません"
        }
    }
}

private struct PrintingCriterionRow: View {
    let label: String
    let value: String
    let requirement: String
    let status: PrintingViewingConditionEvaluation.Status?

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Text(label)
                    .foregroundStyle(.secondary)

                Spacer(minLength: 8)

                if let status {
                    Image(systemName: status.systemImage)
                        .foregroundStyle(status.color)
                        .accessibilityHidden(true)
                }

                Text(value)
                    .font(.body.monospacedDigit())
                    .multilineTextAlignment(.trailing)
                    .textSelection(.enabled)
            }

            Text(requirement)
                .font(.caption2)
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, alignment: .trailing)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(accessibilityLabel)
    }

    private var accessibilityLabel: String {
        let statusText = status.map { "、\($0.accessibilityDescription)" } ?? ""
        return "\(label)、\(value)、\(requirement)\(statusText)"
    }
}

private extension PrintingViewingConditionEvaluation.Status {
    var systemImage: String {
        switch self {
        case .meets:
            "checkmark.circle.fill"
        case .caution, .doesNotMeet:
            "exclamationmark.triangle.fill"
        case .fails:
            "xmark.circle.fill"
        case .unavailable:
            "questionmark.circle"
        }
    }

    var color: Color {
        switch self {
        case .meets:
            .green
        case .caution, .doesNotMeet:
            .orange
        case .fails:
            .red
        case .unavailable:
            .secondary
        }
    }

    var accessibilityDescription: String {
        switch self {
        case .meets:
            "基準内"
        case .caution:
            "注意"
        case .doesNotMeet:
            "基準外"
        case .fails:
            "不適合"
        case .unavailable:
            "判定不可"
        }
    }
}
