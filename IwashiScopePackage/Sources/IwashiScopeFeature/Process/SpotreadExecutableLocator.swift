import Foundation

enum SpotreadExecutableLocator {
    static func locate(
        environment: [String: String] = ProcessInfo.processInfo.environment,
        bundle: Bundle = .main,
        fileManager: FileManager = .default
    ) -> URL? {
        var candidates: [URL] = []

        if let override = environment["IWASHISCOPE_SPOTREAD_PATH"], !override.isEmpty {
            candidates.append(URL(fileURLWithPath: override))
        }

        if let auxiliaryExecutable = bundle.url(forAuxiliaryExecutable: "spotread") {
            candidates.append(auxiliaryExecutable)
        }

        if let executableDirectory = bundle.executableURL?.deletingLastPathComponent() {
            candidates.append(executableDirectory.appendingPathComponent("spotread"))
        }

        if let resourceURL = bundle.resourceURL {
            candidates.append(resourceURL.appendingPathComponent("spotread"))
            candidates.append(resourceURL.appendingPathComponent("ArgyllCMS/bin/spotread"))
        }

        var visited = Set<String>()
        return candidates.first { candidate in
            let standardizedPath = candidate.standardizedFileURL.path
            guard visited.insert(standardizedPath).inserted else { return false }
            return fileManager.isExecutableFile(atPath: standardizedPath)
        }
    }
}
