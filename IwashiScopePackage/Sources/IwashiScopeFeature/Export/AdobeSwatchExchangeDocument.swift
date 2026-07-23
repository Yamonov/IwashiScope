import SwiftUI
import UniformTypeIdentifiers

extension UTType {
    static let adobeSwatchExchange = UTType(filenameExtension: "ase")
        ?? UTType(exportedAs: "com.yamonov.iwashiscope.adobe-swatch-exchange", conformingTo: .data)
}

struct AdobeSwatchExchangeDocument: FileDocument {
    static let readableContentTypes: [UTType] = [.adobeSwatchExchange]

    let data: Data

    init(data: Data) {
        self.data = data
    }

    init(configuration: ReadConfiguration) throws {
        guard let data = configuration.file.regularFileContents else {
            throw CocoaError(.fileReadCorruptFile)
        }
        self.data = data
    }

    func fileWrapper(configuration _: WriteConfiguration) throws -> FileWrapper {
        FileWrapper(regularFileWithContents: data)
    }
}
