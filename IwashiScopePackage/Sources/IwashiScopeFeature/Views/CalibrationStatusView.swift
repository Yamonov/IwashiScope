import SwiftUI

struct CalibrationStatusView: View {
    let mode: MeasurementMode
    let session: MeasurementSession
    let displayedMeasurement: SpotMeasurement?
    let displayedInstrumentIdentity: SpotreadInstrumentIdentity?
    @Binding var usesPracticalSpectrumRange: Bool
    @Binding var spectrumYAxisConfiguration: SpectrumYAxisConfiguration
    let onConnectInstrument: () -> Void

    var body: some View {
        GroupBox {
            VStack(alignment: .leading, spacing: 10) {
                InstrumentModeView(mode: mode)

                Divider()

                HStack(alignment: .top, spacing: 12) {
                    statusSymbol

                    VStack(alignment: .leading, spacing: 6) {
                        HStack(alignment: .firstTextBaseline, spacing: 8) {
                            Text(presentation.title)
                                .font(
                                    .title3.weight(
                                        emphasizesErrorTitle ? .bold : .semibold
                                    )
                                )
                                .foregroundStyle(
                                    emphasizesErrorTitle ? Color.red : Color.primary
                                )

                            Spacer(minLength: 8)

                            if session.phase == .ready, session.calibrationCompleted {
                                Label("Calibration Done", systemImage: "checkmark.seal.fill")
                                    .font(.callout.weight(.medium))
                                    .foregroundStyle(.green)
                                    .accessibilityLabel("キャリブレーション完了")
                            }
                        }
                        .frame(maxWidth: .infinity)

                        Text(presentation.detail)
                            .font(.body)
                            .foregroundStyle(.secondary)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }

                if let notice = session.notice {
                    Label(notice.message, systemImage: "exclamationmark.triangle")
                        .font(.callout)
                        .foregroundStyle(.orange)
                        .fixedSize(horizontal: false, vertical: true)
                }

                controls
                    .frame(maxWidth: .infinity, alignment: .trailing)

                if showsAveragingControls {
                    Divider()

                    AveragingMeasurementControls(session: session)
                }

                Divider()

                InstrumentMetadataView(
                    identity: displayedInstrumentIdentity,
                    measurement: displayedMeasurement,
                    isWorkspace: session.phase == .workspace,
                    usesPracticalSpectrumRange: $usesPracticalSpectrumRange,
                    spectrumYAxisConfiguration: $spectrumYAxisConfiguration
                )
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(4)
        }
    }

    private var emphasizesErrorTitle: Bool {
        switch session.phase {
        case .retryAvailable, .configurationRequired, .failed:
            true
        default:
            false
        }
    }

    private var showsAveragingControls: Bool {
        session.phase == .ready || session.isAveragingMeasurement
    }

    private var statusSymbol: some View {
        ZStack {
            Circle()
                .fill(presentation.color.opacity(0.14))
            Image(systemName: presentation.systemImage)
                .font(.system(size: 24, weight: .semibold))
                .scaleEffect(1.4)
                .offset(y: -2)
                .foregroundStyle(presentation.color)
                .accessibilityHidden(true)
        }
        .frame(width: 52, height: 52)
    }

    @ViewBuilder
    private var controls: some View {
        switch session.phase {
        case .launching, .calibrating, .measuring, .recovering:
            ProgressView()
                .controlSize(.small)

        case .calibrationRecommended:
            Button {
                session.beginCalibration()
            } label: {
                Label("キャリブレーション", systemImage: "scope")
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .frame(maxWidth: .infinity)
            .keyboardShortcut(.defaultAction)

        case .awaitingCalibrationSetup:
            VStack(alignment: .trailing, spacing: 8) {
                Button {
                    session.continueCalibration()
                } label: {
                    Label("キャリブレーション", systemImage: "scope")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .frame(maxWidth: .infinity)
                .keyboardShortcut(.defaultAction)

                if session.calibrationPrompt?.allowsSkip == true {
                    Button("今回はスキップ") {
                        session.skipCalibration()
                    }
                }
            }
            .frame(maxWidth: .infinity)

        case .waitingForInstrument:
            ProgressView("測定器のスイッチ入力を待っています")
                .controlSize(.small)

        case .ready:
            EqualWidthControlGroup {
                Button {
                    session.takeReading()
                } label: {
                    Label("測定", systemImage: "record.circle")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .keyboardShortcut(.defaultAction)

                Button {
                    session.beginCalibration()
                } label: {
                    Label("再キャリブレーション", systemImage: "arrow.clockwise")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.bordered)
            }

        case .retryAvailable:
            if let issue = session.activeIssue {
                TrailingControlGroup {
                    Button {
                        session.recoverFromIssue()
                    } label: {
                        Label(issue.recoveryButtonTitle, systemImage: issue.recoveryButtonSystemImage)
                    }
                    .buttonStyle(.borderedProminent)
                    .keyboardShortcut(.defaultAction)

                    if issue.offersRestartAlternative {
                        Button {
                            session.forceRestart()
                        } label: {
                            Label("spotreadを強制再起動", systemImage: "arrow.clockwise.circle")
                        }
                    }
                }
            }

        case .configurationRequired:
            if let issue = session.activeIssue {
                Button {
                    session.recoverFromIssue()
                } label: {
                    Label(issue.recoveryButtonTitle, systemImage: issue.recoveryButtonSystemImage)
                }
                .buttonStyle(.borderedProminent)
                .keyboardShortcut(.defaultAction)
            }

        case .workspace:
            Button {
                onConnectInstrument()
            } label: {
                Label("測定器に接続", systemImage: "cable.connector")
            }
            .buttonStyle(.borderedProminent)

        case .failed, .stopped:
            Button {
                session.restart()
            } label: {
                Label("spotreadを再起動", systemImage: "play.fill")
            }
            .buttonStyle(.borderedProminent)

        case .idle:
            EmptyView()
        }
    }

    private var presentation: StatusPresentation {
        switch session.phase {
        case .idle:
            StatusPresentation(
                title: localized("待機中"),
                detail: localized("測定モードを選択してください。"),
                systemImage: "pause.fill",
                color: .secondary
            )
        case .launching:
            StatusPresentation(
                title: localized("spotreadを起動中"),
                detail: localized("測定器への接続と初期化を待っています。"),
                systemImage: "ellipsis",
                color: .blue
            )
        case .calibrationRecommended:
            StatusPresentation(
                title: localized("キャリブレーションしてください"),
                detail: localized("測定前に測定器をキャリブレーションします。開始後、表示される手順に従ってください。"),
                systemImage: "scope",
                color: .orange
            )
        case .awaitingCalibrationSetup:
            StatusPresentation(
                title: session.calibrationPrompt?.title ?? localized("キャリブレーションの準備"),
                detail: calibrationSetupDetail,
                systemImage: session.calibrationPrompt?.systemImage ?? "scope",
                color: .orange
            )
        case .waitingForInstrument:
            StatusPresentation(
                title: session.calibrationPrompt?.title ?? localized("測定器を操作してください"),
                detail: session.calibrationPrompt?.instruction ?? localized("測定器のスイッチを押してください。"),
                systemImage: session.calibrationPrompt?.systemImage ?? "hand.tap",
                color: .orange
            )
        case .calibrating:
            StatusPresentation(
                title: localized("キャリブレーション中"),
                detail: localized("測定器から完了通知が返るまで、そのままお待ちください。"),
                systemImage: "hourglass",
                color: .blue
            )
        case .ready:
            StatusPresentation(
                title: localized("測定待機中"),
                detail: readyMeasurementInstruction,
                systemImage: "checkmark.circle.fill",
                color: .green
            )
        case .measuring:
            StatusPresentation(
                title: session.isFinalizingAveragingMeasurement
                    ? localized("平均値を再計算中")
                    : localized("測定中"),
                detail: session.isFinalizingAveragingMeasurement
                    ? localized("平均スペクトルから測色値と演色評価値を再計算しています。")
                    : localized("スペクトルと測色値を取得しています。測定器を動かさないでください。"),
                systemImage: session.isFinalizingAveragingMeasurement
                    ? "function"
                    : "waveform.path.ecg",
                color: .blue
            )
        case .recovering:
            StatusPresentation(
                title: localized("spotreadの応答を待っています"),
                detail: localized("エラー処理が終わるまで測定器を操作しないでください。応答が戻らない場合は自動的に強制再起動します。"),
                systemImage: "arrow.clockwise.circle",
                color: .blue
            )
        case .retryAvailable:
            StatusPresentation(
                title: session.activeIssue?.title ?? localized("操作を再試行できます"),
                detail: session.activeIssue?.instruction ?? localized("測定器の状態を確認してから再試行してください。"),
                systemImage: session.activeIssue?.systemImage ?? "arrow.clockwise.circle",
                color: .orange
            )
        case .configurationRequired:
            StatusPresentation(
                title: session.activeIssue?.title ?? localized("測定器の設定を確認してください"),
                detail: session.activeIssue?.instruction ?? localized("測定モードに合う位置とアダプターへ変更してください。"),
                systemImage: session.activeIssue?.systemImage ?? "dial.medium",
                color: .orange
            )
        case .workspace:
            StatusPresentation(
                title: localized("ワークスペースを表示中"),
                detail: localized("保存された測定結果を表示しています。測定器には接続していません。"),
                systemImage: "folder",
                color: .blue
            )
        case .stopped:
            StatusPresentation(
                title: localized("spotreadは停止しました"),
                detail: localized("再起動するか、別の測定モードを選択してください。"),
                systemImage: "stop.circle",
                color: .secondary
            )
        case .failed:
            StatusPresentation(
                title: session.activeIssue?.title ?? localized("spotreadを実行できません"),
                detail: session.activeIssue?.instruction ?? session.errorMessage ?? localized("不明なエラーが発生しました。"),
                systemImage: session.activeIssue?.systemImage ?? "exclamationmark.triangle.fill",
                color: .red
            )
        }
    }

    private var readyMeasurementInstruction: String {
        if mode == .ambient {
            return localized("本体の設定を環境光測定位置にし、測定位置に静置して測定ボタンまたは測定器本体のスイッチを押してください")
        }
        return localized("測定対象に測定器を置き、測定ボタンまたは測定器本体のスイッチを押してください。")
    }

    private var calibrationSetupDetail: String {
        let instruction = session.calibrationPrompt?.instruction
            ?? localized("測定器を校正位置へ移動してください。")
        return String(
            localized: """
            \(instruction)
            設置後、測定器を動かさず8〜10秒程度待ってからキャリブレーションを開始してください。
            """
        )
    }

    private func localized(_ resource: LocalizedStringResource) -> String {
        String(localized: resource)
    }
}

private struct AveragingMeasurementControls: View {
    let session: MeasurementSession

    private var acceptedCount: Int {
        session.averagingAccumulator.acceptedCount
    }

    private var measurementAttemptCount: Int {
        session.averagingAccumulator.measurementAttemptCount
    }

    private var actionTitle: String {
        if session.isFinalizingAveragingMeasurement {
            return String(localized: "平均値を計算中")
        }
        guard session.isAveragingMeasurement else {
            return String(localized: "平均化測定を開始")
        }
        return session.averagingAccumulator.canOutputAverage
            ? String(localized: "平均値を出力")
            : String(localized: "平均値モードを終了")
    }

    private var actionSystemImage: String {
        if session.isFinalizingAveragingMeasurement {
            return "function"
        }
        return session.isAveragingMeasurement
            ? "waveform.path.ecg.rectangle"
            : "square.stack.3d.up"
    }

    private var message: String {
        if session.supportsSpectrumAnalysis == false {
            return String(
                localized: "同梱のspotreadが平均スペクトルの再計算に対応していません。"
            )
        }
        if let averagingMessage = session.averagingMessage {
            return averagingMessage
        }
        return String(
            localized: "6回から平均値を出力できます。10回以上を推奨し、20回で自動出力します。"
        )
    }

    private var isActionDisabled: Bool {
        session.phase != .ready
            || session.supportsSpectrumAnalysis == false
            || session.isFinalizingAveragingMeasurement
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 10) {
                Button {
                    if session.isAveragingMeasurement {
                        session.finishOrCancelAveragingMeasurement()
                    } else {
                        session.startAveragingMeasurement()
                    }
                } label: {
                    Label(actionTitle, systemImage: actionSystemImage)
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.bordered)
                .disabled(isActionDisabled)

                AveragingProgressIndicator(
                    acceptedCount: acceptedCount,
                    measurementCount: measurementAttemptCount,
                    tier: session.averagingAccumulator.progressTier
                )
                .opacity(measurementAttemptCount == 0 ? 0.35 : 1)
            }

            AveragingQualityIndicator(
                acceptedCount: acceptedCount,
                measurementCount: measurementAttemptCount,
                outlierCount: session.averagingAccumulator.outlierCount,
                convergence: session.averagingAccumulator.convergence
            )
            .opacity(measurementAttemptCount == 0 ? 0.35 : 1)

            Text(message)
                .font(.caption)
                .foregroundStyle(messageColor)
                .fixedSize(horizontal: false, vertical: true)
                .accessibilityIdentifier("averaging-measurement-message")
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var messageColor: Color {
        if message.hasPrefix(String(localized: "異常値です")) {
            return .orange
        }
        return .secondary
    }
}

private struct AveragingProgressIndicator: View {
    let acceptedCount: Int
    let measurementCount: Int
    let tier: AveragingProgressTier

    private var color: Color {
        switch tier {
        case .insufficient:
            .red
        case .minimum:
            .brown
        case .recommended:
            .blue
        case .sufficient:
            .green
        }
    }

    var body: some View {
        VStack(alignment: .trailing, spacing: 3) {
            Text(
                "\(acceptedCount)（\(measurementCount)）/\(AveragingMeasurementAccumulator.maximumCount)"
            )
                .font(.caption.monospacedDigit().weight(.semibold))
                .foregroundStyle(color)

            ProgressView(
                value: Double(acceptedCount),
                total: Double(AveragingMeasurementAccumulator.maximumCount)
            )
            .progressViewStyle(.linear)
            .tint(color)
        }
        .frame(width: 116)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("平均化測定の進捗")
        .accessibilityValue(
            "採用\(acceptedCount)回、実測\(measurementCount)回、最大\(AveragingMeasurementAccumulator.maximumCount)回"
        )
        .help("括弧内は異常値などを含む実測回数です。")
    }
}

private struct AveragingQualityIndicator: View {
    let acceptedCount: Int
    let measurementCount: Int
    let outlierCount: Int
    let convergence: AveragingConvergence?

    private var convergenceColor: Color {
        guard let convergence else { return .secondary }
        switch convergence.tier {
        case .highVariation:
            return .red
        case .converging:
            return .brown
        case .stable:
            return .blue
        case .sufficientlyStable:
            return .green
        }
    }

    private var convergenceName: String {
        guard let convergence else {
            return String(localized: "判定前")
        }
        if convergence.tier == .stable,
           convergence.relative95Uncertainty <= 0.0075,
           acceptedCount < AveragingMeasurementAccumulator.sufficientCount {
            return String(localized: "安定（回数を追加）")
        }
        return convergence.tier.localizedName
    }

    private var convergenceDescription: String {
        guard let convergence else {
            return String(localized: "収束度：判定前")
        }
        let uncertainty = convergence.relative95UncertaintyPercent.formatted(
            .number.precision(.fractionLength(1))
        )
        return String(
            localized: "収束度：\(convergenceName)　95%誤差目安 ±\(uncertainty)%"
        )
    }

    private var accessibilityValue: String {
        guard let convergence else {
            return String(
                localized: "異常値\(outlierCount)回、収束度判定前、採用\(acceptedCount)回、実測\(measurementCount)回"
            )
        }
        let uncertainty = convergence.relative95UncertaintyPercent.formatted(
            .number.precision(.fractionLength(1))
        )
        return String(
            localized: "異常値\(outlierCount)回、収束度\(convergenceName)、95パーセント誤差目安プラスマイナス\(uncertainty)パーセント"
        )
    }

    var body: some View {
        VStack(spacing: 3) {
            HStack(spacing: 8) {
                Text("異常値 \(outlierCount)回")
                    .foregroundStyle(
                        outlierCount == 0 ? Color.secondary : Color.orange
                    )

                Spacer(minLength: 8)

                Text(convergenceDescription)
                    .foregroundStyle(convergenceColor)
                    .multilineTextAlignment(.trailing)
            }
            .font(.caption2.monospacedDigit())

            ProgressView(value: convergence?.progress ?? 0, total: 1)
                .progressViewStyle(.linear)
                .tint(convergenceColor)
        }
        .frame(maxWidth: .infinity)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("平均化測定の品質")
        .accessibilityValue(accessibilityValue)
    }
}

private struct InstrumentMetadataView: View {
    let identity: SpotreadInstrumentIdentity?
    let measurement: SpotMeasurement?
    let isWorkspace: Bool
    @Binding var usesPracticalSpectrumRange: Bool
    @Binding var spectrumYAxisConfiguration: SpectrumYAxisConfiguration

    var body: some View {
        VStack(spacing: 6) {
            InstrumentMetadataRow(
                label: String(localized: "測定器名（シリアル）"),
                value: instrumentNameAndSerial
            )
            InstrumentMetadataRow(
                label: String(localized: "波長範囲"),
                value: wavelengthRange
            )
            InstrumentMetadataRow(
                label: String(localized: "データ点数"),
                value: dataPointCount
            )
            InstrumentMetadataRow(
                label: String(localized: "実用波長範囲"),
                value: practicalWavelengthRange
            )
            InstrumentMetadataToggleRow(
                label: String(localized: "実用エリアを使用する"),
                isOn: practicalRangeToggle
            )
            .disabled(hasPracticalWavelengthRange == false)
            .help(practicalRangeToggleHelp)
            InstrumentMetadataYAxisRow(
                configuration: $spectrumYAxisConfiguration
            )
        }
        .font(.caption)
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var instrumentNameAndSerial: String {
        switch (identity?.name, identity?.serialNumber) {
        case let (name?, serialNumber?):
            "\(name)（S/N \(serialNumber)）"
        case let (name?, nil):
            name
        case let (nil, serialNumber?):
            "S/N \(serialNumber)"
        case (nil, nil):
            isWorkspace ? String(localized: "保存データなし") : String(localized: "取得中…")
        }
    }

    private var wavelengthRange: String {
        guard let measurement else {
            return isWorkspace ? String(localized: "保存データなし") : String(localized: "測定後に表示")
        }
        return "\(format(measurement.spectrumStart))–\(format(measurement.spectrumEnd)) nm"
    }

    private var dataPointCount: String {
        guard let measurement else {
            return isWorkspace ? String(localized: "保存データなし") : String(localized: "測定後に表示")
        }
        return "\(measurement.spectrum.count)"
    }

    private var practicalWavelengthRange: String {
        guard let measurement else {
            return isWorkspace ? String(localized: "保存データなし") : String(localized: "測定後に表示")
        }
        guard let range = measurement.validatedPracticalSpectrumRange else {
            return String(localized: "保存データなし")
        }
        return "\(format(range.start))–\(format(range.end)) nm"
    }

    private var hasPracticalWavelengthRange: Bool {
        measurement?.validatedPracticalSpectrumRange != nil
    }

    private var practicalRangeToggle: Binding<Bool> {
        Binding(
            get: {
                hasPracticalWavelengthRange && usesPracticalSpectrumRange
            },
            set: { newValue in
                usesPracticalSpectrumRange = newValue
            }
        )
    }

    private var practicalRangeToggleHelp: String {
        if hasPracticalWavelengthRange {
            return String(localized: "スペクトルグラフを実用波長範囲に絞って表示します")
        }
        return String(localized: "実用波長範囲を含む測定データがありません")
    }

    private func format(_ value: Double) -> String {
        value.formatted(.number.precision(.fractionLength(0...1)))
    }
}

private struct InstrumentMetadataYAxisRow: View {
    @Binding var configuration: SpectrumYAxisConfiguration

    var body: some View {
        HStack(spacing: 8) {
            Text("縦軸")
                .foregroundStyle(.secondary)

            Spacer(minLength: 6)

            Picker("縦軸", selection: $configuration.mode) {
                Text("自動").tag(SpectrumYAxisMode.automatic)
                Text("固定").tag(SpectrumYAxisMode.fixed)
            }
            .labelsHidden()
            .pickerStyle(.radioGroup)
            .horizontalRadioGroupLayout()
            .fixedSize()
            .accessibilityIdentifier("spectrum-y-axis-mode-picker")

            Slider(
                value: $configuration.fixedUpperBound,
                in: SpectrumYAxisConfiguration.fixedUpperBoundRange,
                step: SpectrumYAxisConfiguration.fixedUpperBoundStep
            )
            .disabled(configuration.mode != .fixed)
            .frame(minWidth: 90)
            .accessibilityLabel("縦軸の固定上限")
            .accessibilityIdentifier("spectrum-y-axis-maximum-slider")

            Text(
                configuration.normalizedFixedUpperBound,
                format: .number.precision(.fractionLength(0))
            )
            .monospacedDigit()
            .frame(width: 28, alignment: .trailing)
            .accessibilityHidden(true)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .help("固定時の縦軸上限を10から500まで10刻みで設定します")
    }
}

private struct InstrumentMetadataToggleRow: View {
    let label: String
    @Binding var isOn: Bool

    var body: some View {
        Toggle(label, isOn: $isOn)
            .toggleStyle(.checkbox)
            .frame(maxWidth: .infinity, alignment: .leading)
            .accessibilityIdentifier("use-practical-spectrum-range-toggle")
    }
}

private struct InstrumentMetadataRow: View {
    let label: String
    let value: String

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 10) {
            Text(label)
                .foregroundStyle(.secondary)

            Spacer(minLength: 8)

            Text(value)
                .multilineTextAlignment(.trailing)
                .textSelection(.enabled)
        }
        .accessibilityElement(children: .combine)
    }
}

private struct InstrumentModeView: View {
    let mode: MeasurementMode

    var body: some View {
        HStack(spacing: 14) {
            Image(systemName: mode.systemImage)
                .font(.largeTitle)
                .foregroundStyle(.tint)
                .frame(width: 40)
                .accessibilityHidden(true)

            Text(mode.title)
                .font(.largeTitle.weight(.semibold))
        }
        .padding(.vertical, 4)
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("測定モード \(mode.title)")
    }
}

private struct StatusPresentation {
    let title: String
    let detail: String
    let systemImage: String
    let color: Color
}

private struct TrailingControlGroup<Content: View>: View {
    let content: Content

    init(@ViewBuilder content: () -> Content) {
        self.content = content()
    }

    var body: some View {
        ViewThatFits(in: .horizontal) {
            HStack(spacing: 8) {
                content
            }

            VStack(alignment: .trailing, spacing: 8) {
                content
            }
        }
    }
}

private struct EqualWidthControlGroup<Content: View>: View {
    let content: Content

    init(@ViewBuilder content: () -> Content) {
        self.content = content()
    }

    var body: some View {
        ViewThatFits(in: .horizontal) {
            EqualWidthControlLayout(axis: .horizontal, spacing: 8) {
                content
            }

            EqualWidthControlLayout(axis: .vertical, spacing: 8) {
                content
            }
        }
    }
}

private struct EqualWidthControlLayout: Layout {
    let axis: Axis
    let spacing: CGFloat

    func sizeThatFits(
        proposal: ProposedViewSize,
        subviews: Subviews,
        cache: inout ()
    ) -> CGSize {
        let itemSize = maximumItemSize(for: subviews)
        let itemCount = CGFloat(subviews.count)
        let totalSpacing = spacing * CGFloat(max(0, subviews.count - 1))

        switch axis {
        case .horizontal:
            return CGSize(
                width: (itemSize.width * itemCount) + totalSpacing,
                height: itemSize.height
            )
        case .vertical:
            return CGSize(
                width: itemSize.width,
                height: (itemSize.height * itemCount) + totalSpacing
            )
        }
    }

    func placeSubviews(
        in bounds: CGRect,
        proposal: ProposedViewSize,
        subviews: Subviews,
        cache: inout ()
    ) {
        let itemSize = maximumItemSize(for: subviews)
        let itemProposal = ProposedViewSize(width: itemSize.width, height: itemSize.height)
        var position = bounds.origin

        for subview in subviews {
            subview.place(
                at: position,
                anchor: .topLeading,
                proposal: itemProposal
            )

            switch axis {
            case .horizontal:
                position.x += itemSize.width + spacing
            case .vertical:
                position.y += itemSize.height + spacing
            }
        }
    }

    private func maximumItemSize(for subviews: Subviews) -> CGSize {
        subviews.reduce(into: .zero) { result, subview in
            let size = subview.sizeThatFits(.unspecified)
            result.width = max(result.width, size.width)
            result.height = max(result.height, size.height)
        }
    }
}
