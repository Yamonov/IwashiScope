/*
 The color-vector geometry is adapted from ArgyllCMS 3.5.0 xicc/tm3015.c,
 function tm3015_plot(), written by Graeme W. Gill, Copyright (C) 2019.
 The original portions are licensed under GPL-2.0-or-later; see
 Argyll_V3.5.0/License2.txt.

 Adapted for IwashiScope by Yamonov on 2026-07-23: converted polar
 normalization and four-step contour interpolation to Swift, added explicit
 16-sector geometry and displacement arrows, and implemented the SwiftUI
 presentation. IwashiScope's modifications are licensed under
 AGPL-3.0-only. The combined IwashiScope distribution is provided under
 AGPL-3.0-only.
*/

import SwiftUI

struct TM30ColorVectorPoint: Equatable, Sendable {
    let x: Double
    let y: Double

    var radius: Double { hypot(x, y) }
}

struct TM30ColorVectorShift: Equatable, Sendable {
    let index: Int
    let reference: TM30ColorVectorPoint
    let test: TM30ColorVectorPoint
}

struct TM30HueBinSector: Equatable, Sendable {
    let index: Int
    let startAngle: Double
    let endAngle: Double
    let labelAngle: Double
}

enum TM30HueBinLayout {
    static let count = 16
    static let angleStep = 2 * Double.pi / Double(count)

    static let sectors: [TM30HueBinSector] = (1...count).map { index in
        let startAngle = Double(index - 1) * angleStep
        let endAngle = Double(index) * angleStep
        return TM30HueBinSector(
            index: index,
            startAngle: startAngle,
            endAngle: endAngle,
            labelAngle: (startAngle + endAngle) / 2
        )
    }
}

struct TM30ColorVectorGeometry: Equatable, Sendable {
    let referenceContour: [TM30ColorVectorPoint]
    let testContour: [TM30ColorVectorPoint]
    let shifts: [TM30ColorVectorShift]

    static func make(
        bins: [TM30HueBin],
        interpolationCount: Int = 4
    ) -> TM30ColorVectorGeometry? {
        guard bins.count == 16,
              interpolationCount > 0,
              bins.map(\.index) == Array(1...16) else {
            return nil
        }

        let polarBins = bins.map { bin in
            (
                reference: polar(a: bin.referenceJab.second, b: bin.referenceJab.third),
                test: polar(a: bin.testJab.second, b: bin.testJab.third)
            )
        }
        guard polarBins.allSatisfy({ bin in
            bin.reference.angle.isFinite
                && bin.reference.radius.isFinite
                && bin.test.angle.isFinite
                && bin.test.radius.isFinite
                && bin.reference.radius > 1e-12
        }) else {
            return nil
        }

        var referenceContour: [TM30ColorVectorPoint] = []
        var testContour: [TM30ColorVectorPoint] = []
        var shifts: [TM30ColorVectorShift] = []
        referenceContour.reserveCapacity(bins.count * interpolationCount)
        testContour.reserveCapacity(bins.count * interpolationCount)
        shifts.reserveCapacity(bins.count)

        for index in polarBins.indices {
            let nextIndex = polarBins.index(after: index) == polarBins.endIndex
                ? polarBins.startIndex
                : polarBins.index(after: index)
            let current = polarBins[index]
            let next = polarBins[nextIndex]
            let referenceAngles = unwrappedPair(
                current.reference.angle,
                next.reference.angle
            )
            let testAngles = unwrappedPair(
                current.test.angle,
                next.test.angle
            )

            for sample in 0..<interpolationCount {
                let fraction = Double(sample) / Double(interpolationCount)
                let referenceAngle = interpolate(
                    referenceAngles.0,
                    referenceAngles.1,
                    fraction: fraction
                )
                let referenceRadius = interpolate(
                    current.reference.radius,
                    next.reference.radius,
                    fraction: fraction
                )
                let testAngle = interpolate(
                    testAngles.0,
                    testAngles.1,
                    fraction: fraction
                )
                let testRadius = interpolate(
                    current.test.radius,
                    next.test.radius,
                    fraction: fraction
                )
                guard referenceRadius.isFinite, referenceRadius > 1e-12 else {
                    return nil
                }

                let normalizedReference = cartesian(angle: referenceAngle, radius: 1)
                let normalizedTest = cartesian(
                    angle: testAngle,
                    radius: testRadius / referenceRadius
                )
                referenceContour.append(normalizedReference)
                testContour.append(normalizedTest)

                if sample == 0 {
                    shifts.append(
                        TM30ColorVectorShift(
                            index: bins[index].index,
                            reference: normalizedReference,
                            test: normalizedTest
                        )
                    )
                }
            }
        }

        guard testContour.allSatisfy({ $0.x.isFinite && $0.y.isFinite }) else {
            return nil
        }
        return TM30ColorVectorGeometry(
            referenceContour: referenceContour,
            testContour: testContour,
            shifts: shifts
        )
    }

