// TV & Movies: more for the player.
//   Queue           "Play next" and "Add to queue" on a film or episode: they follow when the current one ends
//   Skip intro      during an episode's first minutes: jump past the intro (a length you set per series, 85 s at first)
//   Picture & sync  fit / fill / stretch, 16:9 or 4:3, and audio or subtitles earlier or later
//   Languages       per profile: the audio language and the subtitles (off, or a language) to start with
//   Fine-tuning     hardware decoding, deinterlacing, a user agent for providers that ask for one (every profile)
//   VLC             live TV, films or the archive can open in VLC instead (when it's installed), or switch while watching
//   Picture in picture  a small window that stays on top, to drag anywhere, while you do something else
//   Multi-view      up to four live channels at once; the picked one has the sound
//   OpenSubtitles   subtitles found online for what's playing (your own free OpenSubtitles account and API key)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace RecompLauncher {

// the player's settings (in tv-player.json: the OpenSubtitles key and password encrypted with your Windows account)
static class TvPlayerPrefs {
    static string FilePath { get { return Path.Combine(Paths.Root, "tv-player.json"); } }
    public static readonly Dictionary<string, string> Alang = new Dictionary<string, string>(), Slang = new Dictionary<string, string>(), PlayerFor = new Dictionary<string, string>();
    public static readonly Dictionary<string, int> Intro = new Dictionary<string, int>();
    public static bool Hwdec = true, Deint = false; public static string UserAgent = "", OsKey = "", OsUser = "", OsPass = "";
    static bool loaded;
    static string Protect(string s) { if (string.IsNullOrEmpty(s)) return ""; try { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), Encoding.UTF8.GetBytes("RecompLauncher.TV"), DataProtectionScope.CurrentUser)); } catch { return ""; } }
    static string Unprotect(string s) { if (string.IsNullOrEmpty(s)) return ""; try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), Encoding.UTF8.GetBytes("RecompLauncher.TV"), DataProtectionScope.CurrentUser)); } catch { return ""; } }
    public static void Load() {
        if (loaded) return; loaded = true;
        var d = Json.ReadFile(FilePath); if (d == null) return;
        foreach (var k in new[] { "Alang", "Slang", "PlayerFor" }) { var o = Json.Obj(d, k); if (o == null) continue; var tgt = k == "Alang" ? Alang : k == "Slang" ? Slang : PlayerFor; foreach (var kv in o) tgt[kv.Key] = Convert.ToString(kv.Value); }
        var io = Json.Obj(d, "Intro"); if (io != null) foreach (var kv in io) Intro[kv.Key] = (int)Json.Num(io, kv.Key);
        Hwdec = !d.ContainsKey("Hwdec") || Json.Bool(d, "Hwdec"); Deint = Json.Bool(d, "Deint"); UserAgent = Json.Str(d, "UserAgent") ?? "";
        OsKey = Unprotect(Json.Str(d, "OsKey")); OsUser = Json.Str(d, "OsUser") ?? ""; OsPass = Unprotect(Json.Str(d, "OsPass"));
    }
    public static void Save() {
        try {
            Json.WriteFile(FilePath, new Dictionary<string, object> {
                { "Alang", Alang.ToDictionary(kv => kv.Key, kv => (object)kv.Value) }, { "Slang", Slang.ToDictionary(kv => kv.Key, kv => (object)kv.Value) },
                { "PlayerFor", PlayerFor.ToDictionary(kv => kv.Key, kv => (object)kv.Value) }, { "Intro", Intro.ToDictionary(kv => kv.Key, kv => (object)kv.Value) },
                { "Hwdec", Hwdec }, { "Deint", Deint }, { "UserAgent", UserAgent }, { "OsKey", Protect(OsKey) }, { "OsUser", OsUser }, { "OsPass", Protect(OsPass) } });
        } catch { }
    }
    public static string Get(Dictionary<string, string> d, string k, string def) { string v; return d.TryGetValue(k, out v) ? v : def; }
}

partial class Shell {
    readonly List<TvItem> tvQueue = new List<TvItem>();
    static readonly string[][] TvLangs = { new[] { "", "Automatic" }, new[] { "nl,nld,dut", "Nederlands" }, new[] { "en,eng", "English" }, new[] { "de,deu,ger", "Deutsch" }, new[] { "fr,fra,fre", "Français" }, new[] { "es,spa", "Español" }, new[] { "tr,tur", "Türkçe" }, new[] { "ar,ara", "العربية" } };

