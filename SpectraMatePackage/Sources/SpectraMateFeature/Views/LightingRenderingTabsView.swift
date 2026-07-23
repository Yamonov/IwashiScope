import SwiftUI

private enum LightingRenderingTab: Hashable {
    case cri
    case tm30
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
        .frame(height: 480)
    }
}
