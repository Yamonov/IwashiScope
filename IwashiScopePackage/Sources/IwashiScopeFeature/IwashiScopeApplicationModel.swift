import Foundation
import Observation

@MainActor
@Observable
public final class IwashiScopeApplicationModel {
    let historyStore: MeasurementHistoryStore
    let session: MeasurementSession
    private(set) var workspaceMenuRequest: WorkspaceMenuRequest?
    private(set) var lastSavedWorkspaceState: IwashiScopeWorkspaceState?
    private(set) var isBrowsingRestoredWorkspace = false

    public init() {
        let historyStore = MeasurementHistoryStore()
        self.historyStore = historyStore
        self.session = MeasurementSession(historyStore: historyStore)
    }

    func requestWorkspaceSave() {
        workspaceMenuRequest = WorkspaceMenuRequest(operation: .save)
    }

    func requestWorkspaceRestore() {
        workspaceMenuRequest = WorkspaceMenuRequest(operation: .restore)
    }

    func workspaceState(
        selectedMode: MeasurementMode?,
        selectedSidebarTab: MeasurementSidebarTab
    ) -> IwashiScopeWorkspaceState {
        IwashiScopeWorkspaceState(
            selectedMode: selectedMode,
            selectedSidebarTab: selectedSidebarTab,
            history: historyStore.workspaceSnapshot()
        )
    }

    func hasUnsavedWorkspace(_ currentState: IwashiScopeWorkspaceState) -> Bool {
        guard let lastSavedWorkspaceState else {
            return currentState.hasContent
        }
        return currentState != lastSavedWorkspaceState
    }

    func markWorkspaceSaved(_ state: IwashiScopeWorkspaceState) {
        lastSavedWorkspaceState = state
    }

    func restoreWorkspace(_ state: IwashiScopeWorkspaceState) throws {
        try state.validate()
        try historyStore.restore(from: state.history)
        session.stop()
        lastSavedWorkspaceState = state
        isBrowsingRestoredWorkspace = true
    }

    func presentRestoredWorkspace(mode: MeasurementMode) {
        let selectedEntry = historyStore.selectedEntry(for: mode)
        session.presentWorkspace(
            mode: mode,
            measurement: selectedEntry?.measurement,
            instrumentIdentity: selectedEntry?.instrumentIdentity,
            measurementCount: historyStore.entries.lazy.filter {
                $0.measurement.mode == mode
            }.count
        )
    }

    func connectInstrument(mode: MeasurementMode) {
        isBrowsingRestoredWorkspace = false
        session.start(mode: mode)
    }
}

struct WorkspaceMenuRequest: Equatable {
    enum Operation: Equatable {
        case save
        case restore
    }

    let id = UUID()
    let operation: Operation
}
