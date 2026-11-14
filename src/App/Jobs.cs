// Update checks, installs, updates, NSP conversion, uninstall and Play.
// Checks: only games that are installed are looked up (in parallel, in the background), and a release found in the
// last 20 minutes is used instead of asking again. A game that isn't installed is looked up only when it's needed:
// Install, or its release notes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace RecompLauncher {

class Job { public Game G; public string Kind; public bool KeepSaves; public NspJob Nsp; }
class NspJob { public string Base, Update, Keys, Out, TitleKeys; }

partial class Shell {
    const int CacheMinutes = 20;
    int checkGen, checksLeft;
    bool usedOld;
    DispatcherTimer autoTimer;
    static readonly SemaphoreSlim netGate = new SemaphoreSlim(6);

    // looks a game's latest release up in the background; done runs on the window's thread
    void Lookup(Game g, Action<Release, string> done) {
        string token = Settings.Get("GitHubToken");
        Task.Factory.StartNew(() => {
            netGate.Wait();
            try { return Tuple.Create(Engine.FetchLatest(g, token), (string)null); }
            catch (Exception e) { return Tuple.Create((Release)null, e.Message); }
            finally { netGate.Release(); }
        }).ContinueWith(t => W.Dispatcher.BeginInvoke(new Action(() => {
            var r = t.Result.Item1;
            if (r != null && r.Tag != null) { r.Checked = DateTime.Now.ToString("o"); ReleaseCache.Put(g.Id, r); }
            done(r, t.Result.Item2);
        })));
    }
    void SetLatest(Game g, Release r) {
        g.Latest = r;
        if (g.Inst == null) { if (!g.IsBusy) g.State = State.Missing; return; }
        if (g.IsBusy) return;
        bool newer = Versions.IsNewer(r, g.Inst);
        g.State = !newer ? State.Current : r.Problem != null ? State.Problem : State.Update;
    }

