// GitHub releases, downloads, unpacking (.zip, .dmg, .tar.gz, nested ones too) and putting a release in place.
import AppKit
import Foundation

enum LauncherError: LocalizedError {
    case message(String)
    var errorDescription: String? { if case .message(let m) = self { return m }; return nil }
}

// MARK: - GitHub

enum GitHub {
    private struct GHRelease: Decodable {
        let tag_name: String
        let name: String?
        let body: String?
        let prerelease: Bool
        let draft: Bool
        let published_at: String?
        let assets: [GHAsset]
    }
    private struct GHAsset: Decodable {
        let name: String
        let browser_download_url: String
        let size: Int64
        let updated_at: String?
    }
    private static let iso: ISO8601DateFormatter = ISO8601DateFormatter()

    /// the newest release of the game's project that has a download for this Mac
    static func latest(for g: Game, token: String) async throws -> Release? {
        if g.isBuilt || g.asset == nil { return nil }
        var req = URLRequest(url: URL(string: "https://api.github.com/repos/\(g.repo)/releases?per_page=20")!)
        req.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        req.setValue("Recomp-Launcher-macOS", forHTTPHeaderField: "User-Agent")
        if !token.isEmpty { req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization") }
        req.timeoutInterval = 30
        let (data, resp) = try await URLSession.shared.data(for: req)
        let code = (resp as? HTTPURLResponse)?.statusCode ?? 0
        if code == 403 || code == 429 {
            throw LauncherError.message("GitHub's limit for checks without a token is used up. Add a GitHub token in Settings, or try again in an hour.")
        }
        guard code == 200 else { throw LauncherError.message("GitHub answered \(code) for \(g.repo).") }
        let list = try JSONDecoder().decode([GHRelease].self, from: data)
        let pattern = g.assetPattern
        for r in list where !r.draft && (!r.prerelease || (g.pre ?? false)) {
            guard let a = r.assets.first(where: { Glob.match(pattern, $0.name) }), let url = URL(string: a.browser_download_url) else { continue }
            return Release(tag: r.tag_name, title: r.name ?? r.tag_name, date: r.published_at.flatMap { iso.date(from: $0) },
                           notes: r.body ?? "", assetName: a.name, assetURL: url, assetSize: a.size,
                           assetDate: a.updated_at.flatMap { iso.date(from: $0) }, prerelease: r.prerelease)
        }
        return nil
    }
}

// MARK: - running programs

enum Shell {
    /// runs a program and waits; throws with its output when it fails
    @discardableResult
    static func run(_ path: String, _ args: [String], input: FileHandle? = nil) throws -> String {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: path)
        p.arguments = args
        let pipe = Pipe()
        p.standardOutput = pipe
        p.standardError = pipe
        p.standardInput = input ?? FileHandle.nullDevice
        try p.run()
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        let out = String(data: data, encoding: .utf8) ?? ""
        if p.terminationStatus != 0 {
            throw LauncherError.message("\((path as NSString).lastPathComponent) failed: \(out.trimmingCharacters(in: .whitespacesAndNewlines))")
        }
        return out
    }
}

// MARK: - downloads

final class Downloader: NSObject, URLSessionDownloadDelegate {
    private let dest: URL
    private let onProgress: (Int64, Int64) -> Void
    private var cont: CheckedContinuation<URL, Error>?
    private var session: URLSession?
    private var task: URLSessionDownloadTask?

    init(dest: URL, onProgress: @escaping (Int64, Int64) -> Void) {
        self.dest = dest
        self.onProgress = onProgress
    }

    func start(_ url: URL) async throws -> URL {
        try await withCheckedThrowingContinuation { (c: CheckedContinuation<URL, Error>) in
            cont = c
            var req = URLRequest(url: url)
            req.setValue("Recomp-Launcher-macOS", forHTTPHeaderField: "User-Agent")
            let s = URLSession(configuration: .default, delegate: self, delegateQueue: nil)
            session = s
            task = s.downloadTask(with: req)
            task?.resume()
        }
    }

    func cancel() { task?.cancel() }

