import SwiftUI

public struct SpotreadDebugWindowView: View {
    @State private var followsLatestInteraction = true

    private let session: MeasurementSession

    public init(model: IwashiScopeApplicationModel) {
        self.session = model.session
    }

    init(session: MeasurementSession) {
        self.session = session
    }

    public var body: some View {
        VStack(spacing: 0) {
            SpotreadDebugHeader(
                session: session,
                followsLatestInteraction: $followsLatestInteraction
            )

            Divider()

            SpotreadInteractionConsoleView(
                interactions: session.interactions,
                revision: session.interactionRevision,
                followsLatestInteraction: followsLatestInteraction
            )
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

private struct SpotreadDebugHeader: View {
    let session: MeasurementSession
    @Binding var followsLatestInteraction: Bool

    var body: some View {
        HStack(alignment: .top, spacing: 14) {
            VStack(alignment: .leading, spacing: 5) {
                Label(
                    session.isRunning ? "spotread実行中" : "spotread停止中",
                    systemImage: session.isRunning ? "record.circle.fill" : "stop.circle"
                )
                .foregroundStyle(session.isRunning ? .green : .secondary)
                .accessibilityIdentifier("spotread-debug-status")

                SpotreadDebugMetadataRow(
                    label: "モード",
                    value: session.mode?.title ?? "—",
                    accessibilityIdentifier: "spotread-debug-mode"
                )

                SpotreadDebugMetadataRow(
                    label: "パス",
                    value: session.executablePath ?? "—",
                    accessibilityIdentifier: "spotread-debug-path",
                    usesMonospacedFont: true
                )
            }
            .frame(maxWidth: .infinity, alignment: .leading)

            Spacer(minLength: 12)

            HStack(spacing: 12) {
                Toggle("最新", isOn: $followsLatestInteraction)
                    .toggleStyle(.checkbox)

                Button("ログを消去") {
                    session.clearInteractionLog()
                }
                .disabled(session.interactions.isEmpty)
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 12)
    }
}

private struct SpotreadDebugMetadataRow: View {
    let label: String
    let value: String
    let accessibilityIdentifier: String
    var usesMonospacedFont = false

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Text(label)
                .font(.caption)
                .foregroundStyle(.secondary)
                .frame(width: 38, alignment: .leading)

            Text(value)
                .font(usesMonospacedFont ? .caption.monospaced() : .callout)
                .lineLimit(1)
                .truncationMode(.middle)
                .help(value)
        }
        .accessibilityElement(children: .combine)
        .accessibilityIdentifier(accessibilityIdentifier)
    }
}
