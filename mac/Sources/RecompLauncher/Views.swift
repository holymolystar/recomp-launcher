// The screens: Home (the installed games in a row), All games (every Mac port), a game's page, and Settings.
import AppKit
import SwiftUI

struct RootView: View {
    @EnvironmentObject var store: Store

    var body: some View {
        let t = store.theme
        ZStack {
            LinearGradient(colors: [t.top, t.bottom], startPoint: .top, endPoint: .bottom).ignoresSafeArea()
            VStack(spacing: 0) {
                TopBar()
                switch store.page {
                case .home: HomeView()
                case .all: AllGamesView()
                case .game: GamePageView()
                }
            }
        }
        .foregroundColor(t.text)
        .preferredColorScheme(t.dark ? .dark : .light)
        .sheet(isPresented: $store.showSettings) { SettingsView().environmentObject(store) }
        .alert("Uninstall?", isPresented: Binding(get: { store.confirmUninstall != nil }, set: { if !$0 { store.confirmUninstall = nil } }),
               presenting: store.confirmUninstall) { g in
            Button("Move to Trash", role: .destructive) { store.uninstall(g); store.confirmUninstall = nil }
            Button("Cancel", role: .cancel) { store.confirmUninstall = nil }
        } message: { g in
            Text("\(g.name)'s folder (\(store.folder(g).path)) goes to the Trash, where you can put it back. Saves the port keeps in your Library folder stay.")
        }
        .alert(store.message ?? "", isPresented: Binding(get: { store.message != nil }, set: { if !$0 { store.message = nil } })) {
            Button("OK") { store.message = nil }
        }
    }
}

struct TopBar: View {
    @EnvironmentObject var store: Store

    var body: some View {
        HStack(spacing: 14) {
            Text("RECOMP LAUNCHER").font(.system(size: 13, weight: .heavy)).tracking(2).foregroundColor(store.theme.muted)
            Spacer()
            if store.page != .home {
                BarButton(symbol: "house", label: "Home") { store.page = .home; store.refreshInstalled() }
            }
            if store.page != .all {
                BarButton(symbol: "square.grid.2x2", label: "All games") { store.showAll() }
            }
            if store.checking {
                ProgressView().controlSize(.small).frame(width: 28)
            } else {
                BarButton(symbol: "arrow.clockwise", label: "Check for updates") { Task { await store.checkReleases() } }
            }
            BarButton(symbol: "gearshape", label: "Settings") { store.showSettings = true }
            TimelineView(.periodic(from: .now, by: 20)) { ctx in
                Text(ctx.date, style: .time).font(.system(size: 15, weight: .semibold)).monospacedDigit()
            }
        }
        .padding(.horizontal, 28)
        .padding(.top, 30)
        .padding(.bottom, 8)
    }
}

struct BarButton: View {
    let symbol: String
    let label: String
    let action: () -> Void
    var body: some View {
        Button(action: action) { Image(systemName: symbol).font(.system(size: 16, weight: .medium)).frame(width: 30, height: 28) }
            .buttonStyle(.plain)
            .help(label)
    }
}

// MARK: - tiles

struct TileView: View {
    @EnvironmentObject var store: Store
    let game: Game
    let size: CGFloat
    var selected = false
    var caption = true

