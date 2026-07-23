import AppKit
import SwiftUI

struct MeasurementHistoryNameEditor: NSViewRepresentable {
    @Binding var text: String

    let displayText: String
    let isEditing: Bool
    let focusRequest: UUID?
    let onSelect: () -> Void
    let onBeginEditing: () -> Void
    let onCommit: (String) -> Void
    let onNavigate: (String, MeasurementHistoryNameNavigationDirection) -> Void
    let onCancel: () -> Void

    func makeNSView(context: Context) -> MeasurementHistoryNameTextField {
        let textField = MeasurementHistoryNameTextField(frame: .zero)
        textField.cell = MeasurementHistoryNameTextFieldCell()
        textField.delegate = context.coordinator
        textField.stringValue = isEditing ? text : displayText
        textField.placeholderString = nil
        textField.font = .systemFont(ofSize: 12)
        textField.controlSize = .small
        textField.usesSingleLineMode = true
        textField.lineBreakMode = .byTruncatingTail
        textField.isAutomaticTextCompletionEnabled = false
        textField.isBezeled = false
        textField.isBordered = false
        textField.drawsBackground = false
        textField.backgroundColor = .clear
        textField.textColor = .black
        textField.focusRingType = .none
        textField.setAccessibilityLabel("カード名")
        textField.onSelect = { [weak coordinator = context.coordinator] in
            coordinator?.selectCard()
        }
        textField.onBeginEditing = {
            [weak coordinator = context.coordinator, weak textField] in
            guard let textField else { return }
            coordinator?.beginNameEditing(in: textField)
        }
        textField.setEditingState(isEditing)

        if isEditing, let focusRequest {
            context.coordinator.beginEditingCycle(
                focusRequest: focusRequest,
                textField: textField
            )
            textField.beginEditing()
        }
        return textField
    }

    func updateNSView(
        _ nsView: MeasurementHistoryNameTextField,
        context: Context
    ) {
        context.coordinator.parent = self
        if isEditing {
            nsView.setEditingState(true)
        } else {
            if nsView.currentEditor() != nil || nsView.acceptsFirstResponder {
                context.coordinator.finishEditingCycleWithoutAction()
                nsView.window?.endEditing(for: nsView)
            }
            nsView.setEditingState(false)
            if nsView.stringValue != displayText {
                nsView.stringValue = displayText
            }
        }

        guard isEditing,
              let focusRequest,
              context.coordinator.lastFocusRequest != focusRequest else {
            return
        }
        // Seed the AppKit editing buffer once. While the field editor is
        // active it remains the source of truth, so successive keystrokes do
        // not trigger a SwiftUI update that can replace the field editor.
        if nsView.stringValue != text {
            nsView.stringValue = text
        }
        context.coordinator.beginEditingCycle(
            focusRequest: focusRequest,
            textField: nsView
        )
        nsView.beginEditing()
    }

    static func dismantleNSView(
        _ nsView: MeasurementHistoryNameTextField,
        coordinator: Coordinator
    ) {
        coordinator.dispose()
        nsView.delegate = nil
        nsView.onSelect = nil
        nsView.onBeginEditing = nil
    }

    func makeCoordinator() -> Coordinator {
        Coordinator(parent: self)
    }

    func sizeThatFits(
        _ proposal: ProposedViewSize,
        nsView _: MeasurementHistoryNameTextField,
        context _: Context
    ) -> CGSize? {
        CGSize(width: proposal.width ?? 140, height: 24)
    }

    @MainActor
    final class Coordinator: NSObject, NSTextFieldDelegate {
        var parent: MeasurementHistoryNameEditor
        private(set) var lastFocusRequest: UUID?
        private var didFinishEditing = false
        private var outsideClickMonitor: Any?

        init(parent: MeasurementHistoryNameEditor) {
            self.parent = parent
        }

        func beginEditingCycle(
            focusRequest: UUID,
            textField: MeasurementHistoryNameTextField
        ) {
            lastFocusRequest = focusRequest
            didFinishEditing = false
            installOutsideClickMonitorIfNeeded(for: textField)
        }

        func dispose() {
            didFinishEditing = true
            removeOutsideClickMonitor()
        }

        func finishEditingCycleWithoutAction() {
            didFinishEditing = true
            removeOutsideClickMonitor()
        }

        func selectCard() {
            parent.onSelect()
        }

        func beginNameEditing(in textField: MeasurementHistoryNameTextField) {
            didFinishEditing = false
            installOutsideClickMonitorIfNeeded(for: textField)
            parent.onBeginEditing()
        }

        func controlTextDidChange(_ notification: Notification) {
            // Keep uncommitted input in AppKit's field editor. Round-tripping
            // every character through the SwiftUI binding rebuilds the card
            // during editing and can leave only the latest character visible.
            (notification.object as? MeasurementHistoryNameTextField)?
                .cancelPendingFocusRequest()
        }

