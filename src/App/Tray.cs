// While a game runs: the launcher follows it (by its process name, games.json Proc, every 3 seconds; a game that never
// shows up, under another name or because it failed to start, counts as closed after 90 seconds), so the in-game menu
// (Overlay.cs) knows what's playing, and the launcher can come back when it closes.
//
// "Wait in the tray" (Settings > General > When a game starts): the launcher hides to an icon by the clock while the
// game runs, with its timers stopped and its memory given back to Windows (only the controller is still read, for
// the in-game menu, when that's on), and comes back by itself when the game closes. The icon opens it (double click)
// or quits it (right click menu); it also shows notes like "an update is out" (Notify).
using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace RecompLauncher {

partial class Shell {
    Forms.NotifyIcon tray;
    bool inTray;
    Game playing; DateTime playingSince; bool playingSeen;
    DispatcherTimer gameWatch;
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    // a game was started: follow it until it closes
    void WatchGame(Game g) {
        playing = g; playingSince = DateTime.Now; playingSeen = false;
        if (gameWatch == null) {
            gameWatch = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
            gameWatch.Tick += (s, e) => {
                if (playing == null) { gameWatch.Stop(); return; }
                bool runs = Engine.IsRunning(playing);
                if (runs) playingSeen = true;
                if ((playingSeen && !runs) || (!playingSeen && (DateTime.Now - playingSince).TotalSeconds > 90)) GameClosed();
            };
        }
        gameWatch.Start();
    }
    void GameClosed() {
        var g = playing;
        playing = null;
        gameWatch.Stop();
        CloseOverlay(false);
        if (inTray) ComeBack(g);
        else if (g != null) { UpdateViews(); }
    }

    void EnsureTray() {
        if (tray != null) return;
        tray = new Forms.NotifyIcon();
        try { tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { tray.Icon = System.Drawing.SystemIcons.Application; }
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Recomp Launcher", null, (s, e) => W.Dispatcher.BeginInvoke(new Action(() => ComeBack(null))));
        menu.Items.Add("Quit", null, (s, e) => W.Dispatcher.BeginInvoke(new Action(() => { tray.Visible = false; W.Close(); })));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (s, e) => W.Dispatcher.BeginInvoke(new Action(() => ComeBack(null)));
        tray.BalloonTipClicked += (s, e) => W.Dispatcher.BeginInvoke(new Action(() => ComeBack(null)));
        W.Closed += (s, e) => { try { tray.Visible = false; tray.Dispose(); } catch { } };
    }

    void WaitInTray(Game g) {
        EnsureTray();
        string tip = "Recomp Launcher: back when " + g.Name + " closes";
        tray.Text = tip.Length > 63 ? tip.Substring(0, 60) + "..." : tip;
        tray.Visible = true;
        inTray = true;
        W.Topmost = false;
        W.Hide();
        // as light as it gets while waiting: nothing ticks or animates, memory goes back to Windows; the controller
        // is still read for the in-game menu (a few microseconds 60 times a second), unless that's off
        if (Settings.Get("PadMenu") == "off") padTimer.Stop();
        if (homeClockTimer != null) homeClockTimer.Stop();
        StopDrift();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        try { SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, new IntPtr(-1), new IntPtr(-1)); } catch { }
    }
    // the game closed (or the icon was used): the launcher is back as it was, where it was
    void ComeBack(Game from) {
        if (tray != null) tray.Visible = false;
        inTray = false;
        W.Show();
        if (W.WindowState == WindowState.Minimized) W.WindowState = WindowState.Normal;
        W.Activate();
        if (isFull) W.Topmost = true;
        padTimer.Start();
        if (view == View.Home && homeClockTimer != null) { homeClockTimer.Start(); TickHomeClock(); }
        if (from != null) { SetStep("Welcome back from " + from.Name + ".", Ui.Teal); UpdateViews(); }
    }
    // a short note by the clock while the launcher waits in the tray (an update found, an install done)
    void Notify(string title, string text) {
        if (!inTray || tray == null) return;
        try { tray.ShowBalloonTip(6000, title, text, Forms.ToolTipIcon.Info); } catch { }
    }
}
}
