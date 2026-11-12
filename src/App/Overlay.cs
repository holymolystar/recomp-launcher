// The in-game menu: while a game started from the launcher runs, the Xbox / PS / Home button (or View and Menu
// together, for when Steam keeps the Xbox button for itself) opens a panel over the game: the game, how long it's been
// played, the clock and battery, and Resume, Go to the launcher, Close the game. B or the same button again resumes.
// It's its own small window, always on top, on the game's screen; it shows over games in a window or borderless full
// screen. Nothing can draw over a game in exclusive full screen (it has the screen to itself), so for those the menu
// steps the game aside: the game is minimised (it leaves exclusive full screen), the menu shows on its own dark
// background, and Resume brings the game back to full screen. The game keeps getting the controller's presses too.
// Close the game asks first (press again), then asks the game to close as its window's X button would; if it's still
// running 5 seconds later, the button becomes Force close.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace RecompLauncher {

partial class Shell {
    Window ov; Border ovPanel; Image ovIcon; TextBlock ovTitle, ovSub, ovClock, ovNote;
    readonly List<Button> ovButtons = new List<Button>();
    int ovIdx; int ovCloseStep; DateTime ovCloseAt; DispatcherTimer ovTick; IntPtr ovGameWindow;
    bool ovExclusive; Grid ovRoot;   // the game was in exclusive full screen and is minimised while the menu shows
    bool OverlayOpen { get { return ov != null && ov.IsVisible; } }

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    // Windows says a Direct3D game has the screen to itself (exclusive full screen): QUNS_RUNNING_D3D_FULL_SCREEN
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    static bool ExclusiveFullScreen() { int st; try { return SHQueryUserNotificationState(out st) == 0 && st == 3; } catch { return false; } }
    // Windows lets only the window in front hand the front to another; borrowing its input queue for a moment does it
    static void BringToFront(IntPtr h) {
        if (h == IntPtr.Zero) return;
        uint fg = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero), me = GetCurrentThreadId();
        bool attached = fg != me && AttachThreadInput(me, fg, true);
        SetForegroundWindow(h);
        if (attached) AttachThreadInput(me, fg, false);
    }

    void BuildOverlay() {
        if (ov != null) return;
        ov = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize, Title = "Recomp Launcher", ShowActivated = true, FontFamily = W.FontFamily, Foreground = Ui.Brush(Ui.Text) };
        var root = ovRoot = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)) };
        root.MouseLeftButtonUp += (s, e) => { if (e.OriginalSource == root) ResumeGame(); };

        ovPanel = new Border { Width = 440, HorizontalAlignment = HorizontalAlignment.Left, Background = Ui.Brush("#11181D"), BorderBrush = Ui.Brush("#26333C"),
            BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(40, 36, 40, 30), RenderTransform = new TranslateTransform(),
            Effect = new DropShadowEffect { BlurRadius = 50, ShadowDepth = 0, Opacity = 0.6, Color = Colors.Black } };
        var dock = new DockPanel();
        // top: the launcher's name and the clock
        var top = new Grid { Margin = new Thickness(0, 0, 0, 34) };
        top.Children.Add(new TextBlock { Text = "RECOMP LAUNCHER", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal), VerticalAlignment = VerticalAlignment.Center });
        ovClock = new TextBlock { FontSize = 16, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        top.Children.Add(ovClock);
        DockPanel.SetDock(top, Dock.Top); dock.Children.Add(top);
        // the game
        var game = new StackPanel { Margin = new Thickness(0, 0, 0, 30) };
        ovIcon = new Image { Width = 120, Height = 120, HorizontalAlignment = HorizontalAlignment.Left, Clip = new RectangleGeometry(new Rect(0, 0, 120, 120), 22, 22) };
        RenderOptions.SetBitmapScalingMode(ovIcon, BitmapScalingMode.HighQuality);
        ovTitle = new TextBlock { FontSize = 26, FontFamily = Ui.Semi, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 18, 0, 4) };
        ovSub = new TextBlock { FontSize = 13.5, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap };
        game.Children.Add(ovIcon); game.Children.Add(ovTitle); game.Children.Add(ovSub);
        DockPanel.SetDock(game, Dock.Top); dock.Children.Add(game);
        // hints at the bottom
        var hints = ovHints = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-14, 20, 0, 0) };
        hints.Children.Add(Hint("A", "Select")); hints.Children.Add(Hint("B", "Resume"));
        DockPanel.SetDock(hints, Dock.Bottom); dock.Children.Add(hints);
        var music = BuildOverlayMusic();   // Spotify's song and Previous / Play / Next (Music.cs)
        DockPanel.SetDock(music, Dock.Bottom); dock.Children.Add(music);
        ovNote = new TextBlock { FontSize = 12, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(ovNote, Dock.Bottom); dock.Children.Add(ovNote);
        // the choices
        var list = ovList = new StackPanel();
        foreach (var spec in new[] { new[] { "", "Resume" }, new[] { "", "Go to the launcher" }, new[] { "", "Close the game" } }) {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = spec[0], FontFamily = Ui.Mdl, FontSize = 18, Width = 34, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = spec[1], FontSize = 16, FontFamily = Ui.Semi, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Style = (Style)W.FindResource("Btn"), Content = row, Height = 58, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(20, 0, 20, 0), Margin = new Thickness(0, 0, 0, 10) };
            int idx = ovButtons.Count;
            b.Click += (s, e) => { ovIdx = idx; OverlayChoose(); };
            ovButtons.Add(b); list.Children.Add(b);
        }
        TextBlock unused;
        list.Children.Add(OvRow("", "Quick settings", ovButtons, out unused));   // volume, sound output, Bluetooth (OverlayPages.cs)
        list.Children.Add(OvRow("", "Power", ovButtons, out unused));            // sleep, restart, shut down
        list.Children.Add(OvRow("", "Take a screenshot", ovButtons, out unused));   // into the Album (Album.cs)
        dock.Children.Add(list);
        ovPanel.Child = dock;
        root.Children.Add(ovPanel);
        ov.Content = root;
        ov.PreviewKeyDown += (s, e) => {
            int bit = e.Key == Key.Up ? Pad.Up : e.Key == Key.Down ? Pad.Down : e.Key == Key.Left ? Pad.Left : e.Key == Key.Right ? Pad.Right : e.Key == Key.Enter || e.Key == Key.Space ? Pad.A : e.Key == Key.Escape ? Pad.B : 0;
            if (bit != 0) { OverlayPad(bit); e.Handled = true; }
        };
        ovTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        ovTick.Tick += (s, e) => OverlayTick();
    }

    // a globe for the menu's picture over the browser
    static ImageSource browserGlyph;
    static ImageSource BrowserGlyph() {
        if (browserGlyph != null) return browserGlyph;
        var dg = new DrawingGroup();
        using (var dc = dg.Open()) {
            dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(0x2f, 0x8f, 0xe0), Color.FromRgb(0x3f, 0xb8, 0xa0), 45), null, new Rect(0, 0, 120, 120), 22, 22);
            var ft = new FormattedText("\uE774", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe MDL2 Assets"), 60, Brushes.White);
            dc.DrawText(ft, new Point(60 - ft.Width / 2, 60 - ft.Height / 2));
        }
        dg.Freeze(); browserGlyph = new DrawingImage(dg); browserGlyph.Freeze();
        return browserGlyph;
    }
    void ToggleOverlay() { if (OverlayOpen) ResumeGame(); else OpenOverlay(); }
    bool ovBrowser;   // the menu is over the browser inside the launcher, not a game
    void OvLabel(int i, string text) { ((TextBlock)((StackPanel)ovButtons[i].Content).Children[1]).Text = text; }
    void OpenOverlay() {
        var g = playing;
        ovBrowser = BrowserViewOpen && padActive;
        if (g == null && !ovBrowser) return;
        BuildOverlay();
        ovTitle.Text = ovBrowser ? "Browser" : g.Name;
        ovIcon.Source = ovBrowser ? BrowserGlyph() : (ImageSource)g.Icon;
        ovIcon.Margin = new Thickness(0);
        ovIdx = 0; ovCloseStep = 0;
        if (ovPage != "main") ShowOvPage("main");
        OvLabel(0, ovBrowser ? "Back to the browser" : "Resume");
        OvLabel(1, ovBrowser ? "Close the browser" : "Go to the launcher");
        OvLabel(2, "Close the game");
        ovButtons[2].Visibility = ovBrowser ? Visibility.Collapsed : Visibility.Visible;
        ovNote.Text = "";
        // on the game's screen (the browser: the launcher's)
        ovGameWindow = ovBrowser ? new WindowInteropHelper(W).Handle : Engine.Processes(g).Select(p => { try { return p.MainWindowHandle; } catch { return IntPtr.Zero; } }).FirstOrDefault(h => h != IntPtr.Zero);
        var screen = ovGameWindow != IntPtr.Zero ? System.Windows.Forms.Screen.FromHandle(ovGameWindow) : System.Windows.Forms.Screen.PrimaryScreen;
        double scale = 1;
        var src = PresentationSource.FromVisual(W);
        if (src != null && src.CompositionTarget != null) scale = src.CompositionTarget.TransformToDevice.M11;
        ov.Left = screen.Bounds.Left / scale; ov.Top = screen.Bounds.Top / scale; ov.Width = screen.Bounds.Width / scale; ov.Height = screen.Bounds.Height / scale;
        // exclusive full screen: the game in front has the screen to itself; minimise it so the menu can show
        // (its screen is worked out above, before: a minimised window has none)
        ovExclusive = !ovBrowser && ExclusiveFullScreen();
        if (ovExclusive) {
            var fg = GetForegroundWindow();
            if (ovGameWindow == IntPtr.Zero) ovGameWindow = fg;
            ShowWindow(fg != IntPtr.Zero ? fg : ovGameWindow, 6);   // SW_MINIMIZE
        }
        ovRoot.Background = ovExclusive ? (Brush)Ui.Brush("#070B0E") : new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0));
        OverlayTick();
        MarkOverlay();
        ov.Show();
        BringToFront(new WindowInteropHelper(ov).Handle);
        ov.Activate();
        Sound.Play(Sfx.Open);
        ((TranslateTransform)ovPanel.RenderTransform).BeginAnimation(TranslateTransform.XProperty, Anim(0, 320, -440));
        ((UIElement)ov.Content).BeginAnimation(UIElement.OpacityProperty, Anim(1, 200, 0));
        ovTick.Start();
    }
    void OverlayTick() {
        var g = playing;
        if (ovBrowser) ovSub.Text = bvTitle != null ? bvTitle.Text : "";
        else if (g == null) return;
        else {
            var played = DateTime.Now - playingSince;
            ovSub.Text = "Playing for " + Playtime.Format(played.TotalSeconds) + "  •  " + Playtime.Format(Playtime.Total(g.Id) + played.TotalSeconds) + " in all";
        }
        string battery = "";
        try {
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            if ((ps.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) == 0 && ps.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.Unknown)
                battery = "   " + Math.Round(ps.BatteryLifePercent * 100) + "%";
        } catch { }
        bool padChg; int pad = PadBatteryPercent(out padChg);
        if (pad >= 0) battery += "   \U0001F3AE " + (padChg ? "charging" : pad + "%");
        ovClock.Text = DateTime.Now.ToString("HH:mm") + battery;
        UpdateOverlayMusic();
        // a close that didn't take: offer to force it
        if (!ovBrowser && ovCloseStep == 2 && (DateTime.Now - ovCloseAt).TotalSeconds > 5 && Engine.IsRunning(g)) {
            ovCloseStep = 3;
            ((TextBlock)((StackPanel)ovButtons[2].Content).Children[1]).Text = "Force close";
            ovNote.Text = g.Name + " didn't close. Force close ends it at once: anything not saved is lost.";
        }
    }
    void MarkOverlay() {
        var rows = OvRows();
        for (int i = 0; i < rows.Count; i++) {
            bool on = i == ovIdx;
            rows[i].Background = on ? Ui.Brush(Ui.Teal) : Ui.Brush("#1C252C");
            rows[i].Foreground = on ? Ui.Brush(Theme.Current.Light ? "#ffffff" : "#0A0E11") : Ui.Brush(Ui.Text);
        }
    }
    void OverlayPad(int bit) {
        if (OverlayMusicPad(bit)) return;
        if (OverlayPagePad(bit)) return;   // Quick settings and Power (OverlayPages.cs)
        if (bit == Pad.B) { ResumeGame(); return; }
        if (bit == Pad.A) { OverlayChoose(); return; }
        int was = ovIdx;
        if (bit == Pad.Up) ovIdx = Math.Max(0, ovIdx - 1);
        else if (bit == Pad.Down) ovIdx = Math.Min(ovButtons.Count - 1, ovIdx + 1);
        else return;
        if (ovBrowser && ovIdx == 2) ovIdx = bit == Pad.Up ? 1 : 3;   // no Close the game over the browser
        Sound.Play(ovIdx != was ? Sfx.Move : Sfx.Bump);
        MarkOverlay();
    }
    void OverlayChoose() {
        var g = playing;
        if (ovPage != "main") { OverlayPageChoose(); return; }
        if (ovIdx == 0) { ResumeGame(); return; }
        if (ovIdx == 3) { Sound.Play(Sfx.Open); ShowOvPage("quick"); return; }
        if (ovIdx == 4) { Sound.Play(Sfx.Open); ShowOvPage("power"); return; }
        if (ovIdx == 5) { TakeScreenshot(); return; }
        if (ovIdx == 1 && ovBrowser) { CloseOverlay(true); CloseBrowserView(); BringToFront(new WindowInteropHelper(W).Handle); W.Activate(); return; }
        if (ovIdx == 1) { CloseOverlay(true); if (inTray) ComeBack(null); else { W.Show(); W.Activate(); BringToFront(new WindowInteropHelper(W).Handle); } return; }
        if (g == null) { CloseOverlay(false); return; }
        var label = (TextBlock)((StackPanel)ovButtons[2].Content).Children[1];
        if (ovCloseStep == 0) {
            ovCloseStep = 1; label.Text = "Press again to close";
            ovNote.Text = "Save first: the game closes as with its own close button.";
            Sound.Play(Sfx.Bump); return;
        }
        if (ovCloseStep == 1) {
            // as the window's X button would: the game can still save or ask
            foreach (var p in Engine.Processes(g)) { try { if (!p.CloseMainWindow()) p.Kill(); } catch { } }
            ovCloseStep = 2; ovCloseAt = DateTime.Now; label.Text = "Closing...";
            ovNote.Text = "";
            Sound.Play(Sfx.Back); return;
        }
        if (ovCloseStep == 3) {
            foreach (var p in Engine.Processes(g)) { try { p.Kill(); } catch { } }
            label.Text = "Closing..."; ovNote.Text = "";
            Sound.Play(Sfx.Back);
        }
    }
    void ResumeGame() {
        if (!OverlayOpen) return;
        CloseOverlay(true);
        Sound.Play(Sfx.Back);
        if (ovBrowser) { BringToFront(new WindowInteropHelper(W).Handle); W.Activate(); FocusBrowserView(); return; }
        // a game stepped aside from exclusive full screen goes back to it
        if (ovGameWindow != IntPtr.Zero && (ovExclusive || IsIconic(ovGameWindow))) ShowWindow(ovGameWindow, 9);   // SW_RESTORE
        BringToFront(ovGameWindow);
    }
    void CloseOverlay(bool animate) {
        if (ov == null || !ov.IsVisible) return;
        ovTick.Stop();
        if (!animate) { ov.Hide(); return; }
        var a = Anim(0, 160, null, EasingMode.EaseIn);
        a.Completed += (s, e) => ov.Hide();
        ((UIElement)ov.Content).BeginAnimation(UIElement.OpacityProperty, a);
    }
}
}
