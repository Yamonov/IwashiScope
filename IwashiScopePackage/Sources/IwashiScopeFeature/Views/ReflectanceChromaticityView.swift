import AppKit
import SwiftUI

struct ReflectanceChromaticityView: View {
    private let calculation: Result<CIE2006Chromaticity.MeasurementResult, CIE2006ChromaticityError>
    private let markerColor: Color?
    @State private var colorMode: ConfusionColorMode
    @State private var showsBrightnessReference = false

    init(measurement: SpotMeasurement, initialColorMode: ConfusionColorMode = .none) {
        _colorMode = State(initialValue: initialColorMode)
        do {
            calculation = .success(try CIE2006Chromaticity.measuredResult(for: measurement))
        } catch let error as CIE2006ChromaticityError {
            calculation = .failure(error)
        } catch {
            calculation = .failure(.invalidSpectrum)
        }
        markerColor = measurement.lab.flatMap {
            LabColorConverter.managedColor(lab: $0, whitePoint: measurement.labWhitePoint)
        }.map { Color(cgColor: $0) }
    }

    private var result: CIE2006Chromaticity.MeasurementResult? {
        try? calculation.get()
    }

    private var canShowConfusionColors: Bool {
        result != nil
    }

    private var brightnessReference: ChromaticityBrightnessReference? {
        guard let result else { return nil }
        return .init(lms: result.lms, whiteLMS: result.whiteLMS, mode: colorMode)
    }

    var body: some View {
        GroupBox("xy色度図") {
            VStack(alignment: .leading, spacing: 8) {
                HStack(spacing: 8) {
                    Text("色覚タイプ")
                        .fixedSize()
                    Picker("色覚タイプ", selection: $colorMode) {
                        ForEach(ConfusionColorMode.allCases) { mode in
                            Text(verbatim: mode.rawValue).tag(mode)
                        }
                    }
                    .pickerStyle(.radioGroup)
                    .horizontalRadioGroupLayout()
                    .labelsHidden()
                    .disabled(!canShowConfusionColors)
                    .help("C：色の帯なし。元の候補はPでM/S、DでL/S、TでL/Mを一致させます。表示色はタイプ別の相対輝度を保ち、色域外では彩度を下げます。")
                    .accessibilityIdentifier("chromaticity-confusion-color-mode")
                    Text("の混同色を表示")
                        .fixedSize()
                }
                .font(.caption)

                HStack(spacing: 8) {
                    Text(verbatim: "CIE2015 / CIE2006 LMS · 2°")
                        .foregroundStyle(.secondary)
                    Spacer(minLength: 4)
                    Button("明るさ（参考）") { }
                        .buttonStyle(ChromaticityReferenceButtonStyle(isPressed: $showsBrightnessReference))
                        .disabled(brightnessReference == nil)
                        .help("押している間、タイプ別の相対輝度を均一なグレーで表示します。離すとカラーに戻ります。")
                        .accessibilityHint("押している間だけ参考グレーを表示")
                        .accessibilityIdentifier("chromaticity-brightness-reference-button")
                }
                .font(.caption)

                HStack {
                    Text("測定結果（D50）")
                    Spacer(minLength: 4)
                    if let point = result?.point {
                        Text(verbatim: String(format: "x_F %.4f  y_F %.4f", point.x, point.y))
                            .monospacedDigit()
                    } else {
                        Text(verbatim: "x_F —  y_F —")
                    }
                }
                .font(.caption)

                PhysiologicalChromaticityDiagram(
                    point: result?.point, markerColor: markerColor,
                    confusionBand: ChromaticityConfusionBand.make(
                        lms: result?.lms, mode: colorMode
                    ),
                    brightnessReference: brightnessReference,
                    showsBrightnessReference: showsBrightnessReference
                )

                HStack(spacing: 12) {
                    Text("混同線")
                        .foregroundStyle(.secondary)
                    ForEach(PhysiologicalCone.allCases) { cone in
                        HStack(spacing: 4) {
                            ConfusionLineSwatch(cone: cone, isGray: showsBrightnessReference)
                                .frame(width: 22, height: 8)
                                .accessibilityHidden(true)
                            Text(cone.label)
                        }
                        .accessibilityElement(children: .combine)
                    }
                }
                .font(.caption)

                switch calculation {
                case let .success(result):
                    Text("D50・\(result.wavelengthRange.start.formatted(.number.precision(.fractionLength(0...1))))–\(result.wavelengthRange.end.formatted(.number.precision(.fractionLength(0...1)))) nmの測定範囲から再計算。")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                    Text(showsBrightnessReference
                         ? String(localized: "参考グレー：帯と測定点を同じ相対輝度で表示しています。")
                         : markerColor == nil
                         ? String(localized: "測定Labがないため、測定点は輪郭のみで表示します。")
                         : String(localized: "円の色は測定Labの表示色です。座標はRGBから逆算していません。"))
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                case let .failure(error):
                    Text(error.localizedDescription)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }

                Text("タイプ別の相対輝度を一定にして表示（色域外は彩度を調整）。")
                    .font(.caption2)
                    .foregroundStyle(.secondary)
                    .help("PはM、DはL、TはL/Mの輝度成分をD50白に対して規格化した参考モデルです。主観的な明るさや、実際のモニター上の錐体応答一致を保証するものではありません。")
                if brightnessReference?.isDisplayLimited == true {
                    Text("白を超える相対輝度は、画面の表示上限に合わせています。")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                }
            }
            .padding(.top, 4)
        }
        .onChange(of: canShowConfusionColors, initial: true) {
            if !canShowConfusionColors { colorMode = .none }
        }
        .onChange(of: result) { showsBrightnessReference = false }
        .onChange(of: colorMode) { showsBrightnessReference = false }
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didResignActiveNotification)) { _ in
            showsBrightnessReference = false
        }
        .onDisappear { showsBrightnessReference = false }
        .accessibilityIdentifier("reflectance-cie2006-chromaticity-group")
    }
}

