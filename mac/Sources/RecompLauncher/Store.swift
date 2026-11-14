// The launcher's state: the catalog, what is installed, the latest releases, running jobs and where the player is.
import AppKit
import Foundation
import SwiftUI
import UniformTypeIdentifiers

enum Page: Equatable { case home, all, game }
enum Filter: String, CaseIterable, Identifiable {
    case all = "All", installed = "Installed", updates = "Updates", notInstalled = "Not installed"
    var id: String { rawValue }
}

@MainActor
final class Store: ObservableObject {
    @Published var catalog = Catalog.load()
    @Published var settings = LauncherSettings.load()
    @Published var installed: [String: Installed] = [:]
    @Published var releases: [String: Release] = [:]
    @Published var releaseErrors: [String: String] = [:]
    @Published var checking = false
    @Published var jobs: [String: Job] = [:]
    @Published var running: Set<String> = []
    @Published var page: Page = .home
    @Published var previousPage: Page = .home
    @Published var selected: String?
    @Published var filter: Filter = .all
    @Published var showSettings = false
    @Published var confirmUninstall: Game?
    @Published var message: String?
    /// how many tiles fit in a row of All games, set by that view (for up and down)
    var gridColumns = 5
    let input = InputRouter()

    private var processes: [String: Process] = [:]
    private var timer: Timer?
    private var activeObserver: Any?

