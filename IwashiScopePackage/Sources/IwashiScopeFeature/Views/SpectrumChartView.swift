import Charts
import Foundation
import SwiftUI

enum SpectrumYAxisMode: String, CaseIterable, Sendable {
    case automatic
    case fixed
}

struct SpectrumYAxisConfiguration: Equatable, Sendable {
    static let fixedUpperBoundRange = 10.0...500.0
    static let fixedUpperBoundStep = 10.0

    var mode: SpectrumYAxisMode
    var fixedUpperBound: Double

    static func initial(for measurementMode: MeasurementMode) -> Self {
        switch measurementMode {
        case .reflectance:
            Self(mode: .fixed, fixedUpperBound: 100)
        case .ambient, .emissive:
            Self(mode: .automatic, fixedUpperBound: 200)
        }
    }

    static var initialByMeasurementMode: [MeasurementMode: Self] {
        Dictionary(
            uniqueKeysWithValues: MeasurementMode.allCases.map { mode in
                (mode, initial(for: mode))
            }
        )
    }

    var normalizedFixedUpperBound: Double {
        guard fixedUpperBound.isFinite else {
            return Self.fixedUpperBoundRange.lowerBound
        }
        let clamped = min(
            max(fixedUpperBound, Self.fixedUpperBoundRange.lowerBound),
            Self.fixedUpperBoundRange.upperBound
        )
        return (clamped / Self.fixedUpperBoundStep).rounded()
            * Self.fixedUpperBoundStep
    }

}

struct SpectrumYAxisScale: Equatable, Sendable {
    let upperBound: Double
    let tickValues: [Double]

    static func resolve(
        automaticUpperBound: Double,
        configuration: SpectrumYAxisConfiguration
    ) -> Self {
        switch configuration.mode {
        case .fixed:
            let upperBound = configuration.normalizedFixedUpperBound
            let tickStep = upperBound / 5
            return Self(
                upperBound: upperBound,
                tickValues: (0...5).map { Double($0) * tickStep }
            )
        case .automatic:
            let requiredUpperBound = automaticUpperBound.isFinite
                ? max(1, automaticUpperBound)
                : 1
            let tickStep = automaticTickStep(for: requiredUpperBound)
            let intervalCount = max(
                1,
                Int(ceil(requiredUpperBound / tickStep))
            )
            let upperBound = Double(intervalCount) * tickStep
            return Self(
                upperBound: upperBound,
                tickValues: (0...intervalCount).map {
                    Double($0) * tickStep
                }
            )
        }
    }

    private static func automaticTickStep(for upperBound: Double) -> Double {
        let desiredStep = max(1, upperBound / 5)
        let exponent = Int(floor(log10(desiredStep)))
        let multipliers = [1.0, 2.0, 2.5, 5.0, 10.0]
        var candidates = Set<Double>()

        for candidateExponent in (exponent - 1)...(exponent + 1) {
            let magnitude = pow(10, Double(candidateExponent))
            for multiplier in multipliers {
                let candidate = multiplier * magnitude
                let integerCandidate = candidate.rounded()
                let tolerance = max(1, abs(candidate)) * 0.000_001
                if candidate.isFinite,
                   integerCandidate >= 1,
                   abs(candidate - integerCandidate) <= tolerance {
                    candidates.insert(integerCandidate)
                }
            }
        }

        return candidates.sorted().min { lhs, rhs in
            automaticTickScore(step: lhs, upperBound: upperBound)
                < automaticTickScore(step: rhs, upperBound: upperBound)
        } ?? ceil(desiredStep)
    }

