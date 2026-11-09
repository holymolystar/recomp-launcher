// Recomp Launcher: one console-style window for PC recomps and native ports. Shows the installed and latest release
// of each game, installs a game the first time, plays it, and updates one or all with one click. Works with a
// keyboard, mouse or any XInput controller.
// For testing: -Snapshot <png> writes a picture of the window and closes it (-SnapshotDelay ms); -PadScript
// "Right,Right,A" plays controller presses; -Full, -OpenNotes, -OpenSettings, -Search <text>, -Select <id> and
// -View row|grid|compact|list set the window up first.
using System;
using System.IO;
using System.Net;
using System.Windows;

namespace RecompLauncher {

class Args {
    public string Snapshot, Select, Search, PadScript, View, Theme;
    public int SnapshotDelay = 3000;
    public bool Full, OpenNotes, OpenSettings, DrawnIcons;
    public static Args Parse(string[] a) {
        var r = new Args();
        for (int i = 0; i < a.Length; i++) {
            string k = a[i].TrimStart('-', '/').ToLowerInvariant();
            Func<string> next = () => i + 1 < a.Length ? a[++i] : null;
            switch (k) {
                case "snapshot": r.Snapshot = next(); break;
                case "snapshotdelay": int d; if (int.TryParse(next(), out d)) r.SnapshotDelay = d; break;
                case "select": r.Select = next(); break;
                case "search": r.Search = next(); break;
                case "padscript": r.PadScript = next(); break;
                case "view": r.View = next(); break;
                case "theme": r.Theme = next(); break;
                case "full": r.Full = true; break;
                case "opennotes": r.OpenNotes = true; break;
                case "opensettings": r.OpenSettings = true; break;
                case "drawnicons": r.DrawnIcons = true; break;
            }
        }
        return r;
    }
}

static class Program {
    [STAThread]
    static int Main(string[] argv) {
        var args = Args.Parse(argv);
        string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        Paths.Root = root;
        Paths.Logs = Path.Combine(root, "logs");
        Paths.Backups = Path.Combine(root, "backups");
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        ServicePointManager.DefaultConnectionLimit = 8;
        IconX.SetCurrentProcessExplicitAppUserModelID("RecompLauncher.App");
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (s, e) => {
            try { File.AppendAllText(Path.Combine(Paths.Logs, "launcher-errors.txt"), DateTime.Now.ToString("o") + "  " + e.Exception + Environment.NewLine); } catch { }
            MessageBox.Show("Something went wrong: " + e.Exception.Message + "\n\nThe details are in logs\\launcher-errors.txt.", "Recomp Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        try {
            Directory.CreateDirectory(Paths.Logs);
            Catalog.Load(Path.Combine(root, "games.json"));
            Settings.Load(Path.Combine(root, "settings.json"));
            SteamGames.AddInstalled();   // your Steam library (Settings > General > Steam games)
            Stores.AddLibraries();       // Epic Games and GOG (Settings > General > Other stores)
            Theme.Use(Settings.Get("Theme"));
            Profiles.Load();
            int vol; if (int.TryParse(Settings.Get("SoundVolume"), out vol)) Sound.Volume = vol;
            if (args.View != null) Settings.Set("ViewMode", args.View);
            if (args.Theme != null) Theme.Use(args.Theme);
            Icons.DrawnOnly = args.DrawnIcons;
            ReleaseCache.Load(Path.Combine(root, "cache.json"));
            if (Playtime.Path == null) Playtime.Path = Path.Combine(root, "playtime.json");   // Profiles.Load picks the profile's
            Roms.Path = Path.Combine(root, "roms.json");
            var shell = new Shell(args);
            app.Startup += (s, e) => { };
            shell.Run();
            return 0;
        } catch (Exception e) {
            try { File.AppendAllText(Path.Combine(Paths.Logs, "launcher-errors.txt"), DateTime.Now.ToString("o") + "  " + e + Environment.NewLine); } catch { }
            MessageBox.Show("Recomp Launcher couldn't start: " + e.Message, "Recomp Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
}
}
