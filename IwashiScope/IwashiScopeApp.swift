import AppKit
import Sparkle
import SwiftUI
import IwashiScopeFeature

@MainActor
private final class IwashiScopeAppDelegate: NSObject, NSApplicationDelegate {
    weak var model: IwashiScopeApplicationModel?
    private var isPreparingForTermination = false

    func applicationShouldTerminate(
        _ sender: NSApplication
    ) -> NSApplication.TerminateReply {
        guard isPreparingForTermination == false else {
            return .terminateLater
        }

        isPreparingForTermination = true
        Task { @MainActor [weak self] in
            await self?.model?.prepareForApplicationTermination()
            sender.reply(toApplicationShouldTerminate: true)
        }
        return .terminateLater
    }
}

@main
struct IwashiScopeApp: App {
    @NSApplicationDelegateAdaptor(IwashiScopeAppDelegate.self)
    private var appDelegate
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
                .onAppear {
                    appDelegate.model = model
                }
        }
        .defaultSize(width: 1400, height: 760)
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
        .defaultSize(width: 560, height: 560)
        .windowResizability(.contentSize)
    }
}
