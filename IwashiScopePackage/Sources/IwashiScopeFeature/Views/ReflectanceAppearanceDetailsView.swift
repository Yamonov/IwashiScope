import SwiftUI

struct ReflectanceAppearanceDetailsView: View {
    let comparison: ReflectanceIlluminantColorComparisonResult

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("基準・選択光源とも同じ分光積分で再計算。通常の測定結果と履歴は元の測定値を保持します。")
            Text("CIE 1931 2°／計算波長 \(Int(comparison.wavelengthRange.lowerBound))–\(Int(comparison.wavelengthRange.upperBound)) nm／基準白 Y = 100")
            if let source = comparison.sourceAppearance,
               let reference = comparison.referenceAppearance,
               let degree = comparison.sourceAdaptationDegree,
               let referenceDegree = comparison.referenceAdaptationDegree {
                Text("CIECAM16（CIE 248:2022）／D50基準の比較表示")
                Text("想定条件：白 100 cd/m²、順応輝度 20 cd/m²、中性20%背景、Average。周囲も同じ光源。")
                Text("標準の順応度 D：選択光源 \(degree.formatted(.number.precision(.fractionLength(4))))／D50 \(referenceDegree.formatted(.number.precision(.fractionLength(4))))")
                Text("標準Dは輝度と周辺条件から算出します。光源色度に依存する研究式は未適用です。")
                Grid(alignment: .leading, horizontalSpacing: 16, verticalSpacing: 4) {
                    GridRow {
                        Text("CAM予測値")
                        Text("D50基準")
                        Text(comparison.source.displayName)
                    }
                    metric(String(localized: "明るさ Q"), reference.brightness, source.brightness)
                    metric(String(localized: "カラフルネス M"), reference.colorfulness, source.colorfulness)
                    metric(String(localized: "彩度 s"), reference.saturation, source.saturation)
                    GridRow {
                        Text("色相 h")
                        Text(reference.hue.map(format) ?? "—")
                        Text(source.hue.map(format) ?? "—")
                    }
                }
                .monospacedDigit()
            }
            if let measured = comparison.measuredLab {
                let difference = CIEColorDifference.deltaE2000(measured, comparison.referenceLab)
                Text("元の測定LabとD50再計算の差：ΔE00 \(format(difference))（分光積分・白色点の条件差を含みます）")
            }
            Text("予測色差は共通D50 Labの比較です。画面表示用RGBへ変換する前の値を使用します。")
        }
        .font(.caption)
        .foregroundStyle(.secondary)
        .frame(maxWidth: .infinity, alignment: .leading)
        .textSelection(.enabled)
    }

    private func metric(_ title: String, _ reference: Double, _ source: Double) -> some View {
        GridRow {
            Text(title)
            Text(format(reference))
            Text(format(source))
        }
    }

    private func format(_ value: Double) -> String {
        value.formatted(.number.precision(.fractionLength(2)))
    }
}
