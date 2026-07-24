import Foundation
import Observation

enum MeasurementSessionPhase: Equatable {
    case idle
    case launching
    case calibrationRecommended
    case awaitingCalibrationSetup
    case waitingForInstrument
    case calibrating
    case ready
    case measuring
    case retryAvailable
    case configurationRequired
    case recovering
    case workspace
    case stopped
    case failed

    var acceptsManualCalibration: Bool {
        self == .calibrationRecommended || self == .ready
    }

    var responseTimeout: Duration? {
        switch self {
        case .launching:
            .seconds(30)
        case .calibrating:
            .seconds(90)
        case .measuring:
            .seconds(60)
        case .recovering:
            .seconds(15)
        default:
            nil
        }
    }

    var blocksInstrumentInteraction: Bool {
        responseTimeout != nil
    }
}

enum SpotreadCommand: String {
    case trigger = " "
    case calibrate = "k"
    case skipCalibration = "S"
    case ignoreSavedReading = "N"
}

private enum SpotreadProcessEvent: Sendable {
    case protocolOutput(String)
    case logOutput(String)
    case termination(Int32)
}

@MainActor
@Observable
final class MeasurementSession {
    private static let interactionCharacterLimit = 2_000_000
    private static let retainedInteractionCharacterTarget = 1_600_000

    private(set) var mode: MeasurementMode?
    private(set) var phase: MeasurementSessionPhase = .idle
    private(set) var calibrationPrompt: CalibrationPrompt?
    private(set) var calibrationCompleted = false
    private(set) var instrumentIdentity: SpotreadInstrumentIdentity?
    private(set) var latestMeasurement: SpotMeasurement?
    private(set) var measurementCount = 0
    private(set) var executablePath: String?
    @ObservationIgnored private(set) var transcript = ""
    @ObservationIgnored private(set) var interactions: [SpotreadInteraction] = []
    private(set) var interactionRevision = 0
    private(set) var errorMessage: String?
    private(set) var activeIssue: SpotreadIssue?
    private(set) var notice: SpotreadNotice?

    @ObservationIgnored private var parser = SpotreadOutputParser(mode: .reflectance)
    @ObservationIgnored private var runner: SpotreadProcess?
    @ObservationIgnored private var calibrationWasRequested = false
    @ObservationIgnored private var stopWasRequested = false
    @ObservationIgnored private var generation = UUID()
    @ObservationIgnored private var interactionCharacterCount = 0
    @ObservationIgnored private var responseWatchdog: Task<Void, Never>?
    @ObservationIgnored private var relaunchTask: Task<Void, Never>?
    @ObservationIgnored private var processEventTask: Task<Void, Never>?
    @ObservationIgnored private var automaticRecoveryAttemptCount = 0
    @ObservationIgnored private let executableURLOverride: URL?
    @ObservationIgnored private let historyStore: MeasurementHistoryStore

    init(
        historyStore: MeasurementHistoryStore = MeasurementHistoryStore(),
        executableURLOverride: URL? = nil
    ) {
        self.historyStore = historyStore
        self.executableURLOverride = executableURLOverride
    }

    var isRunning: Bool {
        switch phase {
        case .idle, .workspace, .stopped, .failed:
            false
        default:
            true
        }
    }

    func start(mode: MeasurementMode) {
        launch(
            mode: mode,
            resetsMeasurements: true,
            resetsInteractionLog: true,
            resetsRecoveryBudget: true
        )
    }