    var body: some View {
        let r = size * 0.08
        ZStack {
            RoundedRectangle(cornerRadius: r)
                .fill(LinearGradient(colors: [Color(hex: game.accent), Color(hex: game.accent2)], startPoint: .topLeading, endPoint: .bottomTrailing))
            face
            if caption {
                VStack {
                    Spacer()
                    Text(game.name)
                        .font(.system(size: max(10.5, size * 0.07), weight: .semibold))
                        .foregroundColor(.white)
                        .lineLimit(2)
                        .shadow(color: .black.opacity(0.7), radius: 2, y: 1)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.horizontal, size * 0.05)
                        .padding(.top, size * 0.12)
                        .padding(.bottom, size * 0.045)
                        .background(LinearGradient(colors: [.black.opacity(0), .black.opacity(0.72), .black.opacity(0.82)],
                                                   startPoint: .top, endPoint: .bottom))
                }
            }
            VStack {
                HStack {
                    Spacer()
                    badge
                }
                Spacer()
            }
            .padding(size * 0.04)
        }
        .frame(width: size, height: size)
        .clipShape(RoundedRectangle(cornerRadius: r))
        .overlay(RoundedRectangle(cornerRadius: r + 5).stroke(store.theme.ring, lineWidth: selected ? 4 : 0).padding(-6))
        .shadow(color: .black.opacity(selected ? 0.45 : 0.25), radius: selected ? 14 : 6, y: 4)
        .animation(.easeOut(duration: 0.15), value: selected)
    }

    @ViewBuilder var face: some View {
        if let ic = store.icon(for: game) {
            if ic.fill {
                Image(nsImage: ic.image).resizable().aspectRatio(contentMode: .fill).frame(width: size, height: size).clipped()
            } else {
                Image(nsImage: ic.image).resizable().aspectRatio(contentMode: .fit).padding(size * 0.14).padding(.bottom, caption ? size * 0.1 : 0)
            }
        } else {
            Text(game.shortName)
                .font(.system(size: size * (game.shortName.count > 4 ? 0.15 : 0.2), weight: .heavy))
                .foregroundColor(.white.opacity(0.95))
                .shadow(color: .black.opacity(0.3), radius: 3, y: 2)
                .padding(.bottom, caption ? size * 0.12 : 0)
        }
    }

    @ViewBuilder var badge: some View {
        if let job = store.jobs[game.id], !job.finished {
            JobBadge(job: job)
        } else if store.running.contains(game.id) {
            Pill(text: "PLAYING", color: Color(hex: "#3FB8A0"))
        } else if store.hasUpdate(game) {
            Pill(text: "UPDATE", color: Color(hex: "#D9B45A"))
        } else if let b = game.blockedBadge {
            Pill(text: b, color: Color(hex: "#5F717C"))
        } else if game.isBuilt && store.installed[game.id] == nil {
            Pill(text: "BUILD IT", color: Color(hex: "#7A5FC2"))
        }
    }
}

struct Pill: View {
    let text: String
    let color: Color
    var body: some View {
        Text(text).font(.system(size: 10, weight: .heavy)).tracking(0.5).foregroundColor(.white)
            .padding(.horizontal, 7).padding(.vertical, 3)
            .background(Capsule().fill(color))
    }
}

struct JobBadge: View {
    @ObservedObject var job: Job
    var body: some View {
        Pill(text: job.fraction.map { "\(Int($0 * 100))%" } ?? "…", color: Color(hex: "#2F7FD8"))
    }
}

// MARK: - home

struct HomeView: View {
    @EnvironmentObject var store: Store

    var body: some View {
        let games = store.homeGames
        VStack(alignment: .leading, spacing: 0) {
            Spacer(minLength: 10)
            if games.isEmpty {
                VStack(alignment: .leading, spacing: 12) {
                    Text("No games installed yet").font(.system(size: 30, weight: .bold))
                    Text("Every port with a Mac version is in All games: pick one there to install it.")
                        .foregroundColor(store.theme.muted)
                    Button("Open All games") { store.showAll() }.controlSize(.large).padding(.top, 6)
                }
                .padding(.horizontal, 56)
            } else {
                ScrollViewReader { proxy in
                    ScrollView(.horizontal, showsIndicators: false) {
                        HStack(alignment: .center, spacing: 24) {
                            ForEach(games) { g in
                                let sel = g.id == store.selected
                                TileView(game: g, size: sel ? 230 : 180, selected: sel)
                                    .id(g.id)
                                    .onTapGesture(count: 2) { store.select(g); store.play(g) }
                                    .onTapGesture { store.select(g) }
                            }
                            ForEach(0..<max(0, 6 - games.count), id: \.self) { _ in
                                RoundedRectangle(cornerRadius: 14).stroke(store.theme.muted.opacity(0.35), style: StrokeStyle(lineWidth: 2, dash: [6, 6]))
                                    .frame(width: 180, height: 180)
                            }
                        }
                        .padding(.horizontal, 56)
                        .padding(.vertical, 30)
                        .animation(.easeOut(duration: 0.15), value: store.selected)
                    }
                    .onAppear { if let s = store.selected { proxy.scrollTo(s, anchor: .center) } }
                    .onChange(of: store.selected) { id in
                        if let id { withAnimation(.easeOut(duration: 0.2)) { proxy.scrollTo(id, anchor: .center) } }
                    }
                }
                if let g = store.selectedGame, games.contains(g) {
                    GameSummary(game: g).padding(.horizontal, 56)
                }
            }
            Spacer(minLength: 10)
            HintBar(hints: games.isEmpty ? ["↑  All games", "⌘,  Settings"]
                                        : ["← →  choose", "Return  play", "↓  game page", "↑  all games"])
        }
    }
}

