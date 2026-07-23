import Darwin
import Foundation

/// A small independent process that terminates spotread if SpectraMate is
/// force-quit and therefore cannot execute its normal cleanup path.
enum SpotreadParentWatchdog {
    static func start(
        parentProcessIdentifier: Int32 = Darwin.getpid(),
        childProcessIdentifier: Int32
    ) throws -> Process {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/bin/sh")
        process.arguments = [
            "-c",
            script,
            "spectramate-spotread-watchdog",
            String(parentProcessIdentifier),
            String(childProcessIdentifier),
        ]
        process.standardInput = FileHandle.nullDevice
        process.standardOutput = FileHandle.nullDevice
        process.standardError = FileHandle.nullDevice
        try process.run()
        return process
    }

    private static let script = """
    parent_pid="$1"
    child_pid="$2"

    while kill -0 "$parent_pid" 2>/dev/null && kill -0 "$child_pid" 2>/dev/null; do
        sleep 0.2
    done

    if ! kill -0 "$parent_pid" 2>/dev/null && kill -0 "$child_pid" 2>/dev/null; then
        kill -TERM "$child_pid" 2>/dev/null
        sleep 0.3
        kill -KILL "$child_pid" 2>/dev/null
    fi
    """
}