    func presentWorkspace(
        mode: MeasurementMode,
        measurement: SpotMeasurement?,
        instrumentIdentity: SpotreadInstrumentIdentity?,
        measurementCount: Int
    ) {
        relaunchTask?.cancel()
        relaunchTask = nil
        stopRunner(markStopped: false)

        self.mode = mode
        self.instrumentIdentity = instrumentIdentity
        latestMeasurement = measurement
        self.measurementCount = measurementCount
        calibrationPrompt = nil
        calibrationCompleted = false
        executablePath = nil
        errorMessage = nil
        activeIssue = nil
        notice = nil
        calibrationWasRequested = false
        stopWasRequested = false
        parser = SpotreadOutputParser(mode: mode)
        generation = UUID()
        transcript = ""
        interactions.removeAll(keepingCapacity: true)
        interactionCharacterCount = 0
        interactionRevision &+= 1
        automaticRecoveryAttemptCount = 0
        transition(to: .workspace)
    }

    private func launch(
        mode: MeasurementMode,
        resetsMeasurements: Bool,
        resetsInteractionLog: Bool,
        resetsRecoveryBudget: Bool
    ) {
        relaunchTask?.cancel()
        relaunchTask = nil
        stopRunner(markStopped: false)

        self.mode = mode
        calibrationPrompt = nil
        calibrationCompleted = false
        instrumentIdentity = nil
        executablePath = nil
        errorMessage = nil
        activeIssue = nil
        notice = nil
        calibrationWasRequested = false
        stopWasRequested = false
        parser = SpotreadOutputParser(mode: mode)
        let generation = UUID()
        self.generation = generation
        if resetsMeasurements {
            latestMeasurement = nil
            measurementCount = 0
        }
        if resetsInteractionLog {
            transcript = ""
            interactions.removeAll(keepingCapacity: true)
            interactionCharacterCount = 0
            interactionRevision &+= 1
        }
        if resetsRecoveryBudget {
            automaticRecoveryAttemptCount = 0
        }
        transition(to: .launching)

        guard let executableURL = executableURLOverride ?? SpotreadExecutableLocator.locate() else {
            fail(
                String(
                    localized: "spotreadが見つかりません。App BundleのResourcesへ同梱するか、IWASHISCOPE_SPOTREAD_PATHで実行ファイルを指定してください。"
                )
            )
            return
        }

        executablePath = executableURL.path
        let runner = SpotreadProcess()
        self.runner = runner
        let (processEvents, processEventContinuation) = AsyncStream.makeStream(
            of: SpotreadProcessEvent.self
        )
        processEventTask = Task { @MainActor [weak self] in
            for await event in processEvents {
                guard let self else { return }
                switch event {
                case let .protocolOutput(output):
                    self.consumeProtocolOutput(output, generation: generation)
                case let .logOutput(output):
                    self.consumeLogOutput(output, generation: generation)
                case let .termination(status):
                    self.processTerminated(status: status, generation: generation)
                }
            }
        }

        appendInteraction(
            direction: .lifecycle,
            content: String(localized: "起動要求\n実行ファイル: \(executableURL.path)\n引数: \(mode.spotreadArguments.joined(separator: " "))"),
            sessionID: generation
        )

        do {
            try runner.start(
                executableURL: executableURL,
                arguments: mode.spotreadArguments,
                onProtocolOutput: { output in
                    processEventContinuation.yield(.protocolOutput(output))
                },
                onLogOutput: { output in
                    processEventContinuation.yield(.logOutput(output))
                },
                onTermination: { status in
                    processEventContinuation.yield(.termination(status))
                    processEventContinuation.finish()
                }
            )
            appendInteraction(
                direction: .lifecycle,
                content: String(localized: "spotreadを起動しました"),
                sessionID: generation
            )
        } catch {
            processEventContinuation.finish()
            processEventTask?.cancel()
            processEventTask = nil
            self.runner = nil
            let issue = SpotreadIssue.fatal(
                rawText: String(localized: "spotreadを起動できませんでした。 \(error.localizedDescription)")
            )
            activeIssue = issue
            fail(String(localized: "spotreadを起動できませんでした。\n\(error.localizedDescription)"))
        }
    }

    func beginCalibration() {
        guard phase.acceptsManualCalibration else { return }
        calibrationWasRequested = true
        calibrationCompleted = false
        calibrationPrompt = nil
        activeIssue = nil
        errorMessage = nil
        transition(to: .calibrating)
        send(.calibrate)
    }

