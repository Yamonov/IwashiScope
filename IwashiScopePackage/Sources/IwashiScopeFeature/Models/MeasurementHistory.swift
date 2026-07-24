import Foundation
import Observation

struct MeasurementHistoryEntry: Identifiable, Equatable, Sendable {
    let id: UUID
    let name: String?
    let measurement: SpotMeasurement
    let instrumentIdentity: SpotreadInstrumentIdentity?

    init(
        id: UUID = UUID(),
        name: String? = nil,
        measurement: SpotMeasurement,
        instrumentIdentity: SpotreadInstrumentIdentity? = nil
    ) {
        self.id = id
        self.name = Self.normalizedName(name)
        self.measurement = measurement
        self.instrumentIdentity = instrumentIdentity
    }

    var swatchName: String? {
        if let name {
            return name
        }

        guard let lab = measurement.lab,
              let conversion = LabColorConverter.convert(
                lab: lab,
                whitePoint: measurement.labWhitePoint
              ) else {
            return nil
        }
        let hex = conversion.sRGB.hex
        return hex.hasPrefix("#") ? String(hex.dropFirst()) : hex
    }

    private static func normalizedName(_ name: String?) -> String? {
        guard let name else { return nil }
        let singleLineName = name
            .replacingOccurrences(of: "\r\n", with: " ")
            .replacingOccurrences(of: "\n", with: " ")
            .replacingOccurrences(of: "\r", with: " ")
            .trimmingCharacters(in: .whitespacesAndNewlines)
        return singleLineName.isEmpty ? nil : singleLineName
    }
}

enum MeasurementHistorySelectionAction: Equatable, Sendable {
    case exclusive
    case toggle
    case range(additive: Bool)
}

@MainActor
@Observable
final class MeasurementHistoryStore {
    /// Acquisition order is never changed by presentation reordering.
    private(set) var entries: [MeasurementHistoryEntry] = []
    private var presentationOrderByMode: [MeasurementMode: [MeasurementHistoryEntry.ID]] = [:]
    private var selectedEntryIDsByMode: [MeasurementMode: Set<MeasurementHistoryEntry.ID>] = [:]
    private var activeEntryIDByMode: [MeasurementMode: MeasurementHistoryEntry.ID] = [:]
    private var selectionAnchorIDByMode: [MeasurementMode: MeasurementHistoryEntry.ID] = [:]

    @discardableResult
    func append(
        _ measurement: SpotMeasurement,
        instrumentIdentity: SpotreadInstrumentIdentity? = nil
    ) -> MeasurementHistoryEntry {
        let entry = MeasurementHistoryEntry(
            measurement: measurement,
            instrumentIdentity: instrumentIdentity
        )
        entries.append(entry)
        presentationOrderByMode[measurement.mode, default: []].append(entry.id)
        setExclusiveSelection(entry.id, for: measurement.mode)
        return entry
    }

    func selectedEntryID(for mode: MeasurementMode) -> MeasurementHistoryEntry.ID? {
        activeEntryIDByMode[mode]
    }

    func selectedEntryIDs(for mode: MeasurementMode) -> Set<MeasurementHistoryEntry.ID> {
        selectedEntryIDsByMode[mode, default: []]
    }

    func selectedEntry(for mode: MeasurementMode) -> MeasurementHistoryEntry? {
        guard let selectedEntryID = activeEntryIDByMode[mode] else {
            return nil
        }
        return entries.first { $0.id == selectedEntryID }
    }

    func selectedEntries(for mode: MeasurementMode) -> [MeasurementHistoryEntry] {
        let selectedEntryIDs = selectedEntryIDs(for: mode)
        return orderedEntries(for: mode).filter { selectedEntryIDs.contains($0.id) }
    }

    @discardableResult
    func setName(
        _ name: String?,
        for entryID: MeasurementHistoryEntry.ID
    ) -> Bool {
        guard let entryIndex = entries.firstIndex(where: { $0.id == entryID }) else {
            return false
        }

        let entry = entries[entryIndex]
        entries[entryIndex] = MeasurementHistoryEntry(
            id: entry.id,
            name: name,
            measurement: entry.measurement,
            instrumentIdentity: entry.instrumentIdentity
        )
        return true
    }

