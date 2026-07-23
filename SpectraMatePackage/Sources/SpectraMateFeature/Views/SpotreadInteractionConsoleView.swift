import AppKit
import SwiftUI

/// AppKit-backed, incrementally updated console. Using NSTextView prevents a
/// growing communication log from invalidating and laying out a SwiftUI row
/// hierarchy for every pipe update.
struct SpotreadInteractionConsoleView: NSViewRepresentable {
    let interactions: [SpotreadInteraction]
    let revision: Int
    let followsLatestInteraction: Bool

    func makeNSView(context: Context) -> NSScrollView {
        let scrollView = NSTextView.scrollableTextView()
        guard let textView = scrollView.documentView as? NSTextView else {
            return scrollView
        }

        scrollView.hasVerticalScroller = true
        scrollView.hasHorizontalScroller = false
        scrollView.autohidesScrollers = true
        scrollView.borderType = .noBorder
        scrollView.drawsBackground = true
        scrollView.backgroundColor = .textBackgroundColor

        textView.isEditable = false
        textView.isSelectable = true
        textView.isRichText = true
        textView.drawsBackground = false
        textView.textContainerInset = NSSize(width: 12, height: 12)
        textView.textContainer?.lineFragmentPadding = 0
        textView.textContainer?.widthTracksTextView = true
        textView.isHorizontallyResizable = false
        textView.isVerticallyResizable = true
        textView.usesFindPanel = true
        textView.isIncrementalSearchingEnabled = true
        textView.setAccessibilityLabel("spotread詳細ログ")

        context.coordinator.synchronize(
            interactions: interactions,
            revision: revision,
            followsLatestInteraction: followsLatestInteraction,
            textView: textView
        )
        return scrollView
    }

    func updateNSView(_ scrollView: NSScrollView, context: Context) {
        guard let textView = scrollView.documentView as? NSTextView else { return }
        context.coordinator.synchronize(
            interactions: interactions,
            revision: revision,
            followsLatestInteraction: followsLatestInteraction,
            textView: textView
        )
    }

    func makeCoordinator() -> Coordinator {
        Coordinator()
    }

    @MainActor
    final class Coordinator {
        private struct Snapshot: Equatable {
            let id: UUID
            let timestamp: Date
            let sessionID: UUID?
            let direction: SpotreadInteractionDirection
            let content: String
            let inputDeliveryState: SpotreadInputDeliveryState?

            init(_ interaction: SpotreadInteraction) {
                id = interaction.id
                timestamp = interaction.timestamp
                sessionID = interaction.sessionID
                direction = interaction.direction
                content = interaction.content
                inputDeliveryState = interaction.inputDeliveryState
            }
        }

        private var snapshots: [Snapshot] = []
        private var rangesByID: [UUID: NSRange] = [:]
        private var lastRevision: Int?
        private var previouslyFollowedLatest = true

        func synchronize(
            interactions: [SpotreadInteraction],
            revision: Int,
            followsLatestInteraction: Bool,
            textView: NSTextView
        ) {
            let shouldSynchronize = lastRevision != revision
            let shouldScrollAfterEnabling = followsLatestInteraction && !previouslyFollowedLatest

            if shouldSynchronize {
                updateTextStorage(interactions: interactions, textView: textView)
                lastRevision = revision
            }

            if followsLatestInteraction && (shouldSynchronize || shouldScrollAfterEnabling) {
                textView.scrollToEndOfDocument(nil)
            }
            previouslyFollowedLatest = followsLatestInteraction
        }

        private func updateTextStorage(
            interactions: [SpotreadInteraction],
            textView: NSTextView
        ) {
            guard let textStorage = textView.textStorage else { return }
            let nextSnapshots = interactions.map(Snapshot.init)

            guard canUpdateIncrementally(to: nextSnapshots) else {
                rebuild(
                    interactions: interactions,
                    snapshots: nextSnapshots,
                    textStorage: textStorage
                )
                return
            }

            textStorage.beginEditing()
            let existingCount = snapshots.count

            for index in 0..<existingCount where snapshots[index] != nextSnapshots[index] {
                replaceInteraction(
                    interactions[index],
                    at: index,
                    textStorage: textStorage
                )
            }

            for index in existingCount..<interactions.count {
                appendInteraction(interactions[index], textStorage: textStorage)
            }

            textStorage.endEditing()
            snapshots = nextSnapshots
        }

