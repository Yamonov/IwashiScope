import AppKit
import SwiftUI
import UniformTypeIdentifiers

enum MeasurementHistoryCardMetrics {
    static let size = CGSize(width: 164, height: 164)
    static let colorAreaHeight = 116.0
}

struct MeasurementHistoryView: View {
    private static let cardWidth = MeasurementHistoryCardMetrics.size.width
    private static let cardSpacing = 16.0
    private static let dropZoneWidth = 12.0
    private static let cardItemWidth = cardWidth + dropZoneWidth * 2

    @State private var dragState: MeasurementHistoryDragState?
    @State private var dropTarget: MeasurementHistoryDropTarget?
    @State private var keyboardFocusRequest: UUID?
    @State private var editingEntryID: MeasurementHistoryEntry.ID?
    @State private var editingName = ""
    @State private var nameEditorFocusRequest: UUID?

    let historyStore: MeasurementHistoryStore
    let canExportSelectedSwatches: Bool
    let onExportSelectedSwatches: () -> Void

    private let columns = [
        GridItem(
            .adaptive(minimum: cardItemWidth, maximum: cardItemWidth),
            spacing: 0,
            alignment: .top
        )
    ]

    private var entries: [MeasurementHistoryEntry] {
        historyStore.orderedEntries(for: .reflectance)
    }

