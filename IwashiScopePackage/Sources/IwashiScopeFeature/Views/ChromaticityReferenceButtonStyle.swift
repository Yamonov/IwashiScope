import SwiftUI

/// Uses the native Button press lifecycle (mouse and keyboard), not a toggle or timer.
struct ChromaticityReferenceButtonStyle: ButtonStyle {
    @Binding var isPressed: Bool
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .fixedSize()
            .padding(.horizontal, 8)
            .padding(.vertical, 3)
            .foregroundStyle(configuration.isPressed ? Color.white : Color.primary)
            .background(configuration.isPressed ? Color.accentColor : Color.primary.opacity(0.07),
                        in: .rect(cornerRadius: 5))
            .overlay {
                RoundedRectangle(cornerRadius: 5)
                    .strokeBorder(Color.primary.opacity(0.12), lineWidth: 0.5)
            }
            .opacity(isEnabled ? 1 : 0.45)
            .onChange(of: configuration.isPressed, initial: true) {
                isPressed = isEnabled && configuration.isPressed
            }
            .onChange(of: isEnabled) {
                if !isEnabled { isPressed = false }
            }
            .onDisappear { isPressed = false }
    }
}
