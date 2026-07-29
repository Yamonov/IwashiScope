import AppKit
import Foundation
import SwiftUI
import UniformTypeIdentifiers

enum MeasurementSidebarTab: String, Codable, Hashable, Sendable {
    case measurementValues
    case spotreadLog
}

struct MeasurementWorkspaceView: View {
    @State private var exportErrorMessage = ""
    @State private var showsExportError = false
    @State private var isExporting = false
    @State private var analysisContentHeight: CGFloat = 0
    @State private var usesPracticalSpectrumRange = false

    let mode: MeasurementMode
    let session: MeasurementSession
    let historyStore: MeasurementHistoryStore
    @Binding var selectedSidebarTab: MeasurementSidebarTab
    let onChangeMode: () -> Void
    let onConnectInstrument: () -> Void

    var body: some View {
        ZStack {
            workspaceContent
                .disabled(busyPresentation != nil)

            if let busyPresentation {
                InstrumentBusyOverlay(
                    presentation: busyPresentation,
                    onForceRestart: session.forceRestart
                )
            }
        }
        .toolbar {
            ToolbarItem(placement: .navigation) {
                Button(action: onChangeMode) {
                    Label("モード選択へ戻る", systemImage: "chevron.backward")
                }
                .keyboardShortcut(.cancelAction)
                .help("spotreadを終了してモード選択へ戻ります（Esc）")
            }
        }
        .alert("書き出せませんでした", isPresented: $showsExportError) {
            Button("OK") {}
        } message: {
            Text(exportErrorMessage)
        }
    }

    private var workspaceContent: some View {
        HSplitView {
            GeometryReader { geometry in
                let collapsedHistoryHeight = MeasurementHistoryFooterLayout.collapsedHeight(
                    availableHeight: geometry.size.height,
                    analysisContentHeight: analysisContentHeight,
                    mode: mode
                )

                ZStack(alignment: .bottom) {
                    ScrollView {
                        VStack(spacing: 16) {
                            SpectrumChartView(
                                mode: mode,
                                measurement: displayedMeasurement,
                                calibrationCompleted: session.calibrationCompleted,
                                usesPracticalSpectrumRange: usesPracticalSpectrumRange
                            )

                            if mode != .reflectance {
                                LightingRenderingTabsView(measurement: displayedMeasurement)
                            }
                        }
                        .padding(18)
                        .background {
                            GeometryReader { contentGeometry in
                                Color.clear.preference(
                                    key: MeasurementAnalysisHeightPreferenceKey.self,
                                    value: contentGeometry.size.height
                                )
                            }
                        }
                        .padding(.bottom, collapsedHistoryHeight)
                    }

                    MeasurementHistoryFooter(
                        availableHeight: geometry.size.height,
                        collapsedHeight: collapsedHistoryHeight
                    ) {
                        measurementHistory
                    }
                    .id(mode)
                    .zIndex(1)
                }
                .onPreferenceChange(MeasurementAnalysisHeightPreferenceKey.self) { height in
                    guard height.isFinite,
                          height > 0,
                          abs(analysisContentHeight - height) > 0.5 else {
                        return
                    }
                    analysisContentHeight = height
                }
                .onChange(of: mode) {
                    analysisContentHeight = 0
                }
            }
            .frame(minWidth: 540, maxWidth: .infinity, maxHeight: .infinity, alignment: .top)

            VStack(spacing: 0) {
                CalibrationStatusView(
                    mode: mode,
                    session: session,
                    displayedMeasurement: displayedMeasurement,
                    displayedInstrumentIdentity: displayedEntry?.instrumentIdentity
                        ?? session.instrumentIdentity,
                    usesPracticalSpectrumRange: $usesPracticalSpectrumRange,
                    onConnectInstrument: onConnectInstrument
                )
                    .padding(16)

                TabView(selection: $selectedSidebarTab) {
                    MeasurementDetailsView(
                        measurement: displayedMeasurement,
                        calibrationCompleted: session.calibrationCompleted
                    )
                    .tabItem {
                        Label("測定値", systemImage: "list.bullet.rectangle")
                    }
                    .tag(MeasurementSidebarTab.measurementValues)

                    SpotreadDebugWindowView(session: session)
                        .tabItem {
                            Label("spotread詳細ログ", systemImage: "terminal")
                        }
                        .tag(MeasurementSidebarTab.spotreadLog)
                }

                MeasurementExportFooter(
                    mode: mode,
                    availability: exportAvailability,
                    isExporting: isExporting,
                    onExport: presentMeasurementExportPanel
                )
                .id(mode)
            }
            .frame(minWidth: 440, idealWidth: 440, maxWidth: 440, maxHeight: .infinity)
        }
    }

