import AppKit
import Charts
import SwiftUI

enum LightingMeasurementHistoryCardMetrics {
    static let size = MeasurementHistoryCardMetrics.size
    static let spectrumHeight = 44.0
    static let dividerHeight = 1.0
    static let criHeight = 44.0
    static let titleAreaHeight = 21.0
    static let titleFieldHeight = 18.0
    static let titleFieldBottomPadding = 2.0

    static var contentHeight: Double {
        spectrumHeight + dividerHeight + criHeight + titleAreaHeight
    }
}

struct LightingMeasurementHistoryView: View {
    private static let cardSpacing = 16.0
    private static let dropZoneWidth = 12.0
    private static let trailingDropExtension = 64.0

    @State private var dragState: MeasurementHistoryDragState?
    @State private var dropTarget: MeasurementHistoryDropTarget?
    @State private var keyboardFocusRequest: UUID?
    @State private var editingEntryID: MeasurementHistoryEntry.ID?
    @State private var editingName = ""
    @State private var nameEditorFocusRequest: UUID?

    let mode: MeasurementMode
    let historyStore: MeasurementHistoryStore
    let usesPracticalSpectrumRange: Bool
    let spectrumYAxisConfiguration: SpectrumYAxisConfiguration

    private var cardItemWidth: Double {
        LightingMeasurementHistoryCardMetrics.size.width
            + Self.dropZoneWidth * 2
    }

    private var columns: [GridItem] {
        [
            GridItem(
                .adaptive(minimum: cardItemWidth, maximum: cardItemWidth),
                spacing: 0,
                alignment: .top
            ),
        ]
    }

    private var entries: [MeasurementHistoryEntry] {
        historyStore.orderedEntries(for: mode)
    }

    var body: some View {
        GroupBox {
            if entries.isEmpty {
                emptyState
            } else {
                LazyVGrid(columns: columns, alignment: .leading, spacing: Self.cardSpacing) {
                    ForEach(Array(entries.enumerated()), id: \.element.id) { index, entry in
                        historyCard(entry)
                            .padding(.horizontal, Self.dropZoneWidth)
                            .contextMenu {
                                Button {
                                    beginRenaming(entryID: entry.id)
                                } label: {
                                    Label("名前を付ける", systemImage: "pencil")
                                }
                            }
                            .overlay {
                                insertionIndicator(
                                    itemIndex: index,
                                    itemWidth: cardItemWidth
                                )
                            }
                            .contentShape(Rectangle())
                            .background(alignment: .leading) {
                                dropArea(
                                    itemIndex: index,
                                    itemCount: entries.count,
                                    itemWidth: cardItemWidth
                                )
                            }
                    }
                }
                .padding(.horizontal, Self.dropZoneWidth)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.vertical, 4)
                .animation(.snappy(duration: 0.28), value: entries.map(\.id))
            }
        } label: {
            Label("測定履歴", systemImage: "clock.arrow.circlepath")
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .background {
            if editingEntryID == nil {
                MeasurementHistoryKeyboardResponder(
                    focusRequest: keyboardFocusRequest,
                    onDelete: deleteSelectedEntries,
                    onSelectAll: selectAllEntries,
                    onDeselectAll: deselectAllEntries
                )
                .frame(width: 1, height: 1)
                .accessibilityHidden(true)
            }
        }
        .accessibilityIdentifier("lighting-history-group-\(mode.rawValue)")
    }

    private var emptyState: some View {
        ContentUnavailableView(
            "測定履歴はありません",
            systemImage: mode.systemImage,
            description: Text(
                "\(mode.title)を測定すると、スペクトルとCRIをここへ追加します。"
            )
        )
        .frame(maxWidth: .infinity, minHeight: 150)
    }

    private func historyCard(_ entry: MeasurementHistoryEntry) -> some View {
        LightingMeasurementHistoryCard(
            editingName: $editingName,
            entry: entry,
            isSelected: isSelected(entryID: entry.id),
            isActive: historyStore.selectedEntryID(for: mode) == entry.id,
            isEditingName: editingEntryID == entry.id,
            nameEditorFocusRequest: nameEditorFocusRequest,
            onSelect: {
                select(entryID: entry.id)
            },
            onBeginRenaming: {
                beginRenaming(entryID: entry.id)
            },
            onCommitRenaming: { name in
                commitRenaming(entryID: entry.id, name: name)
            },
            onNavigateRenaming: { name, direction in
                navigateRenaming(
                    from: entry.id,
                    name: name,
                    direction: direction
                )
            },
            onCancelRenaming: {
                cancelRenaming(entryID: entry.id)
            },
            dragPreviewEntries: dragPreviewEntries(for: entry),
            onBeginDragging: {
                beginDragging(entryID: entry.id)
            }
        )
    }

