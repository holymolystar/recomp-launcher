// The TV app's video player: mpv (libmpv-2.dll), drawn inside the launcher's own window. It plays what IPTV and
// free TV send (HLS, MPEG-TS) and every file kind (MP4, MKV, AVI...). The library is downloaded once, the first
// time something is played (the newest "mpv-dev-x86_64" build from github.com/shinchiro/mpv-winbuild-cmake, about
// 30 MB, checked against its SHA-256), into tools\mpv; it's never shipped in the installer.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RecompLauncher {

static class Mpv {
    static string Dir { get { return Path.Combine(Paths.Root, @"tools\mpv"); } }
    public static string Dll { get { return Path.Combine(Dir, "libmpv-2.dll"); } }
    public static bool Ready { get { return File.Exists(Dll); } }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibrary(string path);
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr mpv_create();
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern int mpv_initialize(IntPtr ctx);
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern void mpv_terminate_destroy(IntPtr ctx);
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern int mpv_set_option_string(IntPtr ctx, byte[] name, byte[] data);
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern int mpv_command(IntPtr ctx, IntPtr[] args);
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr mpv_get_property_string(IntPtr ctx, byte[] name);
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern int mpv_set_property_string(IntPtr ctx, byte[] name, byte[] data);
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern void mpv_free(IntPtr data);

    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern void mpv_wakeup(IntPtr ctx);
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern int mpv_observe_property(IntPtr ctx, ulong userdata, byte[] name, int format);

    static byte[] U(string s) { return Encoding.UTF8.GetBytes(s + "\0"); }
    static string S(IntPtr p) {
        if (p == IntPtr.Zero) return null;
        int n = 0; while (Marshal.ReadByte(p, n) != 0) n++;
        var b = new byte[n]; Marshal.Copy(p, b, 0, n); return Encoding.UTF8.GetString(b);
    }
    static bool loaded;
    static void Load() { if (!loaded) { LoadLibrary(Dll); loaded = true; } }

    // Every libmpv call for a player runs on that player's own thread, never on the window's thread. mpv draws into a
    // window inside ours, so its video thread sends messages to our window thread; a call made from the window thread
    // that waits on mpv while mpv waits on the window is a deadlock, and Windows then closes the launcher as "not
    // responding". The window thread only queues work here and reads the state mpv reports (cached from its events).
    class Player {
        public IntPtr Ctx;
        public readonly Queue<Action> Work = new Queue<Action>();
        public readonly Dictionary<string, string> State = new Dictionary<string, string>();
        public bool Quit;
        public DateTime BusyUntil;
        public DateTime Pending;   // when a loadfile or stop was sent that mpv hasn't got through yet (MinValue: none)
    }
    static readonly Dictionary<IntPtr, Player> players = new Dictionary<IntPtr, Player>();
    // what the window reads often; kept up to date from mpv's events
    static readonly string[] Watched = { "time-pos", "duration", "idle-active", "pause", "path", "speed", "mute", "volume",
        "panscan", "keepaspect", "video-aspect-override", "audio-delay", "sub-delay" };
    static readonly HashSet<string> WatchedSet = new HashSet<string>(Watched);

    static Player Find(IntPtr ctx) { lock (players) { Player p; return ctx != IntPtr.Zero && players.TryGetValue(ctx, out p) ? p : null; } }
    static bool Post(Player p, Action a) {
        lock (p) { if (p.Quit) return false; p.Work.Enqueue(a); }
        try { mpv_wakeup(p.Ctx); } catch { }
        return true;
    }
    static void Start(IntPtr ctx) {
        var p = new Player { Ctx = ctx };
        lock (players) players[ctx] = p;
        new System.Threading.Thread(() => Loop(p)) { IsBackground = true, Name = "mpv" }.Start();
    }
    // anything mpv takes a second or more over goes in logs\mpv-slow.txt
    static void Slow(string what, Stopwatch sw) { if (sw.ElapsedMilliseconds >= 1000) Note(what + " took " + sw.ElapsedMilliseconds + " ms"); }
    public static void Note(string what) {
        try { File.AppendAllText(Path.Combine(Paths.Logs, "mpv-slow.txt"), DateTime.Now.ToString("s") + "  " + what + Environment.NewLine); } catch { }
    }
    static void Loop(Player p) {
        IntPtr ctx = p.Ctx;
        try { for (int i = 0; i < Watched.Length; i++) mpv_observe_property(ctx, (ulong)(i + 1), U(Watched[i]), 1 /* MPV_FORMAT_STRING */); } catch { }
        while (true) {
            while (true) {
                Action a;
                lock (p) { if (p.Work.Count == 0) break; a = p.Work.Dequeue(); }
                try { a(); } catch { }
            }
            bool quit; lock (p) quit = p.Quit && p.Work.Count == 0;
            if (quit) break;
            IntPtr ev;
            try { ev = mpv_wait_event(ctx, 1.0); } catch { break; }
            if (ev == IntPtr.Zero) continue;
            int id = Marshal.ReadInt32(ev, 0);
            if (id == 6 /* MPV_EVENT_START_FILE */) p.Pending = DateTime.MinValue;
            if (id == 22 /* MPV_EVENT_PROPERTY_CHANGE */) {
                // mpv_event { int event_id; int error; uint64 reply_userdata; void* data } -> mpv_event_property { char* name; int format; void* data }
                IntPtr prop = Marshal.ReadIntPtr(ev, 16); if (prop == IntPtr.Zero) continue;
                string name = S(Marshal.ReadIntPtr(prop, 0));
                int fmt = Marshal.ReadInt32(prop, IntPtr.Size);
                IntPtr data = Marshal.ReadIntPtr(prop, IntPtr.Size * 2);
                string val = fmt == 1 && data != IntPtr.Zero ? S(Marshal.ReadIntPtr(data)) : null;
                if (name != null) lock (p.State) p.State[name] = val;
                if (name == "idle-active" && val == "yes") p.Pending = DateTime.MinValue;
            }
        }
        var sw = Stopwatch.StartNew();
        try { mpv_terminate_destroy(ctx); } catch { }
        Slow("closing the player", sw);
    }

