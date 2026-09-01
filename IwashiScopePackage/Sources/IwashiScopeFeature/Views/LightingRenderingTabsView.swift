import SwiftUI

enum LightingRenderingLayout {
    static let contentHeight: CGFloat = 320
    static let tm30TopHeight: CGFloat = 168
    static let tm30BottomHeight: CGFloat = 140
    static let tm30VerticalSpacing: CGFloat = 12
    static let tm30RfRgMinimumHeight: CGFloat = 140
}

struct LightingRenderingStackView: View {
    let measurement: SpotMeasurement?

    var body: some View {
        VStack(spacing: 16) {
            ColorRenderingChartView(measurement: measurement)
            TM30ColorVectorGraphicView(measurement: measurement)
        }
        .frame(maxWidth: .infinity)
        .accessibilityElement(children: .contain)
        .accessibilityLabel("CRIとTM-30")
    }
}