    private func finish(_ r: Result<URL, Error>) {
        guard let c = cont else { return }
        cont = nil
        session?.finishTasksAndInvalidate()
        c.resume(with: r)
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didWriteData bytesWritten: Int64,
                    totalBytesWritten: Int64, totalBytesExpectedToWrite: Int64) {
        onProgress(totalBytesWritten, totalBytesExpectedToWrite)
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {
        // the file at location is gone once this returns, so it is moved now
        if let code = (downloadTask.response as? HTTPURLResponse)?.statusCode, code != 200 {
            finish(.failure(LauncherError.message("The download answered \(code).")))
            return
        }
        do {
            try? FileManager.default.removeItem(at: dest)
            try FileManager.default.moveItem(at: location, to: dest)
            finish(.success(dest))
        } catch { finish(.failure(error)) }
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        if let error { finish(.failure(error)) }
    }
}

// MARK: - installing

/// one install or update, shown on the game's tile and page
@MainActor
final class Job: ObservableObject {
    let gameId: String
    @Published var stage = "Starting"
    @Published var fraction: Double?
    @Published var failed: String?
    @Published var finished = false
    var downloader: Downloader?
    init(gameId: String) { self.gameId = gameId }
}

enum Installer {
    static let fm = FileManager.default
    static let archiveExts = ["zip", "dmg", "tgz", "gz", "tar", "xz"]

    /// Downloads the release, unpacks it and puts it into the game folder. On an update the .app bundles are replaced
    /// whole; every other file in the release is copied over, and files the release doesn't have (saves, settings,
    /// the game files a port built on its first start) stay.
    static func install(_ g: Game, _ r: Release, into folder: URL, job: Job) async throws {
        let work = Paths.work.appendingPathComponent(g.id)
        try? fm.removeItem(at: work)
        try fm.createDirectory(at: work, withIntermediateDirectories: true)
        defer { try? fm.removeItem(at: work) }

        await MainActor.run { job.stage = "Downloading \(r.tag)"; job.fraction = 0 }
        let file = work.appendingPathComponent(r.assetName)
        let size = r.assetSize
        let dl = Downloader(dest: file) { done, total in
            let t = total > 0 ? total : size
            Task { @MainActor in job.fraction = t > 0 ? Double(done) / Double(t) : nil }
        }
        await MainActor.run { job.downloader = dl }
        _ = try await dl.start(r.assetURL)
        let got = (try? fm.attributesOfItem(atPath: file.path)[.size] as? Int64) ?? 0
        if size > 0 && got != size { throw LauncherError.message("The download is incomplete (\(got) of \(size) bytes).") }

        await MainActor.run { job.stage = "Unpacking"; job.fraction = nil }
        let placed = try await Task.detached(priority: .userInitiated) { () throws -> String in
            let x = work.appendingPathComponent("x")
            try Installer.fm.createDirectory(at: x, withIntermediateDirectories: true)
            try Installer.expand(file, into: x, exe: g.exe)
            // a zip that holds another zip or a disk image (Zelda 64, DK64, Ship of Harkinian): open that too
            for _ in 0..<3 {
                if Installer.findLaunch(in: x, exe: g.exe) != nil { break }
                guard let inner = Installer.visible(x).first(where: { Installer.archiveExts.contains($0.pathExtension.lowercased()) }) else { break }
                try Installer.expand(inner, into: x, exe: nil)
                try? Installer.fm.removeItem(at: inner)
            }
            var root = x
            while true {
                let items = Installer.visible(root)
                if items.count == 1, Installer.isDir(items[0]), items[0].pathExtension.lowercased() != "app" { root = items[0] } else { break }
            }
            guard let launch = Installer.findLaunch(in: root, exe: g.exe) else {
                throw LauncherError.message(g.exe.map { "The download does not contain \($0)." } ?? "The download does not contain a Mac app.")
            }
            try Installer.place(root, into: folder)
            if g.exe != nil { _ = try? Shell.run("/bin/chmod", ["+x", folder.appendingPathComponent(launch).path]) }
            return launch
        }.value

        await MainActor.run { job.stage = "Finishing" }
        try Installed(tag: r.tag, asset: r.assetName, assetDate: r.assetDate ?? r.date, installed: Date(), launch: placed).write(folder)
    }

