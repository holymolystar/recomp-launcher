// TV & Movies: the trailer in a film's or series' card (TvPeek.cs), as on a streaming service. When the card opens,
// the title's trailer (YouTube, found through Cinemeta) starts over the card's picture, with sound (exactly as loud
// as the launcher: Audio.MatchHelpers); until it really
// plays, and when there is none or it can't be played, the picture simply stays. One small browser (WebView2) is
// kept for it and placed over each card; it's stopped (and silent) whenever the card goes away. Edge only starts a
// video in a window that shows, so while it loads it is a single dot; it grows over the picture once it plays.
// Clicking the trailer does what clicking the card does (the page underneath never gets the click).
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace RecompLauncher {

partial class Shell {
    WebView2 tvTrailer; bool tvTrailerReady; string tvTrailerPending; TvItem tvTrailerItem; TvRow tvTrailerRow; DispatcherTimer tvTrailerMouse;
    static bool tvTrailerBroken; Point tvTrailerAt;   // WebView2 didn't start: no trailers this time

    // the page the trailer plays in: YouTube's player, filling the box (a little larger, so its title and logo are
    // off the edges), and a cover over it that takes the clicks
    const string TvTrailerPage = @"<!doctype html><html><head><meta charset='utf-8'><style>
html,body{margin:0;height:100%;background:#000;overflow:hidden}
#p{position:absolute;left:-18%;top:-18%;width:136%;height:136%}
#c{position:absolute;inset:0;cursor:pointer}
</style></head><body><div id='p'></div><div id='c'></div>
<script>
var id=new URLSearchParams(location.search).get('v'),said=false;
function say(m){try{chrome.webview.postMessage(m)}catch(e){}}
document.getElementById('c').onclick=function(){say('click')};
function onYouTubeIframeAPIReady(){
  new YT.Player('p',{videoId:id,host:'https://www.youtube-nocookie.com',
    playerVars:{autoplay:1,controls:0,disablekb:1,fs:0,iv_load_policy:3,modestbranding:1,playsinline:1,rel:0,cc_load_policy:0},
    events:{
      onReady:function(e){e.target.setVolume(100);e.target.playVideo()},
      onStateChange:function(e){if(e.data==1&&!said){said=true;setTimeout(function(){say('playing')},350)} if(e.data==0)say('ended')},
      onError:function(e){say('error '+e.data)}}});
}
</script><script src='https://www.youtube.com/iframe_api' onerror=""say('error')""></script></body></html>";

    // the card at (left, top) opened for this title: its trailer, if it has one
    void TvTrailerStart(TvItem it, TvRow r, double left, double top) {
        TvTrailerStop();
        if (tvTrailerBroken || !TvPeekable(it) || !WebViewAvailable()) return;
        tvTrailerItem = it; tvTrailerRow = r;
        ThreadPool.QueueUserWorkItem(_ => {
            string yt = null; try { yt = Cinemeta.Trailer(it); } catch { }
            if (yt == null) return;   // none: the picture stays
            W.Dispatcher.BeginInvoke(new Action(() => {
                if (tvPeekItem != it || tvTrailerItem != it || tvPeek == null) return;
                TvTrailerMake();
                if (tvTrailer == null) return;
                // Edge doesn't start a video in a hidden window: while it loads it's there, but a single dot
                Canvas.SetLeft(tvTrailer, left + 20); Canvas.SetTop(tvTrailer, top + 20);
                tvTrailer.Width = 1; tvTrailer.Height = 1; tvTrailer.Visibility = Visibility.Visible;
                tvTrailerAt = new Point(left, top);
                string url = "https://trailer.recomp/t.html?v=" + yt;
                if (tvTrailerReady) tvTrailer.CoreWebView2.Navigate(url); else tvTrailerPending = url;
            }));
        });
    }
    // stops it and puts it away (no sound goes on)
    void TvTrailerStop() {
        tvTrailerItem = null; tvTrailerPending = null;
        if (tvTrailerMouse != null) tvTrailerMouse.Stop();
        if (tvTrailer == null) return;
        tvTrailer.Visibility = Visibility.Hidden;
        if (tvTrailerReady) try { tvTrailer.CoreWebView2.Navigate("about:blank"); } catch { }
    }
    bool TvTrailerShown { get { return tvTrailer != null && tvTrailer.Visibility == Visibility.Visible; } }

    void TvTrailerMake() {
        if (tvTrailer != null) return;
        try {
            string dir = Path.Combine(Paths.Root, @"cache\trailer");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "t.html"), TvTrailerPage);
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"RecompLauncher\trailer");
            tvTrailer = new WebView2 { Width = 1, Height = 1, Visibility = Visibility.Hidden, DefaultBackgroundColor = System.Drawing.Color.Black };
            tvPeekLayer.Children.Add(tvTrailer);
            tvTrailer.CoreWebView2InitializationCompleted += (s, e) => {
                if (!e.IsSuccess) { tvTrailerBroken = true; return; }
                var core = tvTrailer.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false; core.Settings.AreDevToolsEnabled = false; core.Settings.IsStatusBarEnabled = false; core.Settings.IsZoomControlEnabled = false;
                core.SetVirtualHostNameToFolderMapping("trailer.recomp", dir, CoreWebView2HostResourceAccessKind.Allow);
                core.NewWindowRequested += (s2, e2) => e2.Handled = true;   // never a YouTube window
                core.WebMessageReceived += (s2, e2) => {
                    string m; try { m = e2.TryGetWebMessageAsString(); } catch { return; }
                    if (m.StartsWith("error")) m = "error";
                    if (m == "playing") { if (tvTrailerItem != null && tvTrailerItem == tvPeekItem && tvPeek != null && tvZone == "rows") { Canvas.SetLeft(tvTrailer, tvTrailerAt.X); Canvas.SetTop(tvTrailer, tvTrailerAt.Y); tvTrailer.Width = TvPeekW; tvTrailer.Height = TvPeekPicH; tvTrailer.Visibility = Visibility.Visible; }
                        // as loud as the launcher (its sound comes from an Edge process with a slider of its own)
                        ThreadPool.QueueUserWorkItem(_ => { Audio.MatchHelpers(); Thread.Sleep(1200); Audio.MatchHelpers(); }); }
                    else if (m == "ended" || m == "error") { tvTrailer.Visibility = Visibility.Hidden; try { core.Navigate("about:blank"); } catch { } }
                    else if (m == "click") { var it = tvTrailerItem; var r = tvTrailerRow; if (it != null) { TvPeekHide(); TvPick(it, r); W.Activate(); } }
                };
                tvTrailerReady = true;
                if (tvTrailerPending != null) { var u = tvTrailerPending; tvTrailerPending = null; core.Navigate(u); }
            };
            var env = CoreWebView2Environment.CreateAsync(null, data, new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
            env.ContinueWith(t => W.Dispatcher.BeginInvoke(new Action(() => {
                if (t.IsFaulted || t.Result == null) { tvTrailerBroken = true; return; }
                try { tvTrailer.EnsureCoreWebView2Async(t.Result); } catch { tvTrailerBroken = true; }
            })));
        } catch { tvTrailerBroken = true; tvTrailer = null; }
    }

    // The trailer is a window of its own over the card, so the card stops getting the mouse while it's over the
    // trailer; whether the mouse has left the card is then checked by where the pointer is.
    [DllImport("user32.dll")] static extern bool GetCursorPos(out TrailerPoint p);
    struct TrailerPoint { public int X, Y; }
    bool TvMouseOverPeek() {
        if (tvPeek == null) return false;
        try { TrailerPoint p; GetCursorPos(out p); var at = tvPeek.PointFromScreen(new Point(p.X, p.Y)); return at.X >= 0 && at.Y >= 0 && at.X <= tvPeek.ActualWidth && at.Y <= tvPeek.ActualHeight; } catch { return false; }
    }
    void TvPeekMouseLeft() {
        if (!TvMouseOverPeek()) { TvPeekHide(); return; }
        // over the trailer: watch for it leaving the card
        if (tvTrailerMouse == null) {
            tvTrailerMouse = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            tvTrailerMouse.Tick += (s, e) => { if (tvPeek == null) { tvTrailerMouse.Stop(); return; } if (!TvMouseOverPeek()) { tvTrailerMouse.Stop(); TvPeekHide(); } };
        }
        tvTrailerMouse.Start();
    }
}
}
