// What a game is (installed version), where its releases are (GitHub or SMO's GitLab project), and starting things:
// the game, its first-run setup, and the PowerShell workers that install, update, convert and uninstall.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace RecompLauncher {

static class Versions {
    // "v0.4.1-alpha.4", "v.0.9.5", "0.3.7+0f65b4e" -> a comparable version; null for names like "low-vram"
    public static Version Parse(string s) {
        if (string.IsNullOrEmpty(s)) return null;
        // a release named after its date ("2026-10-23-9d9230c2") compares as 2026.10.23
        var date = Regex.Match(s, @"^v?(\d{4})-(\d{2})-(\d{2})(?!\d)");
        if (date.Success) return new Version(int.Parse(date.Groups[1].Value), int.Parse(date.Groups[2].Value), int.Parse(date.Groups[3].Value));
        string t = Regex.Replace(s, @"^[^\d]*", "");
        t = Regex.Replace(t, @"[+-].*$", "").TrimEnd('.');
        if (!Regex.IsMatch(t, @"^\d+(\.\d+){0,3}$")) return null;
        if (!t.Contains(".")) t += ".0";
        Version v; return Version.TryParse(t, out v) ? v : null;
    }
    // Whether a release is newer than what is installed. Numbers are compared when both are numbers; projects that
    // name their releases (Hookshot's "low-vram") are newer when the tag differs from the installed one and the
    // release came out after it was installed.
    public static bool IsNewer(Release latest, Installed inst) {
        if (latest == null || inst == null) return false;
        Version a = Parse(latest.Tag), b = Parse(inst.Version);
        if (a != null && b != null) return a > b;
        string tag = inst.Tag ?? inst.Version;
        if (string.Equals(Norm(latest.Tag), Norm(tag), StringComparison.OrdinalIgnoreCase)) return false;
        DateTime pub;
        if (inst.When != DateTime.MinValue && DateTime.TryParse(latest.Date, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out pub))
            return pub > inst.When.ToUniversalTime();
        return a != null || b == null;   // a numbered release over an unknown build, or two names that differ
    }
    static string Norm(string s) { return (s ?? "").Trim().TrimStart('v', 'V'); }
    // every release newer than what is installed, newest first (the latest alone when nothing is installed)
    public static List<Release> Missed(Release latest, Installed inst) {
        var r = new List<Release>();
        if (latest == null) return r;
        r.Add(latest);
        if (inst == null || !IsNewer(latest, inst)) return r;
        foreach (var o in latest.Older) { if (!IsNewer(o, inst)) break; r.Add(o); }
        return r;
    }
}

static class Paths {
    public static string Root, Logs, Backups, Workers, Tools;
    public static string Expand(string p) { return Environment.ExpandEnvironmentVariables(p ?? ""); }
    public static bool Under(string file, string dir) {
        if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(dir)) return false;
        return file.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    }
}

static class Engine {
    // ---------- installed version ----------
    // the game's program; a pattern (SMS Launcher's exe carries its version in its name) means the newest match
    public static string ResolveExe(Game g) {
        string p = Path.Combine(g.Dir, g.Exe);
        if (g.Exe.IndexOfAny(new[] { '*', '?' }) < 0) return p;
        try {
            if (!Directory.Exists(g.Dir)) return p;
            var hit = new DirectoryInfo(g.Dir).GetFiles(g.Exe)
                .OrderByDescending(f => Versions.Parse(FileVersionInfo.GetVersionInfo(f.FullName).ProductVersion) ?? new Version(0, 0))
                .ThenByDescending(f => f.LastWriteTime).FirstOrDefault();
            return hit != null ? hit.FullName : p;
        } catch { return p; }
    }
    public static Dictionary<string, object> Marker(Game g) { return Json.ReadFile(Path.Combine(g.Dir, "recomp-launcher.json")); }
    public static void SaveMarker(Game g, Dictionary<string, object> m) { try { Json.WriteFile(Path.Combine(g.Dir, "recomp-launcher.json"), m); } catch { } }

