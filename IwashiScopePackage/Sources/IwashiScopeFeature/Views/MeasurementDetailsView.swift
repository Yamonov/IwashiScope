import SwiftUI

struct MeasurementDetailsView: View {
    let measurement: SpotMeasurement?
    let calibrationCompleted: Bool

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                if let measurement {
                    measurementHeader(measurement)
                    colorimetricGroup(measurement)

                    if hasLightingMetrics(measurement) {
                        lightingGroup(measurement)
                    }

                    if measurement.mode != .reflectance {
                        PrintingViewingConditionEvaluationView(measurement: measurement)
                        ISO3664NumericEvaluationView(measurement: measurement)
                    }

                    if measurement.cri != nil || measurement.tlci != nil {
                        renderingGroup(measurement)
                    }
                } else {
                    ContentUnavailableView(
                        calibrationCompleted ? "測定値を待っています" : "キャリブレーション待ち",
                        systemImage: "list.bullet.rectangle",
                        description: Text(
                            calibrationCompleted
                                ? "測定後、解析した値をここへ表示します。"
                                : "測定器のキャリブレーションを完了してください。"
                        )
                    )
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 34)
                }
            }
            .padding(16)
        }
        .background(Color.secondary.opacity(0.035))
    }

    private func measurementHeader(_ measurement: SpotMeasurement) -> some View {
        HStack {
            VStack(alignment: .leading, spacing: 3) {
                Text("測定結果")
                    .font(.title2.weight(.semibold))
                Text(measurement.capturedAt.formatted(date: .omitted, time: .standard))
                    .font(.caption.monospacedDigit())
                    .foregroundStyle(.secondary)
            }
            Spacer()
            Image(systemName: "checkmark.circle.fill")
                .font(.title2)
                .foregroundStyle(.green)
                .accessibilityLabel("測定完了")
        }
    }

    private func colorimetricGroup(_ measurement: SpotMeasurement) -> some View {
        GroupBox("測色値") {
            VStack(spacing: 10) {
                if let xyz = measurement.xyz {
                    TripleMetricView(
                        title: "XYZ",
                        labels: ("X", "Y", "Z"),
                        values: (
                            format(xyz.first, digits: 3),
                            format(xyz.second, digits: 3),
                            format(xyz.third, digits: 3)
                        )
                    )
                }

                if let lab = measurement.lab {
                    Divider()
                    TripleMetricView(
                        title: "\(measurement.labWhitePoint ?? "D50") Lab",
                        labels: ("L*", "a*", "b*"),
                        values: (
                            format(lab.first, digits: 3),
                            format(lab.second, digits: 3),
                            format(lab.third, digits: 3)
                        )
                    )

                    if measurement.mode != .ambient,
                       let colorConversion = LabColorConverter.convert(
                           lab: lab,
                           whitePoint: measurement.labWhitePoint
                       ) {
                        Divider()
                        ColorEncodingMetricsView(conversion: colorConversion)
                    }
                }

                if let monochrome = measurement.monochrome {
                    MetricRow(label: "Y", value: format(monochrome.y, digits: 3))
                    MetricRow(label: "L*", value: format(monochrome.lStar, digits: 3))
                }

            }
            .padding(.top, 4)
        }
    }

    private func lightingGroup(_ measurement: SpotMeasurement) -> some View {
        GroupBox("光源情報") {
            VStack(spacing: 8) {
                if let lux = measurement.lux {
                    MetricRow(label: String(localized: "照度"), value: "\(format(lux, digits: 1)) lx")
                }
                if let cct = measurement.cct {
                    MetricRow(label: "CCT", value: "\(format(cct, digits: 0)) K")
                }
                if let duv = measurement.duv {
                    MetricRow(label: "Duv", value: format(duv, digits: 4))
                }
                if let ev = measurement.suggestedEV100 {
                    MetricRow(label: String(localized: "推奨EV（ISO 100）"), value: format(ev, digits: 1))
                }
                if let planckian = measurement.closestPlanckian {
                    Divider()
                    MetricRow(
                        label: String(localized: "最接近黒体軌跡"),
                        value: "\(format(planckian.kelvin, digits: 0)) K  ·  ΔE00 \(format(planckian.deltaE2000, digits: 1))"
                    )
                }
                if let daylight = measurement.closestDaylight {
                    MetricRow(
                        label: String(localized: "最接近昼光軌跡"),
                        value: "\(format(daylight.kelvin, digits: 0)) K  ·  ΔE00 \(format(daylight.deltaE2000, digits: 1))"
                    )
                }
                if measurement.lightingMetricIssues.contains(.invalidCCT) {
                    metricWarning(String(localized: "CCTを算出できませんでした"))
                }
                if measurement.lightingMetricIssues.contains(.invalidPlanckianTemperature) {
                    metricWarning(String(localized: "黒体軌跡の最近接温度を算出できませんでした"))
                }
                if measurement.lightingMetricIssues.contains(.invalidDaylightTemperature) {
                    metricWarning(String(localized: "昼光軌跡の最近接温度を算出できませんでした"))
                }
            }
            .padding(.top, 4)
        }
    }

    private func renderingGroup(_ measurement: SpotMeasurement) -> some View {
        GroupBox("演色評価数（Color Rendering Index / CRI）") {
            VStack(alignment: .leading, spacing: 12) {
                if let cri = measurement.cri {
                    ScoreView(label: "Ra", value: format(cri.ra, digits: 1), caution: cri.caution)
                }

                if measurement.cri != nil, measurement.tlci != nil {
                    Divider()
                }

                if let tlci = measurement.tlci {
                    MetricRow(label: "TLCI 2012 Qa", value: format(tlci.qa, digits: 1))
                    if tlci.caution {
                        metricWarning(String(localized: "TLCIの結果にCautionがあります"))
                    }
                }

            }
            .padding(.top, 4)
        }
    }

    private func hasLightingMetrics(_ measurement: SpotMeasurement) -> Bool {
        measurement.lux != nil
            || measurement.cct != nil
            || measurement.duv != nil
            || measurement.suggestedEV100 != nil
            || measurement.closestPlanckian != nil
            || measurement.closestDaylight != nil
            || !measurement.lightingMetricIssues.isEmpty
    }

    private func metricWarning(_ message: String) -> some View {
        Label(message, systemImage: "exclamationmark.triangle.fill")
            .font(.caption)
            .foregroundStyle(.orange)
    }

    private func format(_ value: Double, digits: Int) -> String {
        value.formatted(.number.precision(.fractionLength(digits)))
    }
}