    func workspaceSnapshot() -> MeasurementHistorySnapshot {
        MeasurementHistorySnapshot(
            entries: entries.map { entry in
                MeasurementHistorySnapshot.Entry(
                    id: entry.id,
                    name: entry.name,
                    measurement: entry.measurement,
                    instrumentIdentity: entry.instrumentIdentity
                )
            },
            modes: MeasurementMode.allCases.map { mode in
                MeasurementHistorySnapshot.ModeState(
                    mode: mode,
                    presentationOrder: orderedEntries(for: mode).map(\.id),
                    selectedEntryIDs: selectedEntryIDs(for: mode),
                    activeEntryID: activeEntryIDByMode[mode],
                    selectionAnchorID: selectionAnchorIDByMode[mode]
                )
            }
        )
    }

    func restore(from snapshot: MeasurementHistorySnapshot) throws {
        try snapshot.validate()

        let restoredEntries = snapshot.entries.map { entry in
            MeasurementHistoryEntry(
                id: entry.id,
                name: entry.name,
                measurement: entry.measurement,
                instrumentIdentity: entry.instrumentIdentity
            )
        }
        let modeStates = Dictionary(uniqueKeysWithValues: snapshot.modes.map { ($0.mode, $0) })

        entries = restoredEntries
        presentationOrderByMode = modeStates.compactMapValues { modeState in
            modeState.presentationOrder.isEmpty ? nil : modeState.presentationOrder
        }
        selectedEntryIDsByMode = modeStates.compactMapValues { modeState in
            modeState.selectedEntryIDs.isEmpty ? nil : modeState.selectedEntryIDs
        }
        activeEntryIDByMode = modeStates.compactMapValues(\.activeEntryID)
        selectionAnchorIDByMode = modeStates.compactMapValues(\.selectionAnchorID)
    }

    @discardableResult
    func select(entryID: MeasurementHistoryEntry.ID) -> Bool {
        select(entryID: entryID, action: .exclusive)
    }

    @discardableResult
    func select(
        entryID: MeasurementHistoryEntry.ID,
        action: MeasurementHistorySelectionAction
    ) -> Bool {
        guard let entry = entries.first(where: { $0.id == entryID }) else {
            return false
        }

        let mode = entry.measurement.mode
        switch action {
        case .exclusive:
            setExclusiveSelection(entryID, for: mode)
        case .toggle:
            toggleSelection(entryID, for: mode)
        case .range(let additive):
            selectRange(through: entryID, for: mode, additive: additive)
        }
        return true
    }

    @discardableResult
    func selectAll(for mode: MeasurementMode) -> Bool {
        let orderedIDs = orderedEntries(for: mode).map(\.id)
        guard orderedIDs.isEmpty == false else {
            return false
        }

        selectedEntryIDsByMode[mode] = Set(orderedIDs)
        let activeEntryID = activeEntryIDByMode[mode].flatMap { currentID in
            orderedIDs.contains(currentID) ? currentID : nil
        } ?? orderedIDs.last
        activeEntryIDByMode[mode] = activeEntryID
        selectionAnchorIDByMode[mode] = activeEntryID
        return true
    }

    @discardableResult
    func deselectAll(for mode: MeasurementMode) -> Bool {
        guard selectedEntryIDs(for: mode).isEmpty == false else {
            return false
        }

        clearSelection(for: mode)
        return true
    }

    func orderedEntries(for mode: MeasurementMode) -> [MeasurementHistoryEntry] {
        let entriesForMode = entries.filter { $0.measurement.mode == mode }
        guard let orderedIDs = presentationOrderByMode[mode],
              orderedIDs.count == entriesForMode.count else {
            return entriesForMode
        }

        let entriesByID = Dictionary(uniqueKeysWithValues: entriesForMode.map { ($0.id, $0) })
        return orderedIDs.compactMap { entriesByID[$0] }
    }

