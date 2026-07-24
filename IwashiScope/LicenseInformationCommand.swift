import Foundation
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
    private var releaseVersion: String {
        Bundle.main.object(
            forInfoDictionaryKey: "CFBundleShortVersionString"
        ) as? String ?? "0.9"
    }

    private var releaseBaseURL: URL {
        URL(
            string: "https://github.com/Yamonov/IwashiScope/releases/tag/v\(releaseVersion)"
        )!
    }

    private var sourceURL: URL {
        releaseBaseURL
    }

    private var agplLicenseURL: URL {
        bundledTextURL(
            named: "ArgyllCMS-AGPL-3.0",
            subdirectory: "Licenses"
        ) ?? repositoryFileURL(path: "LICENSE")
    }

    private var gplLicenseURL: URL {
        bundledTextURL(
            named: "GPL-3.0-only",
            subdirectory: "Licenses"
        ) ?? repositoryFileURL(path: "LICENSES/GPL-3.0-only.txt")
    }

    private var thirdPartyNoticesURL: URL {
        bundledTextURL(named: "THIRD-PARTY-NOTICES")
            ?? repositoryFileURL(path: "THIRD_PARTY_NOTICES.md")
    }

    private var privacyURL: URL {
        repositoryFileURL(path: "PRIVACY.md")
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            VStack(alignment: .leading, spacing: 4) {
                Text("IwashiScope")
                    .font(.title.bold())
                Text("Spectral & Color Measurement")
                    .font(.headline)
                Text("分光・測色ツール")
                    .foregroundStyle(.secondary)
            }

            Text("Copyright © 2026 Yamonov")

            Text(
                """
                IwashiScopeはGNU Affero General Public License \
                バージョン3の条件で再配布・変更できます。

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

                CIE標準光源データから生成したSwift適応物はGPLバージョン3\
                のみで提供し、AGPLバージョン3のIwashiScope部分とは、\
                両ライセンスの第13条に基づいて結合しています。
                """
            )
            .foregroundStyle(.secondary)

            VStack(alignment: .leading, spacing: 9) {
                legalLink(
                    String(localized: "このバージョン（v\(releaseVersion)）の対応ソースと改変内容"),
                    destination: sourceURL
                )
                legalLink(
                    String(localized: "GNU AGPLバージョン3本文"),
                    destination: agplLicenseURL
                )
                legalLink(
                    String(localized: "GNU GPLバージョン3本文"),
                    destination: gplLicenseURL
                )
                legalLink(
                    String(localized: "第三者著作物とライセンス"),
                    destination: thirdPartyNoticesURL
                )
                legalLink(
                    String(localized: "プライバシー"),
                    destination: privacyURL
                )
            }

            Spacer(minLength: 0)
        }
        .textSelection(.enabled)
        .padding(28)
        .frame(width: 560, height: 560)
    }

    private func legalLink(_ title: String, destination: URL) -> some View {
        Link(destination: destination) {
            Label(title, systemImage: "arrow.up.right.square")
        }
    }

    private func repositoryFileURL(path: String) -> URL {
        URL(
            string: "https://github.com/Yamonov/IwashiScope/blob/v\(releaseVersion)/\(path)"
        )!
    }

    private func bundledTextURL(
        named name: String,
        subdirectory: String? = nil
    ) -> URL? {
        if let url = Bundle.main.url(
            forResource: name,
            withExtension: "txt",
            subdirectory: subdirectory
        ) {
            return url
        }

        guard let resourceURL = Bundle.main.resourceURL else {
            return nil
        }

        let relativePaths: [String]
        if let subdirectory {
            relativePaths = [
                "\(subdirectory)/\(name).txt",
                "\(name).txt",
            ]
        } else {
            relativePaths = ["\(name).txt"]
        }

        return relativePaths
            .map(resourceURL.appendingPathComponent)
            .first { FileManager.default.fileExists(atPath: $0.path) }
    }
}