    public static Installed GetInstalled(Game g) {
        if (g.App != null) return Shell.AppInstalledInfo;   // the launcher's own pages are always there
        try { return Detect(g); } catch { return null; }
    }
    static Installed Detect(Game g) {
        if (g.ByStore) return Stores.Detect(g);
        string exe = ResolveExe(g);
        var mk = Marker(g);
        string mkTag = Json.Str(mk, "tag");
        DateTime when = DateTime.MinValue;
        if (mk != null) DateTime.TryParse(Json.Str(mk, "installed"), out when);
        Func<string> fileVer = () => FileVersionInfo.GetVersionInfo(exe).FileVersion;
        Func<string, string> tagVer = t => t == null ? null : Regex.Replace(t, "^v", "");
        var r = new Installed { Tag = mkTag, When = when };
        switch (g.Id) {
            case "totk": {
                string j = Path.Combine(g.Dir, "totk-install.json");
                var inst = Json.ReadFile(j);
                if (inst == null) return null;
                r.Version = Regex.Replace(Json.Str(inst, "release") ?? "?", @"\+.*$", "");
                r.Sub = "Game " + Json.Str(inst, "version") + "  \u2022  " + Json.Str(inst, "lang");
                return r;
            }
            case "dkb": {   // built on TotK Recomp: the same kind of record, under its own name
                var inst = Json.ReadFile(Path.Combine(g.Dir, "dkb-install.json"));
                if (inst == null) return null;
                r.Version = Regex.Replace(Json.Str(inst, "release") ?? "?", @"\+.*$", "");
                string gv = Json.Str(inst, "version"), lang = Json.Str(inst, "lang");
                r.Sub = (gv != null ? "Game " + gv : "") + (lang != null ? "  \u2022  " + lang : "");
                return r;
            }
            case "smo":
                if (!File.Exists(exe)) return null;
                r.Version = fileVer() ?? tagVer(mkTag) ?? "?";
                r.Sub = Directory.Exists(Path.Combine(g.Dir, "extracted")) ? "Game 1.0.0" : "Game files not set up yet";
                return r;
            case "wwhd": {
                if (!File.Exists(exe)) return null;
                string vt = Path.Combine(g.Dir, "VERSION.txt"); string v = null;
                if (File.Exists(vt)) { var m = Regex.Match(File.ReadAllText(vt), @"v(\d+\.\d+(\.\d+)?)"); if (m.Success) v = m.Groups[1].Value; }
                r.Version = v ?? tagVer(mkTag) ?? "?";
                r.Sub = Directory.Exists(Path.Combine(g.Dir, @"data\game")) ? "Game set up" : "Game not set up yet";
                return r;
            }
            case "tod":
                if (!File.Exists(exe)) return null;
                r.Version = tagVer(mkTag) ?? fileVer() ?? "?";
                r.Sub = File.Exists(Path.Combine(g.Dir, @"elf\EBOOT.ELF")) ? "Disc installed" : "Disc not installed yet";
                return r;
            case "mp6":
                if (!File.Exists(exe)) return null;
                r.Version = tagVer(mkTag) ?? fileVer() ?? "?";
                r.Sub = Directory.Exists(Path.Combine(g.Dir, "content")) ? "Disc set up" : "Disc not set up yet";
                return r;
            case "hookshot": {
                // HookshotBuilder.exe installs and updates the builder itself in %LOCALAPPDATA%\HookshotBuilder; the
                // release it has is in versions\current.txt
                if (!File.Exists(exe)) return null;
                string cur = Paths.Expand(@"%LOCALAPPDATA%\HookshotBuilder\versions\current.txt");
                if (File.Exists(cur)) {
                    string t = File.ReadAllText(cur).Trim();
                    if (t.Length > 0) { r.Version = t; r.Tag = t; r.When = File.GetLastWriteTime(cur); return r; }
                }
                r.Version = mkTag ?? "?";
                return r;
            }
            default:
                if (!File.Exists(exe)) return null;
                r.Version = (mkTag != null ? Regex.Replace(mkTag, @"^[^\d]*", "") : null);
                if (string.IsNullOrEmpty(r.Version)) r.Version = mkTag;   // a named release such as "low-vram"
                if (string.IsNullOrEmpty(r.Version)) r.Version = FileVersionInfo.GetVersionInfo(exe).ProductVersion;
                if (string.IsNullOrEmpty(r.Version)) r.Version = "?";
                return r;
        }
    }

