import SwiftUI

struct MeasurementHistorySidebar<Content: View>: View {
    @State private var showsDeleteConfirmation = false

    let mode: MeasurementMode
    let deletableEntryCount: Int
    let onDeleteAll: () -> Void
    @ViewBuilder let content: Content

    init(
        mode: MeasurementMode,
        deletableEntryCount: Int,
        onDeleteAll: @escaping () -> Void,
        @ViewBuilder content: () -> Content
    ) {
        self.mode = mode
        self.deletableEntryCount = deletableEntryCount
        self.onDeleteAll = onDeleteAll
        self.content = content()
    }

    var body: some View {
        VStack(spacing: 0) {
            ScrollView(.vertical) {
                content
                    .frame(maxWidth: .infinity, alignment: .topLeading)
                    .padding(.horizontal, 8)
                    .padding(.vertical, 10)
            }

            Divider()

            Button(role: .destructive) {
                showsDeleteConfirmation = true
            } label: {
                Label("履歴を削除", systemImage: "trash")
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.bordered)
            .disabled(deletableEntryCount == 0)
            .help("現在の\(mode.title)モードにある、ユーザー定義光源に未登録の測定履歴を削除します")
            .accessibilityHint("確認後、ユーザー定義光源に登録されていない現在のモードの測定履歴を削除します")
            .accessibilityIdentifier("delete-measurement-history-\(mode.rawValue)")
            .padding(12)
        }
        .background(.regularMaterial)
        .alert(
            "\(mode.title)の履歴を削除しますか？",
            isPresented: $showsDeleteConfirmation
        ) {
            Button("削除", role: .destructive, action: onDeleteAll)
            Button("キャンセル", role: .cancel) {}
        } message: {
            Text("このモードで削除可能な\(deletableEntryCount)件の測定履歴を削除します。ユーザー定義光源に登録中の履歴は残ります。この操作は取り消せません。")
        }
        .accessibilityIdentifier("measurement-history-sidebar-\(mode.rawValue)")
    }
}