    @discardableResult
    func move(
        entryID: MeasurementHistoryEntry.ID,
        relativeTo targetID: MeasurementHistoryEntry.ID,
        placeAfter: Bool
    ) -> Bool {
        move(entryIDs: [entryID], relativeTo: targetID, placeAfter: placeAfter)
    }

    @discardableResult
    func move(
        entryIDs: Set<MeasurementHistoryEntry.ID>,
        relativeTo targetID: MeasurementHistoryEntry.ID,
        placeAfter: Bool
    ) -> Bool {
        guard entryIDs.isEmpty == false,
              entryIDs.contains(targetID) == false,
              let target = entries.first(where: { $0.id == targetID }),
              let orderedIDs = presentationOrderByMode[target.measurement.mode] else {
            return false
        }

        let movableEntries = entries.filter { entryIDs.contains($0.id) }
        guard movableEntries.count == entryIDs.count,
              movableEntries.allSatisfy({ $0.measurement.mode == target.measurement.mode }) else {
            return false
        }

        let movingIDs = orderedIDs.filter { entryIDs.contains($0) }
        guard movingIDs.count == entryIDs.count else {
            return false
        }

        var reorderedIDs = orderedIDs.filter { entryIDs.contains($0) == false }
        guard let adjustedTargetIndex = reorderedIDs.firstIndex(of: targetID) else {
            return false
        }

        let destinationIndex = adjustedTargetIndex + (placeAfter ? 1 : 0)
        reorderedIDs.insert(contentsOf: movingIDs, at: destinationIndex)
        guard reorderedIDs != orderedIDs else {
            return false
        }

        presentationOrderByMode[target.measurement.mode] = reorderedIDs
        return true
    }

    @discardableResult
    func move(
        entryIDs: Set<MeasurementHistoryEntry.ID>,
        in mode: MeasurementMode,
        toInsertionIndex insertionIndex: Int
    ) -> Bool {
        guard entryIDs.isEmpty == false,
              let orderedIDs = presentationOrderByMode[mode],
              (0...orderedIDs.count).contains(insertionIndex) else {
            return false
        }

        let movingIDs = orderedIDs.filter { entryIDs.contains($0) }
        guard movingIDs.count == entryIDs.count else { return false }

        let movingCountBeforeInsertion = orderedIDs[..<insertionIndex]
            .filter(entryIDs.contains)
            .count
        let adjustedInsertionIndex = insertionIndex - movingCountBeforeInsertion
        var reorderedIDs = orderedIDs.filter { entryIDs.contains($0) == false }
        reorderedIDs.insert(contentsOf: movingIDs, at: adjustedInsertionIndex)

        guard reorderedIDs != orderedIDs else { return false }
        presentationOrderByMode[mode] = reorderedIDs
        return true
    }

    @discardableResult
    func removeSelectedEntry(for mode: MeasurementMode) -> Bool {
        removeSelectedEntries(for: mode) > 0
    }

    @discardableResult
    func removeSelectedEntries(for mode: MeasurementMode) -> Int {
        let selectedEntryIDs = selectedEntryIDs(for: mode)
        guard selectedEntryIDs.isEmpty == false else {
            return 0
        }

        remove(entryIDs: selectedEntryIDs, from: mode)
        return selectedEntryIDs.count
    }

    @discardableResult
    func remove(entryID: MeasurementHistoryEntry.ID) -> Bool {
        guard let entry = entries.first(where: { $0.id == entryID }) else {
            return false
        }

        remove(entryIDs: [entryID], from: entry.measurement.mode)
        return true
    }

    private func setExclusiveSelection(
        _ entryID: MeasurementHistoryEntry.ID,
        for mode: MeasurementMode
    ) {
        selectedEntryIDsByMode[mode] = [entryID]
        activeEntryIDByMode[mode] = entryID
        selectionAnchorIDByMode[mode] = entryID
    }