    func continueCalibration() {
        guard phase == .awaitingCalibrationSetup else { return }
        calibrationWasRequested = true
        calibrationCompleted = false
        activeIssue = nil
        errorMessage = nil
        transition(to: .calibrating)
        send(.trigger)
    }

    func skipCalibration() {
        guard phase == .awaitingCalibrationSetup,
              calibrationPrompt?.allowsSkip == true else { return }
        calibrationWasRequested = true
        calibrationCompleted = false
        activeIssue = nil
        errorMessage = nil
        transition(to: .calibrating)
        send(.skipCalibration)
    }

    func takeReading() {
        guard phase == .ready else { return }
        activeIssue = nil
        errorMessage = nil
        transition(to: .measuring)
        send(.trigger)
    }

    func recoverFromIssue() {
        guard let issue = activeIssue else { return }

        switch issue.recoveryAction {
        case .resumeMeasurementLoop:
            guard phase == .retryAvailable else { return }
            activeIssue = nil
            errorMessage = nil
            transition(to: .recovering)
            send(.trigger)

        case .retryCalibration:
            guard phase == .retryAvailable else { return }
            calibrationWasRequested = true
            calibrationCompleted = false
            activeIssue = nil
            errorMessage = nil
            transition(to: .calibrating)
            send(.trigger)

        case .retryOperation:
            guard phase == .retryAvailable else { return }
            activeIssue = nil
            errorMessage = nil
            transition(to: calibrationWasRequested && !calibrationCompleted ? .calibrating : .recovering)
            send(.trigger)

        case .acknowledgeConfiguration:
            guard phase == .configurationRequired else { return }
            activeIssue = nil
            errorMessage = nil
            transition(to: calibrationCompleted ? .ready : .calibrationRecommended)

        case .restart:
            forceRestart()
        }
    }

    func restart() {
        guard let mode else { return }
        launch(
            mode: mode,
            resetsMeasurements: false,
            resetsInteractionLog: false,
            resetsRecoveryBudget: true
        )
    }

    func forceRestart() {
        guard let mode else { return }
        appendInteraction(
            direction: .lifecycle,
            content: String(localized: "UIからspotreadの強制再起動を要求しました"),
            sessionID: generation
        )
        scheduleForcedRelaunch(
            mode: mode,
            resetsMeasurements: false,
            resetsInteractionLog: false,
            resetsRecoveryBudget: true,
            noticeMessage: String(localized: "spotreadを強制終了して再起動しました。")
        )
    }

    func stop() {
        relaunchTask?.cancel()
        relaunchTask = nil
        stopRunner(markStopped: true)
    }

    func stopForApplicationTermination() {
        relaunchTask?.cancel()
        relaunchTask = nil
        responseWatchdog?.cancel()
        responseWatchdog = nil
        processEventTask?.cancel()
        processEventTask = nil
        runner?.stop()
        runner = nil
    }

    func clearInteractionLog() {
        interactions.removeAll(keepingCapacity: true)
        interactionCharacterCount = 0
        interactionRevision &+= 1
    }

    private func consumeLogOutput(_ output: String, generation: UUID) {
        appendInteraction(direction: .output, content: output, sessionID: generation)
        guard generation == self.generation else { return }
        refreshResponseWatchdog()
        transcript.append(output)
        if transcript.utf8.count > 250_000 {
            transcript = String(transcript.suffix(180_000))
        }

    }

    private func consumeProtocolOutput(_ output: String, generation: UUID) {
        guard generation == self.generation else { return }
        refreshResponseWatchdog()

        for event in parser.consume(output) {
            guard generation == self.generation else { break }
            handle(event)
        }
    }

