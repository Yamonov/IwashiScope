import SwiftUI

public struct IwashiScopeWorkspaceCommands: Commands {
    private let model: IwashiScopeApplicationModel

    public init(model: IwashiScopeApplicationModel) {
        self.model = model
    }

    public var body: some Commands {
        CommandGroup(replacing: .saveItem) {
            Button("ワークスペースを書き出し...") {
                model.requestWorkspaceSave()
            }
            .keyboardShortcut("s", modifiers: .command)

            Button("ワークスペースを読み込み...") {
                model.requestWorkspaceRestore()
            }
        }
    }
}
