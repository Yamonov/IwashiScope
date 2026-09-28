import Foundation
import Observation

@MainActor
@Observable
public final class IwashiScopeApplicationModel {
    let historyStore: MeasurementHistoryStore
    let session: MeasurementSession
    let userIlluminantStore: UserIlluminantStore
    private(set) var workspaceMenuRequest: WorkspaceMenuRequest?
    private(set) var lastSavedWorkspaceState: IwashiScopeWorkspaceState?
    private(set) var isBrowsingRestoredWorkspace = false
    private(set) var historyPersistenceErrorMessage: String?
    private(set) var initialWorkspaceState: IwashiScopeWorkspaceState?
    private var currentMode: MeasurementMode?
    private var currentSidebarTab: MeasurementSidebarTab = .measurementValues
    @ObservationIgnored private let historyPersistenceWriter: MeasurementHistoryPersistenceWriter
    @ObservationIgnored private let defaultWorkspaceLoadFailed: Bool
    @ObservationIgnored private var historyPersistenceTask: Task<Void, Never>?

    public convenience init() {
        self.init(historyPersistence: .applicationSupport)
    }

    init(historyPersistence: MeasurementHistoryPersistence) {
        let historyStore = MeasurementHistoryStore()
        let historyPersistenceWriter = MeasurementHistoryPersistenceWriter(
            persistence: historyPersistence
        )
        var persistenceErrorMessage: String?
        var defaultWorkspaceState: IwashiScopeWorkspaceState?
        var loadFailed = false

        do {
            if let state = try historyPersistence.loadDefaultWorkspace() {
                try historyStore.restore(from: state.history)
                defaultWorkspaceState = state
            } else if let legacySnapshot = try historyPersistence.load() {
                try historyStore.restore(from: legacySnapshot)
            }
        } catch {
            loadFailed = true
            persistenceErrorMessage = Self.persistenceErrorMessage(
                prefix: String(localized: "保存された測定履歴を読み込めませんでした。"),
                errorDescription: error.localizedDescription
            )
        }

        self.historyStore = historyStore
        self.session = MeasurementSession(historyStore: historyStore)
        self.userIlluminantStore = UserIlluminantStore(historyStore: historyStore)
        self.historyPersistenceWriter = historyPersistenceWriter
        self.defaultWorkspaceLoadFailed = loadFailed
        self.historyPersistenceErrorMessage = persistenceErrorMessage
        self.initialWorkspaceState = defaultWorkspaceState
        self.currentMode = defaultWorkspaceState?.selectedMode
        self.currentSidebarTab = defaultWorkspaceState?.selectedSidebarTab ?? .measurementValues

        if let mode = defaultWorkspaceState?.selectedMode {
            isBrowsingRestoredWorkspace = true
            presentRestoredWorkspace(mode: mode)
        }

        historyStore.setPersistentChangeHandler { [weak self] in
            self?.scheduleHistoryPersistence()
        }
    }

    @discardableResult
    public func prepareForApplicationTermination() async -> Bool {
        guard defaultWorkspaceLoadFailed == false else { return false }
        let state = workspaceState(
            selectedMode: currentMode,
            selectedSidebarTab: currentSidebarTab
        )
        session.stopForApplicationTermination()
        historyPersistenceTask?.cancel()

        if let errorDescription = await historyPersistenceWriter.saveDefaultWorkspace(state) {
            historyPersistenceErrorMessage = Self.persistenceErrorMessage(
                prefix: String(localized: "測定履歴を保存できませんでした。"),
                errorDescription: errorDescription
            )
            return false
        }
        return true
    }

    func dismissHistoryPersistenceError() {
        historyPersistenceErrorMessage = nil
    }

    func setCurrentWorkspaceSelection(
        mode: MeasurementMode?,
        sidebarTab: MeasurementSidebarTab
    ) {
        currentMode = mode
        currentSidebarTab = sidebarTab
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

    private func scheduleHistoryPersistence() {
        guard defaultWorkspaceLoadFailed == false else { return }
        historyPersistenceTask?.cancel()
        let state = workspaceState(
            selectedMode: currentMode,
            selectedSidebarTab: currentSidebarTab
        )
        let writer = historyPersistenceWriter

        historyPersistenceTask = Task { [weak self] in
            do {
                try await Task.sleep(for: .milliseconds(200))
            } catch {
                return
            }
            guard Task.isCancelled == false else { return }

            if let errorDescription = await writer.saveDefaultWorkspace(state) {
                self?.historyPersistenceErrorMessage = Self.persistenceErrorMessage(
                    prefix: String(localized: "測定履歴を保存できませんでした。"),
                    errorDescription: errorDescription
                )
            }
        }
    }

    private static func persistenceErrorMessage(
        prefix: String,
        errorDescription: String
    ) -> String {
        "\(prefix)\n\(errorDescription)"
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
