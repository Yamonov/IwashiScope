import Foundation
import SwiftUI

struct MeasurementHistoryDateGroup: Identifiable, Equatable, Sendable {
    let id: Date
    let title: String
    let entries: [MeasurementHistoryEntry]
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
    @ViewBuilder let content: Content

    init(
        group: MeasurementHistoryDateGroup,
        @ViewBuilder content: () -> Content
    ) {
        self.group = group
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

            if isCollapsed == false {
                content
                    .transition(.opacity.combined(with: .move(edge: .top)))
            }
        }
    }

    private func toggleCollapsed() {
        withAnimation(.snappy(duration: 0.22)) {
            isCollapsed.toggle()
        }
    }
}