    private static func polar(a: Double, b: Double) -> (angle: Double, radius: Double) {
        (atan2(b, a), hypot(a, b))
    }

    private static func unwrappedPair(_ first: Double, _ second: Double) -> (Double, Double) {
        var adjustedFirst = first
        if second - adjustedFirst > .pi {
            adjustedFirst += 2 * .pi
        } else if second - adjustedFirst < -.pi {
            adjustedFirst -= 2 * .pi
        }
        return (adjustedFirst, second)
    }

    private static func interpolate(
        _ first: Double,
        _ second: Double,
        fraction: Double
    ) -> Double {
        (1 - fraction) * first + fraction * second
    }

    private static func cartesian(angle: Double, radius: Double) -> TM30ColorVectorPoint {
        TM30ColorVectorPoint(
            x: radius * cos(angle),
            y: radius * sin(angle)
        )
    }
}

struct TM30ColorVectorGraphicView: View {
    let measurement: SpotMeasurement?

    private var result: TM30Result? { measurement?.tm30 }

    private var geometry: TM30ColorVectorGeometry? {
        guard let result else { return nil }
        return TM30ColorVectorGeometry.make(bins: result.hueBins)
    }

    var body: some View {
        GroupBox {
            if let result, let geometry {
                HStack(alignment: .top, spacing: 16) {
                    TM30ColorVectorCanvas(geometry: geometry, result: result)
                        .frame(minWidth: 300, maxWidth: 440, minHeight: 440, maxHeight: 440)

                    VStack(alignment: .leading, spacing: 12) {
                        HStack(alignment: .top, spacing: 14) {
                            TM30ScoreSummary(result: result)
                                .frame(minWidth: 120, maxWidth: 140, alignment: .topLeading)

                            TM30RfRgPlotView(
                                fidelityIndex: result.fidelityIndex,
                                gamutIndex: result.gamutIndex
                            )
                        }
                        .frame(maxWidth: .infinity, minHeight: 230, maxHeight: 230)

                        TM30SampleFidelityChartView(samples: result.evaluationSamples)
                            .frame(maxWidth: .infinity, minHeight: 198, maxHeight: 198)
                    }
                    .frame(
                        minWidth: 360,
                        maxWidth: .infinity,
                        minHeight: 440,
                        maxHeight: 440,
                        alignment: .topLeading
                    )
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            } else {
                ContentUnavailableView(
                    "TM-30評価を待っています",
                    systemImage: "circle.hexagongrid",
                    description: Text("環境光または発光を測定するとTM-30のRf・Rgと各グラフを表示します。")
                )
                .frame(maxWidth: .infinity, minHeight: 440, maxHeight: 440)
            }
        } label: {
            Label("IES TM-30-15", systemImage: "circle.hexagongrid.fill")
        }
        .frame(maxWidth: .infinity)
    }
}

private struct TM30ColorVectorCanvas: View {
    @Environment(\.colorScheme) private var colorScheme

    let geometry: TM30ColorVectorGeometry
    let result: TM30Result

