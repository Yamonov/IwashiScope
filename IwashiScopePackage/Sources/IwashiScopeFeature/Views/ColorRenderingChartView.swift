import Charts
import Foundation
import SwiftUI

struct ColorRenderingChartView: View {
    let measurement: SpotMeasurement?

    @ScaledMetric(relativeTo: .title2) private var raRangeIndicatorHeight = 28.0

    private static let scoreLabels = (1...15).map { "R\($0)" }

    private struct Score: Identifiable {
        let index: Int
        let value: Double

        var id: Int { index }
        var label: String { "R\(index)" }
    }

    private var scores: [Score] {
        guard let individual = measurement?.cri?.individual else {
            return []
        }

        return (1...15).compactMap { index in
            individual[index].map { Score(index: index, value: $0) }
        }
    }

    private var yLowerBound: Double {
        let minimum = scores.lazy.map(\.value).min() ?? 0
        guard minimum < 0 else { return 0 }
        return floor((minimum - 10) / 10) * 10
    }

    private var yUpperBound: Double {
        let maximum = scores.lazy.map(\.value).max() ?? 100
        return max(100, ceil(maximum / 10) * 10)
    }

    private var minorYAxisValues: [Double] {
        stride(from: yLowerBound, through: yUpperBound, by: 10).filter { value in
            !Int((value / 10).rounded()).isMultiple(of: 2)
        }
    }

    private var includesCompleteRaRange: Bool {
        scores.prefix(8).map(\.index) == Array(1...8)
    }

    var body: some View {
        GroupBox {
            if scores.isEmpty {
                emptyState
            } else {
                chart
            }
        } label: {
            Label("演色評価数（Color Rendering Index / CRI）", systemImage: "chart.bar.fill")
        }
        .frame(maxWidth: .infinity)
    }

    private var chart: some View {
        Chart {
            ForEach(scores) { score in
                BarMark(
                    x: .value("試験色", score.label),
                    y: .value("演色評価数", score.value)
                )
                .foregroundStyle(ColorRenderingChartPalette.color(for: score.index).gradient)
                .cornerRadius(3)
                .accessibilityLabel(score.label)
                .accessibilityValue(format(score.value))
            }

            RuleMark(y: .value("基準", 80))
                .lineStyle(.init(lineWidth: 1, dash: [4, 4]))
                .foregroundStyle(.secondary.opacity(0.35))
        }
        .chartXScale(domain: Self.scoreLabels)
        .chartYScale(
            domain: yLowerBound...yUpperBound,
            range: .plotDimension(endPadding: raRangeIndicatorHeight + 4)
        )
        .chartXAxis {
            AxisMarks(values: Self.scoreLabels) {
                AxisValueLabel()
                AxisTick()
            }
        }
        .chartYAxis {
            AxisMarks(position: .leading, values: minorYAxisValues) {
                AxisGridLine(stroke: StrokeStyle(lineWidth: 0.5))
                    .foregroundStyle(.secondary.opacity(0.18))
            }

            AxisMarks(position: .leading, values: .stride(by: 20)) {
                AxisGridLine()
                AxisTick()
                AxisValueLabel()
            }
        }
        .chartOverlay { proxy in
            chartAnnotations(proxy: proxy)
        }
        .frame(height: LightingRenderingLayout.contentHeight)
        .accessibilityLabel("R1からR15までの演色評価棒グラフ")
        .accessibilityHint("RaはR1からR8までの平均です。棒の高さと数値は各試験色の演色評価数を表します")
    }

    private func chartAnnotations(proxy: ChartProxy) -> some View {
        GeometryReader { geometry in
            if let plotFrame = proxy.plotFrame,
               let baselineY = proxy.position(forY: 0) {
                let plotRectangle = geometry[plotFrame]

                if includesCompleteRaRange,
                   let firstRange = proxy.positionRange(forX: "R1"),
                   let lastRange = proxy.positionRange(forX: "R8"),
                   let placement = ColorRenderingRaRangePlacement.resolve(
                       firstCategoryRange: firstRange,
                       lastCategoryRange: lastRange,
                       plotRectangle: plotRectangle,
                       indicatorHeight: raRangeIndicatorHeight
                   ) {
                    RaRangeIndicator(
                        label: ColorRenderingRaRangeLabel.text(
                            for: measurement?.cri?.ra
                        )
                    )
                        .frame(width: placement.width, height: placement.height)
                        .position(x: placement.centerX, y: placement.centerY)
                        .accessibilityHidden(true)
                }

                ForEach(scores) { score in
                    if let x = proxy.position(forX: score.label),
                       let tipY = proxy.position(forY: score.value) {
                        let placement = ColorRenderingValueLabelPlacement.resolve(
                            tipY: plotRectangle.minY + tipY,
                            baselineY: plotRectangle.minY + baselineY
                        )

                        Text(format(score.value))
                            .font(.system(size: 10, weight: .bold).monospacedDigit())
                            .foregroundStyle(
                                placement.location == .inside ? Color.white : Color.black
                            )
                            .position(
                                x: plotRectangle.minX + x,
                                y: placement.centerY
                            )
                            .accessibilityHidden(true)
                    }
                }
            }
        }
        .allowsHitTesting(false)
    }

