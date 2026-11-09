// Games from the other stores, the same way as Steam's (Steam.cs): a tile each in their own group, installed, started
// and kept up to date by the store, and followed by their folder while they run (play time, the in-game menu).
//  - Epic Games: the installed games from the launcher's manifests (ProgramData\Epic\EpicGamesLauncher\Data\Manifests,
//    *.item), the rest of your library from its catalog cache (Data\Catalog\catcache.bin: base64 JSON). Install and
//    Start hand over to the Epic Games Launcher (com.epicgames.launcher://apps/<namespace>:<item>:<app>?action=...).
//  - GOG: the installed games from the registry (GOG.com\Games: one key per game, with its folder, program and
//    uninstaller); the program is started directly, as GOG's own shortcuts do. Owned games that aren't installed
//    are only known to GOG Galaxy (not read).
// Their artwork is downloaded once from the store (Epic's box art, GOG's box art) to cache\art and shown from there.
// Settings > General > Other stores: "on" (the default) or "off".
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace RecompLauncher {

static class Stores {
    public static Action<Game> ArtReady;   // the Shell: show a picture that just finished downloading
    public static Action<Game> HeroReady;  // the Shell: a game page's background that just finished downloading
    static readonly HashSet<string> fetching = new HashSet<string>();
    static string ArtDir { get { return Path.Combine(Paths.Root, @"cache\art"); } }

    public static void AddLibraries() {
        if (Settings.Get("OtherStores") == "off") return;
        var found = new List<Game>();
        try { found.AddRange(EpicGames.Library()); } catch { }
        try { found.AddRange(GogGames.Installed()); } catch { }
        int rank = 2000;
        foreach (var g in found.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)) {
            if (Catalog.Find(g.Id) != null) continue;
            g.SettingsLabel = g.Name; g.CheckFile = ""; g.Exe = ""; g.Rank = rank++;
            if (!Catalog.SeriesRank.ContainsKey(g.Series)) Catalog.SeriesRank[g.Series] = g.Series == "epic" ? 10 : 11;
            Catalog.Games.Add(g);
        }
    }
    public static Game Tile(string store, string id, string name) {
        string initials = new string(name.Split(new[] { ' ', '-', ':' }, StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetterOrDigit(w[0])).Take(3).Select(w => char.ToUpperInvariant(w[0])).ToArray());
        var g = new Game { Id = store + "-" + Regex.Replace(id, "[^A-Za-z0-9]", ""), Name = name, Short = initials.Length > 0 ? initials : "?", Store = store, StoreId = id, Series = store,
            Accent = store == "epic" ? "#2a2a2a" : "#5c2d91", Accent2 = store == "epic" ? "#5a5a5a" : "#a56de2" };
        g.Source = store == "epic" ? "EPIC GAMES" : "GOG";
        g.Needs = name + " from your " + g.StoreName + " library: the launcher asks " + (store == "epic" ? "the Epic Games Launcher" : "GOG") + " to install it, and it keeps it up to date.";
        return g;
    }

    public static Installed Detect(Game g) {
        if (g.Steam != null) return SteamGames.Detect(g);
        if (g.Store == "epic") return EpicGames.Detect(g);
        if (g.Store == "gog") return GogGames.Detect(g);
        return null;
    }
    public static Release Note(Game g) {
        if (g.Steam != null) return SteamGames.Note(g);
        return new Release { Tag = g.StoreName, Notes = "## Kept up to date by " + g.StoreName + "\n" + g.Name + " is installed and updated through " + g.StoreName + ": the launcher starts it there. Its patch notes are on its page in " + g.StoreName + "." };
    }

    // ---------- artwork ----------
    public static ImageSource Art(Game g) {
        if (g.Steam != null) return SteamGames.Art(g);
        string file = Path.Combine(ArtDir, g.Id + ".jpg");
        if (File.Exists(file) && new FileInfo(file).Length > 0) return Square(file, true);
        Fetch(g, file, g.StoreArt, "boxArtImage", ArtReady);
        return null;
    }
    // the wide picture for the game page's background: Steam's library hero, Epic's wide box art, GOG's Galaxy background
    public static ImageSource Hero(Game g) {
        string file = null;
        if (g.Steam != null) {
            try {
                string dir = Path.Combine(SteamGames.Root, @"appcache\librarycache", g.Steam);
                if (Directory.Exists(dir)) file = Directory.GetFiles(dir, "library_hero.jpg", SearchOption.AllDirectories).FirstOrDefault();
            } catch { }
        } else if (g.Store != null) {
            string f = Path.Combine(ArtDir, g.Id + "-hero.jpg");
            if (File.Exists(f) && new FileInfo(f).Length > 0) file = f;
            else Fetch(g, f, g.StoreHero, "galaxyBackgroundImage", HeroReady);
        }
        if (file == null) return null;
        try {
            var img = new BitmapImage();
            img.BeginInit(); img.UriSource = new Uri(file); img.CacheOption = BitmapCacheOption.OnLoad; img.DecodePixelWidth = 1600; img.EndInit(); img.Freeze();
            return img;
        } catch { return null; }
    }
    // download a store picture once, in the background, to cache\art (GOG: its address from GOG's API, by name)
    static void Fetch(Game g, string file, string url, string gogKey, Action<Game> done) {
        lock (fetching) { if (!fetching.Add(file)) return; }
        ThreadPool.QueueUserWorkItem(_ => {
            try {
                if (url == null && g.Store == "gog") {
                    using (var wc = new WebClient()) {
                        wc.Headers[HttpRequestHeader.UserAgent] = "RecompLauncher";
                        var m = Regex.Match(wc.DownloadString("https://api.gog.com/v2/games/" + g.StoreId), "\"" + gogKey + "\":\\{\"href\":\"([^\"]+)\"");
                        if (m.Success) url = m.Groups[1].Value.Replace("\\/", "/");
                    }
                }
                if (url == null) return;
                Directory.CreateDirectory(ArtDir);
                using (var wc = new WebClient()) { wc.Headers[HttpRequestHeader.UserAgent] = "RecompLauncher"; wc.DownloadFile(url, file + ".part"); }
                if (File.Exists(file)) File.Delete(file);
                File.Move(file + ".part", file);
                if (done != null) done(g);
            } catch { try { File.Delete(file + ".part"); } catch { } }
        });
    }
    // the size the store keeps for an installed game (bytes), or 0 when it doesn't
    public static long KnownSize(Game g) {
        if (g.Steam != null) return SteamGames.SizeOnDisk(g);
        if (g.Store == "epic") return EpicGames.InstallSize(g);
        return 0;
    }
    // a picture as a 256 card face: a tall one keeps its top (where the title is), a wide one its middle
    public static ImageSource Square(string file, bool tall) {
        try {
            var img = new BitmapImage();
            img.BeginInit(); img.UriSource = new Uri(file); img.CacheOption = BitmapCacheOption.OnLoad; img.DecodePixelWidth = 320; img.EndInit(); img.Freeze();
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) {
                var rect = new System.Windows.Rect(0, 0, 256, 256);
                dc.PushClip(new RectangleGeometry(rect, 56, 56));
                double w = img.PixelWidth, h = img.PixelHeight, k = Math.Max(256 / w, 256 / h), dw = w * k, dh = h * k;
                dc.DrawImage(img, new System.Windows.Rect((256 - dw) / 2, tall && h > w ? 0 : (256 - dh) / 2, dw, dh));
                dc.Pop();
            }
            var bmp = new RenderTargetBitmap(256, 256, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv); bmp.Freeze();
            return bmp;
        } catch { return null; }
    }
    public static void Open(string uri) { try { Process.Start(uri); } catch { } }
}

