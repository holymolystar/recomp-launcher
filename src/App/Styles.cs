// Launcher styles (Settings > Theme > Launcher style): three different home screens, each with its own themes, its
// own little interface sounds and its own home music. The launcher reopens in the new style when it's saved.
//   Console   the row of square games with round buttons under it (the launcher as it was)
//   Channels  a grid of rounded channel tiles, three high, on a softly striped background; a light, friendly look
//   Cinema    a shelf of wide tiles under a big picture of the picked game, which fills the screen behind them
// Every sound and tune is the launcher's own, drawn in code like the rest (Sfx.cs, HomeMusic.cs).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace RecompLauncher {

static class LauncherStyles {
    public static readonly string[][] All = {
        // id, name, description
        new[] { "glow", "Glow", "Glossy rounded icons that all look alike, a soft violet glow behind them and a dock of round buttons; music and pixel art on Home." },
        new[] { "console", "Console", "A row of big square games, round buttons under it." },
        new[] { "channels", "Channels", "A grid of rounded channel tiles on a soft striped background, with gentle plucked sounds and a light tune." },
        new[] { "cinema", "Cinema", "A shelf of wide tiles under a big picture of the picked game, with glassy sounds and slow airy music." },
    };
}

partial class Shell {
    static string StyleId { get { return Theme.Style; } }
    static double HomeTileW { get { return StyleId == "channels" ? 190 : StyleId == "cinema" ? 176 : StyleId == "glow" ? 132 : 244; } }
    static double HomeTileH { get { return StyleId == "channels" ? 108 : StyleId == "cinema" ? 104 : StyleId == "glow" ? 132 : 244; } }
    static double StyleRadius { get { return StyleId == "channels" ? 16 : StyleId == "cinema" ? 18 : StyleId == "glow" ? 31 : 2; } }
    // Glow: every tile, also Discover's smaller ones, has the same rounded square, about a quarter of its side
    static double TileRadius(double size) { return StyleId == "glow" ? Math.Round(size * 0.235) : 2; }

