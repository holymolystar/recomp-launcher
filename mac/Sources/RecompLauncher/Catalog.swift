// The game list (games-mac.json), where things live on disk, the player's settings, and version comparison.
import AppKit
import Darwin
import Foundation

struct Game: Codable, Identifiable, Hashable {
    var id: String
    var name: String
    var short: String?
    var source: String?
    var repo: String
    var accent: String?
    var accent2: String?
    var series: String?
    var needs: String?
    /// download name pattern for Apple Silicon (or a universal build); several patterns are separated by ";"
    var asset: String?
    /// download name pattern for Intel Macs, when the project publishes a separate one
    var assetIntel: String?
    /// the project publishes only an Apple Silicon build
    var appleSiliconOnly: Bool?
    /// the Mac build is only published as a pre-release (a devel or beta build)
    var pre: Bool?
    /// a release that is a plain program rather than a .app: its file name in the release
    var exe: String?
    /// a game you build yourself from your own dump (no download): the page with the build steps
    var build: String?
    /// for a game you build: the name patterns of the app it makes, looked for in Applications and the game folder
    var apps: String?
    /// the oldest macOS the port runs on (26 for macOS 26)
    var minMacOS: Int?

    enum CodingKeys: String, CodingKey {
        case id = "Id", name = "Name", short = "Short", source = "Source", repo = "Repo", accent = "Accent", accent2 = "Accent2"
        case series = "Series", needs = "Needs", asset = "Asset", assetIntel = "AssetIntel", appleSiliconOnly = "AppleSiliconOnly"
        case pre = "Pre", exe = "Exe", build = "Build", apps = "Apps", minMacOS = "MinMacOS"
    }

    var shortName: String { short ?? String(name.prefix(4)).uppercased() }
    var isBuilt: Bool { build != nil }
    var runsHere: Bool { blockedText == nil }
    /// why it can't run on this Mac, or nil
    var blockedText: String? {
        if (appleSiliconOnly ?? false) && !Machine.isAppleSilicon { return "\(name) only runs on Apple Silicon Macs (M1 and newer)." }
        if let v = minMacOS, Machine.macOS < v { return "\(name) needs macOS \(v) or newer." }
        return nil
    }
    /// the same, as a tile badge
    var blockedBadge: String? {
        if (appleSiliconOnly ?? false) && !Machine.isAppleSilicon { return "APPLE SILICON" }
        if let v = minMacOS, Machine.macOS < v { return "MACOS \(v)" }
        return nil
    }
    /// the download pattern for this Mac
    var assetPattern: String { (!Machine.isAppleSilicon && assetIntel != nil) ? assetIntel! : (asset ?? "") }
}

struct Catalog: Codable {
    var seriesRank: [String: Int]
    var order: [String]
    var games: [Game]
    enum CodingKeys: String, CodingKey { case seriesRank = "SeriesRank", order = "Order", games = "Games" }

    /// the copy in the app, or a newer games-mac.json the player put in the support folder
    static func load() -> Catalog {
        let candidates = [Paths.support.appendingPathComponent("games-mac.json"),
                          Bundle.main.url(forResource: "games-mac", withExtension: "json"),
                          URL(fileURLWithPath: CommandLine.arguments[0]).deletingLastPathComponent().appendingPathComponent("games-mac.json"),
                          URL(fileURLWithPath: FileManager.default.currentDirectoryPath).appendingPathComponent("games-mac.json")]
        for case let url? in candidates {
            if let data = try? Data(contentsOf: url), let c = try? JSONDecoder().decode(Catalog.self, from: data) {
                return c.sorted()
            }
        }
        return Catalog(seriesRank: [:], order: [], games: [])
    }

    func sorted() -> Catalog {
        var c = self
        let pos = Dictionary(uniqueKeysWithValues: order.enumerated().map { ($1, $0) })
        c.games.sort { (pos[$0.id] ?? Int.max, $0.name) < (pos[$1.id] ?? Int.max, $1.name) }
        return c
    }
}

enum Machine {
    static let macOS = ProcessInfo.processInfo.operatingSystemVersion.majorVersion
    static let isAppleSilicon: Bool = {
        var v: Int32 = 0
        var size = MemoryLayout<Int32>.size
        return sysctlbyname("hw.optional.arm64", &v, &size, nil, 0) == 0 && v == 1
    }()
}

enum Paths {
    static let fm = FileManager.default
    static let support: URL = {
        let u = fm.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("Recomp Launcher")
        try? fm.createDirectory(at: u, withIntermediateDirectories: true)
        return u
    }()
    static var icons: URL { support.appendingPathComponent("icons") }
    static var work: URL { support.appendingPathComponent("work") }
    static var defaultGamesRoot: URL { fm.homeDirectoryForCurrentUser.appendingPathComponent("Games") }
    /// a folder name made from a game's name ("Majora's Mask (2 Ship)" stays, "Pokémon: X/Y" loses : and /)
    static func folderName(_ name: String) -> String {
        name.replacingOccurrences(of: "/", with: "-").replacingOccurrences(of: ":", with: "").trimmingCharacters(in: .whitespaces)
    }
}

struct LauncherSettings: Codable {
    var gamesRoot: String = Paths.defaultGamesRoot.path
    var dirs: [String: String] = [:]
    /// for games you build: the app you picked with Find app
    var apps: [String: String] = [:]
    var gitHubToken: String = ""
    var lastSelected: String?
    var theme: String = "midnight"

