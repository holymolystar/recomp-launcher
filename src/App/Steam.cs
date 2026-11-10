// Steam games in the launcher (games.json "Steam": the app id): bought on Steam, installed, started and kept up to date
// by Steam. The launcher reads Steam's own files: steamapps\libraryfolders.vdf for the libraries and
// appmanifest_<id>.acf for whether a game is installed, where, its build and its language. Install, Start and Uninstall
// hand over to Steam (steam://install, rungameid, uninstall); while Steam installs, the launcher looks every 5 seconds
// and says when the game is ready. The tiles use the artwork Steam already keeps (appcache\librarycache).
// Language (games.json "Languages"): Steam picks a game's language in its Properties and downloads its files; the
// game page shows the current one and opens Steam's Properties to change it.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace RecompLauncher {

static class SteamGames {
    static string root;
    public static string Root {
        get {
            if (root != null) return root;
            try { root = (Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string ?? "").Replace('/', '\\'); } catch { root = ""; }
            return root;
        }
    }
    static List<string> Libraries() {
        var l = new List<string>();
        if (Root.Length == 0) return l;
        l.Add(Path.Combine(Root, "steamapps"));
        try {
            string vdf = Path.Combine(Root, @"steamapps\libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    l.Add(Path.Combine(m.Groups[1].Value.Replace(@"\\", @"\"), "steamapps"));
        } catch { }
        return l.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    // a game's appmanifest values (StateFlags, installdir, buildid, language) and its library, or null
    static Dictionary<string, string> Manifest(Game g, out string lib) {
        lib = null;
        foreach (var l in Libraries()) {
            string f = Path.Combine(l, "appmanifest_" + g.Steam + ".acf");
            if (!File.Exists(f)) continue;
            try {
                string t = File.ReadAllText(f);
                var d = new Dictionary<string, string>();
                foreach (var k in new[] { "StateFlags", "installdir", "buildid", "LastUpdated", "SizeOnDisk" }) { var m = Regex.Match(t, "\"" + k + "\"\\s+\"([^\"]*)\""); if (m.Success) d[k] = m.Groups[1].Value; }
                var lang = Regex.Match(t, "\"UserConfig\"\\s*\\{[^}]*\"language\"\\s+\"([^\"]*)\"");
                if (lang.Success) d["language"] = lang.Groups[1].Value;
                lib = l; return d;
            } catch { }
        }
        return null;
    }
    public static long SizeOnDisk(Game g) {
        string lib; var m = Manifest(g, out lib); string v; long n;
        return m != null && m.TryGetValue("SizeOnDisk", out v) && long.TryParse(v, out n) ? n : 0;
    }
    public static string InstallDir(Game g) {
        string lib; var m = Manifest(g, out lib);
        string dir;
        return m != null && m.TryGetValue("installdir", out dir) ? Path.Combine(lib, "common", dir) : null;
    }
    // installed (fully: Steam's flag 4) and its folder there
    public static Installed Detect(Game g) {
        string lib; var m = Manifest(g, out lib);
        if (m == null) return null;
        int flags; int.TryParse(m.ContainsKey("StateFlags") ? m["StateFlags"] : "0", out flags);
        string dir = m.ContainsKey("installdir") ? Path.Combine(lib, "common", m["installdir"]) : null;
        if ((flags & 4) == 0 || dir == null || !Directory.Exists(dir)) return null;
        g.Dir = dir;
        var r = new Installed { Version = "Steam", Tag = m.ContainsKey("buildid") ? m["buildid"] : null };
        string lang = Language(g);
        r.Sub = "Steam" + (m.ContainsKey("buildid") ? "  \u2022  build " + m["buildid"] : "") + (lang != null && g.Languages.Length > 0 ? "  \u2022  " + lang : "");
        return r;
    }
    // the language Steam has the game in ("Dutch"), or null
    public static string Language(Game g) {
        string lib; var m = Manifest(g, out lib);
        string l;
        if (m == null || !m.TryGetValue("language", out l) || l.Length == 0) return null;
        return char.ToUpperInvariant(l[0]) + l.Substring(1);
    }
    // ---------- what Steam knows about your play: its play time, when you last played, achievements ----------
    // localconfig.vdf (userdata\<you>\config: the account that used this PC last) has each game's "Playtime"
    // (minutes) and "LastPlayed" (unix time); librarycache\<id>.json beside it has the achievements Steam last showed.
    static string userConfig; static DateTime userConfigAt; static string localConfig;
    static string UserConfigDir() {
        try {
            var d = new DirectoryInfo(Path.Combine(Root, "userdata"));
            if (!d.Exists) return null;
            var best = d.GetDirectories().Select(x => new FileInfo(Path.Combine(x.FullName, @"config\localconfig.vdf"))).Where(f => f.Exists).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            return best != null ? best.DirectoryName : null;
        } catch { return null; }
    }
    public static string UserConfig() { if (userConfig == null) userConfig = UserConfigDir(); return userConfig; }
    public class Stats { public int Minutes; public DateTime Last = DateTime.MinValue; public int Achieved, AchTotal; }
    public static Stats StatsOf(Game g) {
        if (g.Steam == null || Root.Length == 0) return null;
        try {
            if (userConfig == null) userConfig = UserConfigDir();
            if (userConfig == null) return null;
            var f = new FileInfo(Path.Combine(userConfig, "localconfig.vdf"));
            if (localConfig == null || f.LastWriteTimeUtc != userConfigAt) { localConfig = File.ReadAllText(f.FullName); userConfigAt = f.LastWriteTimeUtc; }
            var s = new Stats();
            // the game's block in "apps": from its "<id>" { to the matching }
            var m = Regex.Match(localConfig, "\"apps\"\\s*\\{");
            int at = m.Success ? localConfig.IndexOf("\"" + g.Steam + "\"", m.Index + m.Length, StringComparison.Ordinal) : -1;
            while (at >= 0) {
                int open = at + g.Steam.Length + 2; while (open < localConfig.Length && char.IsWhiteSpace(localConfig[open])) open++;
                if (open < localConfig.Length && localConfig[open] == '{') {
                    int depth = 0, end = open;
                    for (; end < localConfig.Length; end++) { if (localConfig[end] == '{') depth++; else if (localConfig[end] == '}' && --depth == 0) break; }
                    string block = localConfig.Substring(open, end - open);
                    var pt = Regex.Match(block, "\"Playtime\"\\s+\"(\\d+)\""); if (pt.Success) s.Minutes = int.Parse(pt.Groups[1].Value);
                    var lp = Regex.Match(block, "\"LastPlayed\"\\s+\"(\\d+)\""); long t;
                    if (lp.Success && long.TryParse(lp.Groups[1].Value, out t) && t > 0) s.Last = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(t).ToLocalTime();
                    break;
                }
                at = localConfig.IndexOf("\"" + g.Steam + "\"", at + 1, StringComparison.Ordinal);
            }
            string ach = Path.Combine(userConfig, @"librarycache\" + g.Steam + ".json");
            if (File.Exists(ach)) {
                string j = File.ReadAllText(ach);
                var nt = Regex.Match(j, "\"nTotal\":(\\d+)"); var na = Regex.Match(j, "\"nAchieved\":(\\d+)");
                if (nt.Success) s.AchTotal = int.Parse(nt.Groups[1].Value);
                if (na.Success) s.Achieved = int.Parse(na.Groups[1].Value);
            }
            return s;
        } catch { return null; }
    }
    // the app id of the game Steam is running now ("" when none)
    public static string RunningApp {
        get {
            try { var v = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "RunningAppID", 0); int id = v is int ? (int)v : 0; return id > 0 ? id.ToString() : ""; }
            catch { return ""; }
        }
    }
    // Steam's own tools that aren't games
    static readonly Regex NotAGame = new Regex(@"Redistributable|Steamworks|Proton|Steam Linux Runtime|SteamVR|Dedicated Server|Soundtrack|\bSDK\b|Wallpaper Engine", RegexOptions.IgnoreCase);
    static Game SteamTile(string id, string name) {
        string initials = new string(name.Split(new[] { ' ', '-', ':' }, StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetterOrDigit(w[0])).Take(3).Select(w => char.ToUpperInvariant(w[0])).ToArray());
        return new Game {
            Id = "steam" + id, Name = name, Short = initials.Length > 0 ? initials : "S", Source = "STEAM", Steam = id, Exe = "",
            Series = "steam", Needs = name + " from your Steam library: the launcher asks Steam to install it, and Steam keeps it up to date.",
            Accent = "#3d6f9e", Accent2 = "#9fc6e8"
        };
    }
    // the games in your Steam library (Settings > General > Steam games): "library" (the default) every game you
    // own, "installed" only the installed ones, "listed" only games.json's. Installed ones come from the appmanifests;
    // the rest from Steam's app cache (appinfo.vdf: the games Steam keeps details of for your account, those with
    // library artwork). A tile each, in the Steam group, installed and started by Steam.
    public static void AddInstalled() {
        string mode = Settings.Get("SteamLibrary");
        if (mode == "listed" || Root.Length == 0) return;
        var known = new HashSet<string>(Catalog.Games.Where(x => x.Steam != null).Select(x => x.Steam));
        var found = new List<Game>();
        if (mode != "installed" && mode != "all") {
            try {
                var art = new HashSet<string>(Directory.GetDirectories(Path.Combine(Root, @"appcache\librarycache")).Select(Path.GetFileName));
                foreach (var kv in AppInfoGames())
                    if (art.Contains(kv.Key) && !known.Contains(kv.Key) && !NotAGame.IsMatch(kv.Value) && !kv.Value.EndsWith(" Public Test", StringComparison.OrdinalIgnoreCase)) {
                        known.Add(kv.Key); found.Add(SteamTile(kv.Key, kv.Value));
                    }
            } catch { }
        }
        foreach (var lib in Libraries()) {
            string[] files;
            try { files = Directory.GetFiles(lib, "appmanifest_*.acf"); } catch { continue; }
            foreach (var f in files) {
                try {
                    string t = File.ReadAllText(f);
                    Func<string, string> val = k => { var m = Regex.Match(t, "\"" + k + "\"\\s+\"([^\"]*)\""); return m.Success ? m.Groups[1].Value : null; };
                    string id = val("appid"), name = val("name"), flags = val("StateFlags");
                    int fl; int.TryParse(flags ?? "0", out fl);
                    if (id == null || string.IsNullOrEmpty(name) || (fl & 4) == 0 || known.Contains(id) || id == "228980" || NotAGame.IsMatch(name)) continue;
                    known.Add(id);
                    found.Add(SteamTile(id, name));
                } catch { }
            }
        }
        int rank = 1000;
        foreach (var g in found.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)) { g.FromLibrary = true; g.SettingsLabel = g.Name; g.CheckFile = ""; g.Rank = rank++; Catalog.Games.Add(g); }
    }
    // appinfo.vdf (binary): app id -> name, for the apps whose type is "game". Versions 27-29 (29 keeps its key names
    // in a table at the end of the file).
    static Dictionary<string, string> AppInfoGames() {
        var r = new Dictionary<string, string>();
        byte[] b = File.ReadAllBytes(Path.Combine(Root, @"appcache\appinfo.vdf"));
        uint magic = BitConverter.ToUInt32(b, 0);
        int pos = 8, hdr = 60; string[] keys = null;   // per app: state, last updated, token, sha1, change number, binary sha1
        if (magic == 0x07564429) {
            long st = BitConverter.ToInt64(b, 8); pos = 16;
            int n = (int)BitConverter.ToUInt32(b, (int)st), p = (int)st + 4;
            keys = new string[n];
            for (int i = 0; i < n; i++) { int e = Array.IndexOf(b, (byte)0, p); keys[i] = System.Text.Encoding.UTF8.GetString(b, p, e - p); p = e + 1; }
        } else if (magic == 0x07564427) hdr = 40;
        else if (magic != 0x07564428) return r;
        while (pos + 8 <= b.Length) {
            uint app = BitConverter.ToUInt32(b, pos); if (app == 0) break;
            int size = (int)BitConverter.ToUInt32(b, pos + 4), start = pos + 8;
            try {
                int p = start + hdr;
                var root = ReadKv(b, ref p, keys);
                object o; var top = root.TryGetValue("appinfo", out o) ? (Dictionary<string, object>)o : root;
                if (top.TryGetValue("common", out o)) {
                    var c = (Dictionary<string, object>)o; object t, nm;
                    if (c.TryGetValue("type", out t) && string.Equals(t as string, "game", StringComparison.OrdinalIgnoreCase) && c.TryGetValue("name", out nm) && !string.IsNullOrEmpty(nm as string))
                        r[app.ToString()] = ((string)nm).Trim();
                }
            } catch { }
            pos = start + size;
        }
        return r;
    }
    static Dictionary<string, object> ReadKv(byte[] b, ref int p, string[] keys) {
        var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        while (true) {
            byte t = b[p++];
            if (t == 8) return d;
            string k;
            if (keys != null) { k = keys[BitConverter.ToUInt32(b, p)]; p += 4; }
            else { int e = Array.IndexOf(b, (byte)0, p); k = System.Text.Encoding.UTF8.GetString(b, p, e - p); p = e + 1; }
            object v;
            if (t == 0) v = ReadKv(b, ref p, keys);
            else if (t == 1) { int e = Array.IndexOf(b, (byte)0, p); v = System.Text.Encoding.UTF8.GetString(b, p, e - p); p = e + 1; }
            else if (t == 2 || t == 3 || t == 4 || t == 6) { v = BitConverter.ToInt32(b, p); p += 4; }
            else if (t == 7 || t == 10) { v = BitConverter.ToInt64(b, p); p += 8; }
            else throw new InvalidDataException("appinfo type " + t);
            d[k] = v;
        }
    }
    public static bool Running { get { try { return Process.GetProcessesByName("steam").Length > 0; } catch { return false; } } }
    public static void Open(string what, Game g) {
        try { Process.Start("steam://" + what + "/" + g.Steam); } catch { }
    }
    // what the launcher says instead of release notes
    public static Release Note(Game g) {
        return new Release { Tag = "Steam", Notes = "## Kept up to date by Steam\n" + g.Name + " is bought, installed and updated through Steam: the launcher starts it there. Its patch notes are on its page in your Steam library (the news on the game's page)." };
    }
    // Steam's own artwork for the game: the tall library picture (its top square, where the title is), else the header
    public static ImageSource Art(Game g) {
        try {
            string dir = Path.Combine(Root, @"appcache\librarycache", g.Steam);
            if (!Directory.Exists(dir)) return null;
            var files = Directory.GetFiles(dir, "*.jpg", SearchOption.AllDirectories);
            // the names differ between Steam versions (newer: library_capsule, library_header, in hashed folders)
            Func<string[], string> pickOf = names => names.Select(n => files.FirstOrDefault(f => Path.GetFileName(f).Equals(n, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(f => f != null);
            string tall = pickOf(new[] { "library_600x900.jpg", "library_capsule.jpg" });
            string wide = pickOf(new[] { "header.jpg", "library_header.jpg" });
            string pick = tall ?? wide; if (pick == null) return null;
            var img = new BitmapImage();
            img.BeginInit(); img.UriSource = new Uri(pick); img.CacheOption = BitmapCacheOption.OnLoad; img.DecodePixelWidth = 320; img.EndInit(); img.Freeze();
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) {
                var rect = new Rect(0, 0, 256, 256);
                dc.PushClip(new RectangleGeometry(rect, 56, 56));
                double w = img.PixelWidth, h = img.PixelHeight;
                // fill the square: a tall picture keeps its top, a wide one its middle
                double k = Math.Max(256 / w, 256 / h);
                double dw = w * k, dh = h * k;
                dc.DrawImage(img, new Rect((256 - dw) / 2, tall != null ? 0 : (256 - dh) / 2, dw, dh));
                dc.Pop();
            }
            var bmp = new RenderTargetBitmap(256, 256, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv); bmp.Freeze();
            return bmp;
        } catch { return null; }
    }
}

partial class Shell {
    DispatcherTimer steamWatch; readonly HashSet<Game> steamInstalling = new HashSet<Game>();
    void SteamInstall(Game g) {
        if (SteamGames.Root.Length == 0) { SetStep("Steam isn't installed on this PC: " + g.Name + " is installed through Steam.", Ui.Red); return; }
        Sound.Play(Sfx.Select);
        SteamGames.Open("install", g);
        SetStep("Steam is opening " + g.Name + "'s install. The launcher shows it here once it's installed.", Ui.Teal);
        steamInstalling.Add(g);
        if (steamWatch == null) {
            steamWatch = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
            steamWatch.Tick += (s, e) => {
                foreach (var x in steamInstalling.ToList()) {
                    var inst = SteamGames.Detect(x);
                    if (inst == null) continue;
                    steamInstalling.Remove(x);
                    x.Inst = inst; x.State = State.Current; x.Latest = SteamGames.Note(x);
                    Icons.Load(x); ApplyIcon(x);
                    Log(x.Name + " is installed (through Steam).", Ui.Teal);
                    SetStep(x.Name + " is installed: press Start.", Ui.Teal);
                    ApplySort(true); ApplyFilter(); UpdateViews();
                }
                if (steamInstalling.Count == 0) steamWatch.Stop();
            };
        }
        steamWatch.Start();
    }
    void SteamStart(Game g) {
        SteamGames.Open("rungameid", g);
        SetStep("Starting " + g.Name + " through Steam...", Ui.Teal);
        if (Directory.Exists(g.Dir)) Engine.StartPlaytime(g, g.Dir);
        WatchGame(g);
        if (!running) { string mode = OnPlayMode(); if (mode == "close") CloseLauncher(); else if (mode == "tray") WaitInTray(g); }
    }
    void SteamLanguage(Game g) {
        Sound.Play(Sfx.Select);
        SteamGames.Open("gameproperties", g);
        SetStep("In Steam's Properties for " + g.Name + ": General, then Language (" + string.Join(", ", g.Languages) + "). Steam downloads that language's voices and text.", Ui.Teal);
    }
}
}
