import SwiftUI

struct LicenseInformationCommand: View {
    static let windowID = "license-information"

    @Environment(\.openWindow) private var openWindow

    var body: some View {
        Button("ライセンスとソースコード…") {
            openWindow(id: Self.windowID)
        }
    }
}

struct LicenseInformationView: View {
    private let sourceURL = URL(
        string: "https://github.com/Yamonov/SpectraMate"
    )!

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text("SpectraMate")
                .font(.title.bold())

            Text("Copyright © 2026 Yamonov")

            Text(
                """
                SpectraMateはGNU Affero General Public License \
                バージョン3以降の条件で再配布・変更できます。

                このソフトウェアは有用であることを願って配布されますが、\
                商品性や特定目的への適合性を含め、一切の保証はありません。
                """
            )

            Divider()

            Text(
                """
                同梱するArgyllCMS 3.5.0はGraeme W. Gill氏および各構成要素の\
                著作権者によるものです。SparkleとCIE標準光源データを含む\
                第三者著作物には、それぞれのライセンスが適用されます。
                """
            )
            .foregroundStyle(.secondary)

            Link(destination: sourceURL) {
                Label(
                    "ソースコード、ライセンス、改変内容を表示",
                    systemImage: "arrow.up.right.square"
                )
            }

            Spacer(minLength: 0)
        }
        .textSelection(.enabled)
        .padding(28)
        .frame(width: 520, height: 430)
    }
}