static class EpicGames {
    static string Data { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data"); } }
    static List<Dictionary<string, object>> Manifests() {
        var l = new List<Dictionary<string, object>>();
        try { foreach (var f in Directory.GetFiles(Path.Combine(Data, "Manifests"), "*.item")) { var d = Json.ReadFile(f); if (d != null) l.Add(d); } } catch { }
        return l;
    }
    static bool IsGame(Dictionary<string, object> m) { var c = Json.Strings(m, "AppCategories"); return c.Contains("games") && !c.Contains("addons"); }
    public static List<Game> Library() {
        var r = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in Manifests()) {
            string app = Json.Str(m, "AppName"), name = Json.Str(m, "DisplayName");
            if (app == null || name == null || !IsGame(m) || Json.Bool(m, "bIsIncompleteInstall")) continue;
            var g = Stores.Tile("epic", app, name); g.StoreNs = Json.Str(m, "CatalogNamespace"); g.StoreItem = Json.Str(m, "CatalogItemId");
            r[app] = g;
        }
        // the rest of the library, and the box art for all of them
        try {
            string cat = Path.Combine(Data, @"Catalog\catcache.bin");
            if (File.Exists(cat)) {
                var items = Json.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(File.ReadAllText(cat).Trim()))) as object[];
                foreach (Dictionary<string, object> it in items ?? new object[0]) {
                    object o; var cats = it.TryGetValue("categories", out o) && o is object[] ? ((object[])o).OfType<Dictionary<string, object>>().Select(c => Json.Str(c, "path")).ToList() : new List<string>();
                    if (!cats.Contains("games") || cats.Contains("addons")) continue;
                    var rel = it.TryGetValue("releaseInfo", out o) && o is object[] ? ((object[])o).OfType<Dictionary<string, object>>().FirstOrDefault() : null;
                    string app = rel != null ? Json.Str(rel, "appId") : null, title = Json.Str(it, "title");
                    if (app == null || title == null) continue;
                    title = Regex.Replace(title, @"(?<=\w)\?(?=[\s:]|$)", "™");   // the cache keeps ™ as "?" ("Batman? Arkham City")
                    string art = it.TryGetValue("keyImages", out o) && o is object[] ? ((object[])o).OfType<Dictionary<string, object>>().Where(k => Json.Str(k, "type") == "DieselGameBoxTall").Select(k => Json.Str(k, "url")).FirstOrDefault() : null;
                    Game g;
                    if (!r.TryGetValue(app, out g)) { g = Stores.Tile("epic", app, title); g.StoreNs = Json.Str(it, "namespace"); g.StoreItem = Json.Str(it, "id"); r[app] = g; }
                    if (art != null) g.StoreArt = art + (art.Contains("?") ? "&" : "?") + "h=480&w=360&resize=1";
                    string wide = it.TryGetValue("keyImages", out o) && o is object[] ? ((object[])o).OfType<Dictionary<string, object>>().Where(k => Json.Str(k, "type") == "DieselGameBox").Select(k => Json.Str(k, "url")).FirstOrDefault() : null;
                    if (wide != null) g.StoreHero = wide + (wide.Contains("?") ? "&" : "?") + "w=1600&resize=1";
                }
            }
        } catch { }
        return r.Values.ToList();
    }
    public static Installed Detect(Game g) {
        var m = Manifests().FirstOrDefault(x => string.Equals(Json.Str(x, "AppName"), g.StoreId, StringComparison.OrdinalIgnoreCase));
        if (m == null || Json.Bool(m, "bIsIncompleteInstall")) return null;
        string dir = Json.Str(m, "InstallLocation");
        if (dir == null || !Directory.Exists(dir)) return null;
        g.Dir = dir;
        string ver = Json.Str(m, "AppVersionString");
        return new Installed { Version = "Epic", Tag = ver, Sub = "Epic Games" + (ver != null ? "  •  " + ver : "") };
    }
    public static long InstallSize(Game g) {
        var m = Manifests().FirstOrDefault(x => string.Equals(Json.Str(x, "AppName"), g.StoreId, StringComparison.OrdinalIgnoreCase));
        return m != null ? (long)Json.Num(m, "InstallSize") : 0;
    }
    public static string Uri(Game g, string action) {
        return "com.epicgames.launcher://apps/" + System.Uri.EscapeDataString(g.StoreNs + ":" + g.StoreItem + ":" + g.StoreId) + "?action=" + action + (action == "launch" ? "&silent=true" : "");
    }
}