    private static func automaticTickScore(
        step: Double,
        upperBound: Double
    ) -> Double {
        let intervalCount = max(1, Int(ceil(upperBound / step)))
        let outsidePreferredRange: Int
        if intervalCount < 3 {
            outsidePreferredRange = 3 - intervalCount
        } else if intervalCount > 6 {
            outsidePreferredRange = intervalCount - 6
        } else {
            outsidePreferredRange = 0
        }
        let intervalPenalty = Double(outsidePreferredRange * 100)
        let targetPenalty = Double(abs(intervalCount - 5) * 10)
        let resolvedUpperBound = Double(intervalCount) * step
        let headroomPenalty = (resolvedUpperBound - upperBound)
            / max(upperBound, 1)
        return intervalPenalty + targetPenalty + headroomPenalty
    }
}

struct SpectrumChartView: View {
    let mode: MeasurementMode
    let measurement: SpotMeasurement?
    let measurementName: String?
    let calibrationCompleted: Bool
    let showsReferenceControls: Bool
    let usesPracticalSpectrumRange: Bool
    let yAxisConfiguration: SpectrumYAxisConfiguration
    let roundsPlotAreaCorners: Bool

    @State private var showsD50Reference = false
    @State private var showsD65Reference = false
    @State private var hoveredWavelength: Double?

    init(
        mode: MeasurementMode,
        measurement: SpotMeasurement?,
        measurementName: String? = nil,
        calibrationCompleted: Bool,
        showsReferenceControls: Bool = true,
        usesPracticalSpectrumRange: Bool = false,
        yAxisConfiguration: SpectrumYAxisConfiguration,
        roundsPlotAreaCorners: Bool = true,
        initialShowsD50Reference: Bool = false,
        initialShowsD65Reference: Bool = false
    ) {
        self.mode = mode
        self.measurement = measurement
        self.measurementName = measurementName
        self.calibrationCompleted = calibrationCompleted
        self.showsReferenceControls = showsReferenceControls
        self.usesPracticalSpectrumRange = usesPracticalSpectrumRange
        self.yAxisConfiguration = yAxisConfiguration
        self.roundsPlotAreaCorners = roundsPlotAreaCorners
        _showsD50Reference = State(initialValue: initialShowsD50Reference)
        _showsD65Reference = State(initialValue: initialShowsD65Reference)
    }

    private var allSamples: [SpectralSample] {
        measurement?.spectrum ?? []
    }

    private var samples: [SpectralSample] {
        samplesWithinDisplayRange(allSamples)
    }

    private var displayRange: ClosedRange<Double> {
        if usesPracticalSpectrumRange,
           let range = measurement?.validatedPracticalSpectrumRange {
            return range.closedRange
        }
        guard let measurement,
              measurement.spectrumStart.isFinite,
              measurement.spectrumEnd.isFinite,
              measurement.spectrumStart <= measurement.spectrumEnd else {
            return SpectrumChartStyle.visibleSpectrumRange
        }
        return measurement.spectrumStart...measurement.spectrumEnd
    }

    private var visiblePeak: SpectralSample? {
        samples.max { lhs, rhs in
            lhs.value < rhs.value
        }
    }

    private var hoveredSample: SpectralSample? {
        guard let hoveredWavelength,
              displayRange.contains(hoveredWavelength) else {
            return nil
        }
        return SpectrumChartInteraction.nearestSample(
            to: hoveredWavelength,
            in: samples
        )
    }

    private var d50ReferenceSamples: [SpectralSample] {
        guard showsD50Reference else { return [] }
        return samplesWithinDisplayRange(
            SpectrumOverlayNormalizer.scale(
                referenceSamples: CIEStandardIlluminant.d50.samples,
                to: allSamples
            )
        )
    }

    private var d65ReferenceSamples: [SpectralSample] {
        guard showsD65Reference else { return [] }
        return samplesWithinDisplayRange(
            SpectrumOverlayNormalizer.scale(
                referenceSamples: CIEStandardIlluminant.d65.samples,
                to: allSamples
            )
        )
    }

