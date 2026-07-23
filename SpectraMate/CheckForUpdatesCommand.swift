import Combine
import Sparkle
import SwiftUI

@MainActor
private final class CheckForUpdatesViewModel: ObservableObject {
    @Published private(set) var canCheckForUpdates = false

    init(updater: SPUUpdater) {
        updater.publisher(for: \.canCheckForUpdates)
            .receive(on: RunLoop.main)
            .assign(to: &$canCheckForUpdates)
    }
}

struct CheckForUpdatesCommand: View {
    @StateObject private var viewModel: CheckForUpdatesViewModel
    private let updater: SPUUpdater

    @MainActor
    init(updater: SPUUpdater) {
        self.updater = updater
        _viewModel = StateObject(
            wrappedValue: CheckForUpdatesViewModel(updater: updater)
        )
    }

    var body: some View {
        Button("アップデートを確認…") {
            updater.checkForUpdates()
        }
        .disabled(viewModel.canCheckForUpdates == false)
    }
}
