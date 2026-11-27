// TV & Movies: the player. Watching is always full screen, only the picture. Move the mouse or press anything and the
// controls come up over it: back and the title on top; underneath where you are (click the line to jump), pause, back
// 10 seconds, forward 10 and 30, the next episode, subtitles, audio and speed. After a few seconds of nothing they fade
// away again, with the mouse pointer.
// The picture is mpv's own window, which nothing in the launcher's window can be drawn over; so the controls live in a
// small see-through window of their own that sits exactly over the picture (and never takes the keyboard).
// Controller while watching: any button shows the controls; A pause, left / right 10 s, LB / RB a minute, X subtitles
// (X again: their timing),
// down to the buttons (left / right, A), B stops, LT / RT the volume. On a channel: up / down change channel.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RecompLauncher {

partial class Shell {
    Window tvOverlay; Grid tvOverlayRoot; DispatcherTimer tvHideTimer; bool tvWasWindowed, tvCursorHidden; System.Drawing.Point tvLastMouse;
    TextBlock tvPlaySub;
    Border tvSeekBar, tvSeekFill; Ellipse tvSeekDot; TextBlock tvPlayPos, tvPlayDur, tvPauseGlyph; Grid tvSeekRow;
    readonly List<Border> tvPlayTools = new List<Border>(); readonly List<Action> tvPlayActs = new List<Action>(); int tvPlayIdx = -1, tvPauseIdx; Border tvNextTool, tvSpeedTool, tvChanTool;
    Border tvTrackCard; StackPanel tvTrackList; TextBlock tvTrackTitle, tvTrackHint; string tvTrackKind; bool tvTrackRefresh; int tvSubTimingRow = -1; Border tvTimingTool; readonly List<Border> tvTrackRows = new List<Border>(); readonly List<Action> tvTrackActs = new List<Action>(); int tvTrackIdx;
    bool TvTrackOpen { get { return tvTrackCard != null && tvTrackCard.Visibility == Visibility.Visible; } }
    bool TvControlsUp { get { return tvOverlay != null && tvOverlay.IsVisible && tvOverlayRoot.Opacity > 0.05; } }


    void BuildTvPlayer() {
        tvPlayer = new Grid { Visibility = Visibility.Collapsed, Background = Brushes.Black };
        Grid.SetColumnSpan(tvPlayer, 2);
        // the picture: a window mpv draws into, the whole screen
        tvVideo = TvNewVideoPanel();
        tvVideoHost = new System.Windows.Forms.Integration.WindowsFormsHost { Child = tvVideo, Background = Brushes.Black };
        tvPlayer.Children.Add(tvVideoHost);
        tvLayer.Children.Add(tvPlayer);
        tvPlayer.SizeChanged += (s, e) => TvPlaceOverlay();
        W.LocationChanged += (s, e) => TvPlaceOverlay();
        tvPlayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        tvPlayTimer.Tick += (s, e) => TvTick();
        tvHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        tvHideTimer.Tick += (s, e) => { tvHideTimer.Stop(); if (!TvTrackOpen && !TvMiniOpen && tvPlayIdx < 0) TvSleep(); else tvHideTimer.Start(); };
        BuildTvOverlay();
    }