    private func handle(_ event: SpotreadEvent) {
        switch event {
        case let .instrumentIdentity(identity):
            instrumentIdentity = identity

        case .calibrationStarted:
            calibrationWasRequested = true
            calibrationCompleted = false
            calibrationPrompt = nil
            activeIssue = nil
            errorMessage = nil
            transition(to: .calibrating)

        case let .calibrationPrompt(prompt):
            calibrationWasRequested = true
            calibrationCompleted = false
            calibrationPrompt = prompt
            activeIssue = nil
            errorMessage = nil
            transition(to: prompt.requiresUserConfirmation ? .awaitingCalibrationSetup : .waitingForInstrument)

        case .calibrationComplete:
            calibrationCompleted = true
            calibrationPrompt = nil
            activeIssue = nil
            errorMessage = nil
            automaticRecoveryAttemptCount = 0
            transition(to: .ready)

        case .savedReadingPrompt:
            // IwashiScope only accepts live readings. Using the source-defined
            // N command avoids importing stale readings stored by the device.
            activeIssue = nil
            errorMessage = nil
            transition(to: .recovering)
            send(.ignoreSavedReading)

        case .measurementStarted:
            switch phase {
            case .calibrationRecommended, .ready, .measuring, .retryAvailable,
                 .configurationRequired, .recovering:
                activeIssue = nil
                errorMessage = nil
                transition(to: .measuring)
            default:
                break
            }

        case let .measurement(measurement):
            latestMeasurement = measurement
            measurementCount += 1
            historyStore.append(
                measurement,
                instrumentIdentity: instrumentIdentity
            )

        case .measurementPrompt:
            if calibrationWasRequested, !calibrationCompleted {
                // Some instruments finish immediately and don't print
                // "Calibration complete" because no user setup was needed.
                calibrationCompleted = true
            }

            if phase == .configurationRequired {
                // A wrong sensor position is reported without a keyboard retry.
                // spotread is already back at its measurement prompt, but the UI
                // must retain the setup guidance until the user confirms it.
                return
            }

            activeIssue = nil
            errorMessage = nil
            automaticRecoveryAttemptCount = 0
            if calibrationCompleted {
                calibrationPrompt = nil
                transition(to: .ready)
            } else {
                transition(to: .calibrationRecommended)
            }

        case let .recoverableIssue(issue):
            activeIssue = issue
            errorMessage = nil
            if issue.kind == .calibrationFailure {
                calibrationWasRequested = true
                calibrationCompleted = false
            }
            transition(to: .retryAvailable)

        case let .configurationIssue(issue):
            activeIssue = issue
            errorMessage = nil
            transition(to: .configurationRequired)

        case let .fatalIssue(issue):
            recoverProcessOrFail(issue: issue)

        case let .warning(warning):
            notice = warning
        }
    }

    private func send(_ command: SpotreadCommand) {
        guard let runner else {
            recoverProcessOrFail(
                issue: .fatal(rawText: String(localized: "spotreadが実行されていないため、操作を送信できませんでした。"))
            )
            return
        }

        let commandGeneration = generation
        let interactionID = appendInteraction(
            direction: .input,
            content: command.rawValue,
            sessionID: commandGeneration,
            inputDeliveryState: .pending
        )

        Task { [weak self] in
            do {
                try await runner.send(command.rawValue)
                self?.updateInputDeliveryState(.sent, interactionID: interactionID)
            } catch {
                guard let self else { return }
                self.updateInputDeliveryState(.failed, interactionID: interactionID)

                guard commandGeneration == self.generation,
                      !self.stopWasRequested else {
                    return
                }
                self.recoverProcessOrFail(
                    issue: .fatal(
                        rawText: String(localized: "spotreadへ操作を送信できませんでした。 \(error.localizedDescription)")
                    )
                )
            }
        }
    }