    private var yAxisScale: SpectrumYAxisScale {
        let maximum = (samples + d50ReferenceSamples + d65ReferenceSamples)
            .lazy
            .map(\.value)
            .max() ?? 1
        let automaticUpperBound = max(1, maximum * 1.08)
        return SpectrumYAxisScale.resolve(
            automaticUpperBound: automaticUpperBound,
            configuration: yAxisConfiguration
        )
    }

    private func samplesWithinDisplayRange(
        _ source: [SpectralSample]
    ) -> [SpectralSample] {
        let tolerance = 0.000_001
        return source.filter { sample in
            sample.wavelength >= displayRange.lowerBound - tolerance
                && sample.wavelength <= displayRange.upperBound + tolerance
        }
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
                ReferenceSpectrumToggleLabel(title: "CIE D50（ISO 3664参照）", color: .orange)
            }
            .toggleStyle(.checkbox)
            .help("ISO 3664で参照されるCIE標準光源D50を重ねます")

            Toggle(isOn: $showsD65Reference) {
                ReferenceSpectrumToggleLabel(title: "CIE D65（ISO 3668参照）", color: .blue)
            }
            .toggleStyle(.checkbox)
            .help("ISO 3668で参照されるCIE標準光源D65を重ねます")

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
                    title: "CIE D50（ISO 3664参照）",
                    color: .orange
                )
            }
            if showsD65Reference {
                ReferenceSpectrumToggleLabel(
                    title: "CIE D65（ISO 3668参照）",
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
        let spectrumGradient = SpectrumChartStyle.gradient(for: displayRange)
        let resolvedYAxisScale = yAxisScale

        return Chart {
            ForEach(samples) { sample in
                AreaMark(
                    x: .value("波長（nm）", sample.wavelength),
                    yStart: .value("基準", 0.0),
                    yEnd: .value("スペクトル値", sample.value)
                )
                .interpolationMethod(.linear)
                .foregroundStyle(spectrumGradient)
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
                    series: .value("系列", "CIE D50（ISO 3664参照）")
                )
                .interpolationMethod(.linear)
                .lineStyle(.init(lineWidth: 2, lineCap: .round, lineJoin: .round))
                .foregroundStyle(Color.orange)
            }

            ForEach(d65ReferenceSamples) { sample in
                LineMark(
                    x: .value("波長（nm）", sample.wavelength),
                    y: .value("D65基準分光分布", sample.value),
                    series: .value("系列", "CIE D65（ISO 3668参照）")
                )
                .interpolationMethod(.linear)
                .lineStyle(.init(lineWidth: 2, lineCap: .round, lineJoin: .round, dash: [6, 4]))
                .foregroundStyle(Color.blue)
            }
        }
        .chartXScale(domain: displayRange)
        .chartYScale(domain: 0...resolvedYAxisScale.upperBound)
        .chartXAxis {
            AxisMarks(
                values: SpectrumChartScale.axisValues(for: displayRange)
            ) { value in
                AxisGridLine()
                AxisTick()
                if let wavelength = value.as(Double.self),
                   SpectrumChartScale.isEndpoint(
                       wavelength,
                       of: displayRange
                   ) == false {
                    AxisValueLabel {
                        Text(SpectrumChartScale.format(wavelength))
                    }
                }
            }
        }
        .chartYAxis {
            AxisMarks(
                position: .leading,
                values: resolvedYAxisScale.tickValues
            ) { value in
                AxisGridLine()
                AxisTick()
                if let tickValue = value.as(Double.self) {
                    AxisValueLabel {
                        Text(
                            tickValue.formatted(
                                .number.precision(.fractionLength(0))
                            )
                        )
                    }
                }
            }
        }
        .chartXAxisLabel("(nm)", position: .bottom, alignment: .trailing, spacing: 0)
        .chartPlotStyle { plotArea in
            plotArea
                .background(spectrumGradient.opacity(0.17))
                .compositingGroup()
                .clipShape(
                    .rect(cornerRadius: roundsPlotAreaCorners ? 6 : 0)
                )
        }
        .chartOverlay { proxy in
            spectrumHoverOverlay(proxy: proxy)
        }
        .frame(height: 400)
        .accessibilityLabel("スペクトル分布グラフ")
        .accessibilityHint("波長ごとの測定値と、選択したD50またはD65の基準分光分布を表示します")
        .overlay(alignment: .topTrailing) {
            if let visiblePeak {
                Text(
                    SpectrumPeakAnnotation.text(
                        measurementName: measurementName,
                        measurement: measurement,
                        peak: visiblePeak
                    )
                )
                    .font(.caption.monospacedDigit())
                    .lineLimit(1)
                    .minimumScaleFactor(0.7)
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

    private func spectrumHoverOverlay(proxy: ChartProxy) -> some View {
        GeometryReader { geometry in
            if let plotFrame = proxy.plotFrame {
                let plotRectangle = geometry[plotFrame]

                ZStack {
                    SpectrumEndpointLabels(
                        wavelengthRange: displayRange,
                        plotRectangle: plotRectangle
                    )

                    Rectangle()
                        .fill(.clear)
                        .contentShape(Rectangle())
                        .frame(
                            width: plotRectangle.width,
                            height: plotRectangle.height
                        )
                        .position(
                            x: plotRectangle.midX,
                            y: plotRectangle.midY
                        )
                        .onContinuousHover { phase in
                            switch phase {
                            case let .active(location):
                                guard let wavelength = proxy.value(
                                    atX: location.x,
                                    as: Double.self
                                ),
                                let nearestSample = SpectrumChartInteraction
                                    .nearestSample(
                                        to: wavelength,
                                        in: samples
                                    ) else {
                                    hoveredWavelength = nil
                                    return
                                }
                                if hoveredWavelength
                                    != nearestSample.wavelength {
                                    hoveredWavelength =
                                        nearestSample.wavelength
                                }
                            case .ended:
                                hoveredWavelength = nil
                            }
                        }

                    if let hoveredSample,
                       let sampleX = proxy.position(
                           forX: hoveredSample.wavelength
                       ),
                       let sampleY = proxy.position(
                           forY: hoveredSample.value
                       ) {
                        let point = CGPoint(
                            x: plotRectangle.minX + sampleX,
                            y: plotRectangle.minY + sampleY
                        )
                        let placement = SpectrumHoverCalloutPlacement.resolve(
                            point: point,
                            plotRectangle: plotRectangle,
                            containerSize: geometry.size
                        )

                        Circle()
                            .fill(.background)
                            .overlay {
                                Circle()
                                    .stroke(
                                        .primary.opacity(0.75),
                                        lineWidth: 1.5
                                    )
                            }
                            .frame(width: 7, height: 7)
                            .position(point)
                            .allowsHitTesting(false)

                        SpectrumHoverCallout(
                            sample: hoveredSample,
                            pointerEdge: placement.pointerEdge,
                            pointerX: placement.pointerX
                        )
                        .frame(
                            width: SpectrumHoverCalloutPlacement.calloutSize.width,
                            height: SpectrumHoverCalloutPlacement.calloutSize.height
                        )
                        .position(placement.center)
                    }
                }
            }
        }
    }

    private var emptyState: some View {
        ZStack {
            SpectrumChartStyle.gradient(
                for: SpectrumChartStyle.visibleSpectrumRange
            )
            .opacity(0.12)
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

enum SpectrumChartScale {
    private static let majorTickSpacing = 50.0
    private static let equalityTolerance = 0.000_001

    static func axisValues(
        for wavelengthRange: ClosedRange<Double>
    ) -> [Double] {
        let lowerBound = wavelengthRange.lowerBound
        let upperBound = wavelengthRange.upperBound
        guard lowerBound.isFinite, upperBound.isFinite else {
            return []
        }

        var values = [lowerBound]
        var wavelength = ceil(lowerBound / majorTickSpacing)
            * majorTickSpacing

        while wavelength < upperBound - equalityTolerance {
            if wavelength > lowerBound + equalityTolerance {
                values.append(wavelength)
            }
            wavelength += majorTickSpacing
        }

        if upperBound > lowerBound + equalityTolerance {
            values.append(upperBound)
        }
        return values
    }

    static func isEndpoint(
        _ wavelength: Double,
        of wavelengthRange: ClosedRange<Double>
    ) -> Bool {
        abs(wavelength - wavelengthRange.lowerBound) <= equalityTolerance
            || abs(wavelength - wavelengthRange.upperBound) <= equalityTolerance
    }

    static func format(_ wavelength: Double) -> String {
        wavelength.formatted(
            .number.precision(.fractionLength(0...1))
        )
    }
}

enum SpectrumChartInteraction {
    static func nearestSample(
        to wavelength: Double,
        in samples: [SpectralSample]
    ) -> SpectralSample? {
        guard wavelength.isFinite else { return nil }
        return samples.min { lhs, rhs in
            let lhsDistance = abs(lhs.wavelength - wavelength)
            let rhsDistance = abs(rhs.wavelength - wavelength)
            if lhsDistance == rhsDistance {
                return lhs.wavelength < rhs.wavelength
            }
            return lhsDistance < rhsDistance
        }
    }
}

enum SpectrumChartStyle {
    static let visibleSpectrumRange = 380.0...730.0

    static var gradient: LinearGradient {
        gradient(for: visibleSpectrumRange)
    }

    private static let colorStops = [
        SpectrumColorStop(wavelength: 380.0, red: 0.42, green: 0.18, blue: 0.85),
        SpectrumColorStop(wavelength: 439.5, red: 0.16, green: 0.30, blue: 0.95),
        SpectrumColorStop(wavelength: 488.5, red: 0.05, green: 0.72, blue: 0.95),
        SpectrumColorStop(wavelength: 537.5, red: 0.12, green: 0.82, blue: 0.35),
        SpectrumColorStop(wavelength: 583.0, red: 0.94, green: 0.88, blue: 0.12),
        SpectrumColorStop(wavelength: 632.0, red: 1.00, green: 0.47, blue: 0.08),
        SpectrumColorStop(wavelength: 674.0, red: 0.95, green: 0.08, blue: 0.12),
        SpectrumColorStop(wavelength: 730.0, red: 0.55, green: 0.00, blue: 0.04),
    ]

    static func gradient(for wavelengthRange: ClosedRange<Double>) -> LinearGradient {
        let lowerBound = wavelengthRange.lowerBound
        let upperBound = wavelengthRange.upperBound
        let width = max(upperBound - lowerBound, Double.ulpOfOne)
        let wavelengths = [lowerBound]
            + colorStops
                .map(\.wavelength)
                .filter { $0 > lowerBound && $0 < upperBound }
            + [upperBound]

        return LinearGradient(
            gradient: Gradient(
                stops: wavelengths.map { wavelength in
                    Gradient.Stop(
                        color: color(at: wavelength),
                        location: CGFloat(
                            (wavelength - lowerBound) / width
                        )
                    )
                }
            ),
            startPoint: .leading,
            endPoint: .trailing
        )
    }

    private static func color(at wavelength: Double) -> Color {
        guard let first = colorStops.first,
              let last = colorStops.last else {
            return .clear
        }
        if wavelength <= first.wavelength {
            return first.color
        }
        if wavelength >= last.wavelength {
            return last.color
        }

        for (lower, upper) in zip(colorStops, colorStops.dropFirst())
        where wavelength <= upper.wavelength {
            let progress = (wavelength - lower.wavelength)
                / (upper.wavelength - lower.wavelength)
            return Color(
                red: lower.red + (upper.red - lower.red) * progress,
                green: lower.green + (upper.green - lower.green) * progress,
                blue: lower.blue + (upper.blue - lower.blue) * progress
            )
        }
        return last.color
    }
}

private enum SpectrumHoverPointerEdge {
    case top
    case bottom
}

private struct SpectrumEndpointLabels: View {
    let wavelengthRange: ClosedRange<Double>
    let plotRectangle: CGRect

    private static let labelWidth: CGFloat = 46

    var body: some View {
        Group {
            Text(SpectrumChartScale.format(wavelengthRange.lowerBound))
                .frame(width: Self.labelWidth, alignment: .leading)
                .position(
                    x: plotRectangle.minX + Self.labelWidth / 2,
                    y: plotRectangle.maxY + 13
                )

            Text(SpectrumChartScale.format(wavelengthRange.upperBound))
                .frame(width: Self.labelWidth, alignment: .trailing)
                .position(
                    x: plotRectangle.maxX - Self.labelWidth / 2,
                    y: plotRectangle.maxY + 13
                )
        }
        .font(.caption2)
        .foregroundStyle(.secondary)
        .accessibilityHidden(true)
        .allowsHitTesting(false)
    }
}

private struct SpectrumHoverCalloutPlacement {
    static let calloutSize = CGSize(width: 124, height: 52)

    let center: CGPoint
    let pointerEdge: SpectrumHoverPointerEdge
    let pointerX: CGFloat

    static func resolve(
        point: CGPoint,
        plotRectangle: CGRect,
        containerSize: CGSize
    ) -> SpectrumHoverCalloutPlacement {
        let calloutWidth = calloutSize.width
        let calloutHeight = calloutSize.height
        let showsAbove = point.y - plotRectangle.minY >= calloutHeight
        let pointerEdge: SpectrumHoverPointerEdge = showsAbove
            ? .bottom
            : .top
        let centerY = showsAbove
            ? point.y - calloutHeight / 2
            : point.y + calloutHeight / 2
        let halfWidth = calloutWidth / 2
        let centerX = min(
            max(point.x, halfWidth),
            max(halfWidth, containerSize.width - halfWidth)
        )
        let pointerX = point.x - (centerX - halfWidth)

        return SpectrumHoverCalloutPlacement(
            center: CGPoint(x: centerX, y: centerY),
            pointerEdge: pointerEdge,
            pointerX: pointerX
        )
    }
}

private struct SpectrumHoverCallout: View {
    let sample: SpectralSample
    let pointerEdge: SpectrumHoverPointerEdge
    let pointerX: CGFloat

    private static let pointerHeight: CGFloat = 7

    var body: some View {
        VStack(alignment: .leading, spacing: 1) {
            Text(
                "\(sample.wavelength.formatted(.number.precision(.fractionLength(1)))) nm"
            )
            Text(
                "測定値 \(sample.value.formatted(.number.precision(.fractionLength(2))))"
            )
        }
        .font(.caption.monospacedDigit())
        .lineLimit(1)
        .minimumScaleFactor(0.75)
        .padding(.horizontal, 9)
        .padding(.top, pointerEdge == .top ? Self.pointerHeight + 5 : 5)
        .padding(.bottom, pointerEdge == .bottom ? Self.pointerHeight + 5 : 5)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
        .background {
            SpectrumHoverCalloutShape(
                pointerEdge: pointerEdge,
                pointerX: pointerX,
                pointerHeight: Self.pointerHeight
            )
            .fill(.regularMaterial)
            .overlay {
                SpectrumHoverCalloutShape(
                    pointerEdge: pointerEdge,
                    pointerX: pointerX,
                    pointerHeight: Self.pointerHeight
                )
                .stroke(.primary.opacity(0.18), lineWidth: 0.75)
            }
            .shadow(color: .black.opacity(0.18), radius: 4, y: 2)
        }
        .accessibilityHidden(true)
        .allowsHitTesting(false)
    }
}

private struct SpectrumHoverCalloutShape: Shape {
    let pointerEdge: SpectrumHoverPointerEdge
    let pointerX: CGFloat
    let pointerHeight: CGFloat

    func path(in rect: CGRect) -> Path {
        let bubbleRectangle = CGRect(
            x: rect.minX,
            y: pointerEdge == .top
                ? rect.minY + pointerHeight
                : rect.minY,
            width: rect.width,
            height: rect.height - pointerHeight
        )
        let cornerRadius = min(7, bubbleRectangle.height / 2)
        let pointerHalfWidth = 6.0
        let resolvedPointerX = min(
            max(pointerX, pointerHalfWidth + 2),
            rect.width - pointerHalfWidth - 2
        )

        var path = Path()
        path.move(
            to: CGPoint(
                x: bubbleRectangle.minX + cornerRadius,
                y: bubbleRectangle.minY
            )
        )

        if pointerEdge == .top {
            path.addLine(
                to: CGPoint(
                    x: resolvedPointerX - pointerHalfWidth,
                    y: bubbleRectangle.minY
                )
            )
            path.addLine(
                to: CGPoint(x: resolvedPointerX, y: rect.minY)
            )
            path.addLine(
                to: CGPoint(
                    x: resolvedPointerX + pointerHalfWidth,
                    y: bubbleRectangle.minY
                )
            )
        }

        path.addLine(
            to: CGPoint(
                x: bubbleRectangle.maxX - cornerRadius,
                y: bubbleRectangle.minY
            )
        )
        path.addQuadCurve(
            to: CGPoint(
                x: bubbleRectangle.maxX,
                y: bubbleRectangle.minY + cornerRadius
            ),
            control: CGPoint(
                x: bubbleRectangle.maxX,
                y: bubbleRectangle.minY
            )
        )
        path.addLine(
            to: CGPoint(
                x: bubbleRectangle.maxX,
                y: bubbleRectangle.maxY - cornerRadius
            )
        )
        path.addQuadCurve(
            to: CGPoint(
                x: bubbleRectangle.maxX - cornerRadius,
                y: bubbleRectangle.maxY
            ),
            control: CGPoint(
                x: bubbleRectangle.maxX,
                y: bubbleRectangle.maxY
            )
        )

        if pointerEdge == .bottom {
            path.addLine(
                to: CGPoint(
                    x: resolvedPointerX + pointerHalfWidth,
                    y: bubbleRectangle.maxY
                )
            )
            path.addLine(
                to: CGPoint(x: resolvedPointerX, y: rect.maxY)
            )
            path.addLine(
                to: CGPoint(
                    x: resolvedPointerX - pointerHalfWidth,
                    y: bubbleRectangle.maxY
                )
            )
        }

        path.addLine(
            to: CGPoint(
                x: bubbleRectangle.minX + cornerRadius,
                y: bubbleRectangle.maxY
            )
        )
        path.addQuadCurve(
            to: CGPoint(
                x: bubbleRectangle.minX,
                y: bubbleRectangle.maxY - cornerRadius
            ),
            control: CGPoint(
                x: bubbleRectangle.minX,
                y: bubbleRectangle.maxY
            )
        )
        path.addLine(
            to: CGPoint(
                x: bubbleRectangle.minX,
                y: bubbleRectangle.minY + cornerRadius
            )
        )
        path.addQuadCurve(
            to: CGPoint(
                x: bubbleRectangle.minX + cornerRadius,
                y: bubbleRectangle.minY
            ),
            control: CGPoint(
                x: bubbleRectangle.minX,
                y: bubbleRectangle.minY
            )
        )
        path.closeSubpath()
        return path
    }
}

private struct SpectrumColorStop {
    let wavelength: Double
    let red: Double
    let green: Double
    let blue: Double

    var color: Color {
        Color(red: red, green: green, blue: blue)
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
