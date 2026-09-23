import AppKit
import SwiftUI

/// Read the containing window's profile, never the focused or arbitrarily first display.
struct ChromaticityDisplayProfileReader: NSViewRepresentable {
    let onChange: @MainActor (NSColorSpace?) -> Void

    func makeNSView(context: Context) -> ChromaticityProfileObservingView {
        let view = ChromaticityProfileObservingView(frame: .zero)
        view.onChange = onChange
        return view
    }

    func updateNSView(_ nsView: ChromaticityProfileObservingView, context: Context) {
        nsView.onChange = onChange
        nsView.scheduleUpdate()
    }

    static func dismantleNSView(_ nsView: ChromaticityProfileObservingView, coordinator: ()) {
        nsView.stopObserving()
    }
}

@MainActor
final class ChromaticityProfileObservingView: NSView {
    var onChange: (@MainActor (NSColorSpace?) -> Void)?
    private var lastProfile: NSColorSpace?
    private var updatePending = false
    private var stopped = false

    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        NotificationCenter.default.removeObserver(self)
        if let window, !stopped {
            for name in [NSWindow.didChangeScreenNotification, NSWindow.didChangeBackingPropertiesNotification] {
                NotificationCenter.default.addObserver(self, selector: #selector(profileDidChange(_:)), name: name, object: window)
            }
        }
        scheduleUpdate()
    }

    @objc private func profileDidChange(_ notification: Notification) { scheduleUpdate() }

    func scheduleUpdate() {
        guard !updatePending, !stopped else { return }
        updatePending = true
        // Publishing during updateNSView/layout would mutate SwiftUI state during an update.
        DispatchQueue.main.async { [weak self] in
            guard let self else { return }
            self.updatePending = false
            guard !self.stopped else { return }
            let profile = self.window?.colorSpace ?? self.window?.screen?.colorSpace
            if self.lastProfile == nil && profile == nil { return }
            if let previous = self.lastProfile, let profile, previous.isEqual(profile) { return }
            self.lastProfile = profile
            self.onChange?(profile)
        }
    }

    func stopObserving() {
        stopped = true
        NotificationCenter.default.removeObserver(self)
        onChange = nil
    }
}