struct GameSummary: View {
    @EnvironmentObject var store: Store
    let game: Game

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(game.source ?? "").font(.system(size: 12, weight: .bold)).tracking(1.5).foregroundColor(store.theme.muted)
            Text(game.name).font(.system(size: 34, weight: .bold))
            StatusLine(game: game)
            HStack(spacing: 12) {
                if store.installed[game.id] != nil {
                    Button { store.play(game) } label: { Label(store.running.contains(game.id) ? "Playing" : "Play", systemImage: "play.fill").frame(minWidth: 90) }
                        .buttonStyle(.borderedProminent).controlSize(.large).disabled(store.running.contains(game.id))
                    if store.hasUpdate(game) {
                        Button { store.install(game) } label: { Label("Update", systemImage: "arrow.down.circle") }.controlSize(.large).disabled(store.isBusy(game))
                    }
                }
                Button { store.open(game) } label: { Label("Game page", systemImage: "info.circle") }.controlSize(.large)
            }
            .padding(.top, 6)
        }
    }
}

struct StatusLine: View {
    @EnvironmentObject var store: Store
    let game: Game

    var body: some View {
        HStack(spacing: 10) {
            if let job = store.jobs[game.id], !(job.finished && job.failed == nil) {
                JobLine(job: job)
            } else if game.isBuilt {
                if let app = store.launchURL(game) {
                    Text("Built by you  •  \(app.path)").lineLimit(1).truncationMode(.middle)
                } else {
                    Text("Build it yourself: no \(game.name) app found in Applications yet")
                }
            } else if let i = store.installed[game.id] {
                Text("Installed: \(i.tag)")
                if let r = store.releases[game.id], store.hasUpdate(game) {
                    Text("•")
                    Text("\(r.tag) is out").foregroundColor(Color(hex: "#D9B45A"))
                }
            } else if let r = store.releases[game.id] {
                Text("Not installed  •  latest \(r.tag)\(r.prerelease ? " (pre-release)" : "")  •  \(ByteCountFormatter.string(fromByteCount: r.assetSize, countStyle: .file))")
            } else if let e = store.releaseErrors[game.id] {
                Text(e).foregroundColor(Color(hex: "#E06C5B"))
            } else {
                Text("Looking for the latest release…")
            }
        }
        .font(.system(size: 14))
        .foregroundColor(store.theme.muted)
    }
}

struct JobLine: View {
    @EnvironmentObject var store: Store
    @ObservedObject var job: Job

    var body: some View {
        HStack(spacing: 10) {
            if let f = job.failed {
                Text("Failed: \(f)").foregroundColor(Color(hex: "#E06C5B")).lineLimit(3)
            } else if job.finished {
                Text(job.stage)
            } else {
                Text(job.stage)
                if let f = job.fraction {
                    ProgressView(value: f).frame(width: 180)
                    Text("\(Int(f * 100))%").monospacedDigit()
                } else {
                    ProgressView().controlSize(.small)
                }
                if job.stage.hasPrefix("Downloading"), let g = store.game(job.gameId) {
                    Button("Cancel") { store.cancel(g) }.controlSize(.small)
                }
            }
        }
    }
}

struct HintBar: View {
    @EnvironmentObject var store: Store
    let hints: [String]
    var body: some View {
        HStack(spacing: 26) {
            ForEach(hints, id: \.self) { Text($0) }
            Spacer()
            Text("Esc  back")
        }
        .font(.system(size: 12, weight: .medium))
        .foregroundColor(store.theme.muted)
        .padding(.horizontal, 28)
        .padding(.vertical, 14)
        .background(store.theme.panel)
    }
}

// MARK: - all games

