// TV & Movies: the pause screen, as on a streaming service.
//   Paused and nothing touched for a few seconds: the controls go, the picture dims, and "You're watching" shows the
//   title (or its logo), the episode and the story. Anything (a key, the controller, the mouse) puts it away again.
//   Are you still watching?   after three episodes in a row that started by themselves with nobody touching anything,
//   the next one waits, paused, behind the same screen with that question; A (or a click on play) carries on.
// It lives in the controls' see-through window (TvPlayer.cs), under the controls, and never takes a click.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace RecompLauncher {

partial class Shell {
    Grid tvPauseScreen; TextBlock tvPsKicker, tvPsTitle, tvPsMeta, tvPsPlot, tvPsHint; Image tvPsLogo;
    DateTime tvLastActivity = DateTime.Now; Point tvPsMouse = new Point(-100, -100);
    int tvAutoNexts;        // episodes in a row that started by themselves
    bool tvAskStill;        // the next one waits for "still watching?"
    const double TvPauseAfter = 6;   // seconds
    bool TvPauseUp { get { return tvPauseScreen != null && tvPauseScreen.Visibility == Visibility.Visible; } }

    // the controls' window content: the pause screen under the controls
    UIElement TvWrapOverlay(UIElement controls) {
        tvPauseScreen = new Grid { Visibility = Visibility.Collapsed, Opacity = 0, IsHitTestVisible = false, LayoutTransform = N<Grid>("Main").LayoutTransform };
        tvPauseScreen.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(0xE6, 0, 0, 0), Color.FromArgb(0x66, 0, 0, 0), 0) });
        tvPauseScreen.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0xB3, 0, 0, 0), 90) });
        var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(96, 0, 0, 0), MaxWidth = 760 };
        tvPsKicker = new TextBlock { FontSize = 19, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#B3FFFFFF"), Margin = new Thickness(0, 0, 0, 12) };
        tvPsLogo = new Image { MaxHeight = 140, MaxWidth = 520, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6) };
        RenderOptions.SetBitmapScalingMode(tvPsLogo, BitmapScalingMode.HighQuality);
        tvPsTitle = new TextBlock { FontSize = 58, FontFamily = TvFont, FontWeight = FontWeights.Black, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, LineHeight = 62, MaxHeight = 130, TextTrimming = TextTrimming.CharacterEllipsis };
        tvPsMeta = new TextBlock { FontSize = 20, FontFamily = Ui.Semi, Foreground = Brushes.White, Margin = new Thickness(0, 14, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        tvPsPlot = new TextBlock { FontSize = 18, Foreground = TvTheme.Brush("#D9FFFFFF"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), MaxHeight = 110, TextTrimming = TextTrimming.WordEllipsis, LineHeight = 26 };
        tvPsHint = new TextBlock { FontSize = 16, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#99FFFFFF"), Margin = new Thickness(0, 30, 0, 0) };
        foreach (var e in new UIElement[] { tvPsKicker, tvPsLogo, tvPsTitle, tvPsMeta, tvPsPlot, tvPsHint }) col.Children.Add(e);
        tvPauseScreen.Children.Add(col);
        var all = new Grid();
        all.Children.Add(tvPauseScreen); all.Children.Add(controls);
        return all;
    }

    // a key, the controller or the mouse: someone is there
    void TvActivity() {
        tvLastActivity = DateTime.Now; tvAutoNexts = 0;
        if (TvPauseUp) TvPauseScreen(false, false);
    }
    // twice a second from TvTick
    void TvPauseTick(bool paused) {
        if (tvPauseScreen == null) return;
        if (paused && tvStarted && !TvTrackOpen && (DateTime.Now - tvLastActivity).TotalSeconds > TvPauseAfter) { if (!TvPauseUp) TvPauseScreen(true, false); }
        else if (!paused && TvPauseUp) TvPauseScreen(false, false);
    }
    // the end of an episode that goes on by itself: the third one in a row asks first
    bool TvAutoNextOk() {
        if (++tvAutoNexts >= 3) { tvAskStill = true; tvAutoNexts = 0; }
        return true;
    }
    // just started: paused behind "still watching?" when that was asked for
    void TvStartedNow() {
        if (!tvAskStill || tvMpv == IntPtr.Zero) return;
        tvAskStill = false;
        Mpv.Set(tvMpv, "pause", "yes");
        tvLastActivity = DateTime.MinValue;
        TvPauseScreen(true, true);
    }

    void TvPauseScreen(bool on, bool still) {
        if (tvPauseScreen == null) return;
        if (!on) {
            if (!TvPauseUp) return;
            var a = Anim(0, 220); a.Completed += (s, e) => { if (tvPauseScreen.Opacity < 0.05) tvPauseScreen.Visibility = Visibility.Collapsed; };
            tvPauseScreen.BeginAnimation(UIElement.OpacityProperty, a);
            return;
        }
        var it = tvPlaying; if (it == null) return;
        bool ep = it.Kind == "episode" || (it.Kind == "file" && it.Season > 0);
        tvPsKicker.Text = still ? "Are you still watching?" : "You're watching";
        tvPsKicker.FontSize = still ? 30 : 19; tvPsKicker.FontFamily = still ? TvFont : Ui.Semi; tvPsKicker.FontWeight = still ? FontWeights.Bold : FontWeights.Normal;
        tvPsKicker.Foreground = still ? Brushes.White : TvTheme.Brush("#B3FFFFFF");
        tvPsTitle.Text = ep && !string.IsNullOrEmpty(it.Group) ? it.Group : it.Title;
        tvPsMeta.Text = ep ? (it.Season > 0 ? "S" + it.Season + " E" + it.Episode + "   " : "") + (it.Title ?? "")
            : string.Join("   ·   ", new[] { it.Year, it.Duration, it.Kind == "live" ? "Live" : null }.Where2());
        tvPsPlot.Text = it.Plot ?? "";
        tvPsHint.Text = still ? "Press A to carry on watching, B to stop" : "";
        tvPsLogo.Source = null; tvPsLogo.Visibility = Visibility.Collapsed; tvPsTitle.Visibility = Visibility.Visible;
        if (!ep && !string.IsNullOrEmpty(it.Logo)) { var lg = it; TvLoadImage(tvPsLogo, it.Logo, 700, () => { if (tvPlaying == lg) { tvPsLogo.Visibility = Visibility.Visible; tvPsTitle.Visibility = Visibility.Collapsed; } }); }
        // the controls go; the window they live in stays for this
        TvSleep();
        if (!tvOverlay.IsVisible) { TvPlaceOverlay(); tvOverlay.Show(); }
        tvPauseScreen.Visibility = Visibility.Visible;
        tvPauseScreen.BeginAnimation(UIElement.OpacityProperty, TvCalm ? null : Anim(1, 700, 0));
        if (TvCalm) tvPauseScreen.Opacity = 1;
    }
}

static class TvPauseExt {
    public static string[] Where2(this string[] l) { return System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(l, x => !string.IsNullOrEmpty(x))); }
}
}
