import SwiftUI

public struct ContentView: View {
    @State private var selectedMode: MeasurementMode?
    @State private var selectedSidebarTab: MeasurementSidebarTab = .measurementValues
    @State private var model: IwashiScopeApplicationModel
    @State private var workspaceDocument: IwashiScopeWorkspaceDocument?
    @State private var workspaceStateBeingSaved: IwashiScopeWorkspaceState?
    @State private var isWorkspaceExporterPresented = false
    @State private var isWorkspaceImporterPresented = false
    @State private var showsUnsavedWorkspaceConfirmation = false
    @State private var restoresWorkspaceAfterSaving = false
    @State private var workspaceErrorMessage = ""
    @State private var showsWorkspaceError = false

    public init() {
        _model = State(initialValue: IwashiScopeApplicationModel())
    }

    public init(model: IwashiScopeApplicationModel) {
        _model = State(initialValue: model)
    }

    public var body: some View {
        Group {
            if let selectedMode {
                MeasurementWorkspaceView(
                    mode: selectedMode,
                    session: model.session,
                    historyStore: model.historyStore,
                    selectedSidebarTab: $selectedSidebarTab,
                    onChangeMode: returnToModeSelection,
                    onConnectInstrument: {
                        model.connectInstrument(mode: selectedMode)
                    }
                )
            } else {
                ModeSelectionView(onSelect: selectMode)
            }
        }
        .frame(minWidth: 860, minHeight: 620)
        .navigationTitle(windowTitle)
        .onDisappear {
            model.session.stop()
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
        .fileDialogMessage("現在の測定履歴とワークスペースを保存します。")
        .fileDialogConfirmationLabel("保存")
        .fileImporter(
            isPresented: $isWorkspaceImporterPresented,
            allowedContentTypes: [.iwashiScopeWorkspace],
            allowsMultipleSelection: false
        ) { result in
            handleWorkspaceImportCompletion(result)
        }
        .confirmationDialog(
            "現在のワークスペースを保存しますか？",
            isPresented: $showsUnsavedWorkspaceConfirmation,
            titleVisibility: .visible
        ) {
            Button("保存") {
                beginWorkspaceSave(restoresWorkspaceAfterSaving: true)
            }
            Button("保存せずに復帰", role: .destructive) {
                isWorkspaceImporterPresented = true
            }
            Button("キャンセル", role: .cancel) {}
        } message: {
            Text("保存していない変更は、ワークスペースを復帰すると失われます。")
        }
        .alert("ワークスペースを処理できませんでした", isPresented: $showsWorkspaceError) {
            Button("OK") {}
        } message: {
            Text(workspaceErrorMessage)
        }
    }

    private func selectMode(_ mode: MeasurementMode) {
        selectedSidebarTab = .measurementValues
        selectedMode = mode
        if model.isBrowsingRestoredWorkspace {
            model.presentRestoredWorkspace(mode: mode)
        } else {
            model.session.start(mode: mode)
        }
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
            beginWorkspaceSave(restoresWorkspaceAfterSaving: false)
        case .restore:
            requestWorkspaceRestore()
        }
    }

    private func beginWorkspaceSave(restoresWorkspaceAfterSaving: Bool) {
        let state = currentWorkspaceState
        do {
            workspaceDocument = try IwashiScopeWorkspaceDocument(workspace: state)
            workspaceStateBeingSaved = state
            self.restoresWorkspaceAfterSaving = restoresWorkspaceAfterSaving
            isWorkspaceExporterPresented = true
        } catch {
            presentWorkspaceError(error)
        }
    }

    private func requestWorkspaceRestore() {
        if model.hasUnsavedWorkspace(currentWorkspaceState) {
            showsUnsavedWorkspaceConfirmation = true
        } else {
            isWorkspaceImporterPresented = true
        }
    }

    private var currentWorkspaceState: IwashiScopeWorkspaceState {
        model.workspaceState(
            selectedMode: selectedMode,
            selectedSidebarTab: selectedSidebarTab
        )
    }

    private func handleWorkspaceExportCompletion(_ result: Result<URL, Error>) {
        let shouldRestore = restoresWorkspaceAfterSaving
        defer {
            workspaceDocument = nil
            workspaceStateBeingSaved = nil
            restoresWorkspaceAfterSaving = false
        }

        switch result {
        case .success:
            if let workspaceStateBeingSaved {
                model.markWorkspaceSaved(workspaceStateBeingSaved)
            }
            if shouldRestore {
                isWorkspaceImporterPresented = true
            }
        case .failure(let error):
            guard isUserCancellation(error) == false else { return }
            presentWorkspaceError(error)
        }
    }

    private func handleWorkspaceImportCompletion(_ result: Result<[URL], Error>) {
        switch result {
        case .success(let urls):
            guard let url = urls.first else { return }
            restoreWorkspace(from: url)
        case .failure(let error):
            guard isUserCancellation(error) == false else { return }
            presentWorkspaceError(error)
        }
    }

    private func restoreWorkspace(from url: URL) {
        let accessesSecurityScopedResource = url.startAccessingSecurityScopedResource()
        defer {
            if accessesSecurityScopedResource {
                url.stopAccessingSecurityScopedResource()
            }
        }

        do {
            let data = try Data(contentsOf: url)
            let document = try IwashiScopeWorkspaceDocument(data: data)
            let restoredState = document.archive.workspace
            try model.restoreWorkspace(restoredState)
            selectedMode = restoredState.selectedMode
            selectedSidebarTab = restoredState.selectedSidebarTab

            if let selectedMode = restoredState.selectedMode {
                model.presentRestoredWorkspace(mode: selectedMode)
            }
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
}
