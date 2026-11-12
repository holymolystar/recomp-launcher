// Small console touches:
//  - Break reminder (Settings > General > Break reminder; off unless turned on): after the chosen time playing, a
//    gentle note in the corner of the screen, again each time that much more has passed.
//  - Screensaver (Settings > General > Screensaver): after a few minutes untouched on Home, the screen dims to a large
//    clock and the date with slowly drifting light; any key, the mouse or the controller wakes it.
//  - Hold to sleep: holding A (or the mouse button) on Home's Power button opens Power with Sleep ready to confirm.
//  - A toast: a small note in the corner of the screen, over games too, gone after a few seconds.
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RecompLauncher {

partial class Shell {
    // ---------- a toast in the corner ----------
    Window toast; TextBlock toastTitle, toastText; DispatcherTimer toastTimer;
    void Toast(string title, string text) {
        if (toast == null) {
            toast = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false,
                ShowActivated = false, Focusable = false, ResizeMode = ResizeMode.NoResize, Width = 400, SizeToContent = SizeToContent.Height, FontFamily = W.FontFamily };
            var sp = new StackPanel();
            toastTitle = new TextBlock { FontSize = 15, FontFamily = Ui.Semi, Foreground = Brushes.White };
            toastText = new TextBlock { FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0xc4, 0xd2, 0xdb)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
            sp.Children.Add(toastTitle); sp.Children.Add(toastText);
            toast.Content = new Border { Margin = new Thickness(16), CornerRadius = new CornerRadius(16), Padding = new Thickness(20, 14, 20, 16), Child = sp,
                Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x11, 0x18, 0x1D)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0xB8, 0xA0)), BorderThickness = new Thickness(0, 0, 0, 3),
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 2, Opacity = 0.5, Color = Colors.Black } };
            toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
            toastTimer.Tick += (s, e) => { toastTimer.Stop(); var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)); a.Completed += (s2, e2) => toast.Hide(); toast.BeginAnimation(UIElement.OpacityProperty, a); };
        }
        toastTitle.Text = title; toastText.Text = text;
        var area = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
        double scale = 1; var src = PresentationSource.FromVisual(W); if (src != null && src.CompositionTarget != null) scale = src.CompositionTarget.TransformToDevice.M11;
        toast.Show(); toast.UpdateLayout();
        toast.Left = area.Right / scale - toast.ActualWidth; toast.Top = area.Bottom / scale - toast.ActualHeight;
        toast.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
        Sound.Play(Sfx.Open);
        toastTimer.Stop(); toastTimer.Start();
    }

    // ---------- the break reminder ----------
    DispatcherTimer breakTimer; int breaksSaid;
    static int BreakMinutes() { int m; return int.TryParse(Settings.Get("BreakAfter"), out m) && m > 0 ? m : 0; }   // 0: off (the default)
    void StartBreakWatch() {
        breaksSaid = 0;
        if (breakTimer == null) {
            breakTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            breakTimer.Tick += (s, e) => {
                int m = BreakMinutes();
                if (playing == null || m == 0) { breakTimer.Stop(); return; }
                double played = (DateTime.Now - playingSince).TotalMinutes;
                if (played >= m * (breaksSaid + 1)) {
                    breaksSaid++;
                    Toast("Time for a break?", "You've been playing " + playing.Name + " for " + Playtime.Format(played * 60) + ". Stretch, have a drink, rest your eyes.");
                }
            };
        }
        if (BreakMinutes() > 0) breakTimer.Start();
    }

    // ---------- the screensaver ----------
    [StructLayout(LayoutKind.Sequential)] struct LastInput { public int Size; public uint Time; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput li);
    static double IdleSeconds() {
        var li = new LastInput { Size = Marshal.SizeOf(typeof(LastInput)) };
        return GetLastInputInfo(ref li) ? (Environment.TickCount - (int)li.Time) / 1000.0 : 0;
    }
    DateTime lastPadInput = DateTime.Now; Grid saver; TextBlock saverClock, saverDate; DispatcherTimer saverTimer; Ellipse[] saverLights;
    bool SaverOn { get { return saver != null && saver.Visibility == Visibility.Visible; } }
    static int SaverMinutes() { string v = Settings.Get("Screensaver"); int m; return v == "off" ? 0 : int.TryParse(v, out m) && m > 0 ? m : 5; }
    void StartSaverWatch() {
        saverTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        saverTimer.Tick += (s, e) => {
            int m = SaverMinutes();
            if (SaverOn) { saverClock.Text = DateTime.Now.ToString("HH:mm"); saverDate.Text = DateTime.Now.ToString("dddd d MMMM"); return; }
            if (m == 0 || playing != null || !W.IsActive || W.WindowState == WindowState.Minimized || view != View.Home || AnyDialog() || BrowserViewOpen || PlazaOpen || CreatorOpen) return;
            double idle = Math.Min(IdleSeconds(), (DateTime.Now - lastPadInput).TotalSeconds);
            if (idle >= m * 60) ShowSaver();
        };
        saverTimer.Start();
        W.PreviewMouseMove += (s, e) => { if (SaverOn) HideSaver(); };
        W.PreviewMouseDown += (s, e) => { if (SaverOn) { HideSaver(); e.Handled = true; } };
        W.PreviewKeyDown += (s, e) => { if (SaverOn) { HideSaver(); e.Handled = true; } };
    }
    void ShowSaver() {
        if (saver == null) {
            saver = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xEE, 4, 6, 8)), Visibility = Visibility.Collapsed, ClipToBounds = true };
            Panel.SetZIndex(saver, 9000);
            var lights = new Canvas();
            saverLights = new Ellipse[3];
            string[] cols = { "#3FB8A0", "#2f8fe0", "#D9B45A" };
            for (int i = 0; i < 3; i++) {
                var c = (Color)ColorConverter.ConvertFromString(cols[i]);
                saverLights[i] = new Ellipse { Width = 700, Height = 700, Opacity = 0.22, Fill = new RadialGradientBrush(c, Color.FromArgb(0, c.R, c.G, c.B)), RenderTransform = new TranslateTransform() };
                lights.Children.Add(saverLights[i]);
            }
            saver.Children.Add(lights);
            var mid = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            saverClock = new TextBlock { FontSize = 110, FontFamily = Ui.Semi, Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center };
            saverDate = new TextBlock { FontSize = 22, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center };
            mid.Children.Add(saverClock); mid.Children.Add(saverDate);
            saver.Children.Add(mid);
            saver.LayoutTransform = N<Grid>("Main").LayoutTransform;
            N<Grid>("Clipped").Children.Add(saver);
        }
        saverClock.Text = DateTime.Now.ToString("HH:mm"); saverDate.Text = DateTime.Now.ToString("dddd d MMMM");
        saver.Visibility = Visibility.Visible;
        saver.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(2)));
        // the lights drift slowly, each its own way
        var r = new Random();
        for (int i = 0; i < saverLights.Length; i++) {
            var t = (TranslateTransform)saverLights[i].RenderTransform;
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-300 + r.Next(200), 900 + r.Next(300), TimeSpan.FromSeconds(40 + i * 13)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-250 + r.Next(150), 300 + r.Next(250), TimeSpan.FromSeconds(31 + i * 9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        }
        StopDrift();
    }
    void HideSaver() {
        if (!SaverOn) return;
        lastPadInput = DateTime.Now;
        foreach (var l in saverLights) { var t = (TranslateTransform)l.RenderTransform; t.BeginAnimation(TranslateTransform.XProperty, null); t.BeginAnimation(TranslateTransform.YProperty, null); }
        saver.Visibility = Visibility.Collapsed;
        if (W.IsActive) StartDrift();
    }

    // ---------- hold Power to sleep ----------
    DateTime powerHeldSince = DateTime.MinValue;
    // from Home's Power button: held for a moment, Sleep is picked and asks for one more press
    void PowerHeld() {
        if (!PowerOpen) OpenPower();
        pwIdx = 1; PowerChoose();   // the first press of Sleep: "Press again to sleep"
    }
}
}