    // ---------- the controls' window ----------
    void BuildTvOverlay() {
        tvOverlayRoot = new Grid { Background = TvTheme.Brush("#01000000"), Opacity = 0, LayoutTransform = N<Grid>("Main").LayoutTransform };
        tvOverlayRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        tvOverlayRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        tvOverlayRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        tvOverlayRoot.MouseMove += (s, e) => {
            // only a real move counts (a window showing up under a still mouse sends one too)
            var p = e.GetPosition(tvOverlayRoot);
            if (Math.Abs(p.X - tvPsMouse.X) + Math.Abs(p.Y - tvPsMouse.Y) > 4) { tvPsMouse = p; TvActivity(); }
            TvWake();
        };
        tvOverlayRoot.MouseLeftButtonUp += (s, e) => { if (e.OriginalSource == tvOverlayRoot) { TvPause(); TvWake(); } };
        tvOverlayRoot.MouseWheel += (s, e) => { if (tvMpv != IntPtr.Zero) { Mpv.Command(tvMpv, "add", "volume", e.Delta > 0 ? "5" : "-5"); Mpv.Command(tvMpv, "show-text", "Volume ${volume}%", "900"); } TvWake(); };
        // on top: back, the title, how it's going
        var top = new Grid { Background = new LinearGradientBrush(Color.FromArgb(0xC8, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90), Height = 150, VerticalAlignment = VerticalAlignment.Top };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var back = TvRound("", "Back", 50); back.Margin = new Thickness(36, 30, 0, 0); back.VerticalAlignment = VerticalAlignment.Top;
        back.MouseLeftButtonUp += (s, e) => { e.Handled = true; TvStop(); };
        top.Children.Add(back);
        var titles = new StackPanel { Margin = new Thickness(20, 32, 20, 0), VerticalAlignment = VerticalAlignment.Top }; Grid.SetColumn(titles, 1);
        tvPlayTitle = new TextBlock { FontSize = 26, FontFamily = TvFont, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
        tvPlaySub = new TextBlock { FontSize = 15.5, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#CCFFFFFF"), Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        titles.Children.Add(tvPlayTitle); titles.Children.Add(tvPlaySub);
        top.Children.Add(titles);
        tvPlayTime = new TextBlock { FontSize = 15, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#D9FFFFFF"), Margin = new Thickness(0, 44, 40, 0), VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(tvPlayTime, 2); top.Children.Add(tvPlayTime);
        tvOverlayRoot.Children.Add(top);
        // the menu for subtitles, audio and speed
        tvTrackList = new StackPanel();
        tvTrackTitle = new TextBlock { FontSize = 20, FontFamily = TvFont, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(14, 4, 0, 10) };
        // a line of help under the title for menus that need it (subtitle timing)
        tvTrackHint = new TextBlock { FontSize = 13.5, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#B3FFFFFF"), Margin = new Thickness(14, -4, 10, 10), TextWrapping = TextWrapping.Wrap, LineHeight = 20, Visibility = Visibility.Collapsed };
        var col = new StackPanel(); col.Children.Add(tvTrackTitle); col.Children.Add(tvTrackHint); col.Children.Add(new ScrollViewer { Content = tvTrackList, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false });
        tvTrackCard = new Border { Child = col, Width = 400, Background = TvTheme.Brush("#F01C1C1E"), BorderBrush = TvTheme.Brush("#33FFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(10, 14, 10, 12),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 40, 10), Visibility = Visibility.Collapsed,
            Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 0, Opacity = 0.7, Color = Colors.Black } };
        Grid.SetRow(tvTrackCard, 1); tvOverlayRoot.Children.Add(tvTrackCard);
        // underneath: where you are, and the buttons
        var bottom = BuildTvControls(); Grid.SetRow(bottom, 2);
        tvOverlayRoot.Children.Add(bottom);
        tvOverlay = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, ShowActivated = false, ResizeMode = ResizeMode.NoResize,
            Content = TvWrapOverlay(tvOverlayRoot) /* with the pause screen under the controls (TvPauseScreen.cs) */, Owner = W, Title = "Recomp Launcher player", Focusable = false };
        tvOverlay.SourceInitialized += (s, e) => {
            // never takes the keyboard from the launcher (clicks still work)
            var h = new WindowInteropHelper(tvOverlay).Handle;
            SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x08000000 | 0x00000080);
        };
        tvOverlay.PreviewKeyDown += (s, e) => { if (TvKey(e.Key)) e.Handled = true; };
    }
    Border TvRound(string glyph, string tip, double size) {
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = TvTheme.Brush("#33FFFFFF"), Cursor = Cursors.Hand, ToolTip = tip,
            Child = new TextBlock { Text = glyph, FontFamily = Ui.Mdl, FontSize = size * 0.38, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        b.MouseEnter += (s, e) => b.Background = TvTheme.Brush("#59FFFFFF"); b.MouseLeave += (s, e) => b.Background = TvTheme.Brush("#33FFFFFF");
        return b;
    }
    // the controls' window, exactly over the picture
    void TvPlaceOverlay() {
        if (tvOverlay == null || tvPlayer == null || tvPlayer.Visibility != Visibility.Visible || !tvPlayer.IsLoaded) return;
        try {
            var src = PresentationSource.FromVisual(W); if (src == null) return;
            var m = src.CompositionTarget.TransformFromDevice;
            var a = m.Transform(tvPlayer.PointToScreen(new Point(0, 0)));
            var b = m.Transform(tvPlayer.PointToScreen(new Point(tvPlayer.ActualWidth, tvPlayer.ActualHeight)));
            tvOverlay.Left = a.X; tvOverlay.Top = a.Y; tvOverlay.Width = Math.Max(10, b.X - a.X); tvOverlay.Height = Math.Max(10, b.Y - a.Y);
        } catch { }
    }
    // the controls come up (and go again after a few seconds of nothing)
    void TvWake() {
        if (!TvPlaying || tvOverlay == null) return;
        if (tvCursorHidden) { System.Windows.Forms.Cursor.Show(); tvCursorHidden = false; }
        if (!tvOverlay.IsVisible) { TvPlaceOverlay(); tvOverlay.Show(); }
        if (tvOverlayRoot.Opacity < 1) tvOverlayRoot.BeginAnimation(UIElement.OpacityProperty, Anim(1, 160));
        tvHideTimer.Stop(); tvHideTimer.Start();
    }
    void TvSleep() {
        if (tvOverlay == null || !tvOverlay.IsVisible) return;
        var a = Anim(0, 450); a.Completed += (s, e) => { if (tvOverlayRoot.Opacity < 0.05 && tvOverlay.IsVisible && !TvTrackOpen && !TvPauseUp) tvOverlay.Hide(); };
        tvOverlayRoot.BeginAnimation(UIElement.OpacityProperty, a);
        if (TvPlaying && !tvCursorHidden && W.IsActive) { System.Windows.Forms.Cursor.Hide(); tvCursorHidden = true; }
    }
    // watching: full screen; done: the window as it was
    void TvEnterWatching() {
        if (!isFull) { tvWasWindowed = true; SetFullscreen(true); }
        // nothing of the launcher around the picture: the window content at its full size, edge to edge
        var rs = N<Border>("Root").RenderTransform as ScaleTransform;
        if (rs != null && !rs.IsFrozen) { rs.BeginAnimation(ScaleTransform.ScaleXProperty, null); rs.BeginAnimation(ScaleTransform.ScaleYProperty, null); rs.ScaleX = 1; rs.ScaleY = 1; }
        N<Border>("Root").BorderThickness = new Thickness(0); W.BorderThickness = new Thickness(0); W.Background = Brushes.Black;
        W.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { TvPlaceOverlay(); TvWake(); }));
    }
    void TvLeaveWatching() {
        tvHideTimer.Stop();
        if (tvCursorHidden) { System.Windows.Forms.Cursor.Show(); tvCursorHidden = false; }
        if (TvTrackOpen) tvTrackCard.Visibility = Visibility.Collapsed;
        if (TvMiniOpen) tvMiniCard.Visibility = Visibility.Collapsed;
        if (tvPauseScreen != null) { tvPauseScreen.BeginAnimation(UIElement.OpacityProperty, null); tvPauseScreen.Opacity = 0; tvPauseScreen.Visibility = Visibility.Collapsed; }
        if (tvOverlay != null) { tvOverlayRoot.BeginAnimation(UIElement.OpacityProperty, null); tvOverlayRoot.Opacity = 0; tvOverlay.Hide(); }
        if (tvWasWindowed) { tvWasWindowed = false; SetFullscreen(false); }
    }

    // ---------- underneath: where you are, the buttons ----------
    Grid BuildTvControls() {
        var bar = new Grid { Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0xD9, 0, 0, 0), 90), Height = 190, VerticalAlignment = VerticalAlignment.Bottom };
        bar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // where you are
        tvSeekRow = new Grid { Margin = new Thickness(40, 0, 40, 8) }; Grid.SetRow(tvSeekRow, 1);
        tvSeekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tvSeekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tvSeekFill = new Border { Background = TvTheme.Brush(TvAccent), CornerRadius = new CornerRadius(2.5), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        tvSeekBar = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Background = TvTheme.Brush("#4DFFFFFF"), Child = tvSeekFill, VerticalAlignment = VerticalAlignment.Center };
        tvSeekDot = new Ellipse { Width = 16, Height = 16, Fill = TvTheme.Brush(TvAccent), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        var seek = new Grid { Height = 26, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        seek.Children.Add(tvSeekBar); seek.Children.Add(tvSeekDot);
        Action<MouseEventArgs> jump = e => {
            if (tvMpv == IntPtr.Zero || tvPlaying == null || tvPlaying.Kind == "live") return;
            double f = Math.Max(0, Math.Min(1, e.GetPosition(tvSeekBar).X / Math.Max(1, tvSeekBar.ActualWidth)));
            Mpv.Command(tvMpv, "seek", (f * 100).ToString("0.###", CultureInfo.InvariantCulture), "absolute-percent");
            tvSeekFill.Width = tvSeekBar.ActualWidth * f; tvSeekDot.Margin = new Thickness(tvSeekBar.ActualWidth * f - 8, 0, 0, 0);
        };
        seek.MouseLeftButtonDown += (s, e) => { seek.CaptureMouse(); jump(e); e.Handled = true; };
        seek.MouseMove += (s, e) => { if (seek.IsMouseCaptured) jump(e); };
        seek.MouseLeftButtonUp += (s, e) => { seek.ReleaseMouseCapture(); e.Handled = true; };
        tvSeekRow.Children.Add(seek);
        var times = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) }; Grid.SetColumn(times, 1);
        tvPlayPos = new TextBlock { FontSize = 15, FontFamily = Ui.Semi, Foreground = Brushes.White };
        tvPlayDur = new TextBlock { FontSize = 15, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#99FFFFFF"), Margin = new Thickness(8, 0, 0, 0) };
        times.Children.Add(tvPlayPos); times.Children.Add(tvPlayDur);
        tvSeekRow.Children.Add(times);
        bar.Children.Add(tvSeekRow);
        // the buttons
        var row = new Grid { Margin = new Thickness(30, 0, 30, 28) }; Grid.SetRow(row, 2);
        var left = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        tvPlayTools.Clear(); tvPlayActs.Clear();
        var pp = TvTool(left, "", null, "Pause / play", TvPause, 26); tvPauseGlyph = (TextBlock)((StackPanel)pp.Child).Children[0]; tvPauseIdx = 0;
        TvTool(left, "", "10", "Back 10 seconds", () => TvSeek(-10));
        TvTool(left, "", "10", "Forward 10 seconds", () => TvSeek(10));
        TvTool(left, "", "30", "Forward 30 seconds", () => TvSeek(30));
        tvNextTool = TvTool(right, "", "Next episode", "Next episode", () => { if (TvQueueNext()) return; var n = TvStep(1); if (n != null) TvPlay(n, tvPlayList); });
        TvTool(right, "", "Subtitles", "Subtitles", () => TvTracks("sub"));
        tvTimingTool = TvTool(right, "", "Timing", "Subtitle delay: − / + (T, or Z / X)", TvSubOpenDelay);
        TvTool(right, "", "Audio", "Audio", () => TvTracks("audio"));
        tvChanTool = TvTool(right, "", "Channels", "Channels", TvOpenMini);
        TvTool(right, "\uE91B", "Picture", "Picture, sync, picture in picture", () => TvTracks("picture"));
        tvSpeedTool = TvTool(right, "", "1×", "Speed", () => TvTracks("speed"));
        row.Children.Add(left); row.Children.Add(right);
        bar.Children.Add(row);
        return bar;
    }
    Border TvTool(Panel into, string glyph, string text, string tip, Action act, double size = 20) {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        if (glyph != null) sp.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.Mdl, FontSize = size, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White });
        if (text != null) sp.Children.Add(new TextBlock { Text = text, FontSize = 16, FontFamily = Ui.Semi, Margin = new Thickness(glyph != null ? 9 : 0, 0, 0, 1), VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White });
        var b = new Border { Child = sp, Height = 50, MinWidth = 50, Padding = new Thickness(text != null ? 18 : 0, 0, text != null ? 18 : 0, 0), CornerRadius = new CornerRadius(25), Margin = new Thickness(4, 0, 4, 0),
            Cursor = Cursors.Hand, ToolTip = tip, Background = Brushes.Transparent, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
        b.MouseEnter += (s, e) => { if (tvPlayTools.IndexOf(b) != tvPlayIdx) b.Background = TvTheme.Brush("#33FFFFFF"); };
        b.MouseLeave += (s, e) => TvMarkTools();
        b.MouseLeftButtonUp += (s, e) => { e.Handled = true; Sound.Play(Sfx.Select); act(); TvWake(); };
        tvPlayTools.Add(b); tvPlayActs.Add(act); into.Children.Add(b);
        return b;
    }
    void TvMarkTools() {
        for (int i = 0; i < tvPlayTools.Count; i++) {
            bool on = i == tvPlayIdx;
            tvPlayTools[i].Background = on ? Brushes.White : Brushes.Transparent;
            foreach (var t in ((StackPanel)tvPlayTools[i].Child).Children.OfType<TextBlock>()) t.Foreground = on ? Brushes.Black : Brushes.White;
            var st = (ScaleTransform)tvPlayTools[i].RenderTransform; st.ScaleX = st.ScaleY = on ? 1.08 : 1;
        }
        SetHints();
    }
    // what the controls show for what's playing
    void TvToolsFor(TvItem it) {
        bool live = it.Kind == "live";
        tvPlaySub.Text = live ? "LIVE" + (it.Group != null ? "  •  " + it.Group : "")
            : it.Kind == "archive" ? "TV Archive  •  " + it.Group : it.Kind == "episode" ? (it.Season > 0 ? "S" + it.Season + " E" + it.Episode + "  •  " : "") + (it.Group ?? "") : (it.Year ?? "");
        tvSeekRow.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
        for (int i = 1; i < 4; i++) tvPlayTools[i].Visibility = live ? Visibility.Collapsed : Visibility.Visible;
        var n = TvStep(1);
        tvNextTool.Visibility = (n != null || tvQueue.Count > 0) && !live ? Visibility.Visible : Visibility.Collapsed;
        tvSpeedTool.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
        tvTimingTool.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
        tvChanTool.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
        if (TvMiniOpen) tvMiniCard.Visibility = Visibility.Collapsed;
        ((TextBlock)((StackPanel)tvSpeedTool.Child).Children[1]).Text = "1×";
        if (TvTrackOpen) tvTrackCard.Visibility = Visibility.Collapsed;
        tvPlayIdx = -1; TvMarkTools();
        if (live) TvNowNext(it);   // the channel's number, and what's on now and next (TvExtras.cs)
    }
    void TvToolsTick(double pos, double dur, bool paused) {
        tvPauseGlyph.Text = paused ? "" : "";
        if (dur > 0 && pos >= 0) {
            double f = Math.Min(1, pos / dur);
            tvPlayPos.Text = TvTime(pos); tvPlayDur.Text = "/  " + TvTime(dur);
            tvSeekFill.Width = Math.Max(0, tvSeekBar.ActualWidth * f); tvSeekDot.Margin = new Thickness(Math.Max(0, tvSeekBar.ActualWidth * f - 8), 0, 0, 0);
        } else { tvPlayPos.Text = pos > 0 ? TvTime(pos) : ""; tvPlayDur.Text = ""; tvSeekFill.Width = 0; }
        // paused: the controls stay up
        if (paused && TvPlaying && !TvControlsUp && !TvPauseUp) TvWake();
    }