private struct ColorEncodingMetricsView: View {
    let conversion: LabColorConversion

    var body: some View {
        VStack(spacing: 8) {
            GamutMetricRow(
                label: "HEX（sRGB）",
                value: conversion.sRGB.hex,
                colorSpaceName: "sRGB",
                isOutOfGamut: conversion.sRGB.isOutOfGamut
            )
            GamutMetricRow(
                label: String(localized: "Adobe RGB (1998) RGB値"),
                value: conversion.adobeRGB.rgbDescription,
                colorSpaceName: "Adobe RGB (1998)",
                isOutOfGamut: conversion.adobeRGB.isOutOfGamut
            )
        }
    }
}

private struct GamutMetricRow: View {
    let label: String
    let value: String
    let colorSpaceName: String
    let isOutOfGamut: Bool

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Text(label)
                .foregroundStyle(.secondary)
            Spacer(minLength: 8)
            Text(value)
                .font(.body.monospacedDigit())
                .multilineTextAlignment(.trailing)
                .textSelection(.enabled)
            if isOutOfGamut {
                GamutWarningIcon(colorSpaceName: colorSpaceName)
                    .font(.caption)
            }
        }
        .accessibilityElement(children: .combine)
    }
}

private struct MetricRow: View {
    let label: String
    let value: String

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 12) {
            Text(label)
                .foregroundStyle(.secondary)
            Spacer(minLength: 8)
            Text(value)
                .font(.body.monospacedDigit())
                .multilineTextAlignment(.trailing)
                .textSelection(.enabled)
        }
        .accessibilityElement(children: .combine)
    }
}

private struct TripleMetricView: View {
    let title: String
    let labels: (String, String, String)
    let values: (String, String, String)

    var body: some View {
        VStack(alignment: .leading, spacing: 7) {
            Text(title)
                .font(.headline)
            Grid(horizontalSpacing: 14, verticalSpacing: 4) {
                GridRow {
                    metric(labels.0, values.0)
                    metric(labels.1, values.1)
                    metric(labels.2, values.2)
                }
            }
        }
    }

    private func metric(_ label: String, _ value: String) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(label)
                .font(.caption)
                .foregroundStyle(.secondary)
            Text(value)
                .font(.body.monospacedDigit())
                .textSelection(.enabled)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .combine)
    }
}

private struct ScoreView: View {
    let label: String
    let value: String
    let caution: Bool

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 6) {
            Text(label)
                .font(.headline)
                .foregroundStyle(.secondary)
            Text(value)
                .font(.title2.bold().monospacedDigit())
            if caution {
                Image(systemName: "exclamationmark.triangle.fill")
                    .foregroundStyle(.orange)
                    .accessibilityLabel("注意")
            }
        }
        .accessibilityElement(children: .combine)
    }
}
