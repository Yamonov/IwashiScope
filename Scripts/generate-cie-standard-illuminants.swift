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
    let illuminantCases: [String]
}

private enum GeneratorError: Error, CustomStringConvertible {
    case invalidArguments
    case checksumMismatch(file: String, algorithm: String, expected: String, actual: String)
    case invalidCSV(file: String, line: String)
    case missingWavelength(file: String, wavelength: Int)
    case missingIlluminant(String)
    case generatedFileOutOfDate(String)

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
        case let .missingIlluminant(name):
            "missing generated values for CIE illuminant \(name)"
        case let .generatedFileOutOfDate(file):
            "\(file) is not up to date"
        }
    }
}

private let datasets = [
    Dataset(
        name: "CIE standard illuminant A",
        sourceFileName: "CIE_std_illum_A_1nm.csv",
        sourceURL: "https://files.cie.co.at/CIE_std_illum_A_1nm.csv",
        doi: "10.25039/CIE.DS.8jsxjrsn",
        expectedMD5: "ed0e4effb55d82b950c0912b6278a9d1",
        expectedSHA256: "61ef23fe146b8b665c74706717ab28cec7db6c9022993490bdc71991f43cb59b",
        illuminantCases: ["a"]
    ),
    Dataset(
        name: "CIE illuminant C",
        sourceFileName: "CIE_illum_C.csv",
        sourceURL: "https://files.cie.co.at/CIE_illum_C.csv",
        doi: "10.25039/CIE.DS.mjdd2enu",
        expectedMD5: "bb325f040bab332d91f8d9447aa47b78",
        expectedSHA256: "51c53bf065345fc1fb9d50aff40fb0a2ce370bcf35121a3dc4bafc0888602f0b",
        illuminantCases: ["c"]
    ),
    Dataset(
        name: "CIE standard illuminant D50",
        sourceFileName: "CIE_std_illum_D50.csv",
        sourceURL: "https://files.cie.co.at/CIE_std_illum_D50.csv",
        doi: "10.25039/CIE.DS.etgmuqt5",
        expectedMD5: "e72757c3078b58e78ba63051be4b27b0",
        expectedSHA256: "b23049c6f7b266c1c1fbe147aa271e8930ca02d6e569c5ae1804c036faea4193",
        illuminantCases: ["d50"]
    ),
    Dataset(
        name: "CIE illuminant D55",
        sourceFileName: "CIE_illum_D55.csv",
        sourceURL: "https://files.cie.co.at/CIE_illum_D55.csv",
        doi: "10.25039/CIE.DS.qewfb3kp",
        expectedMD5: "8b5678358f265567a0759b9800f17d7f",
        expectedSHA256: "3e5aa1a8d5514df1928effef1615ab90e16c9a368ccfe513372cd8556c37bf4b",
        illuminantCases: ["d55"]
    ),
    Dataset(
        name: "CIE standard illuminant D65",
        sourceFileName: "CIE_std_illum_D65.csv",
        sourceURL: "https://files.cie.co.at/CIE_std_illum_D65.csv",
        doi: "10.25039/CIE.DS.hjfjmt59",
        expectedMD5: "03d4eb9b837c60671627c946fb534deb",
        expectedSHA256: "e76f210bffff3d552ef7113025da5f325d5dfec200dd4b878b1a2f3a507032cb",
        illuminantCases: ["d65"]
    ),
    Dataset(
        name: "CIE illuminant D75",
        sourceFileName: "CIE_illum_D75.csv",
        sourceURL: "https://files.cie.co.at/CIE_illum_D75.csv",
        doi: "10.25039/CIE.DS.9fvcmrk4",
        expectedMD5: "fc2ae36ece39ad1729696ab2833bd013",
        expectedSHA256: "573a08831f2bcdf18a8e333490049e6d0fabaf1d1c0abbc2a49d6247a939b62d",
        illuminantCases: ["d75"]
    ),
    Dataset(
        name: "CIE indoor daylight illuminant ID50",
        sourceFileName: "CIE_illum_ID50.csv",
        sourceURL: "https://files.cie.co.at/CIE_illum_ID50.csv",
        doi: "10.25039/CIE.DS.r4gcnrzc",
        expectedMD5: "fed40bf7ffc86b054b497f645d9f8fc3",
        expectedSHA256: "70541ed195eb91066d3a986d8d5cdd3cbfb2ab963f713022d2a88dfd59b849e2",
        illuminantCases: ["id50"]
    ),
    Dataset(
        name: "CIE indoor daylight illuminant ID65",
        sourceFileName: "CIE_illum_ID65.csv",
        sourceURL: "https://files.cie.co.at/CIE_illum_ID65.csv",
        doi: "10.25039/CIE.DS.bd53qdqk",
        expectedMD5: "c8843fdca93e747c8a3590cd74b01cfd",
        expectedSHA256: "be1dc615b4b03e0b4b3e294660101526a30ed17dc800e85786c29759b3dcb7a6",
        illuminantCases: ["id65"]
    ),
    Dataset(
        name: "CIE fluorescent lamp illuminants",
        sourceFileName: "CIE_illum_FLs.csv",
        sourceURL: "https://files.cie.co.at/CIE_illum_FLs.csv",
        doi: "10.25039/CIE.DS.ukaymjdn",
        expectedMD5: "441613e501ab0a58e62f2669ff005db7",
        expectedSHA256: "24303adacbfee5e19b2123ea2c3208a993577a054abaf55ea7e19517f98e4d55",
        illuminantCases: [
            "fl1", "fl2", "fl3", "fl4", "fl5", "fl6",
            "fl7", "fl8", "fl9", "fl10", "fl11", "fl12",
            "fl3_1", "fl3_2", "fl3_3", "fl3_4", "fl3_5",
            "fl3_6", "fl3_7", "fl3_8", "fl3_9", "fl3_10",
            "fl3_11", "fl3_12", "fl3_13", "fl3_14", "fl3_15",
        ]
    ),
    Dataset(
        name: "CIE high-pressure discharge lamp illuminants",
        sourceFileName: "CIE_illum_HPs.csv",
        sourceURL: "https://files.cie.co.at/CIE_illum_HPs.csv",
        doi: "10.25039/CIE.DS.f6rvvnev",
        expectedMD5: "423e996fd3ee17eaf783633e4fd2f62c",
        expectedSHA256: "035e09d62b27f4b1e362ff1ba99a50235f0d7886c2781c50d49851b6e4bb4552",
        illuminantCases: ["hp1", "hp2", "hp3", "hp4", "hp5"]
    ),
    Dataset(
        name: "CIE typical LED lamp illuminants",
        sourceFileName: "CIE_illum_LEDs.csv",
        sourceURL: "https://files.cie.co.at/CIE_illum_LEDs.csv",
        doi: "10.25039/CIE.DS.vgssnyfg",
        expectedMD5: "91a73557fe319a085dd598ce96fa8ace",
        expectedSHA256: "6147313bd1ec570ef308df222e1fe1e58115fde1ad0a7d4ba91fb9dcb776b267",
        illuminantCases: [
            "ledB1", "ledB2", "ledB3", "ledB4", "ledB5",
            "ledBH1", "ledRGB1", "ledV1", "ledV2",
        ]
    ),
    Dataset(
        name: "CIE reference spectrum L41",
        sourceFileName: "CIE_RefSpectrum_L41.csv",
        sourceURL: "https://files.cie.co.at/CIE_RefSpectrum_L41.csv",
        doi: "10.25039/CIE.DS.van56dfj",
        expectedMD5: "01dba8b4ebdbd2a3ebb9fa6cc8719939",
        expectedSHA256: "23e07c6b8a2f5273f9e3abb3c64a374f3456eee0a70dd447fdb14c617f55224e",
        illuminantCases: ["l41"]
    ),
]