    static func visible(_ dir: URL) -> [URL] {
        let items = (try? fm.contentsOfDirectory(at: dir, includingPropertiesForKeys: [.isDirectoryKey], options: [.skipsHiddenFiles])) ?? []
        return items.filter { $0.lastPathComponent != "__MACOSX" }.sorted { $0.lastPathComponent < $1.lastPathComponent }
    }

    static func isDir(_ u: URL) -> Bool {
        var d: ObjCBool = false
        return fm.fileExists(atPath: u.path, isDirectory: &d) && d.boolValue
    }

    /// the .app (or the plain program named exe) in the release, one or two folders deep, relative to dir
    static func findLaunch(in dir: URL, exe: String?) -> String? {
        var level = [dir]
        for _ in 0..<3 {
            var next: [URL] = []
            for d in level {
                for item in visible(d) {
                    let isApp = item.pathExtension.lowercased() == "app"
                    if let exe, item.lastPathComponent == exe, !isDir(item) { return relative(item, to: dir) }
                    if exe == nil, isApp, !item.lastPathComponent.lowercased().contains("uninstall") { return relative(item, to: dir) }
                    if isDir(item), !isApp { next.append(item) }
                }
            }
            level = next
        }
        return nil
    }

    static func relative(_ item: URL, to dir: URL) -> String {
        let base = dir.standardizedFileURL.path.hasSuffix("/") ? dir.standardizedFileURL.path : dir.standardizedFileURL.path + "/"
        let p = item.standardizedFileURL.path
        return p.hasPrefix(base) ? String(p.dropFirst(base.count)) : item.lastPathComponent
    }

    static func expand(_ file: URL, into dir: URL, exe: String?) throws {
        let name = file.lastPathComponent.lowercased()
        if name.hasSuffix(".zip") {
            try Shell.run("/usr/bin/ditto", ["-x", "-k", file.path, dir.path])
        } else if name.hasSuffix(".dmg") {
            let mnt = Paths.work.appendingPathComponent("mnt-\(UUID().uuidString.prefix(8))")
            try fm.createDirectory(at: mnt, withIntermediateDirectories: true)
            do {
                try Shell.run("/usr/bin/hdiutil", ["attach", "-nobrowse", "-readonly", "-noautoopen", "-mountpoint", mnt.path, file.path])
            } catch {
                throw LauncherError.message("The disk image didn't open. If it shows a licence to accept, open it yourself in Finder once. (\(error.localizedDescription))")
            }
            defer {
                _ = try? Shell.run("/usr/bin/hdiutil", ["detach", mnt.path, "-force"])
                try? fm.removeItem(at: mnt)
            }
            for item in visible(mnt) {
                // the "drag here" shortcut to /Applications isn't part of the game
                if let dest = try? fm.destinationOfSymbolicLink(atPath: item.path), dest.hasPrefix("/Applications") { continue }
                try Shell.run("/usr/bin/ditto", [item.path, dir.appendingPathComponent(item.lastPathComponent).path])
            }
        } else if name.hasSuffix(".tar.gz") || name.hasSuffix(".tgz") || name.hasSuffix(".tar") || name.hasSuffix(".tar.xz") {
            try Shell.run("/usr/bin/tar", ["-xf", file.path, "-C", dir.path])
        } else {
            // a release that is a single program
            let target = dir.appendingPathComponent(exe ?? file.lastPathComponent)
            try fm.moveItem(at: file, to: target)
            try Shell.run("/bin/chmod", ["+x", target.path])
        }
    }

    /// copies the release over the game folder; .app bundles in it replace the old ones whole
    static func place(_ root: URL, into folder: URL) throws {
        try fm.createDirectory(at: folder, withIntermediateDirectories: true)
        for item in visible(root) where item.pathExtension.lowercased() == "app" {
            let old = folder.appendingPathComponent(item.lastPathComponent)
            if fm.fileExists(atPath: old.path) { try fm.removeItem(at: old) }
        }
        try Shell.run("/usr/bin/ditto", [root.path, folder.path])
    }

    /// puts the game folder in the Trash (it can be put back from there)
    static func uninstall(_ folder: URL) throws {
        guard fm.fileExists(atPath: folder.path) else { return }
        try fm.trashItem(at: folder, resultingItemURL: nil)
    }
}
