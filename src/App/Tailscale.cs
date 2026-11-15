// Online play through Tailscale, for the games that find each other over it (games.json "Online": "tailscale",
// New Super Mario Bros. Wii): the game asks `tailscale status` for the other players' PCs by itself, so all the
// launcher does is get Tailscale ready. The game page's Online play button shows where it stands and the next
// step: install it (winget, from Tailscale's own package), sign in (Tailscale's own page in the browser: you sign
// in there yourself), connect, and who else is on your Tailscale network. Starting such a game connects Tailscale
// first when it's installed, signed in and switched off.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace RecompLauncher {

class TsPeer { public string Name, Os, Ip; public bool Online; }
class TsStatus {
    public bool Installed;
    public string State;   // Running, NeedsLogin, Stopped, NoState, Starting... or null when the service doesn't answer
    public string Name, Ip, Error;
    public List<TsPeer> Peers = new List<TsPeer>();
    public bool Connected { get { return State == "Running"; } }
}

static class Tailscale {
    public static string Exe {
        get {
            foreach (var p in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }) {
                if (string.IsNullOrEmpty(p)) continue;
                string f = Path.Combine(p, @"Tailscale\tailscale.exe");
                if (File.Exists(f)) return f;
            }
            return null;
        }
    }
    // runs tailscale.exe and gives back what it printed (stdout, else stderr); null when it didn't finish in time
    static string Run(string args, int waitMs, out int code) {
        code = -1;
        string exe = Exe; if (exe == null) return null;
        try {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi)) {
                var outTask = p.StandardOutput.ReadToEndAsync(); var errTask = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(waitMs)) { try { p.Kill(); } catch { } return null; }
                code = p.ExitCode;
                string o = outTask.Result; return o.Trim().Length > 0 ? o : errTask.Result;
            }
        } catch (Exception e) { return e.Message; }
    }
    public static TsStatus Status() {
        var s = new TsStatus { Installed = Exe != null };
        if (!s.Installed) return s;
        int code; string json = Run("status --json", 8000, out code);
        var d = json != null ? Json.Parse(json) as Dictionary<string, object> : null;
        if (d == null) { s.Error = (json ?? "").Trim(); return s; }
        s.State = Json.Str(d, "BackendState");
        var self = Json.Obj(d, "Self");
        if (self != null) { s.Name = HostName(self); s.Ip = FirstIp(self); }
        var peers = Json.Obj(d, "Peer");
        if (peers != null)
            foreach (var kv in peers) {
                var p = kv.Value as Dictionary<string, object>; if (p == null) continue;
                s.Peers.Add(new TsPeer { Name = HostName(p), Os = Json.Str(p, "OS"), Ip = FirstIp(p), Online = Json.Bool(p, "Online") });
            }
        s.Peers = s.Peers.OrderByDescending(p => p.Online).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return s;
    }
    static string HostName(Dictionary<string, object> n) {
        string dns = (Json.Str(n, "DNSName") ?? "").TrimEnd('.');
        string first = dns.Split('.')[0];
        return first.Length > 0 ? first : Json.Str(n, "HostName") ?? "?";
    }
    static string FirstIp(Dictionary<string, object> n) {
        object ips; if (!n.TryGetValue("TailscaleIPs", out ips) || !(ips is System.Collections.IEnumerable)) return null;
        return ((System.Collections.IEnumerable)ips).Cast<object>().Select(x => x as string).FirstOrDefault(x => x != null && x.StartsWith("100."));
    }
    // connect (the same as switching it on in Tailscale's tray menu); signing in opens Tailscale's page in the browser
    public static void Up() { int c; Run("up", 20000, out c); }
    public static void Down() { int c; Run("down", 10000, out c); }
    public static void Login() { try { Process.Start(new ProcessStartInfo(Exe, "login") { UseShellExecute = false, CreateNoWindow = true }); } catch { } }
    // before an online game starts: switch Tailscale on when it's installed, signed in and off (in the background)
    public static void EnsureUp() {
        if (Exe == null) return;
        new Thread(() => { try { var s = Status(); if (s.State == "Stopped") Up(); } catch { } }) { IsBackground = true }.Start();
    }
}

