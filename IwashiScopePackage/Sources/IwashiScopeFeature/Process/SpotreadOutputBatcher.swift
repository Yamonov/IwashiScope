import Foundation

/// Coalesces arbitrary pipe read boundaries before crossing to the main actor.
/// Valid UTF-8 scalars are preserved even when they cross a timed flush boundary.
final class SpotreadOutputBatcher: @unchecked Sendable {
    private let queue = DispatchQueue(
        label: "com.yamonov.IwashiScope.spotread-output",
        qos: .userInitiated
    )
    private let flushInterval: DispatchTimeInterval
    private let onOutput: @Sendable (String) -> Void

    private var pendingData = Data()
    private var incompleteUTF8Data = Data()
    private var flushScheduled = false
    private var finished = false

    init(
        flushInterval: DispatchTimeInterval = .milliseconds(100),
        onOutput: @escaping @Sendable (String) -> Void
    ) {
        self.flushInterval = flushInterval
        self.onOutput = onOutput
    }

    func append(_ data: Data) {
        guard !data.isEmpty else { return }

        queue.async { [self] in
            guard !finished else { return }
            pendingData.append(data)

            guard !flushScheduled else { return }
            flushScheduled = true
            queue.asyncAfter(deadline: .now() + flushInterval) { [weak self] in
                guard let self else { return }
                flushScheduled = false
                flushPendingData(isFinal: false)
            }
        }
    }

    func finish(completion: @escaping @Sendable () -> Void) {
        queue.async { [self] in
            guard !finished else {
                completion()
                return
            }

            finished = true
            flushPendingData(isFinal: true)
            completion()
        }
    }

    private func flushPendingData(isFinal: Bool) {
        guard !pendingData.isEmpty || !incompleteUTF8Data.isEmpty else { return }

        var data = incompleteUTF8Data
        data.append(pendingData)
        incompleteUTF8Data.removeAll(keepingCapacity: true)
        pendingData.removeAll(keepingCapacity: true)

        if !isFinal {
            let incompleteCount = Self.trailingIncompleteUTF8ByteCount(in: data)
            if incompleteCount > 0 {
                incompleteUTF8Data = Data(data.suffix(incompleteCount))
                data.removeLast(incompleteCount)
            }
        }

        guard !data.isEmpty else { return }
        onOutput(String(decoding: data, as: UTF8.self))
    }

    private static func trailingIncompleteUTF8ByteCount(in data: Data) -> Int {
        guard !data.isEmpty else { return 0 }

        let bytes = Array(data.suffix(4))
        var continuationCount = 0
        for byte in bytes.reversed() {
            guard byte & 0xC0 == 0x80 else { break }
            continuationCount += 1
        }

        let leadIndex = bytes.count - continuationCount - 1
        guard leadIndex >= 0 else { return 0 }
        let lead = bytes[leadIndex]
        let expectedCount: Int
        switch lead {
        case 0x00...0x7F:
            expectedCount = 1
        case 0xC0...0xDF:
            expectedCount = 2
        case 0xE0...0xEF:
            expectedCount = 3
        case 0xF0...0xF7:
            expectedCount = 4
        default:
            return 0
        }

        let availableCount = continuationCount + 1
        return expectedCount > availableCount ? availableCount : 0
    }
}