    private func dragPreviewEntries(
        for entry: MeasurementHistoryEntry
    ) -> [MeasurementHistoryEntry] {
        guard isSelected(entryID: entry.id) else { return [entry] }
        let selectedEntries = historyStore.selectedEntries(for: mode)
        return selectedEntries.isEmpty ? [entry] : selectedEntries
    }

    private func select(entryID: MeasurementHistoryEntry.ID) {
        guard historyStore.select(
            entryID: entryID,
            action: selectionActionForCurrentEvent
        ) else {
            return
        }
        if (NSApp.currentEvent?.clickCount ?? 1) < 2 {
            focusHistory()
        }
    }

    private func isSelected(entryID: MeasurementHistoryEntry.ID) -> Bool {
        historyStore.selectedEntryIDs(for: mode).contains(entryID)
    }

    private func beginRenaming(entryID: MeasurementHistoryEntry.ID) {
        guard let entry = entries.first(where: { $0.id == entryID }) else {
            return
        }
        keyboardFocusRequest = nil
        if isSelected(entryID: entryID) == false {
            historyStore.select(entryID: entryID)
        }

        editingName = entry.name ?? ""
        editingEntryID = entryID
        nameEditorFocusRequest = UUID()
        clearDragState()
    }

    private func commitRenaming(
        entryID: MeasurementHistoryEntry.ID,
        name: String
    ) {
        historyStore.setName(name, for: entryID)
        guard editingEntryID == entryID else { return }
        clearNameEditingState()
    }

    private func cancelRenaming(entryID: MeasurementHistoryEntry.ID) {
        guard editingEntryID == entryID else { return }
        clearNameEditingState()
    }

    private func navigateRenaming(
        from entryID: MeasurementHistoryEntry.ID,
        name: String,
        direction: MeasurementHistoryNameNavigationDirection
    ) {
        guard editingEntryID == entryID,
              historyStore.setName(name, for: entryID),
              let destinationID = MeasurementHistoryNameNavigation.destinationID(
                  from: entryID,
                  orderedIDs: entries.map(\.id),
                  direction: direction
              ) else {
            clearNameEditingState()
            return
        }

        beginRenaming(entryID: destinationID)
    }

    private func clearNameEditingState() {
        editingEntryID = nil
        editingName = ""
        nameEditorFocusRequest = nil
    }

    private func beginDragging(entryID: MeasurementHistoryEntry.ID) -> NSItemProvider {
        if isSelected(entryID: entryID) == false {
            historyStore.select(entryID: entryID)
        }
        focusHistory()

        let selectedEntryIDs = historyStore.selectedEntryIDs(for: mode)
        dragState = MeasurementHistoryDragState(entryIDs: selectedEntryIDs)
        let selectedEntries = historyStore.selectedEntries(for: mode)
        return MeasurementHistoryDragItemProvider.make(
            entryIDs: selectedEntryIDs,
            swatches: [],
            exportRequest: MeasurementHistoryDragExportRequest(
                mode: mode,
                entries: selectedEntries,
                orderedEntries: entries,
                usesPracticalSpectrumRange: usesPracticalSpectrumRange,
                spectrumYAxisConfiguration: spectrumYAxisConfiguration
            )
        )
    }

