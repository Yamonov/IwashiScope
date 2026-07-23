import SwiftUI

struct MeasurementExportFooter: View {
    let mode: MeasurementMode
    let availability: MeasurementExportAvailability
    let isExporting: Bool
    let onExport: (MeasurementExportOptions) -> Void

    @State private var options: MeasurementExportOptions

    init(
        mode: MeasurementMode,
        availability: MeasurementExportAvailability,
        isExporting: Bool,
        onExport: @escaping (MeasurementExportOptions) -> Void
    ) {
        self.mode = mode
        self.availability = availability
        self.isExporting = isExporting
        self.onExport = onExport
        _options = State(initialValue: .defaults(for: mode))
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Divider()

            HStack {
                Label("書き出し", systemImage: "square.and.arrow.up")
                    .font(.headline)

                Spacer()

                Text("\(availability.selectionCount)件選択")
                    .font(.caption.monospacedDigit())
                    .foregroundStyle(.secondary)
            }

            if mode == .reflectance {
                reflectanceOptions
            } else {
                lightingOptions
            }

            Button {
                onExport(options)
            } label: {
                HStack {
                    if isExporting {
                        ProgressView()
                            .controlSize(.small)
                    }
                    Text(isExporting ? "書き出し中…" : "書き出し")
                }
                .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
            .disabled(
                isExporting
                    || availability.canExport(mode: mode, options: options) == false
            )
        }
        .font(.caption)
        .padding(.horizontal, 16)
        .padding(.bottom, 12)
        .background(.bar)
    }

    private var reflectanceOptions: some View {
        VStack(alignment: .leading, spacing: 5) {
            exportToggle(
                "スウォッチ",
                isOn: $options.includesSwatch,
                isAvailable: availability.hasLab
            )
            exportToggle(
                "スペクトル画像（幅3,000 px PNG）",
                isOn: $options.includesSpectrumImage,
                isAvailable: availability.hasSpectrum
            )
            exportToggle(
                "スペクトルCSV",
                isOn: $options.includesCSV,
                isAvailable: availability.hasSpectrum
            )
        }
    }

    private var lightingOptions: some View {
        VStack(alignment: .leading, spacing: 5) {
            exportToggle(
                "スペクトル画像（幅3,000 px PNG）",
                isOn: $options.includesSpectrumImage,
                isAvailable: availability.hasSpectrum
            )

            HStack(spacing: 18) {
                exportToggle(
                    "D50線",
                    isOn: $options.includesD50Reference,
                    isAvailable: availability.hasSpectrum
                        && options.includesSpectrumImage
                )
                exportToggle(
                    "D65線",
                    isOn: $options.includesD65Reference,
                    isAvailable: availability.hasSpectrum
                        && options.includesSpectrumImage
                )
            }
            .padding(.leading, 22)
            .opacity(options.includesSpectrumImage ? 1 : 0.45)

            exportToggle(
                "CRI画像（幅3,000 px PNG）",
                isOn: $options.includesCRIImage,
                isAvailable: availability.hasCRI
            )
            exportToggle(
                "TM-30-15画像（幅3,000 px PNG）",
                isOn: $options.includesTM30Image,
                isAvailable: availability.hasTM30
            )
            exportToggle(
                "CSV（スペクトル、CRI、TM-30-15）",
                isOn: $options.includesCSV,
                isAvailable: availability.selectionCount > 0
            )
        }
    }

    private func exportToggle(
        _ title: String,
        isOn: Binding<Bool>,
        isAvailable: Bool
    ) -> some View {
        Toggle(title, isOn: isOn)
            .toggleStyle(.checkbox)
            .disabled(isAvailable == false || isExporting)
    }
}