    private func stopRunner(markStopped: Bool) {
        responseWatchdog?.cancel()
        responseWatchdog = nil
        // The old stream keeps recording its drained tail and termination line.
        // Its generation can no longer mutate the current session state.
        processEventTask = nil

        guard let runner else {
            if markStopped, phase != .idle {
                transition(to: .stopped)
            }
            return
        }

        let stoppedGeneration = generation
        appendInteraction(
            direction: .lifecycle,
            content: String(localized: "終了要求（SIGTERM、250 ms後も動作中ならSIGKILL）"),
            sessionID: stoppedGeneration
        )
        stopWasRequested = true
        generation = UUID()
        runner.stop()
        self.runner = nil
        if markStopped {
            transition(to: .stopped)
        }
    }

    private func processTerminated(status: Int32, generation: UUID) {
        appendInteraction(
            direction: .lifecycle,
            content: String(localized: "spotreadが終了しました（終了コード \(status)）"),
            sessionID: generation
        )
        guard generation == self.generation else { return }
        runner = nil

        for event in parser.finish() {
            guard generation == self.generation else { break }
            handle(event)
        }

        if stopWasRequested {
            transition(to: .stopped)
            return
        }

        guard phase != .failed, phase != .recovering else { return }

        let detail = Self.lastUsefulLines(from: transcript)
        let rawText = detail.isEmpty
            ? String(localized: "spotreadが予期せず終了しました（終了コード \(status)）。")
            : String(localized: "spotreadが予期せず終了しました（終了コード \(status)）。\n\(detail)")
        recoverProcessOrFail(issue: activeIssue ?? .fatal(rawText: rawText))
    }

    private func fail(_ message: String) {
        errorMessage = message
        transition(to: .failed)
        appendInteraction(
            direction: .lifecycle,
            content: String(localized: "エラー\n\(message)"),
            sessionID: generation
        )
    }

    private func transition(to newPhase: MeasurementSessionPhase) {
        phase = newPhase
        scheduleResponseWatchdog()
    }

    private func refreshResponseWatchdog() {
        guard phase.responseTimeout != nil else { return }
        scheduleResponseWatchdog()
    }

    private func scheduleResponseWatchdog() {
        responseWatchdog?.cancel()
        responseWatchdog = nil

        guard let timeout = phase.responseTimeout else { return }
        let watchedGeneration = generation
        let watchedPhase = phase

        responseWatchdog = Task { @MainActor [weak self] in
            do {
                try await Task.sleep(for: timeout)
            } catch {
                return
            }
            guard !Task.isCancelled else { return }
            self?.responseTimedOut(
                generation: watchedGeneration,
                phase: watchedPhase
            )
        }
    }

    private func responseTimedOut(
        generation: UUID,
        phase: MeasurementSessionPhase
    ) {
        guard generation == self.generation,
              phase == self.phase,
              !stopWasRequested else { return }

        let operation = switch phase {
        case .launching: String(localized: "起動")
        case .calibrating: String(localized: "キャリブレーション")
        case .measuring: String(localized: "測定")
        case .recovering: String(localized: "エラーからの復旧")
        default: String(localized: "操作")
        }
        recoverProcessOrFail(issue: .unresponsive(operation: operation))
    }

    private func recoverProcessOrFail(issue: SpotreadIssue) {
        activeIssue = issue
        let rawReason = Self.lastUsefulLines(from: issue.rawText)

        guard automaticRecoveryAttemptCount == 0,
              let mode else {
            stopRunner(markStopped: false)
            activeIssue = issue
            fail(issue.instruction)
            return
        }

        automaticRecoveryAttemptCount += 1
        appendInteraction(
            direction: .lifecycle,
            content: String(localized: "応答不能または復旧不能な状態を検出したため、spotreadを強制終了して自動再起動します\n\(rawReason)"),
            sessionID: generation
        )
        scheduleForcedRelaunch(
            mode: mode,
            resetsMeasurements: false,
            resetsInteractionLog: false,
            resetsRecoveryBudget: false,
            noticeMessage: String(localized: "spotreadが応答しなかったため、強制終了して自動再起動しました。"),
            noticeRawText: issue.rawText
        )
    }