struct PhysiologicalChromaticityDiagram: View {
    let point: PhysiologicalChromaticityPoint?
    let markerColor: Color?
    var confusionBand: ChromaticityConfusionBand? = nil
    var brightnessReference: ChromaticityBrightnessReference? = nil
    var showsBrightnessReference = false
    @State private var displayProfile: NSColorSpace?
    @State private var renderingCache = RenderingCache()

    private struct RenderingKey: Equatable {
        let referenceLMS: Vector3?
        let mode: ConfusionColorMode?
        let segment: CIE2006Chromaticity.LineSegment?
        let profile: NSColorSpace?
        let brightness: ChromaticityBrightnessReference?
    }

    private struct PreparedBand {
        let segment: CIE2006Chromaticity.LineSegment
        let raster: ChromaticityBandRaster
    }

    private struct PreparedRendering {
        let key: RenderingKey
        let bands: [PreparedBand]
    }

    @MainActor
    private final class RenderingCache {
        // Memoization only, not observable UI state. A first/offscreen render is complete
        // immediately, without waiting for onAppear/onChange or a second visible frame.
        private var prepared: PreparedRendering?

        func bands(for key: RenderingKey, make: () -> [PreparedBand]) -> [PreparedBand] {
            if let prepared, prepared.key == key { return prepared.bands }
            let bands = make()
            prepared = PreparedRendering(key: key, bands: bands)
            return bands
        }
    }

    private var renderingKey: RenderingKey {
        .init(referenceLMS: confusionBand?.referenceLMS, mode: confusionBand?.colorMode,
              segment: confusionBand?.segment, profile: displayProfile, brightness: currentBrightnessReference)
    }

    private var currentBrightnessReference: ChromaticityBrightnessReference? {
        brightnessReference ?? confusionBand.flatMap(ChromaticityBrightnessReference.forBand)
    }

