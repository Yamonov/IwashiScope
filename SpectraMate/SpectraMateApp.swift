import Sparkle
import SwiftUI
import SpectraMateFeature

@main
struct SpectraMateApp: App {
    @State private var model = SpectraMateApplicationModel()
    private let updaterController: SPUStandardUpdaterController

    init() {
        updaterController = SPUStandardUpdaterController(
            startingUpdater: true,
            updaterDelegate: nil,
            userDriverDelegate: nil
        )
    }

    var body: some Scene {
        WindowGroup {
            ContentView(model: model)
        }
        .defaultSize(width: 1080, height: 760)
        .windowResizability(.contentMinSize)
        .windowToolbarStyle(.unifiedCompact)
        .commands {
            SpectraMateWorkspaceCommands(model: model)

            CommandGroup(after: .appInfo) {
                LicenseInformationCommand()
                Divider()
                CheckForUpdatesCommand(updater: updaterController.updater)
            }
        }

        Window(
            "ライセンスとソースコード",
            id: LicenseInformationCommand.windowID
        ) {
            LicenseInformationView()
        }
        .defaultSize(width: 520, height: 430)
        .windowResizability(.contentSize)
    }
}
