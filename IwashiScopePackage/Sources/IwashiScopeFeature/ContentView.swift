import SwiftUI

public struct ContentView: View {
    @State private var selectedMode: MeasurementMode?
    @State private var selectedSidebarTab: MeasurementSidebarTab = .measurementValues
    @State private var model: IwashiScopeApplicationModel
    @State private var workspaceDocument: IwashiScopeWorkspaceDocument?
    @State private var isWorkspaceExporterPresented = false
    @State private var isWorkspaceImporterPresented = false
    @State private var workspaceErrorMessage = ""
    @State private var showsWorkspaceError = false
    @State private var showsExportModeSelection = false
    @State private var showsImportModeSelection = false
    @State private var selectedTransferModes = Set(MeasurementMode.allCases)
    @State private var availableImportModes = Set<MeasurementMode>()
    @State private var pendingImportedDocument: IwashiScopeWorkspaceDocument?
    @State private var pendingSingleModeImport: MeasurementMode?
    @State private var confirmedExportModes: Set<MeasurementMode>?

    public init() {
        let model = IwashiScopeApplicationModel()
        _model = State(initialValue: model)
        _selectedMode = State(initialValue: model.initialWorkspaceState?.selectedMode)
        _selectedSidebarTab = State(
            initialValue: model.initialWorkspaceState?.selectedSidebarTab ?? .measurementValues
        )
    }

    public init(model: IwashiScopeApplicationModel) {
        _model = State(initialValue: model)
        _selectedMode = State(initialValue: model.initialWorkspaceState?.selectedMode)
        _selectedSidebarTab = State(
            initialValue: model.initialWorkspaceState?.selectedSidebarTab ?? .measurementValues
        )
    }

    public var body: some View {
        Group {
            if let selectedMode {
                MeasurementWorkspaceView(
                    mode: selectedMode,
                    session: model.session,
                    historyStore: model.historyStore,
                    userIlluminantStore: model.userIlluminantStore,
                    selectedSidebarTab: $selectedSidebarTab,
                    onChangeMode: returnToModeSelection,
                    onConnectInstrument: {
                        connectInstrument(mode: selectedMode)
                    },
                    onImportHistory: {
                        pendingSingleModeImport = selectedMode
                        isWorkspaceImporterPresented = true
                    },
                    onExportHistory: {
                        beginWorkspaceSave(modes: [selectedMode])
                    }
                )
            } else {
                ModeSelectionView(onSelect: selectMode)
            }
        }
        .frame(minWidth: 1_300, minHeight: 620)
        .navigationTitle(windowTitle)
        .onDisappear {
            model.session.stop()
        }
        .onAppear {
            model.setCurrentWorkspaceSelection(
                mode: selectedMode,
                sidebarTab: selectedSidebarTab
            )
        }
        .onChange(of: selectedMode) { _, mode in
            model.setCurrentWorkspaceSelection(
                mode: mode,
                sidebarTab: selectedSidebarTab
            )
        }
        .onChange(of: selectedSidebarTab) { _, tab in
            model.setCurrentWorkspaceSelection(
                mode: selectedMode,
                sidebarTab: tab
            )
        }
        .onChange(of: model.workspaceMenuRequest) { _, request in
            guard let request else { return }
            handleWorkspaceMenuRequest(request)
        }
        .fileExporter(
            isPresented: $isWorkspaceExporterPresented,
            document: workspaceDocument,
            contentType: .iwashiScopeWorkspace,
            defaultFilename: "IwashiScope Workspace"
        ) { result in
            handleWorkspaceExportCompletion(result)
        }
        .fileDialogMessage("選択したモードの履歴を保存します。")
        .fileDialogConfirmationLabel("保存")
        .fileImporter(
            isPresented: $isWorkspaceImporterPresented,
            allowedContentTypes: [.iwashiScopeWorkspace],
            allowsMultipleSelection: false
        ) { result in
            handleWorkspaceImportCompletion(result)
        }
        .sheet(isPresented: $showsExportModeSelection, onDismiss: {
            if let confirmedExportModes {
                self.confirmedExportModes = nil
                beginWorkspaceSave(modes: confirmedExportModes)
            }
        }) {
            WorkspaceModeSelectionView(
                title: "ワークスペースを書き出し",
                availableModes: Set(MeasurementMode.allCases),
                selectedModes: $selectedTransferModes,
                onCancel: {
                    confirmedExportModes = nil
                    showsExportModeSelection = false
                },
                onConfirm: {
                    confirmedExportModes = selectedTransferModes
                    showsExportModeSelection = false
                }
            )
        }
        .sheet(isPresented: $showsImportModeSelection) {
            WorkspaceModeSelectionView(
                title: "ワークスペースを読み込み",
                availableModes: availableImportModes,
                selectedModes: $selectedTransferModes,
                onCancel: {
                    showsImportModeSelection = false
                    pendingImportedDocument = nil
                },
                onConfirm: importSelectedHistory
            )
        }
        .alert("ワークスペースを処理できませんでした", isPresented: $showsWorkspaceError) {
            Button("OK") {}
        } message: {
            Text(workspaceErrorMessage)
        }
        .alert(
            "測定履歴を処理できませんでした",
            isPresented: historyPersistenceErrorBinding
        ) {
            Button("OK") {
                model.dismissHistoryPersistenceError()
            }
        } message: {
            Text(model.historyPersistenceErrorMessage ?? "")
        }
    }

