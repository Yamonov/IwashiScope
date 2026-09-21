import Foundation
import SwiftUI

struct MeasurementHistoryDateGroup: Identifiable, Equatable, Sendable {
    let id: Date
    let title: String
    let entries: [MeasurementHistoryEntry]

    var exportName: String { title.replacingOccurrences(of: "/", with: "-") }
}

enum MeasurementHistoryDateGrouping {
    static func groups(
        for entries: [MeasurementHistoryEntry],
        calendar: Calendar = localGregorianCalendar
    ) -> [MeasurementHistoryDateGroup] {
        var entriesByDate: [Date: [MeasurementHistoryEntry]] = [:]
        for entry in entries {
            let date = calendar.startOfDay(for: entry.measurement.capturedAt)
            entriesByDate[date, default: []].append(entry)
        }

        return entriesByDate.keys.sorted(by: >).map { date in
            MeasurementHistoryDateGroup(
                id: date,
                title: title(for: date, calendar: calendar),
                entries: entriesByDate[date, default: []]
            )
        }
    }

    private static var localGregorianCalendar: Calendar {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = .autoupdatingCurrent
        return calendar
    }

    private static func title(
        for date: Date,
        calendar: Calendar
    ) -> String {
        let components = calendar.dateComponents([.year, .month, .day], from: date)
        return String(
            format: "%04d/%02d/%02d",
            components.year ?? 0,
            components.month ?? 0,
            components.day ?? 0
        )
    }
}

enum MeasurementHistoryDateDropPolicy {
    static func allowsMove(
        entryIDs: Set<MeasurementHistoryEntry.ID>,
        within orderedEntryIDs: [MeasurementHistoryEntry.ID]
    ) -> Bool {
        entryIDs.isEmpty == false
            && entryIDs.isSubset(of: Set(orderedEntryIDs))
    }
}

struct MeasurementHistoryDateGroupView<Content: View>: View {
    @State private var isCollapsed = false

    let group: MeasurementHistoryDateGroup
    let deletableEntryIDs: Set<MeasurementHistoryEntry.ID>
    let canExport: Bool
    let onRequestDelete: (Set<MeasurementHistoryEntry.ID>, String) -> Void
    let onExport: (MeasurementHistoryDateGroup) -> Void
    @ViewBuilder let content: Content

    init(
        group: MeasurementHistoryDateGroup,
        deletableEntryIDs: Set<MeasurementHistoryEntry.ID>,
        canExport: Bool,
        onRequestDelete: @escaping (Set<MeasurementHistoryEntry.ID>, String) -> Void,
        onExport: @escaping (MeasurementHistoryDateGroup) -> Void,
        @ViewBuilder content: () -> Content
    ) {
        self.group = group
        self.deletableEntryIDs = deletableEntryIDs
        self.canExport = canExport
        self.onRequestDelete = onRequestDelete
        self.onExport = onExport
        self.content = content()
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Button(action: toggleCollapsed) {
                HStack(spacing: 6) {
                    Image(systemName: isCollapsed ? "chevron.right" : "chevron.down")
                        .font(.caption.weight(.semibold))
                        .frame(width: 10)

                    Text(group.title)
                        .font(.subheadline.weight(.semibold))
                        .monospacedDigit()

                    Spacer(minLength: 8)

                    Text("\(group.entries.count)件")
                        .font(.caption.monospacedDigit())
                        .foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .foregroundStyle(.tint)
            .help(isCollapsed ? "\(group.title)の履歴を表示" : "\(group.title)の履歴を隠す")
            .accessibilityLabel("\(group.title)、\(group.entries.count)件")
            .accessibilityValue(isCollapsed ? "折りたたみ" : "展開中")
            .accessibilityHint("クリックしてこの日付の測定履歴を表示または非表示にします")
            .accessibilityIdentifier("measurement-history-date-\(group.title)")
            .contextMenu {
                Button {
                    onExport(group)
                } label: {
                    Label("\(group.exportName)の履歴を書きだし", systemImage: "square.and.arrow.up")
                }
                .disabled(canExport == false)

                Divider()

                Button(role: .destructive) {
                    onRequestDelete(
                        deletableEntryIDs,
                        String(localized: "\(group.title)の履歴を削除しますか？")
                    )
                } label: {
                    Label("\(group.title)の履歴を削除", systemImage: "trash")
                }
                .disabled(deletableEntryIDs.isEmpty)
            }

            if isCollapsed == false {
                content
                    .transition(.identity)
            }
        }
    }

    private func toggleCollapsed() {
        // Moving a tall grid out of the hierarchy sweeps offscreen cards over
        // other dates. Collapse only this group immediately; card reordering
        // keeps its own animation in the history grids.
        var transaction = Transaction(animation: nil)
        transaction.disablesAnimations = true
        withTransaction(transaction) {
            isCollapsed.toggle()
        }
    }
}