    private func toggleSelection(
        _ entryID: MeasurementHistoryEntry.ID,
        for mode: MeasurementMode
    ) {
        var selectedEntryIDs = selectedEntryIDs(for: mode)
        if selectedEntryIDs.remove(entryID) != nil {
            if selectedEntryIDs.isEmpty {
                clearSelection(for: mode)
                return
            }

            selectedEntryIDsByMode[mode] = selectedEntryIDs
            if activeEntryIDByMode[mode] == entryID {
                activeEntryIDByMode[mode] = lastPresentedID(
                    in: selectedEntryIDs,
                    for: mode
                )
            }
            if selectionAnchorIDByMode[mode] == entryID {
                selectionAnchorIDByMode[mode] = activeEntryIDByMode[mode]
            }
        } else {
            selectedEntryIDs.insert(entryID)
            selectedEntryIDsByMode[mode] = selectedEntryIDs
            activeEntryIDByMode[mode] = entryID
            selectionAnchorIDByMode[mode] = entryID
        }
    }

    private func selectRange(
        through entryID: MeasurementHistoryEntry.ID,
        for mode: MeasurementMode,
        additive: Bool
    ) {
        let orderedIDs = orderedEntries(for: mode).map(\.id)
        guard let targetIndex = orderedIDs.firstIndex(of: entryID),
              let anchorID = selectionAnchorIDByMode[mode],
              let anchorIndex = orderedIDs.firstIndex(of: anchorID) else {
            setExclusiveSelection(entryID, for: mode)
            return
        }

        let range = min(anchorIndex, targetIndex)...max(anchorIndex, targetIndex)
        let rangeIDs = Set(orderedIDs[range])
        if additive {
            selectedEntryIDsByMode[mode, default: []].formUnion(rangeIDs)
        } else {
            selectedEntryIDsByMode[mode] = rangeIDs
        }
        activeEntryIDByMode[mode] = entryID
    }

    private func remove(
        entryIDs: Set<MeasurementHistoryEntry.ID>,
        from mode: MeasurementMode
    ) {
        let previouslySelectedIDs = selectedEntryIDs(for: mode)
        entries.removeAll { entryIDs.contains($0.id) }

        if var orderedIDs = presentationOrderByMode[mode] {
            orderedIDs.removeAll { entryIDs.contains($0) }
            if orderedIDs.isEmpty {
                presentationOrderByMode.removeValue(forKey: mode)
            } else {
                presentationOrderByMode[mode] = orderedIDs
            }
        }

        guard previouslySelectedIDs.isDisjoint(with: entryIDs) == false else {
            return
        }

        let remainingSelectedIDs = previouslySelectedIDs.subtracting(entryIDs)
        if remainingSelectedIDs.isEmpty == false {
            selectedEntryIDsByMode[mode] = remainingSelectedIDs
            if let activeEntryID = activeEntryIDByMode[mode],
               remainingSelectedIDs.contains(activeEntryID) == false {
                activeEntryIDByMode[mode] = lastPresentedID(
                    in: remainingSelectedIDs,
                    for: mode
                )
            }
            if let anchorID = selectionAnchorIDByMode[mode],
               remainingSelectedIDs.contains(anchorID) == false {
                selectionAnchorIDByMode[mode] = activeEntryIDByMode[mode]
            }
        } else if let newestRemainingEntry = entries.last(where: { $0.measurement.mode == mode }) {
            setExclusiveSelection(newestRemainingEntry.id, for: mode)
        } else {
            clearSelection(for: mode)
        }
    }

    private func lastPresentedID(
        in entryIDs: Set<MeasurementHistoryEntry.ID>,
        for mode: MeasurementMode
    ) -> MeasurementHistoryEntry.ID? {
        orderedEntries(for: mode).last { entryIDs.contains($0.id) }?.id
    }

    private func clearSelection(for mode: MeasurementMode) {
        selectedEntryIDsByMode.removeValue(forKey: mode)
        activeEntryIDByMode.removeValue(forKey: mode)
        selectionAnchorIDByMode.removeValue(forKey: mode)
    }
}
