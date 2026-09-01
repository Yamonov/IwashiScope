import Charts
import SwiftUI

struct ReflectanceIlluminantSpectrumView: View {
    @State private var selection: ReflectanceIlluminantSelection = .none
    @State private var sourceKind: ReflectanceIlluminantSourceKind = .cie

    let measurement: SpotMeasurement?
    let usesPracticalSpectrumRange: Bool
    let userIlluminantStore: UserIlluminantStore

    private var result: ReflectanceIlluminantSpectrumResult? {
        ReflectanceIlluminantSpectrumCalculator.result(
            for: measurement,
            source: selectedSource,
            displayRange: displayRange
        )
    }

    private var selectedSource: IlluminantSpectrumDefinition? {
        if sourceKind == .cie {
            return selection.illuminant.map {
                IlluminantSpectrumDefinition(cie: $0)
            }
        }
        guard let slot = sourceKind.userSlot else { return nil }
        return userIlluminantStore.source(for: slot)
    }

    private var displayRange: ClosedRange<Double>? {
        guard usesPracticalSpectrumRange,
              let practicalRange = measurement?.validatedPracticalSpectrumRange else {
            return nil
        }
        return practicalRange.closedRange
    }

    var body: some View {
        GroupBox {
            VStack(alignment: .leading, spacing: 10) {
                sourceControls
                Divider()

                if let result, let measurement {
                    if let source = result.selectedSource,
                       case .user = source.origin {
                        userSourceMetadata(source)
                    }
                    legend(for: result)
                    chart(result)

                    Divider()
                    ReflectanceIlluminantColorComparisonView(
                        measurement: measurement,
                        source: result.selectedSource
                    )

                    if result.requiresUVWarning {
                        uvWarning
                    }
                } else {
                    emptyState
                }
            }
        } label: {
            Label("光源による反射光スペクトル", systemImage: "sun.max.fill")
        }
        .frame(maxWidth: .infinity)
        .accessibilityIdentifier("reflectance-illuminant-spectrum-group")
        .onChange(of: userIlluminantStore.availableSlots) {
            guard let slot = sourceKind.userSlot,
                  userIlluminantStore.hasSpectrum(for: slot) == false else {
                return
            }
            sourceKind = .cie
        }
    }

    private var sourceControls: some View {
        HStack(spacing: 10) {
            sourceRadioButton(.cie, showsTitle: false)
            illuminantPicker
            sourceRadioButton(.user1)
            sourceRadioButton(.user2)
            sourceRadioButton(.user3)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .contain)
        .accessibilityLabel("光源データ")
        .accessibilityIdentifier("illuminant-source-kind-controls")
    }

    private func sourceRadioButton(
        _ kind: ReflectanceIlluminantSourceKind,
        showsTitle: Bool = true
    ) -> some View {
        let isEnabled = isSourceKindEnabled(kind)
        let isSelected = sourceKind == kind

        return Button {
            guard isEnabled else { return }
            sourceKind = kind
        } label: {
            HStack(spacing: 5) {
                Image(
                    systemName: isSelected
                        ? "circle.inset.filled"
                        : "circle"
                )
                .foregroundStyle(
                    isSelected ? Color.accentColor : Color.secondary
                )

                if showsTitle {
                    Text(kind.title)
                        .foregroundStyle(
                            isEnabled ? Color.primary : Color.secondary
                        )
                }
            }
            .contentShape(.rect)
        }
            .buttonStyle(.plain)
            .disabled(isEnabled == false)
            .opacity(isEnabled ? 1 : 0.38)
            .fixedSize()
            .accessibilityLabel(kind.title)
            .accessibilityValue(isSelected ? "選択中" : "未選択")
            .accessibilityAddTraits(isSelected ? .isSelected : [])
            .accessibilityIdentifier("illuminant-source-\(kind.rawValue)")
    }

