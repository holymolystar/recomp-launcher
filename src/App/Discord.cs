// Discord status: while a game started from the launcher runs, Discord shows it on your profile ("Playing Recomp
// Launcher: Tears of the Kingdom", for how long, with the game's picture when it has one on the web). Discord's
// desktop app takes this over a local pipe (\\.\pipe\discord-ipc-0..9) from an "application" of your own: make one
// named Recomp Launcher at discord.com/developers/applications and paste its Application ID in Settings > Discord.
// Nothing is sent anywhere else; with Discord closed nothing happens.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace RecompLauncher {

static class DiscordStatus {
    static readonly object gate = new object();
    static NamedPipeClientStream pipe;

    public static void Playing(Game g, DateTime since) {
        string app = Settings.Get("DiscordAppId").Trim();
        if (app.Length == 0 || g == null) return;
        string name = g.Name, image = g.Steam != null ? "https://cdn.cloudflare.steamstatic.com/steam/apps/" + g.Steam + "/header.jpg" : g.StoreArt;
        string from = g.ByStore ? g.StoreName : "PC recomp";
        long start = (long)(since.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        ThreadPool.QueueUserWorkItem(_ => {
            lock (gate) {
                try {
                    if (!Connect(app)) return;
                    var activity = new Dictionary<string, object> {
                        { "details", name }, { "state", from }, { "timestamps", new Dictionary<string, object> { { "start", start } } }
                    };
                    var assets = new Dictionary<string, object> { { "large_text", name } };
                    if (image != null) assets["large_image"] = image;
                    activity["assets"] = assets;
                    Send(1, new Dictionary<string, object> { { "cmd", "SET_ACTIVITY" }, { "nonce", Guid.NewGuid().ToString() },
                        { "args", new Dictionary<string, object> { { "pid", Process.GetCurrentProcess().Id }, { "activity", activity } } } });
                    Receive();
                } catch { Close(); }
            }
        });
    }
    public static void Clear() {
        ThreadPool.QueueUserWorkItem(_ => {
            lock (gate) {
                if (pipe == null) return;
                try {
                    Send(1, new Dictionary<string, object> { { "cmd", "SET_ACTIVITY" }, { "nonce", Guid.NewGuid().ToString() },
                        { "args", new Dictionary<string, object> { { "pid", Process.GetCurrentProcess().Id } } } });
                    Receive();
                } catch { }
                Close();
            }
        });
    }

    static bool Connect(string app) {
        if (pipe != null && pipe.IsConnected) return true;
        Close();
        for (int i = 0; i < 10; i++) {
            var p = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut);
            try { p.Connect(250); pipe = p; break; } catch { p.Dispose(); }
        }
        if (pipe == null) return false;   // Discord isn't running
        Send(0, new Dictionary<string, object> { { "v", 1 }, { "client_id", app } });
        var hello = Receive();
        if (hello == null || !hello.Contains("READY")) { Close(); return false; }
        return true;
    }
    static void Send(int op, object payload) {
        var body = Encoding.UTF8.GetBytes(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(payload));
        var frame = new byte[8 + body.Length];
        BitConverter.GetBytes(op).CopyTo(frame, 0); BitConverter.GetBytes(body.Length).CopyTo(frame, 4); body.CopyTo(frame, 8);
        pipe.Write(frame, 0, frame.Length); pipe.Flush();
    }
    static string Receive() {
        var head = ReadExactly(8); if (head == null) return null;
        int len = BitConverter.ToInt32(head, 4);
        if (len < 0 || len > 1 << 20) return null;
        var body = ReadExactly(len);
        return body != null ? Encoding.UTF8.GetString(body) : null;
    }
    static byte[] ReadExactly(int n) {
        var b = new byte[n]; int got = 0;
        while (got < n) { int r = pipe.Read(b, got, n - got); if (r <= 0) return null; got += r; }
        return b;
    }
    static void Close() { try { if (pipe != null) pipe.Dispose(); } catch { } pipe = null; }
}
}
