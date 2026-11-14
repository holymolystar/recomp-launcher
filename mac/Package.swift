// swift-tools-version:5.9
// Recomp Launcher for macOS. Build the app with ./build.sh (it runs swift build and wraps the result in a .app).
import PackageDescription

let package = Package(
    name: "RecompLauncher",
    platforms: [.macOS(.v13)],
    targets: [
        .executableTarget(name: "RecompLauncher", path: "Sources/RecompLauncher")
    ]
)
