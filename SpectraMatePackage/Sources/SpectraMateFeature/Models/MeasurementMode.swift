import Foundation

enum MeasurementMode: String, CaseIterable, Codable, Identifiable, Sendable {
    case reflectance
    case ambient
    case emissive

    var id: String { rawValue }

    var title: String {
        switch self {
        case .reflectance:
            "反射原稿"
        case .ambient:
            "環境光"
        case .emissive:
            "発光"
        }
    }

    var subtitle: String {
        switch self {
        case .reflectance:
            "印刷物・用紙・色票"
        case .ambient:
            "照度・CRI・TLCI・TM-30"
        case .emissive:
            "ディスプレイ・ライトボックス・発光体"
        }
    }

    var detail: String {
        switch self {
        case .reflectance:
            "反射スペクトルとXYZ、D50 Labを測定します。"
        case .ambient:
            "入射光のLux、CCT、Duv、演色評価値を測定します。"
        case .emissive:
            "対象に測定器を当て、発光分光分布とXYZ（Y＝輝度）、CCT、Duvを測定します。光源用途では演色指標も表示します。"
        }
    }

    var systemImage: String {
        switch self {
        case .reflectance:
            "doc.text.image"
        case .ambient:
            "sun.max"
        case .emissive:
            "camera.metering.spot"
        }
    }

    var spotreadArguments: [String] {
        var arguments = ["-J", "-v", "-s", "-H"]

        switch self {
        case .reflectance:
            break
        case .ambient:
            // spotread 3.5 enables CCT/CRI output for ambient mode itself.
            // Supplying -T here would toggle that output back off.
            arguments.append("-a")
        case .emissive:
            arguments.append(contentsOf: ["-e", "-T"])
        }

        let instrumentIndex = ProcessInfo.processInfo.environment["SPECTRAMATE_INSTRUMENT_INDEX"] ?? "1"
        arguments.append(contentsOf: ["-c", instrumentIndex])
        return arguments
    }
}