    private var illuminantPicker: some View {
        Picker("CIE光源", selection: $selection) {
            Text("CIE参考光源")
                .tag(ReflectanceIlluminantSelection.none)

            ForEach(CIEIlluminantCategory.allCases) { category in
                Section(category.title) {
                    ForEach(illuminants(in: category)) { illuminant in
                        Text(illuminant.displayName)
                            .tag(ReflectanceIlluminantSelection.cie(illuminant))
                    }
                }
            }
        }
        .pickerStyle(.menu)
        .labelsHidden()
        .disabled(measurement == nil || sourceKind != .cie)
        .frame(width: 210, alignment: .leading)
        .help("反射光の計算に使用するCIE参考光源を選択します")
        .accessibilityIdentifier("cie-reference-illuminant-picker")
    }

    private func isSourceKindEnabled(
        _ kind: ReflectanceIlluminantSourceKind
    ) -> Bool {
        kind.isAvailable(userSlots: userIlluminantStore.availableSlots)
    }

    private func userSourceMetadata(
        _ source: IlluminantSpectrumDefinition
    ) -> some View {
        HStack(spacing: 12) {
            if let name = source.userName {
                Text(name)
                    .font(.callout.weight(.semibold))
            }
            if let measuredAt = source.measuredAt {
                Label {
                    Text(
                        measuredAt.formatted(
                            date: .numeric,
                            time: .standard
                        )
                    )
                } icon: {
                    Image(systemName: "calendar")
                }
                .font(.caption)
                .foregroundStyle(.secondary)
            }
        }
        .accessibilityElement(children: .combine)
        .accessibilityLabel(userSourceAccessibilityLabel(source))
    }

    private func userSourceAccessibilityLabel(
        _ source: IlluminantSpectrumDefinition
    ) -> String {
        var components = ["User光源情報"]
        if let name = source.userName {
            components.append(name)
        }
        if let measuredAt = source.measuredAt {
            components.append(
                "計測日時 " + measuredAt.formatted(
                    date: .numeric,
                    time: .standard
                )
            )
        }
        return components.joined(separator: "、")
    }

    private func illuminants(
        in category: CIEIlluminantCategory
    ) -> [CIEReferenceIlluminant] {
        CIEReferenceIlluminant.allCases.filter { $0.category == category }
    }

