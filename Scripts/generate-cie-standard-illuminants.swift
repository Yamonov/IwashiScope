#!/usr/bin/env swift

/*
 SPDX-FileCopyrightText: 2026 Yamonov
 SPDX-License-Identifier: AGPL-3.0-only

 This generator is part of IwashiScope. It verifies the original CIE datasets
 and creates the GPL-3.0-only Swift adaptation described in
 ThirdParty/CIE/README.md.
*/

import CryptoKit
import Darwin
import Foundation

private struct Dataset {
    let name: String
    let sourceFileName: String
    let sourceURL: String
    let doi: String
    let expectedMD5: String
    let expectedSHA256: String
}

private enum GeneratorError: Error, CustomStringConvertible {
    case invalidArguments
    case checksumMismatch(file: String, algorithm: String, expected: String, actual: String)
    case invalidCSV(file: String, line: String)
    case missingWavelength(file: String, wavelength: Int)
    case generatedFileOutOfDate

    var description: String {
        switch self {
        case .invalidArguments:
            "usage: Scripts/generate-cie-standard-illuminants.swift [--check]"
        case let .checksumMismatch(file, algorithm, expected, actual):
            "\(file): \(algorithm) checksum mismatch; expected \(expected), got \(actual)"
        case let .invalidCSV(file, line):
            "\(file): invalid CSV row: \(line)"
        case let .missingWavelength(file, wavelength):
            "\(file): missing \(wavelength) nm sample"
        case .generatedFileOutOfDate:
            "CIEStandardIlluminantData.generated.swift is not up to date"
        }
    }
}

private let datasets = [
    Dataset(
        name: "D50",
        sourceFileName: "CIE_std_illum_D50.csv",
        sourceURL: "https://files.cie.co.at/CIE_std_illum_D50.csv",
        doi: "10.25039/CIE.DS.etgmuqt5",
        expectedMD5: "e72757c3078b58e78ba63051be4b27b0",
        expectedSHA256: "b23049c6f7b266c1c1fbe147aa271e8930ca02d6e569c5ae1804c036faea4193"
    ),
    Dataset(
        name: "D65",
        sourceFileName: "CIE_std_illum_D65.csv",
        sourceURL: "https://files.cie.co.at/CIE_std_illum_D65.csv",
        doi: "10.25039/CIE.DS.hjfjmt59",
        expectedMD5: "03d4eb9b837c60671627c946fb534deb",
        expectedSHA256: "e76f210bffff3d552ef7113025da5f325d5dfec200dd4b878b1a2f3a507032cb"
    ),
]

