// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "MandelSwift",
    platforms: [.macOS(.v14)],
    products: [.executable(name: "MandelSwift", targets: ["MandelSwift"])],
    targets: [
        .executableTarget(name: "MandelSwift"),
        .testTarget(name: "MandelSwiftTests", dependencies: ["MandelSwift"]),
    ]
)
