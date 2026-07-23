// swift-tools-version: 6.1
// The swift-tools-version declares the minimum version of Swift required to build this package.

import Foundation
import PackageDescription

var targets: [Target] = [
    .target(
        name: "IwashiScopeFeature"
    ),
]

let packageDirectory = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent()
let localTestsDirectory = packageDirectory
    .appendingPathComponent("Tests/IwashiScopeFeatureTests")

if FileManager.default.fileExists(atPath: localTestsDirectory.path) {
    targets.append(
        .testTarget(
            name: "IwashiScopeFeatureTests",
            dependencies: [
                "IwashiScopeFeature"
            ]
        )
    )
}

let package = Package(
    name: "IwashiScopeFeature",
    platforms: [.macOS("14.6")],
    products: [
        // Products define the executables and libraries a package produces, making them visible to other packages.
        .library(
            name: "IwashiScopeFeature",
            targets: ["IwashiScopeFeature"]
        ),
    ],
    targets: targets,
    swiftLanguageModes: [.v6]
)