        private func canUpdateIncrementally(to nextSnapshots: [Snapshot]) -> Bool {
            // The empty state contains placeholder text rather than interaction
            // blocks. Rebuild when the first interaction arrives so that the
            // placeholder is removed before appending the log.
            guard !snapshots.isEmpty else { return false }
            guard nextSnapshots.count >= snapshots.count else { return false }
            return snapshots.indices.allSatisfy { snapshots[$0].id == nextSnapshots[$0].id }
        }

        private func rebuild(
            interactions: [SpotreadInteraction],
            snapshots nextSnapshots: [Snapshot],
            textStorage: NSTextStorage
        ) {
            rangesByID.removeAll(keepingCapacity: true)
            textStorage.beginEditing()
            textStorage.setAttributedString(NSAttributedString())

            if interactions.isEmpty {
                textStorage.append(Self.placeholder)
            } else {
                for interaction in interactions {
                    appendInteraction(interaction, textStorage: textStorage)
                }
            }

            textStorage.endEditing()
            snapshots = nextSnapshots
        }

        private func appendInteraction(
            _ interaction: SpotreadInteraction,
            textStorage: NSTextStorage
        ) {
            let block = Self.attributedBlock(for: interaction)
            let range = NSRange(location: textStorage.length, length: block.length)
            textStorage.append(block)
            rangesByID[interaction.id] = range
        }

        private func replaceInteraction(
            _ interaction: SpotreadInteraction,
            at index: Int,
            textStorage: NSTextStorage
        ) {
            guard let previousRange = rangesByID[interaction.id] else { return }
            let replacement = Self.attributedBlock(for: interaction)
            textStorage.replaceCharacters(in: previousRange, with: replacement)

            let lengthDelta = replacement.length - previousRange.length
            rangesByID[interaction.id] = NSRange(
                location: previousRange.location,
                length: replacement.length
            )

            guard lengthDelta != 0, index + 1 < snapshots.count else { return }
            for followingIndex in (index + 1)..<snapshots.count {
                let followingID = snapshots[followingIndex].id
                guard let range = rangesByID[followingID] else { continue }
                rangesByID[followingID] = NSRange(
                    location: range.location + lengthDelta,
                    length: range.length
                )
            }
        }

        private static func attributedBlock(
            for interaction: SpotreadInteraction
        ) -> NSAttributedString {
            let result = NSMutableAttributedString(string: "")
            let headerParts = [
                interaction.timestamp.formatted(date: .omitted, time: .standard),
                interaction.direction.label,
                interaction.sessionID.map {
                    "session \(String($0.uuidString.prefix(8)).lowercased())"
                },
                interaction.inputDeliveryState?.label,
            ]
            .compactMap { $0 }
            let header = headerParts.joined(separator: "  ") + "\n"

            result.append(
                NSAttributedString(
                    string: header,
                    attributes: [
                        .font: NSFont.monospacedSystemFont(ofSize: 10, weight: .semibold),
                        .foregroundColor: color(for: interaction.direction),
                    ]
                )
            )
            result.append(
                NSAttributedString(
                    string: interaction.displayContent + "\n\n",
                    attributes: [
                        .font: NSFont.monospacedSystemFont(ofSize: 11, weight: .regular),
                        .foregroundColor: NSColor.labelColor,
                    ]
                )
            )
            return result
        }

        private static var placeholder: NSAttributedString {
            NSAttributedString(
                string: "spotreadとの通信を待っています\n",
                attributes: [
                    .font: NSFont.monospacedSystemFont(ofSize: 11, weight: .regular),
                    .foregroundColor: NSColor.secondaryLabelColor,
                ]
            )
        }

        private static func color(
            for direction: SpotreadInteractionDirection
        ) -> NSColor {
            switch direction {
            case .input:
                .systemBlue
            case .output:
                .systemGreen
            case .lifecycle:
                .systemOrange
            }
        }
    }
}