    // ---------- before something plays: this profile's languages and the fine-tuning ----------
    void TvApplyPlayerPrefs(IntPtr ctx) {
        TvPlayerPrefs.Load(); string prof = TvMine.Profile(tvAcc);
        string a = TvPlayerPrefs.Get(TvPlayerPrefs.Alang, prof, ""), s = TvPlayerPrefs.Get(TvPlayerPrefs.Slang, prof, "auto");
        Mpv.Set(ctx, "alang", a.Length > 0 ? a : "nl,nld,dut,en,eng");
        if (s == "off") Mpv.Set(ctx, "sid", "no"); else { Mpv.Set(ctx, "sid", "auto"); Mpv.Set(ctx, "slang", s == "auto" || s.Length == 0 ? "nl,nld,dut,en,eng" : s); }
        Mpv.Set(ctx, "hwdec", TvPlayerPrefs.Hwdec ? "auto-safe" : "no");
        Mpv.Set(ctx, "deinterlace", TvPlayerPrefs.Deint ? "yes" : "no");
        Mpv.Set(ctx, "user-agent", TvSource.Agent);
        Mpv.Set(ctx, "panscan", "0"); Mpv.Set(ctx, "keepaspect", "yes"); Mpv.Set(ctx, "video-aspect-override", "-1"); Mpv.Set(ctx, "audio-delay", "0"); Mpv.Set(ctx, "sub-delay", "0"); Mpv.Set(ctx, "speed", "1");
    }
    static string TvKindFor(TvItem it) { return it.Kind == "live" ? "live" : it.Kind == "archive" ? "archive" : "vod"; }
    // this kind of thing opens in VLC instead (when that's chosen and VLC is installed)
    bool TvUseVlc(TvItem it) { TvPlayerPrefs.Load(); return TvPlayerPrefs.Get(TvPlayerPrefs.PlayerFor, TvKindFor(it), "mpv") == "vlc" && Vlc() != null; }

    // ---------- the queue ----------
    void TvQueueAdd(TvItem it, bool next) {
        tvQueue.RemoveAll(x => x.Key == it.Key);
        if (next) tvQueue.Insert(0, it); else tvQueue.Add(it);
        SetStep(next ? "TV: " + it.Title + " plays next." : "TV: " + it.Title + " is in the queue (" + tvQueue.Count + ").", Ui.Teal);
    }
    // the end of something: the queue first
    bool TvQueueNext() {
        if (tvQueue.Count == 0) return false;
        var n = tvQueue[0]; tvQueue.RemoveAt(0); TvPlay(n, null); return true;
    }