partial class Shell {
    ListPage onlinePage; Game onlineGame;
    void OpenOnline(Game g) {
        onlineGame = g;
        if (onlinePage == null) {
            onlinePage = MakeListPage("Online", "ONLINE PLAY", "Play over the internet", 700, "Refresh");
            onlinePage.OnExtra = () => FillOnline();
            onlinePage.Fill = () => FillOnline();
        }
        OpenListPage(onlinePage);
    }
    // Tailscale is asked in the background (it can take a second); the page fills in when it answers
    void FillOnline() {
        var p = onlinePage; var g = onlineGame;
        p.Title.Text = "Play " + (g != null ? g.Name : "") + " online";
        p.Sub.Text = "Asking Tailscale...";
        FillRows(p, new List<ListRow>(), "");
        new Thread(() => {
            TsStatus s; try { s = Tailscale.Status(); } catch (Exception e) { s = new TsStatus { Installed = Tailscale.Exe != null, Error = e.Message }; }
            W.Dispatcher.BeginInvoke(new Action(() => { if (ListPageOpen && lpOpen == p) ShowOnline(s); }));
        }) { IsBackground = true }.Start();
    }
    void ShowOnline(TsStatus s) {
        var p = onlinePage; var g = onlineGame;
        var rows = new List<ListRow>();
        string how = "Everyone installs Tailscale (free) and joins the same Tailscale network. Then in the game: press Up on the save file screen, switch Couch to LAN and create a room; friends pick LAN and Join, and your room is in their list. Every player needs their own copy of the game and the same version of the port.";
        Action again = () => W.Dispatcher.BeginInvoke(new Action(() => { if (ListPageOpen && lpOpen == p) FillOnline(); }));
        if (!s.Installed) {
            p.Sub.Text = "Tailscale isn't installed. It joins your PC and your friends' into one private network over the internet, so the game finds their rooms as if you were at home together.\n\n" + how;
            rows.Add(new ListRow { Glyph = "", C1 = "#3fb8a0", C2 = "#2f8fe0", Title = "Install Tailscale", Text = "From Tailscale's own package, with Windows' winget. Windows asks for permission once.",
                Do = () => InstallTailscale() });
            rows.Add(new ListRow { Glyph = "", C1 = "#5b6670", C2 = "#5b6670", Title = "Tailscale's website", Text = "tailscale.com/download, to install it yourself.", Do = () => Stores.Open("https://tailscale.com/download/windows") });
        } else if (s.State == null) {
            p.Sub.Text = "Tailscale is installed but isn't answering. " + (s.Error ?? "") + "\nStart it from the Start menu (it sits in the tray), then refresh.";
            rows.Add(new ListRow { Glyph = "", C1 = "#3fb8a0", C2 = "#2f8fe0", Title = "Start Tailscale", Do = () => { StartTailscaleApp(); DelayThen(4000, again); } });
        } else if (s.State == "NeedsLogin" || s.State == "NoState") {
            p.Sub.Text = "Tailscale is installed. Sign in to it: its page opens in your browser, where you sign in yourself (a Google, Microsoft, GitHub or Apple account works). Your friends sign in with their own accounts, and you share your PC with them from Tailscale's page, or you all use one account.\n\n" + how;
            rows.Add(new ListRow { Glyph = "", C1 = "#3fb8a0", C2 = "#2f8fe0", Title = "Sign in to Tailscale", Text = "Opens Tailscale's sign-in page in your browser.", Do = () => { Tailscale.Login(); DelayThen(8000, again); } });
        } else if (!s.Connected) {
            p.Sub.Text = "Tailscale is " + (s.State == "Stopped" ? "switched off" : s.State.ToLowerInvariant()) + ". " + (g != null ? "It switches on by itself when you start " + g.Name + ", or now:" : "");
            rows.Add(new ListRow { Glyph = "", C1 = "#3fb8a0", C2 = "#2f8fe0", Title = "Connect", Do = () => RunThen(Tailscale.Up, again) });
        } else {
            int on = s.Peers.Count(x => x.Online);
            p.Sub.Text = "Connected as " + s.Name + (s.Ip != null ? " (" + s.Ip + ")" : "") + ". " +
                (s.Peers.Count == 0 ? "Nobody else is on your Tailscale network yet: share your PC with your friends from Tailscale's page, or have them sign in with the same account." :
                 on + " of " + s.Peers.Count + (s.Peers.Count == 1 ? " device" : " devices") + " online. The game finds them by itself: create a room under LAN, or join theirs.") +
                "\nIf Windows asks the first time you host a room, allow the game through the firewall, or others can't find it.";
            foreach (var peer in s.Peers.Take(20))
                rows.Add(new ListRow { Glyph = peer.Os == "android" || peer.Os == "iOS" ? "" : "", C1 = peer.Online ? "#3fb8a0" : "#5b6670", C2 = peer.Online ? "#3fb8a0" : "#5b6670",
                    Title = peer.Name, Text = (peer.Online ? "Online" : "Offline") + (peer.Os != null ? "  •  " + peer.Os : "") + (peer.Ip != null ? "  •  " + peer.Ip : ""), Dim = !peer.Online });
            rows.Add(new ListRow { Glyph = "", C1 = "#2f8fe0", C2 = "#2f8fe0", Title = "Invite a friend", Text = "Tailscale's page: share this PC with them (they need Tailscale too).", Do = () => Stores.Open("https://login.tailscale.com/admin/machines") });
            if (g != null) rows.Add(new ListRow { Glyph = "", C1 = "#e8453c", C2 = "#f4a312", Title = "Start " + g.Name, Do = () => { CloseListPage(); PrimaryAction(g); } });
            rows.Add(new ListRow { Glyph = "", C1 = "#5b6670", C2 = "#5b6670", Title = "Disconnect", Text = "Switch Tailscale off on this PC.", Do = () => RunThen(Tailscale.Down, again) });
        }
        FillRows(p, rows, "");
    }
    void RunThen(Action work, Action then) {
        onlinePage.Sub.Text = "One moment...";
        new Thread(() => { try { work(); } catch { } then(); }) { IsBackground = true }.Start();
    }
    void DelayThen(int ms, Action then) {
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (s, e) => { t.Stop(); then(); }; t.Start();
    }
    void StartTailscaleApp() {
        string ipn = Tailscale.Exe != null ? Path.Combine(Path.GetDirectoryName(Tailscale.Exe), "tailscale-ipn.exe") : null;
        try { if (ipn != null && File.Exists(ipn)) Process.Start(ipn); } catch { }
    }
    // winget installs Tailscale's own package (Windows shows its permission prompt); the page checks again after
    void InstallTailscale() {
        onlinePage.Sub.Text = "Installing Tailscale... Windows may ask for permission. This page refreshes when it's done.";
        FillRows(onlinePage, new List<ListRow>(), "");
        new Thread(() => {
            string err = null;
            try {
                var psi = new ProcessStartInfo("winget", "install --id Tailscale.Tailscale -e --accept-source-agreements --accept-package-agreements --silent") { UseShellExecute = false, CreateNoWindow = true };
                using (var pr = Process.Start(psi)) { pr.WaitForExit(10 * 60 * 1000); if (pr.ExitCode != 0 && Tailscale.Exe == null) err = "winget ended with code " + pr.ExitCode + "."; }
            } catch (Exception e) { err = "winget isn't available (" + e.Message + ")."; }
            W.Dispatcher.BeginInvoke(new Action(() => {
                if (!(ListPageOpen && lpOpen == onlinePage)) return;
                if (err != null && Tailscale.Exe == null) {
                    onlinePage.Sub.Text = "Tailscale didn't install: " + err + " Get it from tailscale.com/download instead.";
                    FillRows(onlinePage, new List<ListRow> { new ListRow { Glyph = "", C1 = "#5b6670", C2 = "#5b6670", Title = "Tailscale's website", Do = () => Stores.Open("https://tailscale.com/download/windows") } }, "");
                } else { StartTailscaleApp(); DelayThen(3000, FillOnline); }
            }));
        }) { IsBackground = true }.Start();
    }
}
}