    var body: some View {
        Canvas { context, size in
            let center = CGPoint(x: size.width / 2, y: size.height / 2)
            let maximumTestRadius = geometry.testContour.map(\.radius).max() ?? 1
            let scaleLimit = max(1.2, ceil(maximumTestRadius * 10) / 10)
            let plotRadius = min(size.width, size.height) * 0.40

            func plotPoint(_ value: TM30ColorVectorPoint) -> CGPoint {
                CGPoint(
                    x: center.x + value.x * plotRadius / scaleLimit,
                    y: center.y - value.y * plotRadius / scaleLimit
                )
            }

            let backdropRadius = hypot(size.width, size.height) / 2
            for sector in TM30HueBinLayout.sectors {
                let start = radialPoint(
                    angle: sector.startAngle,
                    radius: backdropRadius,
                    center: center
                )
                let end = radialPoint(
                    angle: sector.endAngle,
                    radius: backdropRadius,
                    center: center
                )
                var wedge = Path()
                wedge.move(to: center)
                wedge.addLine(to: start)
                wedge.addLine(to: end)
                wedge.closeSubpath()
                context.fill(
                    wedge,
                    with: .color(
                        Color(
                            hue: sector.labelAngle / (2 * Double.pi),
                            saturation: 0.62,
                            brightness: 0.96
                        )
                        .opacity(colorScheme == .dark ? 0.27 : 0.23)
                    )
                )
            }

            let washColor = colorScheme == .dark ? Color.black : Color.white
            for step in stride(from: 12, through: 1, by: -1) {
                let fraction = CGFloat(step) / 12
                let radius = plotRadius * 1.25 * fraction
                context.fill(
                    Path(
                        ellipseIn: CGRect(
                            x: center.x - radius,
                            y: center.y - radius,
                            width: 2 * radius,
                            height: 2 * radius
                        )
                    ),
                    with: .color(washColor.opacity(colorScheme == .dark ? 0.045 : 0.075))
                )
            }

            for normalizedRadius: CGFloat in [0.8, 0.9, 1.0, 1.1, 1.2] {
                let radius = normalizedRadius * plotRadius / CGFloat(scaleLimit)
                let rectangle = CGRect(
                    x: center.x - radius,
                    y: center.y - radius,
                    width: 2 * radius,
                    height: 2 * radius
                )
                context.stroke(
                    Path(ellipseIn: rectangle),
                    with: .color(.white.opacity(normalizedRadius == 1 ? 0.78 : 0.52)),
                    style: StrokeStyle(lineWidth: normalizedRadius == 1 ? 1.5 : 0.75)
                )
            }

            let axisStartRadius = plotRadius * 0.08
            let axisEndRadius = plotRadius * 1.22
            let labelRadius = plotRadius * 1.14
            for sector in TM30HueBinLayout.sectors {
                var axis = Path()
                axis.move(
                    to: radialPoint(
                        angle: sector.startAngle,
                        radius: axisStartRadius,
                        center: center
                    )
                )
                axis.addLine(
                    to: radialPoint(
                        angle: sector.startAngle,
                        radius: axisEndRadius,
                        center: center
                    )
                )
                context.stroke(
                    axis,
                    with: .color(.secondary.opacity(0.38)),
                    style: StrokeStyle(lineWidth: 0.8, dash: [4, 4])
                )

                context.draw(
                    Text("\(sector.index)")
                        .font(.caption)
                        .foregroundStyle(.secondary),
                    at: radialPoint(
                        angle: sector.labelAngle,
                        radius: labelRadius,
                        center: center
                    )
                )
            }

            for shift in geometry.shifts {
                let vector = arrowPath(
                    from: plotPoint(shift.reference),
                    to: plotPoint(shift.test)
                )
                context.stroke(
                    vector,
                    with: .color(.blue.opacity(0.55)),
                    style: StrokeStyle(lineWidth: 1, lineCap: .round, lineJoin: .round)
                )
            }

            let testPath = closedPath(points: geometry.testContour.map(plotPoint))
            context.fill(testPath, with: .color(.accentColor.opacity(0.12)))
            context.stroke(
                testPath,
                with: .color(.accentColor),
                style: StrokeStyle(lineWidth: 2, lineJoin: .round)
            )

            let referencePath = closedPath(points: geometry.referenceContour.map(plotPoint))
            context.stroke(
                referencePath,
                with: .color(.primary.opacity(0.7)),
                style: StrokeStyle(lineWidth: 1.25, lineJoin: .round)
            )
        }
        .overlay(alignment: .topLeading) {
            TM30ChromaticityOverlay(cct: result.cct, duv: result.duv)
                .padding(10)
        }
        .overlay(alignment: .bottomTrailing) {
            TM30VectorLegend()
                .padding(10)
        }
        .background(.quaternary.opacity(0.08), in: .rect(cornerRadius: 8))
        .compositingGroup()
        .clipShape(.rect(cornerRadius: 8))
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("IES TM-30-15 色相グラフ")
        .accessibilityValue(
            "CCT \(format(result.cct, digits: 0))ケルビン、Duv \(format(result.duv, digits: 6))"
        )
        .accessibilityHint("16個の均等な色相エリアを補助色で示します。黒線は基準光、アクセントカラーの線は測定光、青線は各色相ビンの差です")
    }