    private func selectMode(_ mode: MeasurementMode) {
        selectedSidebarTab = model.isBrowsingRestoredWorkspace
            ? .measurementValues
            : .spotreadLog
        selectedMode = mode
        if model.isBrowsingRestoredWorkspace {
            model.presentRestoredWorkspace(mode: mode)
        } else {
            model.session.start(mode: mode)
        }
    }

    private func connectInstrument(mode: MeasurementMode) {
        selectedSidebarTab = .spotreadLog
        model.connectInstrument(mode: mode)
    }

    private var windowTitle: String {
        guard let selectedMode else { return "IwashiScope" }
        return "IwashiScope　　\(selectedMode.title)"
    }

    private func returnToModeSelection() {
        selectedSidebarTab = .measurementValues
        selectedMode = nil
        model.session.stop()
    }

    private func handleWorkspaceMenuRequest(_ request: WorkspaceMenuRequest) {
        switch request.operation {
        case .save:
            selectedTransferModes = Set(MeasurementMode.allCases)
            showsExportModeSelection = true
        case .restore:
            pendingSingleModeImport = nil
            isWorkspaceImporterPresented = true
        }
    }

    private func beginWorkspaceSave(modes: Set<MeasurementMode>) {
        let state = IwashiScopeWorkspaceState(
            selectedMode: selectedMode,
            selectedSidebarTab: selectedSidebarTab,
            history: model.historyStore.workspaceSnapshot().containingOnly(modes)
        )
        do {
            workspaceDocument = try IwashiScopeWorkspaceDocument(workspace: state)
            isWorkspaceExporterPresented = true
        } catch {
            presentWorkspaceError(error)
        }
    }

    private func handleWorkspaceExportCompletion(_ result: Result<URL, Error>) {
        defer {
            workspaceDocument = nil
        }

        switch result {
        case .success:
            break
        case .failure(let error):
            guard isUserCancellation(error) == false else { return }
            presentWorkspaceError(error)
        }
    }

    private func handleWorkspaceImportCompletion(_ result: Result<[URL], Error>) {
        switch result {
        case .success(let urls):
            guard let url = urls.first else { return }
            readWorkspaceForImport(from: url)
        case .failure(let error):
            guard isUserCancellation(error) == false else { return }
            presentWorkspaceError(error)
        }
    }

    private func readWorkspaceForImport(from url: URL) {
        let accessesSecurityScopedResource = url.startAccessingSecurityScopedResource()
        defer {
            if accessesSecurityScopedResource {
                url.stopAccessingSecurityScopedResource()
            }
        }

        do {
            let data = try Data(contentsOf: url)
            let document = try IwashiScopeWorkspaceDocument(data: data)
            let available = Set(document.archive.workspace.history.entries.map {
                $0.measurement.mode
            })
            if let pendingSingleModeImport {
                try model.historyStore.importEntries(
                    from: document.archive.workspace.history,
                    modes: [pendingSingleModeImport]
                )
                self.pendingSingleModeImport = nil
            } else {
                pendingImportedDocument = document
                availableImportModes = available
                selectedTransferModes = available
                showsImportModeSelection = true
            }
        } catch {
            presentWorkspaceError(error)
        }
    }

    private func importSelectedHistory() {
        defer {
            showsImportModeSelection = false
            pendingImportedDocument = nil
        }
        guard let pendingImportedDocument else { return }
        do {
            try model.historyStore.importEntries(
                from: pendingImportedDocument.archive.workspace.history,
                modes: selectedTransferModes
            )
        } catch {
            presentWorkspaceError(error)
        }
    }

    private func presentWorkspaceError(_ error: Error) {
        workspaceErrorMessage = error.localizedDescription
        showsWorkspaceError = true
    }

    private func isUserCancellation(_ error: Error) -> Bool {
        let cocoaError = error as NSError
        return cocoaError.domain == NSCocoaErrorDomain
            && cocoaError.code == NSUserCancelledError
    }

    private var historyPersistenceErrorBinding: Binding<Bool> {
        Binding(
            get: { model.historyPersistenceErrorMessage != nil },
            set: { isPresented in
                if isPresented == false {
                    model.dismissHistoryPersistenceError()
                }
            }
        )
    }
}

private struct WorkspaceModeSelectionView: View {
    let title: LocalizedStringKey
    let availableModes: Set<MeasurementMode>
    @Binding var selectedModes: Set<MeasurementMode>
    let onCancel: () -> Void
    let onConfirm: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(title).font(.headline)
            ForEach(MeasurementMode.allCases) { mode in
                Toggle(mode.title, isOn: Binding(
                    get: { selectedModes.contains(mode) },
                    set: { isSelected in
                        if isSelected {
                            selectedModes.insert(mode)
                        } else {
                            selectedModes.remove(mode)
                        }
                    }
                ))
                .disabled(availableModes.contains(mode) == false)
            }
            HStack {
                Spacer()
                Button("キャンセル", action: onCancel)
                Button("OK", action: onConfirm)
                    .keyboardShortcut(.defaultAction)
                    .disabled(selectedModes.isEmpty)
            }
        }
        .padding(20)
        .frame(width: 320)
    }
}
