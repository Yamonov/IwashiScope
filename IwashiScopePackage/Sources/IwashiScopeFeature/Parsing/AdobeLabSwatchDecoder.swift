import Foundation

enum AdobeLabSwatchDecodingError: LocalizedError {
    case unsupportedFormat
    case invalidData
    case noLabSwatches

    var errorDescription: String? {
        switch self {
        case .unsupportedFormat:
            String(localized: "ACOまたはACB形式のスウォッチファイルを選んでください。")
        case .invalidData:
            String(localized: "スウォッチファイルの形式が正しくないか、データが途中で途切れています。")
        case .noLabSwatches:
            String(localized: "このファイルには読み込めるLabスウォッチがありません。")
        }
    }
}

enum AdobeLabSwatchDecoder {
    private static let labColorSpace: UInt16 = 7

    static func load(from url: URL) async throws -> LabSwatchLibrary {
        let accessed = url.startAccessingSecurityScopedResource()
        defer {
            if accessed {
                url.stopAccessingSecurityScopedResource()
            }
        }
        return try decode(Data(contentsOf: url), fileName: url.lastPathComponent)
    }

    static func decode(_ data: Data, fileName: String) throws -> LabSwatchLibrary {
        let file = URL(fileURLWithPath: fileName)
        let fileTitle = file.deletingPathExtension().lastPathComponent
        var reader = SwatchReader(data)
        let library: LabSwatchLibrary
        switch file.pathExtension.lowercased() {
        case "aco":
            library = LabSwatchLibrary(name: fileTitle, swatches: try readACO(&reader))
        case "acb":
            library = try readACB(&reader, fileTitle: fileTitle)
        default:
            throw AdobeLabSwatchDecodingError.unsupportedFormat
        }
        guard !library.swatches.isEmpty else {
            throw AdobeLabSwatchDecodingError.noLabSwatches
        }
        return library
    }

    private static func readACO(_ reader: inout SwatchReader) throws -> [AdobeLabSwatch] {
        var version = try reader.readInteger(UInt16.self)
        var count = Int(try reader.readInteger(UInt16.self))
        let recordSize = 10
        if version == 1 {
            var afterUnnamedRecords = reader
            try afterUnnamedRecords.take(count * recordSize)
            if afterUnnamedRecords.remaining > 0 {
                reader = afterUnnamedRecords
                version = try reader.readInteger(UInt16.self)
                guard version == 2 else { throw AdobeLabSwatchDecodingError.invalidData }
                count = Int(try reader.readInteger(UInt16.self))
            }
        }
        guard version == 1 || version == 2,
              count <= reader.remaining / (version == 1 ? recordSize : recordSize + 4) else {
            throw AdobeLabSwatchDecodingError.invalidData
        }

        var swatches: [AdobeLabSwatch] = []
        for index in 0..<count {
            let colorSpace = try reader.readInteger(UInt16.self)
            guard colorSpace == labColorSpace else {
                try reader.take(recordSize - 2)
                if version == 2 {
                    try reader.takeString()
                }
                continue
            }

            let lightness = try reader.readInteger(UInt16.self)
            let a = try reader.readInteger(Int16.self)
            let b = try reader.readInteger(Int16.self)
            try reader.take(2) // Fourth component is unused.
            let nameRange = version == 2 ? try reader.takeString() : nil
            guard lightness <= 10_000,
                  (-12_800...12_700).contains(a),
                  (-12_800...12_700).contains(b) else {
                continue
            }
            let name = try nameRange.map { try reader.decodeString(in: $0) } ?? ""
            swatches.append(AdobeLabSwatch(
                name: displayName(name, index: index),
                lab: Vector3(first: Double(lightness) / 100, second: Double(a) / 100, third: Double(b) / 100)
            ))
        }
        guard reader.remaining == 0 else { throw AdobeLabSwatchDecodingError.invalidData }
        return swatches
    }

