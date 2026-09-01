import SwiftUI

enum MeasurementHistoryPanelLayout {
    static let minimumWidth: CGFloat = 180
    static let initialWidth: CGFloat = 320
    static let maximumWidth: CGFloat = 420
    static let accessibilityStep: CGFloat = 20

    static func clampedWidth(_ width: CGFloat) -> CGFloat {
        min(maximumWidth, max(minimumWidth, width))
    }
}

struct MeasurementHistoryPanelDivider: View {
    @Binding var panelWidth: CGFloat
    @State private var dragStartWidth: CGFloat?

    var body: some View {
        ZStack {
            Color.clear

            Rectangle()
                .fill(.secondary.opacity(0.22))
                .frame(width: 1)
        }
        .frame(width: 9)
        .frame(maxHeight: .infinity)
        .contentShape(Rectangle())
        .gesture(resizeGesture)
        .help("左右にドラッグして測定履歴パネルの幅を変更します")
        .accessibilityElement()
        .accessibilityLabel("測定履歴パネルの幅")
        .accessibilityValue("\(Int(panelWidth.rounded()))ピクセル")
        .accessibilityHint("左右にドラッグするか、VoiceOverの調整操作で幅を変更します")
        .accessibilityAdjustableAction(adjustWidth)
        .accessibilityIdentifier("measurement-history-panel-divider")
    }

    private var resizeGesture: some Gesture {
        DragGesture(minimumDistance: 1)
            .onChanged { value in
                if dragStartWidth == nil {
                    dragStartWidth = panelWidth
                }
                guard let dragStartWidth else { return }
                panelWidth = MeasurementHistoryPanelLayout.clampedWidth(
                    dragStartWidth + value.translation.width
                )
            }
            .onEnded { value in
                if let dragStartWidth {
                    panelWidth = MeasurementHistoryPanelLayout.clampedWidth(
                        dragStartWidth + value.translation.width
                    )
                }
                dragStartWidth = nil
            }
    }

    private func adjustWidth(_ direction: AccessibilityAdjustmentDirection) {
        let change: CGFloat
        switch direction {
        case .increment:
            change = MeasurementHistoryPanelLayout.accessibilityStep
        case .decrement:
            change = -MeasurementHistoryPanelLayout.accessibilityStep
        @unknown default:
            return
        }

        panelWidth = MeasurementHistoryPanelLayout.clampedWidth(panelWidth + change)
    }
}
