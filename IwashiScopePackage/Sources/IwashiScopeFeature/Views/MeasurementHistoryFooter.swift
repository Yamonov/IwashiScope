import SwiftUI

enum MeasurementHistoryFooterLayout {
    static let minimumCollapsedHeight: CGFloat = 100
    static let maximumCollapsedFraction: CGFloat = 0.75
    static let expandedFraction: CGFloat = 0.9
    static let handleHeight: CGFloat = 30
    private static let estimatedSpectrumAnalysisHeight: CGFloat = 466
    private static let renderingGroupSpacing: CGFloat = 16
    private static let estimatedLightingRenderingHeight: CGFloat = 480

    static func estimatedAnalysisHeight(for mode: MeasurementMode) -> CGFloat {
        switch mode {
        case .reflectance:
            estimatedSpectrumAnalysisHeight
        case .ambient, .emissive:
            estimatedSpectrumAnalysisHeight
                + renderingGroupSpacing
                + estimatedLightingRenderingHeight
        }
    }

    static func collapsedHeight(
        availableHeight: CGFloat,
        analysisContentHeight: CGFloat,
        mode: MeasurementMode
    ) -> CGFloat {
        guard availableHeight.isFinite, availableHeight > 0 else {
            return 0
        }

        let resolvedAnalysisHeight = analysisContentHeight.isFinite
            && analysisContentHeight > 0
            ? analysisContentHeight
            : estimatedAnalysisHeight(for: mode)
        let unusedHeight = max(0, availableHeight - resolvedAnalysisHeight)
        let preferredHeight = max(minimumCollapsedHeight, unusedHeight)
        let maximumHeight = availableHeight * maximumCollapsedFraction

        return min(
            availableHeight,
            min(preferredHeight, maximumHeight)
        )
    }

    static func height(
        availableHeight: CGFloat,
        collapsedHeight: CGFloat,
        manuallyResizedHeight: CGFloat?,
        isExpanded: Bool
    ) -> CGFloat {
        guard availableHeight.isFinite, availableHeight > 0 else {
            return 0
        }

        if isExpanded {
            return availableHeight * expandedFraction
        }

        if let manuallyResizedHeight,
           manuallyResizedHeight.isFinite {
            return resizedHeight(
                availableHeight: availableHeight,
                proposedHeight: manuallyResizedHeight
            )
        }

        let collapsedHeight = min(
            max(0, collapsedHeight),
            availableHeight
        )
        return collapsedHeight
    }

    static func resizedHeight(
        availableHeight: CGFloat,
        proposedHeight: CGFloat
    ) -> CGFloat {
        guard availableHeight.isFinite,
              availableHeight > 0,
              proposedHeight.isFinite else {
            return 0
        }

        let maximumHeight = availableHeight * expandedFraction
        let minimumHeight = min(minimumCollapsedHeight, maximumHeight)
        return min(
            maximumHeight,
            max(minimumHeight, proposedHeight)
        )
    }

    static func resizedHeight(
        availableHeight: CGFloat,
        startHeight: CGFloat,
        verticalTranslation: CGFloat
    ) -> CGFloat {
        guard startHeight.isFinite,
              verticalTranslation.isFinite else {
            return 0
        }

        return resizedHeight(
            availableHeight: availableHeight,
            proposedHeight: startHeight - verticalTranslation
        )
    }
}

struct MeasurementHistoryFooter<Content: View>: View {
    @State private var isExpanded = false
    @State private var manuallyResizedHeight: CGFloat?
    @State private var dragStartHeight: CGFloat?

    let availableHeight: CGFloat
    let collapsedHeight: CGFloat
    private let content: Content

    init(
        availableHeight: CGFloat,
        collapsedHeight: CGFloat,
        @ViewBuilder content: () -> Content
    ) {
        self.availableHeight = availableHeight
        self.collapsedHeight = collapsedHeight
        self.content = content()
    }

    var body: some View {
        VStack(spacing: 0) {
            expansionButton

            ScrollView(.vertical) {
                content
                    .frame(maxWidth: .infinity, alignment: .topLeading)
                    .padding(.horizontal, 18)
                    .padding(.top, 4)
                    .padding(.bottom, 18)
            }
        }
        .frame(maxWidth: .infinity)
        .frame(
            height: displayedHeight,
            alignment: .top
        )
        .background(.regularMaterial)
        .shadow(color: .black.opacity(0.12), radius: 8, y: -2)
        .accessibilityIdentifier("measurement-history-footer")
    }

    private var expansionButton: some View {
        Button(action: toggleExpansion) {
            VStack(spacing: 4) {
                Capsule()
                    .frame(width: 38, height: 1.5)

                Capsule()
                    .frame(width: 38, height: 1.5)
            }
            .foregroundStyle(.secondary)
            .frame(maxWidth: .infinity)
            .frame(height: MeasurementHistoryFooterLayout.handleHeight)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .highPriorityGesture(resizeGesture)
        .help("クリックで展開・縮小、上下ドラッグで高さを調整")
        .accessibilityLabel("測定履歴の高さ")
        .accessibilityValue("\(displayedHeightPercentage)%")
        .accessibilityHint("クリックすると90%まで展開または縮小します。上下ドラッグでも高さを調整できます")
        .accessibilityAdjustableAction(adjustHeight)
        .accessibilityIdentifier("measurement-history-footer-toggle")
    }

    private var displayedHeight: CGFloat {
        MeasurementHistoryFooterLayout.height(
            availableHeight: availableHeight,
            collapsedHeight: collapsedHeight,
            manuallyResizedHeight: manuallyResizedHeight,
            isExpanded: isExpanded
        )
    }

    private var displayedHeightPercentage: Int {
        guard availableHeight > 0 else { return 0 }
        return Int((displayedHeight / availableHeight * 100).rounded())
    }

    private var resizeGesture: some Gesture {
        DragGesture(minimumDistance: 3)
            .onChanged { value in
                if dragStartHeight == nil {
                    dragStartHeight = displayedHeight
                    manuallyResizedHeight = displayedHeight
                    isExpanded = false
                }
                updateHeight(for: value.translation.height)
            }
            .onEnded { value in
                updateHeight(for: value.translation.height)
                dragStartHeight = nil
            }
    }

    private func toggleExpansion() {
        let expandedHeight = availableHeight
            * MeasurementHistoryFooterLayout.expandedFraction
        let shouldCollapse = isExpanded
            || displayedHeight >= expandedHeight - 0.5

        withAnimation(.snappy(duration: 0.28)) {
            manuallyResizedHeight = nil
            isExpanded = shouldCollapse == false
        }
    }

    private func updateHeight(for verticalTranslation: CGFloat) {
        guard let dragStartHeight else { return }
        manuallyResizedHeight = MeasurementHistoryFooterLayout.resizedHeight(
            availableHeight: availableHeight,
            startHeight: dragStartHeight,
            verticalTranslation: verticalTranslation
        )
    }

    private func adjustHeight(_ direction: AccessibilityAdjustmentDirection) {
        let adjustment: CGFloat
        switch direction {
        case .increment:
            adjustment = 40
        case .decrement:
            adjustment = -40
        @unknown default:
            return
        }

        withAnimation(.snappy(duration: 0.2)) {
            isExpanded = false
            manuallyResizedHeight = MeasurementHistoryFooterLayout.resizedHeight(
                availableHeight: availableHeight,
                proposedHeight: displayedHeight + adjustment
            )
        }
    }
}