struct AllGamesView: View {
    @EnvironmentObject var store: Store
    let tile: CGFloat = 150
    let spacing: CGFloat = 26

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .firstTextBaseline, spacing: 14) {
                Text("All games").font(.system(size: 30, weight: .bold))
                Text("\(store.allGames.count) of \(store.catalog.games.count) with a Mac version").foregroundColor(store.theme.muted)
                Spacer()
                Picker("", selection: $store.filter) {
                    ForEach(Filter.allCases) { Text($0.rawValue).tag($0) }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .frame(width: 400)
            }
            .padding(.horizontal, 40)
            .padding(.top, 10)
            GeometryReader { geo in
                let cols = max(1, Int((geo.size.width - 80 + spacing) / (tile + spacing)))
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVGrid(columns: Array(repeating: GridItem(.fixed(tile), spacing: spacing), count: cols), alignment: .leading, spacing: spacing) {
                            ForEach(store.allGames) { g in
                                VStack(alignment: .leading, spacing: 6) {
                                    TileView(game: g, size: tile, selected: g.id == store.selected)
                                        .opacity(store.installed[g.id] == nil ? 0.82 : 1)
                                    Text(store.installed[g.id] != nil ? (g.isBuilt ? "Built" : "Installed") : (g.isBuilt ? "Build it yourself" : (g.runsHere ? "Not installed" : "Can't run here")))
                                        .font(.system(size: 11, weight: .medium)).foregroundColor(store.theme.muted)
                                }
                                .id(g.id)
                                .contentShape(Rectangle())
                                .onTapGesture { store.open(g) }
                            }
                        }
                        .padding(.horizontal, 40)
                        .padding(.vertical, 24)
                    }
                    .onAppear { store.gridColumns = cols; if let s = store.selected { proxy.scrollTo(s, anchor: .center) } }
                    .onChange(of: cols) { store.gridColumns = $0 }
                    .onChange(of: store.selected) { id in if let id { withAnimation { proxy.scrollTo(id, anchor: .center) } } }
                }
            }
            HintBar(hints: ["← → ↑ ↓  choose", "Return  game page"])
        }
    }
}

// MARK: - a game's page

struct GamePageView: View {
    @EnvironmentObject var store: Store

    var body: some View {
        if let g = store.selectedGame {
            VStack(spacing: 0) {
                ScrollView {
                    HStack(alignment: .top, spacing: 40) {
                        TileView(game: g, size: 250, caption: false)
                        VStack(alignment: .leading, spacing: 12) {
                            Text(g.source ?? "").font(.system(size: 12, weight: .bold)).tracking(1.5).foregroundColor(store.theme.muted)
                            Text(g.name).font(.system(size: 36, weight: .bold))
                            StatusLine(game: g)
                            actions(g).padding(.vertical, 6)
                            section("What you need") { Text(g.needs ?? "").fixedSize(horizontal: false, vertical: true) }
                            if let why = g.blockedText {
                                section("This Mac") { Text(why) }
                            }
                            if let r = store.releases[g.id] {
                                section("\(r.title)\(r.date.map { "  •  " + $0.formatted(date: .abbreviated, time: .omitted) } ?? "")") {
                                    Text(Self.notes(r.notes)).textSelection(.enabled).fixedSize(horizontal: false, vertical: true)
                                }
                            }
                            if g.isBuilt {
                                section("Where the launcher looks") {
                                    Text("For \((g.apps ?? "").replacingOccurrences(of: ";", with: " or ")) in /Applications, ~/Applications and \(store.folder(g).path). Built it somewhere else? Use Find app.")
                                        .textSelection(.enabled).foregroundColor(store.theme.muted).fixedSize(horizontal: false, vertical: true)
                                }
                            } else {
                                section("Where it goes") {
                                    Text(store.folder(g).path).textSelection(.enabled).foregroundColor(store.theme.muted)
                                }
                            }
                        }
                        .frame(maxWidth: 720, alignment: .leading)
                        Spacer(minLength: 0)
                    }
                    .padding(.horizontal, 48)
                    .padding(.vertical, 24)
                }
                HintBar(hints: ["Return  \(primaryLabel(g).lowercased())"])
            }
        } else {
            Color.clear.onAppear { store.page = .all }
        }
    }

    func primaryLabel(_ g: Game) -> String {
        if store.installed[g.id] == nil { return g.isBuilt ? "How to build" : "Install" }
        return store.hasUpdate(g) ? "Update" : "Play"
    }

