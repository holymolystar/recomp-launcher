// Home view: a console-style home screen. A flat background in the theme's colours; your picture top left, the
// clock, Wi-Fi and battery top right; one big row of square game tiles (the picked game's name above it, a pulsing
// frame around it, empty slots after the last game); a row of round buttons under it; a line above the bottom bar.
// Left / right move along the row, down goes to the round buttons and up comes back; A / Enter plays (or installs)
// the game or presses the button. Clicking the picked tile again plays it.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RecompLauncher {

class HomeTileUi { public Grid Root; public Border Sel, Face, Shade, Badge; public TextBlock Name, BadgeText; public Image Icon; }
class HomeButton { public Border Circle; public TextBlock Glyph, Label; public string Text; public Action Do; }

partial class Shell {
    Grid homeLayer; StackPanel homeTiles; ScrollViewer homeScroll; TextBlock homeClock, homeUpdates, homeBattery; Border homeUpdatesPill;
    readonly List<HomeButton> homeButtons = new List<HomeButton>();
    string homeZone = "tiles"; int homeIdx;
    DispatcherTimer homeClockTimer;
    const double HomeTileSize = 244, AllTileSize = 168;
    // the All games page
    bool homeAll { get { return homePage == "all"; } }
    Grid homeAllPage; WrapPanel homeAllTiles; ScrollViewer homeAllScroll; StackPanel homeButtonRow, homeFilterPills; TextBlock homeAllName, homeAllState, homeAllCount;
    // raised surfaces (round buttons, empty slots): white on a light theme, a lighter shade on a dark one
    static string Raised { get { return Theme.Current.Light ? "#ffffff" : "#1C252C"; } }   // lower case: not recoloured
    static string Slot { get { return Theme.Current.Light ? "#f7f7f7" : "#11181D"; } }