    // ---------- releases ----------
    static readonly Regex Sha = new Regex("^sha256:([0-9a-f]{64})$");
    static object Get(string url, ref string token) {
        for (int attempt = 0; ; attempt++) {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Recomp-Launcher"; req.Accept = "application/json"; req.Timeout = 20000; req.ReadWriteTimeout = 20000;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            if (!string.IsNullOrEmpty(token)) req.Headers["Authorization"] = "Bearer " + token;
            try {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return Json.Parse(sr.ReadToEnd());
            } catch (WebException e) {
                var hr = e.Response as HttpWebResponse;
                // a wrong or expired token makes GitHub refuse everything (401): ask again without it
                if (hr != null && (int)hr.StatusCode == 401 && !string.IsNullOrEmpty(token) && attempt == 0) { token = null; continue; }
                if (hr != null && ((int)hr.StatusCode == 403 || (int)hr.StatusCode == 429)) throw new Exception("GitHub's limit of 60 checks an hour is used up (add a token in Settings)");
                if (hr != null && (int)hr.StatusCode == 404) throw new Exception("this project has no releases");
                throw;
            }
        }
    }
    static bool Like(string name, string pattern) {
        if (string.IsNullOrEmpty(pattern)) return true;
        string re = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return Regex.IsMatch(name, re, RegexOptions.IgnoreCase);
    }
    // the asset for a release: Asset can list several patterns ("HookshotBuilder.exe;HookshotBuilder*.zip"), first wins
    static Dictionary<string, object> PickAsset(Dictionary<string, object> rel, Game g) {
        var assets = (rel.ContainsKey("assets") ? rel["assets"] as IEnumerable : null) ?? new object[0];
        var list = assets.Cast<Dictionary<string, object>>().ToList();
        foreach (var pat in (g.Asset ?? "*").Split(';')) {
            var hit = list.FirstOrDefault(a => Like(Json.Str(a, "name"), pat.Trim()) && (g.NotAsset == null || !Like(Json.Str(a, "name"), g.NotAsset)));
            if (hit != null) return hit;
        }
        return null;
    }
    public static Release FetchLatest(Game g, string token) {
        if (g.ByStore) return Stores.Note(g);   // the store keeps it up to date
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        if (g.Repo != null) {
            // one request for the recent releases: the newest is the one to install, the ones before it carry the
            // notes of versions a player may have skipped
            var all = ((IEnumerable)Get("https://api.github.com/repos/" + g.Repo + "/releases?per_page=30", ref token)).Cast<Dictionary<string, object>>()
                .Where(x => !Json.Bool(x, "draft") && (g.Pre || !Json.Bool(x, "prerelease")))
                .OrderByDescending(x => Json.Str(x, "published_at")).ToList();
            // projects that only publish pre-releases: the newest release that has the download
            var rel = g.Pre ? all.FirstOrDefault(x => PickAsset(x, g) != null) : all.FirstOrDefault();
            if (rel == null) throw new Exception(g.Pre ? "no release with a Windows download" : "this project has no releases");
            var r = new Release { Tag = Json.Str(rel, "tag_name"), Notes = Json.Str(rel, "body"), Date = Json.Str(rel, "published_at") };
            foreach (var o in all.SkipWhile(x => x != rel).Skip(1).Take(25))
                r.Older.Add(new Release { Tag = Json.Str(o, "tag_name"), Notes = Json.Str(o, "body"), Date = Json.Str(o, "published_at") });
            var asset = PickAsset(rel, g);
            if (asset != null) {
                r.Url = Json.Str(asset, "browser_download_url"); r.Size = Json.Num(asset, "size");
                var m = Sha.Match(Json.Str(asset, "digest") ?? "");
                if (m.Success) r.Sha = m.Groups[1].Value;
            } else r.Problem = "No Windows download in this release";
            return r;
        }
        // Super Mario Odyssey: GitLab; the zip is linked from the notes, its SHA-256 written next to it
        string none = null;
        var gl = ((IEnumerable)Get("https://gitlab.com/api/v4/projects/88379810/releases?per_page=20", ref none)).Cast<Dictionary<string, object>>().ToList();
        var rels = gl.First();
        string body = Json.Str(rels, "description") ?? "";
        Func<string, string> clean = b => Regex.Replace(Regex.Split(b ?? "", @"\*\*Downloads\*\*")[0], @"\\([>\-\*_#\[\]\(\)!.+`])", "$1");
        var res = new Release { Tag = Json.Str(rels, "tag_name"), Date = Json.Str(rels, "released_at"), Notes = clean(body) };
        foreach (var o in gl.Skip(1)) res.Older.Add(new Release { Tag = Json.Str(o, "tag_name"), Date = Json.Str(o, "released_at"), Notes = clean(Json.Str(o, "description")) });
        var up = Regex.Match(body, @"\(/uploads/([0-9a-f]{32})/([^)\s]*windows[^)\s]*\.zip)\)");
        if (up.Success) res.Url = "https://gitlab.com/-/project/88379810/uploads/" + up.Groups[1].Value + "/" + up.Groups[2].Value;
        else {
            var assets = Json.Obj(rels, "assets");
            var links = assets != null && assets.ContainsKey("links") ? ((IEnumerable)assets["links"]).Cast<Dictionary<string, object>>() : Enumerable.Empty<Dictionary<string, object>>();
            var link = links.FirstOrDefault(l => Like(Json.Str(l, "url") ?? "", "*windows*.zip"));
            if (link != null) res.Url = Json.Str(link, "url");
        }
        foreach (var line in Regex.Split(body, @"\r?\n")) {
            var h = Regex.Match(line, @"(?<![0-9a-fA-F])([0-9a-fA-F]{64})(?![0-9a-fA-F])");
            if (Regex.IsMatch(line, @"windows[^\s]*\.zip") && h.Success) { res.Sha = h.Groups[1].Value.ToLowerInvariant(); break; }
        }
        if (res.Url == null) res.Problem = "No Windows download in this release";
        else if (res.Sha == null) res.Problem = "No SHA-256 in the notes to check the download";
        return res;
    }
    public static string TokenLimit(string token) {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        string t = token;
        var d = (Dictionary<string, object>)Get("https://api.github.com/rate_limit", ref t);
        if (t == null) return null;   // refused
        var core = Json.Obj(Json.Obj(d, "resources"), "core");
        return Json.Num(core, "limit") + "|" + Json.Num(core, "remaining");
    }