    init() {
        selected = settings.lastSelected
        refreshInstalled()
        timer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.refreshRunning() }
        }
        // back in the launcher (after building a game, say): look again for what is installed
        activeObserver = NotificationCenter.default.addObserver(forName: NSApplication.didBecomeActiveNotification, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in self?.refreshInstalled() }
        }
        Task { await checkReleases() }
    }

    var theme: Theme { Theme.named(settings.theme) }
    func game(_ id: String?) -> Game? { catalog.games.first { $0.id == id } }
    var selectedGame: Game? { game(selected) }
    func folder(_ g: Game) -> URL { settings.folder(for: g) }
    /// the installed game's .app or program
    func launchURL(_ g: Game) -> URL? {
        guard let i = installed[g.id] else { return nil }
        return i.launch.hasPrefix("/") ? URL(fileURLWithPath: i.launch) : folder(g).appendingPathComponent(i.launch)
    }
    /// a game you build: the app you picked, else one named like it in Applications, ~/Applications or its game folder
    func findBuiltApp(_ g: Game) -> URL? {
        let fm = FileManager.default
        if let p = settings.apps[g.id], fm.fileExists(atPath: p) { return URL(fileURLWithPath: p) }
        guard let pats = g.apps else { return nil }
        let dirs = [URL(fileURLWithPath: "/Applications"), fm.homeDirectoryForCurrentUser.appendingPathComponent("Applications"), folder(g)]
        for d in dirs {
            for item in Installer.visible(d) where item.pathExtension.lowercased() == "app" && Glob.match(pats, item.lastPathComponent) {
                return item
            }
        }
        return nil
    }
    func isBusy(_ g: Game) -> Bool { if let j = jobs[g.id] { return !j.finished } else { return false } }
    func hasUpdate(_ g: Game) -> Bool {
        guard let i = installed[g.id], let r = releases[g.id] else { return false }
        return Versions.isNewer(r, than: i)
    }

    /// the home row: installed games, and one that is being installed
    var homeGames: [Game] { catalog.games.filter { installed[$0.id] != nil || isBusy($0) } }
    var allGames: [Game] {
        catalog.games.filter { g in
            switch filter {
            case .all: return true
            case .installed: return installed[g.id] != nil
            case .updates: return hasUpdate(g)
            case .notInstalled: return installed[g.id] == nil
            }
        }
    }
    /// the games the arrows move through on the current page
    var navGames: [Game] { page == .home ? homeGames : allGames }

    // MARK: state

    func refreshInstalled() {
        var map: [String: Installed] = [:]
        for g in catalog.games {
            if g.isBuilt {
                if let app = findBuiltApp(g) {
                    map[g.id] = Installed(tag: "Built by you", asset: "", assetDate: nil, installed: Date(), launch: app.path)
                }
                continue
            }
            let f = folder(g)
            if let i = Installed.read(f), FileManager.default.fileExists(atPath: f.appendingPathComponent(i.launch).path) { map[g.id] = i }
        }
        installed = map
        iconCache = [:]
        if page == .home, let s = selected, !homeGames.contains(where: { $0.id == s }) { selected = homeGames.first?.id }
        if page == .home, selected == nil { selected = homeGames.first?.id }
    }

    func refreshRunning() {
        var now: Set<String> = []
        let apps = NSWorkspace.shared.runningApplications.compactMap { $0.bundleURL?.standardizedFileURL.path }
        for g in catalog.games {
            guard let target = launchURL(g) else { continue }
            if let p = processes[g.id], p.isRunning { now.insert(g.id); continue }
            if apps.contains(target.standardizedFileURL.path) { now.insert(g.id) }
        }
        if now != running { running = now }
    }

    func checkReleases() async {
        checking = true
        defer { checking = false }
        let token = settings.gitHubToken
        let games = catalog.games.filter { !$0.isBuilt }
        await withTaskGroup(of: (String, Release?, String?).self) { group in
            for g in games {
                group.addTask {
                    do { return (g.id, try await GitHub.latest(for: g, token: token), nil) }
                    catch { return (g.id, nil, error.localizedDescription) }
                }
            }
            for await (id, r, err) in group {
                if let r { releases[id] = r; releaseErrors[id] = nil }
                else { releaseErrors[id] = err ?? "No Mac download in the latest releases." }
            }
        }
    }

    // MARK: actions

    func select(_ g: Game) {
        selected = g.id
        settings.lastSelected = g.id
        settings.save()
    }

    func open(_ g: Game) {
        select(g)
        if page != .game { previousPage = page }
        page = .game
    }

    func back() {
        if showSettings { showSettings = false; return }
        switch page {
        case .game: page = previousPage
        case .all: page = .home; refreshInstalled()
        case .home: break
        }
    }

    func showAll() { previousPage = .home; page = .all }

    func install(_ g: Game) {
        guard !isBusy(g) else { return }
        if g.isBuilt { openBuildSteps(g); return }
        if let why = g.blockedText { message = why; return }
        guard let r = releases[g.id] else { message = releaseErrors[g.id] ?? "The release list hasn't loaded yet."; return }
        let job = Job(gameId: g.id)
        jobs[g.id] = job
        let dest = self.folder(g)
        Task {
            do {
                try await Installer.install(g, r, into: dest, job: job)
                job.stage = "Installed \(r.tag)"
            } catch {
                job.failed = error.localizedDescription
                job.stage = "Failed"
            }
            job.finished = true
            job.fraction = nil
            refreshInstalled()
            objectWillChange.send()
        }
    }

    func cancel(_ g: Game) { jobs[g.id]?.downloader?.cancel() }

    func uninstall(_ g: Game) {
        guard !g.isBuilt else { return }   // an app you built is yours to delete
        do {
            try Installer.uninstall(folder(g))
            jobs[g.id] = nil
            refreshInstalled()
            message = "\(g.name) is in the Trash. Its saves outside the game folder stay where they are."
        } catch { message = "Couldn't remove \(g.name): \(error.localizedDescription)" }
    }

    func play(_ g: Game) {
        guard let target = launchURL(g) else { install(g); return }
        if target.pathExtension.lowercased() == "app" {
            let cfg = NSWorkspace.OpenConfiguration()
            cfg.activates = true
            NSWorkspace.shared.openApplication(at: target, configuration: cfg) { _, error in
                if let error {
                    Task { @MainActor in
                        self.message = "\(g.name) didn't start: \(error.localizedDescription). If macOS says it can't check the app, open it once with Control-click > Open."
                    }
                }
            }
        } else {
            // a plain program: started from its own folder, its output kept in launcher-output.log
            let p = Process()
            p.executableURL = target
            p.currentDirectoryURL = target.deletingLastPathComponent()
            let log = folder(g).appendingPathComponent("launcher-output.log")
            FileManager.default.createFile(atPath: log.path, contents: nil)
            if let h = try? FileHandle(forWritingTo: log) { p.standardOutput = h; p.standardError = h }
            do { try p.run(); processes[g.id] = p } catch { message = "\(g.name) didn't start: \(error.localizedDescription)" }
        }
        Task { try? await Task.sleep(nanoseconds: 1_500_000_000); refreshRunning() }
    }

    /// Return / the A button: play an installed game, otherwise open its page (or install from the page)
    func primary(_ g: Game) {
        if installed[g.id] != nil { if hasUpdate(g) && page == .game { install(g) } else { play(g) } }
        else if page == .game { install(g) }
        else { open(g) }
    }

    func revealInFinder(_ g: Game) {
        let f = g.isBuilt ? (launchURL(g) ?? folder(g)) : folder(g)
        if FileManager.default.fileExists(atPath: f.path) { NSWorkspace.shared.activateFileViewerSelecting([f]) }
    }

    func openBuildSteps(_ g: Game) {
        if let s = g.build, let u = URL(string: s) { NSWorkspace.shared.open(u) }
    }

    /// a game you build: point the launcher at the app, wherever you put it
    func locateApp(_ g: Game) {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.applicationBundle]
        panel.canChooseDirectories = false
        panel.treatsFilePackagesAsDirectories = false
        panel.directoryURL = URL(fileURLWithPath: "/Applications")
        panel.message = "Choose the \(g.name) app you built"
        panel.prompt = "Use this app"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        settings.apps[g.id] = url.path
        settings.save()
        refreshInstalled()
    }

    func openProject(_ g: Game) {
        if let u = URL(string: "https://github.com/\(g.repo)") { NSWorkspace.shared.open(u) }
    }

    /// the player's own picture (fills the tile), else the installed app's icon; looked up once per game
    private var iconCache: [String: (image: NSImage, fill: Bool)?] = [:]
    func icon(for g: Game) -> (image: NSImage, fill: Bool)? {
        if let hit = iconCache[g.id] { return hit }
        var found: (image: NSImage, fill: Bool)?
        for ext in ["png", "jpg", "jpeg"] {
            if let img = NSImage(contentsOf: Paths.icons.appendingPathComponent("\(g.id).\(ext)")) { found = (img, true); break }
        }
        if found == nil, let target = launchURL(g) {
            let p = target.path
            if p.lowercased().hasSuffix(".app") { found = (NSWorkspace.shared.icon(forFile: p), false) }
        }
        iconCache[g.id] = .some(found)
        return found
    }

    // MARK: arrows, Return and Escape (keyboard and controller)

    enum Input { case left, right, up, down, confirm, back, allGames, settings }

    func handle(_ input: Input) {
        if showSettings { if input == .back { showSettings = false }; return }
        if confirmUninstall != nil { if input == .back { confirmUninstall = nil }; return }
        if message != nil, input == .confirm || input == .back { message = nil; return }
        let list = navGames
        let idx = list.firstIndex { $0.id == selected }
        func move(_ d: Int) {
            guard !list.isEmpty else { return }
            let n = max(0, min(list.count - 1, idx.map { $0 + d } ?? 0))
            select(list[n])
        }
        switch (page, input) {
        case (_, .settings): showSettings = true
        case (_, .back): back()
        case (.game, .confirm): if let g = selectedGame { primary(g) }
        case (.game, _): break
        case (.home, .allGames): showAll()
        case (_, .left): move(-1)
        case (_, .right): move(1)
        case (.all, .up): move(-gridColumns)
        case (.all, .down): move(gridColumns)
        case (.home, .down): if let g = selectedGame { open(g) }
        case (.home, .up): showAll()
        case (_, .confirm): if let g = selectedGame { page == .all ? open(g) : primary(g) }
        default: break
        }
    }
}
