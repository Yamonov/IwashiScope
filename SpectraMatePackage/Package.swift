// swift-tools-version: 6.1
// The swift-tools-version declares the minimum version of Swift required to build this package.

import Foundation
import PackageDescription

var targets: [Target] = [
    .target(
        name: "SpectraMateFeature"
    ),
]

let packageDirectory = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent()
let localTestsDirectory = packageDirectory
    .appendingPathComponent("Tests/SpectraMateFeatureTests")

if FileManager.default.fileExists(atPath: localTestsDirectory.path) {
    targets.append(
        .testTarget(
            name: "SpectraMateFeatureTests",
            dependencies: [
                "SpectraMateFeature"
            ]
        )
    )
}

let package = Package(
    name: "SpectraMateFeature",
    platforms: [.macOS("14.6")],
    products: [
        // Products define the executables and libraries a package produces, making them visible to other packages.
        .library(
            name: "SpectraMateFeature",
            targets: ["SpectraMateFeature"]
        ),
    ],
    targets: targets,
    swiftLanguageModes: [.v6]
)
