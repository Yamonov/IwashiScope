import SwiftUI

struct GamutWarningIcon: View {
    let colorSpaceName: String

    var body: some View {
        Image(systemName: "exclamationmark.triangle.fill")
            .foregroundStyle(.yellow)
            .shadow(color: .black.opacity(0.3), radius: 0.5)
            .help("\(colorSpaceName)色域外です")
            .accessibilityLabel("\(colorSpaceName)色域外")
    }
}