    var body: some View {
        let backgroundImage = ChromaticityBackgroundImage.image(for: displayProfile?.cgColorSpace)
        let backgroundOpacity = ChromaticityBackgroundImage.opacity
        let key = renderingKey
        let renderedBands = renderingCache.bands(for: key) { prepareBands() }
        let referenceGray = showsBrightnessReference ? renderedBands.first?.raster.grayColor : nil
        return Canvas { context, size in
            // The same physical scale on x_F and y_F keeps confusion-line angles intact.
            let geometry = ChromaticityDiagramGeometry(size: size)
            let scale = geometry.scale
            let plot = geometry.plot
            guard scale > 0 else { return }
            func position(_ point: PhysiologicalChromaticityPoint) -> CGPoint {
                CGPoint(x: plot.minX + point.x * scale, y: plot.maxY - point.y * scale)
            }
            var locus = Path()
            for sample in ChromaticityDisplayLocus.samples {
                if locus.isEmpty { locus.move(to: position(sample.point)) }
                else { locus.addLine(to: position(sample.point)) }
            }
            locus.closeSubpath()
            if referenceGray != nil {
                // Keep the surroundings uniform even behind the rounded band end caps.
                context.fill(Path(CGRect(origin: .zero, size: size)),
                             with: .color(Color(.sRGB, white: 0.9, opacity: 1)))
            } else {
                context.fill(locus, with: .color(.secondary.opacity(0.09)))
            }
            if referenceGray == nil, let backgroundImage {
                var background = context
                background.clip(to: locus)
                background.opacity = backgroundOpacity
                background.draw(Image(decorative: backgroundImage, scale: 1), in: plot)
            }

            var grid = Path()
            for index in 0...8 {
                let x = Double(index) / 10
                grid.move(to: position(.init(x: x, y: 0)))
                grid.addLine(to: position(.init(x: x, y: 0.9)))
            }
            for index in 0...9 {
                let y = Double(index) / 10
                grid.move(to: position(.init(x: 0, y: y)))
                grid.addLine(to: position(.init(x: 0.8, y: y)))
            }
            context.stroke(grid, with: .color(.secondary.opacity(0.2)), lineWidth: 0.5)
            // Keep the full visible outline inside the filled locus. Centered strokes
            // and sharp joins can extend beyond its narrow, folded red-end boundary.
            var outline = context
            outline.clip(to: locus)
            outline.stroke(locus, with: .color(.primary.opacity(0.6)),
                           style: StrokeStyle(lineWidth: 2.4, lineJoin: .round))
            if let point, point.isFinite {
                var lines = context
                lines.clip(to: locus)
                for cone in PhysiologicalCone.allCases {
                    if let segment = CIE2006Chromaticity.confusionLine(through: point, cone: cone) {
                        var path = Path()
                        path.move(to: position(segment.start))
                        path.addLine(to: position(segment.end))
                        var underlay = cone.diagramStroke
                        underlay.lineWidth += 2
                        lines.stroke(path, with: .color(.white.opacity(0.6)), style: underlay)
                        let lineColor = referenceGray == nil ? cone.diagramColor : .gray
                        lines.stroke(path, with: .color(lineColor.opacity(0.85)), style: cone.diagramStroke)
                    }
                }
                for rendered in renderedBands {
                    let start = position(rendered.segment.start)
                    let end = position(rendered.segment.end)
                    let length = hypot(end.x-start.x,end.y-start.y)
                    guard length > 0 else { continue }
                    let radius = ChromaticityConfusionBand.lineWidth/2
                    var bandContext = context
                    bandContext.translateBy(x:start.x,y:start.y)
                    bandContext.rotate(by:.radians(atan2(end.y-start.y,end.x-start.x)))
                    let capsule=Path(roundedRect:CGRect(x:-radius,y:-radius,width:length+2*radius,height:2*radius),
                                     cornerRadius:radius)
                    bandContext.clip(to:capsule)
                    if let referenceGray {
                        bandContext.fill(capsule, with: .color(Color(cgColor: referenceGray)))
                        continue
                    }
                    bandContext.fill(Path(CGRect(x:-radius,y:-radius,width:radius,height:2*radius)),
                                     with:.color(Color(cgColor:rendered.raster.firstColor)))
                    bandContext.fill(Path(CGRect(x:length,y:-radius,width:radius,height:2*radius)),
                                     with:.color(Color(cgColor:rendered.raster.lastColor)))
                    // Align the first/last texel centers to the segment ends. Bilinear
                    // interpolation preserves bounds; the round caps use exact endpoint colors.
                    let step=length/CGFloat(rendered.raster.image.width-1)
                    bandContext.draw(Image(decorative:rendered.raster.image,scale:1).interpolation(.low),
                                     in:CGRect(x:-step/2,y:-radius,width:length+step,height:2*radius))
                }
                if CIE2006Chromaticity.xRange.contains(point.x), CIE2006Chromaticity.yRange.contains(point.y) {
                    let location = position(point)
                    let marker = CGRect(x: location.x - 7, y: location.y - 7, width: 14, height: 14)
                    context.stroke(Path(ellipseIn: marker.insetBy(dx: -1, dy: -1)),
                                   with: .color(.black.opacity(0.55)), lineWidth: 1)
                    let fillColor = referenceGray.map { Color(cgColor: $0) } ?? markerColor
                    if let fillColor { context.fill(Path(ellipseIn: marker), with: .color(fillColor)) }
                    context.stroke(Path(ellipseIn: marker), with: .color(.white), lineWidth: 2)
                }
            }
        }
        .aspectRatio(ChromaticityDiagramGeometry.aspectRatio, contentMode: .fit)
        .overlay(alignment: .bottom) {
            if currentBrightnessReference != nil, displayProfile != nil, renderedBands.isEmpty {
                Text("この画面プロファイルでは等輝度表示を計算できません。")
                    .font(.caption2)
                    .padding(6)
                    .background(.regularMaterial, in: .rect(cornerRadius: 4))
            }
        }
        .background {
            ChromaticityDisplayProfileReader { displayProfile = $0 }
                .frame(width: 0, height: 0)
                .accessibilityHidden(true)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("CIE2006 LMSに基づく色度図")
        .accessibilityValue(accessibilityValue)
        .accessibilityIdentifier("cie2006-chromaticity-diagram")
    }

    private func prepareBands() -> [PreparedBand] {
        guard let band = confusionBand, let reference = currentBrightnessReference else { return [] }
        return band.sections.compactMap { section in
            guard let raster=ChromaticityBandRaster(section:section,reference:reference,
                                                    destinationSpace:displayProfile?.cgColorSpace) else { return nil }
            return PreparedBand(segment:section.segment,raster:raster)
        }
    }

    private var accessibilityValue: String {
        guard let point else { return String(localized: "測定点なし") }
        if showsBrightnessReference {
            return String(localized: "タイプ別の相対輝度を均一な参考グレーで表示しています。")
        }
        return String(localized: "測定点 x_F \(point.x.formatted(.number.precision(.fractionLength(4))))、y_F \(point.y.formatted(.number.precision(.fractionLength(4))))。L、M、Sの混同線を表示しています。")
    }
}

private struct ConfusionLineSwatch: View {
    let cone: PhysiologicalCone
    var isGray = false
    var body: some View {
        Canvas { context, size in
            var line = Path()
            line.move(to: CGPoint(x: 0, y: size.height / 2))
            line.addLine(to: CGPoint(x: size.width, y: size.height / 2))
            context.stroke(line, with: .color(isGray ? .gray : cone.diagramColor), style: cone.diagramStroke)
        }
    }
}

private extension PhysiologicalCone {
    var diagramColor: Color {
        switch self {
        case .long: .red
        case .medium: .green
        case .short: .blue
        }
    }

    var diagramStroke: StrokeStyle {
        switch self {
        case .long: .init(lineWidth: 1.5, lineCap: .round)
        case .medium: .init(lineWidth: 1.5, lineCap: .round, dash: [6, 4])
        case .short: .init(lineWidth: 1.5, lineCap: .round, dash: [1, 3])
        }
    }
}
