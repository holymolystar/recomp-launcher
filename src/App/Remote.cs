// Phone remote (Settings > Phone remote): a small web page for your phone on the same Wi-Fi. It shows your installed
// games with their pictures and what's playing; tap a game to start it on the PC, or close the game that runs.
// The launcher answers on port 8770 of this PC's home network address (Windows asks once whether to allow that).
// A 6-digit PIN (shown in Settings) is needed for everything but the page itself, so others on the Wi-Fi can't use
// it; after 8 wrong PINs it stops answering for a minute. Off unless turned on.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RecompLauncher {

partial class Shell {
    const int RemotePort = 8770;
    TcpListener remote; int remoteFails; DateTime remoteLockedUntil = DateTime.MinValue;
    readonly Dictionary<string, byte[]> remoteArt = new Dictionary<string, byte[]>();

    public static string RemotePin() {
        string p = Settings.Get("RemotePin");
        if (p.Length != 6) { p = NewRemotePin(); }
        return p;
    }
    static string NewRemotePin() {
        var b = new byte[4]; using (var r = System.Security.Cryptography.RandomNumberGenerator.Create()) r.GetBytes(b);
        string p = (BitConverter.ToUInt32(b, 0) % 1000000).ToString("000000");
        Settings.Set("RemotePin", p); Settings.Save();
        return p;
    }
    // this PC's addresses on the home network
    static List<string> LanAddresses() {
        var l = new List<string>();
        try {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()) {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                if (ni.Description.IndexOf("virtual", StringComparison.OrdinalIgnoreCase) >= 0 || ni.Description.IndexOf("vpn", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                foreach (var a in ni.GetIPProperties().UnicastAddresses)
                    if (a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254.")) l.Add(a.Address.ToString());
            }
        } catch { }
        return l.Distinct().ToList();
    }
    public string RemoteAddress() { var l = LanAddresses(); return l.Count > 0 ? "http://" + l[0] + ":" + RemotePort : "(no network found)"; }

    void ApplyRemote() {
        bool on = Settings.Get("PhoneRemote") == "on";
        if (on && remote == null) {
            try {
                remote = new TcpListener(IPAddress.Any, RemotePort);
                remote.Start();
                var l = remote;
                new Thread(() => AcceptLoop(l)) { IsBackground = true, Name = "Phone remote" }.Start();
            } catch (Exception e) { remote = null; SetStep("The phone remote couldn't start: " + e.Message, Ui.Red); }
        } else if (!on && remote != null) {
            try { remote.Stop(); } catch { }
            remote = null;
        }
    }
    void AcceptLoop(TcpListener l) {
        while (true) {
            TcpClient c;
            try { c = l.AcceptTcpClient(); } catch { return; }   // stopped
            ThreadPool.QueueUserWorkItem(_ => { try { Serve(c); } catch { } finally { try { c.Close(); } catch { } } });
        }
    }
    void Serve(TcpClient c) {
        c.ReceiveTimeout = 5000; c.SendTimeout = 10000;
        var s = c.GetStream();
        // the request line and headers (no bodies are needed)
        var head = new StringBuilder(); var buf = new byte[1];
        while (head.Length < 8192) { if (s.Read(buf, 0, 1) <= 0) return; head.Append((char)buf[0]); if (head.Length >= 4 && head.ToString(head.Length - 4, 4) == "\r\n\r\n") break; }
        var first = head.ToString().Split('\n')[0].Trim().Split(' ');
        if (first.Length < 2) return;
        string method = first[0], target = first[1], path = target, query = "";
        int q = target.IndexOf('?'); if (q >= 0) { path = target.Substring(0, q); query = target.Substring(q + 1); }
        var args = query.Split('&').Where(x => x.Contains("=")).ToDictionary(x => Uri.UnescapeDataString(x.Substring(0, x.IndexOf('='))), x => Uri.UnescapeDataString(x.Substring(x.IndexOf('=') + 1)));

        if (path == "/" || path == "/index.html") { Reply(s, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(RemotePage)); return; }
        // everything else needs the PIN
        if (DateTime.Now < remoteLockedUntil) { Reply(s, 429, "text/plain", Encoding.UTF8.GetBytes("wait")); return; }
        string pin; args.TryGetValue("pin", out pin);
        if (pin != RemotePin()) {
            if (++remoteFails >= 8) { remoteFails = 0; remoteLockedUntil = DateTime.Now.AddMinutes(1); }
            Reply(s, 401, "text/plain", Encoding.UTF8.GetBytes("pin")); return;
        }
        remoteFails = 0;
        if (path == "/api/state") { Reply(s, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes((string)W.Dispatcher.Invoke(new Func<string>(RemoteState)))); return; }
        if (path.StartsWith("/art/")) {
            var png = (byte[])W.Dispatcher.Invoke(new Func<byte[]>(() => RemoteArtOf(path.Substring(5))));
            if (png == null) Reply(s, 404, "text/plain", new byte[0]); else Reply(s, 200, "image/png", png, "Cache-Control: max-age=86400\r\n");
            return;
        }
        if (method == "POST" && path.StartsWith("/api/start/")) { string msg = (string)W.Dispatcher.Invoke(new Func<string>(() => RemoteStart(path.Substring(11)))); Reply(s, 200, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(msg)); return; }
        if (method == "POST" && path == "/api/close") { string msg = (string)W.Dispatcher.Invoke(new Func<string>(RemoteCloseGame)); Reply(s, 200, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(msg)); return; }
        Reply(s, 404, "text/plain", new byte[0]);
    }
    static void Reply(Stream s, int code, string type, byte[] body, string extra = "") {
        string status = code == 200 ? "OK" : code == 401 ? "Unauthorized" : code == 404 ? "Not Found" : code == 429 ? "Too Many Requests" : "Error";
        var h = Encoding.ASCII.GetBytes("HTTP/1.1 " + code + " " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length + "\r\n" + extra + "Connection: close\r\n\r\n");
        s.Write(h, 0, h.Length); s.Write(body, 0, body.Length); s.Flush();
    }
    // ---------- on the launcher's thread ----------
    string RemoteState() {
        var games = Games.Where(g => !g.Hidden && g.Inst != null).OrderByDescending(LastPlayedAny).ThenBy(g => g.Name).Select(g => new Dictionary<string, object> {
            { "id", g.Id }, { "name", g.Name }, { "from", g.ByStore ? g.StoreName : "Recomp" } }).ToList();
        var d = new Dictionary<string, object> { { "games", games } };
        if (playing != null) { d["playing"] = playing.Name; d["playingId"] = playing.Id; d["minutes"] = (int)(DateTime.Now - playingSince).TotalMinutes; }
        return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(d);
    }
    byte[] RemoteArtOf(string id) {
        byte[] png;
        if (remoteArt.TryGetValue(id, out png)) return png;
        var g = Catalog.Find(id); if (g == null) return null;
        if (g.Icon == null) Icons.Load(g);
        var src = g.Icon as ImageSource; if (src == null) return null;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) {
            dc.DrawRectangle(Ui.Gradient(g.Accent, g.Accent2), null, new Rect(0, 0, 192, 192));
            dc.DrawImage(src, g.IconIsArt ? new Rect(0, 0, 192, 192) : new Rect(40, 40, 112, 112));
        }
        var bmp = new RenderTargetBitmap(192, 192, 96, 96, PixelFormats.Pbgra32); bmp.Render(dv);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var ms = new MemoryStream()) { enc.Save(ms); png = ms.ToArray(); }
        remoteArt[id] = png;
        return png;
    }
    string RemoteStart(string id) {
        var g = Catalog.Find(id);
        if (g == null || g.Inst == null) return "That game isn't installed.";
        if (playing != null) return playing.Name + " is still running: close it first.";
        if (AnyDialog()) return "Something is open on the PC's screen: close it there first.";
        ShowGame(g);
        PrimaryAction(g);
        return "Starting " + g.Name + "...";
    }
    string RemoteCloseGame() {
        var g = playing; if (g == null) return "Nothing is running.";
        foreach (var p in Engine.Processes(g)) { try { if (!p.CloseMainWindow()) p.Kill(); } catch { } }
        return "Closing " + g.Name + "...";
    }