    @ViewBuilder
    private var measurementHistory: some View {
        if mode == .reflectance {
            MeasurementHistoryView(
                historyStore: historyStore,
                canExportSelectedSwatches: canExportSelectedSwatches,
                onExportSelectedSwatches: exportSelectedSwatches
            )
        } else {
            LightingMeasurementHistoryView(
                mode: mode,
                historyStore: historyStore
            )
        }
    }

    private var busyPresentation: InstrumentBusyPresentation? {
        switch session.phase {
        case .launching:
            InstrumentBusyPresentation(
                title: String(localized: "測定器に接続中"),
                detail: String(localized: "接続と初期化が終わるまで測定器を操作しないでください。")
            )
        case .calibrating:
            InstrumentBusyPresentation(
                title: String(localized: "キャリブレーション中"),
                detail: String(localized: "完了するまで測定器を動かしたり操作したりしないでください。")
            )
        case .measuring:
            InstrumentBusyPresentation(
                title: String(localized: "測定中"),
                detail: String(localized: "測定器を動かさないでください。")
            )
        case .recovering:
            InstrumentBusyPresentation(
                title: String(localized: "spotreadの応答を待っています"),
                detail: String(localized: "待機画面が消えるまで測定器を操作しないでください。")
            )
        default:
            nil
        }
    }

    private var displayedMeasurement: SpotMeasurement? {
        displayedEntry?.measurement ?? session.latestMeasurement
    }

    private var displayedEntry: MeasurementHistoryEntry? {
        historyStore.selectedEntry(for: mode)
    }

    private var selectedEntries: [MeasurementHistoryEntry] {
        historyStore.selectedEntries(for: mode)
    }

    private var exportAvailability: MeasurementExportAvailability {
        MeasurementExportAvailability(entries: selectedEntries)
    }

    private var canExportSelectedSwatches: Bool {
        mode == .reflectance && selectedLabSwatches.isEmpty == false
    }

    private var selectedLabSwatches: [AdobeLabSwatch] {
        let orderedEntries = historyStore.orderedEntries(for: .reflectance)
        let selectedEntries = historyStore.selectedEntries(for: .reflectance)
        let baseNames = MeasurementExportFileNamer.baseNames(
            for: selectedEntries,
            orderedEntries: orderedEntries
        )
        guard selectedEntries.isEmpty == false,
              selectedEntries.allSatisfy({ $0.measurement.lab != nil }) else {
            return []
        }

        return selectedEntries
            .compactMap { entry in
                guard let lab = entry.measurement.lab,
                      let swatchName = baseNames[entry.id] else { return nil }
                return AdobeLabSwatch(
                    name: swatchName,
                    lab: lab
                )
            }
    }

    private func exportSelectedSwatches() {
        let swatches = selectedLabSwatches
        guard swatches.isEmpty == false else {
            presentExportError(String(localized: "Lab値を持つカードだけを1枚以上選択してください。"))
            return
        }

        do {
            let data = try AdobeSwatchExchangeEncoder.encode(swatches: swatches)
            let fileName = selectedSwatchExportFileName
            // Context-menu actions run while AppKit is tracking the menu.
            // Present only after the menu has closed.
            RunLoop.main.perform(inModes: [.default]) {
                Task { @MainActor in
                    presentSwatchSavePanel(
                        data: data,
                        fileName: fileName
                    )
                }
            }
        } catch {
            presentExportError(error.localizedDescription)
        }
    }

    private var selectedSwatchExportFileName: String {
        let orderedEntries = historyStore.orderedEntries(for: .reflectance)
        let selectedEntries = historyStore.selectedEntries(for: .reflectance)
        return MeasurementExportFileNamer.combinedSwatchFileName(
            for: selectedEntries,
            orderedEntries: orderedEntries
        )
    }