    // Home's row: a single row, or for Channels pages of channels, four across and three down
    Panel MakeHomeRowPanel() {
        if (StyleId == "channels")
            return new WrapPanel { Orientation = Orientation.Vertical, Height = 3 * (HomeTileH + ChanGap), Margin = new Thickness(ChanSide, 4, ChanSide, 0) };
        if (StyleId == "cinema")
            return new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(34, 20, 70, 20) };
        if (StyleId == "glow")   // a grid of icons, as wide as fits (BuildGlowHome); room round each to grow and glow
            return new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 20), Width = 6 * (132 + 24) };
        return new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(60, 0, 60, 0) };
    }
    const double ChanGap = 14, ChanSide = 74; const int ChanCols = 4;

    // a tile's style touches
    //   Channels: a white frame with a grey rim, the picture set into it with rounded corners, a blue ring when picked
    //   Cinema: a rounded icon with no words; it lifts and shines when picked, and its name shows under the dock
    void StyleTile(HomeTileUi h, Grid box, Border shadow, Border caption, double w, double hgt, double r) {
        if (StyleId == "glow") { GlowTile(h, box, shadow, caption, w, hgt, r); return; }
        if (w == hgt) return;   // only Home's own tiles (Discover and the folders keep their square ones)
        // a square picture on a wide tile: cropped evenly from its middle, not from its top
        h.Icon.HorizontalAlignment = HorizontalAlignment.Center; h.Icon.VerticalAlignment = VerticalAlignment.Center;
        if (StyleId == "channels") {
            bool light = Theme.Current.Light;
            box.Children.Add(new Border { CornerRadius = new CornerRadius(r), Background = Ui.Brush(light ? "#fbfbfb" : "#2a3038"), BorderBrush = Ui.Brush(light ? "#c4c7cb" : "#47505a"),
                BorderThickness = new Thickness(2), IsHitTestVisible = false });
            var inset = new Thickness(7);
            h.Face.Margin = inset; h.Face.CornerRadius = new CornerRadius(r - 6);
            h.Face.Clip = new RectangleGeometry(new Rect(0, 0, w - 14, hgt - 14), r - 6, r - 6);   // a Border doesn't round its picture by itself
            h.Shade.Margin = inset; h.Shade.CornerRadius = new CornerRadius(r - 6);
            caption.Margin = inset; caption.CornerRadius = new CornerRadius(0, 0, r - 6, r - 6);
            shadow.Visibility = Visibility.Collapsed;
            h.Sel.Margin = new Thickness(-5); h.Sel.BorderThickness = new Thickness(4); h.Sel.CornerRadius = new CornerRadius(r + 4);
            h.Sel.Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Opacity = 0.8, Color = Ui.Col(Ui.Teal) };
        }
        if (StyleId == "cinema") {
            box.RenderTransformOrigin = new Point(0.5, 0.5); box.RenderTransform = new ScaleTransform(1, 1);
            h.Face.Clip = new RectangleGeometry(new Rect(0, 0, w, hgt), r, r);
            h.Shade.Clip = new RectangleGeometry(new Rect(0, 0, w, hgt), r, r);
            h.Sel.Visibility = Visibility.Collapsed;
            caption.Visibility = Visibility.Collapsed;
            shadow.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.45, Color = Colors.Black };
            // a soft shine from the top left, as on glass
            var shine = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            shine.GradientStops.Add(new GradientStop(Color.FromArgb(60, 255, 255, 255), 0)); shine.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.5)); shine.Freeze();
            box.Children.Add(new Border { CornerRadius = new CornerRadius(r), Background = shine, IsHitTestVisible = false, Tag = "shine", Opacity = 0 });
        }
    }
    // a tile picked or let go
    void StyleMarkTile(HomeTileUi h, bool on) {
        if ((StyleId != "cinema" && StyleId != "glow") || h == null) return;
        var box = ((StackPanel)h.Root.Children[0]).Children.OfType<Grid>().FirstOrDefault(); if (box == null) return;
        var st = box.RenderTransform as ScaleTransform; if (st == null) return;
        bool lift = on && homeZone == "tiles"; double k = StyleId == "glow" ? 1.1 : 1.16;
        st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(lift ? k : 1, 220)); st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(lift ? k : 1, 220));
        var shine = box.Children.OfType<Border>().FirstOrDefault(b => (b.Tag as string) == "shine"); if (shine != null) shine.BeginAnimation(UIElement.OpacityProperty, Anim(lift ? 1 : 0, 220));
        Panel.SetZIndex(h.Root, on ? 5 : 0);
    }

    // ---------- the home screen around the tiles ----------
    Image cinemaBlur, cinemaArt; TextBlock cinemaTitle, cinemaSub, cinemaName; Game cinemaFor; Border cinemaDock, cinemaArtCard;
    Grid chanBar; TextBlock chanClock, chanDate, chanMailCount; Path chanLeft, chanRight; System.Windows.Threading.DispatcherTimer chanClockTimer;
    void ApplyHomeStyle() {
        if (StyleId == "channels") BuildChannelsHome();
        if (StyleId == "cinema") BuildCinemaHome();
        if (StyleId == "glow") BuildGlowHome();
        // every style: friends online and the music on the top bar
        BuildGlowTopBar(Theme.Current.Light);
        BuildGlowMusic(Theme.Current.Light);
    }
    // Home's buttons, the same in every style (a row of round buttons, Glow's tab bar; Channels has them in its Menu)
    void AddHomeActions(Panel row) {
        AddHomeButton(row, "\uE71D", "Discover", "#6fb4ff", () => OpenHomeAll(null));
        AddHomeButton(row, "\uE9F9", "Achievements", "#ffc94d", () => { if (sel != null && sel.App == null) OpenAchievements(sel); });
        AddHomeButton(row, "\uE716", "Friends", "#7fe3d6", OpenFriendList);
        AddHomeButton(row, "\uE790", "Pixel art", "#ff6ec7", () => OpenPixelArt(sel));
        AddHomeButton(row, "\uE70F", "Flip book", "#ffb81c", OpenFlipBooks);
        AddHomeButton(row, "\uE946", "Game page", "#c49cff", () => OpenGamePage());
        AddHomeButton(row, "\uE896", "Check for updates", "#ff8a1c", () => {
            int n = Listed().Count(g => g.State == State.Update);
            if (n > 0 && !running) TryAction("Updating", () => StartQueue(Listed().Where(g => g.State == State.Update).ToList()));
            else if (!running && !checking) CheckAll(true);
        });
        AddHomeButton(row, "\uE713", "Settings", "#a3a3b8", () => { if (!running) OpenSettings(); });
        AddHomeButton(row, "\uE7E8", "Power", "#a3a3b8", () => { OpenPower(); if (padUsed) powerHeldSince = DateTime.Now; });
    }
    // follows the row of tiles: hidden on Discover and the game page
    void FollowRow(FrameworkElement el) { el.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding("Visibility") { Source = homeScroll }); }
    // the Channels background: a soft light gradient (lighter at the top) under very fine lines
    Brush ChanBackdrop() {
        var bg = Ui.Col("#090C0F"); bool light = Theme.Current.Light;
        var top = light ? Theme.Mix(bg, Colors.White, 0.7) : Theme.Mix(bg, Colors.White, 0.06);
        var bottom = light ? Theme.Mix(bg, Colors.Black, 0.04) : Theme.Mix(bg, Colors.Black, 0.25);
        var g = new LinearGradientBrush(top, bottom, 90); g.Freeze(); return g;
    }
    Brush ChanLines() {
        var line = Theme.Current.Light ? Color.FromArgb(11, 0, 0, 0) : Color.FromArgb(9, 255, 255, 255);
        var dg = new DrawingGroup();
        using (var dc = dg.Open()) { dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 8, 4)); dc.DrawRectangle(new SolidColorBrush(line), null, new Rect(0, 0, 8, 1)); }
        var brush = new DrawingBrush(dg) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 8, 4), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None }; brush.Freeze();
        return brush;
    }
    // the background as a layer: the gradient and its lines
    UIElement ChanBackLayer() {
        var g = new Grid { IsHitTestVisible = false, Background = ChanBackdrop() };
        g.Children.Add(new Rectangle { Fill = ChanLines() });
        return g;
    }

    // ---------- Channels ----------
    void BuildChannelsHome() {
        bool light = Theme.Current.Light;
        var back = ChanBackLayer(); Grid.SetRowSpan(back, 7); homeLayer.Children.Insert(0, back);
        homeLayer.RowDefinitions[0].Height = new GridLength(64);
        homeLayer.RowDefinitions[1].Height = new GridLength(0);
        homeLayer.RowDefinitions[3].Height = new GridLength(0);
        homeLayer.RowDefinitions[5].Height = new GridLength(1, GridUnitType.Star);
        // the clock, mail and the controller go to the bar at the bottom
        var topRight = homeClock.Parent as FrameworkElement; if (topRight != null) topRight.Visibility = Visibility.Collapsed;
        if (homePadIcon != null) homePadIcon.Visibility = Visibility.Collapsed;
        foreach (var b in homeLayer.Children.OfType<Border>().Where(b => b.Height == 1 && Grid.GetRow(b) == 6).ToList()) b.Visibility = Visibility.Collapsed;
        // the round buttons: Menu on the left, Mail on the right
        homeButtons.Clear(); homeButtonRow.Children.Clear();
        chanBar = new Grid { Height = 180, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = true };
        Grid.SetRow(chanBar, 5); Grid.SetRowSpan(chanBar, 2);
        var shape = Geometry.Parse("M0,52 L360,52 C420,52 440,12 500,12 L700,12 C760,12 780,52 840,52 L1200,52 L1200,200 L0,200 Z");
        var top = Geometry.Parse("M0,52 L360,52 C420,52 440,12 500,12 L700,12 C760,12 780,52 840,52 L1200,52");
        var fill = new LinearGradientBrush(Ui.Col(light ? "#ececee" : "#262b31"), Ui.Col(light ? "#d9dadd" : "#1b1f24"), 90); fill.Freeze();
        chanBar.Children.Add(new Path { Data = shape, Fill = fill, Stretch = Stretch.Fill, Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 0, Opacity = light ? 0.15 : 0.5, Color = Colors.Black } });
        chanBar.Children.Add(new Path { Data = top, Stroke = Ui.Brush(Ui.Teal), StrokeThickness = 3, Stretch = Stretch.Fill, VerticalAlignment = VerticalAlignment.Top, Height = 36, Margin = new Thickness(0, 9, 0, 0) });
        var clock = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 20, 0, 0) };
        chanClock = new TextBlock { FontSize = 50, FontFamily = new FontFamily("Segoe UI Light"), Foreground = Ui.Brush(light ? "#8a8e93" : "#b5bcc3"), HorizontalAlignment = HorizontalAlignment.Center };
        chanDate = new TextBlock { FontSize = 19, FontFamily = Ui.Semi, Foreground = Ui.Brush(light ? "#8a8e93" : "#8f979f"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -4, 0, 0) };
        clock.Children.Add(chanClock); clock.Children.Add(chanDate);
        chanBar.Children.Add(clock);
        var left = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(46, 40, 0, 0) };
        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 40, 46, 0) };
        chanBar.Children.Add(left); chanBar.Children.Add(right);
        AddHomeButton(left, "", "Menu", Ui.Teal, OpenChanMenu);
        AddHomeButton(right, "", "Mail", Ui.Teal, OpenMail);
        foreach (var hb in homeButtons) {
            hb.Circle.Width = hb.Circle.Height = 78; hb.Circle.CornerRadius = new CornerRadius(39);
            hb.Circle.Background = new LinearGradientBrush(Ui.Col(light ? "#ffffff" : "#3a424b"), Ui.Col(light ? "#e3e5e8" : "#2a3038"), 90);
            hb.Glyph.FontSize = 26;
            // a light blue rim around each, as on the bar
            var stack = (StackPanel)hb.Circle.Parent; int at = stack.Children.IndexOf(hb.Circle); stack.Children.Remove(hb.Circle);
            stack.Children.Insert(at, new Border { Child = hb.Circle, CornerRadius = new CornerRadius(46), BorderThickness = new Thickness(3), Padding = new Thickness(3), BorderBrush = new SolidColorBrush(Ui.Alpha(Ui.Teal, 0x90)), Background = Ui.Brush(light ? "#f4f5f6" : "#22272d") });
            stack.Width = 96;
        }
        chanMailCount = new TextBlock { FontSize = 13, FontFamily = Ui.Semi, Foreground = Ui.Brush(light ? "#6b7076" : "#b5bcc3"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -30, 0, 0), IsHitTestVisible = false };
        right.Children.Add(chanMailCount);
        MailBox.Changed += () => W.Dispatcher.BeginInvoke(new Action(UpdateChanMail)); UpdateChanMail();
        FollowRow(chanBar);
        homeLayer.Children.Add(chanBar);
        chanClockTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        chanClockTimer.Tick += (s, e) => TickChanClock(); chanClockTimer.Start(); TickChanClock();
        // page arrows at the sides of the grid
        chanLeft = ChanArrow(false); chanRight = ChanArrow(true);
        homeLayer.Children.Add(chanLeft); homeLayer.Children.Add(chanRight);
        homeScroll.ScrollChanged += (s, e) => MarkChanArrows();
    }
    void UpdateChanMail() { if (chanMailCount != null) { int n = MailBox.Unread; chanMailCount.Text = n > 0 ? n.ToString() : ""; } }
    void TickChanClock() {
        if (chanClock == null) return;
        chanClock.Text = DateTime.Now.ToString("H:mm");
        chanDate.Text = DateTime.Now.ToString("ddd d/M");
    }
    Path ChanArrow(bool right) {
        var p = new Path { Data = Geometry.Parse(right ? "M0,0 L26,22 L0,44 Z" : "M26,0 L0,22 L26,44 Z"), Width = 26, Height = 44, Stretch = Stretch.Fill, Cursor = System.Windows.Input.Cursors.Hand,
            Fill = new LinearGradientBrush(Ui.Col("#7fd6f5"), Ui.Col(Ui.Teal), 90), Stroke = Ui.Brush("#ffffff"), StrokeThickness = 2.5,
            HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 24, 0),
            Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.3, Color = Colors.Black }, Visibility = Visibility.Collapsed };
        Grid.SetRow(p, 2);
        p.MouseLeftButtonUp += (s, e) => ChanPage(right ? 1 : -1);
        return p;
    }
    int ChanPageOf(Game g) { var list = ShownGames(); int i = list.IndexOf(g); return i < 0 ? 0 : i / (ChanCols * 3); }
    void ChanPage(int d) {
        var list = ShownGames(); int page = ChanPageOf(sel) + d, per = ChanCols * 3;
        if (page < 0 || page * per >= list.Count) { Sound.Play(Sfx.Bump); return; }
        Sound.Play(Sfx.Move); ShowGame(list[page * per]);
    }
    void MarkChanArrows() {
        if (chanLeft == null) return;
        bool home = homePage == "home" && view == View.Home;
        int page = ChanPageOf(sel), pages = (ShownGames().Count + ChanCols * 3 - 1) / (ChanCols * 3);
        chanLeft.Visibility = home && page > 0 ? Visibility.Visible : Visibility.Collapsed;
        chanRight.Visibility = home && page < pages - 1 ? Visibility.Visible : Visibility.Collapsed;
    }
    // where the row scrolls to: the page of channels the picked one is on
    double? ChanScrollTarget() {
        if (StyleId != "channels" || sel == null) return null;
        int page = ChanPageOf(sel);
        return page * ChanCols * (HomeTileW + ChanGap);
    }
    // the grid runs down each column of three, then on to the next column
    bool ChannelsMove(string dir) {
        if (StyleId != "channels" || homeZone != "tiles" || homePage != "home") return false;
        var list = ShownGames(); int i = list.IndexOf(sel);
        if (i < 0) return false;
        int row = i % 3;
        if (dir == "ok") { ChanOpenOrPlay(sel); return true; }
        if (dir == "left" || dir == "right") {
            int j = i + (dir == "left" ? -3 : 3);
            if (j < 0 || j >= list.Count) { if (dir == "right" && i / 3 < (list.Count - 1) / 3) j = list.Count - 1; else { Sound.Play(Sfx.Bump); return true; } }
            Sound.Play(Sfx.Move); ShowGame(list[j]); return true;
        }
        if (dir == "up") { if (row == 0) return false; Sound.Play(Sfx.Move); ShowGame(list[i - 1]); return true; }
        if (dir == "down") { if (row == 2 || i == list.Count - 1) return false; Sound.Play(Sfx.Move); ShowGame(list[i + 1]); return true; }
        return false;
    }
    // a game opens its channel page first; apps and folders open straight away
    void ChanOpenOrPlay(Game g) {
        if (g == null) return;
        if (g.App != null || IsFolder(g) || IsFolderBack(g)) { PrimaryAction(g); return; }
        OpenChanPreview(g);
    }
    // the Menu button: what the round buttons under the row do on the other styles
    ListPage chanMenu;
    void OpenChanMenu() {
        if (chanMenu == null) {
            chanMenu = MakeListPage("ChanMenu", "MENU", "Menu", 520, null);
            chanMenu.Fill = () => {
                int n = Listed().Count(g => g.State == State.Update);
                FillRows(chanMenu, new List<ListRow> {
                    new ListRow { Glyph = "", C1 = "#2f8fe0", C2 = "#57cbde", Title = "Discover", Text = "Every game, to find and install more", Do = () => { CloseListPage(); OpenHomeAll(null); } },
                    new ListRow { Glyph = "\uE9F9", C1 = "#e8a51c", C2 = "#ffc94d", Title = "Achievements", Text = sel != null ? "Of " + sel.Name : null, Do = () => { CloseListPage(); if (sel != null && sel.App == null) OpenAchievements(sel); } },
                    new ListRow { Glyph = "\uE716", C1 = "#1fa6a0", C2 = "#7fe3d6", Title = "Friends", Do = () => { CloseListPage(); OpenFriendList(); } },
                    new ListRow { Glyph = "\uE790", C1 = "#d63fa0", C2 = "#ff6ec7", Title = "Pixel art", Text = sel != null ? "Draw " + sel.Name + "'s icon" : null, Do = () => { CloseListPage(); OpenPixelArt(sel); } },
                    new ListRow { Glyph = "\uE70F", C1 = "#e8a51c", C2 = "#ffb81c", Title = "Flip book", Text = "Draw little animations", Do = () => { CloseListPage(); OpenFlipBooks(); } },
                    new ListRow { Glyph = "\uEC4F", C1 = "#1db954", C2 = "#1ed760", Title = "Music", Do = () => { CloseListPage(); OpenMusicPage(); } },
                    new ListRow { Glyph = "", C1 = "#f4a312", C2 = "#f2d24b", Title = n > 0 ? "Update all (" + n + ")" : "Check for updates", Do = () => { CloseListPage();
                        if (n > 0 && !running) TryAction("Updating", () => StartQueue(Listed().Where(g => g.State == State.Update).ToList())); else if (!running && !checking) CheckAll(true); } },
                    new ListRow { Glyph = "", C1 = "#5b6670", C2 = "#8a959e", Title = "Settings", Do = () => { CloseListPage(); if (!running) OpenSettings(); } },
                    new ListRow { Glyph = "", C1 = "#5b6670", C2 = "#8a959e", Title = "Power", Text = "Close the launcher, sleep, restart or shut down", Do = () => { CloseListPage(); OpenPower(); } },
                }, "");
            };
        }
        OpenListPage(chanMenu);
    }

    // ---------- Channels: a game's channel page ----------
    Grid chanPrev; Grid chanPrevStage; Image chanPrevBlur, chanPrevArt; Path chanPrevTitle; TextBlock chanPrevSub; StackPanel chanPrevPills, chanPrevSmall; Game chanPrevGame;
    readonly List<Border> chanBig = new List<Border>(), chanSmall = new List<Border>(); int chanRow, chanIdx;
    bool ChanPrevOpen { get { return chanPrev != null && chanPrev.Visibility == Visibility.Visible; } }
    void BuildChanPreview() {
        bool light = Theme.Current.Light;
        chanPrev = new Grid { Visibility = Visibility.Collapsed, Background = ChanBackdrop() };
        var lines = ChanLines(); var linesHost = new Rectangle { Fill = lines, IsHitTestVisible = false }; Grid.SetRowSpan(linesHost, 2); chanPrev.Children.Add(linesHost);
        Grid.SetRowSpan(chanPrev, 7); Panel.SetZIndex(chanPrev, 60);
        chanPrev.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.64, GridUnitType.Star) });
        chanPrev.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.36, GridUnitType.Star) });
        // the banner: the game's colours behind, its picture in a frame, its name big
        chanPrevStage = new Grid { ClipToBounds = true };
        chanPrevBlur = new Image { Stretch = Stretch.UniformToFill, Opacity = 0.45, Effect = new BlurEffect { Radius = 50, RenderingBias = RenderingBias.Performance } };
        chanPrevStage.Children.Add(chanPrevBlur);
        var wash = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        var bg = Ui.Col("#090C0F");
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0xD8, bg.R, bg.G, bg.B), 0)); wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, bg.R, bg.G, bg.B), 0.55)); wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x30, bg.R, bg.G, bg.B), 1)); wash.Freeze();
        chanPrevStage.Children.Add(new Rectangle { Fill = wash });
        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 10) };
        var words = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        chanPrevTitle = new Path { HorizontalAlignment = HorizontalAlignment.Center, StrokeThickness = 5, StrokeLineJoin = PenLineJoin.Round,
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 3, Opacity = 0.35, Color = Colors.Black } };
        chanPrevSub = new TextBlock { FontSize = 17, FontFamily = Ui.Semi, Foreground = Ui.Brush(light ? "#6b7076" : "#b5bcc3"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        words.Children.Add(chanPrevTitle); words.Children.Add(chanPrevSub);
        middle.Children.Add(words);
        chanPrevArt = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(chanPrevArt, BitmapScalingMode.HighQuality);
        var frame = new Border { Width = 190, Height = 190, CornerRadius = new CornerRadius(24), Background = Ui.Brush(light ? "#ffffff" : "#2a3038"), Padding = new Thickness(7),
            BorderBrush = Ui.Brush(light ? "#c4c7cb" : "#47505a"), BorderThickness = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.35, Color = Colors.Black },
            Child = new Border { CornerRadius = new CornerRadius(19), ClipToBounds = true, Child = chanPrevArt } };
        middle.Children.Add(frame);
        chanPrevStage.Children.Add(middle);
        // the arrows: the channel before and after
        foreach (var right in new[] { false, true }) {
            var a = ChanArrow(right); Grid.SetRow(a, 0); a.Visibility = Visibility.Visible; a.Width = 34; a.Height = 58; a.Margin = new Thickness(40, 0, 40, 0);
            bool r = right; a.MouseLeftButtonUp += (s, e) => { e.Handled = true; ChanStep(r ? 1 : -1); };
            chanPrevStage.Children.Add(a);
        }
        chanPrev.Children.Add(chanPrevStage);
        // the line under the banner, and the buttons
        var lower = new Grid(); Grid.SetRow(lower, 1);
        lower.Children.Add(new Border { Height = 3, VerticalAlignment = VerticalAlignment.Top, Background = Ui.Brush(light ? "#4a4f55" : "#0b0d10") });
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 22, 0, 0) };
        chanPrevSmall = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) };
        chanPrevPills = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(chanPrevSmall); stack.Children.Add(chanPrevPills);
        lower.Children.Add(stack);
        chanPrev.Children.Add(lower);
        homeLayer.Children.Add(chanPrev);
    }
    Border ChanPill(string text, double w, double h, double fs, Action act) {
        bool light = Theme.Current.Light;
        var b = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(h / 2), Margin = new Thickness(18, 0, 18, 0), Cursor = System.Windows.Input.Cursors.Hand,
            Background = new LinearGradientBrush(Ui.Col(light ? "#ffffff" : "#3a424b"), Ui.Col(light ? "#e4e6e9" : "#2a3038"), 90), BorderThickness = new Thickness(3),
            BorderBrush = new SolidColorBrush(Ui.Alpha(Ui.Teal, 0xB0)), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1), DataContext = act,
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.2, Color = Colors.Black },
            Child = new TextBlock { Text = text, FontSize = fs, FontFamily = Ui.Semi, Foreground = Ui.Brush(light ? "#4a4f55" : "#e9edf0"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        b.MouseLeftButtonUp += (s, e) => { Sound.Play(Sfx.Select); act(); };
        b.MouseEnter += (s, e) => { var l = chanBig.Contains(b) ? chanBig : chanSmall; chanRow = l == chanBig ? 0 : 1; chanIdx = l.IndexOf(b); MarkChanPills(); };
        return b;
    }
    void OpenChanPreview(Game g) {
        if (chanPrev == null) BuildChanPreview();
        bool was = ChanPrevOpen;
        chanPrevGame = g;
        FillChanPreview(g);
        chanPrev.Visibility = Visibility.Visible;
        if (!was) {
            Sound.Play(Sfx.Open);
            chanPrev.RenderTransformOrigin = new Point(0.5, 0.45); var st = new ScaleTransform(0.9, 0.9); chanPrev.RenderTransform = st;
            st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1, 260)); st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1, 260));
            chanPrev.BeginAnimation(UIElement.OpacityProperty, Anim(1, 220, 0));
            chanRow = 0; chanIdx = 1;
        }
        MarkChanPills(); SetHints();
    }
    void FillChanPreview(Game g) {
        bool light = Theme.Current.Light;
        // the name, as a big rounded outline
        var ft = new FormattedText(g.Name, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Arial Rounded MT Bold, Segoe UI Black"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal), 70, Brushes.Black, 1);
        ft.MaxTextWidth = 1000; ft.MaxLineCount = 2; ft.Trimming = TextTrimming.CharacterEllipsis; ft.TextAlignment = TextAlignment.Center;
        if (ft.Height > 95) ft.SetFontSize(50);
        chanPrevTitle.Data = ft.BuildGeometry(new Point(0, 0));
        chanPrevTitle.Fill = new LinearGradientBrush(Ui.Col(light ? "#ffffff" : "#f4fbff"), Theme.Mix(Ui.Col(Ui.Teal), Colors.White, 0.7), 90);
        chanPrevTitle.Stroke = new SolidColorBrush(Theme.Mix(Ui.Col(Ui.Teal), Colors.Black, 0.35));
        var st = StatusOf(g);
        chanPrevSub.Text = string.Join("   •   ", new[] { g.Platform, st.Text, g.Inst != null ? g.Inst.Label : null, Playtime.Total(g.Id) > 60 ? Playtime.Format(Playtime.Total(g.Id)) + " played" : null }.Where(x => !string.IsNullOrEmpty(x)));
        var pic = ((GameUi)g.Ui).Home != null && ((GameUi)g.Ui).Home.Icon.Source != null ? ((GameUi)g.Ui).Home.Icon.Source : (ImageSource)g.Icon;
        chanPrevArt.Source = pic; chanPrevBlur.Source = pic;
        // the buttons: the big two, and the smaller ones over them
        chanBig.Clear(); chanSmall.Clear(); chanPrevPills.Children.Clear(); chanPrevSmall.Children.Clear();
        bool missing = g.State == State.Missing;
        chanBig.Add(ChanPill("Home Menu", 330, 86, 30, CloseChanPreview));
        chanBig.Add(ChanPill(g.State == State.Installing ? "Installing..." : missing ? "Install" : "Start", 330, 86, 30, () => { var gg = chanPrevGame; CloseChanPreview(); PrimaryAction(gg); }));
        if (g.State == State.Update) chanSmall.Add(ChanPill("Update", 170, 46, 17, () => { if (!running) TryAction("Updating " + g.Name, () => StartQueue(new List<Game> { g })); }));
        chanSmall.Add(ChanPill("What's new", 170, 46, 17, () => { CloseChanPreview(); OpenNotes("notes"); }));
        chanSmall.Add(ChanPill(Profiles.IsFav(g.Id) ? "Unfavourite" : "Favourite", 170, 46, 17, () => { ToggleFavourite(g); FillChanPreview(g); MarkChanPills(); }));
        if (Directory.Exists(g.Dir)) chanSmall.Add(ChanPill("Game folder", 170, 46, 17, () => OpenFolder(g.Dir)));
        chanSmall.Add(ChanPill("More", 170, 46, 17, () => { CloseChanPreview(); OpenGamePage(); }));
        foreach (var b in chanBig) chanPrevPills.Children.Add(b);
        foreach (var b in chanSmall) chanPrevSmall.Children.Add(b);
        if (chanIdx >= (chanRow == 0 ? chanBig : chanSmall).Count) chanIdx = 0;
    }
    void MarkChanPills() {
        for (int r = 0; r < 2; r++) {
            var l = r == 0 ? chanBig : chanSmall;
            for (int i = 0; i < l.Count; i++) {
                bool on = r == chanRow && i == chanIdx;
                l[i].BorderBrush = on ? Ui.Brush(Ui.Teal) : new SolidColorBrush(Ui.Alpha(Ui.Teal, 0x70));
                l[i].BorderThickness = new Thickness(on ? 5 : 3);
                var st = (ScaleTransform)l[i].RenderTransform; double k = on ? 1.06 : 1;
                st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(k, 140)); st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(k, 140));
            }
        }
    }
    void CloseChanPreview() {
        if (!ChanPrevOpen) return;
        Sound.Play(Sfx.Back);
        var a = Anim(0, 180); a.Completed += (s, e) => { chanPrev.Visibility = Visibility.Collapsed; SetHints(); };
        chanPrev.BeginAnimation(UIElement.OpacityProperty, a);
    }
    // the channel before or after (games only, as on the grid)
    void ChanStep(int d) {
        var list = ShownGames().Where(x => x.App == null && !IsFolder(x) && !IsFolderBack(x)).ToList();
        int i = list.IndexOf(chanPrevGame) + d;
        if (i < 0 || i >= list.Count) { Sound.Play(Sfx.Bump); return; }
        Sound.Play(Sfx.Move);
        ShowGame(list[i]);
        var tt = new TranslateTransform(d * 60, 0); chanPrevStage.RenderTransform = tt; tt.BeginAnimation(TranslateTransform.XProperty, Anim(0, 240));
        chanPrevStage.BeginAnimation(UIElement.OpacityProperty, Anim(1, 240, 0.3));
        chanPrevGame = list[i]; FillChanPreview(list[i]); MarkChanPills(); SetHints();
    }
    void ChanPrevPad(int bit) {
        var l = chanRow == 0 ? chanBig : chanSmall;
        if (bit == Pad.B) { CloseChanPreview(); return; }
        if (bit == Pad.LB || bit == Pad.RB) { ChanStep(bit == Pad.RB ? 1 : -1); return; }
        if (bit == Pad.A) { if (chanIdx < l.Count) { Sound.Play(Sfx.Select); ((Action)l[chanIdx].DataContext)(); } return; }
        if (bit == Pad.Y) { CloseChanPreview(); OpenGamePage(); return; }
        int was = chanIdx, wasRow = chanRow;
        if (bit == Pad.Left) chanIdx = Math.Max(0, chanIdx - 1);
        else if (bit == Pad.Right) chanIdx = Math.Min(l.Count - 1, chanIdx + 1);
        else if (bit == Pad.Up && chanRow == 0 && chanSmall.Count > 0) { chanRow = 1; chanIdx = Math.Min(chanSmall.Count - 1, chanIdx * chanSmall.Count / 2); }
        else if (bit == Pad.Down && chanRow == 1) { chanRow = 0; chanIdx = chanIdx < chanSmall.Count / 2 ? 0 : 1; }
        Sound.Play(was != chanIdx || wasRow != chanRow ? Sfx.Move : Sfx.Bump);
        MarkChanPills();
    }
    void ChanPrevHints(List<string[]> spec) {
        spec.Add(new[] { "LB", "Channel" }); spec.Add(new[] { "RB", "Channel" });
        spec.Add(new[] { "Y", "More" }); spec.Add(new[] { "A", "Select" }); spec.Add(new[] { "B", "Home Menu" });
    }
    bool ChanPrevKey(System.Windows.Input.Key k) {
        int bit = k == System.Windows.Input.Key.Left ? Pad.Left : k == System.Windows.Input.Key.Right ? Pad.Right : k == System.Windows.Input.Key.Up ? Pad.Up : k == System.Windows.Input.Key.Down ? Pad.Down
            : k == System.Windows.Input.Key.Enter || k == System.Windows.Input.Key.Space ? Pad.A : k == System.Windows.Input.Key.Escape || k == System.Windows.Input.Key.Back ? Pad.B
            : k == System.Windows.Input.Key.PageUp ? Pad.LB : k == System.Windows.Input.Key.PageDown ? Pad.RB : 0;
        if (bit == 0) return false; ChanPrevPad(bit); return true;
    }

    // a picture came in (they load after the window opens): the channel page and Cinema show it too
    void StyleIconChanged(Game g) {
        if (StyleId == "glow") {
            if (g == glowHeroFor) { glowHeroFor = null; MarkGlowName(); }
            if (g == glowLast && glowJumpSmall != null) { var pic = ((GameUi)g.Ui).Home.Icon.Source; glowJumpSmall.Source = pic; glowJumpSmall.Margin = new Thickness(g.IconIsArt ? 0 : 8); if (!glowJumpHasHero) glowJumpArt.Source = pic; }
        }
        if (ChanPrevOpen && g == chanPrevGame) { var pic = ((GameUi)g.Ui).Home.Icon.Source; chanPrevArt.Source = pic; chanPrevBlur.Source = pic; }
        if (cinemaArt != null && g == cinemaFor) { var pic = ((GameUi)g.Ui).Home.Icon.Source; cinemaArt.Source = pic; cinemaBlur.Source = pic; }
    }

    // ---------- Cinema ----------
    void BuildCinemaHome() {
        homeLayer.RowDefinitions[1].Height = new GridLength(1.25, GridUnitType.Star);
        homeLayer.RowDefinitions[3].Height = new GridLength(52);
        homeLayer.RowDefinitions[5].Height = new GridLength(0.3, GridUnitType.Star);
        var host = new Grid { IsHitTestVisible = false, ClipToBounds = true };
        Grid.SetRowSpan(host, 7);
        var bg = Ui.Col("#090C0F");
        // the picked game's colours filling the screen, softly
        cinemaBlur = new Image { Stretch = Stretch.UniformToFill, Opacity = 0, Effect = new BlurEffect { Radius = 60, RenderingBias = RenderingBias.Performance } };
        host.Children.Add(cinemaBlur);
        host.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0x88, bg.R, bg.G, bg.B)) });
        // its picture, sharp, at the top right, fading into the background on its left and bottom edges
        cinemaArt = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
        RenderOptions.SetBitmapScalingMode(cinemaArt, BitmapScalingMode.HighQuality);
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0)); mask.GradientStops.Add(new GradientStop(Colors.Black, 0.45)); mask.Freeze();
        var mask2 = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        mask2.GradientStops.Add(new GradientStop(Colors.Black, 0.55)); mask2.GradientStops.Add(new GradientStop(Colors.Transparent, 1)); mask2.Freeze();
        cinemaArtCard = new Border { Width = 640, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Height = 560, OpacityMask = mask,
            Child = new Border { OpacityMask = mask2, Child = cinemaArt } };
        host.Children.Add(cinemaArtCard);
        var low = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        low.GradientStops.Add(new GradientStop(Color.FromArgb(0, bg.R, bg.G, bg.B), 0.45)); low.GradientStops.Add(new GradientStop(Color.FromArgb(0xC8, bg.R, bg.G, bg.B), 1)); low.Freeze();
        host.Children.Add(new Rectangle { Fill = low });
        homeLayer.Children.Insert(0, host);
        // the name, big, on the left
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(70, 0, 0, 22), IsHitTestVisible = false, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        cinemaSub = new TextBlock { FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Ui.Brush("#C9CCC8"), Margin = new Thickness(2, 0, 0, 4) };
        cinemaTitle = new TextBlock { FontSize = 56, FontFamily = new FontFamily("Segoe UI Black"), Foreground = Ui.Brush(Ui.Text), TextWrapping = TextWrapping.Wrap, LineHeight = 60, MaxHeight = 124, TextTrimming = TextTrimming.WordEllipsis,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black } };
        words.Children.Add(cinemaSub); words.Children.Add(cinemaTitle);
        Grid.SetRow(words, 1);
        homeLayer.Children.Add(words);
        // the dock: frosted glass round the row of icons
        homeLayer.Children.Remove(homeScroll);
        bool light = Theme.Current.Light;
        cinemaDock = new Border { CornerRadius = new CornerRadius(30), Margin = new Thickness(44, 0, 44, 0), Child = homeScroll,
            Background = new SolidColorBrush(light ? Color.FromArgb(0x7A, 255, 255, 255) : Color.FromArgb(0x30, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(light ? Color.FromArgb(0xA0, 255, 255, 255) : Color.FromArgb(0x38, 255, 255, 255)), BorderThickness = new Thickness(1.2) };
        Grid.SetRow(cinemaDock, 2);
        cinemaDock.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding("Visibility") { Source = homeScroll });
        homeLayer.Children.Add(cinemaDock);
        // the picked icon's name, under the dock
        cinemaName = new TextBlock { FontSize = 16, FontFamily = Ui.Semi, Foreground = Ui.Brush(Ui.Text), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0),
            RenderTransform = new TranslateTransform(), IsHitTestVisible = false, Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = 0.6, Color = Colors.Black } };
        Grid.SetRow(cinemaName, 3); FollowRow(cinemaName);
        homeLayer.Children.Add(cinemaName);
    }
    // the picked game: its picture, name and the label under its icon
    void StyleMarkHome(Game g) {
        if (StyleId == "glow") { MarkGlowName(); return; }   // the top half and the grid (above)
        if (StyleId == "channels") { MarkChanArrows(); if (ChanPrevOpen && g != null && g != chanPrevGame && g.App == null && !IsFolder(g) && !IsFolderBack(g)) { chanPrevGame = g; FillChanPreview(g); MarkChanPills(); } }
        if (StyleId != "cinema" || cinemaArt == null || g == null) return;
        PlaceCinemaName();
        if (g == cinemaFor) return;
        cinemaFor = g;
        cinemaTitle.Text = g.Name;
        var st = StatusOf(g);
        cinemaSub.Text = string.Join("  •  ", new[] { g.Platform ?? (g.App != null ? "App" : null), st.Text }.Where(x => !string.IsNullOrEmpty(x))).ToUpperInvariant();
        var pic = ((GameUi)g.Ui).Home != null && ((GameUi)g.Ui).Home.Icon.Source != null ? ((GameUi)g.Ui).Home.Icon.Source : (ImageSource)g.Icon;
        cinemaArt.Source = pic; cinemaBlur.Source = pic;
        cinemaArt.BeginAnimation(UIElement.OpacityProperty, Anim(g.IconIsArt ? 1 : 0.5, 450, 0));
        cinemaBlur.BeginAnimation(UIElement.OpacityProperty, Anim(0.75, 450, 0.3));
        cinemaTitle.BeginAnimation(UIElement.OpacityProperty, Anim(1, 300, 0));
    }
    void PlaceCinemaName() {
        if (cinemaName == null || sel == null) return;
        cinemaName.Text = homeZone == "tiles" ? sel.Name : "";
        W.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => {
            try {
                var h = ((GameUi)sel.Ui).Home; if (h == null || !h.Root.IsVisible) { cinemaName.Text = ""; return; }
                double x = h.Root.TransformToAncestor(homeLayer).Transform(new Point(0, 0)).X + HomeTileW / 2;
                cinemaName.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                ((TranslateTransform)cinemaName.RenderTransform).BeginAnimation(TranslateTransform.XProperty, Anim(x - cinemaName.DesiredSize.Width / 2, 160));
            } catch { }
        }));
    }

    // ---------- Glow ----------
    // Every icon is the same rounded square with the same gloss on top: a light from above that fades out by the
    // middle, and a thin light rim. Pictures fill it edge to edge (cropped from the middle); a program icon sits on
    // the game's colours. The picked one grows a little and gets a glowing ring in the theme's two colours.
    void GlowTile(HomeTileUi h, Grid box, Border shadow, Border caption, double w, double hgt, double r) {
        bool big = Math.Abs(w - HomeTileW) < 1 && Math.Abs(hgt - HomeTileH) < 1;   // Home's own grid (Discover's tiles keep their names on them)
        h.Icon.HorizontalAlignment = HorizontalAlignment.Center; h.Icon.VerticalAlignment = VerticalAlignment.Center;
        h.Face.Clip = new RectangleGeometry(new Rect(0, 0, w, hgt), r, r);
        h.Shade.Clip = new RectangleGeometry(new Rect(0, 0, w, hgt), r, r);
        caption.CornerRadius = new CornerRadius(0, 0, r, r);
        if (big) caption.Visibility = Visibility.Collapsed;   // the name shows under the row
        shadow.Effect = new DropShadowEffect { BlurRadius = big ? 26 : 14, ShadowDepth = big ? 8 : 3, Opacity = Theme.Current.Light ? 0.22 : 0.5, Color = Colors.Black };
        var ring = new LinearGradientBrush(Ui.Col(Ui.Teal), Ui.Col(Ui.Gold), 45); ring.Freeze();
        h.Sel.CornerRadius = new CornerRadius(r + 9); h.Sel.Margin = new Thickness(-10); h.Sel.BorderThickness = new Thickness(4); h.Sel.BorderBrush = ring;
        h.Sel.Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 0, Opacity = 0.9, Color = Ui.Col(Ui.Teal) };
        h.Badge.CornerRadius = new CornerRadius(9); h.Badge.Margin = new Thickness(r * 0.32);
        if (big) { box.RenderTransformOrigin = new Point(0.5, 0.5); box.RenderTransform = new ScaleTransform(1, 1); }
    }
    // the gloss and rim go over the picture, under the badges (added once the tile's parts are in)
    void GlowOverlay(HomeTileUi h, Grid box, double r) {
        if (StyleId != "glow") return;
        int at = box.Children.IndexOf(h.Shade); if (at < 0) return;
        var shine = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        shine.GradientStops.Add(new GradientStop(Color.FromArgb(0x5A, 255, 255, 255), 0));
        shine.GradientStops.Add(new GradientStop(Color.FromArgb(0x16, 255, 255, 255), 0.42));
        shine.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.5)); shine.Freeze();
        box.Children.Insert(at, new Border { CornerRadius = new CornerRadius(r), Background = shine, IsHitTestVisible = false });
        box.Children.Insert(at + 1, new Border { CornerRadius = new CornerRadius(r), BorderThickness = new Thickness(1.6), BorderBrush = new SolidColorBrush(Color.FromArgb(0x48, 255, 255, 255)), IsHitTestVisible = false });
        if (h.Star != null) h.Star.Margin = new Thickness(r * 0.3);
    }
    // the background: the theme's colour with two big soft glows, one in each accent colour
    Grid GlowBlobs(double strength) {
        bool light = Theme.Current.Light;
        var g = new Grid { IsHitTestVisible = false, ClipToBounds = true, Background = Ui.Brush("#090C0F") };
        Func<string, byte, Brush> blob = (c, a) => {
            var col = Ui.Col(c); var rb = new RadialGradientBrush();
            rb.GradientStops.Add(new GradientStop(Color.FromArgb(a, col.R, col.G, col.B), 0));
            rb.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(a / 3), col.R, col.G, col.B), 0.5));
            rb.GradientStops.Add(new GradientStop(Color.FromArgb(0, col.R, col.G, col.B), 1)); rb.Freeze(); return rb;
        };
        byte s1 = (byte)((light ? 0x50 : 0x78) * strength), s2 = (byte)((light ? 0x40 : 0x60) * strength);
        g.Children.Add(new Ellipse { Width = 1300, Height = 1000, Fill = blob(Ui.Teal, s1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-420, -460, 0, 0) });
        g.Children.Add(new Ellipse { Width = 1400, Height = 1000, Fill = blob(Ui.Gold, s2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -480, -520) });
        // a faint wash between them, so the middle isn't flat
        var wash = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
        wash.GradientStops.Add(new GradientStop(light ? Color.FromArgb(0x10, 255, 255, 255) : Color.FromArgb(0x30, 0, 0, 0), 1)); wash.Freeze();
        g.Children.Add(new Rectangle { Fill = wash });
        return g;
    }
    // Glow's home screen is laid out as on a dual-screen handheld, top over bottom:
    //   top     the picked game big: its wide picture across the top (Steam, Epic, GOG), or its icon as a glossy card
    //           on its own colours; its name and state beside it, its achievements at the bottom right. Over it, a
    //           pill on the left with your friends online and one on the right with mail, updates, the time
    //   middle  the tab bar: one frosted pill of round buttons (Discover, Achievements, Friends, Pixel art, ...); the
    //           picked one shows its name
    //   bottom  the games as a grid of rounded icons, starting with "Jump back in!": the game played last, wide
    // Up from the grid's top row goes to the tab bar, up again to the top bar; A on Jump back in starts that game.
    Border glowDock, glowMusic, glowFriends, glowCard, glowAch, glowJump, glowJumpSel; Grid glowHero; Image glowHeroArt, glowHeroBlur, glowCardIcon, glowJumpArt;
    StackPanel glowNameBox, glowFriendFaces; TextBlock glowName, glowSub, glowSong, glowPlayGlyph, glowFriendsText, glowAchText, glowJumpName;
    System.Windows.Threading.DispatcherTimer glowMusicTimer; Game glowHeroFor, glowLast; bool glowOnJump, glowJumpHasHero; Image glowJumpSmall;
    const double GlowGap = 24;
    int GlowCols { get { double w = homeScroll != null && homeScroll.ActualWidth > 0 ? homeScroll.ActualWidth : 1200; return Math.Max(3, Math.Min(9, (int)((w - 120) / (HomeTileW + GlowGap)))); } }
    void BuildGlowHome() {
        bool light = Theme.Current.Light;
        var back = GlowBlobs(1); Grid.SetRowSpan(back, 7); homeLayer.Children.Insert(0, back);
        // rows: top bar, the picked game, the tab bar, the grid, the hints
        var rows = homeLayer.RowDefinitions;
        rows[1].Height = new GridLength(0.8, GridUnitType.Star); rows[2].Height = GridLength.Auto; rows[3].Height = new GridLength(0);
        rows[4].Height = new GridLength(1.45, GridUnitType.Star); rows[5].Height = new GridLength(0);
        foreach (var b in homeLayer.Children.OfType<Border>().Where(b => b.Height == 1 && Grid.GetRow(b) == 6).ToList()) b.Visibility = Visibility.Collapsed;
        BuildGlowHero(light);
        // the grid: rows of icons, scrolling down
        Grid.SetRow(homeScroll, 4);
        homeScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled; homeScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        homeScroll.SizeChanged += (s, e) => { var wp = homeTiles as WrapPanel; if (wp != null) wp.Width = GlowCols * (HomeTileW + GlowGap); };
        // the tab bar
        homeButtons.Clear(); homeButtonRow.Children.Clear();
        AddHomeActions(homeButtonRow);   // the same buttons in every style (below)
        homeLayer.Children.Remove(homeButtonRow);
        glowDock = new Border { CornerRadius = new CornerRadius(34), Padding = new Thickness(10, 8, 10, 8), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 14), Child = homeButtonRow,
            Background = GlassFill(0x26), BorderBrush = GlassRim(), BorderThickness = new Thickness(1.2),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = light ? 0.12 : 0.35, Color = Colors.Black } };
        Grid.SetRow(glowDock, 2);
        glowDock.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding("Visibility") { Source = homeScroll });
        homeLayer.Children.Add(glowDock);
        foreach (var hb in homeButtons) {
            hb.Circle.Width = hb.Circle.Height = 50; hb.Circle.CornerRadius = new CornerRadius(25); hb.Circle.BorderThickness = new Thickness(2.5);
            hb.Circle.Background = new SolidColorBrush(light ? Color.FromArgb(0xFF, 255, 255, 255) : Color.FromArgb(0x1E, 255, 255, 255));
            hb.Circle.Effect = null; hb.Glyph.FontSize = 19;
            var stack = (StackPanel)hb.Circle.Parent; stack.Orientation = Orientation.Horizontal; stack.Width = double.NaN; stack.Margin = new Thickness(4, 0, 4, 0);
            hb.Label.Margin = new Thickness(10, 0, 8, 0); hb.Label.VerticalAlignment = VerticalAlignment.Center; hb.Label.FontFamily = Ui.Semi; hb.Label.FontSize = 15;
            hb.Label.Foreground = Ui.Brush(Ui.Text); hb.Label.Visibility = Visibility.Collapsed; hb.Label.Opacity = 1;
        }
    }
    // the top half: the picked game, big
    void BuildGlowHero(bool light) {
        glowHero = new Grid { ClipToBounds = true, IsHitTestVisible = false };
        Grid.SetRow(glowHero, 0); Grid.SetRowSpan(glowHero, 2);
        glowHeroBlur = new Image { Stretch = Stretch.UniformToFill, Opacity = 0.55, Effect = new BlurEffect { Radius = 70, RenderingBias = RenderingBias.Performance } };
        glowHero.Children.Add(glowHeroBlur);
        glowHeroArt = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
        RenderOptions.SetBitmapScalingMode(glowHeroArt, BitmapScalingMode.HighQuality);
        glowHero.Children.Add(glowHeroArt);
        // darker at the top (the bars) and fading into the background at the bottom
        var bg = Ui.Col("#090C0F"); var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0x90, bg.R, bg.G, bg.B), 0)); fade.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, bg.R, bg.G, bg.B), 0.3));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0x20, bg.R, bg.G, bg.B), 1)); fade.Freeze();
        glowHero.Children.Add(new Rectangle { Fill = fade });
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        mask.GradientStops.Add(new GradientStop(Colors.Black, 0.5)); mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0.98)); mask.Freeze();
        glowHero.OpacityMask = mask;   // the whole top half fades into the background, no edge
        // the glossy card (its icon) with the name beside it
        var mid = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 64, 0, 0) };
        glowCardIcon = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(glowCardIcon, BitmapScalingMode.HighQuality);
        var shine = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        shine.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, 255, 255, 255), 0)); shine.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, 255, 255, 255), 0.45)); shine.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.5)); shine.Freeze();
        var cardInner = new Grid(); cardInner.Children.Add(glowCardIcon);
        cardInner.Children.Add(new Border { Background = shine, CornerRadius = new CornerRadius(40) });
        cardInner.Children.Add(new Border { CornerRadius = new CornerRadius(40), BorderThickness = new Thickness(2), BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 255, 255, 255)) });
        glowCard = new Border { Width = 150, Height = 150, CornerRadius = new CornerRadius(40), Child = cardInner,
            Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 8, Opacity = 0.5, Color = Colors.Black } };
        cardInner.Clip = new RectangleGeometry(new Rect(0, 0, 150, 150), 36, 36);
        mid.Children.Add(glowCard);
        glowNameBox = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(34, 0, 0, 0), MaxWidth = 640 };
        glowName = new TextBlock { FontSize = 40, FontFamily = new FontFamily("Segoe UI Black"), Foreground = Ui.Brush(Ui.Text), TextWrapping = TextWrapping.Wrap, MaxHeight = 110, TextTrimming = TextTrimming.WordEllipsis, LineHeight = 46,
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 0, Opacity = light ? 0 : 0.6, Color = Colors.Black } };
        glowSub = new TextBlock { FontSize = 14, FontFamily = Ui.Semi, Foreground = Ui.Brush("#C9CCC8"), Margin = new Thickness(2, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        glowNameBox.Children.Add(glowName); glowNameBox.Children.Add(glowSub);
        mid.Children.Add(glowNameBox);
        glowHero.Children.Add(mid);
        // achievements, at the bottom right
        glowAchText = new TextBlock { FontSize = 13, FontFamily = Ui.Semi, Foreground = Ui.Brush(Ui.Text), VerticalAlignment = VerticalAlignment.Center };
        var ach = new StackPanel { Orientation = Orientation.Horizontal };
        ach.Children.Add(new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 15, Foreground = Ui.Brush("#ffc94d"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) });
        ach.Children.Add(glowAchText);
        glowAch = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 7, 14, 7), Child = ach, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 60, 8), Background = new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0)), Visibility = Visibility.Collapsed };
        glowHero.Children.Add(glowAch);
        FollowRow(glowHero);
        homeLayer.Children.Insert(1, glowHero);
    }
    // the top bar's pills: friends online on the left (beside your picture), mail / updates / time on the right
    void BuildGlowTopBar(bool light) {
        Brush pillBg = StyleId == "glow" ? new SolidColorBrush(light ? Color.FromArgb(0xC8, 255, 255, 255) : Color.FromArgb(0x40, 0, 0, 0)) : GlassFill(0x26);
        var right = homeClock.Parent as Panel;
        if (StyleId == "glow" && right != null && right.Parent == homeLayer) {
            homeLayer.Children.Remove(right);
            var m = ((FrameworkElement)right).Margin; ((FrameworkElement)right).Margin = new Thickness(0);
            var pill = new Border { CornerRadius = new CornerRadius(22), Padding = new Thickness(16, 6, 18, 6), Background = pillBg, Child = right, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 10, m.Right - 18, 0) };
            homeLayer.Children.Add(pill);
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        glowFriendFaces = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        glowFriendsText = new TextBlock { FontSize = 13.5, FontFamily = Ui.Semi, Foreground = Ui.Brush(Ui.Text), VerticalAlignment = VerticalAlignment.Center, Text = "Friends" };
        row.Children.Add(new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 14, Foreground = Ui.Brush("#7fe3d6"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) });
        row.Children.Add(glowFriendsText); row.Children.Add(glowFriendFaces);
        glowFriends = new Border { CornerRadius = new CornerRadius(22), Padding = new Thickness(14, 7, 14, 7), Background = pillBg, Child = row, Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0), BorderThickness = new Thickness(2), BorderBrush = StyleId == "glow" ? Brushes.Transparent : GlassRim() };
        glowFriends.MouseLeftButtonUp += (s, e) => { e.Handled = true; OpenFriendList(); };
        var me = homeName.Parent as Panel; if (me != null) me.Children.Add(glowFriends); else homeLayer.Children.Add(glowFriends);
        TickGlowFriends();
    }
    void TickGlowFriends() {
        if (glowFriendsText == null) return;
        var on = frFriends.Where(f => f.Online).OrderByDescending(f => f.Playing != null).ToList();
        glowFriendsText.Text = on.Count + " online";
        glowFriendFaces.Children.Clear();
        foreach (var f in on.Take(4)) {
            var pic = FriendFace(f.Look);
            glowFriendFaces.Children.Add(new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(glowFriendFaces.Children.Count == 0 ? 10 : -7, 0, 0, 0), ClipToBounds = true,
                BorderThickness = new Thickness(2), BorderBrush = Ui.Brush("#090C0F"), Background = Ui.Brush("#33424D"), ToolTip = f.Name + (f.Playing != null ? ": " + f.Playing : ""),
                Child = pic != null ? (UIElement)new Image { Source = pic, Stretch = Stretch.UniformToFill } : new TextBlock { Text = (f.Name ?? "?").Substring(0, 1), FontSize = 11, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        }
        if (on.Count > 4) glowFriendFaces.Children.Add(new TextBlock { Text = "+" + (on.Count - 4), FontSize = 12, Foreground = Ui.Brush(Ui.Muted), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
    }
    // the picked game in the top half (and the tab bar's names, the Jump back in mark)
    void MarkGlowName() {
        if (glowName == null) return;
        for (int i = 0; i < homeButtons.Count; i++) homeButtons[i].Label.Visibility = homeZone == "buttons" && i == homeIdx ? Visibility.Visible : Visibility.Collapsed;
        TickGlowFriends();
        MarkGlowJump();
        var g = sel;
        if (g == null) { glowName.Text = ""; glowSub.Text = ""; return; }
        glowName.Text = glowOnJump && homeZone == "tiles" ? g.Name : g.Name;
        var st = StatusOf(g);
        glowSub.Text = string.Join("   •   ", new[] { glowOnJump && homeZone == "tiles" ? "Jump back in!" : null, g.Platform ?? (g.App != null ? "App" : null), st.Text, g.Inst != null ? g.Inst.Label : null,
            Playtime.Total(g.Id) > 60 ? Playtime.Format(Playtime.Total(g.Id)) + " played" : null }.Where(x => !string.IsNullOrEmpty(x)));
        var ss = g.Steam != null ? SteamGames.StatsOf(g) : null;
        bool ach = ss != null && ss.AchTotal > 0;
        glowAch.Visibility = ach ? Visibility.Visible : Visibility.Collapsed;
        if (ach) glowAchText.Text = ss.Achieved + " / " + ss.AchTotal + "  Achievements";
        if (g == glowHeroFor) return;
        glowHeroFor = g;
        var icon = ((GameUi)g.Ui).Home != null && ((GameUi)g.Ui).Home.Icon.Source != null ? ((GameUi)g.Ui).Home.Icon.Source : (ImageSource)g.Icon;
        ImageSource hero = null; try { hero = g.App == null ? Stores.Hero(g) : null; } catch { }
        glowHeroBlur.Source = hero ?? icon;
        glowHeroArt.Source = hero;
        glowHeroArt.BeginAnimation(UIElement.OpacityProperty, Anim(hero != null ? (Theme.Current.Light ? 0.55 : 0.8) : 0, 400, 0));
        glowHeroBlur.BeginAnimation(UIElement.OpacityProperty, Anim(hero != null ? 0.3 : 0.6, 400, 0.2));
        glowCard.Background = Ui.Gradient(g.Accent, g.Accent2);
        glowCardIcon.Source = icon; glowCardIcon.Margin = new Thickness(g.IconIsArt ? 0 : 150 * 0.22);
        var tt = new TranslateTransform(0, 14); glowNameBox.Parent.SetValue(UIElement.RenderTransformProperty, tt);
        tt.BeginAnimation(TranslateTransform.YProperty, Anim(0, 260)); ((UIElement)glowNameBox.Parent).BeginAnimation(UIElement.OpacityProperty, Anim(1, 260, 0));
    }

    // ---------- Glow: the grid, and Jump back in ----------
    // the game played last (in the launcher, or Steam's own count), for the wide first tile
    Game GlowLastPlayed() { return Games.Where(g => g.App == null && g.Inst != null && LastPlayedAny(g) > DateTime.MinValue).OrderByDescending(LastPlayedAny).FirstOrDefault(); }
    void AddGlowJump() {
        glowLast = homePage == "home" && homeFolder == null ? GlowLastPlayed() : null;
        if (glowLast == null) { glowOnJump = false; return; }
        double w = 2 * HomeTileW + GlowGap, h = HomeTileH, r = TileRadius(HomeTileW);
        var g = glowLast;
        var box = new Grid { Width = w, Height = h };
        box.Children.Add(new Border { CornerRadius = new CornerRadius(r), Background = Brushes.Black, Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 6, Opacity = 0.45, Color = Colors.Black } });
        var face = new Grid { ClipToBounds = true, Background = Ui.Gradient(g.Accent, g.Accent2) };
        face.Clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
        ImageSource hero = null; try { hero = Stores.Hero(g); } catch { }
        var icon = ((GameUi)g.Ui).Home != null && ((GameUi)g.Ui).Home.Icon.Source != null ? ((GameUi)g.Ui).Home.Icon.Source : (ImageSource)g.Icon;
        glowJumpArt = new Image { Source = hero ?? icon, Stretch = Stretch.UniformToFill, Opacity = hero != null ? 1 : 0.6 };
        if (hero == null) glowJumpArt.Effect = new BlurEffect { Radius = 18 };
        face.Children.Add(glowJumpArt);
        var dark = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        dark.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, 0, 0, 0), 0)); dark.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, 0, 0, 0), 1)); dark.Freeze();
        face.Children.Add(new Rectangle { Fill = dark });
        // its own icon, small, at the bottom left; the name and the button beside it
        var small = new Border { Width = h * 0.42, Height = h * 0.42, CornerRadius = new CornerRadius(h * 0.1), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(14), Background = Ui.Gradient(g.Accent, g.Accent2), BorderThickness = new Thickness(2), BorderBrush = Brushes.White,
            Child = glowJumpSmall = new Image { Source = icon, Stretch = Stretch.UniformToFill, Margin = new Thickness(g.IconIsArt ? 0 : 8) } };
        glowJumpHasHero = hero != null;
        small.Clip = new RectangleGeometry(new Rect(0, 0, h * 0.42, h * 0.42), h * 0.1, h * 0.1);
        face.Children.Add(small);
        glowJumpName = new TextBlock { Text = g.Name, FontSize = 14, FontFamily = Ui.Semi, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = w - 40,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 12, 16, 0) };
        face.Children.Add(glowJumpName);
        var go = new StackPanel { Orientation = Orientation.Horizontal };
        go.Children.Add(new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Brushes.White, Margin = new Thickness(0, 0, 7, 0),
            Child = new TextBlock { Text = "A", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Ui.Brush("#0A0E11"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        go.Children.Add(new TextBlock { Text = "Jump back in!", FontSize = 13, FontFamily = Ui.Semi, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        face.Children.Add(new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(6, 4, 12, 4), Background = new SolidColorBrush(Color.FromArgb(0x90, 0, 0, 0)), Child = go,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 14, 16), BorderThickness = new Thickness(1.2), BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 255, 255, 255)) });
        box.Children.Add(face);
        var shine = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        shine.GradientStops.Add(new GradientStop(Color.FromArgb(0x40, 255, 255, 255), 0)); shine.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.5)); shine.Freeze();
        box.Children.Add(new Border { CornerRadius = new CornerRadius(r), Background = shine, IsHitTestVisible = false });
        box.Children.Add(new Border { CornerRadius = new CornerRadius(r), BorderThickness = new Thickness(1.6), BorderBrush = new SolidColorBrush(Color.FromArgb(0x48, 255, 255, 255)), IsHitTestVisible = false });
        var ring = new LinearGradientBrush(Ui.Col(Ui.Teal), Ui.Col(Ui.Gold), 45); ring.Freeze();
        glowJumpSel = new Border { CornerRadius = new CornerRadius(r + 9), Margin = new Thickness(-10), BorderThickness = new Thickness(4), BorderBrush = ring, Opacity = 0, IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 0, Opacity = 0.9, Color = Ui.Col(Ui.Teal) } };
        box.Children.Insert(0, glowJumpSel);
        glowJump = new Border { Child = box, Margin = new Thickness(0, 0, GlowGap, GlowGap), Cursor = System.Windows.Input.Cursors.Hand, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
        glowJump.MouseLeftButtonUp += (s, e) => { padUsed = false; homeZone = "tiles"; if (glowOnJump && sel == glowLast) PrimaryAction(glowLast); else { glowOnJump = true; Sound.Play(Sfx.Move); ShowGame(glowLast); } };
        homeTiles.Children.Insert(0, glowJump);
    }
    void MarkGlowJump() {
        if (glowJump == null) return;
        bool on = glowOnJump && glowLast != null && sel == glowLast && homeZone == "tiles" && homePage == "home";
        if (!on && glowOnJump && sel != glowLast) glowOnJump = false;
        var a = Calm(Forever(Anim(0.55, 1000, 1, EasingMode.EaseInOut))); a.AutoReverse = true;
        glowJumpSel.BeginAnimation(UIElement.OpacityProperty, on ? (System.Windows.Media.Animation.AnimationTimeline)a : Anim(0, 120));
        var st = (ScaleTransform)glowJump.RenderTransform;
        st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(on ? 1.05 : 1, 220)); st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(on ? 1.05 : 1, 220));
        // the game's own tile isn't the picked one while Jump back in is
        if (on && glowLast != null) { var h = ((GameUi)glowLast.Ui).Home; if (h != null) { h.Sel.BeginAnimation(UIElement.OpacityProperty, Anim(0, 80)); StyleMarkTile(h, false); } }
    }
    // the grid's moves: left / right along, up / down a row; up from the top row to the tab bar, and from there up
    // to the top bar; Jump back in takes the first two places
    bool GlowMove(string dir) {
        if (StyleId != "glow" || homePage != "home") return false;
        if (homeZone == "buttons") {
            if (dir == "down") { Sound.Play(Sfx.Move); homeZone = "tiles"; MarkHomeButtons(); return true; }
            if (dir == "up") { Sound.Play(Sfx.Move); homeZone = "profile"; homeTopIdx = 0; MarkHomeProfile(); MarkHomeButtons(); return true; }
            return false;
        }
        if (homeZone != "tiles") return false;
        var list = ShownGames(); if (list.Count == 0) return false;
        int cols = GlowCols, lead = glowLast != null && glowJump != null && homeTiles.Children.Contains(glowJump) ? 2 : 0;
        // the place in the grid: Jump back in is 0 (and 1), the games follow
        int cell = glowOnJump && lead > 0 ? 0 : lead + Math.Max(0, list.IndexOf(sel));
        Func<int, bool> go = c => {
            if (c < lead) { if (glowOnJump) return false; glowOnJump = true; Sound.Play(Sfx.Move); ShowGame(glowLast); MarkGlowName(); return true; }
            int i = Math.Min(list.Count - 1, c - lead); if (i < 0) return false;
            bool was = glowOnJump; glowOnJump = false;
            if (list[i] == sel && !was) return false;
            Sound.Play(Sfx.Move); ShowGame(list[i]); MarkGlowName(); return true;
        };
        if (dir == "ok") { if (sel != null) PrimaryAction(sel); return true; }
        if (dir == "left") { if (cell == 0 || (cell < lead)) { Sound.Play(Sfx.Bump); return true; } if (!go(cell - 1)) Sound.Play(Sfx.Bump); return true; }
        if (dir == "right") { int c = cell < lead ? lead : cell + 1; if (c - lead >= list.Count || !go(c)) Sound.Play(Sfx.Bump); return true; }
        if (dir == "up") {
            int c = cell - cols;
            if (c < 0) { Sound.Play(Sfx.Move); homeZone = "buttons"; homeIdx = Math.Min(homeIdx, homeButtons.Count - 1); MarkHomeButtons(); return true; }
            go(c); return true;
        }
        if (dir == "down") {
            int c = cell < lead ? cols : cell + cols;
            if (c - lead >= list.Count) { int lastRow = (lead + list.Count - 1) / cols; if (cell / cols < lastRow) c = lead + list.Count - 1; else { Sound.Play(Sfx.Bump); return true; } }
            go(c); return true;
        }
        return false;
    }
    // keeps the picked icon (or Jump back in) in view as the grid scrolls down
    void GlowScroll() {
        FrameworkElement el = glowOnJump && glowJump != null ? (FrameworkElement)glowJump : sel != null && ((GameUi)sel.Ui).Home != null ? ((GameUi)sel.Ui).Home.Root : null;
        if (el == null || !homeTiles.IsAncestorOf(el)) return;
        el.BringIntoView(new Rect(-20, -34, el.ActualWidth + 40, el.ActualHeight + 70));
    }

    // ---------- Glow: the music on Home ----------
    // A pill at the top: the song playing now (Spotify, VLC, foobar2000, MusicBee...: Music.NowPlaying) with
    // previous / play-pause / next. Clicking it, or A on it with the controller (it's on the top bar, up from the
    // games), opens Music: the same three, and Open for each music app on this PC.
    void BuildGlowMusic(bool light) {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 15, Foreground = Ui.Brush("#1ed760"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        glowSong = new TextBlock { FontSize = 13.5, FontFamily = Ui.Semi, Foreground = Ui.Brush(Ui.Text), MaxWidth = 340, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Text = "Music" };
        row.Children.Add(glowSong);
        Func<string, Action, TextBlock> btn = (glyph, act) => {
            var t = new TextBlock { Text = glyph, FontFamily = Ui.Mdl, FontSize = 13, Foreground = Ui.Brush(Ui.Text), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = System.Windows.Input.Cursors.Hand };
            t.MouseLeftButtonUp += (s, e) => { e.Handled = true; Sound.Play(Sfx.Select); act(); GlowMusicSoon(); };
            row.Children.Add(t); return t;
        };
        btn("", Music.PrevSong); glowPlayGlyph = btn("", Music.Toggle); btn("", Music.NextSong);
        glowMusic = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(16, 8, 16, 8), Child = row, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0), Cursor = System.Windows.Input.Cursors.Hand, BorderThickness = new Thickness(2),
            Background = StyleId == "glow" ? new SolidColorBrush(light ? Color.FromArgb(0xB8, 255, 255, 255) : Color.FromArgb(0x26, 255, 255, 255)) : GlassFill(0x26), BorderBrush = StyleId == "glow" ? Brushes.Transparent : GlassRim() };
        glowMusic.MouseLeftButtonUp += (s, e) => OpenMusicPage();
        Grid.SetRow(glowMusic, 0);
        homeLayer.Children.Add(glowMusic);
        glowMusicTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        glowMusicTimer.Tick += (s, e) => TickGlowMusic(); glowMusicTimer.Start(); TickGlowMusic();
    }
    void TickGlowMusic() {
        if (glowSong == null || view != View.Home || !W.IsVisible) return;
        string song, artist, app; bool paused;
        bool on = Music.NowPlaying(out song, out artist, out app, out paused);
        glowSong.Text = !on ? "Music" : song != null ? (artist != null ? artist + "  –  " + song : song) : app;
        glowPlayGlyph.Text = on && !paused && song != null ? "" : "";
    }
    void GlowMusicSoon() {
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        t.Tick += (s, e) => { t.Stop(); TickGlowMusic(); if (musicPage != null && ListPageOpen && lpOpen == musicPage) { int i = musicPage.Idx; musicPage.Fill(); musicPage.Idx = Math.Min(i, musicPage.Items.Count - 1); MarkRows(musicPage, false); } };
        t.Start();
    }
    ListPage musicPage;
    void OpenMusicPage() {
        if (musicPage == null) {
            musicPage = MakeListPage("MusicPage", "MUSIC", "Music", 540, null);
            musicPage.Fill = () => {
                string song, artist, app; bool paused;
                bool on = Music.NowPlaying(out song, out artist, out app, out paused);
                bool playing = on && !paused && song != null;
                musicPage.Sub.Text = !on ? "No music app is open. Open one below; the buttons work with any app that answers the keyboard's media keys."
                    : song != null ? song + (artist != null ? "   •   " + artist : "") + "   (" + app + (paused ? ", paused" : "") + ")" : app + " is open.";
                var rows = new List<ListRow> {
                    new ListRow { Glyph = playing ? "" : "", C1 = "#1db954", C2 = "#1ed760", Title = playing ? "Pause" : "Play", Do = () => { Music.Toggle(); GlowMusicSoon(); } },
                    new ListRow { Glyph = "", C1 = "#1db954", C2 = "#1ed760", Title = "Next song", Do = () => { Music.NextSong(); GlowMusicSoon(); } },
                    new ListRow { Glyph = "", C1 = "#1db954", C2 = "#1ed760", Title = "Previous song", Do = () => { Music.PrevSong(); GlowMusicSoon(); } },
                };
                foreach (var p in Music.Players()) {
                    var pl = p;
                    rows.Add(new ListRow { Glyph = "", C1 = "#5b6670", C2 = "#8a959e", Title = "Open " + pl[0], Text = app == pl[0] ? "Open now" : null, Do = () => { Music.Open(pl[1]); GlowMusicSoon(); } });
                }
                FillRows(musicPage, rows, "");
            };
        }
        OpenListPage(musicPage);
    }

    // ---------- Settings: the style cards, and only that style's themes ----------
    UniformGrid styleGrid; string pickedStyle;
    FrameworkElement StyleRow() {
        styleGrid = new UniformGrid { Columns = LauncherStyles.All.Length, Margin = new Thickness(0, 0, 0, 14) };
        foreach (var s in LauncherStyles.All) styleGrid.Children.Add(StyleCard(s));
        return styleGrid;
    }
    Button StyleCard(string[] s) {
        var face = new Grid { Height = 96, ClipToBounds = true };
        face.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = Ui.Brush("#0D1215") });
        // a tiny drawing of the style's home screen
        var mini = new Canvas { Width = 150, Height = 60, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        Action<double, double, double, double, double, string> box = (x, y, w, h, r, c) => { var b = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(r), Background = Ui.Brush(c) }; Canvas.SetLeft(b, x); Canvas.SetTop(b, y); mini.Children.Add(b); };
        if (s[0] == "console") { for (int i = 0; i < 4; i++) box(4 + i * 37, 6, 33, 33, 2, i == 0 ? Ui.Teal : "#33424D"); for (int i = 0; i < 4; i++) box(45 + i * 16, 46, 11, 11, 6, "#5F717C"); }
        else if (s[0] == "glow") { box(0, 0, 150, 60, 8, "#11181D"); for (int i = 0; i < 4; i++) box(8 + i * 35, 8, 29, 29, 8, i == 0 ? Ui.Teal : "#5F717C"); box(42, 45, 66, 12, 6, "#33424D"); }
        else if (s[0] == "channels") { for (int c = 0; c < 4; c++) for (int r = 0; r < 3; r++) box(6 + c * 36, 2 + r * 19, 32, 16, 5, c == 0 && r == 0 ? Ui.Teal : "#A9B6BE"); }
        else { box(0, 0, 150, 34, 4, "#26333C"); for (int i = 0; i < 3; i++) box(6 + i * 48, 38, 44, 22, 4, i == 0 ? "#E8E6DF" : "#5F717C"); }
        face.Children.Add(mini);
        face.Children.Add(new TextBlock { Text = s[1], FontSize = 13, FontFamily = Ui.Semi, Margin = new Thickness(12, 8, 0, 0) });
        var card = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), Padding = new Thickness(2), Child = face, BorderBrush = Brushes.Transparent };
        var b2 = new Button { Content = card, Margin = new Thickness(0, 0, 10, 0), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = s[2], Tag = s[0], Focusable = false };
        b2.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"Button\"><ContentPresenter/></ControlTemplate>");
        b2.Click += (o, e) => PickStyle(s[0]);
        return b2;
    }
    void PickStyle(string id) {
        pickedStyle = id;
        foreach (Button b in styleGrid.Children) ((Border)b.Content).BorderBrush = (string)b.Tag == id ? Ui.Brush(Ui.Text) : Brushes.Transparent;
        // the themes of this style
        themeGrid.Children.Clear();
        foreach (var th in Theme.All.Where(t => t.Style == id)) themeGrid.Children.Add(ThemeSwatch(th));
        string keep = pickedTheme != null && Theme.All.Any(t => t.Id == pickedTheme && t.Style == id) ? pickedTheme : Theme.All.First(t => t.Style == id).Id;
        PickTheme(keep);
        if (N<Grid>("SettingsLayer").Visibility == Visibility.Visible) { setStops = SetStops(); }
    }

    // ---------- each style's own interface sounds and home music ----------
    // Channels: soft wooden plucks. Cinema: tiny glassy clicks.
    internal static float[] StyleSound(Sfx s) {
        int R = Sound.Rate;
        Func<int, float[]> buf = ms => new float[R * ms / 1000];
        float[] b = null;
        if (StyleId == "channels") {
            switch (s) {
                case Sfx.Move: b = buf(90); Pluck(b, 0, 1046.5, 0.30, 40); break;
                case Sfx.Bump: b = buf(120); Pluck(b, 0, 196, 0.35, 50); break;
                case Sfx.Select: b = buf(260); Pluck(b, 0, 784, 0.30, 70); Pluck(b, 70, 1174.7, 0.30, 110); break;
                case Sfx.Back: b = buf(260); Pluck(b, 0, 1174.7, 0.28, 70); Pluck(b, 70, 784, 0.28, 110); break;
                case Sfx.Open: b = buf(360); Pluck(b, 0, 659.3, 0.24, 80); Pluck(b, 60, 880, 0.24, 80); Pluck(b, 120, 1318.5, 0.26, 140); break;
                case Sfx.ToggleOn: b = buf(140); Pluck(b, 0, 1568, 0.24, 50); break;
                case Sfx.ToggleOff: b = buf(140); Pluck(b, 0, 1046.5, 0.24, 50); break;
                case Sfx.Boot: b = buf(1300); foreach (var n in new[] { 523.3, 659.3, 784, 1046.5, 1318.5 }.Select((f, i) => new { f, i })) Pluck(b, n.i * 110, n.f, 0.22, 420); break;
                default: b = buf(900); foreach (var n in new[] { 784, 987.8, 1174.7, 1568 }.Select((f, i) => new { f, i })) Pluck(b, n.i * 80, n.f, 0.26, n.i == 3 ? 380 : 120); break;
            }
        } else if (StyleId == "cinema") {
            switch (s) {
                case Sfx.Move: b = buf(45); Glass(b, 0, 3520, 0.16, 9); break;
                case Sfx.Bump: b = buf(90); Glass(b, 0, 440, 0.22, 25); break;
                case Sfx.Select: b = buf(220); Glass(b, 0, 1760, 0.20, 40); Glass(b, 45, 2637, 0.16, 70); break;
                case Sfx.Back: b = buf(220); Glass(b, 0, 2637, 0.18, 40); Glass(b, 45, 1760, 0.18, 70); break;
                case Sfx.Open: b = buf(420); for (int i = 0; i < 4; i++) Glass(b, i * 35, 1318.5 * Math.Pow(1.26, i), 0.12, 160); break;
                case Sfx.ToggleOn: b = buf(110); Glass(b, 0, 2093, 0.18, 30); break;
                case Sfx.ToggleOff: b = buf(110); Glass(b, 0, 1568, 0.18, 30); break;
                case Sfx.Boot: b = buf(1800); foreach (var f in new[] { 261.6, 392, 523.3, 659.3, 987.8 }) Glass(b, 0, f, 0.12, 900); break;
                default: b = buf(1000); foreach (var n in new[] { 1046.5, 1568, 2093, 2637 }.Select((f, i) => new { f, i })) Glass(b, n.i * 60, n.f, 0.14, n.i == 3 ? 500 : 200); break;
            }
        }
        return b;
    }
    // a pluck: a quick, warm decay with a woody second partial
    static void Pluck(float[] b, int atMs, double f, double amp, double tauMs) {
        int R = Sound.Rate, start = R * atMs / 1000; double tau = tauMs / 1000.0;
        for (int i = start; i < b.Length; i++) {
            double t = (i - start) / (double)R, env = Math.Min(1, t / 0.002) * Math.Exp(-t / tau);
            if (env < 0.0004 && t > 0.01) break;
            b[i] += (float)(amp * env * (Math.Sin(2 * Math.PI * f * t) + 0.35 * Math.Sin(2 * Math.PI * f * 3.98 * t) * Math.Exp(-t / (tau * 0.25))));
        }
    }
    // glass: a pure, quiet ping with a little shimmer
    static void Glass(float[] b, int atMs, double f, double amp, double tauMs) {
        int R = Sound.Rate, start = R * atMs / 1000; double tau = tauMs / 1000.0;
        for (int i = start; i < b.Length; i++) {
            double t = (i - start) / (double)R, env = Math.Min(1, t / 0.004) * Math.Exp(-t / tau);
            if (env < 0.0004 && t > 0.01) break;
            b[i] += (float)(amp * env * (Math.Sin(2 * Math.PI * f * t) + 0.18 * Math.Sin(2 * Math.PI * f * 2.76 * t) + 0.08 * Math.Sin(2 * Math.PI * f * 5.4 * t)));
        }
    }

    // Channels: a light, bouncy tune at 112 a minute: plucked chords, a walking bass and a whistled-sounding melody
    static double[] TuneChannels() {
        double bpm = 112, beat = 60 / bpm; int beats = 64, n = (int)(beats * beat * MRate);
        var mix = new double[n];
        int[][] chords = { new[] { 60, 64, 67, 71 }, new[] { 62, 65, 69, 72 }, new[] { 64, 67, 71, 74 }, new[] { 65, 69, 72, 76 },
                           new[] { 60, 64, 67, 71 }, new[] { 57, 60, 64, 67 }, new[] { 62, 65, 69, 72 }, new[] { 55, 59, 62, 65 } };   // Cmaj7 Dm7 Em7 Fmaj7 Cmaj7 Am7 Dm7 G7
        // melody: (beat, note, length)
        double[][] mel = {
            new double[] { 0, 76, 1 }, new double[] { 1.5, 79, 0.5 }, new double[] { 2, 77, 1 }, new double[] { 3, 76, 1 }, new double[] { 4, 74, 1.5 }, new double[] { 6, 72, 2 },
            new double[] { 8, 72, 1 }, new double[] { 9, 74, 0.5 }, new double[] { 9.5, 76, 0.5 }, new double[] { 10, 79, 1.5 }, new double[] { 12, 81, 1 }, new double[] { 13, 79, 1 }, new double[] { 14, 77, 2 },
            new double[] { 16, 76, 1 }, new double[] { 17.5, 74, 0.5 }, new double[] { 18, 72, 1 }, new double[] { 19, 71, 1 }, new double[] { 20, 72, 1.5 }, new double[] { 22, 69, 2 },
            new double[] { 24, 74, 1 }, new double[] { 25, 72, 1 }, new double[] { 26, 71, 1 }, new double[] { 27, 74, 1 }, new double[] { 28, 79, 3.5 },
            new double[] { 32, 76, 1 }, new double[] { 33.5, 79, 0.5 }, new double[] { 34, 77, 1 }, new double[] { 35, 76, 1 }, new double[] { 36, 74, 1.5 }, new double[] { 38, 76, 2 },
            new double[] { 40, 77, 1 }, new double[] { 41, 76, 0.5 }, new double[] { 41.5, 74, 0.5 }, new double[] { 42, 72, 1.5 }, new double[] { 44, 69, 1 }, new double[] { 45, 72, 1 }, new double[] { 46, 76, 2 },
            new double[] { 48, 74, 1 }, new double[] { 49, 76, 1 }, new double[] { 50, 77, 1 }, new double[] { 51, 81, 1 }, new double[] { 52, 79, 2 }, new double[] { 54, 76, 2 },
            new double[] { 56, 74, 1 }, new double[] { 57, 72, 1 }, new double[] { 58, 71, 1 }, new double[] { 59, 67, 1 }, new double[] { 60, 72, 3.5 } };
        for (int bar = 0; bar < 16; bar++) {
            var ch = chords[bar % 8]; double t0 = bar * 4 * beat;
            // the chord, plucked on 1 and the "and" of 2 and on 4
            foreach (double off in new[] { 0, 1.5, 3 }) foreach (int m in ch) Tone(mix, t0 + off * beat, beat * 0.9, Freq(m), 0.028, 0.004, 0.22, 0.4, 0);
            // the bass walks: root, fifth, root an octave up, the next chord's leading note
            int root = ch[0] - 24; var walk = new[] { root, root + 7, root + 12, root + 10 };
            for (int k = 0; k < 4; k++) Tone(mix, t0 + k * beat, beat * 0.9, Freq(walk[k]), 0.10, 0.004, 0.35, 0.1, 0);
        }
        foreach (var m in mel) Tone(mix, m[0] * beat, m[2] * beat * 1.05, Freq((int)m[1]), 0.07, 0.03, 0.9, 0.08, 0.0025);
        return mix;
    }
    // Cinema: slow, airy pads that drift between four chords, with a few distant chimes
    static double[] TuneCinema() {
        double bpm = 66, beat = 60 / bpm; int beats = 64, n = (int)(beats * beat * MRate);
        var mix = new double[n];
        int[][] chords = { new[] { 50, 57, 62, 66, 69 }, new[] { 47, 54, 59, 62, 66 }, new[] { 43, 50, 55, 59, 62 }, new[] { 45, 52, 57, 61, 64 } };   // D, Bm, G, A (add9 colours)
        for (int c = 0; c < 8; c++) {
            var ch = chords[c % 4]; double t0 = c * 8 * beat;
            foreach (int m in ch) { Tone(mix, t0, beat * 9, Freq(m), 0.03, 1.6, 6, 0.0, 0.002); Tone(mix, t0, beat * 9, Freq(m) * 1.003, 0.02, 1.8, 6, 0.0, 0.002); }
            Tone(mix, t0, beat * 8, Freq(ch[0] - 12), 0.06, 0.8, 4, 0.0, 0);
            // chimes, high and soft
            int[] chime = { ch[3] + 12, ch[4] + 12, ch[2] + 24 };
            for (int k = 0; k < 3; k++) Tone(mix, t0 + (1 + k * 2.5) * beat, beat * 4, Freq(chime[k]), 0.025, 0.005, 1.4, 0.3, 0);
        }
        return mix;
    }
}
}
