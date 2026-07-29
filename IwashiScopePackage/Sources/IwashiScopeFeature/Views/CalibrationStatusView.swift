import SwiftUI

struct CalibrationStatusView: View {
    let mode: MeasurementMode
    let session: MeasurementSession
    let displayedMeasurement: SpotMeasurement?
    let displayedInstrumentIdentity: SpotreadInstrumentIdentity?
    @Binding var usesPracticalSpectrumRange: Bool
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

                Divider()

                InstrumentMetadataView(
                    identity: displayedInstrumentIdentity,
                    measurement: displayedMeasurement,
                    isWorkspace: session.phase == .workspace,
                    usesPracticalSpectrumRange: $usesPracticalSpectrumRange
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
                title: localized("測定中"),
                detail: localized("スペクトルと測色値を取得しています。測定器を動かさないでください。"),
                systemImage: "waveform.path.ecg",
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

private struct InstrumentMetadataView: View {
    let identity: SpotreadInstrumentIdentity?
    let measurement: SpotMeasurement?
    let isWorkspace: Bool
    @Binding var usesPracticalSpectrumRange: Bool

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