        func controlTextDidEndEditing(_ notification: Notification) {
            // Do not request focus again here. Re-entering the field editor
            // selects its contents after each end-editing notification, which
            // makes the next character replace everything already typed.
            // The initial delayed focus request already covers double-clicks
            // and context-menu actions; outside clicks commit explicitly.
        }

        func control(
            _ control: NSControl,
            textView: NSTextView,
            doCommandBy commandSelector: Selector
        ) -> Bool {
            guard let textField = control as? NSTextField else { return false }

            if let direction = MeasurementHistoryNameEditorKeyHandling.navigationDirection(
                for: commandSelector,
                hasMarkedText: textView.hasMarkedText()
            ) {
                navigate(textField.stringValue, direction: direction)
                return true
            }

            switch commandSelector {
            case #selector(NSResponder.insertNewline(_:)),
                 #selector(NSResponder.insertNewlineIgnoringFieldEditor(_:)),
                 #selector(NSResponder.insertLineBreak(_:)):
                guard MeasurementHistoryNameEditorKeyHandling.shouldCommitReturn(
                    hasMarkedText: textView.hasMarkedText()
                ) else {
                    return false
                }
                commit(textField.stringValue)
                return true
            case #selector(NSResponder.cancelOperation(_:)):
                guard didFinishEditing == false else { return true }
                didFinishEditing = true
                removeOutsideClickMonitor()
                parent.onCancel()
                return true
            default:
                return false
            }
        }

        private func commit(_ text: String) {
            guard didFinishEditing == false else { return }
            didFinishEditing = true
            removeOutsideClickMonitor()
            parent.text = text
            parent.onCommit(text)
        }

        private func navigate(
            _ text: String,
            direction: MeasurementHistoryNameNavigationDirection
        ) {
            guard didFinishEditing == false else { return }
            didFinishEditing = true
            removeOutsideClickMonitor()
            parent.text = text
            parent.onNavigate(text, direction)
        }

        private func installOutsideClickMonitorIfNeeded(
            for textField: MeasurementHistoryNameTextField
        ) {
            guard outsideClickMonitor == nil else { return }

            outsideClickMonitor = NSEvent.addLocalMonitorForEvents(
                matching: [.leftMouseDown, .rightMouseDown, .otherMouseDown]
            ) { [weak self, weak textField] event in
                guard let self, let textField, self.didFinishEditing == false else {
                    return event
                }

                let isInsideTextField: Bool
                if event.window === textField.window {
                    let localPoint = textField.convert(event.locationInWindow, from: nil)
                    isInsideTextField = textField.bounds.contains(localPoint)
                } else {
                    isInsideTextField = false
                }

                guard isInsideTextField == false else { return event }
                self.commit(textField.stringValue)
                textField.window?.endEditing(for: textField)
                return event
            }
        }

        private func removeOutsideClickMonitor() {
            guard let outsideClickMonitor else { return }
            NSEvent.removeMonitor(outsideClickMonitor)
            self.outsideClickMonitor = nil
        }
    }
}

enum MeasurementHistoryNameEditorKeyHandling {
    static func shouldCommitReturn(hasMarkedText: Bool) -> Bool {
        hasMarkedText == false
    }

    static func navigationDirection(
        for commandSelector: Selector,
        hasMarkedText: Bool
    ) -> MeasurementHistoryNameNavigationDirection? {
        guard hasMarkedText == false else { return nil }

        switch commandSelector {
        case #selector(NSResponder.insertTab(_:)):
            return .next
        case #selector(NSResponder.insertBacktab(_:)):
            return .previous
        default:
            return nil
        }
    }
}

enum MeasurementHistoryNameNavigationDirection: Equatable, Sendable {
    case next
    case previous
}

enum MeasurementHistoryNameNavigation {
    static func destinationID(
        from currentID: MeasurementHistoryEntry.ID,
        orderedIDs: [MeasurementHistoryEntry.ID],
        direction: MeasurementHistoryNameNavigationDirection
    ) -> MeasurementHistoryEntry.ID? {
        guard orderedIDs.isEmpty == false,
              let currentIndex = orderedIDs.firstIndex(of: currentID) else {
            return nil
        }

        let offset = direction == .next ? 1 : -1
        let destinationIndex = (currentIndex + offset + orderedIDs.count) % orderedIDs.count
        return orderedIDs[destinationIndex]
    }
}

@MainActor
final class MeasurementHistoryNameTextField: NSTextField {
    private static let maximumFocusAttemptCount = 6
    private static let initialFocusDelay = 0.06
    private static let focusRetryDelay = 0.05

    private var allowsEditingFocus = false
    private var focusRequestID: UUID?
    private var focusAttemptCount = 0

