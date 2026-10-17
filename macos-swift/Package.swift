// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "MandelSwift",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "MandelSwift", targets: ["MandelSwift"]),
        .executable(name: "SelfCheck", targets: ["SelfCheck"]),
        .executable(name: "MandelCLI", targets: ["MandelCLI"]),
    ],
    targets: [
        .target(name: "MandelCore", path: "source/MandelCore"),
        .executableTarget(name: "MandelSwift", dependencies: ["MandelCore"],
                          path: "source/MandelSwift"),
        .executableTarget(name: "SelfCheck", dependencies: ["MandelCore"],
                          path: "source/SelfCheck"),
        .executableTarget(name: "MandelCLI", dependencies: ["MandelCore"],
                          path: "source/MandelCLI"),
    ]
)