    private func presentSwatchSavePanel(
        data: Data,
        fileName: String
    ) {
        let panel = NSSavePanel()
        panel.title = String(localized: "Lab特色スウォッチを書き出し")
        panel.message = String(localized: "選択した履歴カードをLab特色スウォッチとして書き出します。")
        panel.prompt = String(localized: "書き出し")
        panel.nameFieldLabel = String(localized: "ファイル名:")
        panel.nameFieldStringValue = fileName
        panel.allowedContentTypes = [.adobeSwatchExchange]
        panel.canCreateDirectories = true
        panel.isExtensionHidden = false
        panel.canSelectHiddenExtension = true

        let completionHandler: (NSApplication.ModalResponse) -> Void = { response in
            guard response == .OK, let destinationURL = panel.url else { return }

            do {
                try data.write(to: destinationURL, options: .atomic)
            } catch {
                presentExportError(error.localizedDescription)
            }
        }

        if let window = NSApp.keyWindow ?? NSApp.mainWindow {
            panel.beginSheetModal(
                for: window,
                completionHandler: completionHandler
            )
        } else {
            panel.begin(completionHandler: completionHandler)
        }
    }

    private func presentMeasurementExportPanel(
        options: MeasurementExportOptions
    ) {
        let entries = selectedEntries
        guard MeasurementExportAvailability(entries: entries).canExport(
            mode: mode,
            options: options
        ) else {
            presentExportError(String(localized: "書き出す履歴カードと項目を選択してください。"))
            return
        }
        let orderedEntries = historyStore.orderedEntries(for: mode)

        let panel = NSOpenPanel()
        panel.title = String(localized: "測定履歴を書き出し")
        panel.message = String(localized: "選択した履歴カードの書き出し先を選択してください。")
        panel.prompt = String(localized: "書き出し")
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.canCreateDirectories = true
        panel.allowsMultipleSelection = false

        let completionHandler: (NSApplication.ModalResponse) -> Void = { response in
            guard response == .OK, let directoryURL = panel.url else { return }

            isExporting = true
            Task { @MainActor in
                await Task.yield()
                defer { isExporting = false }

                let didAccess = directoryURL.startAccessingSecurityScopedResource()
                defer {
                    if didAccess {
                        directoryURL.stopAccessingSecurityScopedResource()
                    }
                }

                do {
                    try MeasurementExporter.export(
                        entries: entries,
                        orderedEntries: orderedEntries,
                        mode: mode,
                        options: options,
                        to: directoryURL
                    )
                } catch {
                    presentExportError(error.localizedDescription)
                }
            }
        }

        if let window = NSApp.keyWindow ?? NSApp.mainWindow {
            panel.beginSheetModal(
                for: window,
                completionHandler: completionHandler
            )
        } else {
            panel.begin(completionHandler: completionHandler)
        }
    }

    private func presentExportError(_ message: String) {
        exportErrorMessage = message
        showsExportError = true
    }
}

private struct MeasurementAnalysisHeightPreferenceKey: PreferenceKey {
    static let defaultValue: CGFloat = 0

    static func reduce(
        value: inout CGFloat,
        nextValue: () -> CGFloat
    ) {
        value = max(value, nextValue())
    }
}

private struct InstrumentBusyPresentation {
    let title: String
    let detail: String
}

private struct InstrumentBusyOverlay: View {
    @ScaledMetric(relativeTo: .title2) private var diameter = 250.0
    let presentation: InstrumentBusyPresentation
    let onForceRestart: () -> Void

    var body: some View {
        ZStack {
            Rectangle()
                .fill(.black.opacity(0.12))

            VStack(spacing: 12) {
                ProgressView()
                    .controlSize(.large)
                    .scaleEffect(1.5)

                Text(presentation.title)
                    .font(.title2.weight(.bold))
                    .multilineTextAlignment(.center)

                Text(presentation.detail)
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)

                Button("spotreadを強制再起動", action: onForceRestart)
                    .buttonStyle(.bordered)
                    .controlSize(.small)
            }
            .padding(22)
            .frame(width: diameter, height: diameter)
            .background(.regularMaterial, in: Circle())
            .overlay {
                Circle()
                    .stroke(.tint.opacity(0.35), lineWidth: 2)
            }
            .shadow(color: .black.opacity(0.2), radius: 18, y: 8)
            .accessibilityElement(children: .contain)
            .accessibilityLabel(presentation.title)
            .accessibilityHint(presentation.detail)
        }
    }
}
