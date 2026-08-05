import SwiftUI

private enum LightingRenderingTab: Hashable {
    case cri
    case tm30
}

enum LightingRenderingLayout {
    static let contentHeight: CGFloat = 320
    static let tabHeight: CGFloat = 380
    static let tm30TopHeight: CGFloat = 168
    static let tm30BottomHeight: CGFloat = 140
    static let tm30VerticalSpacing: CGFloat = 12
    static let tm30RfRgMinimumHeight: CGFloat = 140
}

struct LightingRenderingTabsView: View {
    @State private var selectedTab: LightingRenderingTab = .cri

    let measurement: SpotMeasurement?

    var body: some View {
        TabView(selection: $selectedTab) {
            ColorRenderingChartView(measurement: measurement)
                .tabItem {
                    Text("CRI")
                }
                .tag(LightingRenderingTab.cri)

            TM30ColorVectorGraphicView(measurement: measurement)
                .tabItem {
                    Text("TM-30")
                }
                .tag(LightingRenderingTab.tm30)
        }
        .frame(height: LightingRenderingLayout.tabHeight)
    }
}
