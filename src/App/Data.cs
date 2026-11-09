// Data the launcher keeps: the game catalog (games.json), settings.json, cache.json (last known releases),
// playtime.json (written by playtime-worker.ps1) and roms.json (the game files picked before). All JSON files keep
// the format the PowerShell launcher used, so nothing is lost when switching.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace RecompLauncher {

static class Json {
    static readonly JavaScriptSerializer S = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
    public static object Parse(string text) { return S.DeserializeObject(text); }
    public static Dictionary<string, object> ReadFile(string path) {
        try {
            if (!File.Exists(path)) return null;
            string t = File.ReadAllText(path, Encoding.UTF8).TrimStart('\uFEFF');
            return Parse(t) as Dictionary<string, object>;
        } catch { return null; }
    }
    public static string Write(object o) { return Pretty(S.Serialize(o)); }
    public static void WriteFile(string path, object o) {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, Write(o), new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
    }
    public static string Str(IDictionary<string, object> d, string key) {
        object v; if (d == null || !d.TryGetValue(key, out v) || v == null) return null;
        return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
    }
    public static bool Bool(IDictionary<string, object> d, string key) {
        object v; if (d == null || !d.TryGetValue(key, out v) || v == null) return false;
        if (v is bool) return (bool)v;
        return "true".Equals(Convert.ToString(v), StringComparison.OrdinalIgnoreCase);
    }
    public static double Num(IDictionary<string, object> d, string key) {
        object v; if (d == null || !d.TryGetValue(key, out v) || v == null) return 0;
        try { return Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); } catch { return 0; }
    }
    public static string[] Strings(IDictionary<string, object> d, string key) {
        object v; if (d == null || !d.TryGetValue(key, out v) || v == null) return new string[0];
        var e = v as IEnumerable;
        if (v is string || e == null) return new[] { Convert.ToString(v) };
        return e.Cast<object>().Select(x => Convert.ToString(x)).ToArray();
    }
    public static Dictionary<string, object> Obj(IDictionary<string, object> d, string key) {
        object v; if (d == null || !d.TryGetValue(key, out v)) return null;
        return v as Dictionary<string, object>;
    }
    // indents the serializer's one-line output, so the files stay readable
    static string Pretty(string json) {
        var sb = new StringBuilder(json.Length + 256);
        int depth = 0; bool str = false;
        for (int i = 0; i < json.Length; i++) {
            char c = json[i];
            if (str) { sb.Append(c); if (c == '\\') { sb.Append(json[++i]); } else if (c == '"') str = false; continue; }
            switch (c) {
                case '"': str = true; sb.Append(c); break;
                case '{': case '[':
                    sb.Append(c);
                    if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(json[++i]); break; }
                    depth++; sb.Append('\n').Append(' ', depth * 2); break;
                case '}': case ']': depth--; sb.Append('\n').Append(' ', depth * 2).Append(c); break;
                case ',': sb.Append(c).Append('\n').Append(' ', depth * 2); break;
                case ':': sb.Append(": "); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}

// What a game's own setup reads, so the launcher can ask for it before installing and check it
class RomSpec {
    public string Kind = "none";            // file, folder, both, none
    public string[] Formats = new string[0];
    public string[] Ext = new string[0];
    public List<string[]> FolderNeeds = new List<string[]>();
    public string Filter, Handoff, TitleId, CopyAs;
    public string[] CopyIn = new string[0];
    public bool HasNsp, NspUpdate; public string NspHint, NspUpdateId;
}

class Release {
    public string Tag, Notes, Date, Url, Sha, Problem, Checked;
    public double Size;
    // the releases before this one, newest first (tag, date and notes only), for the notes of every version skipped
    public List<Release> Older = new List<Release>();
    public Dictionary<string, object> ToDict() {
        var d = new Dictionary<string, object>();
        d["Tag"] = Tag; d["Notes"] = Notes; d["Date"] = Date; d["Checked"] = Checked;
        if (Url != null) d["Url"] = Url;
        if (Size > 0) d["Size"] = Size;
        if (Sha != null) d["Sha"] = Sha;
        if (Problem != null) d["Problem"] = Problem;
        if (Older.Count > 0) d["Older"] = Older.Select(o => (object)new Dictionary<string, object> { { "Tag", o.Tag }, { "Date", o.Date }, { "Notes", o.Notes } }).ToList();
        return d;
    }
    public static Release From(IDictionary<string, object> d) {
        if (d == null || Json.Str(d, "Tag") == null) return null;
        var r = new Release {
            Tag = Json.Str(d, "Tag"), Notes = Json.Str(d, "Notes"), Date = Json.Str(d, "Date"), Url = Json.Str(d, "Url"),
            Sha = Json.Str(d, "Sha"), Problem = Json.Str(d, "Problem"), Checked = Json.Str(d, "Checked"), Size = Json.Num(d, "Size")
        };
        object older;
        if (d.TryGetValue("Older", out older) && older is System.Collections.IEnumerable)
            foreach (var o in ((System.Collections.IEnumerable)older).OfType<Dictionary<string, object>>())
                if (Json.Str(o, "Tag") != null) r.Older.Add(new Release { Tag = Json.Str(o, "Tag"), Date = Json.Str(o, "Date"), Notes = Json.Str(o, "Notes") });
        return r;
    }
    public DateTime CheckedAt { get { DateTime t; return DateTime.TryParse(Checked, out t) ? t : DateTime.MinValue; } }
}

class Installed {
    public string Version = "?";   // what the launcher shows, without a leading v
    public string Tag;             // the release tag the launcher installed (recomp-launcher.json), if known
    public DateTime When;          // when the launcher installed it
    public string Sub = "";
    // "v1.2.0", but a named release ("low-vram") as it is
    public string Label { get { return Version.Length > 0 && char.IsDigit(Version[0]) ? "v" + Version : Version == "?" ? "installed" : Version; } }
}

enum State { Missing, Checking, Current, Update, Problem, Offline, Unknown, Installing, Updating, Uninstalling, Converting }

class Game {
    public string Id, Name, Short, Source, Exe, Proc, SettingsKey, Launch, Repo, Asset, NotAsset, Accent, Accent2;
    public string Backup, Keep, Replace, FirstRun, SetupArg, Rebuild, Needs, DefaultDir, Series, SettingsLabel, CheckFile, Saves;
    public bool Pre, NoSha, Console;
    public string Steam;                          // a Steam game: its app id (Steam.cs)
    public string[] Languages = new string[0];    // the languages Steam has it in
    // a game from Epic or GOG (Stores.cs): the store, its id there, and what the store needs to start it
    public string Store, StoreId, StoreNs, StoreItem, StoreExe, StoreArgs, StoreUninstall, StoreArt, StoreHero;
    public bool ByStore { get { return Steam != null || Store != null; } }
    public string StoreName { get { return Steam != null ? "Steam" : Store == "epic" ? "Epic Games" : Store == "gog" ? "GOG" : null; } }
    public RomSpec Rom = new RomSpec();
    public int Rank;

    // live state
    public string Dir, Worker, Backups;
    public Installed Inst;
    public Release Latest;
    public State State = State.Missing;
    public string RomPath;
    public bool Hidden;
    public object Icon;          // ImageSource
    public bool IconIsArt;       // a whole card face (drawn or the player's picture), not a program icon
    public object Ui;            // the game's elements in each view (UI.GameUi)

    public bool IsBusy { get { return State == State.Installing || State == State.Updating || State == State.Uninstalling || State == State.Converting; } }
}

static class Catalog {
    public static List<Game> Games = new List<Game>();
    public static Dictionary<string, int> SeriesRank = new Dictionary<string, int>();

    public static void Load(string path) {
        var root = Json.ReadFile(path);
        if (root == null) throw new Exception("games.json is missing or broken: " + path);
        var sr = Json.Obj(root, "SeriesRank");
        if (sr != null) foreach (var kv in sr) SeriesRank[kv.Key] = Convert.ToInt32(kv.Value);
        var order = Json.Strings(root, "Order").ToList();
        int i = 0;
        foreach (var o in (IEnumerable)root["Games"]) {
            var d = (Dictionary<string, object>)o;
            var g = new Game {
                Id = Json.Str(d, "Id"), Name = Json.Str(d, "Name"), Short = Json.Str(d, "Short") ?? "", Source = Json.Str(d, "Source") ?? "",
                Exe = Json.Str(d, "Exe"), Proc = Json.Str(d, "Proc"), SettingsKey = Json.Str(d, "SettingsKey"), Launch = Json.Str(d, "Launch"),
                Repo = Json.Str(d, "Repo"), Asset = Json.Str(d, "Asset"), NotAsset = Json.Str(d, "NotAsset"),
                Accent = (Json.Str(d, "Accent") ?? "#3fb8a0").ToLowerInvariant(), Accent2 = (Json.Str(d, "Accent2") ?? "#d9b45a").ToLowerInvariant(),   // lower case: never recoloured by a theme
                Backup = Json.Str(d, "Backup"), Keep = Json.Str(d, "Keep"), Replace = Json.Str(d, "Replace"), FirstRun = Json.Str(d, "FirstRun"),
                SetupArg = Json.Str(d, "SetupArg"), Rebuild = Json.Str(d, "Rebuild"), Needs = Json.Str(d, "Needs") ?? "",
                DefaultDir = Json.Str(d, "DefaultDir"), Series = Json.Str(d, "Series") ?? "nintendo",
                SettingsLabel = Json.Str(d, "SettingsLabel"), CheckFile = Json.Str(d, "CheckFile"), Saves = Json.Str(d, "Saves"),
                Pre = Json.Bool(d, "Pre"), NoSha = Json.Bool(d, "NoSha"), Console = Json.Bool(d, "Console"),
                Steam = Json.Str(d, "Steam"), Languages = Json.Strings(d, "Languages")
            };
            if (g.SettingsLabel == null) g.SettingsLabel = g.Name;
            if (g.CheckFile == null) g.CheckFile = g.Exe;
            int r = order.IndexOf(g.Id);
            g.Rank = r >= 0 ? r : 100 + i;
            var rs = Json.Obj(d, "Rom");
            if (rs != null) {
                g.Rom.Kind = Json.Str(rs, "Kind") ?? "none";
                g.Rom.Formats = Json.Strings(rs, "Formats");
                g.Rom.Ext = Json.Strings(rs, "Ext").Select(x => x.ToLowerInvariant()).ToArray();
                object fn;
                if (rs.TryGetValue("FolderNeeds", out fn) && fn is IEnumerable)
                    foreach (var set in (IEnumerable)fn)
                        g.Rom.FolderNeeds.Add(set is string ? new[] { (string)set } : ((IEnumerable)set).Cast<object>().Select(x => (string)x).ToArray());
                g.Rom.Filter = Json.Str(rs, "Filter"); g.Rom.Handoff = Json.Str(rs, "Handoff");
                g.Rom.TitleId = Json.Str(rs, "TitleId"); g.Rom.CopyAs = Json.Str(rs, "CopyAs");
                g.Rom.CopyIn = Json.Strings(rs, "CopyIn");
                var nsp = Json.Obj(rs, "Nsp");
                if (nsp != null) { g.Rom.HasNsp = true; g.Rom.NspUpdate = Json.Bool(nsp, "Update"); g.Rom.NspHint = Json.Str(nsp, "Hint"); g.Rom.NspUpdateId = Json.Str(nsp, "UpdateId"); }
            }
            Games.Add(g);
            i++;
        }
    }
    public static int SeriesOf(Game g) { int r; return SeriesRank.TryGetValue(g.Series, out r) ? r : 50; }
    public static Game Find(string id) { return Games.FirstOrDefault(g => g.Id == id); }
}

// settings.json: string values, the same keys as before (the game folders are "<Name>Dir")
static class Settings {
    public static string Path;
    static Dictionary<string, object> values = new Dictionary<string, object>();
    static readonly Dictionary<string, string> Defaults = new Dictionary<string, string> {
        { "TotkUpdaterDir", @"C:\Games\TOTK Updater" }, { "Fullscreen", "" }, { "LastGame", "totk" }, { "SortMode", "default" },
        { "CloseOnPlay", "on" }, { "AutoCheck", "on" }, { "GitHubToken", "" }, { "ViewMode", "home" }, { "Hidden", "" }, { "SoundVolume", "50" }
    };
    public static void Load(string path) {
        Path = path;
        values = Json.ReadFile(path) ?? new Dictionary<string, object>();
    }
    public static string Get(string key) {
        string v = Json.Str(values, key);
        if (!string.IsNullOrEmpty(v)) return v;
        string d;
        if (Defaults.TryGetValue(key, out d)) return d;
        var g = Catalog.Games.FirstOrDefault(x => x.SettingsKey == key);
        return g != null ? g.DefaultDir : "";
    }
    public static string Default(string key) {
        string d; if (Defaults.TryGetValue(key, out d)) return d;
        var g = Catalog.Games.FirstOrDefault(x => x.SettingsKey == key);
        return g != null ? g.DefaultDir : "";
    }
    public static void Set(string key, string value) { values[key] = value ?? ""; }
    public static bool On(string key) { return Get(key) != "off"; }
    public static void Save() {
        try {
            foreach (var g in Catalog.Games) if (g.SettingsKey != null && !values.ContainsKey(g.SettingsKey)) values[g.SettingsKey] = g.DefaultDir;
            Json.WriteFile(Path, values);
        } catch { }
    }
}

// cache.json: the last release found for each game, used for 20 minutes instead of asking again, and whenever a
// release page can't be reached
static class ReleaseCache {
    public static string Path;
    static readonly Dictionary<string, Release> items = new Dictionary<string, Release>();
    static readonly object gate = new object();
    public static void Load(string path) {
        Path = path;
        var d = Json.ReadFile(path);
        if (d == null) return;
        foreach (var kv in d) { var r = Release.From(kv.Value as Dictionary<string, object>); if (r != null) items[kv.Key] = r; }
    }
    public static Release Get(string id) { lock (gate) { Release r; return items.TryGetValue(id, out r) ? r : null; } }
    public static void Put(string id, Release r) {
        lock (gate) {
            items[id] = r;
            try { Json.WriteFile(Path, items.ToDictionary(kv => kv.Key, kv => (object)kv.Value.ToDict())); } catch { }
        }
    }
}

// playtime.json: { "<id>": { "total": seconds, "last": "ISO time", "sessions": n } }, read again when it changes
static class Playtime {
    public static string Path;
    static DateTime read = DateTime.MinValue;
    static Dictionary<string, object> data = new Dictionary<string, object>();
    static void Refresh() {
        DateTime t = File.Exists(Path) ? File.GetLastWriteTimeUtc(Path) : DateTime.MinValue;
        if (t == read) return;
        read = t; data = Json.ReadFile(Path) ?? new Dictionary<string, object>();
    }
    // the profile whose play time is shown and counted (Profiles.PlaytimeFile)
    public static void Use(string path) { Path = path; read = DateTime.MinValue; data = new Dictionary<string, object>(); }
    public static double Total(string id) { Refresh(); return Json.Num(Json.Obj(data, id), "total"); }
    // all of a profile's play time, and its most played game's id (for Who's playing?)
    public static double TotalIn(string path, out string top) {
        top = null; double sum = 0, most = 0;
        var d = Json.ReadFile(path);
        if (d == null) return 0;
        foreach (var kv in d) {
            double t = Json.Num(kv.Value as Dictionary<string, object>, "total");
            sum += t; if (t > most) { most = t; top = kv.Key; }
        }
        return sum;
    }
    public static DateTime Last(string id) {
        Refresh(); DateTime d;
        return DateTime.TryParse(Json.Str(Json.Obj(data, id), "last"), out d) ? d : DateTime.MinValue;
    }
    public static string Format(double secs) {
        int m = (int)Math.Round(secs / 60);
        if (m < 1) return "just started";
        if (m < 60) return m + " min";
        return (m / 60) + " h " + (m % 60) + " min";
    }
    public static string When(DateTime d) {
        if (d == DateTime.MinValue) return "";
        int days = (DateTime.Today - d.Date).Days;
        if (days <= 0) return "today";
        if (days == 1) return "yesterday";
        if (days < 7) return days + " days ago";
        return d.ToString("MMM d");
    }
}

// roms.json: the game file or folder picked for each game ("_keys": prod.keys)
static class Roms {
    public static string Path;
    public static string Get(string id) { return Json.Str(Json.ReadFile(Path), id); }
    public static void Set(string id, string value) {
        var d = Json.ReadFile(Path) ?? new Dictionary<string, object>();
        d[id] = value;
        try { Json.WriteFile(Path, d); } catch { }
    }
}
}
