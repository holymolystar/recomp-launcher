// Recomp Launcher for macOS: installs, updates and starts the recompiled and native ports that have a Mac version.
import AppKit
import SwiftUI

@main
struct RecompLauncherApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @StateObject private var store = Store()

    var body: some Scene {
        WindowGroup("Recomp Launcher") {
            RootView()
                .environmentObject(store)
                .frame(minWidth: 980, minHeight: 620)
                .onAppear { store.input.start(store) }
        }
        .windowStyle(.hiddenTitleBar)
        .commands {
            CommandGroup(replacing: .newItem) {}
            CommandGroup(replacing: .appSettings) {
                Button("Settings…") { store.showSettings = true }.keyboardShortcut(",")
            }
            CommandMenu("Games") {
                Button("Home") { store.page = .home; store.refreshInstalled() }.keyboardShortcut("1")
                Button("All games") { store.showAll() }.keyboardShortcut("2")
                Divider()
                Button("Check for updates") { Task { await store.checkReleases() } }.keyboardShortcut("r")
            }
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        // also a normal app with a Dock icon when started straight from `swift run`
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}
