enum SpotreadIssueKind: Equatable, Sendable {
    case userStopped
    case sensorSaturated
    case inconsistentReading
    case measurementFailure
    case communicationFailure
    case wrongConfiguration
    case outputParsingFailure
    case calibrationFailure
    case operationFailure
    case fatalFailure
}

enum SpotreadRecoveryAction: Equatable, Sendable {
    /// A character only dismisses spotread's error prompt. A new reading must be
    /// triggered after the normal measurement prompt returns.
    case resumeMeasurementLoop
    /// A character makes inst_handle_calibrate retry its current calibration.
    case retryCalibration
    /// A character retries the operation that printed spotread's generic ierror prompt.
    case retryOperation
    /// spotread has already returned to its measurement prompt; no input is needed.
    case acknowledgeConfiguration
    case restart
}

struct SpotreadIssue: Equatable, Sendable {
    let kind: SpotreadIssueKind
    let title: String
    let instruction: String
    let systemImage: String
    let recoveryAction: SpotreadRecoveryAction
    let rawText: String

    var recoveryButtonTitle: String {
        switch recoveryAction {
        case .resumeMeasurementLoop:
            kind == .communicationFailure ? "接続後に続行" : "測定待機へ戻る"
        case .retryCalibration:
            "キャリブレーションを再試行"
        case .retryOperation:
            "操作を再試行"
        case .acknowledgeConfiguration:
            kind == .outputParsingFailure ? "測定待機へ戻る" : "設定を直しました"
        case .restart:
            "spotreadを再起動"
        }
    }

    var recoveryButtonSystemImage: String {
        switch recoveryAction {
        case .resumeMeasurementLoop, .retryCalibration, .retryOperation:
            "arrow.clockwise"
        case .acknowledgeConfiguration:
            "checkmark.circle"
        case .restart:
            "play.fill"
        }
    }

    var offersRestartAlternative: Bool {
        kind == .communicationFailure
    }
}

struct SpotreadNotice: Equatable, Sendable {
    let message: String
    let rawText: String
}

