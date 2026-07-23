import SwiftUI

struct TM30RfRgPlotScale {
    static let fidelityRange = 50.0...100.0
    static let gamutRange = 60.0...140.0

    static func normalizedPoint(fidelityIndex: Double, gamutIndex: Double) -> CGPoint {
        let clampedFidelity = min(max(fidelityIndex, fidelityRange.lowerBound), fidelityRange.upperBound)
        let clampedGamut = min(max(gamutIndex, gamutRange.lowerBound), gamutRange.upperBound)
        return projectedPoint(
            fidelityIndex: clampedFidelity,
            gamutIndex: clampedGamut
        )
    }

    static func projectedPoint(fidelityIndex: Double, gamutIndex: Double) -> CGPoint {
        return CGPoint(
            x: (fidelityIndex - fidelityRange.lowerBound)
                / (fidelityRange.upperBound - fidelityRange.lowerBound),
            y: 1 - (gamutIndex - gamutRange.lowerBound)
                / (gamutRange.upperBound - gamutRange.lowerBound)
        )
    }
}

enum TM30RfRgGuide {
    private static let planckianSlope = 0.76
    private static let practicalSlope = 1.0

    static func planckianRange(at fidelityIndex: Double) -> ClosedRange<Double> {
        range(at: fidelityIndex, slope: planckianSlope)
    }

    static func practicalRange(at fidelityIndex: Double) -> ClosedRange<Double> {
        range(at: fidelityIndex, slope: practicalSlope)
    }

    private static func range(at fidelityIndex: Double, slope: Double) -> ClosedRange<Double> {
        let fidelity = min(max(fidelityIndex, 50), 100)
        let deviation = slope * (100 - fidelity)
        return (100 - deviation)...(100 + deviation)
    }
}

struct TM30RfRgPlotView: View {
    @Environment(\.colorScheme) private var colorScheme

    let fidelityIndex: Double
    let gamutIndex: Double

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text("Rf–Rgプロット")
                .font(.caption.weight(.semibold))