    private func format(_ value: Double, digits: Int) -> String {
        value.formatted(.number.precision(.fractionLength(digits)))
    }

    private func radialPoint(angle: Double, radius: CGFloat, center: CGPoint) -> CGPoint {
        CGPoint(
            x: center.x + radius * CGFloat(cos(angle)),
            y: center.y - radius * CGFloat(sin(angle))
        )
    }

    private func arrowPath(from start: CGPoint, to end: CGPoint) -> Path {
        var path = Path()
        path.move(to: start)
        path.addLine(to: end)

        let deltaX = end.x - start.x
        let deltaY = end.y - start.y
        let length = hypot(deltaX, deltaY)
        guard length >= 4 else { return path }

        let unitX = deltaX / length
        let unitY = deltaY / length
        let arrowLength = min(7, length * 0.55)
        let halfWidth = arrowLength * 0.45
        let base = CGPoint(
            x: end.x - unitX * arrowLength,
            y: end.y - unitY * arrowLength
        )
        let perpendicularX = -unitY
        let perpendicularY = unitX
        let left = CGPoint(
            x: base.x + perpendicularX * halfWidth,
            y: base.y + perpendicularY * halfWidth
        )
        let right = CGPoint(
            x: base.x - perpendicularX * halfWidth,
            y: base.y - perpendicularY * halfWidth
        )

        path.move(to: left)
        path.addLine(to: end)
        path.addLine(to: right)
        return path
    }

    private func closedPath(points: [CGPoint]) -> Path {
        var path = Path()
        guard let first = points.first else { return path }
        path.move(to: first)
        for point in points.dropFirst() {
            path.addLine(to: point)
        }
        path.closeSubpath()
        return path
    }
}

private struct TM30ChromaticityOverlay: View {
    let cct: Double
    let duv: Double

    var body: some View {
        Grid(alignment: .leading, horizontalSpacing: 8, verticalSpacing: 3) {
            GridRow {
                Text("CCT")
                Text("\(format(cct, digits: 0)) K")
                    .gridColumnAlignment(.trailing)
            }
            GridRow {
                Text("Duv")
                Text(format(duv, digits: 6))
                    .gridColumnAlignment(.trailing)
            }
        }
        .font(.caption.monospacedDigit())
        .padding(.horizontal, 8)
        .padding(.vertical, 6)
        .background(.thinMaterial, in: .rect(cornerRadius: 6))
    }

    private func format(_ value: Double, digits: Int) -> String {
        value.formatted(.number.precision(.fractionLength(digits)))
    }
}

private struct TM30VectorLegend: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Label("基準光", systemImage: "minus")
                .foregroundStyle(.primary.opacity(0.7))
            Label("測定光", systemImage: "minus")
                .foregroundStyle(.tint)
            Label("色相ビンの変位", systemImage: "arrow.right")
                .foregroundStyle(.blue)
        }
        .font(.caption2)
        .padding(.horizontal, 8)
        .padding(.vertical, 6)
        .background(.thinMaterial, in: .rect(cornerRadius: 6))
        .accessibilityHidden(true)
    }
}

private struct TM30ScoreSummary: View {
    let result: TM30Result

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            TM30Score(label: "Rf", value: format(result.fidelityIndex, digits: 1))
            TM30Score(label: "Rg", value: format(result.gamutIndex, digits: 1))

            VStack(alignment: .leading, spacing: 4) {
                Text("① プランク軌跡上の光源（概略）")
                Text("② 実用光源（概略）")
            }
            .font(.caption2)
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
            .accessibilityHidden(true)

            if result.caution {
                Label("基準光の適用範囲外です", systemImage: "exclamationmark.triangle.fill")
                    .font(.caption2)
                    .foregroundStyle(.orange)
            }
        }
        .padding(.vertical, 8)
        .accessibilityElement(children: .combine)
        .accessibilityLabel("TM-30評価")
        .accessibilityValue(
            "Rf \(format(result.fidelityIndex, digits: 1))、Rg \(format(result.gamutIndex, digits: 1))"
        )
    }

    private func format(_ value: Double, digits: Int) -> String {
        value.formatted(.number.precision(.fractionLength(digits)))
    }
}

private struct TM30Score: View {
    let label: String
    let value: String

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(label)
                .font(.caption)
                .foregroundStyle(.secondary)
            Text(value)
                .font(.system(.title, design: .rounded, weight: .semibold).monospacedDigit())
        }
    }
}