    const string RemotePage = @"<!doctype html><html lang='en'><head><meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1,viewport-fit=cover'>
<meta name='theme-color' content='#0d1317'><title>Recomp Launcher</title>
<style>
:root{--bg:#0d1317;--card:#161f25;--line:#26333c;--ink:#eef3f6;--muted:#8a9aa5;--teal:#2fc4b2;--red:#e8453c}
*{box-sizing:border-box;-webkit-tap-highlight-color:transparent}
body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.4 system-ui,-apple-system,Segoe UI,Roboto,sans-serif;padding:18px 16px 40px}
h1{font-size:13px;letter-spacing:.14em;color:var(--teal);margin:4px 0 14px}
.now{background:var(--card);border:1px solid var(--line);border-radius:18px;padding:14px;display:flex;gap:14px;align-items:center;margin-bottom:16px}
.now img{width:64px;height:64px;border-radius:12px}
.now b{display:block;font-size:17px}.now span{color:var(--muted);font-size:13px}
button{font:inherit;border:0;border-radius:22px;padding:10px 18px;background:#24313a;color:var(--ink);font-weight:600}
button.go{background:var(--teal);color:#06110f}button.stop{background:var(--red);color:#fff}
input{width:100%;font:inherit;padding:11px 14px;border-radius:14px;border:1px solid var(--line);background:#0a0e11;color:var(--ink);margin-bottom:14px}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(104px,1fr));gap:12px}
.tile{position:relative;border-radius:16px;overflow:hidden;aspect-ratio:1;background:var(--card)}
.tile img{width:100%;height:100%;object-fit:cover;display:block}
.tile div{position:absolute;left:0;right:0;bottom:0;padding:18px 8px 7px;font-size:12px;font-weight:600;background:linear-gradient(transparent,rgba(0,0,0,.75))}
.sheet{position:fixed;inset:0;background:rgba(0,0,0,.6);display:none;align-items:flex-end}
.sheet.on{display:flex}.sheet>div{background:var(--card);width:100%;border-radius:22px 22px 0 0;padding:22px 18px 30px;text-align:center}
.sheet img{width:120px;height:120px;border-radius:18px}.sheet b{display:block;font-size:19px;margin:12px 0 18px}
.row{display:flex;gap:10px;justify-content:center}.msg{color:var(--muted);font-size:13px;min-height:20px;margin:-6px 0 10px}
.pin{max-width:320px;margin:60px auto;text-align:center}.pin input{text-align:center;font-size:26px;letter-spacing:.3em}
</style></head><body>
<div id='pinbox' class='pin' style='display:none'><h1>RECOMP LAUNCHER</h1><p>Type the PIN shown on the PC in Settings &gt; Phone remote.</p>
<input id='pin' inputmode='numeric' maxlength='6' autocomplete='off'><button class='go' onclick='savePin()'>Connect</button><p class='msg' id='pinmsg'></p></div>
<div id='app' style='display:none'><h1>RECOMP LAUNCHER</h1><div id='now'></div><div class='msg' id='msg'></div>
<input id='q' placeholder='Search your games' oninput='draw()'><div class='grid' id='grid'></div></div>
<div class='sheet' id='sheet' onclick='if(event.target===this)hide()'><div><img id='simg'><b id='sname'></b>
<div class='row'><button onclick='hide()'>Cancel</button><button class='go' onclick='start()'>Start on the PC</button></div></div></div>
<script>
let pin=localStorage.getItem('pin')||'',state=null,pick=null,closeArmed=false;
const $=id=>document.getElementById(id),art=id=>'/art/'+encodeURIComponent(id)+'?pin='+pin;
function savePin(){pin=$('pin').value.trim();localStorage.setItem('pin',pin);load();}
async function api(p,post){const r=await fetch(p+(p.includes('?')?'&':'?')+'pin='+pin,{method:post?'POST':'GET'});
 if(r.status==401){$('app').style.display='none';$('pinbox').style.display='block';$('pinmsg').textContent=pin?'That PIN is not right.':'';throw 0}
 if(r.status==429){$('pinmsg').textContent='Too many tries: wait a minute.';throw 0}
 $('pinbox').style.display='none';$('app').style.display='block';return post?r.text():r.json();}
async function load(){try{state=await api('/api/state');draw();}catch(e){}}
function draw(){if(!state)return;const q=$('q').value.toLowerCase();
 $('now').innerHTML=state.playing?`<div class='now'><img src='${art(state.playingId)}'><div style='flex:1'><span>Playing for ${state.minutes} min</span><b>${esc(state.playing)}</b></div><button class='stop' onclick='closeGame()'>${closeArmed?'Tap again':'Close'}</button></div>`:'';
 $('grid').innerHTML=state.games.filter(g=>g.name.toLowerCase().includes(q)).map(g=>`<div class='tile' onclick='ask(${JSON.stringify(g.id)})'><img loading='lazy' src='${art(g.id)}'><div>${esc(g.name)}</div></div>`).join('');}
function esc(s){return s.replace(/[&<>']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;',""'"":'&#39;'}[c]));}
function ask(id){pick=state.games.find(g=>g.id===id);$('simg').src=art(id);$('sname').textContent=pick.name;$('sheet').classList.add('on');}
function hide(){$('sheet').classList.remove('on');}
async function start(){hide();try{$('msg').textContent=await api('/api/start/'+encodeURIComponent(pick.id),true);setTimeout(load,2500);}catch(e){}}
async function closeGame(){if(!closeArmed){closeArmed=true;draw();setTimeout(()=>{closeArmed=false;draw()},4000);return}
 closeArmed=false;try{$('msg').textContent=await api('/api/close',true);setTimeout(load,3000);}catch(e){}}
if(pin)load();else $('pinbox').style.display='block';setInterval(()=>{if(pin&&$('app').style.display!='none')load()},4000);
</script></body></html>";
}
}
