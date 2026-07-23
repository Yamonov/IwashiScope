import SwiftUI

public struct IwashiScopeWorkspaceCommands: Commands {
    private let model: IwashiScopeApplicationModel

    public init(model: IwashiScopeApplicationModel) {
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