    private func scheduleForcedRelaunch(
        mode: MeasurementMode,
        resetsMeasurements: Bool,
        resetsInteractionLog: Bool,
        resetsRecoveryBudget: Bool,
        noticeMessage: String,
        noticeRawText: String = ""
    ) {
        relaunchTask?.cancel()
        relaunchTask = nil
        stopRunner(markStopped: false)
        stopWasRequested = false
        transition(to: .recovering)

        relaunchTask = Task { @MainActor [weak self] in
            do {
                // SpotreadProcess escalates SIGTERM to SIGKILL after 250 ms.
                // Starting the replacement slightly later avoids competing for
                // the same USB device while the old process is being reaped.
                try await Task.sleep(for: .milliseconds(400))
            } catch {
                return
            }
            guard let self,
                  !Task.isCancelled,
                  self.phase == .recovering else { return }

            self.relaunchTask = nil
            self.launch(
                mode: mode,
                resetsMeasurements: resetsMeasurements,
                resetsInteractionLog: resetsInteractionLog,
                resetsRecoveryBudget: resetsRecoveryBudget
            )
            self.notice = SpotreadNotice(
                message: noticeMessage,
                rawText: noticeRawText
            )
        }
    }

    @discardableResult
    private func appendInteraction(
        direction: SpotreadInteractionDirection,
        content: String,
        sessionID: UUID?,
        inputDeliveryState: SpotreadInputDeliveryState? = nil
    ) -> UUID {
        if direction == .output,
           let lastIndex = interactions.indices.last,
           interactions[lastIndex].direction == .output,
           interactions[lastIndex].sessionID == sessionID {
            interactions[lastIndex].content.append(content)
            let interactionID = interactions[lastIndex].id
            interactionCharacterCount += content.count
            interactionRevision &+= 1
            trimInteractionLogIfNeeded()
            return interactionID
        }

        let interaction = SpotreadInteraction(
            sessionID: sessionID,
            direction: direction,
            content: content,
            inputDeliveryState: inputDeliveryState
        )
        interactions.append(interaction)
        interactionCharacterCount += content.count
        interactionRevision &+= 1
        trimInteractionLogIfNeeded()
        return interaction.id
    }

    private func updateInputDeliveryState(
        _ state: SpotreadInputDeliveryState,
        interactionID: UUID
    ) {
        guard let index = interactions.firstIndex(where: { $0.id == interactionID }) else { return }
        interactions[index].inputDeliveryState = state
        interactionRevision &+= 1
    }

    private func trimInteractionLogIfNeeded() {
        guard interactionCharacterCount > Self.interactionCharacterLimit else { return }

        while interactionCharacterCount > Self.retainedInteractionCharacterTarget,
              interactions.count > 1 {
            interactionCharacterCount -= interactions.removeFirst().content.count
        }

        if interactionCharacterCount > Self.retainedInteractionCharacterTarget,
           let onlyIndex = interactions.indices.first {
            let retainedContent = String(
                interactions[onlyIndex].content.suffix(Self.retainedInteractionCharacterTarget)
            )
            interactionCharacterCount = retainedContent.count
            interactions[onlyIndex].content = retainedContent
        }

        let notice = SpotreadInteraction(
            direction: .lifecycle,
            content: String(localized: "表示上限を超えたため、古い通信記録を省略しました")
        )
        interactions.insert(notice, at: 0)
        interactionCharacterCount += notice.content.count
    }

    private static func lastUsefulLines(from text: String) -> String {
        let ignoredPrefixes = [
            "Hit ESC", "Hit Esc", "and hit", "or hit", "Place instrument on spot"
        ]

        let lines = text
            .split(whereSeparator: \.isNewline)
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { line in
                !line.isEmpty && !ignoredPrefixes.contains { line.hasPrefix($0) }
            }

        return lines.suffix(4).joined(separator: "\n")
    }
}
