import Charts
import Foundation
import SwiftUI

enum TM30SampleFidelity {
    static let scalingFactor = 7.54

    static func score(reference: Vector3, test: Vector3) -> Double? {
        let values = [
            reference.first,
            reference.second,
            reference.third,
            test.first,
            test.second,
            test.third
        ]
        guard values.allSatisfy(\.isFinite) else { return nil }

        let deltaJ = test.first - reference.first
        let deltaA = test.second - reference.second
        let deltaB = test.third - reference.third
        let deltaE = hypot(hypot(deltaJ, deltaA), deltaB)
        // Keep the per-sample scale identical to ArgyllCMS icx_IES_TM_30_15.
        let untransformedScore = max(100 - scalingFactor * deltaE, 0)
        let transformedScore = 10 * log1p(exp(untransformedScore / 10))
        return min(max(transformedScore, 0), 100)
    }
}

struct TM30PatchVisualColor: Equatable, Sendable {
    let hue: Double
    let saturation: Double
    let brightness: Double

    static func make(from jab: Vector3) -> Self? {
        guard jab.first.isFinite,
              jab.second.isFinite,
              jab.third.isFinite else {
            return nil
        }

        let chroma = hypot(jab.second, jab.third)
        let rawHue = atan2(jab.third, jab.second) / (2 * Double.pi)
        let hue = rawHue < 0 ? rawHue + 1 : rawHue
        return Self(
            hue: chroma < 1e-12 ? 0 : hue,
            saturation: min(max(chroma / 45, 0), 0.85),
            brightness: min(max(0.22 + 0.78 * jab.first / 100, 0.18), 1)
        )
    }

    var color: Color {
        Color(hue: hue, saturation: saturation, brightness: brightness)
    }
}

struct TM30SampleFidelityPoint: Identifiable, Equatable, Sendable {
    static let halfBarWidth = 0.43

    let index: Int
    let fidelity: Double
    let visualColor: TM30PatchVisualColor

    var id: Int { index }
    var chartIndex: Double { Double(index) }
    var xStart: Double { chartIndex - Self.halfBarWidth }
    var xEnd: Double { chartIndex + Self.halfBarWidth }
}

enum TM30SampleFidelitySeries {
    static func make(samples: [TM30EvaluationSample]) -> [TM30SampleFidelityPoint] {
        samples.compactMap { sample in
            guard let fidelity = TM30SampleFidelity.score(
                reference: sample.referenceJab,
                test: sample.testJab
            ), let visualColor = TM30PatchVisualColor.make(from: sample.referenceJab) else {
                return nil
            }
            return TM30SampleFidelityPoint(
                index: sample.index,
                fidelity: fidelity,
                visualColor: visualColor
            )
        }
        .sorted { $0.index < $1.index }
    }
}

enum TM30SampleFidelityChartScale {
    static let xDomain: ClosedRange<Double> = 0.5...99.5
    static let yDomain: ClosedRange<Double> = 0...100
    static let xAxisValues: [Double] = [1, 20, 40, 60, 80, 99]
    static let yAxisValues: [Double] = [0, 20, 40, 60, 80, 100]
}

struct TM30SampleFidelityChartView: View {
    private let points: [TM30SampleFidelityPoint]

    init(samples: [TM30EvaluationSample]) {
        points = TM30SampleFidelitySeries.make(samples: samples)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("99色評価用試料")
                .font(.caption.weight(.semibold))

            Chart(points) { point in
                RectangleMark(
                    xStart: .value("試料範囲の始点", point.xStart),
                    xEnd: .value("試料範囲の終点", point.xEnd),
                    yStart: .value("忠実度の下限", 0.0),
                    yEnd: .value("試料別忠実度", point.fidelity)
                )
                .foregroundStyle(point.visualColor.color)
            }
            .chartXScale(domain: TM30SampleFidelityChartScale.xDomain)
            .chartYScale(domain: TM30SampleFidelityChartScale.yDomain)
            .chartXAxis {
                AxisMarks(values: TM30SampleFidelityChartScale.xAxisValues) { value in
                    AxisGridLine()
                        .foregroundStyle(.secondary.opacity(0.12))
                    AxisTick()
                    AxisValueLabel {
                        if let index = value.as(Double.self) {
                            Text(index.formatted(.number.precision(.fractionLength(0))))
                        }
                    }
                }
            }
            .chartYAxis {
                AxisMarks(values: TM30SampleFidelityChartScale.yAxisValues) {
                    AxisGridLine()
                        .foregroundStyle(.secondary.opacity(0.18))
                    AxisTick()
                    AxisValueLabel()
                }
            }
            .chartXAxisLabel("色評価用試料（CES）", alignment: .center)
            .chartYAxisLabel("Rf,CES")
            .chartLegend(.hidden)
            .chartPlotStyle { plotArea in
                plotArea.background(.quaternary.opacity(0.10))
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
        .accessibilityLabel("TM-30 99色評価用試料の試料別忠実度")
        .accessibilityHint("99本の棒が各色評価用試料のRf,CESを0から100で示します")
    }
}