private let scriptURL = URL(fileURLWithPath: #filePath).standardizedFileURL
private let projectRoot = scriptURL
    .deletingLastPathComponent()
    .deletingLastPathComponent()
private let sourceDirectory = projectRoot
    .appendingPathComponent("ThirdParty/CIE", isDirectory: true)
private let swiftOutputURL = projectRoot
    .appendingPathComponent(
        "IwashiScopePackage/Sources/IwashiScopeFeature/Models/"
            + "CIEStandardIlluminantData.generated.swift"
    )
private let cSharpOutputURL = projectRoot
    .appendingPathComponent(
        "Windows/src/IwashiScope.Core/Calculations/"
            + "CieReferenceIlluminantData.generated.cs"
    )

private func hexadecimalDigest<Digest: Sequence>(_ digest: Digest) -> String
where Digest.Element == UInt8 {
    digest.map { String(format: "%02x", $0) }.joined()
}

private func loadValues(for dataset: Dataset) throws -> [String: [String]] {
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

    var valuesByWavelength: [Int: [String]] = [:]
    for line in text.split(whereSeparator: { $0.isNewline }) {
        let columns = line.split(separator: ",", omittingEmptySubsequences: false)
        guard columns.count == dataset.illuminantCases.count + 1,
              let wavelength = Int(columns[0]),
              columns.dropFirst().allSatisfy({ value in
                  guard let parsed = Double(value) else { return false }
                  return parsed.isFinite
              }) else {
            throw GeneratorError.invalidCSV(file: dataset.sourceFileName, line: String(line))
        }
        valuesByWavelength[wavelength] = columns.dropFirst().map(String.init)
    }

    let selectedRows = try stride(from: 380, through: 730, by: 5).map { wavelength in
        guard let values = valuesByWavelength[wavelength] else {
            throw GeneratorError.missingWavelength(
                file: dataset.sourceFileName,
                wavelength: wavelength
            )
        }
        return values
    }

    return Dictionary(uniqueKeysWithValues: dataset.illuminantCases.enumerated().map {
        columnIndex, illuminantCase in
        (
            illuminantCase,
            selectedRows.map { $0[columnIndex] }
        )
    })
}

private func formattedArray(
    _ values: [String],
    indentation: String
) -> String {
    stride(from: 0, to: values.count, by: 8).map { startIndex in
        let endIndex = min(startIndex + 8, values.count)
        return indentation + values[startIndex..<endIndex].joined(separator: ", ") + ","
    }
    .joined(separator: "\n")
}

private func generatedSource() throws -> String {
    var valuesByIlluminant: [String: [String]] = [:]
    for dataset in datasets {
        valuesByIlluminant.merge(try loadValues(for: dataset)) { _, new in new }
    }

    guard let d50Values = valuesByIlluminant["d50"] else {
        throw GeneratorError.missingIlluminant("D50")
    }
    guard let d65Values = valuesByIlluminant["d65"] else {
        throw GeneratorError.missingIlluminant("D65")
    }

    let sourceLines = datasets.map { "   \($0.name), DOI \($0.doi)" }
        .joined(separator: "\n")
    let referenceEntries = try datasets
        .flatMap(\.illuminantCases)
        .map { illuminantCase -> String in
            if illuminantCase == "d50" {
                return "        .d50: CIEStandardIlluminantData.d50Values,"
            }
            if illuminantCase == "d65" {
                return "        .d65: CIEStandardIlluminantData.d65Values,"
            }
            guard let values = valuesByIlluminant[illuminantCase] else {
                throw GeneratorError.missingIlluminant(illuminantCase)
            }
            return """
                    .\(illuminantCase): [
            \(formattedArray(values, indentation: "            "))
                    ],
            """
        }
        .joined(separator: "\n")

    return """
    /*
     SPDX-FileCopyrightText: 2026 Yamonov
     SPDX-License-Identifier: GPL-3.0-only

     This file is generated by Scripts/generate-cie-standard-illuminants.swift.
     Do not edit it by hand.

     Adapted from CIE datasets:
    \(sourceLines)
     Dataset creator and publisher:
       International Commission on Illumination (CIE), Vienna, AT
     Original dataset license:
       Creative Commons Attribution-ShareAlike 4.0 International
       https://creativecommons.org/licenses/by-sa/4.0/
     Transformation: select 380...730 nm at 5 nm intervals from the official
                     CSV data without numerical interpolation, and express
                     the selected values as Swift arrays.
     Adaptation date: 2026-09-01

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
    \(formattedArray(d50Values, indentation: "        "))
        ]

        static let d65Values: [Double] = [
    \(formattedArray(d65Values, indentation: "        "))
        ]
    }

    enum CIEReferenceIlluminantData {
        static let valuesByIlluminant: [CIEReferenceIlluminant: [Double]] = [
    \(referenceEntries)
        ]
    }

    """
}

private func cSharpCaseName(_ illuminantCase: String) -> String {
    if illuminantCase.hasPrefix("fl3_") {
        return "FL3_" + illuminantCase.dropFirst(4)
    }
    if illuminantCase.hasPrefix("fl")
        || illuminantCase.hasPrefix("hp")
        || illuminantCase == "id50"
        || illuminantCase == "id65"
        || illuminantCase == "d50"
        || illuminantCase == "d55"
        || illuminantCase == "d65"
        || illuminantCase == "d75"
        || illuminantCase == "l41" {
        return illuminantCase.uppercased()
    }
    if illuminantCase.hasPrefix("led") {
        return "LED" + illuminantCase.dropFirst(3)
    }
    return illuminantCase.uppercased()
}

private func generatedCSharpSource() throws -> String {
    var valuesByIlluminant: [String: [String]] = [:]
    for dataset in datasets {
        valuesByIlluminant.merge(try loadValues(for: dataset)) { _, new in new }
    }

    let sourceLines = datasets.map { "   \($0.name), DOI \($0.doi)" }
        .joined(separator: "\n")
    let referenceEntries = try datasets
        .flatMap(\.illuminantCases)
        .map { illuminantCase -> String in
            guard let values = valuesByIlluminant[illuminantCase] else {
                throw GeneratorError.missingIlluminant(illuminantCase)
            }
            return """
                    [CieReferenceIlluminant.\(cSharpCaseName(illuminantCase))] =
                    [
            \(formattedArray(values, indentation: "            "))
                    ],
            """
        }
        .joined(separator: "\n")

    return """
    /*
     SPDX-FileCopyrightText: 2026 Yamonov
     SPDX-License-Identifier: GPL-3.0-only

     This file is generated by Scripts/generate-cie-standard-illuminants.swift.
     Do not edit it by hand.

     Adapted from CIE datasets:
    \(sourceLines)
     Dataset creator and publisher:
       International Commission on Illumination (CIE), Vienna, AT
     Original dataset license:
       Creative Commons Attribution-ShareAlike 4.0 International
       https://creativecommons.org/licenses/by-sa/4.0/
     Transformation: select 380...730 nm at 5 nm intervals from the official
                     CSV data without numerical interpolation, and express
                     the selected values as C# arrays.
     Adaptation date: 2026-09-01

     Yamonov's contributions to this adaptation are licensed under GNU GPL
     version 3 only, a one-way BY-SA Compatible License for adaptations of
     CC BY-SA 4.0 material. Both CC BY-SA 4.0 and GPL-3.0-only apply to the
     adapted material. When combined with IwashiScope, GPL-3.0-only continues
     to govern this file and AGPL-3.0-only continues to govern the IwashiScope
     portions under section 13 of GPLv3 and AGPLv3.

     Full attribution, source files, checksums, transformation details, and
     license references are in ThirdParty/CIE/README.md.
     */

    namespace IwashiScope.Core.Calculations;

    internal static class CieReferenceIlluminantData
    {
        internal static IReadOnlyDictionary<CieReferenceIlluminant, double[]> ValuesByIlluminant { get; } =
            new Dictionary<CieReferenceIlluminant, double[]>
            {
    \(referenceEntries)
            };
    }

    """
}

do {
    let arguments = Array(CommandLine.arguments.dropFirst())
    guard arguments.isEmpty || arguments == ["--check"] else {
        throw GeneratorError.invalidArguments
    }

    let swiftOutput = try generatedSource()
    let cSharpOutput = try generatedCSharpSource()
    if arguments == ["--check"] {
        let existingSwiftOutput = try String(contentsOf: swiftOutputURL, encoding: .utf8)
        guard existingSwiftOutput == swiftOutput else {
            throw GeneratorError.generatedFileOutOfDate(swiftOutputURL.lastPathComponent)
        }
        let existingCSharpOutput = try String(contentsOf: cSharpOutputURL, encoding: .utf8)
        guard existingCSharpOutput == cSharpOutput else {
            throw GeneratorError.generatedFileOutOfDate(cSharpOutputURL.lastPathComponent)
        }
        print("CIE illuminant data is up to date.")
    } else {
        try swiftOutput.write(to: swiftOutputURL, atomically: true, encoding: .utf8)
        try cSharpOutput.write(to: cSharpOutputURL, atomically: true, encoding: .utf8)
        print("Generated \(swiftOutputURL.path)")
        print("Generated \(cSharpOutputURL.path)")
    }
} catch {
    FileHandle.standardError.write(Data("error: \(error)\n".utf8))
    exit(1)
}
