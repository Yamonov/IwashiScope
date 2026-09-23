import Charts
import SwiftUI

struct ReflectanceIlluminantSpectrumChart: View {
    let result: ReflectanceIlluminantSpectrumResult
    let showsCIE2006LMS: Bool

    var body: some View {
        let spectrumGradient = SpectrumChartStyle.gradient(for: result.wavelengthRange)
        let yAxisScale = SpectrumYAxisScale.resolve(
            automaticUpperBound: result.automaticUpperBound,
            configuration: SpectrumYAxisConfiguration(mode: .automatic, fixedUpperBound: 100)
        )

        Chart {
            ForEach(result.reflectedLight) { sample in
                AreaMark(
                    x: .value("波長（nm）", sample.wavelength),
                    yStart: .value("基準", 0.0),
                    yEnd: .value("反射光相対値", sample.value)
                )
                .interpolationMethod(.linear)
                .foregroundStyle(spectrumGradient)
                .alignsMarkStylesWithPlotArea()
            }

            ForEach(result.measuredReflectance) { sample in
                LineMark(
                    x: .value("波長（nm）", sample.wavelength),
                    y: .value("計測反射率（%）", sample.value),
                    series: .value("系列", "計測反射率")
                )
                .interpolationMethod(.linear)
                .lineStyle(.init(lineWidth: 2.2, lineCap: .round, lineJoin: .round))
                .foregroundStyle(Color.black)
            }

            ForEach(result.illuminant) { sample in
                LineMark(
                    x: .value("波長（nm）", sample.wavelength),
                    y: .value("光源相対値", sample.value),
                    series: .value("系列", "選択光源")
                )
                .interpolationMethod(.linear)
                .lineStyle(.init(lineWidth: 2.4, lineCap: .round, lineJoin: .round, dash: [7, 4]))
                .foregroundStyle(Color.yellow)
            }
        }
        .chartXScale(domain: result.wavelengthRange)
        .chartYScale(domain: 0...yAxisScale.upperBound)
        .chartXAxis {
            AxisMarks(values: SpectrumChartScale.axisValues(for: result.wavelengthRange)) {
                AxisGridLine()
                AxisTick()
                AxisValueLabel()
            }
        }
        .chartYAxis {
            AxisMarks(position: .leading, values: yAxisScale.tickValues) { value in
                AxisGridLine()
                AxisTick()
                if let tickValue = value.as(Double.self) {
                    AxisValueLabel {
                        Text(tickValue.formatted(.number.precision(.fractionLength(0))))
                    }
                }
            }
        }
        .chartXAxisLabel("(nm)", position: .bottom, alignment: .trailing, spacing: 0)
        .chartLegend(.hidden)
        .chartPlotStyle { plotArea in
            plotArea
                .background(spectrumGradient.opacity(0.17))
                .compositingGroup()
                .clipShape(.rect(cornerRadius: 6))
        }
        .chartOverlay { proxy in
            if showsCIE2006LMS {
                CIE2006LMSChartOverlay(
                    proxy: proxy,
                    curves: CIE2006LMSReference.curves(in: result.wavelengthRange),
                    yUpperBound: yAxisScale.upperBound
                )
            }
        }
        .frame(height: 400)
        .accessibilityLabel("光源による反射光スペクトルグラフ")
        .accessibilityHint(
            result.illuminant.isEmpty
                ? "波長ごとの計測反射率を表示します"
                : "計測反射率、選択光源、光源を適用した反射光の相対分光分布を表示します"
        )
        .accessibilityValue(showsCIE2006LMS ? "CIE 2006 LMSの参考曲線を表示中。" : "")
    }
}

/// An overlay, not chart data: always in front and never included in the spectral scale.
struct CIE2006LMSChartOverlay: View {
    let proxy: ChartProxy
    let curves: [CIE2006LMSReference.Curve]
    let yUpperBound: Double
    var roundsPlotAreaCorners = true

    var body: some View {
        GeometryReader { geometry in
            if let plotAnchor = proxy.plotFrame {
                let plotFrame = geometry[plotAnchor]
                ZStack(alignment: .topLeading) {
                    ForEach(curves) { curve in
                        Path { path in
                            for sample in curve.samples {
                                if let x = proxy.position(forX: sample.wavelength),
                                   let y = proxy.position(forY: sample.value * yUpperBound) {
                                    let point = CGPoint(x: x, y: y)
                                    if path.isEmpty {
                                        path.move(to: point)
                                    } else {
                                        path.addLine(to: point)
                                    }
                                }
                            }
                        }
                        .stroke(
                            Color.gray.opacity(CIE2006LMSReference.strokeOpacity),
                            style: StrokeStyle(lineWidth: 2, lineCap: .round, lineJoin: .round)
                        )
                    }
                }
                .frame(width: plotFrame.width, height: plotFrame.height)
                .compositingGroup()
                .clipShape(.rect(cornerRadius: roundsPlotAreaCorners ? 6 : 0))
                .offset(x: plotFrame.minX, y: plotFrame.minY)
            }
        }
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }
}