    var hasPendingFocusRequest: Bool {
        focusRequestID != nil
    }

    var onSelect: (() -> Void)?
    var onBeginEditing: (() -> Void)?

    override var acceptsFirstResponder: Bool {
        allowsEditingFocus
    }

    override func becomeFirstResponder() -> Bool {
        guard allowsEditingFocus else { return false }
        return super.becomeFirstResponder()
    }

    func setEditingState(_ isEditing: Bool) {
        // Keep the control mouse-active while it is displaying a name. Actual
        // text focus is still gated by acceptsFirstResponder.
        isEditable = true
        isSelectable = true
        allowsEditingFocus = isEditing
        if isEditing == false {
            focusRequestID = nil
            focusAttemptCount = 0
        }
    }

    func beginEditing() {
        setEditingState(true)
        guard currentEditor() == nil, focusRequestID == nil else { return }
        let requestID = UUID()
        focusRequestID = requestID
        focusAttemptCount = 0
        scheduleFocusAttempt(requestID: requestID)
    }

    func cancelPendingFocusRequest() {
        focusRequestID = nil
        focusAttemptCount = 0
    }

    override func mouseDown(with event: NSEvent) {
        guard currentEditor() == nil else {
            super.mouseDown(with: event)
            return
        }

        if allowsEditingFocus {
            beginEditing()
            return
        }

        if event.clickCount >= 2 {
            onBeginEditing?()
            beginEditing()
        } else {
            onSelect?()
        }
    }

    private func scheduleFocusAttempt(requestID: UUID) {
        let delay = focusAttemptCount == 0
            ? Self.initialFocusDelay
            : Self.focusRetryDelay
        DispatchQueue.main.asyncAfter(deadline: .now() + delay) { [weak self] in
            self?.performInDefaultRunLoopMode(requestID: requestID)
        }
    }

    private func performInDefaultRunLoopMode(requestID: UUID) {
        // A context-menu action runs while AppKit is tracking the menu. Waiting
        // for the default mode prevents the closing menu from immediately
        // taking focus back from the name field.
        RunLoop.main.perform(inModes: [.default]) { [weak self] in
            // RunLoop.main executes this block on the main thread. Enter the
            // main actor synchronously so makeFirstResponder does not inherit
            // the priority of a newly created Swift task.
            MainActor.assumeIsolated {
                self?.applyFocusRequest(requestID: requestID)
            }
        }
    }

    private func applyFocusRequest(requestID: UUID) {
        guard focusRequestID == requestID, allowsEditingFocus else { return }

        if currentEditor() != nil {
            cancelPendingFocusRequest()
            placeInsertionPointAtEnd()
            return
        }

        focusAttemptCount += 1
        if let window, window.makeFirstResponder(self) {
            cancelPendingFocusRequest()
            if currentEditor() == nil {
                selectText(nil)
            }
            placeInsertionPointAtEnd()
            return
        }

        guard focusAttemptCount < Self.maximumFocusAttemptCount else {
            focusRequestID = nil
            focusAttemptCount = 0
            return
        }
        scheduleFocusAttempt(requestID: requestID)
    }

    private func placeInsertionPointAtEnd() {
        guard let fieldEditor = currentEditor() as? NSTextView else { return }
        fieldEditor.setSelectedRange(
            NSRange(location: (fieldEditor.string as NSString).length, length: 0)
        )
    }
}

@MainActor
final class MeasurementHistoryNameTextFieldCell: NSTextFieldCell {
    override func drawingRect(forBounds rect: NSRect) -> NSRect {
        verticallyCenteredTextRect(forBounds: rect)
    }

    override func edit(
        withFrame rect: NSRect,
        in controlView: NSView,
        editor textObject: NSText,
        delegate: Any?,
        event: NSEvent?
    ) {
        super.edit(
            withFrame: verticallyCenteredTextRect(forBounds: rect),
            in: controlView,
            editor: textObject,
            delegate: delegate,
            event: event
        )
    }

    override func select(
        withFrame rect: NSRect,
        in controlView: NSView,
        editor textObject: NSText,
        delegate: Any?,
        start selectionStart: Int,
        length selectionLength: Int
    ) {
        super.select(
            withFrame: verticallyCenteredTextRect(forBounds: rect),
            in: controlView,
            editor: textObject,
            delegate: delegate,
            start: selectionStart,
            length: selectionLength
        )
    }

    func verticallyCenteredTextRect(forBounds bounds: NSRect) -> NSRect {
        var textRect = super.drawingRect(forBounds: bounds)
        guard let font else { return textRect }

        let naturalHeight = ceil(font.ascender - font.descender + font.leading)
        let centeredHeight = min(naturalHeight, textRect.height)
        textRect.origin.y += (textRect.height - centeredHeight) / 2
        textRect.size.height = centeredHeight
        return textRect
    }
}
