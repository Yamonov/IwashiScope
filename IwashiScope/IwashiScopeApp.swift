import Sparkle
import SwiftUI
import IwashiScopeFeature

@main
struct IwashiScopeApp: App {
    @State private var model = IwashiScopeApplicationModel()
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
            IwashiScopeWorkspaceCommands(model: model)

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
        .defaultSize(width: 520, height: 470)
        .windowResizability(.contentSize)
    }
}