    // ---------- skip intro ----------
    Border tvSkipBtn; bool tvSkipped;
    int TvIntroLen(TvItem it) { TvPlayerPrefs.Load(); int v; return TvPlayerPrefs.Intro.TryGetValue(it.Group ?? it.Title ?? "", out v) ? v : 85; }
    bool TvSkipAvailable(double pos) { return tvPlaying != null && (tvPlaying.Kind == "episode" || (tvPlaying.Kind == "file" && tvPlaying.Season > 0)) && !tvSkipped && pos > 2 && pos < 600 && TvIntroLen(tvPlaying) > 0; }
    void TvSkipIntro() {
        if (tvMpv == IntPtr.Zero || tvPlaying == null) return;
        int n = TvIntroLen(tvPlaying); tvSkipped = true;
        // an intro chapter in the file: to its end (TvExtras.cs)
        double end, pos = Mpv.Num(tvMpv, "time-pos");
        if (TvIntroChapterEnd(pos, out end)) { Mpv.Command(tvMpv, "seek", end.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), "absolute"); Mpv.Command(tvMpv, "show-text", "Intro skipped", "1500"); if (tvSkipBtn != null) tvSkipBtn.Visibility = Visibility.Collapsed; return; }
        Mpv.Command(tvMpv, "seek", n.ToString(), "relative"); Mpv.Command(tvMpv, "show-text", "Intro skipped (" + n + " s)", "1500");
        if (tvSkipBtn != null) tvSkipBtn.Visibility = Visibility.Collapsed;
    }
    void TvSkipTick(double pos) {
        if (tvSkipBtn == null) {
            tvSkipBtn = new Border { Child = new TextBlock { Text = "Skip intro", FontSize = 17, FontFamily = Ui.Semi, Foreground = Brushes.Black }, Background = Brushes.White, CornerRadius = new CornerRadius(8), Padding = new Thickness(22, 10, 22, 11),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 44, 20), Cursor = Cursors.Hand, Visibility = Visibility.Collapsed };
            tvSkipBtn.MouseLeftButtonUp += (s, e) => { e.Handled = true; TvSkipIntro(); };
            Grid.SetRow(tvSkipBtn, 1); tvOverlayRoot.Children.Add(tvSkipBtn);
        }
        var want = TvSkipAvailable(pos) ? Visibility.Visible : Visibility.Collapsed;
        if (tvSkipBtn.Visibility != want) { tvSkipBtn.Visibility = want; if (want == Visibility.Visible) TvWake(); }
    }

    // ---------- the next episode: a countdown card at the end ----------
    // In the last 20 seconds of an episode (when there's a next one, or something in the queue) a card says what's
    // next and counts down; it plays when the count runs out. Play now (A) starts it at once, Cancel (B) lets this one
    // end without going on.
    Border tvNextCard; TextBlock tvNextName, tvNextCount; TvItem tvNextFor; bool tvNextCancelled;
    bool TvNextCardUp { get { return tvNextCard != null && tvNextCard.Visibility == Visibility.Visible; } }
    TvItem TvNextUp() {
        var it = tvPlaying;
        if (it == null || !(it.Kind == "episode" || (it.Kind == "file" && it.Season > 0))) return null;
        return tvQueue.Count > 0 ? tvQueue[0] : TvStep(1);
    }
    void TvNextTick(double pos, double dur) {
        if (tvNextFor != tvPlaying) { tvNextFor = tvPlaying; tvNextCancelled = false; }
        var next = TvNextUp(); double left = dur - pos;
        // the credits (a chapter, or what was learnt for this series): the card then, with ten seconds to go (TvExtras.cs)
        double cs = TvCreditsAt(dur); bool credits = cs > 0 && pos >= cs;
        bool want = next != null && !tvNextCancelled && dur > 120 && pos > 0 && left > 0 && (left <= 20 || credits);
        if (credits && left > 20) left = Math.Max(0, cs + 10 - pos);
        if (!want) { if (tvNextCard != null) tvNextCard.Visibility = Visibility.Collapsed; return; }
        if (tvNextCard == null) {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = "NEXT EPISODE", FontSize = 12.5, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#B3FFFFFF") });
            tvNextName = new TextBlock { FontSize = 19, FontFamily = TvFont, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 4, 0, 14), TextTrimming = TextTrimming.CharacterEllipsis };
            sp.Children.Add(tvNextName);
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            var now = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(8), Padding = new Thickness(18, 9, 20, 10), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 10, 0) };
            var nowRow = new StackPanel { Orientation = Orientation.Horizontal };
            nowRow.Children.Add(new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 14, Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            tvNextCount = new TextBlock { FontSize = 16, FontFamily = Ui.Semi, Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center };
            nowRow.Children.Add(tvNextCount); now.Child = nowRow;
            now.MouseLeftButtonUp += (s, e) => { e.Handled = true; TvNextByHand(); };
            var cancel = new Border { Background = TvTheme.Brush("#59FFFFFF"), CornerRadius = new CornerRadius(8), Padding = new Thickness(18, 9, 18, 10), Cursor = Cursors.Hand,
                Child = new TextBlock { Text = "Cancel", FontSize = 16, FontFamily = Ui.Semi, Foreground = Brushes.White } };
            cancel.MouseLeftButtonUp += (s, e) => { e.Handled = true; TvNextCancel(); };
            btns.Children.Add(now); btns.Children.Add(cancel); sp.Children.Add(btns);
            tvNextCard = new Border { Child = sp, Width = 380, Background = TvTheme.Brush("#E61C1C1E"), BorderBrush = TvTheme.Brush("#33FFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(20, 16, 20, 18),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 44, 20), Visibility = Visibility.Collapsed,
                Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 0, Opacity = 0.7, Color = Colors.Black } };
            Grid.SetRow(tvNextCard, 1); tvOverlayRoot.Children.Add(tvNextCard);
        }
        tvNextName.Text = (next.Season > 0 ? "S" + next.Season + " E" + next.Episode + "  ·  " : "") + next.Title;
        int n = (int)Math.Ceiling(left - 1);
        tvNextCount.Text = n > 0 ? "Next episode in " + n : "Next episode";
        if (tvNextCard.Visibility != Visibility.Visible) tvNextCard.Visibility = Visibility.Visible;
        TvWake();   // the controls (and the card with them) stay up while it counts
        if (left <= 1) TvNextNow();
    }
    void TvNextNow() {
        var it = tvPlaying; if (it == null) return;
        if (tvNextCard != null) tvNextCard.Visibility = Visibility.Collapsed;
        TvMine.SetWatched(it, true);
        if (TvQueueNext()) return;
        var next = TvStep(1); if (next != null) TvPlay(next, tvPlayList);
    }
    void TvNextCancel() { tvNextCancelled = true; Sound.Play(Sfx.Back); if (tvNextCard != null) tvNextCard.Visibility = Visibility.Collapsed; }

    // ---------- the picture menu: framing and sync ----------
    void TvPictureItems(List<Tuple<string, bool, Action>> items) {
        string ps = Mpv.Get(tvMpv, "panscan") ?? "0", ka = Mpv.Get(tvMpv, "keepaspect") ?? "yes", ar = Mpv.Get(tvMpv, "video-aspect-override") ?? "-1";
        bool fill = ps.StartsWith("1"), stretch = ka == "no";
        Action<string, string, string> frame = (p, k, label) => { Mpv.Set(tvMpv, "panscan", p); Mpv.Set(tvMpv, "keepaspect", k); Mpv.Command(tvMpv, "show-text", label, "1200"); };
        items.Add(Tuple.Create("Fit (whole picture)", !fill && !stretch, (Action)(() => frame("0", "yes", "Fit"))));
        items.Add(Tuple.Create("Fill the screen (crop)", fill && !stretch, (Action)(() => frame("1", "yes", "Fill"))));
        items.Add(Tuple.Create("Stretch", stretch, (Action)(() => frame("0", "no", "Stretch"))));
        foreach (var a in new[] { new[] { "-1", "Original shape" }, new[] { "16:9", "Shape 16:9" }, new[] { "4:3", "Shape 4:3" } }) { var aa = a; items.Add(Tuple.Create(aa[1], ar.StartsWith(aa[0] == "-1" ? "-1" : aa[0].Substring(0, 2)) || (aa[0] == "-1" && ar == "no"), (Action)(() => { Mpv.Set(tvMpv, "video-aspect-override", aa[0]); Mpv.Command(tvMpv, "show-text", aa[1], "1200"); }))); }
        double ad = Mpv.Num(tvMpv, "audio-delay");
        Func<double, string> sec = v => (v >= 0 ? "+" : "") + v.ToString("0.0", CultureInfo.InvariantCulture) + " s";
        items.Add(Tuple.Create("Sound later (now " + sec(ad) + ")", false, (Action)(() => { Mpv.Command(tvMpv, "add", "audio-delay", "0.1"); Mpv.Command(tvMpv, "show-text", "Sound ${audio-delay}", "1200"); TvTracks("picture"); })));
        items.Add(Tuple.Create("Sound earlier", false, (Action)(() => { Mpv.Command(tvMpv, "add", "audio-delay", "-0.1"); Mpv.Command(tvMpv, "show-text", "Sound ${audio-delay}", "1200"); TvTracks("picture"); })));
        items.Add(Tuple.Create(TvSubTimingLabel(), false, (Action)TvSubOpenDelay));   // earlier / later, sync to a line (TvSubs.cs)
        items.Add(Tuple.Create("Sound and subtitles in sync again", false, (Action)(() => { Mpv.Set(tvMpv, "audio-delay", "0"); TvSubShift(0, true); Mpv.Command(tvMpv, "show-text", "In sync", "1200"); })));
        if (Vlc() != null) items.Add(Tuple.Create("Open in VLC from here", false, (Action)TvSwitchToVlc));
        items.Add(Tuple.Create("Picture in picture", false, (Action)TvStartPip));
    }
    void TvSwitchToVlc() {
        var it = tvPlaying; if (it == null) return;
        double pos = Mpv.Num(tvMpv, "time-pos"), dur = Mpv.Num(tvMpv, "duration");
        if (pos > 0 && dur > 0 && it.Kind != "live") TvStore.SetProgress(it, pos, dur);
        TvStop(); TvVlc(it);
    }

    // ---------- picture in picture ----------
    Window tvPip; IntPtr tvPipMpv; TvItem tvPipItem; List<TvItem> tvPipList; DispatcherTimer tvPipTimer;
    void TvStartPip() {
        var it = tvPlaying; if (it == null) return;
        double pos = Mpv.Num(tvMpv, "time-pos"), dur = Mpv.Num(tvMpv, "duration");
        if (pos > 0 && dur > 0 && it.Kind != "live") TvStore.SetProgress(it, pos, dur);
        var list = tvPlayList; string url = TvUrl(it);
        TvStop();
        TvClosePip();
        tvPipItem = it; tvPipList = list;
        var panel = new System.Windows.Forms.Panel { BackColor = System.Drawing.Color.Black, Dock = System.Windows.Forms.DockStyle.Fill };
        var host = new System.Windows.Forms.Integration.WindowsFormsHost { Child = panel, Background = Brushes.Black };
        var bar = new Grid { Height = 36, Background = TvTheme.Brush("#1C1C1E"), Cursor = Cursors.SizeAll };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Children.Add(new TextBlock { Text = it.Title, FontSize = 13, FontFamily = Ui.Semi, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        var btns = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(btns, 1);
        Func<string, string, Action, Border> b = (g, tip, act) => {
            var x = new Border { Width = 36, Height = 36, Cursor = Cursors.Hand, ToolTip = tip, Background = Brushes.Transparent, Child = new TextBlock { Text = g, FontFamily = Ui.Mdl, FontSize = 14, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            x.MouseEnter += (s, e) => x.Background = TvTheme.Brush("#33FFFFFF"); x.MouseLeave += (s, e) => x.Background = Brushes.Transparent;
            x.MouseLeftButtonUp += (s, e) => { e.Handled = true; act(); }; return x;
        };
        var pauseB = b("", "Pause / play", () => { if (tvPipMpv != IntPtr.Zero) Mpv.Command(tvPipMpv, "cycle", "pause"); });
        btns.Children.Add(pauseB);
        btns.Children.Add(b("", "Back to full screen", TvPipToFull));
        btns.Children.Add(b("", "Close", TvClosePip));
        bar.Children.Add(btns);
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(host); Grid.SetRow(bar, 1); root.Children.Add(bar);
        var wa = SystemParameters.WorkArea;
        tvPip = new Window { Title = "Picture in picture", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.CanResizeWithGrip, Topmost = true, ShowInTaskbar = true, Width = 480, Height = 306, Background = Brushes.Black,
            Left = wa.Right - 500, Top = wa.Bottom - 326, Content = root };
        bar.MouseLeftButtonDown += (s, e) => { if (e.OriginalSource == bar || e.OriginalSource is TextBlock && ((TextBlock)e.OriginalSource).Parent == bar) try { tvPip.DragMove(); } catch { } };
        tvPip.Closed += (s, e) => { if (tvPipMpv != IntPtr.Zero) { var c = tvPipMpv; tvPipMpv = IntPtr.Zero; Mpv.Destroy(c); } if (tvPipTimer != null) tvPipTimer.Stop(); tvPip = null; };
        tvPip.Loaded += (s, e) => {
            try {
                tvPipMpv = Mpv.Create(panel.Handle); TvApplyPlayerPrefs(tvPipMpv);
                double at = it.Kind == "live" ? 0 : TvStore.ResumeAt(it);
                Mpv.Set(tvPipMpv, "start", at > 0 ? ((int)at).ToString() : "none");
                Mpv.Command(tvPipMpv, "loadfile", url, "replace");
                tvPipTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                tvPipTimer.Tick += (s2, e2) => { if (tvPipMpv == IntPtr.Zero) return; ((TextBlock)pauseB.Child).Text = Mpv.Get(tvPipMpv, "pause") == "yes" ? "" : ""; };
                tvPipTimer.Start();
            } catch (Exception ex) { SetStep("TV: picture in picture didn't start: " + ex.Message, Ui.Red); TvClosePip(); }
        };
        tvPip.Show();
        SetStep("TV: " + it.Title + " plays in a small window. Drag it by its bar; the square button brings it back full screen.", Ui.Teal);
    }
    void TvPipToFull() {
        var it = tvPipItem; var list = tvPipList; if (it == null) return;
        if (tvPipMpv != IntPtr.Zero && it.Kind != "live") { double pos = Mpv.Num(tvPipMpv, "time-pos"), dur = Mpv.Num(tvPipMpv, "duration"); if (pos > 0 && dur > 0) TvStore.SetProgress(it, pos, dur); }
        TvClosePip();
        W.Activate();
        if (!TvOpen) { OpenTv(); if (TvProfilesOpen) { int idx = it.Account == null ? 0 : TvStore.Accounts.IndexOf(it.Account) + 1; TvProfileAct(Math.Max(0, idx), false); } }
        TvPlay(it, list);
    }
    void TvClosePip() { if (tvPip != null) { var w = tvPip; tvPip = null; w.Close(); } }

    // ---------- multi-view: up to four live channels ----------
    Grid tvMulti; readonly List<TvItem> tvMultiItems = new List<TvItem>(); readonly IntPtr[] tvMultiMpv = new IntPtr[4];
    readonly System.Windows.Forms.Panel[] tvMultiPanels = new System.Windows.Forms.Panel[4]; readonly Border[] tvMultiCells = new Border[4]; int tvMultiIdx; bool tvMultiWindowed;
    bool TvMultiOpen { get { return tvMulti != null && tvMulti.Visibility == Visibility.Visible; } }
    void TvMultiAdd(TvItem ch) {
        if (ch == null || ch.Kind != "live") return;
        if (tvMultiItems.Any(x => x.Key == ch.Key)) { SetStep("TV: " + ch.Title + " is already in the multi-view.", Ui.Teal); }
        else if (tvMultiItems.Count >= 4) { SetStep("TV: the multi-view holds four channels; take one out first (Y on it).", Ui.Red); return; }
        else tvMultiItems.Add(ch);
        if (ListPageOpen) CloseListPage();
        TvOpenMulti();
    }
    void TvOpenMulti() {
        if (!Mpv.Ready) { SetStep("TV: watch one channel first; the player is fetched then.", Ui.Teal); return; }
        if (tvMulti == null) {
            tvMulti = new Grid { Background = Brushes.Black, Visibility = Visibility.Collapsed };
            Grid.SetColumnSpan(tvMulti, 2); Panel.SetZIndex(tvMulti, 50);
            for (int i = 0; i < 4; i++) {
                int k = i;
                tvMultiPanels[i] = new System.Windows.Forms.Panel { BackColor = System.Drawing.Color.Black, Dock = System.Windows.Forms.DockStyle.Fill };
                tvMultiPanels[i].MouseClick += (s, e) => { tvMultiIdx = k; TvMarkMulti(); };
                tvMultiPanels[i].MouseDoubleClick += (s, e) => { tvMultiIdx = k; TvMultiFull(); };
                var host = new System.Windows.Forms.Integration.WindowsFormsHost { Child = tvMultiPanels[i], Background = Brushes.Black };
                tvMultiCells[i] = new Border { Child = host, BorderThickness = new Thickness(4), BorderBrush = Brushes.Transparent, Margin = new Thickness(3), Background = Brushes.Black };
            }
            tvLayer.Children.Add(tvMulti);
        }
        if (TvPlaying) TvStop();
        if (TvGuideOpen && tvGuideHost != null) { TvGuideStopPreview(); tvGuideHost.Visibility = Visibility.Collapsed; }
        if (!isFull) { tvMultiWindowed = true; SetFullscreen(true); }
        tvMulti.Visibility = Visibility.Visible; tvZone = "multi";
        TvLayoutMulti();
        SetHints();
    }
    void TvLayoutMulti() {
        tvMulti.Children.Clear(); tvMulti.RowDefinitions.Clear(); tvMulti.ColumnDefinitions.Clear();
        int n = Math.Max(1, tvMultiItems.Count), cols = n == 1 ? 1 : 2, rows = n <= 2 ? 1 : 2;
        for (int c = 0; c < cols; c++) tvMulti.ColumnDefinitions.Add(new ColumnDefinition());
        for (int r = 0; r < rows; r++) tvMulti.RowDefinitions.Add(new RowDefinition());
        for (int i = 0; i < 4; i++) {
            if (i < tvMultiItems.Count) {
                Grid.SetColumn(tvMultiCells[i], i % cols); Grid.SetRow(tvMultiCells[i], i / cols); tvMulti.Children.Add(tvMultiCells[i]);
            } else if (tvMultiMpv[i] != IntPtr.Zero) { Mpv.Command(tvMultiMpv[i], "stop"); }
        }
        if (tvMultiIdx >= tvMultiItems.Count) tvMultiIdx = Math.Max(0, tvMultiItems.Count - 1);
        W.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
            for (int i = 0; i < tvMultiItems.Count; i++) {
                try {
                    if (tvMultiMpv[i] == IntPtr.Zero) { tvMultiMpv[i] = Mpv.Create(tvMultiPanels[i].Handle); TvApplyPlayerPrefs(tvMultiMpv[i]); }
                    if (Mpv.Get(tvMultiMpv[i], "path") != tvMultiItems[i].Url) Mpv.Command(tvMultiMpv[i], "loadfile", tvMultiItems[i].Url, "replace");
                    Mpv.Command(tvMultiMpv[i], "show-text", tvMultiItems[i].Title, "2500");
                } catch { }
            }
            TvMarkMulti();
        }));
    }
    void TvMarkMulti() {
        for (int i = 0; i < 4; i++) {
            bool on = i == tvMultiIdx && i < tvMultiItems.Count;
            tvMultiCells[i].BorderBrush = on ? Brushes.White : Brushes.Transparent;
            if (tvMultiMpv[i] != IntPtr.Zero) Mpv.Set(tvMultiMpv[i], "mute", on ? "no" : "yes");
        }
    }
    void TvMultiFull() {
        if (tvMultiIdx >= tvMultiItems.Count) return;
        var ch = tvMultiItems[tvMultiIdx]; var list = tvMultiItems.ToList();
        TvCloseMulti(false); TvPlay(ch, list);
    }
    void TvCloseMulti(bool restoreWindow = true) {
        if (tvMulti == null) return;
        for (int i = 0; i < 4; i++) if (tvMultiMpv[i] != IntPtr.Zero) Mpv.Command(tvMultiMpv[i], "stop");
        tvMulti.Visibility = Visibility.Collapsed;
        if (restoreWindow && tvMultiWindowed) { tvMultiWindowed = false; SetFullscreen(false); }
        tvZone = TvGuideOpen ? "guide" : TvLiveOpen ? "live" : "rows";
        if (TvGuideOpen) TvGuideBack();
        SetHints();
    }
    void TvMultiPad(int bit) {
        int n = tvMultiItems.Count, cols = n == 1 ? 1 : 2;
        if (bit == Pad.B) { Sound.Play(Sfx.Back); TvCloseMulti(); return; }
        if (n == 0) return;
        int j = tvMultiIdx;
        if (bit == Pad.Left && j % cols > 0) j--; else if (bit == Pad.Right && j % cols < cols - 1 && j + 1 < n) j++;
        else if (bit == Pad.Up && j - cols >= 0) j -= cols; else if (bit == Pad.Down && j + cols < n) j += cols;
        else if (bit == Pad.A) { TvMultiFull(); return; }
        else if (bit == Pad.Y) { var ch = tvMultiItems[j]; tvMultiItems.RemoveAt(j); if (tvMultiMpv[j] != IntPtr.Zero) Mpv.Command(tvMultiMpv[j], "stop");
            // the players move up with the channels
            for (int i = j; i < 3; i++) { var t = tvMultiMpv[i]; tvMultiMpv[i] = tvMultiMpv[i + 1]; tvMultiMpv[i + 1] = t; var pnl = tvMultiPanels[i]; tvMultiPanels[i] = tvMultiPanels[i + 1]; tvMultiPanels[i + 1] = pnl; var c = tvMultiCells[i]; tvMultiCells[i] = tvMultiCells[i + 1]; tvMultiCells[i + 1] = c; }
            SetStep("TV: " + ch.Title + " is out of the multi-view.", Ui.Teal); if (tvMultiItems.Count == 0) { TvCloseMulti(); return; } TvLayoutMulti(); return; }
        if (j != tvMultiIdx) { tvMultiIdx = j; Sound.Play(Sfx.Move); TvMarkMulti(); } else Sound.Play(Sfx.Bump);
    }

    // ---------- OpenSubtitles ----------
    // the key, then the username and password (both optional: more downloads a day); "done" afterwards
    void TvOsSetup(Action done) {
        OpenOsk("Your OpenSubtitles API key (paste it with Ctrl+V; empty: off)", TvPlayerPrefs.OsKey, null, key => {
            TvPlayerPrefs.OsKey = (key ?? "").Trim(); TvPlayerPrefs.Save(); tvOsToken = null;
            if (TvPlayerPrefs.OsKey.Length == 0) { if (done != null) done(); return; }
            OpenOsk("Your OpenSubtitles username (optional)", TvPlayerPrefs.OsUser, null, user => {
                TvPlayerPrefs.OsUser = (user ?? "").Trim(); TvPlayerPrefs.Save();
                OpenOsk("Your OpenSubtitles password", "", null, pass => {
                    TvPlayerPrefs.OsPass = pass ?? ""; TvPlayerPrefs.Save();
                    SetStep("OpenSubtitles is set up: Dutch and English subtitles now come by themselves.", Ui.Teal);
                    if (done != null) done();
                }, done, true);
            }, done, false, true);
        }, done, true, true);
    }
    string tvOsToken; List<Dictionary<string, object>> tvOsResults; string tvOsNote;
    static string OsCall(string method, string path, string body, string token) {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        var r = (HttpWebRequest)WebRequest.Create("https://api.opensubtitles.com/api/v1/" + path);
        r.Method = method; r.UserAgent = "RecompLauncher v2.5"; r.Accept = "application/json"; r.Headers["Api-Key"] = TvPlayerPrefs.OsKey; r.Timeout = 20000;
        if (token != null) r.Headers["Authorization"] = "Bearer " + token;
        if (body != null) { r.ContentType = "application/json"; var b = Encoding.UTF8.GetBytes(body); using (var s = r.GetRequestStream()) s.Write(b, 0, b.Length); }
        using (var resp = r.GetResponse()) using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
    }
    void TvOsSearch() {
        var it = tvPlaying; if (it == null) return;
        TvPlayerPrefs.Load();
        if (string.IsNullOrEmpty(TvPlayerPrefs.OsKey)) {
            tvOsResults = null;
            tvOsNote = "Not set up yet: needs your free OpenSubtitles API key (opensubtitles.com → API consumers).";
            TvTracks("os"); return;
        }
        tvOsResults = null; tvOsNote = "Searching OpenSubtitles..."; TvTracks("os");
        string prof = TvMine.Profile(tvAcc), sl = TvPlayerPrefs.Get(TvPlayerPrefs.Slang, prof, "auto");
        string langs = sl.Length > 0 && sl != "auto" && sl != "off" ? sl.Split(',')[0] + ",en" : "nl,en";
        { string first = langs.Split(',')[0]; langs = first == "nl" || first == "en" ? "nl,en" : first + ",nl,en"; }   // Dutch and English always, your own language first
        string q = it.Kind == "episode" ? (it.Group ?? it.Title) : it.Title;
        new Thread(() => {
            List<Dictionary<string, object>> res = null; string note = null;
            try {
                // by the IMDb number when Cinemeta knows it (TvSubs.cs); the most downloaded first, Dutch before English
                res = TvOsFind(it, langs).OrderBy(r => (Json.Str(Json.Obj(r, "attributes"), "language") ?? "") == "nl" ? 0 : 1).ThenByDescending(r => Json.Num(Json.Obj(r, "attributes"), "download_count")).Take(25).ToList();
                if (res.Count == 0) note = "Nothing found for \"" + q + "\".";
            } catch (Exception e) { note = "OpenSubtitles didn't answer: " + e.Message; }
            W.Dispatcher.BeginInvoke(new Action(() => { tvOsResults = res; tvOsNote = note; if (TvTrackOpen && tvTrackTitle.Text.StartsWith("Online")) TvTracks("os"); }));
        }) { IsBackground = true }.Start();
    }
    void TvOsItems(List<Tuple<string, bool, Action>> items) {
        if (tvOsNote != null) items.Add(Tuple.Create(tvOsNote, false, (Action)(() => { })));
        // not set up: right here (the film stops: the keyboard can't show over the picture; play it again after)
        if (string.IsNullOrEmpty(TvPlayerPrefs.OsKey)) items.Add(Tuple.Create("Set up OpenSubtitles now (stops the film)", false, (Action)(() => { TvStop(); TvOsSetup(null); })));
        if (tvOsResults != null) foreach (var r in tvOsResults) {
            var a = Json.Obj(r, "attributes"); if (a == null) continue;
            var files = (a.ContainsKey("files") ? a["files"] as System.Collections.IEnumerable : null) ?? new object[0];
            var f = files.Cast<object>().OfType<Dictionary<string, object>>().FirstOrDefault(); if (f == null) continue;
            double dls = Json.Num(a, "download_count"); string lang = Json.Str(a, "language") ?? "";
            string fileId = Json.Str(f, "file_id"), name = (TvLang(lang) + "  –  " + (Json.Str(a, "release") ?? Json.Str(f, "file_name") ?? "")).Trim();
            string shown = name + (dls > 0 ? "   ·  " + (dls >= 1000 ? (dls / 1000).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "k" : dls.ToString("0")) + " downloads" : "");
            items.Add(Tuple.Create(shown, false, (Action)(() => TvOsGet(fileId, name, lang))));
        }
    }
    void TvOsGet(string fileId, string name, string lang) {
        Mpv.Command(tvMpv, "show-text", "Getting the subtitles...", "2000");
        var it = tvPlaying;
        new Thread(() => {
            string err = null, file = null;
            try { file = TvOsDownload(fileId, lang); } catch (Exception e) { err = e.Message; }   // (TvSubs.cs)
            W.Dispatcher.BeginInvoke(new Action(() => {
                if (tvMpv == IntPtr.Zero) return;
                if (err != null) { Mpv.Command(tvMpv, "show-text", "OpenSubtitles: " + err, "3500"); return; }
                // picked by hand: kept for this film as its Dutch or English ones (they come with it next time)
                if (it != null && it.Kind != "live" && (lang == "nl" || lang == "en")) { TvSubStore.Put(it.Key, "File_" + lang, file); TvSubStore.Put(it.Key, "Name_" + lang, name); }
                Mpv.Command(tvMpv, "sub-add", file, "select", name); Mpv.Command(tvMpv, "show-text", "Subtitles: " + name, "2000");
            }));
        }) { IsBackground = true }.Start();
    }

    // ---------- the settings, in a profile's settings ----------
    IEnumerable<ListRow> TvPlayerRows() {
        TvPlayerPrefs.Load();
        string id = tvAccFree ? "free" : tvAccEdit.Id;
        string a = TvPlayerPrefs.Get(TvPlayerPrefs.Alang, id, ""), s = TvPlayerPrefs.Get(TvPlayerPrefs.Slang, id, "auto");
        Func<string, string> langName = code => (TvLangs.FirstOrDefault(l => l[0] == code) ?? new[] { code, code })[1];
        // first in the section: online subtitles (Dutch by default, English extra; TvSubs.cs)
        bool os = !string.IsNullOrEmpty(TvPlayerPrefs.OsKey);
        yield return new ListRow { Glyph = "", C1 = "#2f8fe0", C2 = "#2f8fe0", Title = "OpenSubtitles: " + (os ? (string.IsNullOrEmpty(TvPlayerPrefs.OsUser) ? "API key set" : "signed in as " + TvPlayerPrefs.OsUser) : "not set up"),
            Text = os ? "Every profile  •  Dutch and English subtitles are fetched by themselves when a film has none; pick to change" : "Every profile  •  pick to type your API key (opensubtitles.com → API consumers), then your username and password",
            Do = () => TvOsSetup(() => TvFillAccountForm()) };
        yield return new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Audio language: " + langName(a), Text = "Pick to change",
            Do = () => { int i = Array.FindIndex(TvLangs, l => l[0] == a); TvPlayerPrefs.Alang[id] = TvLangs[(i + 1) % TvLangs.Length][0]; TvPlayerPrefs.Save(); TvFillAccountForm(); } };
        var subs = new[] { "auto", "off" }.Concat(TvLangs.Skip(1).Select(l => l[0])).ToList();
        yield return new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Subtitles: " + (s == "auto" ? "when there are Dutch or English" : s == "off" ? "off" : langName(s)), Text = "Pick to change",
            Do = () => { int i = subs.IndexOf(s); TvPlayerPrefs.Slang[id] = subs[(i + 1) % subs.Count]; TvPlayerPrefs.Save(); TvFillAccountForm(); } };
        if (Vlc() != null) foreach (var k in new[] { new[] { "live", "Live TV" }, new[] { "vod", "Films and series" }, new[] { "archive", "TV Archive" } }) {
            var kk = k; string cur = TvPlayerPrefs.Get(TvPlayerPrefs.PlayerFor, kk[0], "mpv");
            yield return new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = kk[1] + " plays in: " + (cur == "vlc" ? "VLC" : "the launcher's player"), Text = "Every profile  •  pick to change",
                Do = () => { TvPlayerPrefs.PlayerFor[kk[0]] = cur == "vlc" ? "mpv" : "vlc"; TvPlayerPrefs.Save(); TvFillAccountForm(); } };
        }
        yield return new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Hardware decoding: " + (TvPlayerPrefs.Hwdec ? "on" : "off"), Text = "Every profile  •  turn off when a stream shows blocks or green",
            Do = () => { TvPlayerPrefs.Hwdec = !TvPlayerPrefs.Hwdec; TvPlayerPrefs.Save(); TvFillAccountForm(); } };
        yield return new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Deinterlace: " + (TvPlayerPrefs.Deint ? "on" : "off"), Text = "Every profile  •  for live TV with lines in moving pictures",
            Do = () => { TvPlayerPrefs.Deint = !TvPlayerPrefs.Deint; TvPlayerPrefs.Save(); TvFillAccountForm(); } };
        yield return new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "User agent: " + (string.IsNullOrEmpty(TvPlayerPrefs.UserAgent) ? "the usual one" : "your own"), Text = "Every profile  •  only when your provider asks for a specific one",
            Do = () => OpenOsk("A user agent (empty: the usual one)", TvPlayerPrefs.UserAgent, null, v => { TvPlayerPrefs.UserAgent = (v ?? "").Trim(); TvPlayerPrefs.Save(); TvFillAccountForm(); }, null, false, true) };
    }
}
}