static class GogGames {
    const string Key = @"SOFTWARE\WOW6432Node\GOG.com\Games";
    public static List<Game> Installed() {
        var l = new List<Game>();
        using (var k = Registry.LocalMachine.OpenSubKey(Key)) {
            if (k == null) return l;
            foreach (var id in k.GetSubKeyNames()) {
                using (var s = k.OpenSubKey(id)) {
                    if (s == null) continue;
                    string name = s.GetValue("gameName") as string, dep = s.GetValue("dependsOn") as string;
                    if (string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(dep)) continue;   // a DLC: part of its game
                    var g = Stores.Tile("gog", id, name);
                    Fill(g, s);
                    l.Add(g);
                }
            }
        }
        return l;
    }
    static void Fill(Game g, RegistryKey s) {
        g.StoreExe = (s.GetValue("launchCommand") as string) ?? (s.GetValue("exe") as string);
        g.StoreArgs = s.GetValue("launchParam") as string;
        g.StoreUninstall = s.GetValue("uninstallCommand") as string;
    }
    public static Installed Detect(Game g) {
        using (var s = Registry.LocalMachine.OpenSubKey(Key + "\\" + g.StoreId)) {
            if (s == null) return null;
            string dir = s.GetValue("path") as string;
            if (dir == null || !Directory.Exists(dir)) return null;
            g.Dir = dir; Fill(g, s);
            string ver = s.GetValue("ver") as string;
            return new Installed { Version = "GOG", Tag = ver, Sub = "GOG" + (string.IsNullOrEmpty(ver) ? "" : "  •  " + ver) };
        }
    }
}

