// TV & Movies: the finishing touches, as on a streaming service.
//   Badges       a provider's film added in the last two weeks says "Recently added"; a series with episodes added
//                in that time "New episodes". A small red ribbon at the top of the poster, made once with the tile.
//   Opening      TV & Movies opens with its mark growing in the middle and fading away, then "Who's watching?".
//                Picking a profile: the profiles grow towards you and fade while its page comes in.
//   Screensaver  after three minutes of nothing on a page (not while watching, not in a menu) the rows and the bar
//                fade away and the big picture shows something else every 14 seconds, drifting slowly. Anything (a
//                key, the controller, the mouse) brings the page back as it was.
// Settings > Motion > Reduced (or a slow graphics card, TvCalm): no opening, no zoom; the screensaver still changes
// pictures, without the drift.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace RecompLauncher {

partial class Shell {
    // ---------- badges ----------
    static Border TvBadge(TvItem it) {
        if (it == null || it.Added <= 0 || it.Account == null) return null;
        double days = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - it.Added) / 86400.0;
        if (days < 0 || days > 14) return null;
        string text = it.Kind == "series" ? "NEW EPISODES" : it.Kind == "movie" ? "RECENTLY ADDED" : null;
        if (text == null) return null;
        return new Border {
            Background = TvTheme.Brush(TvAccent), CornerRadius = new CornerRadius(0, 0, 6, 6), Padding = new Thickness(8, 3, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
            Child = new TextBlock { Text = text, FontSize = 10.5, FontFamily = Ui.Semi, FontWeight = FontWeights.Bold, Foreground = Brushes.White }
        };
    }

