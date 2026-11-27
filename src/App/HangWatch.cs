// When the launcher stops responding (its window thread stuck for 3 seconds or more), what that thread was doing is
// written to logs\ui-hang.txt, once per hang, so the cause can be found instead of guessed. The window thread gives a
// small sign of life twice a second; a thread of its own watches for it.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Threading;

namespace RecompLauncher {

partial class Shell {
    static int hangBeat = Environment.TickCount;
    void StartHangWatch() {
        var ui = Thread.CurrentThread;
        var beat = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(500) };
        beat.Tick += (s, e) => hangBeat = Environment.TickCount;
        beat.Start();
        new Thread(() => {
            bool logged = false;
            while (true) {
                Thread.Sleep(500);
                int age = Environment.TickCount - hangBeat;
                if (age < 3000) { logged = false; continue; }
                if (logged) continue;
                logged = true;
                string stack;
#pragma warning disable 618   // Suspend and a stack of another thread: only to look, and let go at once
                try { ui.Suspend(); try { stack = new StackTrace(ui, false).ToString(); } finally { ui.Resume(); } }
                catch (Exception e) { stack = "(couldn't look: " + e.Message + ")"; }
#pragma warning restore 618
                try { File.AppendAllText(Path.Combine(Paths.Logs, "ui-hang.txt"), DateTime.Now.ToString("s") + "  not responding for " + age / 1000.0 + " s, the window thread was in:" + Environment.NewLine + stack + Environment.NewLine); } catch { }
            }
        }) { IsBackground = true, Name = "hang-watch" }.Start();
    }
}
}
