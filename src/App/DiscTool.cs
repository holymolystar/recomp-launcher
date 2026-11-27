// GameCube and Wii discs in the form a port needs, with Dolphin's DolphinTool (games.json Rom "Disc"):
//   "iso"      an .rvz, .wia or .gcz you pick is converted to an .iso in the game's folder first (Pikmin, Super
//              Mario Sunshine...); an .iso, .gcm or .wbfs is handed over as it is
//   "extract"  the disc you pick (.iso, .rvz, .wbfs...) is unpacked into the game folder's "game" folder, where the
//              port finds it by itself (New Super Mario Bros. Wii: game\sys and game\files)
// DolphinTool comes from a Dolphin you have installed, or is downloaded once (about 20 MB) from dolphin-emu.org into
// tools\dolphin. Your own disc file is only read, never changed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows;

namespace RecompLauncher {

static class DiscTool {
    static readonly string[] Packed = { ".rvz", ".wia", ".gcz" };
    public static bool IsPacked(string file) { return Packed.Contains(Path.GetExtension(file ?? "").ToLowerInvariant()); }
    static string Own { get { return Path.Combine(Paths.Root, @"tools\dolphin"); } }
    // a DolphinTool.exe: the launcher's own, then Dolphin installs in the usual places
    public static string Find() {
        var places = new List<string> { Own };
        foreach (var pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                                   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs") })
            if (!string.IsNullOrEmpty(pf) && Directory.Exists(pf))
                try { places.AddRange(Directory.GetDirectories(pf, "Dolphin*")); } catch { }
        foreach (var d in places) {
            if (!Directory.Exists(d)) continue;
            try { var f = Directory.GetFiles(d, "DolphinTool.exe", SearchOption.AllDirectories).FirstOrDefault(); if (f != null) return f; } catch { }
        }
        return null;
    }
    // what the picker says about a disc you picked
    public static string Note(RomSpec s, string file) {
        string extra = Find() == null ? " Dolphin's DolphinTool (about 20 MB) is downloaded once from dolphin-emu.org to do it." : "";
        if (s.Disc == "iso" && IsPacked(file)) return ". It's converted to an ISO in the game's folder first (a few minutes; yours stays as it is)." + extra;
        if (s.Disc == "extract") return ". After the install it's unpacked into the game's folder, where the game finds it (a few minutes; yours stays as it is)." + extra;
        return "";
    }

    // ---------- getting DolphinTool: the newest Dolphin release's 7z, unpacked with Windows' own tar ----------
    const string Fallback = "2609b";   // a release known to be there
    static bool Exists(string url) {
        try {
            var r = (HttpWebRequest)WebRequest.Create(url); r.Method = "HEAD"; r.UserAgent = "Mozilla/5.0 (Recomp Launcher)"; r.Timeout = 10000;
            using (var resp = (HttpWebResponse)r.GetResponse()) return resp.StatusCode == HttpStatusCode.OK;
        } catch { return false; }
    }
    static string Url(string v) { return "https://dl.dolphin-emu.org/releases/" + v + "/dolphin-" + v + "-x64.7z"; }
    // Dolphin's releases are named year-month ("2609"), with hotfix letters ("2609b"): the newest one of the last
    // year that's there
    static string Newest() {
        for (var m = DateTime.Now; m > DateTime.Now.AddMonths(-14); m = m.AddMonths(-1)) {
            string v = m.ToString("yyMM");
            if (!Exists(Url(v))) continue;
            string best = v;
            foreach (var letter in "abcdef") { if (Exists(Url(v + letter))) best = v + letter; else break; }
            return best;
        }
        return Fallback;
    }
    public static string Get(Action<string> say) {
        string have = Find(); if (have != null) return have;
        string v = Newest();
        say("Downloading Dolphin " + v + " for its DolphinTool (about 20 MB, from dolphin-emu.org)...");
        Directory.CreateDirectory(Own);
        string arc = Path.Combine(Own, "dolphin-" + v + "-x64.7z");
        using (var wc = new WebClient()) { wc.Headers.Add("User-Agent", "Mozilla/5.0 (Recomp Launcher)"); wc.DownloadFile(Url(v), arc); }
        say("Unpacking DolphinTool...");
        string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
        var p = Process.Start(new ProcessStartInfo(tar, "-xf " + Engine.Q(arc) + " -C " + Engine.Q(Own)) { UseShellExecute = false, CreateNoWindow = true });
        p.WaitForExit(5 * 60 * 1000);
        try { File.Delete(arc); } catch { }
        have = Find();
        if (have == null) throw new Exception("DolphinTool wasn't in Dolphin " + v + "'s download.");
        return have;
    }

    // runs DolphinTool; while it works, every 2 seconds says how far it got (the size written so far)
    public static void Run(string tool, string args, Func<string> progress, Action<string> say) {
        var psi = new ProcessStartInfo(tool, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = Path.GetDirectoryName(tool) };
        using (var p = Process.Start(psi)) {
            var err = p.StandardError.ReadToEndAsync(); p.StandardOutput.ReadToEndAsync();
            while (!p.WaitForExit(2000)) { try { say(progress()); } catch { } }
            if (p.ExitCode != 0) throw new Exception("DolphinTool stopped (code " + p.ExitCode + "): " + (err.Result ?? "").Trim());
        }
    }
    public static long FolderSize(string dir) {
        try { return Directory.Exists(dir) ? new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0; } catch { return 0; }
    }
    public static string Gb(long b) { return string.Format("{0:N1} GB", b / 1073741824.0); }
}

partial class Shell {
    readonly HashSet<Game> discReady = new HashSet<Game>();
    // before a game's first start: the disc in the form it needs. True when it's working on that (then is called after)
    bool PrepareDisc(Game g, Action then) {
        var spec = g.Rom; string rom = g.RomPath;
        if (discReady.Contains(g)) { discReady.Remove(g); return false; }
        if (spec.Disc == null || rom == null || !File.Exists(rom)) return false;
        bool toIso = spec.Disc == "iso" && DiscTool.IsPacked(rom), extract = spec.Disc == "extract";
        if (!toIso && !extract) return false;
        Action<string> say = t => W.Dispatcher.BeginInvoke(new Action(() => SetStep(g.Name + ": " + t, Ui.Teal)));
        Action<string> log = t => W.Dispatcher.BeginInvoke(new Action(() => Log(t, Ui.Muted)));
        SetIndeterminate(true);
        new Thread(() => {
            string error = null;
            try {
                string tool = DiscTool.Get(t => { say(t); log(t); });
                if (toIso) {
                    string iso = Path.Combine(g.Dir, Path.GetFileNameWithoutExtension(rom) + ".iso");
                    log("Converting " + Path.GetFileName(rom) + " to " + iso + " with DolphinTool.");
                    say("converting your disc to ISO...");
                    DiscTool.Run(tool, "convert -i " + Engine.Q(rom) + " -o " + Engine.Q(iso) + " -f iso",
                        () => "converting your disc to ISO: " + DiscTool.Gb(File.Exists(iso) ? new FileInfo(iso).Length : 0) + " so far", say);
                    g.RomPath = iso;
                    log("Your disc is now " + iso + ".");
                } else {
                    // unpacked next to the game as game\sys and game\files (the DATA partition, as Dolphin's Extract Entire Disc does)
                    string work = Path.Combine(g.Dir, "game-unpacking"), dest = Path.Combine(g.Dir, "game");
                    if (Directory.Exists(work)) Directory.Delete(work, true);
                    log("Unpacking " + Path.GetFileName(rom) + " into " + dest + " with DolphinTool.");
                    DiscTool.Run(tool, "extract -i " + Engine.Q(rom) + " -o " + Engine.Q(work) + " -g -q",
                        () => "unpacking your disc: " + DiscTool.Gb(DiscTool.FolderSize(work)) + " so far", say);
                    string from = Directory.Exists(Path.Combine(work, "DATA")) ? Path.Combine(work, "DATA") : work;   // a Wii disc's partition, or a GameCube disc as it is
                    if (Directory.Exists(dest)) Directory.Delete(dest, true);
                    Directory.Move(from, dest);
                    if (Directory.Exists(work)) Directory.Delete(work, true);
                    g.RomPath = null;   // nothing to hand over: the game finds its "game" folder
                    log("Your disc is unpacked in " + dest + " (" + DiscTool.Gb(DiscTool.FolderSize(dest)) + ").");
                }
            } catch (Exception e) { error = e.Message; }
            W.Dispatcher.BeginInvoke(new Action(() => {
                SetIndeterminate(false);
                if (error != null) { Log(g.Name + ": your disc couldn't be prepared: " + error, Ui.Red); SetStep(g.Name + ": your disc couldn't be prepared (details in What's new > Activity)", Ui.Red); return; }
                SetStep(g.Name + ": your disc is ready", Ui.Teal);
                discReady.Add(g); then();
            }));
        }) { IsBackground = true }.Start();
        return true;
    }
}
}
