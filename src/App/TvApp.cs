// TV & Movies: a streaming-service style app inside the launcher (an app tile on Home, like Mail). It opens on "Who's
// watching?" (TvProfiles.cs). Then: a bar at the top with the sections as pills, the picked thing full screen behind
// its title and buttons, rows of posters under it:
//   Home            Continue watching, your favourites, and a little of everything below
//   Live TV         free-to-air channels (iptv-org): the Netherlands by kind, Belgium, world news, music, kids
//   Free to watch   NPO Start, NPO Live, KIJK, Plex, Rakuten TV, Pluto TV, YouTube, the Internet Archive (in the
//                   launcher's browser)
//   My library      your own films and series (TvData.cs: the files in your folders)
//   My IPTV         your provider's Live TV, Movies and Series, each in its own section, and your logins
// Controller: the d-pad moves (left from the first poster goes to the menu), A picks, Y adds to / removes from My
// list, LB / RB the previous / next section, B goes back (to the menu, then out). While watching: A pauses, left /
// right 10 seconds, LB / RB a minute, up / down the next / previous channel, Y shows what's on, B stops.
// Playing: streams play in the app's own player (WebView2 with hls.js and mpegts.js); what it can't play (most .mkv,
// .avi) opens in VLC when it's installed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Path = System.IO.Path;

namespace RecompLauncher {

class TvRow {
    public string Title; public List<TvItem> Items; public bool Wide, Top10;
    public StackPanel Panel; public ScrollViewer Scroll; public FrameworkElement Root;
    public readonly List<Border> Tiles = new List<Border>(); public int Col;
}

partial class Shell {
    Grid tvLayer, tvContent, tvDetail, tvPlayer; StackPanel tvRail, tvRows, tvHeroBtns, tvDetailBtns, tvEpisodes; ScrollViewer tvScroll, tvEpScroll;
    Image tvHeroImg, tvDetailImg, tvDetailPoster; TextBlock tvHeroKicker, tvHeroTitle, tvHeroMeta, tvHeroPlot, tvDetailTitle, tvDetailMeta, tvDetailPlot, tvDetailSeason, tvStatus, tvPageTitle;
    Border tvRailHost; WebView2 tvWeb; Border tvWebHost;
    readonly List<Border> tvRailItems = new List<Border>(); readonly List<string> tvRailIds = new List<string>();
    readonly List<TvRow> tvRowList = new List<TvRow>(); readonly List<Border> tvHeroButtons = new List<Border>(), tvDetailButtons = new List<Border>(), tvEpisodeRows = new List<Border>();
    string tvPage = "home", tvZone = "rows"; int tvRailIdx = 1, tvRowIdx, tvHeroIdx, tvDetailIdx, tvEpIdx, tvSeason;
    TvItem tvFocus, tvDetailItem, tvPlaying; List<TvItem> tvPlayList; TvAccount tvAcc;
    List<TvItem> tvLibMovies, tvLibSeries; readonly Dictionary<string, List<TvItem>> tvFree = new Dictionary<string, List<TvItem>>(); List<KeyValuePair<string, List<TvItem>>> tvWorld;
    string tvSearch = ""; DispatcherTimer tvHeroTimer; int tvGen;
    bool TvOpen { get { return tvLayer != null && tvLayer.Visibility == Visibility.Visible; } }
    bool TvPlaying { get { return tvPlayer != null && tvPlayer.Visibility == Visibility.Visible; } }

    // the sections, as pills in the bar at the top; search, logins and close as round buttons on its right
    static readonly string[][] TvRailDef = {
        new[] { "home", "", "For You" }, new[] { "iptv-movies", "", "Movies" }, new[] { "iptv-series", "", "Shows" }, new[] { "iptv-live", "", "TV Live" },
        new[] { "guide", "", "TV Guide" },
        new[] { "langs", "", "Languages" }, new[] { "search", "", "Search" }, new[] { "full", "", "Full screen (F11)" }, new[] { "profiles", "", "Profiles" }, new[] { "close", "", "Back to Home" } };
    const string TvAccent = "#FF3D1F", TvBg = "#000000";
    static readonly FontFamily TvFont = new FontFamily("Segoe UI Variable Display, Segoe UI");
    Image tvHeroLogo, tvDetailLogo; TextBlock tvClock, tvMore; Grid tvHeroBox, tvBar; Border tvHeroFrame, tvHeroPosterBox; Image tvHeroPoster;
    readonly Dictionary<string, string> tvResolved = new Dictionary<string, string>();
    string TvUrl(TvItem it) { var dl = TvDownloaded(it); if (dl != null) return dl; string u; return it.Url != null && tvResolved.TryGetValue(it.Url, out u) ? u : it.Url; } DispatcherTimer tvClockTimer;

