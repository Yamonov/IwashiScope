import AppKit
import SwiftUI

enum MeasurementHistoryPanelLayout {
    static let minimumWidth: CGFloat = 180
    // Two 110-point cards plus drop zones, panel padding, and a visible scrollbar.
    static let initialWidth: CGFloat = 360
    static let maximumWidth: CGFloat = 800
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
        .modifier(MeasurementHistoryPanelResizeCursor())
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
        // The divider moves as panelWidth changes. Measure against the fixed
        // window coordinate space so that movement does not shift its origin.
        DragGesture(minimumDistance: 1, coordinateSpace: .global)
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

private struct MeasurementHistoryPanelResizeCursor: ViewModifier {
    @ViewBuilder
    func body(content: Content) -> some View {
        if #available(macOS 15.0, *) {
            content.pointerStyle(.columnResize)
        } else {
            content
                .onHover { isHovering in
                    if isHovering {
                        NSCursor.resizeLeftRight.set()
                    } else {
                        NSCursor.arrow.set()
                    }
                }
                .onDisappear {
                    NSCursor.arrow.set()
                }
        }
    }
}
