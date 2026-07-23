import Foundation

struct AdobeLabSwatch: Equatable, Sendable {
    let name: String
    let lab: Vector3
}

enum AdobeSwatchExchangeEncodingError: Error, Equatable {
    case tooManySwatches
    case swatchNameTooLong(String)
    case invalidLabValue(swatchName: String)
    case blockTooLarge
}

extension AdobeSwatchExchangeEncodingError: LocalizedError {
    var errorDescription: String? {
        switch self {
        case .tooManySwatches:
            "選択されたスウォッチ数がASE形式の上限を超えています。"
        case .swatchNameTooLong(let name):
            "スウォッチ名が長すぎます：\(name)"
        case .invalidLabValue(let swatchName):
            "有限値ではないLab値が含まれています：\(swatchName)"
        case .blockTooLarge:
            "ASEデータブロックが形式の上限を超えています。"
        }
    }
}

enum AdobeSwatchExchangeEncoder {
    private static let colorEntryBlockType: UInt16 = 0x0001
    private static let spotColorType: UInt16 = 0x0001

    static func encode(swatches: [AdobeLabSwatch]) throws -> Data {
        guard UInt64(swatches.count) <= UInt64(UInt32.max) else {
            throw AdobeSwatchExchangeEncodingError.tooManySwatches
        }

        var writer = BigEndianDataWriter()
        writer.appendASCII("ASEF")
        writer.append(UInt16(1))
        writer.append(UInt16(0))
        writer.append(UInt32(swatches.count))

        for swatch in swatches {
            let payload = try colorEntryPayload(for: swatch)
            guard UInt64(payload.count) <= UInt64(UInt32.max) else {
                throw AdobeSwatchExchangeEncodingError.blockTooLarge
            }

            writer.append(colorEntryBlockType)
            writer.append(UInt32(payload.count))
            writer.append(payload)
        }

        return writer.data
    }

    private static func colorEntryPayload(for swatch: AdobeLabSwatch) throws -> Data {
        let values = [swatch.lab.first, swatch.lab.second, swatch.lab.third]
        let floatValues = values.map(Float.init)
        guard values.allSatisfy(\.isFinite),
              floatValues.allSatisfy(\.isFinite) else {
            throw AdobeSwatchExchangeEncodingError.invalidLabValue(
                swatchName: swatch.name
            )
        }

        var writer = BigEndianDataWriter()
        try writer.appendASEName(swatch.name)
        writer.appendASCII("LAB ")
        // ASE stores CIE L* normalized to 0...1; a* and b* retain their Lab values.
        writer.append(floatValues[0] / 100)
        writer.append(floatValues[1])
        writer.append(floatValues[2])
        writer.append(spotColorType)
        return writer.data
    }
}

private struct BigEndianDataWriter {
    private(set) var data = Data()

    mutating func append(_ value: UInt16) {
        var bigEndianValue = value.bigEndian
        Swift.withUnsafeBytes(of: &bigEndianValue) { bytes in
            data.append(contentsOf: bytes)
        }
    }

    mutating func append(_ value: UInt32) {
        var bigEndianValue = value.bigEndian
        Swift.withUnsafeBytes(of: &bigEndianValue) { bytes in
            data.append(contentsOf: bytes)
        }
    }

    mutating func append(_ value: Float) {
        append(value.bitPattern)
    }

    mutating func append(_ value: Data) {
        data.append(value)
    }

    mutating func appendASCII(_ value: String) {
        data.append(contentsOf: value.utf8)
    }

    mutating func appendASEName(_ name: String) throws {
        let codeUnits = Array(name.utf16)
        guard codeUnits.count < Int(UInt16.max) else {
            throw AdobeSwatchExchangeEncodingError.swatchNameTooLong(name)
        }

        append(UInt16(codeUnits.count + 1))
        codeUnits.forEach { append($0) }
        append(UInt16(0))
    }
}