    // a player drawn into a window handle
    public static IntPtr Create(IntPtr wid) {
        Load();
        var ctx = mpv_create(); if (ctx == IntPtr.Zero) throw new Exception("mpv didn't start");
        foreach (var o in new[] {
            new[] { "wid", wid.ToInt64().ToString() }, new[] { "vo", "gpu" }, new[] { "hwdec", "auto-safe" }, new[] { "idle", "yes" }, new[] { "keep-open", "no" },
            new[] { "input-default-bindings", "no" }, new[] { "input-vo-keyboard", "no" }, new[] { "input-cursor", "no" }, new[] { "osc", "no" }, new[] { "terminal", "no" },
            new[] { "osd-font-size", "34" }, new[] { "osd-bar", "yes" }, new[] { "cache", "yes" }, new[] { "demuxer-max-bytes", "150MiB" }, new[] { "network-timeout", "20" },
            new[] { "user-agent", TvSource.BrowserAgent }, new[] { "audio-display", "no" }, new[] { "volume", "100" },
            // subtitles: Dutch first, then English; a .srt next to a file is found by itself
            new[] { "slang", "nl,nld,dut,en,eng" }, new[] { "alang", "nl,nld,dut,en,eng" }, new[] { "sub-auto", "fuzzy" }, new[] { "sub-font-size", "46" }, new[] { "sub-border-size", "2.6" } })
            mpv_set_option_string(ctx, U(o[0]), U(o[1]));
        // no picture is made yet (that waits for the first file), so starting it here doesn't touch the window
        if (mpv_initialize(ctx) < 0) { mpv_terminate_destroy(ctx); throw new Exception("mpv didn't start"); }
        Start(ctx);
        return ctx;
    }
    // a player with no picture and no sound: for recording a stream (stream-record) while you watch something else
    public static IntPtr CreateHeadless() {
        Load();
        var ctx = mpv_create(); if (ctx == IntPtr.Zero) throw new Exception("mpv didn't start");
        foreach (var o in new[] { new[] { "vo", "null" }, new[] { "ao", "null" }, new[] { "idle", "yes" }, new[] { "terminal", "no" }, new[] { "cache", "yes" }, new[] { "network-timeout", "20" }, new[] { "input-default-bindings", "no" } })
            mpv_set_option_string(ctx, U(o[0]), U(o[1]));
        if (mpv_initialize(ctx) < 0) { mpv_terminate_destroy(ctx); throw new Exception("mpv didn't start"); }
        Start(ctx);
        return ctx;
    }
    // closes the player on its own thread, after what's queued; this returns at once
    public static void Destroy(IntPtr ctx) {
        Player p; lock (players) { if (ctx == IntPtr.Zero || !players.TryGetValue(ctx, out p)) return; players.Remove(ctx); }
        lock (p) p.Quit = true;
        try { mpv_wakeup(ctx); } catch { }
    }
    // Some streams (DASH, .mpd) can leave mpv stuck for good while it closes them: a new file or a stop never gets
    // through. Then this is true, and the player is best replaced by a fresh one (see Shell.TvVideoSurface).
    public static bool Stuck(IntPtr ctx, double seconds = 4) {
        var p = Find(ctx); if (p == null) return false;
        var t = p.Pending; return t != DateTime.MinValue && (DateTime.Now - t).TotalSeconds > seconds;
    }
    // queued for the player's thread; this returns at once
    public static void Command(IntPtr ctx, params string[] args) {
        var p = Find(ctx); if (p == null || args.Length == 0) return;
        // a new file or a stop: what the last one reported no longer counts
        if (args[0] == "loadfile" || args[0] == "stop") lock (p.State) { p.State.Remove("time-pos"); p.State.Remove("duration"); p.State.Remove("path"); if (args[0] == "loadfile" && args.Length > 1) { p.State["idle-active"] = "no"; p.State["path"] = args[1]; } }
        var copy = (string[])args.Clone();
        bool idleNow = Get(ctx, "idle-active") == "yes";
        Post(p, () => {
            if (copy[0] == "loadfile" || (copy[0] == "stop" && !idleNow)) p.Pending = DateTime.Now;
            var ptrs = new IntPtr[copy.Length + 1];
            var sw = Stopwatch.StartNew();
            try {
                for (int i = 0; i < copy.Length; i++) { var b = U(copy[i]); ptrs[i] = Marshal.AllocHGlobal(b.Length); Marshal.Copy(b, 0, ptrs[i], b.Length); }
                mpv_command(p.Ctx, ptrs);
            } finally { for (int i = 0; i < copy.Length; i++) if (ptrs[i] != IntPtr.Zero) Marshal.FreeHGlobal(ptrs[i]); }
            Slow(copy[0], sw);
        });
    }
    // What mpv last reported (time-pos, pause, idle-active...) comes straight from the cache. Anything else is asked
    // of the player's thread, waiting at most a moment; a busy player gives null rather than freezing the window.
    public static string Get(IntPtr ctx, string name) {
        var p = Find(ctx); if (p == null) return null;
        if (WatchedSet.Contains(name)) { string v; lock (p.State) return p.State.TryGetValue(name, out v) ? v : null; }
        if (DateTime.Now < p.BusyUntil) return null;
        string got = null;
        var done = new System.Threading.ManualResetEvent(false);
        if (!Post(p, () => {
            try { var r = mpv_get_property_string(p.Ctx, U(name)); if (r != IntPtr.Zero) try { got = S(r); } finally { mpv_free(r); } } catch { }
            done.Set();
        })) return null;
        if (!done.WaitOne(400)) { p.BusyUntil = DateTime.Now.AddSeconds(2); return null; }
        return got;
    }
    public static double Num(IntPtr ctx, string name) { double v; return double.TryParse(Get(ctx, name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : -1; }
    // queued for the player's thread; a value mpv reports on is cached at once, so a read right after sees it
    public static void Set(IntPtr ctx, string name, string value) {
        var p = Find(ctx); if (p == null) return;
        if (WatchedSet.Contains(name)) lock (p.State) p.State[name] = value;
        Post(p, () => { var sw = Stopwatch.StartNew(); mpv_set_property_string(p.Ctx, U(name), U(value)); Slow("setting " + name, sw); });
    }

    // ---------- getting it, once ----------
    public static void Download(Action<string> say) {
        if (Ready) return;
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        say("Getting the video player (mpv, about 30 MB, once)...");
        string api;
        using (var wc = new WebClient()) { wc.Headers.Add("User-Agent", "RecompLauncher"); api = wc.DownloadString("https://api.github.com/repos/shinchiro/mpv-winbuild-cmake/releases/latest"); }
        var rel = Json.Parse(api) as Dictionary<string, object>;
        var asset = ((System.Collections.IEnumerable)rel["assets"]).Cast<Dictionary<string, object>>()
            .FirstOrDefault(a => Regex.IsMatch(Json.Str(a, "name") ?? "", @"^mpv-dev-x86_64-\d{8}-git-[0-9a-f]+\.7z$"));
        if (asset == null) throw new Exception("no Windows player in mpv's newest build");
        var sha = Regex.Match(Json.Str(asset, "digest") ?? "", "^sha256:([0-9a-f]{64})$");
        Directory.CreateDirectory(Dir);
        string arc = Path.Combine(Dir, "mpv-dev.7z");
        using (var wc = new WebClient()) { wc.Headers.Add("User-Agent", "RecompLauncher"); wc.DownloadFile(Json.Str(asset, "browser_download_url"), arc); }
        if (sha.Success) {
            string got; using (var s = File.OpenRead(arc)) got = BitConverter.ToString(SHA256.Create().ComputeHash(s)).Replace("-", "").ToLowerInvariant();
            if (got != sha.Groups[1].Value) { File.Delete(arc); throw new Exception("the download didn't match its checksum"); }
        }
        say("Unpacking the video player...");
        string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
        var p = Process.Start(new ProcessStartInfo(tar, "-xf " + Engine.Q(arc) + " -C " + Engine.Q(Dir) + " libmpv-2.dll") { UseShellExecute = false, CreateNoWindow = true });
        p.WaitForExit(120000);
        try { File.Delete(arc); } catch { }
        if (!Ready) throw new Exception("libmpv-2.dll wasn't in the download");
    }
}
}