    @ViewBuilder func actions(_ g: Game) -> some View {
        let inst = store.installed[g.id] != nil
        let busy = store.isBusy(g)
        HStack(spacing: 12) {
            Button { store.primary(g) } label: {
                Label(primaryLabel(g), systemImage: inst && !store.hasUpdate(g) ? "play.fill" : (g.isBuilt ? "hammer.fill" : "arrow.down.circle.fill")).frame(minWidth: 100)
            }
            .buttonStyle(.borderedProminent).controlSize(.large)
            .disabled(busy || (!inst && !g.isBuilt && (store.releases[g.id] == nil || !g.runsHere)) || store.running.contains(g.id))
            if g.isBuilt {
                Button { store.locateApp(g) } label: { Label(inst ? "Choose another app" : "Find app", systemImage: "magnifyingglass") }.controlSize(.large)
            }
            if inst && store.hasUpdate(g) {
                Button { store.play(g) } label: { Label("Play", systemImage: "play.fill") }.controlSize(.large).disabled(store.running.contains(g.id))
            }
            if inst {
                Button { store.revealInFinder(g) } label: { Label("Show in Finder", systemImage: "folder") }.controlSize(.large)
            }
            Button { store.openProject(g) } label: { Label("Project page", systemImage: "safari") }.controlSize(.large)
            if inst && !g.isBuilt {
                Button(role: .destructive) { store.confirmUninstall = g } label: { Label("Uninstall", systemImage: "trash") }
                    .controlSize(.large).disabled(busy || store.running.contains(g.id))
            }
        }
    }

    @ViewBuilder func section<C: View>(_ title: String, @ViewBuilder _ content: () -> C) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(title.uppercased()).font(.system(size: 11, weight: .bold)).tracking(1.2).foregroundColor(store.theme.muted)
            content().font(.system(size: 14))
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 10).fill(store.theme.panel))
    }

    static func notes(_ s: String) -> AttributedString {
        let text = s.isEmpty ? "No release notes." : s
        return (try? AttributedString(markdown: text, options: .init(interpretedSyntax: .inlineOnlyPreservingWhitespace))) ?? AttributedString(text)
    }
}

// MARK: - settings

struct SettingsView: View {
    @EnvironmentObject var store: Store
    @State private var token = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text("Settings").font(.title.bold())
            Form {
                LabeledContent("Games folder") {
                    HStack {
                        Text(store.settings.gamesRoot).lineLimit(1).truncationMode(.middle).frame(maxWidth: 320, alignment: .leading)
                        Button("Choose…") { chooseRoot() }
                    }
                }
                Text("New installs go here, each in its own folder. Games you already installed stay where they are.")
                    .font(.caption).foregroundColor(.secondary)
                Picker("Look", selection: Binding(get: { store.settings.theme }, set: { store.settings.theme = $0; store.settings.save() })) {
                    ForEach(Theme.all, id: \.name) { Text($0.title).tag($0.name) }
                }
                SecureField("GitHub token", text: $token)
                Text("Optional. Without one GitHub allows 60 update checks an hour; a token with no permissions raises that to 5,000.")
                    .font(.caption).foregroundColor(.secondary)
                LabeledContent("Tile pictures") {
                    Button("Open the icons folder") {
                        try? FileManager.default.createDirectory(at: Paths.icons, withIntermediateDirectories: true)
                        NSWorkspace.shared.open(Paths.icons)
                    }
                }
                Text("Put your own picture there as <game id>.png (for example mm.png or oot.png). Without one a tile shows the game's app icon.")
                    .font(.caption).foregroundColor(.secondary)
                LabeledContent("This Mac", value: Machine.isAppleSilicon ? "Apple Silicon" : "Intel")
            }
            .formStyle(.grouped)
            HStack {
                Spacer()
                Button("Done") { save(); store.showSettings = false }.keyboardShortcut(.defaultAction)
            }
        }
        .padding(24)
        .frame(width: 600)
        .onAppear { token = store.settings.gitHubToken }
    }

    func save() {
        let changed = token != store.settings.gitHubToken
        store.settings.gitHubToken = token.trimmingCharacters(in: .whitespacesAndNewlines)
        store.settings.save()
        if changed { Task { await store.checkReleases() } }
    }

    func chooseRoot() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        panel.prompt = "Use this folder"
        panel.directoryURL = URL(fileURLWithPath: (store.settings.gamesRoot as NSString).expandingTildeInPath)
        guard panel.runModal() == .OK, let url = panel.url else { return }
        // the installed games keep their folders
        for g in store.catalog.games where store.installed[g.id] != nil && store.settings.dirs[g.id] == nil {
            store.settings.dirs[g.id] = store.folder(g).path
        }
        store.settings.gamesRoot = url.path
        store.settings.save()
        store.refreshInstalled()
    }
}