    private func insertionIndicator(
        itemIndex: Int,
        itemWidth: Double
    ) -> some View {
        ZStack(alignment: .leading) {
            if dropTarget?.itemIndex == itemIndex,
               dropTarget?.insertionIndex == itemIndex {
                MeasurementHistoryInsertionIndicator()
            }
            if dropTarget?.itemIndex == itemIndex,
               dropTarget?.insertionIndex == itemIndex + 1 {
                MeasurementHistoryInsertionIndicator()
                    .offset(
                        x: itemWidth
                            - MeasurementHistoryInsertionIndicatorMetrics.width
                    )
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }

    private func dropArea(
        itemIndex: Int,
        itemCount: Int,
        itemWidth: Double
    ) -> some View {
        Color.clear
            .frame(
                width: itemWidth + (
                    itemIndex == itemCount - 1
                        ? Self.trailingDropExtension
                        : 0
                )
            )
            .contentShape(Rectangle())
            .onDrop(
                of: [MeasurementHistoryDragPayload.contentType],
                delegate: MeasurementHistoryDropDelegate(
                    mode: mode,
                    itemIndex: itemIndex,
                    itemCount: itemCount,
                    itemWidth: itemWidth,
                    historyStore: historyStore,
                    dragState: $dragState,
                    dropTarget: $dropTarget
                )
            )
    }

    private func deleteSelectedEntries() {
        guard historyStore.removeSelectedEntries(for: mode) > 0 else { return }
        clearNameEditingState()
        clearDragState()
    }

    private var selectionActionForCurrentEvent: MeasurementHistorySelectionAction {
        let modifiers = NSApp.currentEvent?.modifierFlags
            .intersection(.deviceIndependentFlagsMask) ?? []

        if modifiers.contains(.shift) {
            return .range(additive: modifiers.contains(.command))
        }
        if modifiers.contains(.command) {
            return .toggle
        }
        return .exclusive
    }

    private func selectAllEntries() {
        historyStore.selectAll(for: mode)
    }

    private func deselectAllEntries() {
        guard historyStore.deselectAll(for: mode) else { return }
        clearDragState()
    }

    private func clearDragState() {
        dragState = nil
        dropTarget = nil
    }

    private func focusHistory() {
        keyboardFocusRequest = UUID()
    }
}

private struct LightingMeasurementHistoryCard: View {
    @Binding var editingName: String

    let entry: MeasurementHistoryEntry
    let isSelected: Bool
    let isActive: Bool
    let isEditingName: Bool
    let nameEditorFocusRequest: UUID?
    let onSelect: () -> Void
    let onBeginRenaming: () -> Void
    let onCommitRenaming: (String) -> Void
    let onNavigateRenaming: (String, MeasurementHistoryNameNavigationDirection) -> Void
    let onCancelRenaming: () -> Void
    let dragPreviewEntries: [MeasurementHistoryEntry]
    let onBeginDragging: () -> NSItemProvider

    var body: some View {
        ZStack(alignment: .bottom) {
            selectionSurface
            nameField
                .padding(.bottom, LightingMeasurementHistoryCardMetrics.titleFieldBottomPadding)
        }
        .frame(
            width: LightingMeasurementHistoryCardMetrics.size.width,
            height: LightingMeasurementHistoryCardMetrics.size.height
        )
        .help("クリックで選択し、下部のタイトル欄をダブルクリックすると編集できます。編集中はTabで次、Shift+Tabで前のカードへ移動します。Commandクリックで追加選択、Shiftクリックで範囲選択します。Cmd+Aで全選択、Cmd+Dで選択解除、ドラッグで並べ替え、Deleteキーで削除できます。")
        .accessibilityElement(children: isEditingName ? .contain : .ignore)
        .accessibilityLabel(accessibilityLabel)
        .accessibilityValue(accessibilitySelectionValue)
        .accessibilityHint("クリックして測定値、スペクトル、CRIを表示します。")
        .accessibilityAddTraits(isSelected ? .isSelected : [])
        .accessibilityAction(named: "名前を付ける", onBeginRenaming)
    }

    @ViewBuilder
    private var selectionSurface: some View {
        if isEditingName {
            cardSurface
        } else {
            Button(action: onSelect) {
                cardSurface
            }
            .buttonStyle(.plain)
            .onDrag(onBeginDragging) {
                LightingMeasurementHistoryDragPreview(entries: dragPreviewEntries)
            }
        }
    }

    private var cardSurface: some View {
        VStack(spacing: 0) {
            LightingHistorySpectrumThumbnail(measurement: entry.measurement)
                .frame(height: LightingMeasurementHistoryCardMetrics.spectrumHeight)

            Rectangle()
                .fill(Color.secondary.opacity(0.20))
                .frame(height: LightingMeasurementHistoryCardMetrics.dividerHeight)

            LightingHistoryCRIThumbnail(measurement: entry.measurement)
                .frame(height: LightingMeasurementHistoryCardMetrics.criHeight)

            Color.secondary.opacity(0.055)
                .frame(height: LightingMeasurementHistoryCardMetrics.titleAreaHeight)
        }
        .frame(
            width: LightingMeasurementHistoryCardMetrics.size.width,
            height: LightingMeasurementHistoryCardMetrics.size.height
        )
        .background(Color.secondary.opacity(0.055))
        .overlay {
            RoundedRectangle(cornerRadius: 10)
                .strokeBorder(
                    isSelected ? Color.accentColor : Color.secondary.opacity(0.22),
                    lineWidth: isActive ? 3 : (isSelected ? 2 : 1)
                )
        }
        .compositingGroup()
        .clipShape(.rect(cornerRadius: 10))
        .contentShape(.rect(cornerRadius: 10))
    }

    private var nameField: some View {
        MeasurementHistoryNameEditor(
            text: $editingName,
            displayText: entry.name ?? "",
            isEditing: isEditingName,
            focusRequest: nameEditorFocusRequest,
            onSelect: onSelect,
            onBeginEditing: onBeginRenaming,
            onCommit: onCommitRenaming,
            onNavigate: onNavigateRenaming,
            onCancel: onCancelRenaming
        )
        .padding(.horizontal, 4)
        .frame(
            width: LightingMeasurementHistoryCardMetrics.size.width - 12,
            height: LightingMeasurementHistoryCardMetrics.titleFieldHeight
        )
        .background(Color.white.opacity(0.5), in: .rect(cornerRadius: 6))
        .overlay {
            RoundedRectangle(cornerRadius: 6)
                .strokeBorder(
                    isEditingName ? Color.accentColor : Color.primary.opacity(0.22),
                    lineWidth: isEditingName ? 2 : 1
                )
        }
        .accessibilityHidden(isEditingName == false)
    }

    private var accessibilityLabel: String {
        let name = entry.name.map { String(localized: "タイトル \($0)") }
            ?? String(localized: "タイトルなし")
        let spectrum = String(localized: "スペクトル \(entry.measurement.spectrum.count)点")
        let cri = entry.measurement.cri.map {
            "CRI、Ra \($0.ra.formatted(.number.precision(.fractionLength(1))))"
        } ?? String(localized: "CRIなし")
        return [name, spectrum, cri].joined(separator: "、")
    }

    private var accessibilitySelectionValue: String {
        if isActive {
            return String(localized: "選択中、測定値を表示中")
        }
        return isSelected ? String(localized: "選択中") : String(localized: "未選択")
    }
}

private struct LightingHistorySpectrumThumbnail: View {
    let measurement: SpotMeasurement

    private var samples: [SpectralSample] {
        measurement.spectrum
    }

    private var xDomain: ClosedRange<Double> {
        let start = measurement.spectrumStart
        let end = measurement.spectrumEnd
        return end > start ? start...end : start...(start + 1)
    }

    private var yUpperBound: Double {
        max(1, (samples.lazy.map(\.value).max() ?? 1) * 1.08)
    }

    var body: some View {
        ZStack {
            SpectrumChartStyle.gradient.opacity(0.10)

            if samples.isEmpty {
                LightingHistoryChartPlaceholder(
                    title: String(localized: "スペクトルなし"),
                    systemImage: "waveform.path.ecg"
                )
            } else {
                Chart {
                    ForEach(samples) { sample in
                        AreaMark(
                            x: .value("波長", sample.wavelength),
                            yStart: .value("基準", 0.0),
                            yEnd: .value("スペクトル値", sample.value)
                        )
                        .interpolationMethod(.linear)
                        .foregroundStyle(SpectrumChartStyle.gradient)
                        .alignsMarkStylesWithPlotArea()

                        LineMark(
                            x: .value("波長", sample.wavelength),
                            y: .value("スペクトル値", sample.value)
                        )
                        .interpolationMethod(.linear)
                        .lineStyle(.init(lineWidth: 1.25, lineJoin: .round))
                        .foregroundStyle(Color.gray)
                    }
                }
                .chartXScale(domain: xDomain)
                .chartYScale(domain: 0...yUpperBound)
                .chartXAxis(.hidden)
                .chartYAxis(.hidden)
                .chartPlotStyle { plotArea in
                    plotArea
                        .background(SpectrumChartStyle.gradient.opacity(0.13))
                        .clipShape(.rect(cornerRadius: 5))
                }
                .padding(4)
            }
        }
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }
}

private struct LightingHistoryCRIThumbnail: View {
    private static let scoreLabels = (1...15).map { "R\($0)" }

    private struct Score: Identifiable {
        let index: Int
        let value: Double

        var id: Int { index }
        var label: String { "R\(index)" }
    }

    let measurement: SpotMeasurement

    private var scores: [Score] {
        guard let individual = measurement.cri?.individual else { return [] }
        return (1...15).compactMap { index in
            individual[index].map { Score(index: index, value: $0) }
        }
    }

    private var yLowerBound: Double {
        min(0, scores.lazy.map(\.value).min() ?? 0)
    }

    private var yUpperBound: Double {
        max(100, scores.lazy.map(\.value).max() ?? 100)
    }

    var body: some View {
        ZStack {
            Color.secondary.opacity(0.035)

            if scores.isEmpty {
                LightingHistoryChartPlaceholder(
                    title: String(localized: "CRIなし"),
                    systemImage: "chart.bar"
                )
            } else {
                Chart {
                    ForEach(scores) { score in
                        BarMark(
                            x: .value("試験色", score.label),
                            y: .value("演色評価数", score.value)
                        )
                        .foregroundStyle(
                            ColorRenderingChartPalette.color(for: score.index).gradient
                        )
                        .cornerRadius(1.5)
                    }

                    RuleMark(y: .value("基準", 80))
                        .lineStyle(.init(lineWidth: 0.75, dash: [3, 3]))
                        .foregroundStyle(.secondary.opacity(0.35))
                }
                .chartXScale(domain: Self.scoreLabels)
                .chartYScale(domain: yLowerBound...yUpperBound)
                .chartXAxis(.hidden)
                .chartYAxis(.hidden)
                .chartPlotStyle { plotArea in
                    plotArea
                        .background(Color.secondary.opacity(0.035))
                        .clipShape(.rect(cornerRadius: 5))
                }
                .padding(4)
            }
        }
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }
}

private struct LightingHistoryChartPlaceholder: View {
    let title: String
    let systemImage: String

    var body: some View {
        Label(title, systemImage: systemImage)
            .font(.caption)
            .foregroundStyle(.secondary)
    }
}

private struct LightingMeasurementHistoryDragPreview: View {
    private static let previewSize = 82.0
    private static let xOffset = 12.0
    private static let yOffset = 7.0
    private static let maximumVisibleCardCount = 4

    let entries: [MeasurementHistoryEntry]

    private var visibleEntries: [MeasurementHistoryEntry] {
        Array(entries.prefix(Self.maximumVisibleCardCount))
    }

    var body: some View {
        ZStack(alignment: .topLeading) {
            ForEach(Array(visibleEntries.enumerated()), id: \.element.id) { index, _ in
                RoundedRectangle(cornerRadius: 8)
                    .fill(.regularMaterial)
                    .frame(width: Self.previewSize, height: Self.previewSize)
                    .overlay {
                        VStack(spacing: 7) {
                            Image(systemName: "waveform.path.ecg")
                            Image(systemName: "chart.bar.fill")
                        }
                        .foregroundStyle(.tint)
                    }
                    .overlay {
                        RoundedRectangle(cornerRadius: 8)
                            .strokeBorder(Color.white.opacity(0.8), lineWidth: 1)
                    }
                    .offset(
                        x: Double(index) * Self.xOffset,
                        y: Double(index) * Self.yOffset
                    )
            }
        }
        .frame(
            width: Self.previewSize
                + Double(max(visibleEntries.count - 1, 0)) * Self.xOffset,
            height: Self.previewSize
                + Double(max(visibleEntries.count - 1, 0)) * Self.yOffset
        )
        .overlay(alignment: .bottomTrailing) {
            if entries.count > 1 {
                Text("\(entries.count)枚")
                    .font(.caption2.weight(.bold))
                    .foregroundStyle(.white)
                    .padding(.horizontal, 7)
                    .padding(.vertical, 3)
                    .background(.black.opacity(0.72), in: .capsule)
                    .offset(x: 6, y: 5)
            }
        }
        .padding(6)
        .shadow(color: .black.opacity(0.24), radius: 8, y: 4)
    }
}