            Canvas { context, size in
                let plotRectangle = CGRect(
                    x: 30,
                    y: 5,
                    width: max(1, size.width - 38),
                    height: max(1, size.height - 29)
                )

                func point(
                    fidelityIndex: Double,
                    gamutIndex: Double,
                    clamped: Bool = true
                ) -> CGPoint {
                    let normalized = clamped
                        ? TM30RfRgPlotScale.normalizedPoint(
                            fidelityIndex: fidelityIndex,
                            gamutIndex: gamutIndex
                        )
                        : TM30RfRgPlotScale.projectedPoint(
                            fidelityIndex: fidelityIndex,
                            gamutIndex: gamutIndex
                        )
                    return CGPoint(
                        x: plotRectangle.minX + normalized.x * plotRectangle.width,
                        y: plotRectangle.minY + normalized.y * plotRectangle.height
                    )
                }

                context.fill(
                    Path(plotRectangle),
                    with: .color(.primary.opacity(colorScheme == .dark ? 0.08 : 0.035))
                )

                let practicalAt50 = TM30RfRgGuide.practicalRange(at: 50)
                var practicalRange = Path()
                practicalRange.move(
                    to: point(
                        fidelityIndex: 50,
                        gamutIndex: practicalAt50.lowerBound,
                        clamped: false
                    )
                )
                practicalRange.addLine(
                    to: point(
                        fidelityIndex: 50,
                        gamutIndex: practicalAt50.upperBound,
                        clamped: false
                    )
                )
                practicalRange.addLine(to: point(fidelityIndex: 100, gamutIndex: 100))
                practicalRange.closeSubpath()

                let planckianAt50 = TM30RfRgGuide.planckianRange(at: 50)
                var planckianRange = Path()
                planckianRange.move(
                    to: point(fidelityIndex: 50, gamutIndex: planckianAt50.lowerBound)
                )
                planckianRange.addLine(
                    to: point(fidelityIndex: 50, gamutIndex: planckianAt50.upperBound)
                )
                planckianRange.addLine(to: point(fidelityIndex: 100, gamutIndex: 100))
                planckianRange.closeSubpath()

                var rangeContext = context
                rangeContext.clip(to: Path(plotRectangle))
                rangeContext.fill(
                    practicalRange,
                    with: .color(.secondary.opacity(colorScheme == .dark ? 0.24 : 0.14))
                )
                rangeContext.fill(
                    planckianRange,
                    with: .color(.secondary.opacity(colorScheme == .dark ? 0.42 : 0.27))
                )

                context.draw(
                    Text("①")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundStyle(.primary.opacity(0.72)),
                    at: point(fidelityIndex: 57, gamutIndex: 126)
                )
                context.draw(
                    Text("②")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundStyle(.primary.opacity(0.72)),
                    at: point(fidelityIndex: 67, gamutIndex: 128)
                )

                var centerRule = Path()
                centerRule.move(to: point(fidelityIndex: 50, gamutIndex: 100))
                centerRule.addLine(to: point(fidelityIndex: 100, gamutIndex: 100))
                context.stroke(
                    centerRule,
                    with: .color(.primary.opacity(0.55)),
                    style: StrokeStyle(lineWidth: 0.8, dash: [3, 3])
                )

                context.stroke(
                    Path(plotRectangle),
                    with: .color(.secondary.opacity(0.75)),
                    lineWidth: 0.8
                )

                for value in stride(from: 50.0, through: 100.0, by: 10) {
                    let location = point(fidelityIndex: value, gamutIndex: 60)
                    context.draw(
                        Text(value.formatted(.number.precision(.fractionLength(0))))
                            .font(.system(size: 8).monospacedDigit())
                            .foregroundStyle(.secondary),
                        at: CGPoint(x: location.x, y: plotRectangle.maxY + 8)
                    )
                }

                for value in stride(from: 60.0, through: 140.0, by: 20) {
                    let location = point(fidelityIndex: 50, gamutIndex: value)
                    context.draw(
                        Text(value.formatted(.number.precision(.fractionLength(0))))
                            .font(.system(size: 8).monospacedDigit())
                            .foregroundStyle(.secondary),
                        at: CGPoint(x: plotRectangle.minX - 13, y: location.y)
                    )
                }

                context.draw(
                    Text("Rf")
                        .font(.system(size: 8, weight: .medium))
                        .foregroundStyle(.secondary),
                    at: CGPoint(x: plotRectangle.midX, y: size.height - 3)
                )
                context.draw(
                    Text("Rg")
                        .font(.system(size: 8, weight: .medium))
                        .foregroundStyle(.secondary),
                    at: CGPoint(x: 6, y: plotRectangle.midY)
                )

                let measuredPoint = point(
                    fidelityIndex: fidelityIndex,
                    gamutIndex: gamutIndex
                )
                let markerRectangle = CGRect(
                    x: measuredPoint.x - 3.5,
                    y: measuredPoint.y - 3.5,
                    width: 7,
                    height: 7
                )
                context.fill(Path(ellipseIn: markerRectangle), with: .color(.red))
                context.stroke(
                    Path(ellipseIn: markerRectangle),
                    with: .color(.white.opacity(0.9)),
                    lineWidth: 0.8
                )
            }
            .frame(maxWidth: .infinity, minHeight: 118, maxHeight: .infinity)
            .accessibilityHidden(true)

            HStack(spacing: 14) {
                Text("① プランク軌跡上の光源（概略）")
                Text("② 実用光源（概略）")
            }
            .font(.caption2)
            .foregroundStyle(.secondary)
            .lineLimit(1)
            .minimumScaleFactor(0.8)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("TM-30 Rf–Rgプロット")
        .accessibilityValue(
            "Rf \(format(fidelityIndex))、Rg \(format(gamutIndex))"
        )
        .accessibilityHint("領域1はプランク軌跡上の光源、領域2は実用光源のおおよその範囲です。適合範囲ではありません")
        .help("①はプランク軌跡上の光源、②は実用光源のおおよその範囲です。適合判定ではありません。")
    }

    private func format(_ value: Double) -> String {
        value.formatted(.number.precision(.fractionLength(1)))
    }
}