    private var emptyState: some View {
        ContentUnavailableView(
            "演色評価を待っています",
            systemImage: "chart.bar",
            description: Text("光源を測定するとR1〜R15をここへ表示します。")
        )
        .frame(
            maxWidth: .infinity,
            minHeight: LightingRenderingLayout.contentHeight,
            maxHeight: LightingRenderingLayout.contentHeight
        )
    }

    private func format(_ value: Double) -> String {
        value.formatted(.number.precision(.fractionLength(1)))
    }

}

enum ColorRenderingChartPalette {
    static func color(for index: Int) -> Color {
        switch index {
        case 1: Color(red: 0.72, green: 0.48, blue: 0.46)
        case 2: Color(red: 0.78, green: 0.64, blue: 0.27)
        case 3: Color(red: 0.56, green: 0.70, blue: 0.27)
        case 4: Color(red: 0.25, green: 0.66, blue: 0.48)
        case 5: Color(red: 0.20, green: 0.66, blue: 0.72)
        case 6: Color(red: 0.24, green: 0.50, blue: 0.83)
        case 7: Color(red: 0.47, green: 0.37, blue: 0.78)
        case 8: Color(red: 0.72, green: 0.35, blue: 0.68)
        case 9: Color(red: 0.88, green: 0.20, blue: 0.20)
        case 10: Color(red: 0.89, green: 0.68, blue: 0.16)
        case 11: Color(red: 0.22, green: 0.65, blue: 0.30)
        case 12: Color(red: 0.18, green: 0.38, blue: 0.80)
        case 13: Color(red: 0.86, green: 0.53, blue: 0.43)
        case 14: Color(red: 0.64, green: 0.61, blue: 0.22)
        case 15: Color(red: 0.79, green: 0.43, blue: 0.32)
        default: .secondary
        }
    }
}

enum ColorRenderingRaRangeLabel {
    static func text(
        for value: Double?,
        locale: Locale = .current
    ) -> String {
        guard let value else { return "Ra" }
        let formattedValue = value.formatted(
            .number
                .locale(locale)
                .precision(.fractionLength(1))
        )
        return "Ra \(formattedValue)"
    }
}

private struct RaRangeIndicator: View {
    let label: String

    var body: some View {
        HStack(spacing: 9) {
            RaRangeBracketSegment(capEdge: .leading)
                .stroke(
                    Color.accentColor,
                    style: StrokeStyle(lineWidth: 1.5, lineCap: .round, lineJoin: .round)
                )

            Text(label)
                .font(.title2)
                .foregroundStyle(.tint)

            RaRangeBracketSegment(capEdge: .trailing)
                .stroke(
                    Color.accentColor,
                    style: StrokeStyle(lineWidth: 1.5, lineCap: .round, lineJoin: .round)
                )
        }
    }
}

private struct RaRangeBracketSegment: Shape {
    let capEdge: Edge

    func path(in rect: CGRect) -> Path {
        let lineY = rect.minY + (rect.height * 0.42)
        var path = Path()

        switch capEdge {
        case .leading:
            path.move(to: CGPoint(x: rect.minX, y: rect.maxY))
            path.addLine(to: CGPoint(x: rect.minX, y: lineY))
            path.addLine(to: CGPoint(x: rect.maxX, y: lineY))
        case .trailing:
            path.move(to: CGPoint(x: rect.minX, y: lineY))
            path.addLine(to: CGPoint(x: rect.maxX, y: lineY))
            path.addLine(to: CGPoint(x: rect.maxX, y: rect.maxY))
        default:
            break
        }

        return path
    }
}

struct ColorRenderingRaRangePlacement: Equatable {
    let centerX: CGFloat
    let centerY: CGFloat
    let width: CGFloat
    let height: CGFloat

    static func resolve(
        firstCategoryRange: ClosedRange<CGFloat>,
        lastCategoryRange: ClosedRange<CGFloat>,
        plotRectangle: CGRect,
        indicatorHeight: CGFloat = 28
    ) -> Self? {
        let startX = max(
            plotRectangle.minX,
            plotRectangle.minX + firstCategoryRange.lowerBound
        )
        let endX = min(
            plotRectangle.maxX,
            plotRectangle.minX + lastCategoryRange.upperBound
        )
        guard endX > startX else { return nil }

        return Self(
            centerX: (startX + endX) / 2,
            centerY: plotRectangle.minY + (indicatorHeight / 2),
            width: endX - startX,
            height: indicatorHeight
        )
    }
}

struct ColorRenderingValueLabelPlacement: Equatable {
    enum Location: Equatable {
        case inside
        case outside
    }

    let location: Location
    let centerY: CGFloat

    static func resolve(
        tipY: CGFloat,
        baselineY: CGFloat,
        labelHeight: CGFloat = 13,
        tipMargin: CGFloat = 6,
        baselineMargin: CGFloat = 4
    ) -> Self {
        let barHeight = abs(baselineY - tipY)
        let fitsInside = barHeight >= tipMargin + labelHeight + baselineMargin
        let directionIntoBar: CGFloat = tipY <= baselineY ? 1 : -1
        let labelOffset = tipMargin + (labelHeight / 2)
        let direction = fitsInside ? directionIntoBar : -directionIntoBar

        return Self(
            location: fitsInside ? .inside : .outside,
            centerY: tipY + (direction * labelOffset)
        )
    }
}
