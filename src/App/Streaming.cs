// Streaming to a TV: Sunshine (free, on this PC) sends the picture to Moonlight (on the TV, an Apple TV or anything
// else with the app), and the controller paired with the TV plays on this PC. System > Streaming to a TV runs it from
// the launcher: is Sunshine there and running, sign in to it (the username and password you made on its own page the
// first time), pair a TV with the PIN Moonlight shows, put Recomp Launcher on the TV's list (it opens full screen on
// Home), and the TVs paired so far. Sunshine's own page (https://localhost:47990) is one press away for the rest.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace RecompLauncher {

static class Sunshine {
    public const string Page = "https://localhost:47990";
    public static string Exe { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Sunshine", "sunshine.exe"); } }
    public static bool Installed { get { return File.Exists(Exe); } }
    public static bool Running { get { try { return Process.GetProcessesByName("sunshine").Length > 0; } catch { return false; } } }
    public static bool SignedIn { get { return Settings.Get("SunshineUser").Length > 0; } }
    public const string AppName = "Recomp Launcher";
    // the controller driver Sunshine needs (ViGEmBus): without it the TV's controller does nothing on this PC
    public static bool PadDriver { get { try { using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\ViGEmBus")) return k != null; } catch { return false; } } }

    // ---------- installing: the official installers, checked, then run with Windows' permission (one prompt) ----------
    // Sunshine: its newest stable release on GitHub, checked against GitHub's SHA-256; ViGEmBus 1.22.0 from its maker.
    // Both must carry a valid Windows signature from their makers before they run.
    public class Download { public string Url, Name, Sha, Signer; public long Size; }
    public static Download LatestSunshine() {
        using (var wc = new WebClient()) {
            wc.Headers.Add("User-Agent", "RecompLauncher");
            var list = Json.Parse(wc.DownloadString("https://api.github.com/repos/SunshineStream/Sunshine/releases?per_page=10")) as System.Collections.IEnumerable;
            foreach (var r in list.OfType<Dictionary<string, object>>()) {
                if (Json.Bool(r, "prerelease") || Json.Bool(r, "draft")) continue;
                object a; if (!r.TryGetValue("assets", out a)) continue;
                foreach (var x in ((System.Collections.IEnumerable)a).OfType<Dictionary<string, object>>())
                    if (Json.Str(x, "name") == "Sunshine-Windows-AMD64-installer.msi")
                        return new Download { Url = Json.Str(x, "browser_download_url"), Name = Json.Str(x, "name"), Size = (long)Json.Num(x, "size"),
                            Sha = (Json.Str(x, "digest") ?? "").Replace("sha256:", ""), Signer = "David Lane" };
            }
        }
        return null;
    }
    public static Download PadDriverDownload() {
        return new Download { Url = "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe", Name = "ViGEmBus_1.22.0_x64_x86_arm64.exe",
            Sha = "89220a7865076b342892f98865f3499fb7c4cfd673159e89d352c360fd014c6a", Signer = "Nefarius Software Solutions" };
    }
    // downloads it into a folder of its own and checks it: null when it's fine, else why not
    public static string Fetch(Download d, out string file) {
        string dir = Path.Combine(Path.GetTempPath(), "RecompLauncher-setup-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        file = Path.Combine(dir, d.Name);
        using (var wc = new WebClient()) { wc.Headers.Add("User-Agent", "RecompLauncher"); wc.DownloadFile(d.Url, file); }
        if (!string.IsNullOrEmpty(d.Sha)) {
            string got;
            using (var s = File.OpenRead(file)) got = BitConverter.ToString(System.Security.Cryptography.SHA256.Create().ComputeHash(s)).Replace("-", "").ToLowerInvariant();
            if (got != d.Sha.ToLowerInvariant()) return "the download doesn't match its published SHA-256";
        }
        // Windows' own check of the signature (valid, and by its maker)
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            "-NoProfile -Command \"$s = Get-AuthenticodeSignature -LiteralPath '" + file.Replace("'", "''") + "'; $s.Status.ToString() + '|' + $s.SignerCertificate.Subject\"") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        string outp;
        using (var p = Process.Start(psi)) { outp = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(); }
        if (!outp.StartsWith("Valid|") || outp.IndexOf(d.Signer, StringComparison.OrdinalIgnoreCase) < 0) return "its signature isn't valid or isn't its maker's (" + outp + ")";
        return null;
    }
    // runs the checked installers and restarts Sunshine, all in one step with Windows' permission (its prompt shows
    // on this PC's screen); true when it ran
    public static bool InstallElevated(string sunshineMsi, string driverExe) {
        var cmd = new StringBuilder("$ErrorActionPreference = 'Continue'; ");
        if (sunshineMsi != null) cmd.Append("Start-Process msiexec.exe -ArgumentList '/i \"" + sunshineMsi.Replace("'", "''") + "\" /passive /norestart' -Wait; ");
        if (driverExe != null) cmd.Append("Start-Process '" + driverExe.Replace("'", "''") + "' -ArgumentList '/passive /norestart' -Wait; ");
        cmd.Append("Restart-Service SunshineService -Force -ErrorAction SilentlyContinue");
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            "-NoProfile -WindowStyle Hidden -Command \"" + cmd.ToString().Replace("\"", "\\\"") + "\"") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        try { using (var p = Process.Start(psi)) p.WaitForExit(); return true; } catch { return false; }   // the prompt was answered No (or nobody answered)
    }

    // Sunshine's own API, on this PC only (its certificate is its own, so it's trusted for localhost and nothing else)
    static object Call(string method, string path, object body) {
        var req = (HttpWebRequest)WebRequest.Create(Page + path);
        req.Method = method; req.Timeout = 6000; req.UserAgent = "RecompLauncher";
        req.ServerCertificateValidationCallback = (s, cert, chain, err) => req.RequestUri.IsLoopback;
        string auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(Settings.Get("SunshineUser") + ":" + Settings.Get("SunshinePass")));
        req.Headers[HttpRequestHeader.Authorization] = "Basic " + auth;
        if (body != null) {
            var b = Encoding.UTF8.GetBytes(Json.Write(body));
            req.ContentType = "application/json"; req.ContentLength = b.Length;
            using (var s = req.GetRequestStream()) s.Write(b, 0, b.Length);
        }
        using (var resp = (HttpWebResponse)req.GetResponse())
        using (var r = new StreamReader(resp.GetResponseStream())) return Json.Parse(r.ReadToEnd());
    }
    // a short reason when a call fails
    public static string Why(Exception e) {
        var we = e as WebException;
        if (we != null && we.Response is HttpWebResponse && ((HttpWebResponse)we.Response).StatusCode == HttpStatusCode.Unauthorized) return "Sunshine didn't accept that username and password.";
        if (we != null && we.Status == WebExceptionStatus.ConnectFailure) return "Sunshine isn't answering (is it running?).";
        return e.Message;
    }
    public static List<string> Apps() {
        var d = Call("GET", "/api/apps", null) as Dictionary<string, object>; object a;
        var l = new List<string>();
        if (d != null && d.TryGetValue("apps", out a) && a is System.Collections.IEnumerable)
            foreach (var x in ((System.Collections.IEnumerable)a).OfType<Dictionary<string, object>>()) l.Add(Json.Str(x, "name") ?? "");
        return l;
    }
    public static List<string> Paired() {
        var d = Call("GET", "/api/clients/list", null) as Dictionary<string, object>; object a;
        var l = new List<string>();
        if (d != null && d.TryGetValue("named_certs", out a) && a is System.Collections.IEnumerable)
            foreach (var x in ((System.Collections.IEnumerable)a).OfType<Dictionary<string, object>>()) l.Add(Json.Str(x, "name") ?? "A device");
        return l;
    }
    public static bool Pair(string pin, string name) {
        var d = Call("POST", "/api/pin", new Dictionary<string, object> { { "pin", pin }, { "name", name } }) as Dictionary<string, object>;
        return d != null && (Json.Bool(d, "status") || Json.Str(d, "status") == "true");
    }
    // the launcher as one of Sunshine's apps: it opens full screen on Home, and Sunshine waits while it runs
    public static void AddLauncher() {
        string exe = Path.Combine(Paths.Root, "Recomp Launcher.exe"), logo = Path.Combine(Paths.Root, "logo.png");
        Call("POST", "/api/apps", new Dictionary<string, object> {
            { "name", AppName }, { "output", "" }, { "cmd", "\"" + exe + "\" -Full -Startup" }, { "working-dir", Paths.Root },
            { "index", -1 }, { "exclude-global-prep-cmd", false }, { "elevated", false }, { "auto-detach", true }, { "wait-all", true }, { "exit-timeout", 5 },
            { "prep-cmd", new List<object>() }, { "detached", new List<object>() }, { "image-path", File.Exists(logo) ? logo : "" } });
    }
}

partial class Shell {
    ListPage streamPage; string streamNote; List<string> streamApps, streamPaired; bool streamInstalling;
    void InstallStreaming() {
        if (streamInstalling) return;
        streamInstalling = true; bool needSun = !Sunshine.Installed, needPad = !Sunshine.PadDriver;
        Action<string> say = t => W.Dispatcher.BeginInvoke(new Action(() => { streamNote = t; if (ListPageOpen && lpOpen == streamPage) FillStreaming(); }));
        ThreadPool.QueueUserWorkItem(_ => {
            string err = null, msi = null, drv = null;
            try {
                if (needSun) {
                    say("Getting Sunshine...");
                    var d = Sunshine.LatestSunshine();
                    if (d == null) err = "couldn't find Sunshine's installer";
                    else { err = Sunshine.Fetch(d, out msi); if (err != null) err = "Sunshine: " + err; }
                }
                if (err == null && needPad) {
                    say("Getting the controller driver...");
                    err = Sunshine.Fetch(Sunshine.PadDriverDownload(), out drv);
                    if (err != null) err = "the controller driver: " + err;
                }
                if (err == null) {
                    say("Installing: press Yes on the Windows prompt on this PC's screen.");
                    if (!Sunshine.InstallElevated(msi, drv)) err = "Windows' prompt was answered No, or nobody was at the PC";
                }
            } catch (Exception e) { err = e.Message; }
            W.Dispatcher.BeginInvoke(new Action(() => {
                streamInstalling = false;
                streamNote = err != null ? "Not installed: " + err + "." : Sunshine.Installed && Sunshine.PadDriver ? "Installed. Sunshine is starting." : "Done, but something is still missing: try again.";
                SetStep(streamNote, err == null ? Ui.Teal : Ui.Red);
                RefreshLater();
                if (ListPageOpen && lpOpen == streamPage) FillStreaming();
            }));
        });
    }
    void OpenStreaming() {
        if (streamPage == null) {
            streamPage = MakeListPage("Strm", "STREAMING", "Play on your TV", 780, "Pair a TV");
            streamPage.Fill = FillStreaming;
            streamPage.OnExtra = PairTv;
        }
        streamNote = null; streamApps = null; streamPaired = null;
        OpenListPage(streamPage);
        RefreshStreaming();
    }
    void RefreshStreaming() {
        if (!Sunshine.Installed || !Sunshine.Running || !Sunshine.SignedIn) { FillStreaming(); return; }
        ThreadPool.QueueUserWorkItem(_ => {
            List<string> apps = null, paired = null; string err = null;
            try { apps = Sunshine.Apps(); paired = Sunshine.Paired(); } catch (Exception e) { err = Sunshine.Why(e); }
            W.Dispatcher.BeginInvoke(new Action(() => {
                streamApps = apps; streamPaired = paired; if (err != null) streamNote = err;
                if (ListPageOpen && lpOpen == streamPage) FillStreaming();
            }));
        });
    }
    void FillStreaming() {
        var rows = new List<ListRow>();
        bool inst = Sunshine.Installed, run = Sunshine.Running, signed = Sunshine.SignedIn;
        if (!inst || !Sunshine.PadDriver)
            rows.Add(new ListRow { Glyph = "\uE896", C1 = "#2f8fe0", C2 = "#57cbde", Bold = true,
                Title = streamInstalling ? "Installing..." : !inst ? "Install Sunshine and the controller driver" : "Install the controller driver",
                Text = streamInstalling ? (streamNote ?? "") : (!inst ? "Sunshine (about 30 MB) and ViGEmBus, the driver that lets the TV's controller play here. " : "ViGEmBus: without it the TV's controller does nothing on this PC. ") + "Both are the official ones, checked before they run. Windows asks once on this PC's screen: press Yes there.",
                Do = streamInstalling ? null : (Action)InstallStreaming });
        rows.Add(new ListRow { Glyph = "", C1 = run ? "#5fae3a" : "#e8453c", C2 = run ? "#a8d86b" : "#f2a33a", Bold = true,
            Title = !inst ? "Sunshine isn't installed" : run ? "Sunshine is on" : "Sunshine is installed, but not running",
            Text = !inst ? "Sunshine (free) sends this PC's picture to the TV. Ask for it to be installed, or get it from its GitHub page."
                : run ? "Your TV can find this PC in Moonlight." : "It normally starts with Windows. A starts it now.",
            Do = !inst ? (Action)(() => Stores.Open("https://github.com/SunshineStream/Sunshine/releases/latest"))
                : run ? (Action)(() => StartBrowserExternal(Sunshine.Page)) : (Action)(() => { try { Process.Start(new ProcessStartInfo(Sunshine.Exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(Sunshine.Exe) }); } catch { } RefreshLater(); }) });
        if (inst) {
            rows.Add(new ListRow { Glyph = "", C1 = "#3a6fd8", C2 = "#6ea8ff",
                Title = signed ? "Signed in to Sunshine as " + Settings.Get("SunshineUser") : "Sign in to Sunshine",
                Text = signed ? "A signs in again with other details." : "The first time, open Sunshine's page (the first row) and make a username and password there. Then A here and type the same.",
                Do = SignInSunshine });
            if (signed && run) {
                bool listed = streamApps != null && streamApps.Contains(Sunshine.AppName);
                rows.Add(new ListRow { Glyph = "", C1 = "#e8453c", C2 = "#f2a33a", Title = listed ? "Recomp Launcher is on your TV's list" : "Put Recomp Launcher on your TV's list",
                    Text = listed ? "In Moonlight, pick this PC, then Recomp Launcher: it opens full screen on Home." : streamApps == null ? "Checking..." : "A adds it: in Moonlight it opens the launcher full screen on Home.",
                    Do = listed || streamApps == null ? null : (Action)(() => StreamAction(Sunshine.AddLauncher, "Recomp Launcher is on your TV's list now.")) });
                var paired = streamPaired ?? new List<string>();
                rows.Add(new ListRow { Glyph = "", C1 = "#5b6670", C2 = "#8a959e", Title = paired.Count == 0 ? "No TV paired yet" : paired.Count + (paired.Count == 1 ? " device paired" : " devices paired"),
                    Text = paired.Count == 0 ? "X (Pair a TV): Moonlight shows a 4-digit PIN when you pick this PC; type it here." : string.Join(", ", paired.ToArray()) });
            }
        }
        rows.Add(new ListRow { Glyph = "", C1 = "#7d8a93", C2 = "#b4bec6", Title = "On the TV",
            Text = "Install Moonlight from the TV's app store (on Apple TV: the App Store). Pair your controller with the TV (Apple TV: Settings > Remotes and Devices > Bluetooth). In Moonlight pick this PC; it shows a PIN to type here. A wired network, or 5 GHz Wi-Fi, gives the smoothest picture." });
        streamPage.Sub.Text = (streamNote != null ? streamNote + "  " : "") + "The TV shows this PC's screen and your controller (paired with the TV) plays here.";
        streamPage.Extra.IsEnabled = inst && run && signed;
        FillRows(streamPage, rows, "");
    }
    void RefreshLater() { var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) }; t.Tick += (s, e) => { t.Stop(); if (ListPageOpen && lpOpen == streamPage) RefreshStreaming(); }; t.Start(); }
    // Sunshine's page is opened in your usual browser: it has a certificate of its own, which browsers ask about once
    static void StartBrowserExternal(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }
    void SignInSunshine() {
        OpenOsk("Sunshine username", Settings.Get("SunshineUser"), null, user => {
            user = (user ?? "").Trim(); if (user.Length == 0) return;
            OpenOsk("Sunshine password", "", null, pass => {
                Settings.Set("SunshineUser", user); Settings.Set("SunshinePass", pass ?? ""); Settings.Save();
                streamNote = null; RefreshStreaming();
            }, null);
        }, null);
    }
    void PairTv() {
        OpenOsk("The PIN Moonlight shows (4 digits)", "", null, pin => {
            pin = new string((pin ?? "").Where(char.IsDigit).ToArray());
            if (pin.Length != 4) { SetStep("The PIN is the 4 digits Moonlight shows.", Ui.Gold); return; }
            ThreadPool.QueueUserWorkItem(_ => {
                string msg; bool ok = false;
                try { ok = Sunshine.Pair(pin, "TV " + DateTime.Now.ToString("d MMM")); msg = ok ? "Paired: the TV can stream now." : "Sunshine didn't take that PIN; pick the PC in Moonlight again for a new one."; }
                catch (Exception e) { msg = Sunshine.Why(e); }
                W.Dispatcher.BeginInvoke(new Action(() => {
                    SetStep(msg, ok ? Ui.Teal : Ui.Gold); streamNote = msg;
                    if (ok) MailBox.Add("paired:" + DateTime.Now.Ticks, "system", "A TV can stream from this PC", "It's paired with Sunshine: open Moonlight on it and pick Recomp Launcher.", null);
                    RefreshStreaming();
                }));
            });
        }, null);
    }
    void StreamAction(Action a, string done) {
        ThreadPool.QueueUserWorkItem(_ => {
            string err = null;
            try { a(); } catch (Exception e) { err = Sunshine.Why(e); }
            W.Dispatcher.BeginInvoke(new Action(() => { SetStep(err ?? done, err == null ? Ui.Teal : Ui.Red); streamNote = err; RefreshStreaming(); }));
        });
    }
}
}