extension SpotreadIssue {
    static func misread(reason: String, rawText: String) -> SpotreadIssue {
        let normalized = reason.lowercased()

        if normalized.contains("saturated") {
            return SpotreadIssue(
                kind: .sensorSaturated,
                title: "センサーが飽和しています",
                instruction: "光量を下げるか、測定器と光源の距離・向きを調整してから測定待機へ戻ってください。",
                systemImage: "sun.max.trianglebadge.exclamationmark",
                recoveryAction: .resumeMeasurementLoop,
                rawText: rawText
            )
        }

        if normalized.contains("dark calibration reading is inconsistent") {
            return measurementFailure(
                title: "暗部校正値が安定しません",
                instruction: "測定器を動かさず、遮光状態を確認してから測定待機へ戻ってください。",
                rawText: rawText
            )
        }

        if normalized.contains("white calibration reading is inconsistent") {
            return measurementFailure(
                title: "白色校正値が安定しません",
                instruction: "白色基準への置き方と測定器の固定を確認してから測定待機へ戻ってください。",
                rawText: rawText
            )
        }

        if normalized.contains("dark reading is not valid") {
            return measurementFailure(
                title: "遮光が不十分です",
                instruction: "外光が入らない状態にしてから測定待機へ戻ってください。",
                rawText: rawText
            )
        }

        if normalized.contains("white reference reading")
            || normalized.contains("white reference calibration didn't converge") {
            return measurementFailure(
                title: "白色基準を読み取れません",
                instruction: "白色基準、測定位置、測定器の固定を確認してから測定待機へ戻ってください。",
                rawText: rawText
            )
        }

        if normalized.contains("wavelength calibration reading is too low") {
            return measurementFailure(
                title: "波長キャリブレーションの信号が不足しています",
                instruction: "校正基準と測定器の位置、光量を確認してから測定待機へ戻ってください。",
                rawText: rawText
            )
        }

        if normalized.contains("transmission white is too low") {
            return measurementFailure(
                title: "透過白基準の光量が不足しています",
                instruction: "透過光源、白基準、光路を確認してから測定待機へ戻ってください。",
                rawText: rawText
            )
        }

        if normalized.contains("light level is too low") {
            return measurementFailure(
                title: "光量が不足しています",
                instruction: "光源を明るくするか、測定器を光源へ近づけてから測定待機へ戻ってください。",
                rawText: rawText
            )
        }

        if normalized.contains("light level is too high") {
            return measurementFailure(
                title: "光量が強すぎます",
                instruction: "光量を下げるか、測定器を光源から離してから測定待機へ戻ってください。",
                rawText: rawText
            )
        }

        if normalized.contains("reading is too short") {
            return measurementFailure(
                title: "測定時間が不足しています",
                instruction: "測定が完了するまで測定器を動かさず、スイッチを必要な時間だけ操作してください。",
                rawText: rawText
            )
        }

        if normalized.contains("reading is inconsistent") {
            return SpotreadIssue(
                kind: .inconsistentReading,
                title: "読み取り値が安定しません",
                instruction: "測定器を固定し、対象や光源が変化していないことを確認してから測定待機へ戻ってください。",
                systemImage: "waveform.path.ecg",
                recoveryAction: .resumeMeasurementLoop,
                rawText: rawText
            )
        }

        if normalized.contains("inconsistent") {
            return SpotreadIssue(
                kind: .inconsistentReading,
                title: "読み取り値が安定しません",
                instruction: "測定器を固定し、対象や光源が変化していないことを確認してから測定待機へ戻ってください。",
                systemImage: "waveform.path.ecg",
                recoveryAction: .resumeMeasurementLoop,
                rawText: rawText
            )
        }

        if normalized.contains("transmission white reference") {
            return measurementFailure(
                title: "透過白基準を読み取れません",
                instruction: "透過白基準の光量と配置を確認してから測定待機へ戻ってください。",
                rawText: rawText
            )
        }

        if normalized.contains("not enough patches")
            || normalized.contains("too many patches")
            || normalized.contains("number of patches to match is wrong") {
            return measurementFailure(
                title: "読み取ったパッチ数が一致しません",
                instruction: "測定範囲と走査方法を確認してから測定待機へ戻ってください。",
                rawText: rawText
            )
        }

        if normalized.contains("not enough samples per patch") {
            return measurementFailure(
                title: "パッチごとの測定量が不足しています",
                instruction: "測定器をよりゆっくり動かして再測定してください。",
                rawText: rawText
            )
        }

        if normalized.contains("no flashes recognized") {
            return measurementFailure(
                title: "フラッシュを検出できません",
                instruction: "フラッシュのタイミングと測定器の向きを確認してから測定待機へ戻ってください。",
                rawText: rawText
            )
        }

        if normalized.contains("no ambient found before first flash") {
            return measurementFailure(
                title: "フラッシュ前の環境光を検出できません",
                instruction: "測定開始後に環境光を保持してからフラッシュを発光してください。",
                rawText: rawText
            )
        }

        if normalized.contains("no refresh rate detected") {
            return measurementFailure(
                title: "リフレッシュレートを検出できません",
                instruction: "表示機器の状態と測定器の向きを確認してから測定待機へ戻ってください。",
                rawText: rawText
            )
        }

        if normalized.contains("no delay calibration transition") {
            return measurementFailure(
                title: "表示遷移を検出できません",
                instruction: "表示機器の切り替わりと測定位置を確認してから測定待機へ戻ってください。",
                rawText: rawText
            )
        }

        if normalized.contains("swipe didn't start and end on the media") {
            return measurementFailure(
                title: "走査範囲が正しくありません",
                instruction: "用紙の上で走査を開始し、用紙の上で終了するように再測定してください。",
                rawText: rawText
            )
        }

        if normalized.contains("battery") && normalized.contains("too low") {
            return measurementFailure(
                title: "測定器のバッテリー残量が不足しています",
                instruction: "測定器を充電または給電してから測定待機へ戻ってください。",
                rawText: rawText
            )
        }

        return measurementFailure(
            title: "測定に失敗しました",
            instruction: "測定器と測定対象を確認してから測定待機へ戻ってください。\nspotread: \(reason)",
            rawText: rawText
        )
    }

    static func communicationFailure(rawText: String) -> SpotreadIssue {
        SpotreadIssue(
            kind: .communicationFailure,
            title: "測定器との通信が切れました",
            instruction: "測定器を接続し直し、macOSに認識されるのを待ってから続行してください。復旧しない場合はspotreadを再起動してください。",
            systemImage: "cable.connector.slash",
            recoveryAction: .resumeMeasurementLoop,
            rawText: rawText
        )
    }

    static func userStopped(rawText: String) -> SpotreadIssue {
        SpotreadIssue(
            kind: .userStopped,
            title: "測定が中断されました",
            instruction: "測定を続ける場合は測定待機へ戻ってください。終了する場合はモード選択へ戻ってください。",
            systemImage: "pause.circle",
            recoveryAction: .resumeMeasurementLoop,
            rawText: rawText
        )
    }

    static func wrongConfiguration(reason: String, rawText: String) -> SpotreadIssue {
        let normalized = reason.lowercased()
        let instruction: String

        if normalized.contains("ambient") {
            instruction = "環境光アダプターを取り付け、センサーを環境光測定位置に合わせてください。"
        } else if normalized.contains("calibration") || normalized.contains("calibration tile") {
            instruction = "必要なアダプターを取り付け、測定器を校正位置または校正タイルに合わせてください。"
        } else if normalized.contains("surface") || normalized.contains("polarization filter") {
            instruction = "反射測定用アダプターを取り付け、センサーを表面測定位置に合わせてください。"
        } else if normalized.contains("projector") {
            instruction = "センサーをプロジェクター測定位置に合わせてください。"
        } else {
            instruction = "測定モードに合うアダプターとセンサー位置へ変更してください。\nspotread: \(reason)"
        }

        return SpotreadIssue(
            kind: .wrongConfiguration,
            title: "測定器の位置またはアダプターが違います",
            instruction: instruction,
            systemImage: "dial.medium",
            recoveryAction: .acknowledgeConfiguration,
            rawText: rawText
        )
    }

