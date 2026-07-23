import SwiftUI

public struct SpectraMateWorkspaceCommands: Commands {
    private let model: SpectraMateApplicationModel

    public init(model: SpectraMateApplicationModel) {
        self.model = model
    }

    public var body: some Commands {
        CommandGroup(replacing: .saveItem) {
            Button("ワークスペースを保存...") {
                model.requestWorkspaceSave()
            }
            .keyboardShortcut("s", modifiers: .command)

            Button("ワークスペースを復帰...") {
                model.requestWorkspaceRestore()
            }
        }
    }
}