    init() {}
    // every field may be missing (a settings file from an older version)
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        gamesRoot = try c.decodeIfPresent(String.self, forKey: .gamesRoot) ?? gamesRoot
        dirs = try c.decodeIfPresent([String: String].self, forKey: .dirs) ?? [:]
        apps = try c.decodeIfPresent([String: String].self, forKey: .apps) ?? [:]
        gitHubToken = try c.decodeIfPresent(String.self, forKey: .gitHubToken) ?? ""
        lastSelected = try c.decodeIfPresent(String.self, forKey: .lastSelected)
        theme = try c.decodeIfPresent(String.self, forKey: .theme) ?? theme
    }

    static var file: URL { Paths.support.appendingPathComponent("settings.json") }
    static func load() -> LauncherSettings {
        guard let data = try? Data(contentsOf: file), let s = try? JSONDecoder().decode(LauncherSettings.self, from: data) else { return LauncherSettings() }
        return s
    }
    func save() {
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        if let data = try? enc.encode(self) { try? data.write(to: LauncherSettings.file, options: .atomic) }
    }
    func folder(for g: Game) -> URL {
        if let d = dirs[g.id], !d.isEmpty { return URL(fileURLWithPath: d) }
        return URL(fileURLWithPath: (gamesRoot as NSString).expandingTildeInPath).appendingPathComponent(Paths.folderName(g.name))
    }
}

/// what the launcher wrote into a game folder when it installed it (recomp-launcher.json)
struct Installed: Codable {
    var tag: String
    var asset: String
    var assetDate: Date?
    var installed: Date
    /// the .app or program to start, relative to the game folder
    var launch: String

    static let fileName = "recomp-launcher.json"
    static func read(_ folder: URL) -> Installed? {
        guard let data = try? Data(contentsOf: folder.appendingPathComponent(fileName)) else { return nil }
        let dec = JSONDecoder(); dec.dateDecodingStrategy = .iso8601
        return try? dec.decode(Installed.self, from: data)
    }
    func write(_ folder: URL) throws {
        let enc = JSONEncoder(); enc.dateEncodingStrategy = .iso8601; enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        try enc.encode(self).write(to: folder.appendingPathComponent(Installed.fileName), options: .atomic)
    }
}

/// a release of a game's project, with the download for this Mac
struct Release: Equatable {
    var tag: String
    var title: String
    var date: Date?
    var notes: String
    var assetName: String
    var assetURL: URL
    var assetSize: Int64
    var assetDate: Date?
    var prerelease: Bool
}

enum Versions {
    /// "v0.4.1-alpha.4", "v.0.9.5", "0.3.7+0f65b4e" -> [0,4,1]; a date tag "2026-10-23-..." -> [2026,10,23]; nil for names
    static func parse(_ s: String) -> [Int]? {
        if let m = s.range(of: #"^v?(\d{4})-(\d{2})-(\d{2})(?!\d)"#, options: .regularExpression) {
            return s[m].replacingOccurrences(of: "v", with: "").split(separator: "-").compactMap { Int($0) }
        }
        var t = s.replacingOccurrences(of: #"^[^\d]*"#, with: "", options: .regularExpression)
        t = t.replacingOccurrences(of: #"[+-].*$"#, with: "", options: .regularExpression)
        while t.hasSuffix(".") { t.removeLast() }
        guard t.range(of: #"^\d+(\.\d+){0,3}$"#, options: .regularExpression) != nil else { return nil }
        return t.split(separator: ".").compactMap { Int($0) }
    }
    static func less(_ a: [Int], _ b: [Int]) -> Bool {
        for i in 0..<max(a.count, b.count) {
            let x = i < a.count ? a[i] : 0, y = i < b.count ? b[i] : 0
            if x != y { return x < y }
        }
        return false
    }
    /// Numbered releases compare as numbers. Builds named "continuous", "beta" or "devel-build" are rebuilt under the
    /// same name, so those are newer when their download is newer than the installed one.
    static func isNewer(_ r: Release, than inst: Installed) -> Bool {
        if let a = parse(r.tag), let b = parse(inst.tag), a != b { return less(b, a) }
        if let d = r.assetDate, let i = inst.assetDate { return d > i.addingTimeInterval(60) }
        return r.assetName != inst.asset || r.tag != inst.tag
    }
}

enum Glob {
    /// a file name matches one of the ";"-separated patterns (case doesn't matter)
    static func match(_ patterns: String, _ name: String) -> Bool {
        patterns.split(separator: ";").map { $0.trimmingCharacters(in: .whitespaces) }.contains { p in
            !p.isEmpty && fnmatch(p, name, FNM_CASEFOLD) == 0
        }
    }
}

extension NSColor {
    convenience init(hex: String?) {
        var s = (hex ?? "#3A4A57").trimmingCharacters(in: .whitespaces)
        if s.hasPrefix("#") { s.removeFirst() }
        let v = UInt64(s, radix: 16) ?? 0x3A4A57
        self.init(srgbRed: CGFloat((v >> 16) & 0xFF) / 255, green: CGFloat((v >> 8) & 0xFF) / 255, blue: CGFloat(v & 0xFF) / 255, alpha: 1)
    }
}
