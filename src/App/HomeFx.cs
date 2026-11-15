// Home's little extras (Settings > General):
//  - Characters on Home: now and then one of the profiles' characters strolls along the bottom of Home, behind the
//    game tiles, in its own way of walking.
//  - Tile previews: the picked tile's picture slowly zooms in and out; for a Steam game, after a moment, its trailer
//    plays in the tile, silent (from the Steam store's own page data, no key needed).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace RecompLauncher {

partial class Shell {
    // ---------- characters strolling along the bottom ----------
    Canvas strollLayer; DispatcherTimer strollTimer; DateTime strollNext = DateTime.Now.AddSeconds(8), strollLast;
    class Stroller { public Image Img; public ImageSource[] Steps; public double X, Speed, Phase, Cadence; public int Dir; }
    readonly List<Stroller> strollers = new List<Stroller>();
    readonly Dictionary<string, ImageSource[]> strollFrames = new Dictionary<string, ImageSource[]>();
    void StartStrollers() {
        if (strollLayer != null || homeLayer == null) return;
        strollLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        Grid.SetRowSpan(strollLayer, 10);
        homeLayer.Children.Insert(0, strollLayer);   // behind everything on Home
        strollTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        strollTimer.Tick += (s, e) => StrollTick();
        strollLast = DateTime.Now; strollTimer.Start();
    }
    bool StrollAllowed() {
        return Settings.Get("HomeStrollers") != "off" && Settings.Get("Motion") != "reduced" && view == View.Home && homePage == "home" && W.IsActive && !AnyDialog() && !SaverOn && !BrowserViewOpen && !PlazaOpen && playing == null;
    }
    void StrollTick() {
        double dt = Math.Min(0.1, (DateTime.Now - strollLast).TotalSeconds); strollLast = DateTime.Now;
        if (!StrollAllowed()) {
            if (strollers.Count > 0) { foreach (var x in strollers) strollLayer.Children.Remove(x.Img); strollers.Clear(); }
            strollNext = DateTime.Now.AddSeconds(10);
            return;
        }
        if (DateTime.Now >= strollNext && strollers.Count < 2 && Profiles.All.Count > 0) {
            strollNext = DateTime.Now.AddSeconds(18 + new Random().Next(25));
            SpawnStroller();
        }
        double w = strollLayer.ActualWidth, feet = strollLayer.ActualHeight - 70;
        foreach (var x in strollers.ToList()) {
            x.X += x.Dir * x.Speed * dt;
            x.Phase += dt * x.Cadence;
            x.Img.Source = x.Steps[1 + (int)(x.Phase / (Math.PI / 4)) % 8];
            Canvas.SetLeft(x.Img, x.X - x.Img.Width / 2); Canvas.SetTop(x.Img, feet - x.Img.Height - Math.Abs(Math.Sin(x.Phase)) * 2);
            if ((x.Dir > 0 && x.X > w + 80) || (x.Dir < 0 && x.X < -80)) { strollLayer.Children.Remove(x.Img); strollers.Remove(x); }
        }
    }
    void SpawnStroller() {
        var r = new Random();
        var p = Profiles.All[r.Next(Profiles.All.Count)];
        ImageSource[] frames;
        if (!strollFrames.TryGetValue(p.Id + p.Character, out frames)) {
            var def = AvatarDef.Parse(p.Character);
            // nine pictures: standing, then eight steps (3D: built once, posed for each; Avatar3D.cs)
            var states = new AvatarState[9]; states[0] = new AvatarState();
            for (int k = 0; k < 8; k++) states[k + 1] = new AvatarState { Walk = (k + 0.5) * Math.PI / 4 };
            try { frames = Avatar3D.RenderMany(def, states, AvatarArt.Crop.Full, 120, 180); }
            catch { frames = states.Select(x => AvatarArt.Render(def, x, AvatarArt.Crop.Full, 120, 180)).ToArray(); }
            strollFrames[p.Id + p.Character] = frames;
        }
        var def2 = AvatarDef.Parse(p.Character);
        int dir = r.Next(2) == 0 ? 1 : -1;
        var img = new Image { Width = 80, Height = 120, Opacity = 0.85, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(dir, 1) };
        var st = new Stroller { Img = img, Steps = frames, Dir = dir, X = dir > 0 ? -60 : strollLayer.ActualWidth + 60,
            Speed = 55 + 35 * def2.D("height"), Cadence = 10.5 - 3.2 * def2.D("height") };
        strollers.Add(st); strollLayer.Children.Add(img);
    }