    static func outputParsingFailure(rawText: String) -> SpotreadIssue {
        SpotreadIssue(
            kind: .outputParsingFailure,
            title: "spotreadの測定出力を解析できません",
            instruction: "不完全または想定外の測定値を破棄しました。測定器を確認して、もう一度測定してください。詳細はspotread詳細ログで確認できます。",
            systemImage: "text.badge.xmark",
            recoveryAction: .acknowledgeConfiguration,
            rawText: rawText
        )
    }

    static func calibrationFailure(reason: String, rawText: String) -> SpotreadIssue {
        SpotreadIssue(
            kind: .calibrationFailure,
            title: "キャリブレーションに失敗しました",
            instruction: "校正位置、アダプター、白色基準または遮光状態を確認して再試行してください。\nspotread: \(reason)",
            systemImage: "scope",
            recoveryAction: .retryCalibration,
            rawText: rawText
        )
    }

    static func operationFailure(reason: String, rawText: String) -> SpotreadIssue {
        SpotreadIssue(
            kind: .operationFailure,
            title: "測定器の操作に失敗しました",
            instruction: "測定器の状態を確認して操作を再試行してください。\nspotread: \(reason)",
            systemImage: "arrow.clockwise.circle",
            recoveryAction: .retryOperation,
            rawText: rawText
        )
    }

    static func unresponsive(operation: String) -> SpotreadIssue {
        SpotreadIssue(
            kind: .fatalFailure,
            title: "spotreadから応答がありません",
            instruction: "\(operation)が完了しなかったため、spotreadを強制終了しました。測定器の接続と状態を確認して再起動してください。",
            systemImage: "exclamationmark.triangle.fill",
            recoveryAction: .restart,
            rawText: "Response timeout while \(operation)"
        )
    }

    static func fatal(rawText: String) -> SpotreadIssue {
        let normalized = rawText.lowercased()
        let title: String
        let instruction: String

        if normalized.contains("defaulting to emission")
            || normalized.contains("defaulting to transmission") {
            title = "選択した測定モードで動作できません"
            instruction = "測定器が別の測定モードへ自動変更したため、測定値を受理しませんでした。選択したモードに対応する測定器またはアダプターを使用してください。"
        } else if normalized.contains("diagnostic:")
                    || normalized.contains("no instrument at port")
                    || normalized.contains("no instrument detected") {
            title = "指定した測定器を使用できません"
            instruction = "測定器の接続と選択番号を確認してspotreadを再起動してください。"
        } else if normalized.contains("communication") || normalized.contains("initialise communications") {
            title = "測定器との通信を継続できません"
            instruction = "測定器を接続し直してからspotreadを再起動してください。"
        } else if normalized.contains("initialisation failed") {
            title = "測定器を初期化できません"
            instruction = "測定器の接続、電源、使用中のアプリを確認してspotreadを再起動してください。"
        } else if normalized.contains("calibrat") {
            title = "キャリブレーションを継続できません"
            instruction = "測定器の状態を確認してspotreadを再起動してください。"
        } else if normalized.contains("doesn't support") || normalized.contains("not supported") {
            title = "この測定器では選択したモードを使用できません"
            instruction = "対応する測定モードまたは測定器を選択してください。"
        } else {
            title = "spotreadで復旧不能なエラーが発生しました"
            instruction = "測定器の接続と状態を確認してspotreadを再起動してください。\n\(rawText)"
        }

        return SpotreadIssue(
            kind: .fatalFailure,
            title: title,
            instruction: instruction,
            systemImage: "exclamationmark.triangle.fill",
            recoveryAction: .restart,
            rawText: rawText
        )
    }

    private static func measurementFailure(
        title: String,
        instruction: String,
        rawText: String
    ) -> SpotreadIssue {
        SpotreadIssue(
            kind: .measurementFailure,
            title: title,
            instruction: instruction,
            systemImage: "exclamationmark.circle",
            recoveryAction: .resumeMeasurementLoop,
            rawText: rawText
        )
    }
}

extension SpotreadNotice {
    static func from(rawText: String) -> SpotreadNotice {
        let normalized = rawText.lowercased()
        let message: String

        if normalized.contains("high resolution") || normalized.contains("high res") {
            message = "この測定器は高解像度モードに対応していないため、標準解像度で測定します。"
        } else if normalized.contains("80% white patch")
                    || normalized.contains("calibrate refresh frequency") {
            message = "リフレッシュ周波数を校正するため、先に80%の白色パッチを測定してください。"
        } else if normalized.contains("spotread: warning") {
            message = "spotreadから警告が出ています。詳細はspotread詳細ログで確認してください。"
        } else {
            message = "spotreadの指定の一部が測定器に対応していないため、その指定を無効にしました。"
        }

        return SpotreadNotice(message: message, rawText: rawText)
    }
}
