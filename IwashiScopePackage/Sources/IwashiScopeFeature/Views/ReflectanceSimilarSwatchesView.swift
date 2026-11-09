import AppKit
import SwiftUI
import UniformTypeIdentifiers

struct ReflectanceSimilarSwatchesView: View {
    private static let fileTypes = ["aco", "acb"].compactMap { UTType(filenameExtension: $0) }
    private static let columns = [
        GridItem(.adaptive(
            minimum: SimilarSwatchCard.width,
            maximum: SimilarSwatchCard.width
        ), spacing: 12)
    ]

    @State private var library: LabSwatchLibrary?
    @State private var ranking: LabSwatchLibrary.Ranking = .standard
    @State private var matches: [LabSwatchLibrary.Match] = []
    @State private var isImporting = false
    @State private var importError: String?

    let measurement: SpotMeasurement?

    private var referenceLab: Vector3? {
        guard let measurement else { return nil }
        if let lab = measurement.lab,
           (measurement.labWhitePoint ?? "D50").caseInsensitiveCompare("D50") == .orderedSame {
            return lab
        }
        return try? ReflectanceIlluminantColorComparisonCalculator.referenceLab(for: measurement)
    }

    var body: some View {
        GroupBox {
            VStack(alignment: .leading, spacing: 10) {
                HStack(spacing: 8) {
                    Button("スウォッチファイルを読み込む", systemImage: "folder", action: chooseSwatchFile)
                        .disabled(isImporting)
                    if isImporting {
                        ProgressView()
                            .controlSize(.small)
                    }
                    Spacer(minLength: 0)
                    Picker("近似色の並び順", selection: $ranking) {
                        Text("標準").tag(LabSwatchLibrary.Ranking.standard)
                        Text("色み優先").tag(LabSwatchLibrary.Ranking.colorToneFirst)
                    }
                    .labelsHidden()
                    .pickerStyle(.segmented)
                    .fixedSize()
                }
                if let importError {
                    Text(importError)
                        .font(.caption)
                        .foregroundStyle(.red)
                }
                if library != nil {
                    if matches.isEmpty {
                        Text("比較する反射原稿の測定結果を選んでください。")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    } else {
                        LazyVGrid(columns: Self.columns, alignment: .leading, spacing: 12) {
                            ForEach(matches) { match in
                                SimilarSwatchCard(match: match)
                            }
                        }
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        } label: {
            if let library, ranking == .colorToneFirst {
                Label("\(library.name) の近似色（色み優先順）", systemImage: "swatchpalette")
            } else if let library {
                Label("\(library.name) の近似色", systemImage: "swatchpalette")
            } else {
                Label("近似色", systemImage: "swatchpalette")
            }
        }
        .accessibilityIdentifier("reflectance-similar-swatches")
        .onChange(of: referenceLab) { updateMatches() }
        .onChange(of: library) { updateMatches() }
        .onChange(of: ranking) { updateMatches() }
    }

    private func chooseSwatchFile() {
        let panel = NSOpenPanel()
        panel.title = String(localized: "スウォッチファイルを読み込む")
        panel.allowedContentTypes = Self.fileTypes
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false

        let completionHandler: (NSApplication.ModalResponse) -> Void = { response in
            guard response == .OK, let url = panel.url else { return }
            importSwatches(from: url)
        }
        if let window = NSApp.keyWindow ?? NSApp.mainWindow {
            panel.beginSheetModal(for: window, completionHandler: completionHandler)
        } else {
            panel.begin(completionHandler: completionHandler)
        }
    }

    private func importSwatches(from url: URL) {
        isImporting = true
        importError = nil
        Task {
            defer { isImporting = false }
            do {
                library = try await AdobeLabSwatchDecoder.load(from: url)
            } catch {
                importError = error.localizedDescription
            }
        }
    }

    private func updateMatches() {
        guard let library, let referenceLab else {
            matches = []
            return
        }
        matches = library.nearestSwatches(to: referenceLab, ranking: ranking)
    }
}

private struct SimilarSwatchCard: View {
    static let width = MeasurementHistoryCardMetrics.size.width * 2.5

    let match: LabSwatchLibrary.Match

    private var swatch: AdobeLabSwatch { match.swatch }

    private var color: Color {
        LabColorConverter.managedColor(lab: swatch.lab, whitePoint: "D50")
            .map { Color(cgColor: $0) } ?? Color.secondary.opacity(0.15)
    }

    var body: some View {
        VStack(spacing: 0) {
            Rectangle()
                .fill(color)
                .frame(height: MeasurementHistoryCardMetrics.colorAreaHeight)
                .overlay(alignment: .bottom) {
                    Text(swatch.name)
                        .font(.system(size: 12))
                        .foregroundStyle(.black)
                        .lineLimit(1)
                        .padding(.horizontal, 4)
                        .frame(width: Self.width - 12, height: MeasurementHistoryCardMetrics.nameFieldHeight)
                        .background(Color.white.opacity(0.5), in: .rect(cornerRadius: 6))
                        .overlay {
                            RoundedRectangle(cornerRadius: 6)
                                .strokeBorder(Color.primary.opacity(0.22))
                        }
                        .padding(.bottom, MeasurementHistoryCardMetrics.nameFieldBottomPadding)
                }
            MeasurementCardLabValues(
                lab: swatch.lab,
                deltaE: (match.deltaE2000, match.deltaE76)
            )
        }
        .frame(width: Self.width, height: MeasurementHistoryCardMetrics.size.height)
        .background(Color.secondary.opacity(0.055))
        .overlay {
            RoundedRectangle(cornerRadius: 10)
                .strokeBorder(Color.secondary.opacity(0.22))
        }
        .clipShape(.rect(cornerRadius: 10))
        .help(swatch.name)
        .accessibilityElement(children: .combine)
    }
}