    // ---------- the picked tile: a slow zoom, and a Steam game's trailer ----------
    Game previewFor; DispatcherTimer trailerWait; MediaElement trailer;
    static readonly Dictionary<string, string> trailerUrls = new Dictionary<string, string>();
    void TilePreview(Game g) {
        StopTilePreview();
        if (g == null || Settings.Get("TilePreviews") == "off" || g.Ui == null) return;
        var h = ((GameUi)g.Ui).Home; if (h == null) return;
        previewFor = g;
        if (Settings.Get("Motion") == "reduced") { if (g.Steam != null) { if (trailerWait == null) { trailerWait = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) }; trailerWait.Tick += (s, e) => { trailerWait.Stop(); if (previewFor != null) LoadTrailer(previewFor); }; } trailerWait.Start(); } return; }
        h.Icon.RenderTransformOrigin = new Point(0.5, 0.5);
        var sc = new ScaleTransform(1, 1); h.Icon.RenderTransform = sc;
        var z = new DoubleAnimation(1, 1.09, TimeSpan.FromSeconds(9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, z); sc.BeginAnimation(ScaleTransform.ScaleYProperty, z);
        if (g.Steam == null) return;
        if (trailerWait == null) {
            trailerWait = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            trailerWait.Tick += (s, e) => { trailerWait.Stop(); if (previewFor != null) LoadTrailer(previewFor); };
        }
        trailerWait.Start();
    }
    void StopTilePreview() {
        if (trailerWait != null) trailerWait.Stop();
        if (previewFor != null && previewFor.Ui != null) {
            var h = ((GameUi)previewFor.Ui).Home;
            if (h != null && h.Icon.RenderTransform is ScaleTransform) {
                var sc = (ScaleTransform)h.Icon.RenderTransform;
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, null); sc.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            }
        }
        if (trailer != null) {
            try { trailer.Stop(); trailer.Close(); } catch { }
            var host = trailer.Parent as Grid; if (host != null) host.Children.Remove(trailer);
            trailer = null;
        }
        previewFor = null;
    }
    void LoadTrailer(Game g) {
        if (view != View.Home || homePage != "home" || !W.IsActive) return;
        string url;
        if (trailerUrls.TryGetValue(g.Steam, out url)) { PlayTrailer(g, url); return; }
        string app = g.Steam;
        ThreadPool.QueueUserWorkItem(_ => {
            string found = null;
            try {
                using (var wc = new WebClient()) {
                    wc.Encoding = System.Text.Encoding.UTF8;
                    string j = wc.DownloadString("https://store.steampowered.com/api/appdetails?filters=movies&appids=" + app);
                    var m = Regex.Match(j, "\"mp4\":\\{\"480\":\"([^\"]+)\"");
                    if (m.Success) found = m.Groups[1].Value.Replace("\\/", "/");
                }
            } catch { }
            W.Dispatcher.BeginInvoke(new Action(() => { trailerUrls[app] = found; if (previewFor == g) PlayTrailer(g, found); }));
        });
    }
    void PlayTrailer(Game g, string url) {
        if (url == null || previewFor != g) return;
        var h = ((GameUi)g.Ui).Home;
        // the tile's face holds its picture; the trailer goes over it
        var grid = h.Face.Child as Grid;
        if (grid == null) { var icon = h.Face.Child; h.Face.Child = null; grid = new Grid(); grid.Children.Add(icon); h.Face.Child = grid; }
        trailer = new MediaElement { Source = new Uri(url), IsMuted = true, Volume = 0, Stretch = Stretch.UniformToFill, LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Opacity = 0 };
        trailer.MediaOpened += (s, e) => { var t = (MediaElement)s; t.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(600))); };
        trailer.MediaEnded += (s, e) => { var t = (MediaElement)s; t.Position = TimeSpan.Zero; t.Play(); };
        trailer.MediaFailed += (s, e) => { var t = (MediaElement)s; var p = t.Parent as Grid; if (p != null) p.Children.Remove(t); };
        grid.Children.Add(trailer);
        trailer.Play();
    }
}
}