partial class Shell {
    // Install, Start and Uninstall for a game from a store
    void StoreInstall(Game g) {
        if (g.Steam != null) { SteamInstall(g); return; }
        Sound.Play(Sfx.Select);
        if (g.Store == "epic") Stores.Open(EpicGames.Uri(g, "install"));
        else Stores.Open(File.Exists(@"C:\Program Files (x86)\GOG Galaxy\GalaxyClient.exe") ? "goggalaxy://openGameView/" + g.StoreId : "https://www.gog.com/en/account");   // Galaxy, else your games on gog.com
        SetStep(g.StoreName + " is opening " + g.Name + "'s install. The launcher shows it here once it's installed.", Ui.Teal);
        WatchStoreInstall(g);
    }
    // games that run in ScummVM (the Humongous games on Steam: Putt-Putt, Freddi Fish, Pajama Sam, Spy Fox...): turn on
    // its controller support before each start (the stick moves the pointer, A clicks, B right-clicks). Steam rewrites
    // the file when it changes the game's language or checks its files, so it's done every time.
    // Settings > Controller > ScummVM games.
    static void PrepareScummVm(Game g) {
        if (Settings.Get("PadScummVm") == "off" || string.IsNullOrEmpty(g.Dir) || !Directory.Exists(g.Dir)) return;
        try {
            foreach (var dir in new[] { g.Dir, Path.Combine(g.Dir, "ScummVM_Windows") }.Where(Directory.Exists)) {
                if (!File.Exists(Path.Combine(dir, "scummvm.exe"))) continue;
                foreach (var ini in Directory.GetFiles(dir, "scummvm*.ini")) {
                    var lines = File.ReadAllLines(ini).ToList();
                    int sec = lines.FindIndex(l => l.Trim().Equals("[scummvm]", StringComparison.OrdinalIgnoreCase));
                    if (sec < 0) { lines.Insert(0, "[scummvm]"); sec = 0; }
                    int end = lines.FindIndex(sec + 1, l => l.TrimStart().StartsWith("["));
                    if (end < 0) end = lines.Count;
                    int at = lines.FindIndex(sec + 1, end - sec - 1, l => l.TrimStart().StartsWith("joystick_num=", StringComparison.OrdinalIgnoreCase));
                    if (at >= 0 && lines[at].Trim() == "joystick_num=0") continue;
                    if (at >= 0) lines[at] = "joystick_num=0"; else lines.Insert(sec + 1, "joystick_num=0");
                    File.WriteAllLines(ini, lines);
                }
            }
        } catch { }
    }
    void StoreStart(Game g) {
        PrepareScummVm(g);
        if (g.Steam != null) { SteamStart(g); return; }
        if (g.Store == "epic") Stores.Open(EpicGames.Uri(g, "launch"));
        else {
            try {
                var psi = new ProcessStartInfo(g.StoreExe, g.StoreArgs ?? "") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(g.StoreExe) };
                Process.Start(psi);
            } catch (Exception e) { SetStep(g.Name + " didn't start: " + e.Message, Ui.Red); return; }
        }
        SetStep("Starting " + g.Name + (g.Store == "epic" ? " through the Epic Games Launcher..." : "..."), Ui.Teal);
        if (Directory.Exists(g.Dir)) Engine.StartPlaytime(g, g.Dir);
        WatchGame(g);
        if (!running) { string mode = OnPlayMode(); if (mode == "close") CloseLauncher(); else if (mode == "tray") WaitInTray(g); }
    }
    void StoreUninstall(Game g) {
        if (g.Steam != null) { SteamGames.Open("uninstall", g); SetStep("Steam asks to uninstall " + g.Name + ".", Ui.Muted); return; }
        if (g.Store == "gog" && !string.IsNullOrEmpty(g.StoreUninstall) && File.Exists(g.StoreUninstall)) {
            try { Process.Start(new ProcessStartInfo(g.StoreUninstall) { UseShellExecute = true }); } catch { }
            SetStep("GOG's uninstaller for " + g.Name + " is open.", Ui.Muted);
        } else {
            Stores.Open("com.epicgames.launcher://store/library");
            SetStep("In the Epic Games Launcher's library: the three dots on " + g.Name + ", then Uninstall.", Ui.Muted);
        }
        WatchStoreInstall(g);
    }
    // while a store installs or uninstalls: look every 5 seconds and show the change
    DispatcherTimerLite storeWatch;
    void WatchStoreInstall(Game g) {
        if (storeWatch == null) storeWatch = new DispatcherTimerLite(this);
        storeWatch.Add(g);
    }
    class DispatcherTimerLite {
        readonly Shell sh; readonly Dictionary<Game, bool> games = new Dictionary<Game, bool>(); readonly System.Windows.Threading.DispatcherTimer t; int ticks;
        public DispatcherTimerLite(Shell s) {
            sh = s;
            t = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
            t.Tick += (o, e) => Tick();
        }
        public void Add(Game g) { games[g] = g.Inst != null; ticks = 0; t.Start(); }
        void Tick() {
            foreach (var kv in games.ToList()) {
                var g = kv.Key; var inst = Stores.Detect(g);
                if ((inst != null) == kv.Value) continue;
                games.Remove(g);
                g.Inst = inst; g.State = inst != null ? State.Current : State.Missing; g.Latest = Stores.Note(g);
                Icons.Load(g); sh.ApplyIcon(g);
                sh.Log(g.Name + (inst != null ? " is installed" : " is uninstalled") + " (through " + g.StoreName + ").", Ui.Teal);
                sh.SetStep(g.Name + (inst != null ? " is installed: press Start." : " is uninstalled."), Ui.Teal);
                sh.ApplySort(true); sh.ApplyFilter(); sh.UpdateViews();
            }
            // an install takes as long as it takes; stop looking after 3 hours
            if (games.Count == 0 || ++ticks > 2160) { games.Clear(); t.Stop(); }
        }
    }
}
}