    // ---------- building it ----------
    void BuildTv() {
        TvStore.Load(); TvMine.Load();
        tvLayer = new Grid { Visibility = Visibility.Collapsed, Background = TvTheme.Brush(TvBg), ClipToBounds = true };
        tvLayer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tvLayer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // the picture fills the whole screen: darker to the left, towards the bottom and under the bar
        tvContent = new Grid(); Grid.SetColumnSpan(tvContent, 2);
        var heroArt = new Grid { IsHitTestVisible = false };
        tvHeroImg = new Image { Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0 };
        RenderOptions.SetBitmapScalingMode(tvHeroImg, BitmapScalingMode.HighQuality);
        tvHeroFrame = new Border { Child = tvHeroImg, ClipToBounds = true };
        heroArt.Children.Add(tvHeroFrame);
        heroArt.Children.Add(new Border { Background = Fade(true) });
        heroArt.Children.Add(new Border { Background = Fade(false) });
        heroArt.Children.Add(new Border { Height = 170, VerticalAlignment = VerticalAlignment.Top, Background = TopFade() });
        heroArt.Children.Add(TvGlowLayer());   // a glow in the picture's own colour (TvMotion.cs)
        tvHeroPoster = new Image { Stretch = Stretch.UniformToFill, Width = 290, Height = 435, Clip = new RectangleGeometry(new Rect(0, 0, 290, 435), 14, 14) };
        RenderOptions.SetBitmapScalingMode(tvHeroPoster, BitmapScalingMode.HighQuality);
        tvHeroPosterBox = new Border { Child = tvHeroPoster, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 50, 130, 0), Visibility = Visibility.Collapsed,
            Effect = new DropShadowEffect { BlurRadius = 60, ShadowDepth = 0, Opacity = 0.8, Color = Colors.Black } };
        heroArt.Children.Add(tvHeroPosterBox);
        tvContent.Children.Add(heroArt);
        // the page: the preview fills the first screen, the rows are under it
        tvRows = new StackPanel { Margin = new Thickness(0, 0, 0, 80) };
        var page = new StackPanel();
        tvHeroBox = new Grid { Height = 640 };
        var hero = new StackPanel { Margin = new Thickness(64, 0, 0, 62), MaxWidth = 740, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
        tvPageTitle = new TextBlock { Visibility = Visibility.Collapsed };
        tvHeroKicker = new TextBlock { FontSize = 13.5, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#D9FFFFFF"), Margin = new Thickness(0, 0, 0, 8) };
        tvHeroTitle = new TextBlock { FontSize = 56, FontFamily = TvFont, FontWeight = FontWeights.Black, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, LineHeight = 60, MaxHeight = 126, TextTrimming = TextTrimming.CharacterEllipsis,
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black } };
        tvHeroMeta = new TextBlock { Margin = new Thickness(0, 14, 0, 0) };
        tvHeroPlot = new TextBlock { FontSize = 16.5, Foreground = TvTheme.Brush("#EBFFFFFF"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), MaxHeight = 94, TextTrimming = TextTrimming.WordEllipsis, LineHeight = 23.5 };
        tvHeroBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 24, 0, 0) };
        // a film's own title logo (Cinemeta) stands in for its name when there is one
        tvHeroLogo = new Image { MaxHeight = 130, MaxWidth = 480, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 4) };
        RenderOptions.SetBitmapScalingMode(tvHeroLogo, BitmapScalingMode.HighQuality);
        foreach (var e in new UIElement[] { tvPageTitle, tvHeroKicker, tvHeroLogo, tvHeroTitle, tvHeroMeta, tvHeroPlot, tvHeroBtns }) hero.Children.Add(e);
        tvHeroBox.Children.Add(hero);
        tvMore = new TextBlock { Text = "Scroll down for more", FontSize = 16, Foreground = TvTheme.Brush("#80FFFFFF"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 20) };
        tvHeroBox.Children.Add(tvMore);
        page.Children.Add(tvHeroBox);
        tvStatus = new TextBlock { FontSize = 15, Foreground = TvTheme.Brush("#B3FFFFFF"), Margin = new Thickness(64, 0, 56, 16), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        page.Children.Add(tvStatus);
        page.Children.Add(tvRows);
        tvScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = TvMotionWrap(page), Focusable = false };   // with the gliding focus frame (TvMotion.cs)
        tvScroll.SizeChanged += (s, e) => tvHeroBox.Height = Math.Max(440, e.NewSize.Height - 30);
        // the preview stays at the top until you go down into the rows
        tvScroll.ScrollChanged += (s, e) => {
            if (tvZone != "rows" && tvScroll.VerticalOffset > 0 && !MouseMode && tvVAnim == 0) tvScroll.ScrollToVerticalOffset(0);
            if (e.VerticalChange != 0) TvPostersSoon();   // pictures far from view let go, near ones back
            // scrolled down: the big poster (part of the background) steps aside
            if (tvHeroPosterBox != null) tvHeroPosterBox.Opacity = tvScroll.VerticalOffset > 120 || (tvZone == "rows" && !tvMouseNav) ? 0 : 1;
        };
        tvContent.Children.Add(tvScroll);
        tvLayer.Children.Add(tvContent);
        // the bar at the top: the mark and the time, the sections, then search, logins and close
        tvBar = new Grid { Height = 60, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(44, 22, 44, 0) };
        Grid.SetColumnSpan(tvBar, 2);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        brand.Children.Add(TvMark(40));
        tvClock = new TextBlock { FontSize = 15.5, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#A6FFFFFF"), Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(tvClock);
        tvBar.Children.Add(brand);
        tvRail = new StackPanel { Orientation = Orientation.Horizontal };
        tvRailHost = new Border { Child = tvRail, CornerRadius = new CornerRadius(27), Padding = new Thickness(5), Background = TvTheme.Brush("#F21C1C1E"), BorderBrush = TvTheme.Brush("#2E2E31"), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        tvBar.Children.Add(tvRailHost);
        var icons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        foreach (var d in TvRailDef) { var b = TvRailItem(d); if (d[1] == "") tvRail.Children.Add(b); else icons.Children.Add(b); }
        tvBar.Children.Add(icons);
        tvLayer.Children.Add(tvBar);
        BuildTvDetail();
        BuildTvPlayer();
        BuildTvProfiles();
        tvLayer.LayoutTransform = N<Grid>("Main").LayoutTransform;
        Panel.SetZIndex(tvLayer, 800);
        N<Grid>("Clipped").Children.Add(tvLayer);
        tvHeroTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        tvHeroTimer.Tick += (s, e) => { tvHeroTimer.Stop(); ShowTvHero(tvFocus); };
        tvClockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        tvClockTimer.Tick += (s, e) => TvClock();
        tvClockTimer.Start(); TvClock();
    }
    // ---------- full screen: the button in the bar, F11, the controller's View button ----------
    void TvToggleFull() { ToggleFullscreen(); TvFullIcon(); SetStep(isFull ? "Full screen  •  F11 or the View button to leave" : "", Ui.Teal); }
    // the bar's button shows what it will do
    void TvFullIcon() {
        int i = tvRailIds.IndexOf("full"); if (i < 0) return;
        var t = tvRailItems[i].Child as TextBlock; if (t == null) return;
        t.Text = isFull ? "" : ""; tvRailItems[i].ToolTip = isFull ? "Leave full screen (F11)" : "Full screen (F11)";
    }
    void TvClock() { if (tvClock != null) tvClock.Text = DateTime.Now.ToString("ddd  HH:mm", System.Globalization.CultureInfo.InvariantCulture); }
    // the app's mark: a tile with a screen on it, in the theme's colours
    static Border TvMark(double s) {
        return new Border { Width = s, Height = s, CornerRadius = new CornerRadius(s * 0.26), Background = TvTheme.Mark,
            Child = new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = s * 0.48, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
    }
    // the picture fades into the background (the theme's, TvThemes.cs)
    static Brush Fade(bool left) {
        return TvTheme.Live(left ? new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) } : new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) }, b => {
        var bg = TvTheme.Col(TvBg); b.GradientStops.Clear();
        if (left) { b.GradientStops.Add(new GradientStop(Color.FromArgb(0xD9, bg.R, bg.G, bg.B), 0)); b.GradientStops.Add(new GradientStop(Color.FromArgb(0x8C, bg.R, bg.G, bg.B), 0.38)); b.GradientStops.Add(new GradientStop(Color.FromArgb(0, bg.R, bg.G, bg.B), 0.72)); }
        else { b.GradientStops.Add(new GradientStop(Color.FromArgb(0, bg.R, bg.G, bg.B), 0.42)); b.GradientStops.Add(new GradientStop(Color.FromArgb(0xC8, bg.R, bg.G, bg.B), 0.8)); b.GradientStops.Add(new GradientStop(bg, 1)); }
        });
    }
    static Brush TopFade() { var b = new LinearGradientBrush(Color.FromArgb(0xB0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90); b.Freeze(); return b; }
    Border TvRailItem(string[] d) {
        bool icon = d[1] != "";
        if (d[0] == "profiles") {
            var pb = new Border { Child = new Grid { Width = 38, Height = 38 }, Width = 46, Height = 46, CornerRadius = new CornerRadius(23), BorderThickness = new Thickness(2), BorderBrush = TvTheme.Brush("#40FFFFFF"),
                Margin = new Thickness(12, 0, 0, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = "Switch profile", RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
            int pi = tvRailItems.Count;
            pb.MouseLeftButtonUp += (s, e) => { tvRailIdx = pi; TvRailPick(); };
            tvRailItems.Add(pb); tvRailIds.Add(d[0]);
            return pb;
        }
        var t = new TextBlock { Text = icon ? d[1] : d[2], FontFamily = icon ? Ui.Mdl : TvFont, FontWeight = icon ? FontWeights.Normal : FontWeights.SemiBold, FontSize = icon ? 21 : 17.5,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border { Child = t, Height = 44, MinWidth = 44, Padding = new Thickness(icon ? 0 : 19, 0, icon ? 0 : 19, 1), CornerRadius = new CornerRadius(22), Margin = new Thickness(icon ? 10 : 0, 0, 0, 0),
            Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = icon ? d[2] : null, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
        int idx = tvRailItems.Count;
        b.MouseLeftButtonUp += (s, e) => { tvRailIdx = idx; TvRailPick(); };
        b.MouseEnter += (s, e) => { if (MouseMode && !(idx == tvRailIdx && tvZone == "rail")) b.Background = TvTheme.Brush("#33FFFFFF"); };
        b.MouseLeave += (s, e) => MarkTvRail();
        tvRailItems.Add(b); tvRailIds.Add(d[0]);
        return b;
    }
    void TvRailWide(bool wide) { MarkTvRail(); }
    void MarkTvRail() {
        for (int i = 0; i < tvRailItems.Count; i++) {
            bool icon = TvRailDef[i][1] != "", cur = tvRailIds[i] == tvPage, on = i == tvRailIdx && tvZone == "rail";
            var b = tvRailItems[i]; var t = b.Child as TextBlock;
            if (t == null) { b.BorderBrush = on ? Brushes.White : TvTheme.Brush("#40FFFFFF"); var ps = (ScaleTransform)b.RenderTransform; ps.ScaleX = ps.ScaleY = on ? 1.1 : 1; continue; }
            b.Background = on ? Brushes.White : cur && !icon ? TvTheme.Brush("#3A3A3C") : Brushes.Transparent;
            t.Foreground = on ? Brushes.Black : cur || icon ? Brushes.White : TvTheme.Brush("#8E8E93");
            t.FontWeight = icon ? FontWeights.Normal : cur || on ? FontWeights.Bold : FontWeights.SemiBold;
            var st = (ScaleTransform)b.RenderTransform; double k = on ? 1.08 : 1;
            if (Math.Abs(st.ScaleX - k) > 0.001) { st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(k, 140)); st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(k, 140)); }
        }
    }

    // ---------- opening and closing ----------
    void OpenTv() {
        if (tvLayer == null) BuildTv();
        tvLayer.Visibility = Visibility.Visible;
        tvLayer.BeginAnimation(UIElement.OpacityProperty, Anim(1, 260, 0));
        Sound.Play(Sfx.Launch);
        OpenTvProfiles();
        TvFullIcon();
        TvIntro(); TvIdleStart();   // the opening, and the screensaver's clock (TvLook.cs)
        if (tvWarmGen != tvPreGen) TvPrefetch(null);   // not while the start-up warm-up is still going
        HideRing(); SetHints();
    }
    void CloseTv() {
        if (!TvOpen) return;
        if (TvPlaying) TvStop();
        TvCloseGuide(); TvCloseLive(); TvCloseMulti();
        tvProfWait = false; tvPreAfter.Clear(); TvPeekHide();   // a profile that was still loading doesn't open by itself later
        if (tvSearchPage != null) tvSearchPage.Visibility = Visibility.Collapsed;   // search starts fresh next time
        tvLayer.Visibility = Visibility.Collapsed;
        Sound.Play(Sfx.Back);
        SetHints();
    }

    // ---------- the sections ----------
    void TvShowPage(string id) {
        if (id == "close") { CloseTv(); return; }
        if (id == "full") { TvToggleFull(); return; }   // full screen and back (the button keeps its place)
        if (id == "search") { TvAskSearch(); return; }
        if (id == "langs") { TvOpenLangs(false); return; }
        if (id == "profiles") { OpenTvProfiles(); return; }
        TvPeekHide();
        if (id != "guide") TvCloseGuide();
        TvCloseLive();
        tvFeatured = null;
        tvPage = id; tvGen++; int gen = tvGen; tvImgGen++;
        tvRowList.Clear(); tvRows.Children.Clear(); tvMarked = null; TvFocusHide(); tvStatus.Visibility = Visibility.Collapsed;
        tvScroll.BeginAnimation(TvVOffsetProperty, null); tvScroll.ScrollToVerticalOffset(0);
        TvPageIn();
        int ri = tvRailIds.IndexOf(id); if (ri >= 0) tvRailIdx = ri;
        var def = TvRailDef.FirstOrDefault(d => d[0] == id); string title = def != null ? def[2] : id == "accounts" ? "Logins" : id;
        tvPageTitle.Text = id.StartsWith("iptv") ? (tvAcc != null ? tvAcc.Name + "  •  " : "My IPTV  •  ") + title : id == "accounts" ? "My IPTV  •  Logins" : title;
        MarkTvRail();
        if (id == "guide") { TvOpenGuide(); return; }
        if (id == "iptv-live") { TvOpenLive(); return; }
        Action<string> wait = text => { tvStatus.Text = text; tvStatus.Visibility = Visibility.Visible; };
        // work in the background, then the rows on the window's thread (only if this section is still the one shown)
        bool keep = TvReadyPages.Contains(id); var accK = tvAcc;
        Action<Func<List<TvRow>>, string> load = (make, waiting) => {
            // made ahead (TvReady.cs): at once
            var ready = keep ? TvRowsFromCache(accK, id) : null;
            if (ready != null) { tvStatus.Visibility = Visibility.Collapsed; TvSetRows(ready); return; }
            wait(waiting);
            new Thread(() => {
                List<TvRow> rows = null; string err = null; string ver = TvVer;
                try { rows = make(); if (keep) TvRowsToCache(accK, id, rows, ver); } catch (Exception e) { err = e.Message; }
                W.Dispatcher.BeginInvoke(new Action(() => {
                    if (gen != tvGen) return;
                    if (err != null) { wait("Couldn't load this: " + err); TvSetRows(new List<TvRow>()); return; }
                    tvStatus.Visibility = Visibility.Collapsed; TvSetRows(rows);
                }));
            }) { IsBackground = true }.Start();
        };
        var acc0 = tvAcc;
        if (id == "home") load(() => acc0 != null ? TvProviderHomeRows(acc0) : TvFreeHomeRows(), acc0 != null ? "Loading " + acc0.Name + "..." : TvFree.Loaded ? "Loading..." : "Getting the free films and series (the first time takes about a minute)...");
        else if (id == "live") load(() => TvSources.Select(s => TvRowOf(s.Key, s.Value, true)).ToList(), "Loading the free channels...");
        else if (id == "world") { TvOpenBrowse("world", null); return; }
        else if (id == "world-rows") load(() => { if (tvWorld == null) tvWorld = TvSource.WorldChannels(); return tvWorld.Select(kv => TvRowOf(kv.Key + "  •  " + kv.Value.Count, kv.Value, true)).ToList(); }, "Loading channels from all over the world (about 11,000, this takes a moment)...");
        else if (id == "library") load(() => { TvLib(); return TvLibraryRows(); }, "Looking through your video folders (" + string.Join(", ", TvSource.LibraryFolders()) + ")...");
        else if (id == "accounts") TvSetRows(TvAccountRows());
        else if (id.StartsWith("iptv")) {
            if (tvAcc == null) {
                if (id == "iptv-movies") load(TvFreeMovieRows, TvFree.Loaded ? "Loading the free films..." : "Getting the free films and series (the first time takes about a minute)...");
                else if (id == "iptv-series") load(TvFreeShowRows, TvFree.Loaded ? "Loading the free series..." : "Getting the free films and series (the first time takes about a minute)...");
                else load(() => TvSources.Select(s => TvRowOf(s.Key, s.Value, true)).ToList(), "Loading the free channels...");
                return;
            }
            var a = tvAcc;
            load(() => { if (!a.Loaded) TvSource.LoadAccount(a); return TvIptvRows(a, id); }, "Loading " + a.Name + "...");
        }
    }
    // the free channels, loaded once
    Dictionary<string, List<TvItem>> TvSources {
        get {
            lock (tvFree) {
                if (tvFree.Count == 0)
                    foreach (var l in TvSource.FreeLists) {
                        try {
                            var items = TvSource.FreeChannels(l[1], l[0]);
                            if (l[0] == "Nederland") { foreach (var g in items.GroupBy(TvSource.DutchGroup).Where(g => g.Key != null).OrderBy(g => TvSource.DutchOrder(g.Key))) tvFree[g.Key] = g.OrderBy(x => x.Title.StartsWith("NPO") ? 0 : 1).ThenBy(x => x.Title.Replace(" HD", "")).ThenBy(x => x.Title.Contains(" HD") ? 0 : 1).ToList(); }
                            else tvFree[l[0]] = items;
                        } catch { }
                    }
                return tvFree;
            }
        }
    }
    static string TvGroupName(string g) {
        switch ((g ?? "").ToLowerInvariant()) {
            case "general": return "algemeen"; case "news": return "nieuws"; case "music": return "muziek"; case "kids": return "kinderen"; case "religious": return "religie";
            case "sports": return "sport"; case "entertainment": return "entertainment"; case "documentary": return "documentaires"; case "movies": return "films"; case "series": return "series";
        }
        return (g ?? "overig").ToLowerInvariant();
    }
    TvRow TvRowOf(string title, IEnumerable<TvItem> items, bool wide) { return new TvRow { Title = title, Items = items.ToList(), Wide = wide }; }
    List<TvRow> TvHomeRows() {
        var rows = new List<TvRow>();
        var cont = TvStore.Continue(); if (cont.Count > 0) rows.Add(TvRowOf("Continue watching", cont, false));
        var free = TvSources; List<TvItem> nl;
        if (free.TryGetValue("NPO", out nl)) rows.Add(TvRowOf("NPO, live", nl, true));
        if (free.TryGetValue("Regional", out nl)) rows.Add(TvRowOf("Regional TV, live", nl.Take(30), true));
        if (tvAcc != null) {
            try {
                if (!tvAcc.Loaded) TvSource.LoadAccount(tvAcc);
                var favs = tvAcc.Live.Concat(tvAcc.Movies).Concat(tvAcc.Series).Where(TvStore.IsFav).Take(40).ToList();
                if (favs.Count > 0) rows.Add(TvRowOf("My list", favs, false));
                if (tvAcc.Movies.Count > 0) rows.Add(TvRowOf("New films on " + tvAcc.Name, tvAcc.Movies.Take(40), false));
                if (tvAcc.Series.Count > 0) rows.Add(TvRowOf("Series on " + tvAcc.Name, tvAcc.Series.Take(40), false));
                if (tvAcc.Live.Count > 0) rows.Add(TvRowOf("Live on " + tvAcc.Name, tvAcc.Live.Take(30), true));
            } catch { }
        }
        TvLib();
        if (tvLibMovies.Count > 0) rows.Add(TvRowOf("Your films", tvLibMovies.Take(40), false));
        if (tvLibSeries.Count > 0) rows.Add(TvRowOf("Your series", tvLibSeries.Take(40), false));
        return rows;
    }
    List<TvItem> TvServices() {
        return TvSource.Services.Select(s => new TvItem { Kind = "service", Title = s[1], Url = s[2], Group = s[0], Plot = s[5], Image = null, Genre = s[3] + "|" + s[4] }).ToList();
    }
    List<TvRow> TvServiceRows() { return TvServices().GroupBy(s => s.Group).Select(g => TvRowOf(g.Key, g, true)).ToList(); }
    void TvLib() { if (tvLibMovies == null) { List<TvItem> m, s; TvSource.ScanLibrary(out m, out s); tvLibMovies = m; tvLibSeries = s; } }
    List<TvRow> TvLibraryRows() {
        var rows = new List<TvRow>();
        if (tvLibMovies.Count > 0) rows.Add(TvRowOf("Films", tvLibMovies, false));
        if (tvLibSeries.Count > 0) rows.Add(TvRowOf("Series", tvLibSeries, false));
        if (rows.Count == 0) W.Dispatcher.BeginInvoke(new Action(() => { tvStatus.Text = "No video files found in " + string.Join(", ", TvSource.LibraryFolders()) + ". Add the folders with your films and series below."; tvStatus.Visibility = Visibility.Visible; }));
        // where it looks: pick a folder to stop looking there, or add one
        var folders = TvSource.LibraryFolders().Select(f => new TvItem { Kind = "folder", Title = Path.GetFileName(f.TrimEnd('\\')), Url = f, Plot = f + "  •  pick to stop looking here" }).ToList();
        folders.Add(new TvItem { Kind = "addfolder", Title = "Add a folder", Plot = "A folder with your own films and series (series are found by their S01E02 names)." });
        rows.Add(TvRowOf("Where your library looks", folders, true));
        return rows;
    }
    List<TvRow> TvIptvRows(TvAccount a, string id) {
        var list = id == "iptv-live" ? a.Live : id == "iptv-movies" ? a.Movies : a.Series;
        bool wide = id == "iptv-live";
        var rows = new List<TvRow>();
        bool films = id == "iptv-movies", shows = id == "iptv-series";
        // as a streaming service: what you're in the middle of, your list, the top 10, titles like what you watched,
        // what's new, then a row per genre; the provider's own categories after those
        if (films || shows) { var cont = TvStore.Continue(a, true).Where(x => films ? x.Kind == "movie" : x.Kind == "episode" || x.Kind == "series").ToList(); if (cont.Count > 0) rows.Add(TvRowOf("Continue watching", cont, false)); }
        var favs = list.Where(TvStore.IsFav).ToList(); if (favs.Count > 0) rows.Add(TvRowOf("My list", favs, wide));
        TvFeature(id != "iptv-live" ? TvPickFeatured(list.Take(60)) : null);
        if (id != "iptv-live" && list.Count > 0) rows.Add(TvCategoryRow(films ? "movies" : "series", list.Select(x => x.Group ?? "Other")));
        if ((films || shows) && list.Count >= 10) rows.Add(new TvRow { Title = films ? "Top 10 new films" : "Top 10 new series", Items = list.Take(10).ToList(), Top10 = true });
        // what the provider added in the last seven days (TvExtras.cs)
        if (films || shows) { var nw = TvNewThisWeek(list); if (nw.Count >= 3) rows.Add(TvRowOf(films ? "New this week" : "New episodes this week", nw, false)); }
        if (films || shows) rows.AddRange(TvBecauseRows(list, x => x.Kind == (films ? "movie" : "series"), 2));
        if (films) { var cols = TvCollections(list, 30); if (cols.Count >= 2) rows.Add(TvRowOf("Collections", cols, false)); }   // film series as one tile
        if ((films || shows) && list.Count > 10) rows.Add(TvRowOf(films ? "Recently added" : "New series", list.Skip(10).Take(40), false));
        if (films || shows) rows.AddRange(TvGenreRowsFor(list, shows));
        foreach (var g in list.GroupBy(x => x.Group ?? "Other")) rows.Add(TvRowOf(g.Key, g, wide));
        if (list.Count == 0) W.Dispatcher.BeginInvoke(new Action(() => { tvStatus.Text = a.Name + " has nothing here."; tvStatus.Visibility = Visibility.Visible; }));
        return rows;
    }
    List<TvRow> TvAccountRows() {
        var items = TvStore.Accounts.Select(a => new TvItem { Kind = "account", Id = a.Id, Title = a.Name, Plot = (a.Type == "m3u" ? "Playlist link" : "Xtream login: " + a.Base) + (a == tvAcc ? "  •  in use" : ""), Account = a }).ToList();
        items.Add(new TvItem { Kind = "add", Title = "Add a login", Plot = "Your provider's Xtream login (server, username, password) or a playlist (M3U) link." });
        return new List<TvRow> { TvRowOf("Your IPTV logins", items, true) };
    }
    void TvAskSearch() {
        TvSearchShow(); return;   // the search page (TvSearch.cs); below, the old way: the on-screen keyboard, then rows
#pragma warning disable 162
        OpenOsk("Search TV & Movies", tvSearch, null, text => {
            tvSearch = text.Trim(); if (tvSearch.Length == 0) return;
            tvPage = "search"; tvGen++; int gen = tvGen; tvRowList.Clear(); tvRows.Children.Clear(); tvMarked = null; TvFocusHide();
            tvPageTitle.Text = "Search  •  " + tvSearch; tvStatus.Text = "Searching..."; tvStatus.Visibility = Visibility.Visible;
            string q = tvSearch.ToLowerInvariant();
            new Thread(() => {
                var rows = new List<TvRow>();
                Func<IEnumerable<TvItem>, List<TvItem>> hit = l => l.Where(i => (i.Title ?? "").ToLowerInvariant().Contains(q)).Take(60).ToList();
                try {
                    if (tvAcc != null) {
                        if (!tvAcc.Loaded) TvSource.LoadAccount(tvAcc);
                        var m = hit(tvAcc.Movies); if (m.Count > 0) rows.Add(TvRowOf("Films", m, false));
                        var s = hit(tvAcc.Series); if (s.Count > 0) rows.Add(TvRowOf("Series", s, false));
                        var l = hit(tvAcc.Live); if (l.Count > 0) rows.Add(TvRowOf("Live", l, true));
                    }
                    var ff = hit(TvFree.Films.Concat(TvFree.Shows)); if (ff.Count > 0) rows.Add(TvRowOf("Free films and series", ff, false));
                    // people: the films and series they're in
                    if (q.Length >= 4) {
                        var pool = tvAcc != null ? (tvAcc.Movies ?? new List<TvItem>()).Concat(tvAcc.Series ?? new List<TvItem>()) : TvFree.Films.Concat(TvFree.Shows);
                        var ppl = pool.Where(x => TvHas(x, q)).Take(40).ToList(); if (ppl.Count > 0) rows.Add(TvRowOf("With \"" + tvSearch + "\" in it", ppl, false));
                    }
                    var fc = hit(TvSources.Values.SelectMany(x => x)); if (fc.Count > 0) rows.Add(TvRowOf("Free channels", fc, true));
                } catch { }
                W.Dispatcher.BeginInvoke(new Action(() => {
                    if (gen != tvGen) return;
                    tvStatus.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed; tvStatus.Text = "Nothing found for \"" + tvSearch + "\".";
                    TvSetRows(rows);
                }));
            }) { IsBackground = true }.Start();
        }, null);
    }

#pragma warning restore 162
    // ---------- the rows of posters ----------
    void TvSetRows(List<TvRow> rows) {
        tvRowList.Clear(); tvRows.Children.Clear(); tvMarked = null; TvFocusHide();
        foreach (var r in rows.Where(r => r.Items.Count > 0)) {
            foreach (var x in r.Items) TvApplyMeta(x);
            r.Items = r.Items.Where(TvAllowed).ToList(); if (r.Items.Count == 0) continue;
            var head = new TextBlock { Text = r.Title, FontSize = 21, FontFamily = TvFont, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(64, 4, 0, 8) };
            r.Panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(64, 14, 56, 22) };
            r.Scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = r.Panel, Focusable = false, Margin = new Thickness(0, -14, 0, 0) };
            var row = r;
            r.Scroll.PreviewMouseWheel += (s, e) => { if (Keyboard.Modifiers == ModifierKeys.Shift) { row.Scroll.ScrollToHorizontalOffset(row.Scroll.HorizontalOffset - e.Delta); e.Handled = true; } };
            r.Scroll.ScrollChanged += (s, e) => { if (e.HorizontalChange != 0) TvFocusFollow(row); };
            var box = new Grid();
            box.Children.Add(r.Scroll);
            // arrows at the row's ends, for the mouse
            foreach (var dir in new[] { -1, 1 }) {
                int d = dir;
                var arrow = new Border { Width = 46, HorizontalAlignment = d < 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right, Background = TvTheme.Brush("#99050709"), Cursor = Cursors.Hand, Opacity = 0,
                    Child = new TextBlock { Text = d < 0 ? "" : "", FontFamily = Ui.Mdl, FontSize = 22, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                arrow.MouseEnter += (s, e) => arrow.Opacity = 1; arrow.MouseLeave += (s, e) => arrow.Opacity = 0;
                arrow.MouseLeftButtonUp += (s, e) => { TvBuildMore(row, 30); row.Scroll.ScrollToHorizontalOffset(row.Scroll.HorizontalOffset + d * row.Scroll.ViewportWidth * 0.8); };
                box.Children.Add(arrow);
            }
            var stack = new StackPanel(); stack.Children.Add(head); stack.Children.Add(box);
            r.Root = stack; tvRows.Children.Add(stack);
            tvRowList.Add(r);
            // the first rows at once; the others hold their place and get their tiles just after (TvBuildRowsLater)
            if (tvRowList.Count <= 3) TvBuildMore(r, 14); else r.Panel.MinHeight = (r.Wide ? 153 : 246) + 6;
        }
        TvBuildRowsLater(tvGen);
        tvRowIdx = 0; tvHeroIdx = 0; if (tvZone != "rail" || tvRowList.Count > 0) tvZone = tvRowList.Count > 0 ? "hero" : "rail";
        tvFeatured = TvFeaturedFor(tvPage);
        tvFocus = tvFeatured ?? (tvRowList.Count > 0 ? tvRowList[0].Items[0] : null);
        if (tvZone == "rail") TvRailWide(true); else TvRailWide(false);
        ShowTvHero(tvFocus);
        MarkTv();
    }
    // the rest of a page's rows, one at a time while nothing else is waiting, so a big page (a provider's films can be
    // a hundred rows) shows at once and stays quick to leave
    void TvBuildRowsLater(int gen) {
        W.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
            if (gen != tvGen) return;
            var r = tvRowList.FirstOrDefault(x => x.Tiles.Count == 0);
            if (r == null) return;
            TvBuildMore(r, 14);
            TvBuildRowsLater(gen);
        }));
    }
    // tiles are made as they're about to be seen (a provider can have thousands in one row)
    void TvBuildMore(TvRow r, int more) {
        int upto = Math.Min(r.Items.Count, r.Tiles.Count + more);
        for (int i = r.Tiles.Count; i < upto; i++) { var t = TvTile(r, i); r.Tiles.Add(t); r.Panel.Children.Add(r.Top10 ? TvTop10(t, i) : (UIElement)t); }
    }
    Border TvTile(TvRow r, int i) {
        var it = r.Items[i];
        double w = r.Wide ? 272 : 164, h = r.Wide ? 153 : 246;
        var g = new Grid { Width = w, Height = h, ClipToBounds = true };
        string c1 = "#1c252c", c2 = "#33424d";
        bool chan = it.Kind == "live";
        if (it.Kind == "service" && it.Genre != null) { var cs = it.Genre.Split('|'); c1 = cs[0].ToLowerInvariant(); c2 = cs[1].ToLowerInvariant(); }
        else if (chan) { c1 = "#2C2C30"; c2 = "#161618"; }
        else { int hsh = Math.Abs((it.Title ?? "").GetHashCode()); string[] pal = { "#5b2a86", "#1b4f72", "#7b241c", "#145a32", "#784212", "#4a235a", "#1a5276", "#6e2c00" }; c1 = pal[hsh % pal.Length]; c2 = "#0b1114"; }
        g.Children.Add(new Border { Background = TvTheme.Gradient(c1, c2) });
        // the name, when there's no picture (or under a channel's logo)
        var name = new TextBlock { Text = it.Kind == "add" ? "+  " + it.Title : it.Title, FontSize = r.Wide ? 18 : 16, FontFamily = Ui.Semi, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14), TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = h - 20 };
        g.Children.Add(name);
        if (!string.IsNullOrEmpty(it.Image)) {
            var img = new Image { Stretch = it.Kind == "live" ? Stretch.Uniform : Stretch.UniformToFill, Margin = it.Kind == "live" ? new Thickness(36, 22, 36, 34) : new Thickness(0) };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            Action shown = () => { if (it.Kind != "live") name.Visibility = Visibility.Collapsed; else { name.VerticalAlignment = VerticalAlignment.Bottom; name.FontSize = 12.5; name.Margin = new Thickness(8, 0, 8, 8); name.MaxHeight = 36; } };
            // remembered, so it can be let go far from view; rows far down get theirs when scrolled near (TvMotion.cs)
            TvRememberPicture(img, it.Image, (int)(w * 1.5), shown);
            if (TvRowSoon(r)) TvLoadImage(img, it.Image, (int)(w * 1.5), shown);
            g.Children.Add(img);
        }
        TvArtLater(it, g, name, (int)(w * 1.5));
        // how far you got
        Dictionary<string, object> p;
        if (TvStore.Progress.TryGetValue(it.Key, out p) && Json.Num(p, "Dur") > 0) {
            double frac = Math.Min(1, Json.Num(p, "Pos") / Json.Num(p, "Dur"));
            var bar = new Grid { Height = 4, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 10, 10) };
            bar.Children.Add(new Border { Background = TvTheme.Brush("#66ffffff"), CornerRadius = new CornerRadius(2) });
            bar.Children.Add(new Border { Background = TvTheme.Brush(TvAccent), CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = (w - 20) * frac });
            g.Children.Add(bar);
        }
        if (TvStore.IsFav(it)) g.Children.Add(new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 14, Foreground = TvTheme.Brush("#f2c230"), Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top });
        if (TvMine.IsWatched(it)) g.Children.Add(TvWatchedChip());
        else { var badge = TvBadge(it); if (badge != null) g.Children.Add(badge); }   // "Recently added", "New episodes" (TvLook.cs)
        var tile = new Border { Child = g, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 16, 0), BorderThickness = new Thickness(3), BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1), Tag = it };
        g.Clip = new RectangleGeometry(new Rect(0, 0, w, h), 10, 10);
        var row = r; int idx = i;
        TvOnHover(tile, () => { int ri = tvRowList.IndexOf(row); if (ri != tvRowIdx || idx != row.Col || tvZone != "rows") TvFocusTile(ri, idx, false); });
        tile.MouseLeftButtonUp += (s, e) => { TvFocusTile(tvRowList.IndexOf(row), idx, false); TvPick(it, row); };
        tile.MouseRightButtonUp += (s, e) => { e.Handled = true; TvItemActions(it); };
        return tile;
    }
    // pictures from the provider, loaded in the background (and quietly skipped when they don't come)
    static void TvLoadImage(Image img, string url, int px, Action ok) {
        try {
            var bi = new BitmapImage();
            bi.BeginInit();
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) { TvLoadViaDisk(img, url, px, ok); return; }   // kept on disk (cache	vimg)
            bi.UriSource = url.StartsWith("http", StringComparison.OrdinalIgnoreCase) || File.Exists(url) ? new Uri(url) : null;
            if (bi.UriSource == null) return;
            bi.DecodePixelWidth = px; bi.CacheOption = BitmapCacheOption.OnLoad; bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bi.EndInit();
            Action show = () => { img.Source = bi; img.BeginAnimation(UIElement.OpacityProperty, Anim(1, 250, 0)); if (ok != null) ok(); };
            if (bi.IsDownloading) { bi.DownloadCompleted += (s, e) => show(); bi.DownloadFailed += (s, e) => { }; } else show();
        } catch { }
    }
    void TvFocusTile(int row, int col, bool sound) {
        if (row < 0 || row >= tvRowList.Count) return;
        var r = tvRowList[row];
        col = Math.Max(0, Math.Min(r.Items.Count - 1, col));
        if (col >= r.Tiles.Count - 6) TvBuildMore(r, 30);
        bool moved = row != tvRowIdx || col != r.Col || tvZone != "rows";
        tvRowIdx = row; r.Col = col; tvZone = "rows";
        if (sound) Sound.Play(moved ? Sfx.Move : Sfx.Bump);
        tvFocus = r.Items[col];
        tvHeroTimer.Stop(); tvHeroTimer.Start();
        MarkTv();
        TvPeekSoon();   // after a moment it opens up into a card (TvPeek.cs)
    }
    void MarkTv() {
        if (tvZone != "rows") TvPeekHide();
        // only the tile that was picked and the one that is now change; the white frame glides between them (TvMotion.cs)
        TvMarkTiles();
        for (int i = 0; i < tvHeroButtons.Count; i++) TvMarkButton(tvHeroButtons[i], tvZone == "hero" && i == tvHeroIdx);
        if (tvZone == "rows" && tvRowIdx < tvRowList.Count) {
            var r = tvRowList[tvRowIdx];
            if (r.Col < r.Tiles.Count) {
                var t = r.Tiles[r.Col];
                // the picked row slides to about a third of the way down, its tile into view with room on both sides
                bool hover = tvHoverMark;
                W.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
                    double shift = 0;
                    if (!hover) {
                        try {
                            double y = r.Root.TransformToAncestor((Visual)tvScroll.Content).Transform(new Point(0, 0)).Y;
                            double vh = tvScroll.ViewportHeight;
                            double to = y - (tvRowIdx == 0 ? vh * 0.6 : vh * 0.33);
                            // the first row: the bar stays up, so the preview's words stop just under it
                            if (tvRowIdx == 0) {
                                double heroTop = ((FrameworkElement)tvHeroKicker.Parent).TransformToAncestor((Visual)tvScroll.Content).Transform(new Point(0, 0)).Y;
                                to = Math.Min(to, heroTop - (tvBar.Margin.Top + tvBar.Height + 14));
                            }
                            TvSlideV(to);
                            double x = t.TransformToAncestor(r.Panel).Transform(new Point(0, 0)).X, vw = r.Scroll.ViewportWidth, tw = t.ActualWidth + 12;
                            if (x < r.Scroll.HorizontalOffset + tw * 0.5) shift = TvSlideH(r.Scroll, x - tw * 0.5);
                            else if (x + tw * 1.6 > r.Scroll.HorizontalOffset + vw) shift = TvSlideH(r.Scroll, x + tw * 1.6 - vw);
                        } catch { }
                    }
                    if (tvZone == "rows" && tvRowIdx < tvRowList.Count && tvRowList[tvRowIdx] == r && r.Col < r.Tiles.Count && r.Tiles[r.Col] == t) TvPlaceFocus(true, shift);
                }));
            }
        } else { TvFocusHide(); TvSlideV(0); W.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { if (tvZone != "rows" && tvVAnim == 0) tvScroll.ScrollToVerticalOffset(0); })); }
        // the bar goes away while you look further down the rows
        bool bar = tvMouseNav || !(tvZone == "rows" && tvRowIdx > 0);
        tvHeroBtns.Visibility = tvZone == "rows" && !tvMouseNav ? Visibility.Collapsed : Visibility.Visible;
        tvHeroPosterBox.Opacity = tvZone == "rows" && (!tvMouseNav || tvScroll.VerticalOffset > 120) ? 0 : 1;
        tvBar.BeginAnimation(UIElement.OpacityProperty, Anim(bar ? 1 : 0, 180)); tvBar.IsHitTestVisible = bar;
        tvMore.Visibility = tvZone == "rows" || tvRowList.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        MarkTvRail(); SetHints();
    }
    // pill buttons; with no words, a round one (its ToolTip says what it does)
    Border TvButton(string glyph, string text, bool primary, Action act) {
        bool round = text == null;
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (glyph != null) sp.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.Mdl, FontSize = round ? 19 : 14.5, Margin = new Thickness(0, round ? 0 : 1, round ? 0 : 10, 0), VerticalAlignment = VerticalAlignment.Center });
        if (!round) sp.Children.Add(new TextBlock { Text = text, FontSize = 17.5, FontFamily = Ui.Semi, VerticalAlignment = VerticalAlignment.Center });
        var b = new Border { Child = sp, Height = round ? 52 : 48, Width = round ? 52 : double.NaN, Padding = new Thickness(round ? 0 : 24, 0, round ? 0 : 26, 1), CornerRadius = new CornerRadius(round ? 26 : 24),
            Margin = new Thickness(0, 0, 14, 0), Cursor = Cursors.Hand, Tag = primary, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
        b.MouseLeftButtonUp += (s, e) => { Sound.Play(Sfx.Select); act(); };
        b.MouseEnter += (s, e) => { if (MouseMode) TvMarkButton(b, true); };
        b.MouseLeave += (s, e) => { if (MouseMode) TvMarkButton(b, false); };
        b.DataContext = act;
        TvMarkButton(b, false);
        return b;
    }
    static void TvMarkButton(Border b, bool on) {
        bool primary = (bool)b.Tag;
        b.Background = on ? Brushes.White : primary ? TvTheme.Brush("#E6FFFFFF") : TvTheme.Brush("#59767676");
        foreach (var t in ((StackPanel)b.Child).Children.OfType<TextBlock>()) t.Foreground = on || primary ? Brushes.Black : Brushes.White;
        b.Effect = on ? new DropShadowEffect { BlurRadius = 26, ShadowDepth = 0, Opacity = 0.45, Color = Colors.Black } : null;
        var st = (ScaleTransform)b.RenderTransform; double k = on ? 1.08 : 1;
        if (Math.Abs(st.ScaleX - k) > 0.001) { st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(k, 130)); st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(k, 130)); }
    }

    // ---------- the preview: the picked thing, full screen ----------
    void ShowTvHero(TvItem it) {
        tvHeroButtons.Clear(); tvHeroBtns.Children.Clear();
        var row = tvRowIdx < tvRowList.Count ? tvRowList[tvRowIdx] : null;
        if (it == null) {
            var d = TvRailDef.FirstOrDefault(x => x[0] == tvPage);
            tvHeroTitle.Text = d != null ? d[2] : ""; tvHeroKicker.Text = ""; tvHeroMeta.Inlines.Clear(); tvHeroPlot.Text = ""; tvHeroImg.Source = null; return;
        }
        string kick = it == tvFeatured || (tvZone != "rows" && tvPage == "home") ? "Recommended" : row != null ? row.Title.Split(new[] { "  •  " }, StringSplitOptions.None)[0] : "";
        tvHeroKicker.Text = kick.ToUpperInvariant();
        tvHeroTitle.Text = it.Title;
        tvHeroLogo.Source = null; tvHeroLogo.Visibility = Visibility.Collapsed; tvHeroTitle.Visibility = Visibility.Visible;
        if (!string.IsNullOrEmpty(it.Logo)) { var lg = it; TvLoadImage(tvHeroLogo, it.Logo, 700, () => { if (tvFocus == lg) { tvHeroLogo.Visibility = Visibility.Visible; tvHeroTitle.Visibility = Visibility.Collapsed; } }); }
        TvEnrichAsync(it, () => { if (tvFocus == it) ShowTvHero(it); });
        TvMeta(tvHeroMeta, it, null);
        tvHeroPlot.Text = it.Plot ?? "";
        string art = it.Backdrop ?? it.Image;
        bool poster = it.Backdrop == null && !string.IsNullOrEmpty(it.Image) && it.Ext != "lowres" && (it.Kind == "movie" || it.Kind == "series" || it.Kind == "episode" || it.Kind == "file");
        bool logo = poster || it.Kind == "live" || it.Ext == "lowres" || (it.Backdrop == null && (it.Kind == "service" || it.Kind == "account"));
        tvHeroFrame.Opacity = poster ? 0.55 : logo ? 0.42 : 1;
        tvHeroImg.Effect = null;   // a soft picture is blurred once when it comes (TvBlurred), not on every frame
        double blur = !logo ? 0 : poster ? 45 : 70;
        tvHeroPosterBox.Visibility = poster ? Visibility.Visible : Visibility.Collapsed; tvHeroPoster.Source = null;
        ((FrameworkElement)tvHeroKicker.Parent).MaxWidth = poster ? 560 : 740;
        if (poster) { tvHeroPoster.Opacity = 0; TvLoadImage(tvHeroPoster, it.Image, 600, null); }
        tvHeroImg.BeginAnimation(UIElement.OpacityProperty, Anim(0, 120));
        // when the picture is in: soft ones blurred once, the glow takes its colour, a wide one drifts slowly closer
        TvSlowZoom(false);
        var heroFor = it;
        Action came = () => { if (blur > 0) TvBlurInPlace(tvHeroImg, blur); if (tvFocus != heroFor && tvFeatured != heroFor) return; TvGlowFrom(tvHeroImg.Source); TvSlowZoom(blur == 0); };
        if (!string.IsNullOrEmpty(art)) TvLoadImage(tvHeroImg, art, blur > 0 ? 480 : 1600, came);
        else { tvHeroImg.Source = TvArt(it); came(); tvHeroImg.BeginAnimation(UIElement.OpacityProperty, Anim(1, 300, 0)); }
        // the buttons
        if (it.Kind == "add") tvHeroButtons.Add(TvButton("", "Add a login", false, () => TvWizardStart()));
        else if (it.Kind == "account") { tvHeroButtons.Add(TvButton("", "Use", false, () => TvPick(it, row))); tvHeroButtons.Add(TvButton("", "Edit", false, () => TvAccountForm(it.Account))); }
        else if (it.Kind == "category") tvHeroButtons.Add(TvButton("\uE8FD", "Browse " + it.Title, false, () => TvPick(it, row)));
        else if (it.Kind == "collection") tvHeroButtons.Add(TvButton("", "Open the collection", false, () => TvPick(it, row)));
        else if (it.Kind == "service") tvHeroButtons.Add(TvButton("", "Open " + it.Title, false, () => TvPick(it, row)));
        else if (it.Kind == "folder" || it.Kind == "addfolder") tvHeroButtons.Add(TvButton(it.Kind == "folder" ? "" : "", it.Kind == "folder" ? "Stop looking here" : "Add a folder", false, () => TvPick(it, row)));
        else {
            double at = TvStore.ResumeAt(it);
            tvHeroButtons.Add(TvButton("", it.Kind == "series" ? "Episodes" : at > 0 ? "Resume" : it.Kind == "live" ? "Watch" : "Play", false, () => TvPick(it, row)));
            if (it.Kind != "live") tvHeroButtons.Add(TvButton("", "Info", false, () => TvOpenDetail(it)));
            tvHeroButtons.Add(TvButton(TvStore.IsFav(it) ? "" : "", "My list", false, () => { TvStore.ToggleFav(it); ShowTvHero(it); }));
        }
        foreach (var b in tvHeroButtons) tvHeroBtns.Children.Add(b);
        if (tvHeroIdx >= tvHeroButtons.Count) tvHeroIdx = 0;
        if (tvZone == "hero") MarkTv();
        // what's on now, for a provider's channel
        if (it.Kind == "live" && it.Account != null) {
            var ch = it;
            new Thread(() => { var nn = TvSource.NowNext(ch); W.Dispatcher.BeginInvoke(new Action(() => { if (tvFocus == ch && nn != null && nn.Length > 0) tvHeroPlot.Text = "Now  " + nn[0] + (nn.Length > 1 ? "\nNext  " + nn[1] : ""); })); }) { IsBackground = true }.Start();
        }
    }

    // a picture for what has none: its colours, with its name drawn large and faint
    static ImageSource TvArt(TvItem it) {
        string c1 = "#5b2a86", c2 = "#0b1114";
        if (it.Kind == "service" && it.Genre != null) { var cs = it.Genre.Split('|'); c1 = cs[0]; c2 = cs[1]; }
        var dg = new DrawingGroup();
        using (var dc = dg.Open()) {
            var bg = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(c1), (Color)ColorConverter.ConvertFromString(c2), 30); bg.Freeze();
            dc.DrawRectangle(bg, null, new Rect(0, 0, 1600, 900));
            var glow = new RadialGradientBrush(Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)) { Center = new Point(0.7, 0.3), GradientOrigin = new Point(0.7, 0.3), RadiusX = 0.6, RadiusY = 0.8 }; glow.Freeze();
            dc.DrawRectangle(glow, null, new Rect(0, 0, 1600, 900));
            var ft = new FormattedText(it.Title ?? "", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Ui.Semi, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 220, new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1);
            dc.DrawText(ft, new Point(1600 - ft.Width - 80, 560 - ft.Height));
        }
        dg.Freeze(); var img = new DrawingImage(dg); img.Freeze(); return img;
    }

    // ---------- picking something ----------
    void TvPick(TvItem it, TvRow row) {
        if (it == null) return;
        TvPeekHide();
        Sound.Play(Sfx.Select);
        switch (it.Kind) {
            case "service": OpenBrowserView(it.Url); break;
            case "add": TvWizardStart(); break;
            case "account": tvAcc = it.Account; TvApplyProfile(); SetStep("TV: using " + it.Account.Name + ".", Ui.Teal); TvShowPage("iptv-live"); break;
            case "series": TvOpenDetail(it); break;
            case "collection": TvOpenCollection(it); break;   // its films as a row (TvExtras.cs)
            case "category": TvOpenBrowse(it.Group, it.Title); break;
            case "folder": case "addfolder": TvFolder(it); break;
            case "live": TvPlay(it, row != null ? row.Items : null); break;
            // a film, an episode or a video: its page first, with Play (or Resume) ready
            default: TvOpenDetail(it); break;
        }
    }

    // your library's folders: add one (a folder dialog with the mouse, typed with the controller), or stop using one
    void TvFolder(TvItem it) {
        var list = TvSource.LibraryFolders().ToList();
        Action<string> save = v => { Settings.Set("TvFolders", string.Join(";", list)); Settings.Save(); tvLibMovies = null; tvLibSeries = null; TvShowPage("library"); if (v != null) SetStep(v, Ui.Teal); };
        if (it.Kind == "folder") { list.Remove(it.Url); save("TV: your library doesn't look in " + it.Url + " any more."); return; }
        if (!padUsed || MouseMode) {
            using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "A folder with your films and series" })
                if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK && !list.Contains(d.SelectedPath)) { list.Add(d.SelectedPath); save("TV: your library looks in " + d.SelectedPath + " too."); }
            return;
        }
        OpenOsk("A folder with your films and series (for example D:\\Films)", "", null, v => { v = v.Trim(); if (v.Length > 0 && !list.Contains(v)) { list.Add(v); save(Directory.Exists(v) ? "TV: your library looks in " + v + " too." : "TV: " + v + " isn't there (yet)."); } }, null, false, true);
    }

    // ---------- a film's or series' page: its picture fills the screen, the words and buttons over it ----------
    TextBlock tvDetailKicker, tvDetailCaption, tvDetailNext, tvDetailCast; StackPanel tvSeasonBar; Border tvDetailPosterBox;
    readonly List<Border> tvSeasonPills = new List<Border>(); int tvDetailRow, tvSeasonFocus; string tvDetailFrom = "rows";
    void BuildTvDetail() {
        tvDetail = new Grid { Visibility = Visibility.Collapsed, Background = TvTheme.Brush(TvBg) };
        Grid.SetColumnSpan(tvDetail, 2);
        tvDetailImg = new Image { Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(tvDetailImg, BitmapScalingMode.HighQuality);
        tvDetail.Children.Add(new Border { Child = tvDetailImg, ClipToBounds = true });
        tvDetail.Children.Add(new Border { Background = Fade(true) });
        tvDetail.Children.Add(new Border { Background = Fade(false) });
        tvDetail.Children.Add(new Border { Height = 150, VerticalAlignment = VerticalAlignment.Top, Background = TopFade() });
        tvDetailPoster = new Image { Stretch = Stretch.UniformToFill, Width = 250, Height = 375, Clip = new RectangleGeometry(new Rect(0, 0, 250, 375), 14, 14) };
        RenderOptions.SetBitmapScalingMode(tvDetailPoster, BitmapScalingMode.HighQuality);
        tvDetailPosterBox = new Border { Child = tvDetailPoster, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 76, 96, 0), Visibility = Visibility.Collapsed,
            Effect = new DropShadowEffect { BlurRadius = 60, ShadowDepth = 0, Opacity = 0.8, Color = Colors.Black } };
        tvDetail.Children.Add(tvDetailPosterBox);
        var page = new Grid { Margin = new Thickness(64, 30, 56, 28) };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // the mark (back), and the episode that's next
        var top = new Grid();
        var mk = TvMark(34); mk.HorizontalAlignment = HorizontalAlignment.Left; mk.Cursor = Cursors.Hand; mk.ToolTip = "Back";
        mk.MouseLeftButtonUp += (s, e) => TvCloseDetail();
        top.Children.Add(mk);
        tvDetailNext = new TextBlock { FontSize = 15, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#D9FFFFFF"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        top.Children.Add(tvDetailNext);
        page.Children.Add(top);
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom }; Grid.SetRow(body, 2);
        var info = new Grid();
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { MaxWidth = 780, HorizontalAlignment = HorizontalAlignment.Left };
        tvDetailKicker = new TextBlock { FontSize = 13.5, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#D9FFFFFF"), Margin = new Thickness(0, 0, 0, 8) };
        tvDetailTitle = new TextBlock { FontSize = 62, FontFamily = TvFont, FontWeight = FontWeights.Black, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 140,
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black } };
        tvDetailMeta = new TextBlock { Margin = new Thickness(0, 14, 0, 0) };
        tvDetailPlot = new TextBlock { FontSize = 16.5, Foreground = TvTheme.Brush("#EBFFFFFF"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), LineHeight = 23.5, MaxHeight = 94, TextTrimming = TextTrimming.WordEllipsis };
        tvDetailBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 24, 0, 0) };
        tvDetailCaption = new TextBlock { FontSize = 13.5, Foreground = TvTheme.Brush("#99FFFFFF"), Margin = new Thickness(6, 9, 0, 0), MinHeight = 18 };
        tvDetailLogo = new Image { MaxHeight = 140, MaxWidth = 520, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 4) };
        RenderOptions.SetBitmapScalingMode(tvDetailLogo, BitmapScalingMode.HighQuality);
        foreach (var e in new UIElement[] { tvDetailKicker, tvDetailLogo, tvDetailTitle, tvDetailMeta, tvDetailPlot, tvDetailBtns, tvDetailCaption }) left.Children.Add(e);
        info.Children.Add(left);
        tvDetailCast = new TextBlock { FontSize = 15, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxWidth = 340, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(30, 0, 0, 40) };
        Grid.SetColumn(tvDetailCast, 1); info.Children.Add(tvDetailCast);
        body.Children.Add(info);
        tvSeasonBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 0) };
        body.Children.Add(tvSeasonBar);
        tvEpisodes = new StackPanel { Orientation = Orientation.Horizontal };
        tvEpScroll = new ScrollViewer { Content = tvEpisodes, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false,
            Margin = new Thickness(-10, 10, -56, 0), Padding = new Thickness(10, 8, 56, 4) };
        tvEpScroll.PreviewMouseWheel += (s, e) => { tvEpScroll.ScrollToHorizontalOffset(tvEpScroll.HorizontalOffset - e.Delta); e.Handled = true; };
        body.Children.Add(tvEpScroll);
        page.Children.Add(body);
        tvDetail.Children.Add(page);
        tvLayer.Children.Add(tvDetail);
    }
    bool TvDetailOpen { get { return tvDetail != null && tvDetail.Visibility == Visibility.Visible; } }
    void TvOpenDetail(TvItem it) {
        if (!TvDetailOpen) tvDetailFrom = tvZone;
        TvApplyMeta(it);
        TvEnrichAsync(it, () => { if (tvDetailItem == it && TvDetailOpen) TvFillDetail(it); });
        tvDetailItem = it; tvDetail.Visibility = Visibility.Visible; tvDetail.BeginAnimation(UIElement.OpacityProperty, Anim(1, 220, 0));
        TvZoomFromTile(tvDetail);   // it grows out of the picked tile (TvMotion.cs)
        Sound.Play(Sfx.Open);
        tvZone = "detail"; tvDetailIdx = 0; tvDetailRow = 0; tvEpIdx = 0; tvSeason = 0; tvSeasonFocus = 0;
        TvFillDetail(it);
        if ((it.Kind == "movie" && it.Account != null && it.Plot == null) || (it.Kind == "series" && it.Episodes == null)) {
            new Thread(() => { if (it.Kind == "movie") TvSource.MovieInfo(it); else TvSource.SeriesInfo(it); W.Dispatcher.BeginInvoke(new Action(() => { if (tvDetailItem == it && TvDetailOpen) { TvSeasonOfNext(it); TvFillDetail(it); } })); }) { IsBackground = true }.Start();
        } else TvSeasonOfNext(it);
    }
    // a series opens on the season of the episode that's next
    void TvSeasonOfNext(TvItem it) {
        var next = it.Kind == "series" ? TvNextEpisode(it) : null; if (next == null) return;
        var seasons = it.Episodes.Select(e => e.Season).Distinct().OrderBy(x => x).ToList();
        int k = seasons.IndexOf(next.Season); if (k < 0) return;
        tvSeason = tvSeasonFocus = k; tvEpIdx = Math.Max(0, it.Episodes.Where(e => e.Season == next.Season).ToList().IndexOf(next));
        TvFillDetail(it);
    }
    static IEnumerable<string> TvGenres(TvItem it) {
        return (it.Kind == "service" ? "" : it.Genre ?? "").Split(new[] { ',', '/', ';', '|' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0 && x.Length < 24).Take(2);
    }
    static string TvSpaced(string s) { return string.Join(" ", s.ToCharArray()); }
    // MOVIE · ADVENTURE · 2018 · 2h 29m   ★ 8.2
    static void TvMeta(TextBlock tb, TvItem it, string extra) {
        tb.Inlines.Clear(); tb.FontSize = 14.5; tb.FontFamily = Ui.Semi; tb.TextWrapping = TextWrapping.Wrap;
        var parts = new List<string>();
        string kind = it.Kind == "live" ? "Live" : it.Kind == "series" ? "Show" : it.Kind == "episode" ? "Episode" : it.Kind == "service" ? "Free to watch" : it.Kind == "movie" || (it.Kind == "file" && it.Episodes == null) ? "Movie" : null;
        if (kind != null) parts.Add(kind);
        parts.AddRange(TvGenres(it));
        if (!string.IsNullOrEmpty(it.Year)) parts.Add(it.Year);
        if (!string.IsNullOrEmpty(it.Duration)) parts.Add(it.Duration);
        if (!string.IsNullOrEmpty(extra)) parts.Add(extra);
        for (int i = 0; i < parts.Count; i++) {
            if (i > 0) tb.Inlines.Add(new System.Windows.Documents.Run("   ·   ") { Foreground = TvTheme.Brush("#59FFFFFF") });
            tb.Inlines.Add(new System.Windows.Documents.Run(TvSpaced(parts[i].ToUpperInvariant())) { Foreground = TvTheme.Brush("#F2FFFFFF") });
        }
        double r;
        if (double.TryParse(it.Rating ?? "", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r) && r > 0)
            tb.Inlines.Add(new System.Windows.Documents.Run((parts.Count > 0 ? "      " : "") + "★ " + r.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)) { Foreground = TvTheme.Brush("#F5B50A") });
    }
    List<TvItem> TvSeasonEps() {
        var it = tvDetailItem; if (it == null || it.Episodes == null) return new List<TvItem>();
        var seasons = it.Episodes.Select(e => e.Season).Distinct().OrderBy(x => x).ToList(); if (seasons.Count == 0) return new List<TvItem>();
        int s = seasons[Math.Min(tvSeason, seasons.Count - 1)];
        return it.Episodes.Where(e => e.Season == s).ToList();
    }
    void TvFillDetail(TvItem it) {
        bool series = it.Kind == "series";
        var eps = it.Episodes ?? new List<TvItem>();
        var seasons = eps.Select(e => e.Season).Distinct().OrderBy(x => x).ToList();
        tvDetailKicker.Text = string.Join("   ·   ", new[] { series ? "SHOWS" : it.Kind == "live" ? "LIVE" : "MOVIES" }.Concat(TvGenres(it).Select(g => g.ToUpperInvariant())).Select(TvSpaced));
        tvDetailTitle.Text = it.Title;
        tvDetailLogo.Source = null; tvDetailLogo.Visibility = Visibility.Collapsed; tvDetailTitle.Visibility = Visibility.Visible;
        if (!string.IsNullOrEmpty(it.Logo)) { var lg = it; TvLoadImage(tvDetailLogo, it.Logo, 700, () => { if (tvDetailItem == lg) { tvDetailLogo.Visibility = Visibility.Visible; tvDetailTitle.Visibility = Visibility.Collapsed; } }); }
        TvMeta(tvDetailMeta, new TvItem { Year = it.Year, Duration = series ? null : it.Duration, Rating = it.Rating }, seasons.Count > 0 ? seasons.Count + (seasons.Count == 1 ? " Season" : " Seasons") : series && it.Episodes == null ? "Loading..." : null);
        tvDetailPlot.Text = it.Plot ?? "";
        TvDetailPeople(it);
        string art = it.Backdrop ?? it.Image;
        if (tvDetailImg.Tag as string != (art ?? "")) {
            tvDetailImg.Tag = art ?? ""; tvDetailImg.Source = null;
            bool poster = it.Backdrop == null && !string.IsNullOrEmpty(it.Image) && it.Ext != "lowres" && it.Kind != "live";
            bool soft = poster || it.Ext == "lowres" || it.Kind == "live";
            // a soft picture is blurred once when it comes (TvBlurred), not on every frame
            if (!string.IsNullOrEmpty(art)) TvLoadImage(tvDetailImg, art, soft ? 480 : 1600, soft ? (Action)(() => TvBlurInPlace(tvDetailImg, 45)) : null);
            else { tvDetailImg.Source = TvArt(it); if (soft) TvBlurInPlace(tvDetailImg, 45); tvDetailImg.Opacity = 1; }
            tvDetailImg.Effect = null;
            ((Border)tvDetailImg.Parent).Opacity = soft ? 0.5 : 1;
            tvDetailPosterBox.Visibility = poster ? Visibility.Visible : Visibility.Collapsed; tvDetailPoster.Source = null;
            ((FrameworkElement)tvDetailKicker.Parent).MaxWidth = poster ? 560 : 780;
            tvDetailCast.Margin = new Thickness(30, 0, poster ? 300 : 0, 40);
            if (poster) { tvDetailPoster.Opacity = 0; TvLoadImage(tvDetailPoster, it.Image, 500, null); }
        }
        // the buttons: a big Play, then round ones
        tvDetailButtons.Clear(); tvDetailBtns.Children.Clear();
        var next = series ? TvNextEpisode(it) : null;
        tvDetailNext.Text = next != null ? "S" + next.Season + " E" + next.Episode + "    |    " + next.Title : "";
        Border play = null;
        if (series) { if (next != null) { play = TvButton("", (TvStore.ResumeAt(next) > 0 ? "Resume   " : "Play   ") + "S" + next.Season + " E" + next.Episode, true, () => TvPlay(next, eps)); play.ToolTip = string.IsNullOrEmpty(next.Duration) ? null : "Duration " + next.Duration; } }
        else {
            double at = TvStore.ResumeAt(it);
            play = TvButton("", at > 0 ? "Resume   " + TvTime(at) : it.Kind == "live" ? "Watch" : "Play", true, () => TvPlay(it, null));
            play.ToolTip = string.IsNullOrEmpty(it.Duration) ? null : "Duration " + it.Duration;
        }
        if (play != null) tvDetailButtons.Add(play);
        if (!series && TvStore.ResumeAt(it) > 0) { var b = TvButton("", null, false, () => { TvStore.Progress.Remove(it.Key); TvPlay(it, null); }); b.ToolTip = "From the start"; tvDetailButtons.Add(b); }
        bool fav = TvStore.IsFav(it);
        var fb = TvButton(fav ? "" : "", null, false, () => { TvStore.ToggleFav(it); TvFillDetail(it); }); fb.ToolTip = fav ? "In My list (pick to take it out)" : "Add to My list"; tvDetailButtons.Add(fb);
        // the same film or series elsewhere at the provider: the original, the dubs (TvVersions.cs)
        { var vers = TvVersionsOf(it, () => { if (tvDetailItem == it) TvFillDetail(it); });
          if (vers != null && vers.Count > 0) { var vb = TvButton("", "Versions  " + (vers.Count + 1), false, () => TvOpenVersions(it)); vb.ToolTip = "Other copies: the original and dubs"; tvDetailButtons.Add(vb); } }
        var bb = TvButton("", null, false, TvCloseDetail); bb.ToolTip = "Back"; tvDetailButtons.Add(bb);
        { var mb = TvButton("\uE712", null, false, () => TvItemActions(it)); mb.ToolTip = "More: tags, watched, My list order"; tvDetailButtons.Insert(tvDetailButtons.Count - 1, mb); }
        foreach (var b in tvDetailButtons) tvDetailBtns.Children.Add(b);
        if (tvDetailIdx >= tvDetailButtons.Count) tvDetailIdx = 0;
        // the seasons, as pills
        tvSeasonPills.Clear(); tvSeasonBar.Children.Clear();
        tvSeasonBar.Visibility = series ? Visibility.Visible : Visibility.Collapsed;
        if (series && it.Episodes == null) tvSeasonBar.Children.Add(new TextBlock { Text = "Loading the episodes...", FontSize = 15, Foreground = TvTheme.Brush("#99FFFFFF") });
        else if (series && seasons.Count == 0) tvSeasonBar.Children.Add(new TextBlock { Text = "No episodes found.", FontSize = 15, Foreground = TvTheme.Brush("#99FFFFFF") });
        for (int k = 0; k < seasons.Count; k++) {
            int kk = k;
            var pill = new Border { Child = new TextBlock { Text = "Season " + seasons[k], FontSize = 15.5, FontFamily = Ui.Semi }, Padding = new Thickness(20, 8, 20, 9), CornerRadius = new CornerRadius(19), Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand };
            pill.MouseLeftButtonUp += (s, e) => { tvSeason = tvSeasonFocus = kk; tvEpIdx = 0; Sound.Play(Sfx.Select); TvFillDetail(it); };
            tvSeasonPills.Add(pill); tvSeasonBar.Children.Add(pill);
        }
        // the episodes of the picked season, as cards
        tvEpisodeRows.Clear(); tvEpisodes.Children.Clear();
        if (series) {
            var list = TvSeasonEps();
            tvEpScroll.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var e in list) TvEpisodeCard(e, it, eps);
        } else {
            tvDetailMore = it.Kind == "movie" ? TvSimilar(it) : new List<TvItem>();
            if (tvDetailMore.Count > 0) { tvSeasonBar.Visibility = Visibility.Visible; tvSeasonBar.Children.Add(new TextBlock { Text = "More like this", FontSize = 19, FontFamily = TvFont, FontWeight = FontWeights.Bold, Foreground = Brushes.White }); }
            tvEpScroll.Visibility = tvDetailMore.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            for (int i = 0; i < tvDetailMore.Count; i++) TvMoreCard(tvDetailMore[i], i);
        }
        if (tvEpIdx >= tvEpisodeRows.Count) tvEpIdx = Math.Max(0, tvEpisodeRows.Count - 1);
        if (tvDetailRow == 2 && tvEpisodeRows.Count == 0) tvDetailRow = 0;
        MarkTvDetail();
    }
    void TvEpisodeCard(TvItem e, TvItem series, List<TvItem> eps) {
        double w = 272, h = 153;
        var pic = new Grid { Width = w, Height = h };
        int hsh = Math.Abs((series.Title ?? "").GetHashCode()); string[] pal = { "#5b2a86", "#1b4f72", "#7b241c", "#145a32", "#784212", "#4a235a" };
        pic.Children.Add(new Border { Background = TvTheme.Gradient(pal[hsh % pal.Length], "#0b1114") });
        pic.Children.Add(new TextBlock { Text = "E" + e.Episode, FontSize = 54, FontFamily = TvFont, FontWeight = FontWeights.Black, Foreground = TvTheme.Brush("#26FFFFFF"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        string art = e.Image ?? series.Backdrop;
        if (!string.IsNullOrEmpty(art)) {
            var img = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 }; RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            TvLoadImage(img, art, 480, null); pic.Children.Add(img);
        }
        var shade = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0xB3, 0, 0, 0), 90); shade.Freeze();
        pic.Children.Add(new Border { Height = 64, VerticalAlignment = VerticalAlignment.Bottom, Background = shade });
        pic.Children.Add(new Border { Background = TvTheme.Brush("#D9101012"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 4), Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Child = new TextBlock { Text = "S" + e.Season + " E" + e.Episode, FontSize = 12.5, FontFamily = Ui.Semi, Foreground = Brushes.White } });
        double at = TvStore.ResumeAt(e);
        bool seen = TvMine.IsWatched(e);
        if (!string.IsNullOrEmpty(e.Duration) || at > 0 || seen)
            pic.Children.Add(new TextBlock { Text = seen ? "✓  Watched" : at > 0 ? TvTime(at) + " in" : e.Duration, FontSize = 13, FontFamily = Ui.Semi, Foreground = Brushes.White, Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom });
        Dictionary<string, object> p;
        if (TvStore.Progress.TryGetValue(e.Key, out p) && Json.Num(p, "Dur") > 0) {
            double frac = Math.Min(1, Json.Num(p, "Pos") / Json.Num(p, "Dur"));
            pic.Children.Add(new Border { Height = 4, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Width = w * frac, Background = TvTheme.Brush(TvAccent) });
        }
        pic.Clip = new RectangleGeometry(new Rect(0, 0, w, h), 10, 10);
        var frame = new Border { Child = pic, CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(3), BorderBrush = Brushes.Transparent, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
        var card = new StackPanel { Width = w + 6, Margin = new Thickness(0, 0, 20, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        card.Children.Add(frame);
        card.Children.Add(new TextBlock { Text = e.Title, FontSize = 16, FontFamily = Ui.Semi, Foreground = Brushes.White, Margin = new Thickness(3, 10, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrEmpty(e.Plot)) card.Children.Add(new TextBlock { Text = e.Plot, FontSize = 13, Foreground = TvTheme.Brush("#8CFFFFFF"), Margin = new Thickness(3, 4, 0, 0), TextWrapping = TextWrapping.Wrap, MaxHeight = 36, TextTrimming = TextTrimming.WordEllipsis });
        int idx = tvEpisodeRows.Count; var ep = e;
        card.MouseLeftButtonUp += (s, ev) => { tvEpIdx = idx; TvPlay(ep, eps); };
        card.MouseRightButtonUp += (s, ev) => { ev.Handled = true; TvItemActions(ep); };
        TvOnHover(card, () => { if (tvEpIdx != idx || tvDetailRow != 2) { tvEpIdx = idx; tvDetailRow = 2; MarkTvDetail(); } });
        tvEpisodeRows.Add(frame); tvEpisodes.Children.Add(card);
    }
    TvItem TvNextEpisode(TvItem series) {
        var eps = series.Episodes; if (eps == null || eps.Count == 0) return null;
        // the last one watched: carry on there (or the one after it, once it's finished)
        TvItem last = null; string when = "";
        foreach (var e in eps) { Dictionary<string, object> p; if (TvStore.Progress.TryGetValue(e.Key, out p) && string.CompareOrdinal(Json.Str(p, "When"), when) > 0) { last = e; when = Json.Str(p, "When"); } }
        if (last == null) return eps.FirstOrDefault(e => !TvMine.IsWatched(e)) ?? eps[0];
        if (TvStore.ResumeAt(last) > 0 && !TvMine.IsWatched(last)) return last;
        int i = eps.IndexOf(last);
        return eps.Skip(i + 1).FirstOrDefault(e => !TvMine.IsWatched(e)) ?? (i + 1 < eps.Count ? eps[i + 1] : last);
    }
    void MarkTvDetail() {
        for (int i = 0; i < tvDetailButtons.Count; i++) TvMarkButton(tvDetailButtons[i], tvDetailRow == 0 && i == tvDetailIdx);
        var cb = tvDetailRow == 0 && tvDetailIdx < tvDetailButtons.Count ? tvDetailButtons[tvDetailIdx] : tvDetailButtons.FirstOrDefault();
        tvDetailCaption.Text = cb != null ? cb.ToolTip as string ?? "" : "";
        for (int i = 0; i < tvSeasonPills.Count; i++) {
            bool on = tvDetailRow == 1 && i == tvSeasonFocus, cur = i == tvSeason;
            tvSeasonPills[i].Background = on ? Brushes.White : TvTheme.Brush(cur ? "#47FFFFFF" : "#1AFFFFFF");
            ((TextBlock)tvSeasonPills[i].Child).Foreground = on ? Brushes.Black : cur ? Brushes.White : TvTheme.Brush("#B3FFFFFF");
        }
        for (int i = 0; i < tvEpisodeRows.Count; i++) {
            bool on = tvDetailRow == 2 && i == tvEpIdx;
            var f = tvEpisodeRows[i]; f.BorderBrush = on ? Brushes.White : Brushes.Transparent;
            var st = (ScaleTransform)f.RenderTransform; double k = on ? 1.04 : 1;
            if (Math.Abs(st.ScaleX - k) > 0.001) { st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(k, 140)); st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(k, 140)); }
            if (on) f.BringIntoView();
        }
        // lower down (seasons, episodes) the story and buttons make room, like turning the page
        bool series = tvDetailItem != null && (tvDetailItem.Kind == "series" || tvEpisodeRows.Count > 0), compact = tvDetailRow >= 1 && !MouseMode;
        var show = compact ? Visibility.Collapsed : Visibility.Visible;
        tvDetailPlot.Visibility = show; tvDetailBtns.Visibility = show; tvDetailCaption.Visibility = show; tvDetailCast.Visibility = show;
        tvDetailTitle.FontSize = compact ? 40 : series ? 50 : 62; tvDetailTitle.MaxHeight = compact || series ? 72 : 140;
        tvDetailPlot.MaxHeight = series ? 47 : 94;
        SetHints();
    }
    void TvCloseDetail() { tvDetail.Visibility = Visibility.Collapsed; tvDetailItem = null; Sound.Play(Sfx.Back); tvZone = tvDetailFrom == "hero" || tvDetailFrom == "live" ? tvDetailFrom : "rows"; if (tvZone == "live") TvLiveMark(); else MarkTv(); }
    static string TvTime(double s) { var t = TimeSpan.FromSeconds(s); return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss"); }

    // ---------- IPTV logins: add, change, remove ----------
    ListPage tvAccPage; TvAccount tvAccEdit, tvAccOrig; bool tvAccNew;
    void TvAccountForm(TvAccount a) {
        tvAccNew = a == null; tvAccFree = false; tvAccPicking = false; tvAccOrig = a;
        tvAccEdit = a != null ? new TvAccount { Id = a.Id, Name = a.Name, Type = a.Type, Server = a.Server, User = a.User, Pass = a.Pass, Url = a.Url, Avatar = a.Avatar, Formats = a.Formats }
                              : new TvAccount { Id = "acc" + DateTime.Now.Ticks, Name = "My IPTV", Type = "xtream", Avatar = "a" + ((TvStore.Accounts.Count + 1) % TvAvatars.Length) };
        TvOpenAccountPage();
    }
    void TvOpenAccountPage() { TvOpenProfilePage(); }
    void TvOpenAccountCard() {
        if (tvAccPage == null) {
            tvAccPage = MakeListPage("TvAccount", "MY IPTV", "Your IPTV login", 640, null);
            tvAccPage.Layer.SetValue(Panel.ZIndexProperty, 900);
            tvAccPage.Fill = TvFillAccountForm;
        }
        OpenListPage(tvAccPage);
    }
    void TvFillAccountForm() {
        if (TvProfPageOpen) { TvFillProfilePage(); return; }
        if (tvAccPicking) { TvFillAvatarPicker(); return; }
        var a = tvAccEdit; var p = tvAccPage;
        p.Title.Text = tvAccNew ? "Add a profile" : "Change " + a.Name;
        p.Sub.Text = "From your provider: an Xtream login (server address, username and password) or a playlist link. It's kept on this PC only, encrypted with your Windows account, and only goes to your provider's server." + (a.Status != null ? "\n\n" + a.Status : "");
        Func<string, string, string, bool, ListRow> field = (title, value, ask, secret) => new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = title,
            Text = string.IsNullOrEmpty(value) ? "(empty: pick to type it)" : secret ? new string('•', Math.Min(12, value.Length)) : value };
        var rows = new List<ListRow>();
        var name = field("Name", a.Name, null, false); name.Do = () => OpenOsk("A name for this login", a.Name, null, v => { a.Name = v.Trim().Length > 0 ? v.Trim() : a.Name; TvFillAccountForm(); }, null); rows.Add(name);
        rows.Add(TvPictureRow());
        rows.Add(TvStartRow()); rows.Add(TvThemeRow());
        if (!tvAccFree && !tvAccNew) rows.Add(TvLangRow(tvAccEdit));
        rows.AddRange(TvGuideRows());
        rows.AddRange(TvPlayerRows());
        rows.AddRange(TvSafeRows());
        if (tvAccFree) {
            p.Sub.Text = "The free profile: free films and series (public domain, from the Internet Archive), and free channels from the Netherlands and around the world. It's always here.";
            rows.Add(new ListRow { Glyph = "\uE73E", C1 = "#3fb8a0", C2 = "#2f8fe0", Title = "Save", Do = TvSaveFree });
            FillRows(p, rows, ""); return;
        }
        rows.Add(new ListRow { Glyph = "", C1 = "#2f8fe0", C2 = "#2f8fe0", Title = "Kind: " + (a.Type == "m3u" ? "Playlist link (M3U)" : "Xtream login"), Text = "Pick to switch",
            Do = () => { a.Type = a.Type == "m3u" ? "xtream" : "m3u"; TvFillAccountForm(); } });
        if (a.Type == "m3u") {
            var url = field("Playlist link", a.Url, null, true); url.Do = () => OpenOsk("The playlist (M3U) link from your provider", a.Url, null, v => { a.Url = v.Trim(); TvFillAccountForm(); }, null, false, true); rows.Add(url);
        } else {
            var srv = field("Server address", a.Server, null, false); srv.Do = () => OpenOsk("Server address (for example http://example.com:8080)", a.Server, null, v => { v = v.Trim(); if (v.Length > 0 && !v.Contains("://")) v = "http://" + v; a.Server = v; TvFillAccountForm(); }, null, false, true); rows.Add(srv);
            var usr = field("Username", a.User, null, false); usr.Do = () => OpenOsk("Username", a.User, null, v => { a.User = v.Trim(); TvFillAccountForm(); }, null, false, true); rows.Add(usr);
            var pwd = field("Password", a.Pass, null, true); pwd.Do = () => OpenOsk("Password", a.Pass, null, v => { a.Pass = v; TvFillAccountForm(); }, null, true); rows.Add(pwd);
        }
        rows.Add(new ListRow { Glyph = "", C1 = "#3fb8a0", C2 = "#2f8fe0", Title = "Save and sign in", Text = "Checks the login with your provider", Do = TvSaveAccount });
        if (!tvAccNew) rows.Add(new ListRow { Glyph = "", C1 = "#c0453c", C2 = "#c0453c", Title = "Remove this login", Do = () => {
            TvStore.Accounts.RemoveAll(x => x.Id == a.Id); TvStore.Save(); if (tvAcc != null && tvAcc.Id == a.Id) tvAcc = TvStore.Accounts.FirstOrDefault();
            CloseListPage(); TvAfterProfileChange(); } });
        FillRows(p, rows, "");
    }
    void TvSaveAccount() {
        var a = tvAccEdit;
        if (tvAccFree) { TvSaveFree(); return; }
        bool ready = a.Type == "m3u" ? !string.IsNullOrEmpty(a.Url) : !string.IsNullOrEmpty(a.Server) && !string.IsNullOrEmpty(a.User) && !string.IsNullOrEmpty(a.Pass);
        if (!ready) { a.Status = "Fill in " + (a.Type == "m3u" ? "the playlist link" : "the server, username and password") + " first."; TvFillAccountForm(); return; }
        // only the name or the picture changed: no need to sign in again
        var o = tvAccOrig;
        if (o != null && o.Type == a.Type && o.Server == a.Server && o.User == a.User && o.Pass == a.Pass && o.Url == a.Url) {
            o.Name = a.Name; o.Avatar = a.Avatar; TvStore.Save();
            if (TvProfPageOpen) { a.Status = "✓  Nothing in the login changed."; TvFillProfilePage(); return; }
            if (ListPageOpen && lpOpen == tvAccPage) CloseListPage();
            TvAfterProfileChange(); return;
        }
        a.Status = "Signing in..."; TvFillAccountForm();
        new Thread(() => {
            string msg; bool ok = TvSource.SignIn(a, out msg);
            W.Dispatcher.BeginInvoke(new Action(() => {
                a.Status = (ok ? "✓  " : "✗  ") + msg;
                if (!ok) { if (TvProfPageOpen || (ListPageOpen && lpOpen == tvAccPage)) TvFillAccountForm(); return; }
                int at = TvStore.Accounts.FindIndex(x => x.Id == a.Id);
                if (at >= 0) TvStore.Accounts[at] = a; else TvStore.Accounts.Add(a);
                TvStore.Save(); if (!TvProfilesOpen) tvAcc = a;
                tvAccOrig = a; if (TvProfPageOpen) { TvFillProfilePage(); return; }
                if (ListPageOpen && lpOpen == tvAccPage) CloseListPage();
                SetStep("TV: signed in to " + a.Name + ". " + msg, Ui.Teal);
                if (TvProfilesOpen) FillTvProfiles(); else { TvApplyProfile(); TvShowPage("iptv-live"); }
            }));
        }) { IsBackground = true }.Start();
    }

    // ---------- the player: mpv, inside the window (Mpv.cs) ----------
    System.Windows.Forms.Integration.WindowsFormsHost tvVideoHost; System.Windows.Forms.Panel tvVideo; IntPtr tvMpv;
    TextBlock tvPlayTitle, tvPlayTime; DispatcherTimer tvPlayTimer; bool tvStarted; DateTime tvLoadedAt, tvSavedAt;
    static string Vlc() {
        foreach (var pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }) {
            string f = Path.Combine(pf ?? "", @"VideoLAN\VLC\vlc.exe"); if (File.Exists(f)) return f;
        }
        return null;
    }
    void TvPlay(TvItem it, List<TvItem> list) {
        TvChannelChanging(it);   // the channel before, for "last channel" (TvExtras.cs)
        if (it == null || string.IsNullOrEmpty(it.Url)) return;
        TvZapCancel(); TvPeekHide();   // a channel picked another way wins over one still being zapped to
        if (TvGuideOpen && tvGuideHost != null) { TvGuideStopPreview(); tvGuideHost.Visibility = Visibility.Collapsed; }
        string real;
        if (it.Url.StartsWith("ia:") && TvDownloaded(it) == null && !tvResolved.TryGetValue(it.Url, out real)) {
            SetStep("TV: getting " + it.Title + " from the Internet Archive...", Ui.Teal);
            new Thread(() => {
                string u = null, err = null; try { u = TvFree.Resolve(it.Url); } catch (Exception e) { err = e.Message; }
                W.Dispatcher.BeginInvoke(new Action(() => {
                    if (err != null) { SetStep("TV: " + it.Title + " couldn't be played: " + err, Ui.Red); if (TvGuideOpen) TvGuideBack(); return; }
                    tvResolved[it.Url] = u; SetStep("", Ui.Teal); TvPlay(it, list);
                }));
            }) { IsBackground = true }.Start();
            return;
        }
        // VLC instead, when that's chosen for this kind of thing
        if (TvUseVlc(it) && TvVlc(it)) return;
        // the player is fetched the first time (Mpv.cs)
        if (!Mpv.Ready) {
            SetStep("TV: getting the video player (about 30 MB, once)...", Ui.Teal);
            new Thread(() => {
                string err = null;
                try { Mpv.Download(t => W.Dispatcher.BeginInvoke(new Action(() => SetStep("TV: " + t, Ui.Teal)))); } catch (Exception e) { err = e.Message; }
                W.Dispatcher.BeginInvoke(new Action(() => {
                    if (err != null) { if (!TvVlc(it)) SetStep("TV: the video player couldn't be fetched: " + err, Ui.Red); return; }
                    SetStep("TV: the video player is ready.", Ui.Teal); TvPlay(it, list);
                }));
            }) { IsBackground = true }.Start();
            return;
        }
        tvPlaying = it; tvPlayList = list;
        if (it.Kind == "live") TvMine.AddLastLive(it, tvAcc);
        tvPlayer.Visibility = Visibility.Visible;
        TvEnterWatching();
        tvZone = "player";
        Sound.Play(Sfx.Launch);
        tvPlayTitle.Text = it.Title;
        tvPlayTime.Text = "Loading...";
        TvToolsFor(it);
        W.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => TvLoad(it)));
        SetHints();
    }
    System.Windows.Forms.Panel TvNewVideoPanel() {
        var v = new System.Windows.Forms.Panel { BackColor = System.Drawing.Color.Black, Dock = System.Windows.Forms.DockStyle.Fill };
        v.MouseMove += (s, e) => { if (e.Location != tvLastMouse) { tvLastMouse = e.Location; TvActivity(); TvWake(); } };
        v.MouseClick += (s, e) => { if (TvControlsUp) TvPause(); TvWake(); };
        return v;
    }
    // A player mpv got stuck in (Mpv.Stuck) is left behind, hidden and never closed (closing it would wait on it
    // forever), and a fresh player on a fresh picture takes its place.
    readonly List<System.Windows.Forms.Control> tvStuckPanels = new List<System.Windows.Forms.Control>();
    void TvFreshPlayer() {
        var old = tvMpv; tvMpv = IntPtr.Zero; Mpv.Destroy(old);
        tvVideo.Visible = false; tvStuckPanels.Add(tvVideo);
        tvVideo = TvNewVideoPanel(); tvVideoHost.Child = tvVideo;
        Mpv.Note("the main player got stuck and was replaced");
    }
    void TvLoad(TvItem it) {
        try {
            if (tvMpv != IntPtr.Zero && Mpv.Stuck(tvMpv, 3)) TvFreshPlayer();
            if (tvMpv == IntPtr.Zero) tvMpv = Mpv.Create(tvVideo.Handle);
            TvApplyPlayerPrefs(tvMpv); tvSkipped = false;
            TvOriginalAudio(tvMpv, it);   // the film's own language first, when the file has it (TvVersions.cs)
            TvSubsBeforeLoad(tvMpv, it);  // its kept Dutch / English subtitles and their timing (TvSubs.cs)
            TvSubStyleApply(tvMpv); TvAudioFxApply(tvMpv);   // subtitle style, night mode, louder (TvExtras.cs)
            if (it.Kind == "live") Mpv.Set(tvMpv, "sid", "no");   // TV channels: no subtitles unless picked
            double at = TvStore.ResumeAt(it);
            Mpv.Set(tvMpv, "start", at > 0 ? ((int)at).ToString() : "none");
            Mpv.Command(tvMpv, "loadfile", TvUrl(it), "replace");
            Mpv.Set(tvMpv, "pause", "no");
            Mpv.Command(tvMpv, "show-text", it.Title, "2500");
            tvStarted = false; tvLoadedAt = DateTime.Now; tvSavedAt = DateTime.Now;
            tvPlayTimer.Start();
        } catch (Exception e) { TvStop(); if (!TvVlc(it)) SetStep("TV: the player didn't start: " + e.Message, Ui.Red); }
    }
    // a little after loading, twice a second: the time, saving where you are, the end, or that it can't be played
    void TvTick() {
        if (tvMpv == IntPtr.Zero || tvPlaying == null) { tvPlayTimer.Stop(); return; }
        if (Mpv.Stuck(tvMpv)) { TvFreshPlayer(); TvLoad(tvPlaying); return; }
        double pos = Mpv.Num(tvMpv, "time-pos"), dur = Mpv.Num(tvMpv, "duration");
        bool idle = Mpv.Get(tvMpv, "idle-active") == "yes", paused = Mpv.Get(tvMpv, "pause") == "yes";
        TvToolsTick(pos, dur, paused);
        TvSkipTick(pos);
        TvNextTick(pos, dur);
        TvPauseTick(paused);   // paused a while: the pause screen (TvPauseScreen.cs)
        if (pos > 0.5 && !tvStarted) { tvStarted = true; TvStartedNow(); TvAutoSubs(tvPlaying); TvChaptersLoad(); }
        TvRecapTick(pos); TvNowNextTick();   // "Skip recap"; live: now and next again every minute (TvExtras.cs)   // no Dutch / English subtitles in it: fetched (TvSubs.cs)
        bool live = tvPlaying.Kind == "live";
        tvPlayTime.Text = !tvStarted ? (idle && (DateTime.Now - tvLoadedAt).TotalSeconds > 2 ? "" : "Loading...") : paused ? "Paused" : live || dur <= 0 ? "● LIVE" : "";
        if (tvStarted && !live && dur > 0 && (DateTime.Now - tvSavedAt).TotalSeconds > 5) { TvStore.SetProgress(tvPlaying, pos, dur); tvSavedAt = DateTime.Now; }
        if (idle && (DateTime.Now - tvLoadedAt).TotalSeconds > 1.5) {
            var it = tvPlaying;
            if (tvStarted) {
                // the end: a series carries on with the next episode
                TvMine.SetWatched(it, true);
                // (not after Cancel on the next-episode card; the third in a row asks "still watching?" first)
                if (!tvNextCancelled && TvNextUp() != null) TvAutoNextOk();
                if (!tvNextCancelled && TvQueueNext()) return;
                if (!tvNextCancelled && (it.Kind == "episode" || (it.Kind == "file" && it.Season > 0))) { var next = TvStep(1); if (next != null) { TvPlay(next, tvPlayList); return; } }
                TvStop();
            } else {
                TvStop();
                TvExplainFailure(it);
            }
        }
    }
    void TvPause() {
        if (tvMpv == IntPtr.Zero) return;
        bool paused = Mpv.Get(tvMpv, "pause") != "yes";   // what it will be (the player works on its own thread)
        Mpv.Set(tvMpv, "pause", paused ? "yes" : "no");
        Mpv.Command(tvMpv, "show-text", paused ? "⏸  Paused" : "▶  Playing", "1500");
        Sound.Play(paused ? Sfx.ToggleOff : Sfx.ToggleOn);
    }
    void TvSeek(int s) { if (tvMpv != IntPtr.Zero && tvPlaying != null && tvPlaying.Kind != "live") { Mpv.Command(tvMpv, "seek", s.ToString(), "relative"); TvSeekInfo(); } }   // where you are, and the chapter
    bool TvVlc(TvItem it) {
        string vlc = Vlc(); if (vlc == null) return false;
        double at = TvStore.ResumeAt(it);
        try {
            Process.Start(new ProcessStartInfo(vlc, "--fullscreen --play-and-exit " + (at > 0 ? "--start-time=" + (int)at + " " : "") + Engine.Q(TvUrl(it))) { UseShellExecute = false });
            SetStep("TV: " + it.Title + " plays in VLC.", Ui.Teal);
            return true;
        } catch { return false; }
    }
    TvItem TvStep(int d) {
        if (tvPlayList == null || tvPlaying == null) return null;
        int i = tvPlayList.FindIndex(x => x.Key == tvPlaying.Key); if (i < 0) return null;
        int j = i + d; if (j < 0 || j >= tvPlayList.Count) return null;
        return tvPlayList[j];
    }
    void TvStop() {
        TvZapCancel();
        TvLeaveWatching();
        tvPlayTimer.Stop();
        if (tvMpv != IntPtr.Zero && tvPlaying != null && tvStarted && tvPlaying.Kind != "live") { double pos = Mpv.Num(tvMpv, "time-pos"), dur = Mpv.Num(tvMpv, "duration"); if (pos > 0 && dur > 0) TvStore.SetProgress(tvPlaying, pos, dur); if (pos > 0 && dur > 0 && pos > dur * 0.95) TvMine.SetWatched(tvPlaying, true); }
        TvStore.Save();
        if (tvMpv != IntPtr.Zero) { Mpv.Command(tvMpv, "stop"); }
        tvPlayer.Visibility = Visibility.Collapsed; tvPlaying = null;
        Sound.Play(Sfx.Back);
        tvZone = TvDetailOpen ? "detail" : TvGuideOpen ? "guide" : TvLiveOpen ? "live" : "rows";
        if (TvGuideOpen && !TvDetailOpen) TvGuideBack();
        if (TvDetailOpen && tvDetailItem != null) TvFillDetail(tvDetailItem);
        SetHints();
    }

    // ---------- the controller and the keyboard ----------
    void TvPad(int bit) {
        if (TvInput()) return;   // the screensaver was up: this press only brings the page back
        if (TvPeekPad(bit)) return;   // in (or into) a tile's card (TvPeek.cs)
        tvMouseNav = false; TvPeekHide();
        // the View button (two squares): full screen and back, anywhere but the player (watching is full screen already)
        if (bit == Pad.Back && !TvPlaying && !TvMultiOpen) { TvToggleFull(); return; }
        if (TvProfPageOpen) { TvProfilePagePad(bit); return; }
        if (TvProfilesOpen) { TvProfilesPad(bit); return; }
        if (TvMultiOpen) { TvMultiPad(bit); return; }
        if (TvPairOpen) { if (bit == Pad.B) TvPairCancel(); return; }
        if (TvPlaying) { TvPlayPad(bit); return; }
        if (TvDetailOpen) { TvDetailPad(bit); return; }
        if (TvSearchOpen) { TvSearchPad(bit); return; }   // the search page (TvSearch.cs)
        if (bit == Pad.LB || bit == Pad.RB) {
            var ids = Enumerable.Range(0, tvRailIds.Count).Where(i => TvRailDef[i][1] == "" && TvRailShown(i)).Select(i => tvRailIds[i]).ToList();
            int at = Math.Max(0, ids.IndexOf(tvPage)), to = (at + (bit == Pad.RB ? 1 : -1) + ids.Count) % ids.Count;
            Sound.Play(Sfx.Move); TvShowPage(ids[to]); return;
        }
        if (tvZone == "guide") { TvGuidePad(bit); return; }
        if (tvZone == "live") { TvLivePad(bit); return; }
        // the bar at the top
        if (tvZone == "rail") {
            if (bit == Pad.Left || bit == Pad.Right) {
                int was = tvRailIdx, k = tvRailIdx;
                do { k += bit == Pad.Left ? -1 : 1; } while (k >= 0 && k < tvRailItems.Count && !TvRailShown(k));
                if (k >= 0 && k < tvRailItems.Count) tvRailIdx = k;
                Sound.Play(tvRailIdx != was ? Sfx.Move : Sfx.Bump); MarkTvRail();
            } else if (bit == Pad.A) TvRailPick();
            else if (bit == Pad.Down) {
                if (TvGuideOpen) { tvZone = "guide"; Sound.Play(Sfx.Move); MarkTvRail(); TvGuideMark(); }
                else if (TvLiveOpen) { tvZone = "live"; Sound.Play(Sfx.Move); MarkTvRail(); TvLiveMark(); }
                else if (tvHeroButtons.Count > 0) { tvZone = "hero"; tvHeroIdx = 0; Sound.Play(Sfx.Move); MarkTv(); }
                else if (tvRowList.Count > 0) TvFocusTile(0, tvRowList[0].Col, true);
                else Sound.Play(Sfx.Bump);
            }
            else if (bit == Pad.B) CloseTv();
            else Sound.Play(Sfx.Bump);
            return;
        }
        // the preview's buttons
        if (tvZone == "hero") {
            if (bit == Pad.Left) { if (tvHeroIdx > 0) { tvHeroIdx--; Sound.Play(Sfx.Move); MarkTv(); } else Sound.Play(Sfx.Bump); }
            else if (bit == Pad.Right) { if (tvHeroIdx < tvHeroButtons.Count - 1) { tvHeroIdx++; Sound.Play(Sfx.Move); MarkTv(); } else Sound.Play(Sfx.Bump); }
            else if (bit == Pad.Down) { if (tvRowList.Count > 0) TvFocusTile(0, tvRowList[0].Col, true); else Sound.Play(Sfx.Bump); }
            else if (bit == Pad.Up || bit == Pad.B) TvToRail();
            else if (bit == Pad.A && tvHeroIdx < tvHeroButtons.Count) ((Action)tvHeroButtons[tvHeroIdx].DataContext)();
            return;
        }
        // the rows
        if (tvRowList.Count == 0) { TvToRail(); return; }
        var r = tvRowList[tvRowIdx];
        if (bit == Pad.Left) { if (r.Col > 0) TvFocusTile(tvRowIdx, r.Col - 1, true); else Sound.Play(Sfx.Bump); }
        else if (bit == Pad.Right) TvFocusTile(tvRowIdx, r.Col + 1, true);
        else if (bit == Pad.Down) { if (tvRowIdx < tvRowList.Count - 1) TvFocusTile(tvRowIdx + 1, tvRowList[tvRowIdx + 1].Col, true); else Sound.Play(Sfx.Bump); }
        else if (bit == Pad.Up) {
            if (tvRowIdx > 0) TvFocusTile(tvRowIdx - 1, tvRowList[tvRowIdx - 1].Col, true);
            else if (tvHeroButtons.Count > 0) { tvZone = "hero"; tvHeroIdx = 0; Sound.Play(Sfx.Move); ShowTvHero(tvFocus); MarkTv(); }
            else TvToRail();
        }
        else if (bit == Pad.A) TvPick(r.Items[r.Col], r);
        else if (bit == Pad.X) TvItemActions(r.Items[r.Col]);
        else if (bit == Pad.Y) { var it = r.Items[r.Col]; if (it.Kind != "add" && it.Kind != "account") { TvStore.ToggleFav(it); Sound.Play(Sfx.Select); SetStep(TvStore.IsFav(it) ? it.Title + " is in My list." : it.Title + " is out of My list.", Ui.Teal); ShowTvHero(it); } }
        else if (bit == Pad.B) { if (tvPage == "collection") TvShowPage(tvCollectionFrom ?? "home"); else TvToRail(); }   // a collection: back to where it was opened
    }
    void TvToRail() {
        tvZone = "rail"; int ri = tvRailIds.IndexOf(tvPage); if (ri >= 0) tvRailIdx = ri;
        Sound.Play(Sfx.Move); MarkTv();
        if (TvGuideOpen) TvGuideMark();
        if (TvLiveOpen) TvLiveMark();
    }
    void TvRailPick() {
        string id = tvRailIds[tvRailIdx];
        Sound.Play(Sfx.Select);
        TvShowPage(id);
    }
    void TvDetailPad(int bit) {
        if (bit == Pad.B) { TvCloseDetail(); return; }
        if (bit == Pad.Up || bit == Pad.Down) {
            int want = tvDetailRow + (bit == Pad.Down ? 1 : -1);
            if (want == 1 && tvSeasonPills.Count == 0) want += bit == Pad.Down ? 1 : -1;
            if (want == 2 && tvEpisodeRows.Count == 0) want = -1;
            if (want < 0 || want > 2) { Sound.Play(Sfx.Bump); return; }
            tvDetailRow = want; if (want == 1) tvSeasonFocus = tvSeason;
            Sound.Play(Sfx.Move); MarkTvDetail(); return;
        }
        int dx = bit == Pad.Left ? -1 : bit == Pad.Right ? 1 : 0;
        if (tvDetailRow == 0) {
            if (dx != 0) { int n = Math.Max(0, Math.Min(tvDetailButtons.Count - 1, tvDetailIdx + dx)); Sound.Play(n != tvDetailIdx ? Sfx.Move : Sfx.Bump); tvDetailIdx = n; }
            else if (bit == Pad.A && tvDetailIdx < tvDetailButtons.Count) { Sound.Play(Sfx.Select); ((Action)tvDetailButtons[tvDetailIdx].DataContext)(); return; }
        } else if (tvDetailRow == 1) {
            if (dx != 0) { int n = Math.Max(0, Math.Min(tvSeasonPills.Count - 1, tvSeasonFocus + dx)); Sound.Play(n != tvSeasonFocus ? Sfx.Move : Sfx.Bump); tvSeasonFocus = n; }
            else if (bit == Pad.A) { tvSeason = tvSeasonFocus; tvEpIdx = 0; Sound.Play(Sfx.Select); TvFillDetail(tvDetailItem); return; }
        } else {
            if (dx != 0) { int n = Math.Max(0, Math.Min(tvEpisodeRows.Count - 1, tvEpIdx + dx)); Sound.Play(n != tvEpIdx ? Sfx.Move : Sfx.Bump); tvEpIdx = n; }
            else if (bit == Pad.A && tvDetailItem.Kind != "series") { if (tvEpIdx < tvDetailMore.Count) { Sound.Play(Sfx.Select); TvOpenDetail(tvDetailMore[tvEpIdx]); } return; }
            else if (bit == Pad.A) { var eps = TvSeasonEps(); if (tvEpIdx < eps.Count) TvPlay(eps[tvEpIdx], tvDetailItem.Episodes); return; }
        }
        MarkTvDetail();
    }
    bool TvKey(Key k) {
        if (k == Key.F11) { if (!TvPlaying) TvToggleFull(); return true; }
        // live TV: a channel's number (TvExtras.cs)
        if (TvPlaying && k >= Key.D0 && k <= Key.D9 && TvChannelDigit(k - Key.D0)) return true;
        if (TvPlaying && k >= Key.NumPad0 && k <= Key.NumPad9 && TvChannelDigit(k - Key.NumPad0)) return true;   // full screen (watching is always full screen)
        if (TvSearchKey(k)) return true;   // typing on the search page (TvSearch.cs)
        // subtitle timing while watching: Z earlier, X later (Shift: a whole second), T the timing menu (TvSubs.cs)
        if (TvPlaying && (k == Key.Z || k == Key.X) && TvSubKey(k == Key.X ? 1 : -1)) return true;
        if (TvPlaying && k == Key.T) { TvSubOpenDelay(); return true; }
        int bit = k == Key.Left ? Pad.Left : k == Key.Right ? Pad.Right : k == Key.Up ? Pad.Up : k == Key.Down ? Pad.Down : k == Key.Enter || k == Key.Space ? Pad.A
                : k == Key.Escape || k == Key.Back ? Pad.B : k == Key.PageUp ? Pad.LB : k == Key.PageDown ? Pad.RB : k == Key.F ? Pad.Y : k == Key.E ? Pad.X : 0;
        if (k == Key.S && !TvPlaying && !TvProfilesOpen) { TvAskSearch(); return true; }
        if (bit == 0) return false;
        TvPad(bit); return true;
    }
    void TvHints(List<string[]> spec) {
        if (TvProfPageOpen) { spec.Add(new[] { "A", "Select" }); spec.Add(new[] { "B", "Back" }); return; }
        if (TvProfilesOpen) { spec.Add(new[] { "A", tvProfIdx < 0 ? (tvManage ? "Done" : "Manage") : tvManage ? "Change" : "Open" }); if (tvProfIdx >= 0 && !tvManage) spec.Add(new[] { "X", "Change" }); spec.Add(new[] { "B", tvManage ? "Done" : "Close" }); return; }
        if (TvPlaying) { TvPlayHints(spec); return; }
        if (TvDetailOpen) { spec.Add(new[] { "A", "Select" }); spec.Add(new[] { "B", "Back" }); return; }
        if (TvLiveOpen && tvZone == "live") { TvLiveHints(spec); return; }
        if (TvGuideOpen && tvZone == "guide") { spec.Add(new[] { "X", "More" }); spec.Add(new[] { "A", "Select" }); spec.Add(new[] { "↕", "Channel" }); spec.Add(new[] { "↔", "Programme" }); spec.Add(new[] { "B", "Top" }); return; }
        spec.Add(new[] { "LB", "Section" }); spec.Add(new[] { "RB", "Section" });
        if (tvZone == "rows" && tvFocus != null && tvFocus.Kind != "add" && tvFocus.Kind != "account") { spec.Add(new[] { "Y", TvStore.IsFav(tvFocus) ? "Remove from My list" : "My list" }); }
        if (tvZone == "rows") spec.Add(new[] { "X", "More" });
        spec.Add(new[] { "A", tvZone == "rail" ? "Open" : "Select" });
        spec.Add(new[] { "B", tvZone == "rail" ? "Close" : "Top" });
    }

}
}
