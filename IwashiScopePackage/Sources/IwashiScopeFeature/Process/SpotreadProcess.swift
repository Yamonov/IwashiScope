import Foundation
import Darwin

enum SpotreadProcessError: LocalizedError {
    case alreadyRunning
    case notRunning
    case inputUnavailable

    var errorDescription: String? {
        switch self {
        case .alreadyRunning:
            String(localized: "spotreadはすでに実行中です。")
        case .notRunning:
            String(localized: "spotreadが実行されていません。")
        case .inputUnavailable:
            String(localized: "spotreadへ操作を送信できません。")
        }
    }
}

final class SpotreadProcess: @unchecked Sendable {
    private static let forcedTerminationQueue = DispatchQueue(
        label: "com.yamonov.IwashiScope.spotread-termination",
        qos: .userInitiated
    )

    private let lock = NSLock()
    private let inputQueue = DispatchQueue(
        label: "com.yamonov.IwashiScope.spotread-input",
        qos: .userInitiated
    )
    private var process: Process?
    private var inputPipe: Pipe?
    private var protocolOutputPipe: Pipe?
    private var logOutputPipe: Pipe?
    private var watchdogProcess: Process?

    func start(
        executableURL: URL,
        arguments: [String],
        onProtocolOutput: @escaping @Sendable (String) -> Void,
        onLogOutput: @escaping @Sendable (String) -> Void,
        onTermination: @escaping @Sendable (Int32) -> Void
    ) throws {
        lock.lock()
        defer { lock.unlock() }

        guard process == nil else { throw SpotreadProcessError.alreadyRunning }

        let process = Process()
        let inputPipe = Pipe()
        let protocolOutputPipe = Pipe()
        let logOutputPipe = Pipe()
        let protocolOutputBatcher = SpotreadOutputBatcher(onOutput: onProtocolOutput)
        let logOutputBatcher = SpotreadOutputBatcher(onOutput: onLogOutput)
        let protocolReadQueue = DispatchQueue(
            label: "com.yamonov.IwashiScope.spotread-protocol-read",
            qos: .userInitiated
        )
        let logReadQueue = DispatchQueue(
            label: "com.yamonov.IwashiScope.spotread-log-read",
            qos: .userInitiated
        )

        process.executableURL = executableURL
        process.arguments = arguments
        var environment = ProcessInfo.processInfo.environment
        environment["ARGYLL_NOT_INTERACTIVE"] = "1"
        process.environment = environment
        process.standardInput = inputPipe
        process.standardOutput = protocolOutputPipe
        process.standardError = logOutputPipe

        protocolOutputPipe.fileHandleForReading.readabilityHandler = { handle in
            let reachedEndOfFile = protocolReadQueue.sync {
                let data = handle.availableData
                guard !data.isEmpty else { return true }
                protocolOutputBatcher.append(data)
                return false
            }
            if reachedEndOfFile {
                handle.readabilityHandler = nil
            }
        }

        logOutputPipe.fileHandleForReading.readabilityHandler = { handle in
            let reachedEndOfFile = logReadQueue.sync {
                let data = handle.availableData
                guard !data.isEmpty else { return true }
                logOutputBatcher.append(data)
                return false
            }
            if reachedEndOfFile {
                handle.readabilityHandler = nil
            }
        }

        process.terminationHandler = { [weak self] terminatedProcess in
            let protocolHandle = protocolOutputPipe.fileHandleForReading
            let logHandle = logOutputPipe.fileHandleForReading
            protocolHandle.readabilityHandler = nil
            logHandle.readabilityHandler = nil
            self?.clearIfCurrent(terminatedProcess)

            let drainGroup = DispatchGroup()
            drainGroup.enter()
            protocolReadQueue.async {
                protocolOutputBatcher.append(protocolHandle.readDataToEndOfFile())
                protocolOutputBatcher.finish {
                    drainGroup.leave()
                }
            }
            drainGroup.enter()
            logReadQueue.async {
                logOutputBatcher.append(logHandle.readDataToEndOfFile())
                logOutputBatcher.finish {
                    drainGroup.leave()
                }
            }
            drainGroup.notify(queue: Self.forcedTerminationQueue) {
                onTermination(terminatedProcess.terminationStatus)
            }
        }

        self.process = process
        self.inputPipe = inputPipe
        self.protocolOutputPipe = protocolOutputPipe
        self.logOutputPipe = logOutputPipe

        do {
            try process.run()
            watchdogProcess = try SpotreadParentWatchdog.start(
                childProcessIdentifier: process.processIdentifier
            )
        } catch {
            protocolOutputPipe.fileHandleForReading.readabilityHandler = nil
            logOutputPipe.fileHandleForReading.readabilityHandler = nil
            process.terminationHandler = nil
            if process.isRunning {
                Darwin.kill(process.processIdentifier, SIGKILL)
            }
            self.process = nil
            self.inputPipe = nil
            self.protocolOutputPipe = nil
            self.logOutputPipe = nil
            watchdogProcess = nil
            throw error
        }
    }

    func send(_ text: String) async throws {
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            inputQueue.async { [weak self] in
                guard let self else {
                    continuation.resume(throwing: SpotreadProcessError.notRunning)
                    return
                }

                do {
                    try self.write(text)
                    continuation.resume(returning: ())
                } catch {
                    continuation.resume(throwing: error)
                }
            }
        }
    }

    private func write(_ text: String) throws {
        let handle: FileHandle

        lock.lock()
        guard let process, process.isRunning else {
            lock.unlock()
            throw SpotreadProcessError.notRunning
        }
        guard let inputPipe else {
            lock.unlock()
            throw SpotreadProcessError.inputUnavailable
        }
        handle = inputPipe.fileHandleForWriting
        lock.unlock()

        guard let data = text.data(using: .utf8) else {
            throw SpotreadProcessError.inputUnavailable
        }
        try handle.write(contentsOf: data)
    }

    func stop() {
        let runningProcess: Process?
        let runningWatchdog: Process?
        let inputHandle: FileHandle?

        lock.lock()
        runningProcess = process
        runningWatchdog = watchdogProcess
        inputHandle = inputPipe?.fileHandleForWriting
        process = nil
        inputPipe = nil
        protocolOutputPipe = nil
        logOutputPipe = nil
        watchdogProcess = nil
        lock.unlock()

        if let runningProcess, runningProcess.isRunning {
            Darwin.kill(runningProcess.processIdentifier, SIGTERM)
        }

        Self.forcedTerminationQueue.async {
            try? inputHandle?.close()
            if let runningWatchdog, runningWatchdog.isRunning {
                runningWatchdog.terminate()
            }

            guard let runningProcess else { return }
            let processIdentifier = runningProcess.processIdentifier
            Self.forcedTerminationQueue.asyncAfter(deadline: .now() + .milliseconds(250)) {
                guard runningProcess.isRunning else { return }
                Darwin.kill(processIdentifier, SIGKILL)
            }
        }
    }

    private func clearIfCurrent(_ terminatedProcess: Process) {
        let watchdog: Process?

        lock.lock()
        guard process === terminatedProcess else {
            lock.unlock()
            return
        }
        watchdog = watchdogProcess
        process = nil
        inputPipe = nil
        protocolOutputPipe = nil
        logOutputPipe = nil
        watchdogProcess = nil
        lock.unlock()

        if let watchdog, watchdog.isRunning {
            watchdog.terminate()
        }
    }

    deinit {
        stop()
    }
}