    private static func readACB(
        _ reader: inout SwatchReader,
        fileTitle: String
    ) throws -> LabSwatchLibrary {
        guard try reader.readInteger(UInt32.self) == 0x38424342, // 8BCB
              try reader.readInteger(UInt16.self) == 1 else {
            throw AdobeLabSwatchDecodingError.invalidData
        }
        try reader.take(2) // Book ID
        let titleRange = try reader.takeString()
        let prefixRange = try reader.takeString()
        let suffixRange = try reader.takeString()
        try reader.takeString() // Description
        let count = Int(try reader.readInteger(UInt16.self))
        try reader.take(4) // Page size and key color index
        guard try reader.readInteger(UInt16.self) == labColorSpace else {
            throw AdobeLabSwatchDecodingError.noLabSwatches
        }
        guard count <= reader.remaining / 13 else { throw AdobeLabSwatchDecodingError.invalidData }

        let title = displayString(try reader.decodeString(in: titleRange))
        let prefix = displayString(try reader.decodeString(in: prefixRange))
        let suffix = displayString(try reader.decodeString(in: suffixRange))
        var swatches: [AdobeLabSwatch] = []
        for _ in 0..<count {
            let name = displayString(try reader.readString())
            try reader.take(6) // Color code
            let lightness = try reader.readInteger(UInt8.self)
            let a = try reader.readInteger(UInt8.self)
            let b = try reader.readInteger(UInt8.self)
            // Empty names are used to leave gaps in the book's page layout.
            guard !name.isEmpty else { continue }
            swatches.append(AdobeLabSwatch(
                name: prefix + name + suffix,
                lab: Vector3(
                    first: Double(lightness) * 100 / 255,
                    second: Double(a) - 128,
                    third: Double(b) - 128
                )
            ))
        }
        if reader.remaining != 0 {
            let footer = try reader.readInteger(UInt64.self)
            guard reader.remaining == 0,
                  footer == 0x7370666c73706f74 || footer == 0x7370666c70726f63 else { // spflspot / spflproc
                throw AdobeLabSwatchDecodingError.invalidData
            }
        }
        return LabSwatchLibrary(name: title.isEmpty ? fileTitle : title, swatches: swatches)
    }

    private static func displayName(_ name: String, index: Int) -> String {
        let displayName = displayString(name)
        return displayName.isEmpty ? String(localized: "スウォッチ \(index + 1)") : displayName
    }

    private static func displayString(_ string: String) -> String {
        var result = string.trimmingCharacters(in: CharacterSet(charactersIn: "\0"))
        if result.hasPrefix("$$$/"), let separator = result.firstIndex(of: "=") {
            result = String(result[result.index(after: separator)...])
        }
        return result.replacingOccurrences(of: "^R", with: "®")
            .replacingOccurrences(of: "^C", with: "©")
    }
}

private struct SwatchReader {
    let data: Data
    private var offset: Data.Index

    init(_ data: Data) {
        self.data = data
        offset = data.startIndex
    }

    var remaining: Int { data.endIndex - offset }

    @discardableResult
    mutating func take(_ count: Int) throws -> Range<Data.Index> {
        guard count <= remaining else { throw AdobeLabSwatchDecodingError.invalidData }
        let range = offset..<(offset + count)
        offset = range.upperBound
        return range
    }

    mutating func readInteger<T: FixedWidthInteger>(_ type: T.Type) throws -> T {
        let range = try take(MemoryLayout<T>.size)
        return data.withUnsafeBytes { bytes in
            T(bigEndian: bytes.loadUnaligned(fromByteOffset: range.lowerBound - data.startIndex, as: type))
        }
    }

    @discardableResult
    mutating func takeString() throws -> Range<Data.Index> {
        let count = Int(try readInteger(UInt32.self))
        return try take(count * 2)
    }

    mutating func readString() throws -> String {
        let range = try takeString()
        return try decodeString(in: range)
    }

    func decodeString(in range: Range<Data.Index>) throws -> String {
        guard let string = String(data: data[range], encoding: .utf16BigEndian) else {
            throw AdobeLabSwatchDecodingError.invalidData
        }
        return string
    }
}