private let scriptURL = URL(fileURLWithPath: #filePath).standardizedFileURL
private let projectRoot = scriptURL
    .deletingLastPathComponent()
    .deletingLastPathComponent()
private let sourceDirectory = projectRoot
    .appendingPathComponent("ThirdParty/CIE", isDirectory: true)
private let outputURL = projectRoot
    .appendingPathComponent(
        "IwashiScopePackage/Sources/IwashiScopeFeature/Models/"
            + "CIEStandardIlluminantData.generated.swift"
    )

private func hexadecimalDigest<Digest: Sequence>(_ digest: Digest) -> String
where Digest.Element == UInt8 {
    digest.map { String(format: "%02x", $0) }.joined()
}

private func loadValues(for dataset: Dataset) throws -> [String] {
    let sourceURL = sourceDirectory.appendingPathComponent(dataset.sourceFileName)
    let data = try Data(contentsOf: sourceURL)

    let md5 = hexadecimalDigest(Insecure.MD5.hash(data: data))
    guard md5 == dataset.expectedMD5 else {
        throw GeneratorError.checksumMismatch(
            file: dataset.sourceFileName,
            algorithm: "MD5",
            expected: dataset.expectedMD5,
            actual: md5
        )
    }

    let sha256 = hexadecimalDigest(SHA256.hash(data: data))
    guard sha256 == dataset.expectedSHA256 else {
        throw GeneratorError.checksumMismatch(
            file: dataset.sourceFileName,
            algorithm: "SHA-256",
            expected: dataset.expectedSHA256,
            actual: sha256
        )
    }

    guard let text = String(data: data, encoding: .utf8) else {
        throw GeneratorError.invalidCSV(file: dataset.sourceFileName, line: "<not UTF-8>")
    }

    var valuesByWavelength: [Int: String] = [:]
    for line in text.split(whereSeparator: { $0.isNewline }) {
        let columns = line.split(separator: ",", omittingEmptySubsequences: false)
        guard columns.count == 2,
              let wavelength = Int(columns[0]),
              let value = Double(columns[1]),
              value.isFinite else {
            throw GeneratorError.invalidCSV(file: dataset.sourceFileName, line: String(line))
        }
        valuesByWavelength[wavelength] = String(columns[1])
    }

    return try stride(from: 380, through: 730, by: 5).map { wavelength in
        guard let value = valuesByWavelength[wavelength] else {
            throw GeneratorError.missingWavelength(
                file: dataset.sourceFileName,
                wavelength: wavelength
            )
        }
        return value
    }
}

private func formattedArray(_ values: [String]) -> String {
    stride(from: 0, to: values.count, by: 8).map { startIndex in
        let endIndex = min(startIndex + 8, values.count)
        return "        " + values[startIndex..<endIndex].joined(separator: ", ") + ","
    }
    .joined(separator: "\n")
}

private func generatedSource() throws -> String {
    let d50Values = try loadValues(for: datasets[0])
    let d65Values = try loadValues(for: datasets[1])

    return """
    /*
     SPDX-FileCopyrightText: 2026 Yamonov
     SPDX-License-Identifier: GPL-3.0-only

     This file is generated by Scripts/generate-cie-standard-illuminants.swift.
     Do not edit it by hand.

     Adapted from:
       CIE standard illuminant D50, DOI 10.25039/CIE.DS.etgmuqt5
       CIE standard illuminant D65, DOI 10.25039/CIE.DS.hjfjmt59
     Dataset creator and publisher:
       International Commission on Illumination (CIE), Vienna, AT
     Original dataset license:
       Creative Commons Attribution-ShareAlike 4.0 International
       https://creativecommons.org/licenses/by-sa/4.0/
     Transformation: select 380...730 nm at 5 nm intervals from the official
                     1 nm CSV data without numerical interpolation, and express
                     the selected values as Swift arrays.
     Adaptation date: 2026-07-23

     Yamonov's contributions to this adaptation are licensed under GNU GPL
     version 3 only, a one-way BY-SA Compatible License for adaptations of
     CC BY-SA 4.0 material. Both CC BY-SA 4.0 and GPL-3.0-only apply to the
     adapted material. When combined with IwashiScope, GPL-3.0-only continues
     to govern this file and AGPL-3.0-only continues to govern the IwashiScope
     portions under section 13 of GPLv3 and AGPLv3.

     Full attribution, source files, checksums, transformation details, and
     license references are in ThirdParty/CIE/README.md.
     */

    enum CIEStandardIlluminantData {
        static let d50Values: [Double] = [
    \(formattedArray(d50Values))
        ]

        static let d65Values: [Double] = [
    \(formattedArray(d65Values))
        ]
    }

    """
}

do {
    let arguments = Array(CommandLine.arguments.dropFirst())
    guard arguments.isEmpty || arguments == ["--check"] else {
        throw GeneratorError.invalidArguments
    }

    let output = try generatedSource()
    if arguments == ["--check"] {
        let existingOutput = try String(contentsOf: outputURL, encoding: .utf8)
        guard existingOutput == output else {
            throw GeneratorError.generatedFileOutOfDate
        }
        print("CIE standard illuminant data is up to date.")
    } else {
        try output.write(to: outputURL, atomically: true, encoding: .utf8)
        print("Generated \(outputURL.path)")
    }
} catch {
    FileHandle.standardError.write(Data("error: \(error)\n".utf8))
    exit(1)
}