    void BuildHome() {
        if (homeLayer != null) return;
        homeLayer = new Grid();
        Grid.SetRowSpan(homeLayer, 4);
        foreach (var h in new[] { new GridLength(84), new GridLength(0.55, GridUnitType.Star), GridLength.Auto, new GridLength(46), GridLength.Auto, new GridLength(1, GridUnitType.Star), new GridLength(62) })
            homeLayer.RowDefinitions.Add(new RowDefinition { Height = h });

        // top left: the launcher's picture in a round frame
        var avatar = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(24), BorderThickness = new Thickness(3), BorderBrush = Ui.Brush(Raised),
            Background = Ui.Brush(Raised), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(36, 10, 0, 0),
            Child = new Image { Source = N<Image>("LogoImg").Source }, ToolTip = "Recomp Launcher", Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.25 } };
        homeLayer.Children.Add(avatar);
        // top right: updates waiting, clock, Wi-Fi, battery (laptops)
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 10, 112, 0) };
        homeUpdates = new TextBlock { FontSize = 12.5, FontFamily = Ui.Semi, Foreground = Ui.Brush("#0A0E11"), VerticalAlignment = VerticalAlignment.Center };
        homeUpdatesPill = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(11, 3, 11, 4), Background = Ui.Brush(Ui.Gold), Margin = new Thickness(0, 0, 20, 0),
            Child = homeUpdates, Cursor = Cursors.Hand, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Show the games with updates" };
        homeUpdatesPill.MouseLeftButtonUp += (s, e) => OpenHomeAll("updates");
        homeClock = new TextBlock { FontSize = 19, Foreground = Ui.Brush(Ui.Text), VerticalAlignment = VerticalAlignment.Center };
        homeBattery = new TextBlock { FontFamily = Ui.Mdl, FontSize = 17, Foreground = Ui.Brush(Ui.Text), Margin = new Thickness(14, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(homeUpdatesPill);
        right.Children.Add(homeClock);
        right.Children.Add(new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 17, Foreground = Ui.Brush(Ui.Text), Margin = new Thickness(16, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        right.Children.Add(homeBattery);
        homeLayer.Children.Add(right);
        homeClockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        homeClockTimer.Tick += (s, e) => TickHomeClock();
        TickHomeClock();

        // the row of games
        homeTiles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(60, 0, 60, 0) };
        homeScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Content = homeTiles };
        Grid.SetRow(homeScroll, 2);
        homeLayer.Children.Add(homeScroll);

        // the round buttons, each icon in its own colour
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        homeButtonRow = row;
        Grid.SetRow(row, 4);
        AddHomeButton(row, "\uE946", "Game page", "#2f8fe0", () => OpenGamePage());
        AddHomeButton(row, "", "What's new", "#e8453c", () => OpenNotes("notes"));
        AddHomeButton(row, "", "Check for updates", "#f4a312", () => {
            int n = Listed().Count(g => g.State == State.Update);
            if (n > 0 && !running) TryAction("Updating", () => StartQueue(Listed().Where(g => g.State == State.Update).ToList()));
            else if (!running && !checking) CheckAll(true);
        });
        AddHomeButton(row, "", "All games", "#2f8fe0", () => OpenHomeAll(null));
        AddHomeButton(row, "", "Full screen", "#7d8a93", () => ToggleFullscreen());
        AddHomeButton(row, "", "Settings", "#7d8a93", () => { if (!running) OpenSettings(); });
        AddHomeButton(row, "", "Close", "#7d8a93", () => W.Close());
        homeLayer.Children.Add(row);

        // the bottom bar: a line across, a console glyph on the left (the button hints sit on the right)
        var line = new Border { Height = 1, Background = Ui.Brush("#33424D"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(26, 0, 26, 0) };
        Grid.SetRow(line, 6);
        homeLayer.Children.Add(line);
        var console = new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 22, Foreground = Ui.Brush(Ui.Text), Margin = new Thickness(40, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        Grid.SetRow(console, 6);
        homeLayer.Children.Add(console);

        BuildHomeAll();
        BuildGamePage();
        homeLayer.Visibility = Visibility.Collapsed;
        N<Grid>("Main").Children.Insert(0, homeLayer);
        foreach (var g in Games) BuildHomeTile(g);
    }
    void TickHomeClock() {
        homeClock.Text = DateTime.Now.ToString("HH:mm");
        try {
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            if ((ps.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) != 0 || ps.BatteryChargeStatus == System.Windows.Forms.BatteryChargeStatus.Unknown) { homeBattery.Visibility = Visibility.Collapsed; return; }
            int level = (int)Math.Round(Math.Max(0, Math.Min(1, ps.BatteryLifePercent)) * 10);
            bool charging = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
            homeBattery.Text = ((char)((charging ? 0xE85A : 0xE850) + Math.Min(level, 9))).ToString();
            if (level >= 10 && !charging) homeBattery.Text = "";
            homeBattery.Visibility = Visibility.Visible;
        } catch { homeBattery.Visibility = Visibility.Collapsed; }
    }
    void AddHomeButton(Panel row, string glyph, string text, string color, Action action) {
        var hb = new HomeButton { Text = text, Do = action };
        hb.Glyph = new TextBlock { Text = glyph, FontFamily = Ui.Mdl, FontSize = 21, Foreground = Ui.Brush(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        hb.Circle = new Border { Width = 60, Height = 60, CornerRadius = new CornerRadius(30), Background = Ui.Brush(Raised), BorderThickness = new Thickness(3),
            BorderBrush = Brushes.Transparent, Child = hb.Glyph, Cursor = Cursors.Hand, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1),
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1.5, Opacity = Theme.Current.Light ? 0.18 : 0.35, Color = Colors.Black } };
        // the name shows under the picked (or pointed at) button
        hb.Label = new TextBlock { Text = text, FontSize = 14, Foreground = Ui.Brush(Ui.Teal), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(-40, 10, -40, 0), Opacity = 0 };
        var stack = new StackPanel { Width = 74, Margin = new Thickness(6, 0, 6, 0) };
        stack.Children.Add(hb.Circle); stack.Children.Add(hb.Label);
        int idx = homeButtons.Count;
        hb.Circle.MouseEnter += (s, e) => { if (!(homeZone == "buttons" && homeIdx == idx)) ShowButtonLabel(hb, true); };
        hb.Circle.MouseLeave += (s, e) => { if (!(homeZone == "buttons" && homeIdx == idx)) ShowButtonLabel(hb, false); };
        hb.Circle.MouseLeftButtonDown += (s, e) => { var st = (ScaleTransform)hb.Circle.RenderTransform; st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(0.9, 90)); st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(0.9, 90)); };
        hb.Circle.MouseLeftButtonUp += (s, e) => { var st = (ScaleTransform)hb.Circle.RenderTransform; st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1, 160)); st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1, 160)); hb.Do(); };
        homeButtons.Add(hb);
        row.Children.Add(stack);
    }
    void ShowButtonLabel(HomeButton hb, bool on) { hb.Label.BeginAnimation(UIElement.OpacityProperty, Anim(on ? 1 : 0, on ? 120 : 200)); }

    void BuildHomeTile(Game g) {
        var u = (GameUi)g.Ui;
        u.Home = MakeHomeTile(g, HomeTileSize, true);
        u.HomeAll = MakeHomeTile(g, AllTileSize, false);
        u.HomeSmall = MakeHomeTile(g, SmallTileSize, false);
        u.HomeSmall.Root.Margin = new Thickness(0, 0, 12, 14);
        u.HomeRow = MakeHomeRow(g);
    }
    HomeTileUi MakeHomeTile(Game g, double size, bool named) {
        var h = new HomeTileUi();
        // the name sits above the picked tile and may run past its right edge
        h.Name = new TextBlock { Text = g.Name, FontSize = 17, Foreground = Ui.Brush(Ui.Teal), Opacity = 0, IsHitTestVisible = false };
        var nameHost = new Canvas { Height = named ? 36 : 0 };
        if (!named) h.Name.Visibility = Visibility.Collapsed;   // the All games page names the picked game in its header
        Canvas.SetLeft(h.Name, 4); Canvas.SetTop(h.Name, 2);
        nameHost.Children.Add(h.Name);
        var box = new Grid { Width = size, Height = size };
        // the frame: a coloured line with a thin gap of background between it and the tile
        h.Sel = new Border { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(5), BorderBrush = Ui.Brush(Ui.Teal), Margin = new Thickness(-9), Opacity = 0, IsHitTestVisible = false };
        h.Icon = new Image { Source = (ImageSource)g.Icon, Margin = new Thickness(g.IconIsArt ? 0 : size * 0.24), Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(h.Icon, BitmapScalingMode.HighQuality);
        h.Face = new Border { CornerRadius = new CornerRadius(2), ClipToBounds = true, Background = Ui.Gradient(g.Accent, g.Accent2), Child = h.Icon,
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = Theme.Current.Light ? 0.2 : 0.4, Color = Colors.Black } };
        h.Shade = new Border { CornerRadius = new CornerRadius(2), Background = Ui.Brush("#090C0F"), Opacity = 0, IsHitTestVisible = false };
        h.BadgeText = new TextBlock { FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Ui.Brush("#0A0E11") };
        h.Badge = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(7, 2, 7, 3), Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, Background = Ui.Brush(Ui.Gold), Child = h.BadgeText, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        // the game's name on the tile itself, over a dark fade at the bottom
        double fs = Math.Max(10.5, Math.Round(size * 0.07, 1));
        var capText = new TextBlock { Text = g.Name, FontSize = fs, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = fs * 2.7, LineHeight = fs * 1.25,
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.7, Color = Colors.Black } };
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0xB8, 0, 0, 0), 0.55));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0xD0, 0, 0, 0), 1));
        var caption = new Border { VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(0, 0, 2, 2), Background = fade,
            Padding = new Thickness(size * 0.05, fs * 1.6, size * 0.05, size * 0.045), Child = capText, IsHitTestVisible = false };
        box.Children.Add(h.Sel); box.Children.Add(h.Face); box.Children.Add(caption); box.Children.Add(h.Shade); box.Children.Add(h.Badge);
        var stack = new StackPanel();
        stack.Children.Add(nameHost); stack.Children.Add(box);
        h.Root = new Grid { Margin = named ? new Thickness(0, 0, 9, 14) : new Thickness(0, 0, 16, 18), Cursor = Cursors.Hand, Background = Brushes.Transparent, Tag = g };
        h.Root.Children.Add(stack);
        h.Root.MouseLeftButtonUp += (s, e) => {
            padUsed = false;
            homeZone = "tiles"; MarkHomeButtons();
            if (g == sel) PrimaryAction(g); else ShowGame(g);
        };
        h.Root.MouseRightButtonUp += (s, e) => { ShowGame(g); OpenGamePage(); e.Handled = true; };
        h.Root.MouseEnter += (s, e) => { if (g != sel) h.Shade.BeginAnimation(UIElement.OpacityProperty, Anim(ShadeFor(g) * 0.4, 150)); };
        h.Root.MouseLeave += (s, e) => { if (g != sel) h.Shade.BeginAnimation(UIElement.OpacityProperty, Anim(ShadeFor(g), 220)); };
        return h;
    }
    // games that aren't installed are shown a little faded
    static double ShadeFor(Game g) { return g.Inst == null ? (Theme.Current.Light ? 0.35 : 0.45) : 0; }
    // empty slots after the last game, as on a console
    void AddHomeSlots() {
        for (int i = 0; i < 3; i++)
            homeTiles.Children.Add(new Border { Width = HomeTileSize, Height = HomeTileSize, Margin = new Thickness(0, 36, 9, 14), CornerRadius = new CornerRadius(2),
                Background = Ui.Brush(Slot), Opacity = 0.75, IsHitTestVisible = false });
    }

    void UpdateHomeTile(Game g) {
        foreach (var h in new[] { ((GameUi)g.Ui).Home, ((GameUi)g.Ui).HomeAll, ((GameUi)g.Ui).HomeSmall }) UpdateHomeTile(g, h);
        UpdateHomeRow(g);
    }
    void UpdateHomeTile(Game g, HomeTileUi h) {
        if (h == null) return;
        if (g != sel) h.Shade.Opacity = ShadeFor(g);
        bool gold = g.State == State.Update || g.IsBusy;
        string badge = g.State == State.Update ? "UPDATE" : g.IsBusy ? StatusOf(g).Tile.ToUpper() : null;
        h.Badge.Visibility = badge != null ? Visibility.Visible : Visibility.Collapsed;
        if (badge != null) { h.BadgeText.Text = badge; h.Badge.Background = Ui.Brush(gold ? Ui.Gold : Raised); }
    }
    void MarkHome(Game prev, Game g) {
        foreach (var o in prev == null ? Games : (IEnumerable<Game>)new[] { prev, g }) {
            if (o == null) continue;
            foreach (var h in new[] { ((GameUi)o.Ui).Home, ((GameUi)o.Ui).HomeAll, ((GameUi)o.Ui).HomeSmall }) MarkHomeTile(o, h, o == g);
            MarkHomeRow(o, o == g);
        }
        if (homeAllName != null && g != null) {
            homeAllName.Text = g.Name;
            var st = StatusOf(g);
            homeAllState.Text = st.Text + (g.Inst != null ? "  \u2022  " + g.Inst.Label : "") + (g.Latest != null && g.State == State.Update ? "  \u2192  " + g.Latest.Tag : "");
        }
        if (homePage == "game") UpdateGamePage();
        ScrollHome();
    }
    void MarkHomeTile(Game o, HomeTileUi h, bool on) {
        {
            if (h == null) return;
            h.Name.BeginAnimation(UIElement.OpacityProperty, Anim(on ? 1 : 0, on ? 160 : 100));
            if (on) {
                // the frame breathes while the game is picked; quieter while the buttons have the focus
                var a = Calm(Forever(Anim(0.55, 1000, 1, EasingMode.EaseInOut))); a.AutoReverse = true;
                h.Sel.BeginAnimation(UIElement.OpacityProperty, homeZone == "tiles" ? (AnimationTimeline)a : Anim(0, 150));
                h.Shade.BeginAnimation(UIElement.OpacityProperty, Anim(0, 150));
            } else {
                h.Sel.BeginAnimation(UIElement.OpacityProperty, Anim(0, 120));
                h.Shade.BeginAnimation(UIElement.OpacityProperty, Anim(ShadeFor(o), 200));
            }
        }
    }
    // keeps the picked tile in view, with room for part of the next one on each side
    void ScrollHome() {
        if (view != View.Home || sel == null) return;
        W.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
            if (homeAll) { var el = AllElement(sel); if (homeAllTiles.IsAncestorOf(el)) el.BringIntoView(new Rect(-10, -60, el.ActualWidth + 30, el.ActualHeight + 100)); return; }
            if (homePage != "home") return;
            var h = ((GameUi)sel.Ui).Home; if (h == null || !homeTiles.IsAncestorOf(h.Root)) return;
            double x = h.Root.TransformToAncestor(homeTiles).Transform(new Point(0, 0)).X + homeTiles.Margin.Left;
            // measured from where the row is heading, not where it is mid-glide
            double vw = homeScroll.ViewportWidth, cur = homeScrollTo ?? homeScroll.HorizontalOffset, pad = HomeTileSize * 0.45, target = cur;
            if (x - pad < cur) target = x - pad;
            else if (x + HomeTileSize + pad > cur + vw) target = x + HomeTileSize + pad - vw;
            target = Math.Max(0, Math.Min(target, homeScroll.ScrollableWidth));
            if (Math.Abs(target - homeScroll.HorizontalOffset) < 1) { homeScrollTo = null; return; }
            homeScrollTo = target;
            // one glide at a time: a new target replaces the old one (two glides fought each other before)
            if (homeScrollTimer == null) {
                homeScrollTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
                homeScrollTimer.Tick += (s, e) => {
                    if (homeScrollTo == null || homeScroll.Visibility != Visibility.Visible) { homeScrollTo = null; homeScrollTimer.Stop(); return; }
                    double to = Math.Max(0, Math.Min(homeScrollTo.Value, homeScroll.ScrollableWidth));
                    double c = homeScroll.HorizontalOffset, n = c + (to - c) * 0.25;
                    if (Math.Abs(to - n) < 0.5 || ++homeScrollTicks > 120) { n = to; homeScrollTo = null; homeScrollTimer.Stop(); }
                    homeScroll.ScrollToHorizontalOffset(n);
                };
            }
            homeScrollTicks = 0;
            homeScrollTimer.Start();
        }));
    }
    double? homeScrollTo; DispatcherTimer homeScrollTimer; int homeScrollTicks;
    void MarkHomeButtons() {
        for (int i = 0; i < homeButtons.Count; i++) {
            var hb = homeButtons[i]; bool on = homeZone == "buttons" && i == homeIdx;
            hb.Circle.BorderBrush = on ? Ui.Brush(Ui.Teal) : Brushes.Transparent;
            ShowButtonLabel(hb, on);
        }
        if (sel != null) MarkHome(sel, sel);
        SetHints();
    }
    void UpdateHomeChrome() {
        if (homeLayer == null) return;
        int n = Listed().Count(g => g.State == State.Update);
        homeUpdatesPill.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        homeUpdates.Text = n == 1 ? "1 update" : n + " updates";
        homeButtons[2].Text = n > 0 ? "Update all (" + n + ")" : "Check for updates";
        homeButtons[2].Label.Text = homeButtons[2].Text;
        foreach (var g in Games) UpdateHomeTile(g);
        if (view == View.Home)
            // the launcher's own top bar keeps only the window buttons here
            foreach (var name in new[] { "Logo", "SearchPill", "LastCheck", "BtnCheck", "BtnAll", "BtnSettings", "BtnFull" }) ((FrameworkElement)W.FindName(name)).Visibility = Visibility.Collapsed;
    }
    void SetHomeMode(bool on) {
        BuildHome();
        homeLayer.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        N<Grid>("Hero").Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        var lib = (FrameworkElement)N<ScrollViewer>("TileScroll").Parent;
        lib.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        // the status line and progress bar stay; on the home screen they are quieter
        N<Grid>("Track").Visibility = on && !running ? Visibility.Hidden : Visibility.Visible;
        // beside the console glyph in the bottom bar
        var status = (FrameworkElement)((FrameworkElement)N<TextBlock>("StepText").Parent).Parent;
        status.Margin = on ? new Thickness(84, 20, 40, 0) : new Thickness(70, 4, 40, 0);
        if (on) { StopDrift(); homeClockTimer.Start(); TickHomeClock(); }
        else {
            homeClockTimer.Stop();
            foreach (var name in new[] { "Logo", "SearchPill", "LastCheck", "BtnCheck", "BtnSettings", "BtnFull" }) ((FrameworkElement)W.FindName(name)).Visibility = Visibility.Visible;
            if (W.IsActive) StartDrift();
        }
        homeZone = "tiles"; MarkHomeButtons();
        if (on) UpdateHomeChrome(); else if (homePage != "home") { homePage = "home"; ShowHomePage(); }
    }

    // ---------- All games: every game as a grid of tiles, with filters, on the home screen ----------
    void BuildHomeAll() {
        homeAllPage = new Grid { Visibility = Visibility.Collapsed };
        Grid.SetRow(homeAllPage, 1); Grid.SetRowSpan(homeAllPage, 5);
        homeAllPage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        homeAllPage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var head = new Grid { Margin = new Thickness(60, 0, 60, 0) };
        var left = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock { Text = "All games", FontSize = 26, FontFamily = Ui.Semi, Foreground = Ui.Brush(Ui.Text) });
        homeAllCount = new TextBlock { FontSize = 14, Foreground = Ui.Brush(Ui.Muted), Margin = new Thickness(14, 0, 0, 3), VerticalAlignment = VerticalAlignment.Bottom };
        titleRow.Children.Add(homeAllCount);
        left.Children.Add(titleRow);
        var picked = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        homeAllName = new TextBlock { FontSize = 16, Foreground = Ui.Brush(Ui.Teal), VerticalAlignment = VerticalAlignment.Center };
        homeAllState = new TextBlock { FontSize = 13, Foreground = Ui.Brush(Ui.Muted), Margin = new Thickness(14, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        picked.Children.Add(homeAllName); picked.Children.Add(homeAllState);
        left.Children.Add(picked);
        head.Children.Add(left);
        homeFilterPills = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var f in FilterDefs) {
            string id = f[0];
            var pill = new Border { CornerRadius = new CornerRadius(16), Height = 32, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand, Tag = id,
                Child = new TextBlock { FontSize = 13, FontFamily = Ui.Semi, VerticalAlignment = VerticalAlignment.Center },
                Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = Theme.Current.Light ? 0.15 : 0.3 } };
            pill.MouseLeftButtonUp += (s, e) => SetFilter(id);
            homeFilterPills.Children.Add(pill);
        }
        homeFilterPills.Children.Add(new TextBlock { Text = "LB / RB", FontSize = 11, Foreground = Ui.Brush(Ui.Dim), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        BuildLayoutPills(homeFilterPills);
        head.Children.Add(homeFilterPills);
        var line = new Border { Height = 1, Background = Ui.Brush("#33424D"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(-34, 0, -34, -14) };
        head.Children.Add(line);
        homeAllPage.Children.Add(head);
        homeAllTiles = new WrapPanel { Margin = new Thickness(60, 34, 44, 20) };
        homeAllScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Content = homeAllTiles };
        Grid.SetRow(homeAllScroll, 1);
        homeAllScroll.SizeChanged += (s, e) => SizeListRows();
        homeAllPage.Children.Add(homeAllScroll);
        homeLayer.Children.Add(homeAllPage);
    }
    void UpdateHomeFilters() {
        if (homeFilterPills == null) return;
        var list = Listed();
        foreach (var c in homeFilterPills.Children.OfType<Border>()) {
            string id = (string)c.Tag; bool on = id == filter;
            int n = id == "installed" ? list.Count(g => g.Inst != null) : id == "updates" ? list.Count(g => g.State == State.Update) : id == "new" ? list.Count(g => g.Inst == null) : list.Count;
            var t = (TextBlock)c.Child;
            t.Text = FilterDefs.First(f => f[0] == id)[1] + (id == "all" ? "" : "  " + n);
            c.Background = Ui.Brush(on ? Ui.Teal : Raised);
            t.Foreground = Ui.Brush(on ? (Theme.Current.Light ? "#ffffff" : "#0A0E11") : Ui.Text);
        }
        if (homeAllCount != null) { int shown = list.Count(Shown); homeAllCount.Text = shown == list.Count ? list.Count + " games" : shown + " of " + list.Count; }
    }
    void OpenHomeAll(string filterId) {
        homePage = "all";
        if (filterId != null) filter = filterId;
        ShowHomePage();
    }
    void CloseHomeAll() {
        homePage = "home";
        filter = "all";   // the home row ignores filters: it always shows the installed games
        ShowHomePage();
    }
    void ShowHomePage() {
        if (homeAllPage == null) return;
        homeAllPage.Visibility = homePage == "all" ? Visibility.Visible : Visibility.Collapsed;
        gamePage.Visibility = homePage == "game" ? Visibility.Visible : Visibility.Collapsed;
        homeScroll.Visibility = homePage == "home" ? Visibility.Visible : Visibility.Collapsed;
        homeButtonRow.Visibility = homePage == "home" ? Visibility.Visible : Visibility.Collapsed;
        if (homePage == "game") UpdateGamePage();
        SizeListRows();
        homeZone = "tiles";
        ApplySort(true);
        UpdateChips();
        ApplyFilter();
        MarkHome(null, sel);
        MarkHomeButtons();
        var fade = homePage == "all" ? (UIElement)homeAllPage : homePage == "game" ? (UIElement)gamePage : homeScroll;
        fade.BeginAnimation(UIElement.OpacityProperty, Anim(1, 220, 0));
    }

    // keyboard and controller on the home screen; true when handled
    bool HomeMove(string dir) {
        if (homePage == "game") return GamePageMove(dir);
        if (dir == "info") { OpenGamePage(); return true; }
        if (homeAll) {
            if (dir == "left") StepGame(-1);
            else if (dir == "right") StepGame(1);
            else if (dir == "up") { if (allLayout == "list") StepGame(-1); else MoveVertical(-1); }
            else if (dir == "down") { if (allLayout == "list") StepGame(1); else MoveVertical(1); }
            else if (dir == "ok") { if (sel != null) PrimaryAction(sel); }
            else if (dir == "back") CloseHomeAll();
            else return false;
            return true;
        }
        if (homeZone == "tiles") {
            if (dir == "left") StepGame(-1);
            else if (dir == "right") StepGame(1);
            else if (dir == "down") { homeZone = "buttons"; homeIdx = Math.Min(homeIdx, homeButtons.Count - 1); MarkHomeButtons(); }
            else if (dir == "ok") { if (sel != null) PrimaryAction(sel); }
            else return false;
        } else {
            if (dir == "left") homeIdx = Math.Max(0, homeIdx - 1);
            else if (dir == "right") homeIdx = Math.Min(homeButtons.Count - 1, homeIdx + 1);
            else if (dir == "up" || dir == "back") homeZone = "tiles";
            else if (dir == "ok") { homeButtons[homeIdx].Do(); return true; }
            else return false;
            MarkHomeButtons();
        }
        return true;
    }
    bool HomeKey(Key k) {
        switch (k) {
            case Key.Left: return HomeMove("left");
            case Key.Right: return HomeMove("right");
            case Key.Up: return HomeMove("up");
            case Key.Down: return HomeMove("down");
            case Key.Enter: case Key.Space: return HomeMove("ok");
            case Key.Back: return (homePage != "home" || homeZone == "buttons") && HomeMove("back");
            case Key.Escape: return homePage != "home" && HomeMove("back");
            case Key.I: case Key.Apps: return HomeMove("info");
            default: return false;
        }
    }
}
}