    var body: some View {
        GroupBox {
            if entries.isEmpty {
                ContentUnavailableView(
                    "測定履歴はありません",
                    systemImage: "swatchpalette",
                    description: Text("反射原稿を測定すると、Labカラーと測色値をここへ追加します。")
                )
                .frame(maxWidth: .infinity, minHeight: 150)
            } else {
                LazyVGrid(columns: columns, alignment: .leading, spacing: Self.cardSpacing) {
                    ForEach(entries) { entry in
                        historyCard(entry)
                            .padding(.horizontal, Self.dropZoneWidth)
                            .contextMenu {
                                Button {
                                    beginRenaming(entryID: entry.id)
                                } label: {
                                    Label("名前を付ける", systemImage: "pencil")
                                }

                                Divider()

                                Button {
                                    exportSwatches(from: entry)
                                } label: {
                                    Label(
                                        "選択カードをスウォッチに書き出し",
                                        systemImage: "square.and.arrow.up"
                                    )
                                }
                                .disabled(canExportSwatch(from: entry) == false)
                            }
                            .overlay(alignment: .leading) {
                                insertionDropZone(
                                    entryID: entry.id,
                                    placement: .before,
                                    width: Self.dropZoneWidth
                                )
                            }
                            .overlay(alignment: .trailing) {
                                insertionDropZone(
                                    entryID: entry.id,
                                    placement: .after,
                                    width: Self.dropZoneWidth
                                )
                            }
                    }
                }
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
    }

    private func historyCard(_ entry: MeasurementHistoryEntry) -> some View {
        MeasurementHistoryCard(
            entry: entry,
            width: Self.cardWidth,
            isSelected: historyStore.selectedEntryIDs(for: .reflectance).contains(entry.id),
            isActive: historyStore.selectedEntryID(for: .reflectance) == entry.id,
            isEditingName: editingEntryID == entry.id,
            editingName: $editingName,
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
        let selectedEntries = historyStore.selectedEntries(for: .reflectance)
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
        historyStore.selectedEntryIDs(for: .reflectance).contains(entryID)
    }

    private func canExportSwatch(from entry: MeasurementHistoryEntry) -> Bool {
        entry.measurement.lab != nil
    }

    private func exportSwatches(from entry: MeasurementHistoryEntry) {
        if isSelected(entryID: entry.id) == false {
            historyStore.select(entryID: entry.id)
        }
        onExportSelectedSwatches()
    }

    private func beginRenaming(entryID: MeasurementHistoryEntry.ID) {
        guard let entry = historyStore.entries.first(where: { $0.id == entryID }) else {
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
        if historyStore.selectedEntryIDs(for: .reflectance).contains(entryID) == false {
            historyStore.select(entryID: entryID)
        }
        focusHistory()

        dragState = MeasurementHistoryDragState(
            entryIDs: historyStore.selectedEntryIDs(for: .reflectance)
        )
        let selectedEntries = historyStore.selectedEntries(for: .reflectance)
        return MeasurementHistoryDragItemProvider.make(
            internalIdentifier: entryID.uuidString,
            swatches: [],
            exportRequest: MeasurementHistoryDragExportRequest(
                mode: .reflectance,
                entries: selectedEntries,
                orderedEntries: entries
            )
        )
    }

    private func insertionDropZone(
        entryID: MeasurementHistoryEntry.ID,
        placement: MeasurementHistoryDropPlacement,
        width: Double
    ) -> some View {
        ZStack {
            Color.clear
                .contentShape(Rectangle())

            if dropTarget == MeasurementHistoryDropTarget(
                entryID: entryID,
                placement: placement
            ) {
                MeasurementHistoryInsertionIndicator(placement: placement)
            }
        }
        .frame(width: width)
        .frame(maxHeight: .infinity)
        .contentShape(Rectangle())
        .onDrop(
            of: [.plainText],
            delegate: MeasurementHistoryDropDelegate(
                targetEntryID: entryID,
                placement: placement,
                historyStore: historyStore,
                dragState: $dragState,
                dropTarget: $dropTarget
            )
        )
        .animation(.easeOut(duration: 0.14), value: dropTarget)
        .accessibilityHidden(true)
    }

    private func deleteSelectedEntries() {
        guard historyStore.removeSelectedEntries(for: .reflectance) > 0 else { return }
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
        historyStore.selectAll(for: .reflectance)
    }

    private func deselectAllEntries() {
        guard historyStore.deselectAll(for: .reflectance) else { return }
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

struct MeasurementHistoryDragState: Equatable {
    let entryIDs: Set<MeasurementHistoryEntry.ID>
}

private struct MeasurementHistoryCard: View {
    private static let colorAreaHeight = MeasurementHistoryCardMetrics.colorAreaHeight
    private static let nameFieldHeight = 24.0
    private static let nameFieldBottomPadding = 4.0

    private static var nameFieldTopPadding: Double {
        colorAreaHeight - nameFieldHeight - nameFieldBottomPadding
    }

    @Binding var editingName: String

    let entry: MeasurementHistoryEntry
    let width: Double
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

    private let colorConversion: LabColorConversion?

    init(
        entry: MeasurementHistoryEntry,
        width: Double,
        isSelected: Bool,
        isActive: Bool,
        isEditingName: Bool,
        editingName: Binding<String>,
        nameEditorFocusRequest: UUID?,
        onSelect: @escaping () -> Void,
        onBeginRenaming: @escaping () -> Void,
        onCommitRenaming: @escaping (String) -> Void,
        onNavigateRenaming: @escaping (
            String,
            MeasurementHistoryNameNavigationDirection
        ) -> Void,
        onCancelRenaming: @escaping () -> Void,
        dragPreviewEntries: [MeasurementHistoryEntry],
        onBeginDragging: @escaping () -> NSItemProvider
    ) {
        self._editingName = editingName
        self.entry = entry
        self.width = width
        self.isSelected = isSelected
        self.isActive = isActive
        self.isEditingName = isEditingName
        self.nameEditorFocusRequest = nameEditorFocusRequest
        self.onSelect = onSelect
        self.onBeginRenaming = onBeginRenaming
        self.onCommitRenaming = onCommitRenaming
        self.onNavigateRenaming = onNavigateRenaming
        self.onCancelRenaming = onCancelRenaming
        self.dragPreviewEntries = dragPreviewEntries
        self.onBeginDragging = onBeginDragging
        self.colorConversion = entry.measurement.lab.flatMap {
            LabColorConverter.convert(lab: $0, whitePoint: entry.measurement.labWhitePoint)
        }
    }

    var body: some View {
        ZStack(alignment: .top) {
            selectionSurface
            nameField
        }
        .help("クリックで選択し、Lab値の上にある名前欄をダブルクリックすると名前を編集できます。編集中はTabで次、Shift+Tabで前のカードへ移動します。Commandクリックで追加選択、Shiftクリックで範囲選択します。Cmd+Aで全選択、Cmd+Dで選択解除、ドラッグで並べ替え、Deleteキーで削除できます。")
        .accessibilityElement(children: isEditingName ? .contain : .ignore)
        .accessibilityLabel(accessibilityLabel)
        .accessibilityValue(accessibilitySelectionValue)
        .accessibilityHint("クリックして測定値とスペクトルを表示します。ダブルクリックまたは名前を付けるアクションでカード名を編集できます。")
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
                MeasurementHistoryDragPreview(entries: dragPreviewEntries)
            }
        }
    }

    private var cardSurface: some View {
        VStack(spacing: 0) {
            colorSwatch
            labValues
        }
        .frame(
            width: width,
            height: MeasurementHistoryCardMetrics.size.height
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

    private var colorSwatch: some View {
        Rectangle()
            .fill(cardColor)
            .frame(width: width, height: Self.colorAreaHeight)
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
        .padding(.horizontal, 5)
        .frame(width: width - 16, height: Self.nameFieldHeight)
        .background(Color.white.opacity(0.5), in: .rect(cornerRadius: 6))
        .overlay {
            RoundedRectangle(cornerRadius: 6)
                .strokeBorder(
                    isEditingName ? Color.accentColor : Color.primary.opacity(0.22),
                    lineWidth: isEditingName ? 2 : 1
                )
        }
        .padding(.top, Self.nameFieldTopPadding)
        .accessibilityHidden(isEditingName == false)
    }

    private var cardColor: Color {
        colorConversion.map { Color(cgColor: $0.managedColor) }
            ?? Color.secondary.opacity(0.15)
    }

    private var labValues: some View {
        Grid(horizontalSpacing: 7, verticalSpacing: 2) {
            GridRow {
                labMetric("L*", entry.measurement.lab?.first)
                labMetric("a*", entry.measurement.lab?.second)
                labMetric("b*", entry.measurement.lab?.third)
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 8)
    }

    private func labMetric(_ label: String, _ value: Double?) -> some View {
        VStack(alignment: .leading, spacing: 1) {
            Text(label)
                .font(.caption2)
                .foregroundStyle(.secondary)
            Text(value?.formatted(.number.precision(.fractionLength(1))) ?? "—")
                .font(.caption.monospacedDigit())
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var accessibilityLabel: String {
        guard let lab = entry.measurement.lab else {
            return [entry.name, "Lab値なし"]
                .compactMap { $0 }
                .joined(separator: "、")
        }
        let gamutDescription = colorConversion?.sRGB.isOutOfGamut == true
            ? "sRGB色域外"
            : "sRGB色域内"
        let nameDescription = entry.name.map { "名前 \($0)、" } ?? ""
        return "\(nameDescription)Lab、Lスター \(formatted(lab.first))、aスター \(formatted(lab.second))、bスター \(formatted(lab.third))、\(gamutDescription)"
    }

    private var accessibilitySelectionValue: String {
        if isActive {
            return "選択中、測定値を表示中"
        }
        return isSelected ? "選択中" : "未選択"
    }

    private func formatted(_ value: Double) -> String {
        value.formatted(.number.precision(.fractionLength(1)))
    }
}

private struct MeasurementHistoryDragPreview: View {
    private static let cardSize = CGSize(width: 92, height: 64)
    private static let xOffset = 12.0
    private static let yOffset = 7.0
    private static let maximumVisibleCardCount = 4

    let entries: [MeasurementHistoryEntry]

    private var visibleEntries: [MeasurementHistoryEntry] {
        Array(entries.prefix(Self.maximumVisibleCardCount))
    }

    var body: some View {
        ZStack(alignment: .topLeading) {
            ForEach(Array(visibleEntries.enumerated()), id: \.element.id) { index, entry in
                RoundedRectangle(cornerRadius: 8)
                    .fill(previewColor(for: entry))
                    .frame(width: Self.cardSize.width, height: Self.cardSize.height)
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
            width: Self.cardSize.width
                + Double(max(visibleEntries.count - 1, 0)) * Self.xOffset,
            height: Self.cardSize.height
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

    private func previewColor(for entry: MeasurementHistoryEntry) -> Color {
        guard let lab = entry.measurement.lab,
              let conversion = LabColorConverter.convert(
                  lab: lab,
                  whitePoint: entry.measurement.labWhitePoint
              ) else {
            return Color.secondary.opacity(0.4)
        }
        return Color(cgColor: conversion.managedColor)
    }
}

struct MeasurementHistoryDropTarget: Equatable {
    let entryID: MeasurementHistoryEntry.ID
    let placement: MeasurementHistoryDropPlacement
}

enum MeasurementHistoryDropPlacement: Equatable {
    case before
    case after
}

enum MeasurementHistoryInsertionIndicatorMetrics {
    static let width = 4.0

    static func alignment(
        for placement: MeasurementHistoryDropPlacement
    ) -> Alignment {
        switch placement {
        case .before:
            .leading
        case .after:
            .trailing
        }
    }

    static func horizontalOffset(
        for placement: MeasurementHistoryDropPlacement
    ) -> Double {
        switch placement {
        case .before:
            -width / 2
        case .after:
            width / 2
        }
    }
}

struct MeasurementHistoryInsertionIndicator: View {
    let placement: MeasurementHistoryDropPlacement

    var body: some View {
        Capsule()
            .fill(Color.accentColor)
            .frame(width: MeasurementHistoryInsertionIndicatorMetrics.width)
            .padding(.vertical, 7)
            .frame(
                maxWidth: .infinity,
                alignment: MeasurementHistoryInsertionIndicatorMetrics.alignment(
                    for: placement
                )
            )
            .offset(
                x: MeasurementHistoryInsertionIndicatorMetrics.horizontalOffset(
                    for: placement
                )
            )
            .transition(.opacity.combined(with: .scale(scale: 0.8)))
    }
}

struct MeasurementHistoryDropDelegate: DropDelegate {
    let targetEntryID: MeasurementHistoryEntry.ID
    let placement: MeasurementHistoryDropPlacement
    let historyStore: MeasurementHistoryStore
    @Binding var dragState: MeasurementHistoryDragState?
    @Binding var dropTarget: MeasurementHistoryDropTarget?

    func validateDrop(info _: DropInfo) -> Bool {
        guard let dragState else { return false }
        return dragState.entryIDs.contains(targetEntryID) == false
    }

    func dropEntered(info _: DropInfo) {
        updateDropTarget()
    }

    func dropExited(info _: DropInfo) {
        if dropTarget?.entryID == targetEntryID {
            dropTarget = nil
        }
    }

    func dropUpdated(info _: DropInfo) -> DropProposal? {
        updateDropTarget()
        return DropProposal(operation: .move)
    }

    func performDrop(info: DropInfo) -> Bool {
        defer {
            dragState = nil
            dropTarget = nil
        }

        guard validateDrop(info: info), let dragState else {
            return false
        }

        withAnimation(.snappy(duration: 0.28)) {
            _ = historyStore.move(
                entryIDs: dragState.entryIDs,
                relativeTo: targetEntryID,
                placeAfter: placement == .after
            )
        }
        return true
    }

    private func updateDropTarget() {
        guard let dragState,
              dragState.entryIDs.contains(targetEntryID) == false else { return }
        withAnimation(.easeOut(duration: 0.14)) {
            dropTarget = MeasurementHistoryDropTarget(
                entryID: targetEntryID,
                placement: placement
            )
        }
    }
}

struct MeasurementHistoryKeyboardResponder: NSViewRepresentable {
    let focusRequest: UUID?
    let onDelete: () -> Void
    let onSelectAll: () -> Void
    let onDeselectAll: () -> Void

    func makeNSView(context: Context) -> MeasurementHistoryKeyboardView {
        let view = MeasurementHistoryKeyboardView()
        configure(view)
        if let focusRequest {
            context.coordinator.lastFocusRequest = focusRequest
            view.requestFocus()
        }
        return view
    }

    func updateNSView(
        _ nsView: MeasurementHistoryKeyboardView,
        context: Context
    ) {
        configure(nsView)
        guard let focusRequest else {
            context.coordinator.lastFocusRequest = nil
            nsView.cancelPendingFocusRequest()
            return
        }
        guard context.coordinator.lastFocusRequest != focusRequest else { return }

        context.coordinator.lastFocusRequest = focusRequest
        nsView.requestFocus()
    }

    static func dismantleNSView(
        _ nsView: MeasurementHistoryKeyboardView,
        coordinator _: Coordinator
    ) {
        nsView.cancelPendingFocusRequest()
    }

    func makeCoordinator() -> Coordinator {
        Coordinator()
    }

    private func configure(_ view: MeasurementHistoryKeyboardView) {
        view.onDelete = onDelete
        view.onSelectAll = onSelectAll
        view.onDeselectAll = onDeselectAll
    }

    @MainActor
    final class Coordinator {
        var lastFocusRequest: UUID?
    }
}

@MainActor
final class MeasurementHistoryKeyboardView: NSView {
    var onDelete: () -> Void = {}
    var onSelectAll: () -> Void = {}
    var onDeselectAll: () -> Void = {}

    private var hasPendingFocusRequest = false

    override var acceptsFirstResponder: Bool {
        true
    }

    func requestFocus() {
        hasPendingFocusRequest = true
        Task { @MainActor [weak self] in
            self?.applyPendingFocusRequest()
        }
    }

    func cancelPendingFocusRequest() {
        hasPendingFocusRequest = false
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        applyPendingFocusRequest()
    }

    override func keyDown(with event: NSEvent) {
        guard handle(event) == false else { return }
        super.keyDown(with: event)
    }

    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        handle(event) || super.performKeyEquivalent(with: event)
    }

    override func selectAll(_ sender: Any?) {
        onSelectAll()
    }

    override func deleteBackward(_ sender: Any?) {
        onDelete()
    }

    override func deleteForward(_ sender: Any?) {
        onDelete()
    }

    private func applyPendingFocusRequest() {
        guard hasPendingFocusRequest, let window else { return }
        hasPendingFocusRequest = false
        window.makeFirstResponder(self)
    }

    private func handle(_ event: NSEvent) -> Bool {
        let modifiers = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        let commandDisallowedModifiers = modifiers.intersection([.control, .option, .shift])

        if modifiers.contains(.command), commandDisallowedModifiers.isEmpty {
            switch event.charactersIgnoringModifiers?.lowercased() {
            case "a":
                onSelectAll()
                return true
            case "d":
                onDeselectAll()
                return true
            default:
                break
            }
        }

        let deleteDisallowedModifiers = modifiers.intersection([.command, .control, .option])
        if deleteDisallowedModifiers.isEmpty,
           event.keyCode == 51 || event.keyCode == 117 {
            onDelete()
            return true
        }

        return false
    }
}