    // ---------- opening ----------
    Grid tvIntro;
    void TvIntro() {
        if (TvCalm || tvLayer == null) return;
        if (tvIntro == null) {
            tvIntro = new Grid { Background = Brushes.Black, IsHitTestVisible = false };
            var mark = TvMark(120); mark.RenderTransformOrigin = new Point(0.5, 0.5); mark.RenderTransform = new ScaleTransform(1, 1);
            mark.HorizontalAlignment = HorizontalAlignment.Center; mark.VerticalAlignment = VerticalAlignment.Center;
            tvIntro.Children.Add(mark);
            Grid.SetColumnSpan(tvIntro, 2); Panel.SetZIndex(tvIntro, 999);
            tvLayer.Children.Add(tvIntro);
        }
        var m = (Border)tvIntro.Children[0]; var st = (ScaleTransform)m.RenderTransform;
        tvIntro.Visibility = Visibility.Visible; tvIntro.BeginAnimation(UIElement.OpacityProperty, null); tvIntro.Opacity = 1;
        m.BeginAnimation(UIElement.OpacityProperty, Anim(1, 260, 0));
        st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1.12, 900, 0.7)); st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1.12, 900, 0.7));
        var fade = Anim(0, 380); fade.BeginTime = TimeSpan.FromMilliseconds(650);
        fade.Completed += (s, e) => { if (tvIntro.Opacity < 0.05) tvIntro.Visibility = Visibility.Collapsed; };
        tvIntro.BeginAnimation(UIElement.OpacityProperty, fade);
    }
    // the profiles grow towards you and fade; the page comes in under them
    bool tvProfLeaving;
    void TvProfilesAway() {
        if (tvProfiles == null) return;
        if (TvCalm) { tvProfiles.Visibility = Visibility.Collapsed; return; }
        var st = tvProfiles.RenderTransform as ScaleTransform; if (st == null) { st = new ScaleTransform(1, 1); tvProfiles.RenderTransform = st; tvProfiles.RenderTransformOrigin = new Point(0.5, 0.45); }
        st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1.08, 320, 1)); st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1.08, 320, 1));
        tvProfLeaving = true;   // presses go to the page now, not to the fading profiles
        var a = Anim(0, 320, 1); a.Completed += (s, e) => {
            if (!tvProfLeaving) return;   // opened again meanwhile
            tvProfLeaving = false;
            tvProfiles.Visibility = Visibility.Collapsed;
            tvProfiles.BeginAnimation(UIElement.OpacityProperty, null); tvProfiles.Opacity = 1;
            st.BeginAnimation(ScaleTransform.ScaleXProperty, null); st.BeginAnimation(ScaleTransform.ScaleYProperty, null); st.ScaleX = st.ScaleY = 1;
        };
        tvProfiles.BeginAnimation(UIElement.OpacityProperty, a);
    }

    // ---------- screensaver ----------
    DispatcherTimer tvIdleTimer, tvSaverTimer; DateTime tvLastInput = DateTime.Now; bool tvSaverOn; int tvSaverIdx; TvItem tvSaverWas;
    const double TvIdleMinutes = 3;
    void TvIdleStart() {
        if (tvIdleTimer != null) return;
        tvIdleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        tvIdleTimer.Tick += (s, e) => { if (!tvSaverOn && TvSaverAllowed() && (DateTime.Now - tvLastInput).TotalMinutes >= TvIdleMinutes) TvSaver(true); };
        tvIdleTimer.Start();
        tvLayer.PreviewMouseMove += (s, e) => { var p = e.GetPosition(tvLayer); if (Math.Abs(p.X - tvSaverMouse.X) + Math.Abs(p.Y - tvSaverMouse.Y) > 4) { tvSaverMouse = p; TvInput(); } };
        tvLayer.PreviewMouseDown += (s, e) => TvInput();
        tvLayer.PreviewMouseWheel += (s, e) => TvInput();
    }
    Point tvSaverMouse = new Point(-100, -100);
    // something was pressed or moved: the screensaver goes; true when it was showing (the press only wakes it)
    bool TvInput() {
        tvLastInput = DateTime.Now;
        if (!tvSaverOn) return false;
        TvSaver(false); return true;
    }
    bool TvSaverAllowed() {
        return TvOpen && !TvPlaying && !TvProfilesOpen && !TvDetailOpen && !TvSearchOpen && !TvGuideOpen && !TvLiveOpen && !ListPageOpen && W.IsActive && tvRowList.Count > 0;
    }
    // what the screensaver shows: the page's films and series with a wide picture
    List<TvItem> TvSaverItems() {
        return tvRowList.SelectMany(r => r.Items).Where(x => !string.IsNullOrEmpty(x.Backdrop) && (x.Kind == "movie" || x.Kind == "series")).GroupBy(x => x.Key).Select(g => g.First()).Take(60).ToList();
    }
    void TvSaver(bool on) {
        var fadeOut = new UIElement[] { tvScroll, tvBar };
        if (on) {
            var items = TvSaverItems(); if (items.Count == 0) { tvLastInput = DateTime.Now; return; }
            tvSaverOn = true; tvSaverWas = tvFocus; TvPeekHide();
            // the words of the preview and the rows go; only the picture and its glow stay
            foreach (var u in fadeOut) u.BeginAnimation(UIElement.OpacityProperty, Anim(0, 900));
            if (tvSaverTimer == null) { tvSaverTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(14) }; tvSaverTimer.Tick += (s, e) => TvSaverNext(); }
            tvSaverIdx = new Random().Next(items.Count);
            TvSaverNext(); tvSaverTimer.Start();
        } else {
            tvSaverOn = false; if (tvSaverTimer != null) tvSaverTimer.Stop();
            foreach (var u in fadeOut) u.BeginAnimation(UIElement.OpacityProperty, Anim(1, 260));
            tvFocus = tvSaverWas ?? tvFocus; ShowTvHero(tvFocus); MarkTv();
        }
    }
    void TvSaverNext() {
        if (!tvSaverOn) return;
        if (!TvSaverAllowed()) { TvSaver(false); return; }
        var items = TvSaverItems(); if (items.Count == 0) return;
        tvSaverIdx = (tvSaverIdx + 1) % items.Count;
        var it = items[tvSaverIdx];
        tvFocus = it; ShowTvHero(it);   // its picture, colour glow and slow drift (TvMotion.cs)
    }
}
}
