import SwiftUI

struct ModeSelectionView: View {
    let onSelect: (MeasurementMode) -> Void

    private let isSpotreadAvailable = SpotreadExecutableLocator.locate() != nil

    var body: some View {
        VStack(spacing: 32) {
            VStack(spacing: 10) {
                Image(systemName: "waveform.path.ecg.rectangle")
                    .font(.system(size: 42, weight: .medium))
                    .foregroundStyle(.tint)
                    .accessibilityHidden(true)

                Text("IwashiScope")
                    .font(.largeTitle.weight(.semibold))

                Text("Spectral & Color Measurement")
                    .font(.title3)

                Text("分光・測色ツール")
                    .font(.headline)
                    .foregroundStyle(.secondary)

                Text("測定モードを選択")
                    .font(.title2.weight(.semibold))
                    .padding(.top, 4)

                Text("選択後にspotreadを高解像度モードで起動します")
                    .font(.body)
                    .foregroundStyle(.secondary)
            }

            HStack(alignment: .top, spacing: 18) {
                ForEach(MeasurementMode.allCases) { mode in
                    MeasurementModeCard(mode: mode) {
                        onSelect(mode)
                    }
                }
            }
            .frame(maxWidth: 900)

            SpotreadAvailabilityView(isAvailable: isSpotreadAvailable)
        }
        .padding(40)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background {
            LinearGradient(
                colors: [.accentColor.opacity(0.09), .clear, .purple.opacity(0.05)],
                startPoint: .topLeading,
                endPoint: .bottomTrailing
            )
        }
    }
}

private struct MeasurementModeCard: View {
    let mode: MeasurementMode
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            VStack(alignment: .leading, spacing: 14) {
                Image(systemName: mode.systemImage)
                    .font(.system(size: 30, weight: .medium))
                    .foregroundStyle(.tint)
                    .frame(height: 36)
                    .accessibilityHidden(true)

                VStack(alignment: .leading, spacing: 4) {
                    Text(mode.title)
                        .font(.title2.weight(.semibold))
                    Text(mode.subtitle)
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }

                Text(mode.detail)
                    .font(.body)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)

                Spacer(minLength: 6)

                Label("このモードで開始", systemImage: "arrow.right.circle.fill")
                    .font(.headline)
                    .foregroundStyle(.tint)
            }
            .frame(maxWidth: .infinity, minHeight: 230, alignment: .leading)
            .padding(22)
            .background(.regularMaterial)
            .overlay {
                RoundedRectangle(cornerRadius: 16)
                    .strokeBorder(.separator.opacity(0.65), lineWidth: 1)
            }
            .compositingGroup()
            .clipShape(.rect(cornerRadius: 16))
            .contentShape(.rect)
        }
        .buttonStyle(.plain)
        .accessibilityIdentifier("mode-\(mode.rawValue)")
        .accessibilityLabel("\(mode.title)、\(mode.subtitle)。\(mode.detail)")
        .accessibilityHint("この測定モードでspotreadを起動します")
    }
}

private struct SpotreadAvailabilityView: View {
    let isAvailable: Bool

    var body: some View {
        HStack(spacing: 8) {
            Image(systemName: isAvailable ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                .foregroundStyle(isAvailable ? .green : .orange)
                .accessibilityHidden(true)

            if isAvailable {
                Text("spotreadを確認しました")
            } else {
                Text("spotreadが見つかりません。起動後に設定方法を表示します。")
            }
        }
        .font(.callout)
        .foregroundStyle(.secondary)
        .accessibilityElement(children: .combine)
    }
}