    // ---------- the menus: subtitles, audio, speed ----------
    void TvTracks(string kind) {
        if (tvMpv == IntPtr.Zero) return;
        tvTrackRows.Clear(); tvTrackActs.Clear(); tvTrackList.Children.Clear(); tvTrackIdx = 0;
        tvTrackKind = kind; tvSubTimingRow = -1; tvSubDelayVal = null; tvTrackHint.Visibility = Visibility.Collapsed;
        var items = new List<Tuple<string, bool, Action>>();
        if (kind == "picture") { tvTrackTitle.Text = "Picture and sync"; TvPictureItems(items); }
        else if (kind == "os") { tvTrackTitle.Text = "Online subtitles"; TvOsItems(items); }
        else if (kind == "subtime") TvSubTimeItems(items);   // earlier / later (TvSubs.cs)
        else if (kind == "substyle") TvSubStyleItems(items);   // size, colour, box, position (TvExtras.cs)
        else if (kind == "speed") {
            tvTrackTitle.Text = "Speed";
            double cur = Mpv.Num(tvMpv, "speed");
            foreach (var v in new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 }) {
                double vv = v; string label = vv == 1 ? "Normal" : vv.ToString("0.##", CultureInfo.InvariantCulture) + "×";
                items.Add(Tuple.Create(label, Math.Abs(cur - vv) < 0.01, (Action)(() => {
                    Mpv.Set(tvMpv, "speed", vv.ToString(CultureInfo.InvariantCulture)); Mpv.Command(tvMpv, "show-text", "Speed " + label, "1500");
                    ((TextBlock)((StackPanel)tvSpeedTool.Child).Children[1]).Text = vv == 1 ? "1×" : vv.ToString("0.##", CultureInfo.InvariantCulture) + "×"; })));
            }
        } else {
            string type = kind, prop = kind == "sub" ? "sid" : "aid";
            tvTrackTitle.Text = kind == "sub" ? "Subtitles" : "Audio";
            int count = (int)Math.Max(0, Mpv.Num(tvMpv, "track-list/count"));
            bool any = false, anySel = false;
            var tracks = new List<Tuple<string, bool, Action>>();
            for (int i = 0; i < count; i++) {
                if (Mpv.Get(tvMpv, "track-list/" + i + "/type") != type) continue;
                string id = Mpv.Get(tvMpv, "track-list/" + i + "/id"), lang = Mpv.Get(tvMpv, "track-list/" + i + "/lang"), title = Mpv.Get(tvMpv, "track-list/" + i + "/title");
                bool sel = Mpv.Get(tvMpv, "track-list/" + i + "/selected") == "yes", ext = Mpv.Get(tvMpv, "track-list/" + i + "/external") == "yes";
                // a kept subtitle file ("11588279.en.srt"): its language from the name, and called "saved" (TvSubs.cs)
                var kept = ext ? System.Text.RegularExpressions.Regex.Match(title ?? "", @"^\d+\.([a-z]{2,3}(?:-[a-z]{2})?)\.(srt|ass|ssa|vtt|sub)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) : System.Text.RegularExpressions.Match.Empty;
                if (kept.Success) { if (string.IsNullOrEmpty(lang)) lang = kept.Groups[1].Value; title = null; }
                string name = TvLang(lang); if (!string.IsNullOrEmpty(title) && title != lang) name = (name.Length > 0 ? name + "  –  " : "") + title;
                if (name.Length == 0) name = (kind == "sub" ? "Subtitles " : "Track ") + id; if (ext) name += kept.Success ? "  (saved)" : "  (file)";
                string tid = id, nm = name;
                tracks.Add(Tuple.Create(name, sel, (Action)(() => { Mpv.Set(tvMpv, prop, tid); Mpv.Command(tvMpv, "show-text", (kind == "sub" ? "Subtitles: " : "Audio: ") + nm, "1800"); })));
                any = true; anySel |= sel;
            }
            if (kind == "sub") items.Add(Tuple.Create("Off", !anySel, (Action)(() => { Mpv.Set(tvMpv, "sid", "no"); Mpv.Command(tvMpv, "show-text", "Subtitles off", "1500"); })));
            items.AddRange(tracks);
            // the delay, right at the top: − / + change it live (TvSubs.cs); A on it: sync to a line
            if (kind == "sub" && any) { tvSubTimingRow = 0; items.Insert(0, Tuple.Create("Delay", false, (Action)(() => TvTracks("subtime")))); }
            if (kind == "sub") items.Add(Tuple.Create("Style: " + TvSubStyleText(TvSubStyle()) + "  ·  change...", false, (Action)(() => TvTracks("substyle"))));
            if (kind == "sub") items.Add(Tuple.Create("Load a subtitle file...", false, (Action)(() => {
                TvLeaveForDialog(true);
                var d = new Microsoft.Win32.OpenFileDialog { Filter = "Subtitles|*.srt;*.ass;*.ssa;*.vtt;*.sub|All files|*.*", Title = "A subtitle file" };
                bool ok = d.ShowDialog(W) == true;
                TvLeaveForDialog(false);
                if (ok) { Mpv.Command(tvMpv, "sub-add", d.FileName, "select"); Mpv.Command(tvMpv, "show-text", "Subtitles: " + System.IO.Path.GetFileName(d.FileName), "1800"); } })));
            // online: always offered; without an account it says where to set one up
            if (kind == "sub") { TvPlayerPrefs.Load(); items.Add(Tuple.Create("Find online (OpenSubtitles)...", false, (Action)TvOsSearch)); }
            if (!any && kind == "audio") items.Add(Tuple.Create("Only one audio track", true, (Action)(() => { })));
            if (kind == "audio") TvAudioFxItems(items);   // night mode, louder (TvExtras.cs)
        }
        foreach (var x in items) {
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(new TextBlock { Text = x.Item2 ? "" : "", FontFamily = Ui.Mdl, FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
            var t = new TextBlock { Text = x.Item1, FontSize = 16.5, FontFamily = Ui.Semi, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetColumn(t, 1); g.Children.Add(t);
            var b = new Border { Child = g, Height = 46, Padding = new Thickness(14, 0, 14, 0), CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            int idx = tvTrackRows.Count; var act = x.Item3;
            if (x.Item2) tvTrackIdx = idx;
            b.MouseEnter += (s, e) => { tvTrackIdx = idx; TvMarkTracks(); };
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; Sound.Play(Sfx.Select); tvTrackCard.Visibility = Visibility.Collapsed; act(); TvWake(); SetHints(); };
            tvTrackRows.Add(b); tvTrackActs.Add(act); tvTrackList.Children.Add(b);
        }
        if (kind == "sub" && tvSubTimingRow >= 0) TvSubDelayRow(tvTrackRows[tvSubTimingRow]);
        TvMarkTracks();
        tvTrackCard.Visibility = Visibility.Visible; if (!tvTrackRefresh) Sound.Play(Sfx.Open); tvTrackRefresh = false;
        TvWake(); SetHints();
    }
    // a Windows dialog over a full-screen picture: the controls step aside while it's open
    void TvLeaveForDialog(bool going) {
        if (going) { tvTrackCard.Visibility = Visibility.Collapsed; if (tvOverlay != null) tvOverlay.Hide(); W.Topmost = false; }
        else { TvWake(); if (isFull) W.Topmost = W.IsActive; }
    }
    void TvTrackRowText(int i, string text) {
        if (i < 0 || i >= tvTrackRows.Count) return;
        var t = ((Grid)tvTrackRows[i].Child).Children.OfType<TextBlock>().LastOrDefault(); if (t != null) t.Text = text;
    }
    void TvMarkTracks() {
        for (int i = 0; i < tvTrackRows.Count; i++) {
            bool on = i == tvTrackIdx;
            tvTrackRows[i].Background = on ? Brushes.White : Brushes.Transparent;
            foreach (var t in ((Grid)tvTrackRows[i].Child).Children.OfType<TextBlock>()) t.Foreground = on ? Brushes.Black : Brushes.White;
        }
        // a long menu scrolls so the picked row stays in sight
        if (tvTrackIdx >= 0 && tvTrackIdx < tvTrackRows.Count) tvTrackRows[tvTrackIdx].BringIntoView();
    }
    static string TvLang(string l) {
        switch ((l ?? "").ToLowerInvariant()) {
            case "nl": case "nld": case "dut": return "Nederlands"; case "en": case "eng": return "English"; case "de": case "ger": case "deu": return "Deutsch"; case "fr": case "fre": case "fra": return "Français";
            case "es": case "spa": return "Español"; case "it": case "ita": return "Italiano"; case "pt": case "por": return "Português"; case "tr": case "tur": return "Türkçe"; case "ar": case "ara": return "العربية";
            case "pl": case "pol": return "Polski"; case "ru": case "rus": return "Русский"; case "ja": case "jpn": return "日本語"; case "": case "und": return "";
        }
        return l.ToUpperInvariant();
    }

    // ---------- the controller while watching ----------
    void TvPlayPad(int bit) {
        TvActivity();   // someone is there: the pause screen goes, "still watching?" starts counting again
        // LT / RT: the launcher's own volume (its slider in Windows' mixer; the PC's volume and other apps stay as
        // they are), 5% a press, shown on the picture
        if (bit == Pad.LT || bit == Pad.RT) {
            int v = Audio.AppLevel;
            if (v < 0) { Sound.Play(Sfx.Bump); return; }
            Audio.SetAppLevel(bit == Pad.RT ? (v / 5 + 1) * 5 : Math.Max(0, (v + 4) / 5 - 1) * 5);
            if (tvMpv != IntPtr.Zero) Mpv.Command(tvMpv, "show-text", "Volume " + Audio.AppLevel + "%", "1200");
            return;
        }
        // the next-episode card: A plays it now, B cancels
        if (TvNextCardUp && (bit == Pad.A || bit == Pad.B)) { if (bit == Pad.A) { Sound.Play(Sfx.Select); TvNextByHand(); } else TvNextCancel(); return; }
        bool wasUp = TvControlsUp;
        TvWake();
        if (TvMiniPad(bit)) return;
        if (TvTrackOpen) {
            if (bit == Pad.Up || bit == Pad.Down) { int j = tvTrackIdx + (bit == Pad.Up ? -1 : 1); if (j >= 0 && j < tvTrackRows.Count) { tvTrackIdx = j; Sound.Play(Sfx.Move); TvMarkTracks(); } else Sound.Play(Sfx.Bump); }
            // subtitle timing: left / right a tenth, LB / RB a second, Y back in sync, straight away (the menu stays as it is)
            else if (tvTrackKind == "subtime" && (bit == Pad.Left || bit == Pad.Right || bit == Pad.LB || bit == Pad.RB || bit == Pad.Y)) {
                Sound.Play(Sfx.Move);
                if (bit == Pad.Y) TvSubShift(0, true); else TvSubShift((bit == Pad.Left || bit == Pad.LB ? -1 : 1) * (bit == Pad.LB || bit == Pad.RB ? 1 : 0.1), false);
            }
            // in the Subtitles menu: left / right on the Timing row nudge it there; X opens the timing menu
            // in the Subtitles menu on the Delay row: the same, live
            else if (tvTrackKind == "sub" && tvTrackIdx == tvSubTimingRow && (bit == Pad.Left || bit == Pad.Right || bit == Pad.LB || bit == Pad.RB || bit == Pad.Y)) {
                Sound.Play(Sfx.Move);
                if (bit == Pad.Y) TvSubShift(0, true); else TvSubShift((bit == Pad.Left || bit == Pad.LB ? -1 : 1) * (bit == Pad.LB || bit == Pad.RB ? 1 : 0.1), false);
            }
            else if (tvTrackKind == "sub" && bit == Pad.X) { Sound.Play(Sfx.Select); TvTracks("subtime"); }
            else if (tvTrackKind == "subtime" && bit == Pad.X) { Sound.Play(Sfx.Back); tvTrackCard.Visibility = Visibility.Collapsed; SetHints(); }
            else if (bit == Pad.A && tvTrackIdx < tvTrackActs.Count) { Sound.Play(Sfx.Select); tvTrackCard.Visibility = Visibility.Collapsed; tvTrackActs[tvTrackIdx](); SetHints(); }
            else if (bit == Pad.B) { Sound.Play(Sfx.Back); tvTrackCard.Visibility = Visibility.Collapsed; SetHints(); }
            return;
        }
        bool live = tvPlaying != null && tvPlaying.Kind == "live";
        if (tvPlayIdx >= 0) {
            if (bit == Pad.Left || bit == Pad.Right) {
                int j = tvPlayIdx;
                do { j += bit == Pad.Left ? -1 : 1; } while (j >= 0 && j < tvPlayTools.Count && tvPlayTools[j].Visibility != Visibility.Visible);
                if (j >= 0 && j < tvPlayTools.Count) { tvPlayIdx = j; Sound.Play(Sfx.Move); TvMarkTools(); } else Sound.Play(Sfx.Bump);
            }
            else if (bit == Pad.A) { Sound.Play(Sfx.Select); tvPlayActs[tvPlayIdx](); }
            else if (bit == Pad.Up || bit == Pad.B) { tvPlayIdx = -1; Sound.Play(Sfx.Move); TvMarkTools(); }
            return;
        }
        // live TV: B opens the channel list (B again there leaves), left / right the channel before or after
        if (live && (bit == Pad.B || bit == Pad.Back)) { TvOpenMini(); return; }
        if (live && (bit == Pad.Left || bit == Pad.Right)) { TvZap(bit == Pad.Right ? 1 : -1); return; }
        if (bit == Pad.B) TvStop();
        else if (bit == Pad.A) TvPause();
        else if (bit == Pad.X) { if (live) TvOpenMini(); else TvTracks("sub"); }
        else if (bit == Pad.Y) {
            if (live) TvLastChannel();   // live TV: back to the channel before (TvExtras.cs)
            else if (TvRecapUp) TvSkipRecap();
            else if (tvSkipBtn != null && tvSkipBtn.Visibility == Visibility.Visible) TvSkipIntro(); else TvSeekInfo();
        }
        else if (bit == Pad.Left || bit == Pad.Right) TvSeek(bit == Pad.Left ? -10 : 10);
        else if (bit == Pad.LB || bit == Pad.RB) TvSeek(bit == Pad.LB ? -60 : 60);
        else if (live && (bit == Pad.Up || bit == Pad.Down)) TvZap(bit == Pad.Down ? 1 : -1);
        else if (bit == Pad.Down || bit == Pad.Up) { if (wasUp) { tvPlayIdx = tvPauseIdx; Sound.Play(Sfx.Move); TvMarkTools(); } }
    }
    // Changing channel: the name changes at once with every press, but the channel is only loaded once you stop on one
    // (a moment without a press). Loading every channel passed on the way made mpv start and drop stream after stream,
    // which can leave it stuck; this way zapping quickly through ten channels loads one.
    TvItem tvZapTo; DispatcherTimer tvZapTimer;
    void TvZap(int d) {
        var from = tvZapTo ?? tvPlaying;
        if (tvPlayList == null || from == null) { Sound.Play(Sfx.Bump); return; }
        int i = tvPlayList.FindIndex(x => x.Key == from.Key), j = i + d;
        if (i < 0 || j < 0 || j >= tvPlayList.Count) { Sound.Play(Sfx.Bump); return; }
        tvZapTo = tvPlayList[j];
        Sound.Play(Sfx.Move);
        tvPlayTitle.Text = tvZapTo.Title; tvPlayTime.Text = "Loading...";
        if (tvMpv != IntPtr.Zero) Mpv.Command(tvMpv, "show-text", tvZapTo.Title, "1500");
        if (tvZapTimer == null) {
            tvZapTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            tvZapTimer.Tick += (s, e) => { tvZapTimer.Stop(); var n = tvZapTo; tvZapTo = null; if (n != null && TvPlaying) TvPlay(n, tvPlayList); };
        }
        tvZapTimer.Stop(); tvZapTimer.Start();
    }
    void TvZapCancel() { tvZapTo = null; if (tvZapTimer != null) tvZapTimer.Stop(); }

    // while watching the launcher's hint bar stays away (it would take room from the picture): the controls say it all
    void TvPlayHints(List<string[]> spec) { }
}
}