    // ---------- starting things ----------
    public static Process Start(string file, string args, string dir, bool hidden) {
        var psi = new ProcessStartInfo(file) { WorkingDirectory = dir, UseShellExecute = true };
        if (!string.IsNullOrEmpty(args)) psi.Arguments = args;
        if (hidden) psi.WindowStyle = ProcessWindowStyle.Hidden;
        return Process.Start(psi);
    }
    public static string Q(string s) { return "\"" + (s ?? "").TrimEnd('\\') + "\""; }
    public static Process RunWorker(string script, string args) {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " + Q(script) + " " + args) {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Paths.Root
        };
        return Process.Start(psi);
    }

    // What Play starts: after an update that needs it, the game's builder (Rebuild) once; else the first Launch
    // choice that exists (relative to the game folder, or full paths with %VARIABLES% and wildcards); else Exe
    public static string PlayFile(Game g, out string args, bool consume) {
        args = null;
        var mk = Marker(g);
        bool needsSetup = mk != null && Json.Bool(mk, "needsSetup");
        if (g.Rebuild != null && needsSetup && File.Exists(Path.Combine(g.Dir, g.Rebuild))) {
            if (consume) { mk["needsSetup"] = false; SaveMarker(g, mk); }
            return Path.Combine(g.Dir, g.Rebuild);
        }
        if (g.Launch != null) {
            foreach (var c in g.Launch.Split(';')) {
                string p = Paths.Expand(c.Trim());
                if (!Path.IsPathRooted(p)) p = Path.Combine(g.Dir, p);
                string hit = Glob(p);
                if (hit != null) { return Finish(g, hit, mk, needsSetup, consume, out args); }
            }
        }
        return Finish(g, ResolveExe(g), mk, needsSetup, consume, out args);
    }
    static string Finish(Game g, string file, Dictionary<string, object> mk, bool needsSetup, bool consume, out string args) {
        args = null;
        if (g.SetupArg != null && needsSetup) {
            args = g.SetupArg;
            if (consume) { mk["needsSetup"] = false; SaveMarker(g, mk); }
        }
        return file;
    }
    // a path with wildcards in any part ("C:\x\*\hookshot.exe"): the first file that matches
    public static string Glob(string p) {
        try {
            if (p.IndexOfAny(new[] { '*', '?' }) < 0) return File.Exists(p) ? p : null;
            var parts = p.Split('\\');
            var dirs = new List<string> { parts[0] + "\\" };
            for (int i = 1; i < parts.Length; i++) {
                bool last = i == parts.Length - 1;
                var next = new List<string>();
                foreach (var d in dirs) {
                    if (!Directory.Exists(d)) continue;
                    if (parts[i].IndexOfAny(new[] { '*', '?' }) < 0) {
                        string c = Path.Combine(d, parts[i]);
                        if (last ? File.Exists(c) : Directory.Exists(c)) next.Add(c);
                    } else next.AddRange(last ? Directory.GetFiles(d, parts[i]) : Directory.GetDirectories(d, parts[i]));
                }
                dirs = next;
                if (dirs.Count == 0) return null;
            }
            return dirs.FirstOrDefault(File.Exists);
        } catch { return null; }
    }
    // the game's own processes (by games.json Proc, which can end in *)
    public static List<Process> Processes(Game g) {
        var r = new List<Process>();
        // a game the launcher found in a store (no Proc): whatever runs from its folder
        if (string.IsNullOrEmpty(g.Proc) && g.ByStore && !string.IsNullOrEmpty(g.Dir)) {
            foreach (var p in Process.GetProcesses()) { try { if (Paths.Under(p.MainModule.FileName, g.Dir)) r.Add(p); } catch { } }
            return r;
        }
        if (string.IsNullOrEmpty(g.Proc)) return r;
        try {
            if (g.Proc.Contains("*")) {
                string pre = g.Proc.Replace("*", "");
                r.AddRange(Process.GetProcesses().Where(p => p.ProcessName.StartsWith(pre, StringComparison.OrdinalIgnoreCase)));
            } else r.AddRange(Process.GetProcessesByName(g.Proc));
        } catch { }
        return r;
    }
    public static bool IsRunning(Game g) {
        if (string.IsNullOrEmpty(g.Proc) && g.Steam != null) return SteamGames.RunningApp == g.Steam;   // Steam says which game runs
        if (string.IsNullOrEmpty(g.Proc) && g.ByStore) return Processes(g).Count > 0;
        if (string.IsNullOrEmpty(g.Proc)) return false;
        try {
            if (g.Proc.Contains("*")) {
                string pre = g.Proc.Replace("*", "");
                return Process.GetProcesses().Any(p => p.ProcessName.StartsWith(pre, StringComparison.OrdinalIgnoreCase));
            }
            return Process.GetProcessesByName(g.Proc).Length > 0;
        } catch { return false; }
    }
    public static bool RunsFrom(string dir) {
        foreach (var p in Process.GetProcesses()) {
            try { if (Paths.Under(p.MainModule.FileName, dir)) return true; } catch { }
        }
        return false;
    }
    public static void StartPlaytime(Game g, string playDir) {
        string tracker = Path.Combine(Paths.Root, "playtime-worker.ps1");
        if (File.Exists(tracker)) RunWorker(tracker, "-Id " + g.Id + " -Dir " + Q(playDir) + " -Store " + Q(Playtime.Path));
    }
    public static void TrimLogs() {
        try {
            Directory.CreateDirectory(Paths.Logs);
            foreach (var f in new DirectoryInfo(Paths.Logs).GetFiles("*.log").OrderByDescending(f => f.LastWriteTime).Skip(10)) f.Delete();
        } catch { }
    }
}

// One worker process and its log: every line is a JSON event (stage, text, step, progress, done)
class WorkerRun {
    public Process Proc;
    public string LogPath;
    long pos; string partial = "";
    public bool Finished;
    public IEnumerable<object> ReadEvents() {
        var list = new List<object>();
        if (LogPath == null || !File.Exists(LogPath)) return list;
        string chunk;
        try {
            using (var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                fs.Seek(pos, SeekOrigin.Begin);
                using (var sr = new StreamReader(fs, Encoding.UTF8)) { chunk = sr.ReadToEnd(); pos = fs.Position; }
            }
        } catch { return list; }
        if (string.IsNullOrEmpty(chunk)) return list;
        var lines = (partial + chunk).Split('\n');
        partial = lines[lines.Length - 1];
        for (int i = 0; i < lines.Length - 1; i++) {
            string l = lines[i].Trim().TrimStart('\uFEFF');
            if (l.Length == 0) continue;
            object ev;
            try { ev = Json.Parse(l); } catch { ev = l; }
            list.Add(ev ?? l);
        }
        return list;
    }
    public bool Exited { get { try { return Proc == null || Proc.HasExited; } catch { return true; } } }
}
}
