// System (a tile at the end of Home's row): the PC's settings in the launcher's style. Wi-Fi, Bluetooth, sound,
// display, controllers, storage, date and time, power, Windows Update and the launcher's own update. Each row says
// how it is now; A opens the launcher's own page for it, or the right page of Windows' Settings.
// Storage: how much each installed game takes and on which drive, with Move to another drive (the folder is moved,
// paths saved inside the game's own settings files are changed with it, and Start menu shortcuts follow).
// After the launcher updates itself, its first start shows what's new (and sends it as mail).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace RecompLauncher {

partial class Shell {
    ListPage sysPage, storePage, whatsNewPage;
    static void OpenWindowsSettings(string page) { try { Process.Start(new ProcessStartInfo("ms-settings:" + page) { UseShellExecute = true }); } catch { } }

    void OpenSystem() {
        if (sysPage == null) { sysPage = MakeListPage("Sys", "SYSTEM", "System settings", 760, null); sysPage.Fill = FillSystem; }
        OpenListPage(sysPage);
        // the slower facts (Wi-Fi name) in the background
        ThreadPool.QueueUserWorkItem(_ => { string w = WifiName(); W.Dispatcher.BeginInvoke(new Action(() => { sysWifi = w; if (ListPageOpen && lpOpen == sysPage) FillSystem(); })); });
    }
    string sysWifi;
    void FillSystem() {
        var rows = new List<ListRow>();
        Func<string, string, string, string, string, Action, ListRow> R = (glyph, c, title, text, right, act) => new ListRow { Glyph = glyph, C1 = c, C2 = c, Title = title, Text = text, Right = right, Do = act };
        rows.Add(R("", "#2f8fe0", "Internet", sysWifi == null ? "Looking..." : sysWifi.Length > 0 ? "Wi-Fi: " + sysWifi : "Wired, or not connected by Wi-Fi", "", () => OpenWindowsSettings("network-status")));
        rows.Add(R("", "#3a6fd8", "Bluetooth", "Pair controllers and headphones", "", () => { CloseListPage(); OpenBluetooth(); }));
        rows.Add(R("", "#5b6670", "Controllers", "Test buttons, battery, pairing", "", () => { CloseListPage(); OpenControllers(); }));
        string outName = ""; try { string cur; var outs = Audio.Outputs(out cur); var o = outs.FirstOrDefault(x => x.Id == cur); if (o != null) outName = o.Name; } catch { }
        rows.Add(R("", "#3fb8a0", "Sound", outName.Length > 0 ? "Playing on " + outName : "Volume and output", "", () => OpenWindowsSettings("sound")));
        var scr = System.Windows.Forms.Screen.AllScreens;
        rows.Add(R("", "#7b3fa0", "Display", scr.Length + (scr.Length == 1 ? " screen" : " screens") + ", " + string.Join(" and ", scr.Select(x => x.Bounds.Width + " x " + x.Bounds.Height).ToArray()), "", () => OpenWindowsSettings("display")));
        rows.Add(R("", "#f4a312", "Storage", StorageLine(), "", () => { CloseListPage(); OpenStorage(); }));
        rows.Add(R("", "#e8453c", "Date and time", DateTime.Now.ToString("dddd d MMMM, HH:mm"), "", () => OpenWindowsSettings("dateandtime")));
        rows.Add(R("", "#5fae3a", "Power and sleep", "When the screen turns off and the PC sleeps", "", () => OpenWindowsSettings("powersleep")));
        rows.Add(R("", "#2f8fe0", "Windows Update", "Updates for Windows itself", "", () => OpenWindowsSettings("windowsupdate")));
        rows.Add(R("", "#3fb8a0", "System update", "Recomp Launcher " + LauncherVersion.Current + (LauncherNewer ? ": " + launcherLatest.Tag + " is out" : ""), LauncherNewer ? "New" : "",
            () => { CloseListPage(); if (LauncherNewer) UpdateLauncher(); else { CheckLauncher(true); SetStep("Checking for a newer launcher...", Ui.Teal); } }));
        rows.Add(R("", "#7d8a93", "Launcher settings", "Theme, Home, sound, controller and more", "", () => { CloseListPage(); if (!running) OpenSettings(); }));
        sysPage.Sub.Text = Environment.MachineName + "  •  " + WindowsName() + "  •  " + DateTime.Now.ToString("HH:mm");
        FillRows(sysPage, rows, "");
    }
    // "Windows 11 25H2": Windows reports itself as 6.2 to programs that don't say they know newer ones
    static string WindowsName() {
        try {
            using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")) {
                int build; int.TryParse(Convert.ToString(k.GetValue("CurrentBuild")), out build);
                string ver = Convert.ToString(k.GetValue("DisplayVersion"));
                return (build >= 22000 ? "Windows 11" : "Windows 10") + (string.IsNullOrEmpty(ver) ? "" : " " + ver);
            }
        } catch { return "Windows"; }
    }
    static string WifiName() {
        try {
            var psi = new ProcessStartInfo("netsh", "wlan show interfaces") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            using (var p = Process.Start(psi)) {
                string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(3000);
                var m = Regex.Match(o, @"^\s*SSID\s*:\s*(.+)$", RegexOptions.Multiline);
                return m.Success ? m.Groups[1].Value.Trim() : "";
            }
        } catch { return ""; }
    }

    // ---------- storage ----------
    static IEnumerable<DriveInfo> GameDrives() {
        return DriveInfo.GetDrives().Where(d => { try { return d.IsReady && d.DriveType == DriveType.Fixed && d.TotalSize > 40L * 1024 * 1024 * 1024; } catch { return false; } });
    }
    static string SizeGb(long b) { return (b / 1073741824.0).ToString(b < 10L * 1073741824 ? "0.0" : "0") + " GB"; }
    static string StorageLine() {
        try { return string.Join("   ", GameDrives().Select(d => d.Name.TrimEnd('\\') + " " + SizeGb(d.AvailableFreeSpace) + " free").ToArray()); } catch { return ""; }
    }
    static readonly Dictionary<string, long> gameSizes = new Dictionary<string, long>();
    string moving; string moveNote; Game moveAsk;
    List<Game> StorageGames() {
        return Games.Where(g => g.App == null && !g.ByStore && g.SettingsKey != null && g.Inst != null && !string.IsNullOrEmpty(g.Dir) && Directory.Exists(g.Dir)).ToList();
    }
    void OpenStorage() {
        if (storePage == null) { storePage = MakeListPage("Store", "STORAGE", "Storage", 820, null); storePage.Fill = FillStorage; storePage.OnClose = () => { moveAsk = null; }; }
        moveAsk = null;
        OpenListPage(storePage);
        // the sizes, counted in the background
        var todo = StorageGames().Where(g => !gameSizes.ContainsKey(g.Dir)).ToList();
        if (todo.Count > 0)
            ThreadPool.QueueUserWorkItem(_ => {
                foreach (var g in todo) {
                    long n = 0;
                    try { n = new DirectoryInfo(g.Dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => { try { return f.Length; } catch { return 0L; } }); } catch { }
                    lock (gameSizes) gameSizes[g.Dir] = n;
                    W.Dispatcher.BeginInvoke(new Action(() => { if (ListPageOpen && lpOpen == storePage) FillStorage(); }));
                }
            });
    }
    void FillStorage() {
        var rows = new List<ListRow>();
        var drives = GameDrives().ToList();
        foreach (var d in drives) {
            long used = d.TotalSize - d.AvailableFreeSpace;
            rows.Add(new ListRow { Glyph = "", C1 = "#5b6670", C2 = "#8a959e", Title = "Drive " + d.Name.TrimEnd('\\') + (string.IsNullOrEmpty(d.VolumeLabel) ? "" : "  (" + d.VolumeLabel + ")"),
                Text = SizeGb(d.AvailableFreeSpace) + " free of " + SizeGb(d.TotalSize) + "  •  " + Bar(used / (double)d.TotalSize), Bold = true });
        }
        foreach (var g in StorageGames().OrderBy(x => x.Dir.Substring(0, 1)).ThenBy(x => x.Name)) {
            var game = g;
            long size; bool known; lock (gameSizes) known = gameSizes.TryGetValue(g.Dir, out size);
            string drive = Path.GetPathRoot(g.Dir).TrimEnd('\\');
            var target = drives.FirstOrDefault(d => !string.Equals(d.Name.TrimEnd('\\'), drive, StringComparison.OrdinalIgnoreCase) && (!known || d.AvailableFreeSpace > size + 2L * 1073741824));
            string text = drive + "  •  " + g.Dir;
            string right = known ? SizeGb(size) : "...";
            Action act = null;
            if (moving == g.Id) { text = moveNote ?? "Moving..."; right = ""; }
            else if (moving == null && target != null) {
                string to = target.Name.TrimEnd('\\');
                if (moveAsk == g) text = "Move to " + to + "? Press A again. Close the game first; it may take a while.";
                else text += "   (A: move to " + to + ")";
                act = () => {
                    if (moveAsk != game) { moveAsk = game; FillStorage(); return; }
                    moveAsk = null; MoveGame(game, Path.Combine(to + "\\Games", Path.GetFileName(game.Dir.TrimEnd('\\'))));
                };
            }
            rows.Add(new ListRow { Pic = g.IconIsArt ? (System.Windows.Media.ImageSource)g.Icon : null, Glyph = "", C1 = g.Accent, C2 = g.Accent2, Title = g.Name, Text = text, Right = right, Do = act });
        }
        storePage.Sub.Text = "Your installed games and where they are. A game can move to another drive: its folder, saves and settings go with it.";
        FillRows(storePage, rows, "");
    }
    static string Bar(double f) { int n = (int)Math.Round(Math.Max(0, Math.Min(1, f)) * 20); return new string('█', n) + new string('░', 20 - n); }

    // moves a game's folder to another drive, then points the launcher (and the files that name the old place) at it
    void MoveGame(Game g, string to) {
        if (playing == g || g.IsBusy || moving != null) { Sound.Play(Sfx.Bump); return; }
        if (Engine.IsRunning(g)) { SetStep("Close " + g.Name + " first, then move it.", Ui.Gold); Sound.Play(Sfx.Bump); return; }
        string from = g.Dir.TrimEnd('\\');
        if (Directory.Exists(to) && Directory.EnumerateFileSystemEntries(to).Any()) { SetStep(to + " already has files in it: move it by hand, or empty that folder.", Ui.Gold); return; }
        moving = g.Id; moveNote = "Moving to " + to + "..."; FillStorage();
        long total; lock (gameSizes) gameSizes.TryGetValue(g.Dir, out total);
        var tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        tick.Tick += (s, e) => {
            ThreadPool.QueueUserWorkItem(_ => {
                long done = 0; try { if (Directory.Exists(to)) done = new DirectoryInfo(to).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => { try { return f.Length; } catch { return 0L; } }); } catch { }
                W.Dispatcher.BeginInvoke(new Action(() => { if (moving == g.Id) { moveNote = "Moving to " + to + "...  " + (total > 0 ? (int)(100.0 * done / total) + "%" : SizeGb(done)); if (ListPageOpen && lpOpen == storePage) FillStorage(); } }));
            });
        };
        tick.Start();
        ThreadPool.QueueUserWorkItem(_ => {
            string err = null;
            try {
                Directory.CreateDirectory(to);
                var psi = new ProcessStartInfo("robocopy", "\"" + from + "\" \"" + to + "\" /E /MOVE /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NP /NFL /NDL /NJH /NJS") { UseShellExecute = false, CreateNoWindow = true };
                using (var p = Process.Start(psi)) { p.WaitForExit(); if (p.ExitCode >= 8) err = "robocopy stopped (code " + p.ExitCode + ")"; }
                if (err == null) FixPaths(to, from, to);
            } catch (Exception e) { err = e.Message; }
            W.Dispatcher.BeginInvoke(new Action(() => {
                tick.Stop(); moving = null;
                if (err == null) {
                    Settings.Set(g.SettingsKey, to); Settings.Save(); ApplySettings();
                    lock (gameSizes) { gameSizes.Remove(from); gameSizes[to] = total; }
                    try { MoveShortcuts(from, to); } catch { }
                    g.Inst = Engine.GetInstalled(g); UpdateViews();
                    SetStep(g.Name + " is now in " + to + ".", Ui.Teal);
                    MailBox.Add("moved:" + g.Id + ":" + to + ":" + DateTime.Now.Ticks, "system", g.Name + " moved", "It's now in " + to + ".", g.Id);
                } else SetStep("Couldn't move " + g.Name + ": " + err + ". What was copied is in " + to + "; the game still runs from " + from + ".", Ui.Red);
                if (ListPageOpen && lpOpen == storePage) FillStorage();
            }));
        });
    }
    // small text files in the game's folder that name the old place (its own settings) get the new one
    static void FixPaths(string dir, string from, string to) {
        var exts = new[] { ".json", ".ini", ".cfg", ".toml", ".xml", ".yml", ".yaml", ".txt", ".conf" };
        var forms = new[] { new[] { from, to }, new[] { from.Replace("\\", "\\\\"), to.Replace("\\", "\\\\") }, new[] { from.Replace('\\', '/'), to.Replace('\\', '/') } };
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) {
            try {
                if (!exts.Contains(Path.GetExtension(f).ToLowerInvariant()) || new FileInfo(f).Length > 2 * 1024 * 1024) continue;
                var bytes = File.ReadAllBytes(f);
                bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                string s = new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)), o = s;
                foreach (var p in forms) s = Regex.Replace(s, Regex.Escape(p[0]), p[1].Replace("$", "$$"), RegexOptions.IgnoreCase);
                if (s == o) continue;
                File.Copy(f, f + ".before-move.bak", true);
                var outb = new UTF8Encoding(bom).GetPreamble().Concat(new UTF8Encoding(false).GetBytes(s)).ToArray();
                File.WriteAllBytes(f, outb);
            } catch { }
        }
    }
    // Start menu and desktop shortcuts into the old folder follow it
    static void MoveShortcuts(string from, string to) {
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        var dirs = new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) };
        foreach (var d in dirs) {
            if (!Directory.Exists(d)) continue;
            foreach (var lnk in Directory.EnumerateFiles(d, "*.lnk", SearchOption.AllDirectories)) {
                try {
                    dynamic sc = shell.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                    string t = sc.TargetPath;
                    if (string.IsNullOrEmpty(t) || !t.StartsWith(from + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                    sc.TargetPath = to + t.Substring(from.Length);
                    string wd = sc.WorkingDirectory;
                    if (!string.IsNullOrEmpty(wd) && wd.StartsWith(from, StringComparison.OrdinalIgnoreCase)) sc.WorkingDirectory = to + wd.Substring(from.Length);
                    sc.Save();
                } catch { }
            }
        }
    }

    // ---------- after a launcher update: what's new ----------
    void ShowWhatsNewIfUpdated() {
        string seen = Settings.Get("SeenVersion");
        Settings.Set("SeenVersion", LauncherVersion.Current); Settings.Save();
        if (string.IsNullOrEmpty(seen) || seen == LauncherVersion.Current) return;
        ThreadPool.QueueUserWorkItem(_ => {
            string notes = null;
            try {
                using (var wc = new System.Net.WebClient()) {
                    wc.Headers.Add("User-Agent", "RecompLauncher"); wc.Encoding = Encoding.UTF8;
                    var d = Json.Parse(wc.DownloadString("https://api.github.com/repos/" + LauncherVersion.Repo + "/releases/tags/v" + LauncherVersion.Current)) as Dictionary<string, object>;
                    notes = d != null ? Json.Str(d, "body") : null;
                }
            } catch { }
            W.Dispatcher.BeginInvoke(new Action(() => {
                var lines = (notes ?? "").Replace("\r", "").Split('\n').Select(l => Regex.Replace(l, @"^[\s#*\-]+|[*_`]", "").Trim()).Where(l => l.Length > 0).Take(14).ToList();
                MailBox.Add("system:" + LauncherVersion.Current, "system", "Recomp Launcher " + LauncherVersion.Current, lines.Count > 0 ? lines[0] : "The launcher was updated.", null);
                if (whatsNewPage == null) whatsNewPage = MakeListPage("Wn", "SYSTEM UPDATE", "Updated to " + LauncherVersion.Current, 760, null);
                whatsNewPage.Fill = () => {
                    whatsNewPage.Sub.Text = "From " + seen + ". Your settings, profiles, saves and play time are all here.";
                    FillRows(whatsNewPage, lines.Select(l => new ListRow { Glyph = "", C1 = "#3fb8a0", C2 = "#57cbde", Title = l }).ToList(), "This update's notes are on the launcher's release page.");
                };
                OpenListPage(whatsNewPage);
            }));
        });
    }

    // ---------- start with Windows, as the PC's console ----------
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunName = "Recomp Launcher";
    static bool StartsWithWindows() {
        try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey)) { if (k != null && k.GetValue(RunName) != null) return true; } } catch { }
        return File.Exists(StartupLink);
    }
    // in the Run list (it starts as Windows opens the desktop), full screen on Home; the old Startup-folder shortcut
    // goes. Windows holds startup apps back for a few seconds: that wait is turned off so the launcher comes straight up.
    static void SetStartsWithWindows(bool on) {
        try {
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey)) {
                if (on) k.SetValue(RunName, "\"" + Path.Combine(Paths.Root, "Recomp Launcher.exe") + "\" -Full -Startup");
                else if (k.GetValue(RunName) != null) k.DeleteValue(RunName);
            }
            if (File.Exists(StartupLink)) File.Delete(StartupLink);
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize")) {
                if (on) { k.SetValue("StartupDelayInMSec", 0, Microsoft.Win32.RegistryValueKind.DWord); Settings.Set("StartupDelayOff", "on"); }
                else if (Settings.Get("StartupDelayOff") == "on") { try { k.DeleteValue("StartupDelayInMSec"); } catch { } Settings.Set("StartupDelayOff", ""); }
            }
        } catch { }
    }
}
}