    private func legend(
        for result: ReflectanceIlluminantSpectrumResult
    ) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 16) {
                ReflectanceSpectrumLineLegend(
                    title: "計測反射率",
                    color: .black
                )

                if result.illuminant.isEmpty == false {
                    ReflectanceSpectrumLineLegend(
                        title: "選択光源",
                        color: .yellow,
                        isDashed: true
                    )
                }

                if result.reflectedLight.isEmpty == false {
                    ReflectanceSpectrumGradientLegend(title: "反射光")
                }
            }

            Text(
                result.illuminant.isEmpty
                    ? "縦軸：分光反射率（%）"
                    : "黒：分光反射率（%）／黄：ピーク100の光源SPD／色付き面：光源SPD × 分光反射率"
            )
            .font(.caption)
            .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func chart(
        _ result: ReflectanceIlluminantSpectrumResult
    ) -> some View {
        let spectrumGradient = SpectrumChartStyle.gradient(
            for: result.wavelengthRange
        )
        let yAxisScale = SpectrumYAxisScale.resolve(
            automaticUpperBound: result.automaticUpperBound,
            configuration: SpectrumYAxisConfiguration(
                mode: .automatic,
                fixedUpperBound: 100
            )
        )

        return Chart {
            ForEach(result.reflectedLight) { sample in
                AreaMark(
                    x: .value("波長（nm）", sample.wavelength),
                    yStart: .value("基準", 0.0),
                    yEnd: .value("反射光相対値", sample.value)
                )
                .interpolationMethod(.linear)
                .foregroundStyle(spectrumGradient)
                .alignsMarkStylesWithPlotArea()
            }

            ForEach(result.measuredReflectance) { sample in
                LineMark(
                    x: .value("波長（nm）", sample.wavelength),
                    y: .value("計測反射率（%）", sample.value),
                    series: .value("系列", "計測反射率")
                )
                .interpolationMethod(.linear)
                .lineStyle(
                    .init(
                        lineWidth: 2.2,
                        lineCap: .round,
                        lineJoin: .round
                    )
                )
                .foregroundStyle(Color.black)
            }

            ForEach(result.illuminant) { sample in
                LineMark(
                    x: .value("波長（nm）", sample.wavelength),
                    y: .value("光源相対値", sample.value),
                    series: .value("系列", "選択光源")
                )
                .interpolationMethod(.linear)
                .lineStyle(
                    .init(
                        lineWidth: 2.4,
                        lineCap: .round,
                        lineJoin: .round,
                        dash: [7, 4]
                    )
                )
                .foregroundStyle(Color.yellow)
            }
        }
        .chartXScale(domain: result.wavelengthRange)
        .chartYScale(domain: 0...yAxisScale.upperBound)
        .chartXAxis {
            AxisMarks(
                values: SpectrumChartScale.axisValues(
                    for: result.wavelengthRange
                )
            ) {
                AxisGridLine()
                AxisTick()
                AxisValueLabel()
            }
        }
        .chartYAxis {
            AxisMarks(
                position: .leading,
                values: yAxisScale.tickValues
            ) { value in
                AxisGridLine()
                AxisTick()
                if let tickValue = value.as(Double.self) {
                    AxisValueLabel {
                        Text(
                            tickValue.formatted(
                                .number.precision(.fractionLength(0))
                            )
                        )
                    }
                }
            }
        }
        .chartXAxisLabel(
            "(nm)",
            position: .bottom,
            alignment: .trailing,
            spacing: 0
        )
        .chartLegend(.hidden)
        .chartPlotStyle { plotArea in
            plotArea
                .background(spectrumGradient.opacity(0.17))
                .compositingGroup()
                .clipShape(.rect(cornerRadius: 6))
        }
        .frame(height: 400)
        .accessibilityLabel("光源による反射光スペクトルグラフ")
        .accessibilityHint(
            result.illuminant.isEmpty
                ? "波長ごとの計測反射率を表示します"
                : "計測反射率、選択光源、光源を適用した反射光の相対分光分布を表示します"
        )
    }

    private var uvWarning: some View {
        Label {
            Text(
                "UVデータを含まない反射測定からの予測です。蛍光増白紙・蛍光インキでは実際の反射光と一致しない場合があります。"
            )
        } icon: {
            Image(systemName: "exclamationmark.triangle.fill")
        }
        .font(.caption)
        .foregroundStyle(.orange)
        .accessibilityIdentifier("reflectance-illuminant-uv-warning")
    }

    private var emptyState: some View {
        ContentUnavailableView(
            "反射測定値がありません",
            systemImage: "waveform.path.ecg",
            description: Text("反射原稿を測定すると、選択した光源下の反射光スペクトルを表示します。")
        )
        .frame(maxWidth: .infinity, minHeight: 260)
    }
}

private struct ReflectanceSpectrumLineLegend: View {
    let title: String
    let color: Color
    var isDashed = false

    var body: some View {
        HStack(spacing: 6) {
            Capsule()
                .fill(color)
                .frame(width: 28, height: isDashed ? 2 : 2.5)
                .mask {
                    if isDashed {
                        HStack(spacing: 3) {
                            ForEach(0..<4, id: \.self) { _ in
                                Rectangle()
                            }
                        }
                    } else {
                        Rectangle()
                    }
                }

            Text(title)
        }
        .font(.caption)
        .accessibilityElement(children: .combine)
    }
}

private struct ReflectanceSpectrumGradientLegend: View {
    let title: String

    var body: some View {
        HStack(spacing: 6) {
            RoundedRectangle(cornerRadius: 2)
                .fill(SpectrumChartStyle.gradient)
                .frame(width: 28, height: 8)

            Text(title)
        }
        .font(.caption)
        .accessibilityElement(children: .combine)
    }
}