    void CheckAll(bool force) {
        int gen = ++checkGen;
        var todo = new List<Game>();
        foreach (var g in Games) {
            if (g.IsBusy) continue;
            g.Inst = Engine.GetInstalled(g);
            if (g.App != null) { g.State = State.Current; g.Latest = null; continue; }   // nothing to update
            if (g.ByStore) { g.State = g.Inst == null ? State.Missing : State.Current; g.Latest = Stores.Note(g); continue; }
            if (g.Inst == null) { g.State = State.Missing; g.Latest = ReleaseCache.Get(g.Id); continue; }
            var c = ReleaseCache.Get(g.Id);
            if (!force && c != null && (DateTime.Now - c.CheckedAt).TotalMinutes < CacheMinutes) { SetLatest(g, c); continue; }
            g.State = State.Checking;
            todo.Add(g);
        }
        usedOld = false;
        checksLeft = todo.Count;
        if (todo.Count == 0) { FinishCheck(); UpdateViews(); return; }
        checking = true;
        var icon = (System.Windows.Media.RotateTransform)N<System.Windows.Controls.TextBlock>("CheckIcon").RenderTransform;
        icon.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, Forever(Anim(360, 900, 0, null)));
        ((System.Windows.Media.RotateTransform)N<System.Windows.Controls.Image>("LogoImg").RenderTransform)
            .BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, Anim(360, 1200, 0, System.Windows.Media.Animation.EasingMode.EaseInOut));
        SetStep("Checking " + todo.Count + " installed game" + (todo.Count == 1 ? "" : "s") + " for updates...", Ui.Text);
        SetIndeterminate(true);
        foreach (var g0 in todo) {
            var g = g0;
            Lookup(g, (r, err) => {
                if (gen != checkGen) return;
                if (r == null || r.Tag == null) {
                    var old = ReleaseCache.Get(g.Id);   // unreachable: the last known release
                    if (old != null) { usedOld = true; SetLatest(g, old); }
                    else if (!g.IsBusy) g.State = State.Offline;
                } else SetLatest(g, r);
                if (g == sel && notesFor == g) FillNotes(g);
                if (--checksLeft <= 0) FinishCheck();
                UpdateViews();
            });
        }
        UpdateViews();
    }
    void FinishCheck() {
        bool was = checking;
        checking = false;
        if (was) {
            ((System.Windows.Media.RotateTransform)N<System.Windows.Controls.TextBlock>("CheckIcon").RenderTransform)
                .BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, Anim(360, 400));
            SetIndeterminate(false);
        }
        N<System.Windows.Controls.TextBlock>("LastCheck").Text = "Checked " + DateTime.Now.ToString("h:mm tt");
        if (running) return;
        var list = Listed();
        int n = list.Count(g => g.State == State.Update), off = list.Count(g => g.State == State.Offline);
        int inst = list.Count(g => g.Inst != null);
        string old = usedOld ? "Some release pages could not be reached; showing the last known versions." : "";
        if (n > 0) {
            SetStep(n == 1 ? "1 update available" : n + " updates available", Ui.Gold, old); SetProgress(0);
            var up = list.Where(g => g.State == State.Update).ToList();
            Notify(n == 1 ? "An update is out" : n + " updates are out", string.Join(", ", up.Take(3).Select(g => g.Name + " " + g.Latest.Tag).ToArray()) + (n > 3 ? " and more" : ""));
        }
        else if (off > 0) { SetStep("Couldn't reach the release pages", Ui.Red, "Check your internet connection, or add a GitHub token in Settings."); SetProgress(0); }
        else if (inst == 0) { SetStep("No games installed yet: pick one and press Install", Ui.Muted); SetProgress(0); }
        else { SetStep("Everything is up to date", Ui.Teal, old); SetProgress(1); }
    }
    void SetAutoCheck() {
        if (autoTimer == null) {
            autoTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(3) };
            autoTimer.Tick += (s, e) => { if (!running && !checking) CheckAll(true); };
        }
        if (Settings.On("AutoCheck")) autoTimer.Start(); else autoTimer.Stop();
    }

    // ---------- install ----------
    // where a game's setup download is unpacked (TotK's keeps its old name)
    static string SetupDirOf(Game g) { return g.Id == "totk" ? TotkSetupDir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"RecompLauncher\" + g.Id + "-setup"); }
    static string SetupExeOf(Game g) { return g.Setup ?? "TotK-Setup.exe"; }
    static string TotkSetupDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"RecompLauncher\totk-setup"); } }
    void StartInstall(Game g) {
        // no download known yet: look it up first (a game that isn't installed isn't checked at start)
        var c = g.Latest;
        if (c == null || c.Url == null || (DateTime.Now - c.CheckedAt).TotalMinutes > CacheMinutes) {
            SetStep("Looking up the " + g.Name + " download...", Ui.Text);
            SetIndeterminate(true);
            Lookup(g, (r, err) => {
                SetIndeterminate(false);
                if (r != null && r.Tag != null && r.Url != null) {
                    g.Latest = r;
                    if (g == sel) FillNotes(g);
                    SetStep("Found " + g.Name + " " + r.Tag, Ui.Teal);
                    UpdateViews();
                    ContinueInstall(g);
                } else if (r == null && c != null && c.Url != null) {
                    SetStep("Using the last known " + g.Name + " release (" + c.Tag + "): " + err, Ui.Gold);
                    ContinueInstall(g);
                } else {
                    string why = r != null && r.Problem != null ? r.Problem : err ?? "the release page couldn't be reached. Check your internet connection and try again.";
                    SetStep("Can't install " + g.Name + " yet: " + why, Ui.Red);
                    Log("Can't install " + g.Name + ": " + why, Ui.Red);
                }
            });
            return;
        }
        ContinueInstall(g);
    }
    void ContinueInstall(Game g) {
        if (g.Latest.Problem != null) { SetStep("Can't install " + g.Name + ": " + g.Latest.Problem, Ui.Red); return; }
        if (g.Id != "totk" && System.Text.RegularExpressions.Regex.IsMatch(g.Dir, @"^[A-Za-z]:\\Program Files", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) {
            MessageBox.Show(W, "Program Files isn't writable for games. Pick another folder in Settings.", "Recomp Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OpenRomPicker(g);   // asks for the player's game first; its Install button starts the download
    }

    void StartQueue(List<Game> list) {
        var busy = list.Where(Engine.IsRunning).ToList();
        if (busy.Count > 0) {
            MessageBox.Show(W, "Close " + string.Join(" and ", busy.Select(g => g.Name)) + " before updating.", "Recomp Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        RunJobs(list.Select(g => new Job { G = g, Kind = "update" }).ToList());
    }

    // ---------- the queue: one worker at a time, its log followed ----------
    readonly Queue<Job> queue = new Queue<Job>();
    readonly List<Tuple<Game, bool, string>> results = new List<Tuple<Game, bool, string>>();
    Job current;
    WorkerRun worker;
    DispatcherTimer poll;
    Game reopenRom; bool reopenNsp;

    void RunJobs(List<Job> jobs) {
        queue.Clear(); foreach (var j in jobs) queue.Enqueue(j);
        results.Clear();
        running = true;
        activity.Blocks.Clear();
        SetTab("activity");
        StartNext();
    }
    void StartNext() {
        if (queue.Count == 0) { FinishQueue(); return; }
        var j = queue.Dequeue(); var g = j.G;
        current = j;
        g.State = j.Kind == "update" ? State.Updating : j.Kind == "convert" ? State.Converting : j.Kind == "uninstall" ? State.Uninstalling : State.Installing;
        if (sel != g) ShowGame(g);
        UpdateViews();
        Engine.TrimLogs();
        string log = Path.Combine(Paths.Logs, g.Id + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
        worker = new WorkerRun { LogPath = log };
        switch (j.Kind) {
            case "update": Log(g.Name + ": updating " + g.Inst.Label + " to " + g.Latest.Tag, g.Accent); break;
            case "convert": Log(g.Name + ": converting " + Path.GetFileName(j.Nsp.Base) + (string.IsNullOrEmpty(j.Nsp.Update) ? "" : " with " + Path.GetFileName(j.Nsp.Update)) + " to exefs + romfs", g.Accent); break;
            case "uninstall": Log(g.Name + ": uninstalling from " + g.Dir, g.Accent); break;
            default: Log(g.Name + ": installing " + g.Latest.Tag, g.Accent); break;
        }
        SetStep(g.Name + ": starting...", Ui.Text);
        SetIndeterminate(true);
        string a;
        if (j.Kind == "uninstall") {
            a = "-Game " + Engine.Q(g.Dir) + " -Exe " + Engine.Q(g.Exe) + " -Log " + Engine.Q(log) + " -Saves " + Engine.Q(g.Saves ?? "") + " -BackupDir " + Engine.Q(g.Backups);
            if (j.KeepSaves) a += " -KeepSaves";
            worker.Proc = Engine.RunWorker(Path.Combine(Paths.Root, "uninstall-worker.ps1"), a);
        } else if (j.Kind == "convert") {
            var n = j.Nsp;
            a = "-Base " + Engine.Q(n.Base) + " -Keys " + Engine.Q(n.Keys) + " -Out " + Engine.Q(n.Out) + " -Log " + Engine.Q(log);
            if (!string.IsNullOrEmpty(n.Update)) a += " -Update " + Engine.Q(n.Update);
            if (!string.IsNullOrEmpty(n.TitleKeys)) a += " -TitleKeys " + Engine.Q(n.TitleKeys);
            worker.Proc = Engine.RunWorker(Path.Combine(Paths.Root, "nsp-worker.ps1"), a);
        } else {
            // a game with its own setup program is updated by running the new setup (it rebuilds the game)
            if (g.Setup != null && j.Kind == "update") j.Kind = "setup";
            bool totkWorker = g.Id == "totk" && j.Kind == "update";
            string script = totkWorker ? g.Worker : Path.Combine(Paths.Root, "recomp-worker.ps1");
            if (!File.Exists(script)) {
                FinishGame("failed", "The update helper is missing: " + script + ". Check the TotK Updater folder in Settings.", null, null);
                return;
            }
            if (totkWorker) a = "-Game " + Engine.Q(g.Dir) + " -Url " + Engine.Q(g.Latest.Url) + " -Tag " + Engine.Q(g.Latest.Tag) + " -Log " + Engine.Q(log);
            else {
                string dest = j.Kind == "setup" ? SetupDirOf(g) : g.Dir;
                bool pattern = g.Exe.IndexOfAny(new[] { '*', '?' }) >= 0;
                // a pattern Exe is a single-file release: it keeps the download's own name, older copies are removed; in a
                // zip or tarball the pattern finds the exe (named after its version), and the older ones go too
                string file = Path.GetFileName(new Uri(g.Latest.Url).LocalPath).ToLowerInvariant();
                bool archive = file.EndsWith(".zip") || file.EndsWith(".tar.gz") || file.EndsWith(".tgz");
                string exe = j.Kind == "setup" ? SetupExeOf(g) : pattern && !archive ? Path.GetFileName(new Uri(g.Latest.Url).LocalPath) : g.Exe;
                a = "-Mode " + j.Kind + " -Game " + Engine.Q(dest) + " -Url " + Engine.Q(g.Latest.Url) + " -Tag " + Engine.Q(g.Latest.Tag) +
                    " -Sha256 " + Engine.Q(g.Latest.Sha ?? "") + " -Log " + Engine.Q(log) + " -Exe " + Engine.Q(exe) + " -BackupDir " + Engine.Q(g.Backups) +
                    " -Backup " + Engine.Q(g.Backup ?? "") + " -Keep " + Engine.Q(g.Keep ?? "") + " -Replace " + Engine.Q(g.Replace ?? "");
                if (pattern) a += " -RemoveOld " + Engine.Q(g.Exe);
                if (g.NoSha && g.Latest.Sha == null) a += " -NoSha";
                if (j.Kind == "update" && (g.SetupArg != null || g.Rebuild != null)) a += " -NeedsSetup";
            }
            worker.Proc = Engine.RunWorker(script, a);
        }
        if (poll == null) {
            poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            poll.Tick += (s, e) => Poll();
        }
        poll.Start();
    }
    static string Names(string step) {
        switch (step) {
            case "backup": return "Backing up saves"; case "download": return "Downloading"; case "unpack": return "Unpacking";
            case "install": return "Starting installer"; case "check": return "Checking game files"; case "extract": return "Extracting game files";
            case "lift": return "Translating game code"; case "compile": return "Building totk.exe"; case "prepare": return "Building shaders";
            case "cleanup": return "Finishing up"; case "verify": return "Checking the download"; case "tools": return "Getting hactool"; case "keys": return "Reading your keys";
            default: return step;
        }
    }
    void Poll() {
        try {
            foreach (var o in worker.ReadEvents()) {
                var e = o as Dictionary<string, object>;
                if (e == null) { Log(Convert.ToString(o), Ui.Muted); continue; }
                HandleEvent(e);
                if (worker.Finished) return;
            }
            if (worker.Exited && !worker.Finished) {
                // the last lines may have been written as it ended
                foreach (var o in worker.ReadEvents()) { var e = o as Dictionary<string, object>; if (e != null) HandleEvent(e); if (worker.Finished) return; }
                FinishGame("failed", "The updater stopped unexpectedly.", null, null);
            }
        } catch (Exception ex) { Log("Launcher error: " + ex.Message, Ui.Red); }
    }
    void HandleEvent(Dictionary<string, object> e) {
        string who = current.G.Name, type = Json.Str(e, "type");
        switch (type) {
            case "stage": SetStep(who + ": " + Json.Str(e, "text")); SetIndeterminate(true); Log(Json.Str(e, "text")); break;
            case "text": Log(Json.Str(e, "text"), Ui.Muted); break;
            case "start": Log("Installer " + Json.Str(e, "release") + " in " + Json.Str(e, "mode") + " mode", Ui.Muted); break;
            case "step": {
                string name = Names(Json.Str(e, "step")), state = Json.Str(e, "state");
                if (state == "started") { SetStep(who + ": " + name); SetIndeterminate(true); Log(name); }
                else if (state == "done") Log(string.Format("  done in {0:N0} s", Json.Num(e, "seconds")), Ui.Teal);
                else if (state == "skipped") Log(name + "  -  skipped (" + Json.Str(e, "reason") + ")", Ui.Muted);
                else Log(name + "  -  " + state, Ui.Muted);
                break;
            }
            case "progress": {
                string phase = (Json.Str(e, "phase") ?? "").Replace('_', ' ');
                double total = Json.Num(e, "total"), done = Json.Num(e, "done");
                if (total > 0) {
                    double f = done / total; SetProgress(f);
                    N<System.Windows.Controls.TextBlock>("PctText").Text = string.Format("{0:N0}%", 100 * f);
                    string count = Json.Str(e, "unit") == "bytes" ? string.Format("{0:N0} / {1:N0} MB", done / 1048576, total / 1048576) : string.Format("{0:N0} / {1:N0} {2}", done, total, Json.Str(e, "unit"));
                    N<System.Windows.Controls.TextBlock>("DetailText").Text = phase + "   " + count;
                } else {
                    if (!indet) SetIndeterminate(true);
                    N<System.Windows.Controls.TextBlock>("PctText").Text = ""; N<System.Windows.Controls.TextBlock>("DetailText").Text = phase;
                }
                break;
            }
            case "end": case "plan": break;
            case "done": FinishGame(Json.Str(e, "result"), Json.Str(e, "message"), Json.Str(e, "backup"), Json.Str(e, "out")); break;
            default: {
                string color = type != null && (type.Contains("error") || type.Contains("fail")) ? Ui.Red : type != null && type.Contains("warn") ? Ui.Gold : Ui.Muted;
                Log(Json.Write(e).Replace("\n", " "), color);
                break;
            }
        }
    }
    void FinishGame(string result, string message, string backup, string outDir) {
        worker.Finished = true;
        poll.Stop();
        var g = current.G; string job = current.Kind;
        if (result == "ok") {
            switch (job) {
                case "update":
                    Log(g.Name + " updated to " + g.Latest.Tag + ". Saves and settings were kept.", Ui.Teal);
                    Board.Add("update", null, g.Id, g.Name + " was updated to " + g.Latest.Tag + ".");
                    if (backup != null) Log("Backup: " + backup, Ui.Muted);
                    if (g.Id == "smo") Log("The first start after an update can rebuild the game code (a few minutes).", Ui.Muted);
                    if (g.SetupArg != null) Log("The next Play runs the game's setup once, to rebuild the game for this version.", Ui.Muted);
                    if (g.Rebuild != null) Log("The next Play opens the builder once: build over the same folder with the same game to update it.", Ui.Muted);
                    break;
                case "install": Log(g.Name + " " + g.Latest.Tag + " is in " + g.Dir + ".", Ui.Teal); StartFirstRun(g); MailInstalled(g); break;
                case "uninstall": Log(g.Name + " is uninstalled.", Ui.Teal); if (backup != null) Log("Saves and settings kept in " + backup, Ui.Muted); break;
                case "convert":
                    Log(g.Name + ": exefs and romfs are in " + outDir + ".", Ui.Teal);
                    Roms.Set(g.Id, outDir); reopenRom = g;
                    break;
                case "setup": {
                    string sdir = SetupDirOf(g), sname = SetupExeOf(g);
                    string exe = Directory.Exists(sdir) ? Directory.GetFiles(sdir, sname, SearchOption.AllDirectories).FirstOrDefault() : null;
                    if (exe != null) {
                        Log("Started the " + g.Short + " setup. Choose your game files, and " + g.Dir + " as the install folder (or change it in Settings to match).", Ui.Teal);
                        Engine.Start(exe, null, Path.GetDirectoryName(exe), false);
                    } else Log("The " + g.Short + " download has no " + sname + " in it.", Ui.Red);
                    break;
                }
            }
            g.Inst = Engine.GetInstalled(g);
            Icons.Load(g); ApplyIcon(g);
            g.State = State.Missing;
            if (g.Inst != null) { if (g.Latest != null) SetLatest(g, g.Latest); else g.State = State.Current; }
            results.Add(Tuple.Create(g, true, job));
        } else {
            string what = job == "update" ? "update" : job == "convert" ? "NSP conversion" : job == "uninstall" ? "uninstall" : "install";
            Log(g.Name + " " + what + " failed: " + message, Ui.Red);
            Log("Log: " + worker.LogPath, Ui.Muted);
            g.Inst = Engine.GetInstalled(g);
            g.State = g.Inst == null ? State.Missing : job == "update" ? State.Update : State.Current;
            if (job == "convert") { reopenRom = g; reopenNsp = true; }
            results.Add(Tuple.Create(g, false, job));
        }
        UpdateViews();
        StartNext();
    }
    void FinishQueue() {
        current = null; running = false;
        SetIndeterminate(false);
        var failed = results.Where(r => !r.Item2).ToList();
        var jobs = results.Select(r => r.Item3).ToList();
        string word = jobs.Contains("uninstall") ? "Uninstall" : jobs.Contains("convert") ? "Conversion" : jobs.Any(x => x != "update") ? "Install" : "Update";
        if (failed.Count == 0) { SetProgress(1); SetStep(word + " complete", Ui.Teal, "", "100%"); System.Media.SystemSounds.Asterisk.Play(); }
        else {
            SetProgress(0);
            SetStep(word + " failed: " + string.Join(", ", failed.Select(r => r.Item1.Name)) + "  •  details in What's new > Activity", Ui.Red);
            System.Media.SystemSounds.Hand.Play();
        }
        UpdateViews();
        if (reopenRom != null) {
            var g = reopenRom; reopenRom = null;
            ShowGame(g); OpenRomPicker(g);
            if (reopenNsp) { reopenNsp = false; ShowNspPanel(true); }
        }
    }

    // ---------- first start after an install: the game's own setup, with the chosen game handed over ----------
    void StartFirstRun(Game g) {
        string rom = g.RomPath; var spec = g.Rom;
        if (rom != null && spec.Handoff == "tod") {
            string setup = Path.Combine(g.Dir, "tod_setup.exe");
            if (File.Exists(setup)) {
                Log("Installing the game from " + rom + " (tod_setup.exe --install). This copies the disc, about 24 GB.", Ui.Muted);
                Engine.Start(setup, "--install --base " + Engine.Q(g.Dir) + " " + Engine.Q(rom), g.Dir, false);
                return;
            }
        }
        if (rom != null && spec.Handoff == "nectar") {
            string nl = Path.Combine(g.Dir, "nectar-launcher.exe");
            if (File.Exists(nl)) {
                Log("Open Nectar is checking your disc (" + rom + ") and extracting the game: a minute or two.", Ui.Muted);
                Engine.Start(nl, "--rom " + Engine.Q(rom) + " --install-dir " + Engine.Q(g.Dir), g.Dir, false);
                return;
            }
        }
        string first = g.FirstRun == g.Exe ? Engine.ResolveExe(g) : g.FirstRun != null ? Path.Combine(g.Dir, g.FirstRun) : null;
        if (first == null || !File.Exists(first)) return;
        if (rom != null && spec.CopyAs != null && File.Exists(rom)) {
            try {
                string dest = Path.Combine(g.Dir, spec.CopyAs);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));   // CopyAs can be in a folder ("data\pd.ntsc-final.z64")
                File.Copy(rom, dest, true);
                Log("Copied your game in as " + spec.CopyAs + ".", Ui.Muted);
                Engine.Start(first, null, g.Dir, false);
                return;
            } catch (Exception e) { Log("Couldn't copy your game in: " + e.Message, Ui.Red); }
        }
        if (rom != null && spec.CopyIn.Length > 0 && Directory.Exists(rom)) {
            var cmds = spec.CopyIn.Select(f => "robocopy " + Engine.Q(Path.Combine(rom, f)) + " " + Engine.Q(Path.Combine(g.Dir, f)) + " /E /NFL /NDL /NJH /NP");
            string line = string.Join(" & ", cmds) + " & cd /d " + Engine.Q(g.Dir) + " & call " + Engine.Q(first);
            Log("Copying " + string.Join(" and ", spec.CopyIn) + " from " + rom + " into the game's folder, then starting its setup.", Ui.Muted);
            Engine.Start("cmd.exe", "/c \"" + line + "\"", g.Dir, false);
            return;
        }
        if (rom != null) {
            try { Clipboard.SetText(rom); } catch { }
            Log("Starting its setup. Your game is " + rom + "; that path is on the clipboard: paste it (Ctrl+V) into the setup's file box.", Ui.Muted);
            SetStep(g.Name + ": paste your game's path into its setup (Ctrl+V)", Ui.Teal);
        } else Log("Starting its setup: choose your game there. " + g.Needs, Ui.Muted);
        Engine.Start(first, null, g.Dir, first.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) && !g.Console);
    }

    // ---------- Play ----------
    void StartGame(Game g) {
        string args;
        string file = Engine.PlayFile(g, out args, true);
        if (!File.Exists(file)) { SetStep(g.Name + ": " + Path.GetFileName(file) + " isn't there. Install it again, or check its folder in Settings.", Ui.Red); return; }
        // play time watches the folder the game runs from
        string playDir = Paths.Under(file, g.Dir) ? g.Dir : Path.GetDirectoryName(file);
        bool hidden = file.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) && !g.Console;
        // the current profile's saves go in place first (not while the game is already running)
        if (!Engine.IsRunning(g)) {
            try { Profiles.PrepareSaves(g); }
            catch (Exception e) {
                SetStep("Couldn't put " + Profiles.DisplayName(Profiles.Current) + "'s saves in place for " + g.Name + ", so it wasn't started: " + e.Message, Ui.Red);
                Log("Save switch for " + g.Name + " failed: " + e, Ui.Red);
                return;
            }
        }
        Engine.Start(file, args, playDir, hidden);
        SetStep("Starting " + g.Name + "...", Ui.Teal);
        Engine.StartPlaytime(g, playDir);
        WatchGame(g);
        if (!running) { string mode = OnPlayMode(); if (mode == "close") CloseLauncher(); else if (mode == "tray") WaitInTray(g); }
    }
    void CloseLauncher() {
        W.Topmost = false;
        var root = N<System.Windows.Controls.Border>("Root");
        var a = Anim(0, 300, null, System.Windows.Media.Animation.EasingMode.EaseIn);
        // the start sound rings out before the launcher goes
        a.Completed += (s, e) => {
            var wait = Sound.BusyUntil - DateTime.Now;
            if (wait.TotalMilliseconds <= 0) { W.Close(); return; }
            W.Hide();
            var t = new System.Windows.Threading.DispatcherTimer { Interval = wait };
            t.Tick += (s2, e2) => { t.Stop(); W.Close(); };
            t.Start();
        };
        root.BeginAnimation(UIElement.OpacityProperty, a);
        var rs = (System.Windows.Media.ScaleTransform)root.RenderTransform;
        rs.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, Anim(0.97, 300, null, System.Windows.Media.Animation.EasingMode.EaseIn));
        rs.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, Anim(0.97, 300, null, System.Windows.Media.Animation.EasingMode.EaseIn));
    }
}
}
