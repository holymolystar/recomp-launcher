// The launcher window: the library in one of four views (row of cards under the game's page, large grid, compact
// grid, list), search, filters and sort, and the selected game's page and buttons. Panels.cs has the overlays
// (release notes, settings, choose your game, uninstall, file browser), Jobs.cs installs/updates/plays, Controller.cs
// the XInput controller.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

using Path = System.IO.Path;

namespace RecompLauncher {

enum View { Row, Grid, Compact, List, Home }

// A game's elements in each view
class GameUi {
    // card (row and large grid share it)
    public Grid Tile, Box; public Border Glow, Face, Shade, Sel; public DropShadowEffect GlowFx; public Ellipse Dot;
    public TextBlock Name, Sub; public Image Icon; public ScaleTransform Scale; public TranslateTransform Shine; public UIElement ShineEl;
    // compact card
    public Border CTile; public Image CIcon; public TextBlock CName, CSub; public Ellipse CDot;
    // list row
    public Border LRow; public Image LIcon; public TextBlock LName, LSource, LInst, LLatest, LStatus, LPlayed, LLast; public Ellipse LDot;
    public bool Pulsing;
    public HomeTileUi Home;
}

partial class Shell {
    public Window W;
    readonly Args args;
    List<Game> Games { get { return Catalog.Games; } }
    Game sel;
    View view = View.Row;
    string filter = "all", sort = "default";
    List<Game> order = new List<Game>();
    bool running, checking, isFull, fullInit, padUsed;
    Rect normalRect;

    public Shell(Args a) { args = a; }

    T N<T>(string name) where T : class { return W.FindName(name) as T; }
    static Stream Res(string name) { return Assembly.GetExecutingAssembly().GetManifestResourceStream(name); }
    static string ResText(string name) { using (var s = Res(name)) using (var r = new StreamReader(s)) return Theme.MapXaml(r.ReadToEnd()); }

    // ---------- animation helpers ----------
    static Duration Dur(double ms) { return new Duration(TimeSpan.FromMilliseconds(ms)); }
    static DoubleAnimation Anim(double to, double ms, double? from = null, EasingMode? ease = EasingMode.EaseOut) {
        var a = new DoubleAnimation { To = to, Duration = Dur(ms) };
        if (from.HasValue) a.From = from.Value;
        if (ease.HasValue) a.EasingFunction = new CubicEase { EasingMode = ease.Value };
        return a;
    }
    static ColorAnimation ColorAnim(Color to, double ms) { return new ColorAnimation { To = to, Duration = Dur(ms), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } }; }
    static T Forever<T>(T a) where T : Timeline { a.RepeatBehavior = RepeatBehavior.Forever; return a; }
    // slow never-ending animations at 30 frames a second: they look the same and cost a fraction
    static T Calm<T>(T a) where T : Timeline { Timeline.SetDesiredFrameRate(a, 15); return a; }
    static void Fade(UIElement el, double to, double ms) { el.BeginAnimation(UIElement.OpacityProperty, Anim(to, ms)); }

    // ---------- start ----------
    public void Run() {
        W = (Window)XamlReader.Parse(ResText("MainWindow.xaml"));
        var logo = Path.Combine(Paths.Root, "logo.png");
        if (File.Exists(logo)) {
            var b = new System.Windows.Media.Imaging.BitmapImage();
            b.BeginInit(); b.UriSource = new Uri(logo); b.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze();
            N<Image>("LogoImg").Source = b; W.Icon = b;
        }
        sort = new[] { "default", "installed", "recent", "name" }.Contains(Settings.Get("SortMode")) ? Settings.Get("SortMode") : "default";
        view = ParseView(Settings.Get("ViewMode"));
        foreach (var g in Games) g.Hidden = ("," + Settings.Get("Hidden") + ",").Contains("," + g.Id + ",");
        ApplySettings();
        foreach (var g in Games) { g.Inst = Engine.GetInstalled(g); g.State = g.Inst != null ? State.Checking : State.Missing; g.Latest = ReleaseCache.Get(g.Id); }
        BuildCards();
        BuildChips();
        BuildPanels();
        BuildController();
        WireEvents();
        W.ContentRendered += (s, e) => Started();
        W.ShowDialog();
    }
    void ApplySettings() {
        foreach (var g in Games) {
            g.Dir = Paths.Expand(Settings.Get(g.SettingsKey));
            g.Worker = Path.Combine(Paths.Root, "recomp-worker.ps1");
            g.Backups = Path.Combine(Paths.Backups, g.Id);
        }
        var t = Catalog.Find("totk");
        if (t != null) {
            t.Worker = Path.Combine(Settings.Get("TotkUpdaterDir"), "update-worker.ps1");
            t.Backups = Path.Combine(Settings.Get("TotkUpdaterDir"), "backups");
        }
    }
    static View ParseView(string v) {
        switch (v) { case "grid": return View.Grid; case "compact": return View.Compact; case "list": return View.List; case "home": return View.Home; default: return View.Row; }
    }
    static string ViewName(View v) { return v == View.Grid ? "grid" : v == View.Compact ? "compact" : v == View.List ? "list" : v == View.Home ? "home" : "row"; }

    void Started() {
        var root = N<Border>("Root");
        root.BeginAnimation(UIElement.OpacityProperty, Anim(1, 350, 0));
        var rs = (ScaleTransform)root.RenderTransform;
        rs.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1, 450, 0.97)); rs.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1, 450, 0.97));
        // slowly drifting light behind the game: moved (no layout), 12 frames a second, only while the window is
        // in front
        foreach (var n in new[] { "Glow1", "Glow2" }) {
            var el = N<Ellipse>(n);
            el.RenderTransform = new TranslateTransform();
        }
        StartDrift();
        W.Activated += (s, e) => StartDrift();
        W.Deactivated += (s, e) => StopDrift();
        W.StateChanged += (s, e) => { if (W.WindowState == WindowState.Minimized) StopDrift(); };

        var first = Catalog.Find(args.Select ?? Settings.Get("LastGame")) ?? Games[0];
        SetTab("notes");
        SetView(view, false);
        ShowGame(first);
        if (args.Full || (args.Snapshot == null && Settings.Get("Fullscreen") == "True")) SetFullscreen(true);
        // card faces after the first frame, a few at a time, so the window shows at once
        LoadIconsLater(Games.ToList());
        CheckAll(false);
        StartController();
        SetAutoCheck();
        if (args.Search != null) N<TextBox>("SearchBox").Text = args.Search;
        if (args.OpenSettings) OpenSettings();
        if (args.OpenNotes) OpenNotes("notes");
        if (args.PadScript != null) PlayPadScript(args.PadScript);
        if (args.Snapshot != null) {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(args.SnapshotDelay) };
            t.Tick += (s, e) => { t.Stop(); Snapshot(args.Snapshot); W.Close(); };
            t.Start();
        }
    }
    bool drifting;
    void StartDrift() {
        if (drifting || args.Snapshot != null || view == View.Home) return;
        drifting = true;
        Func<double, double, AnimationTimeline> d = (to, ms) => {
            var a = Anim(to, ms, null, EasingMode.EaseInOut); a.AutoReverse = true; a.RepeatBehavior = RepeatBehavior.Forever;
            Timeline.SetDesiredFrameRate(a, 12); return a;
        };
        var t1 = (TranslateTransform)N<Ellipse>("Glow1").RenderTransform; var t2 = (TranslateTransform)N<Ellipse>("Glow2").RenderTransform;
        t1.BeginAnimation(TranslateTransform.XProperty, d(-180, 9000));
        t1.BeginAnimation(TranslateTransform.YProperty, d(140, 11000));
        t2.BeginAnimation(TranslateTransform.XProperty, d(300, 12000));
    }
    // the window is in the background (a game is being played): the light stops moving
    void StopDrift() {
        if (!drifting) return;
        drifting = false;
        var t1 = (TranslateTransform)N<Ellipse>("Glow1").RenderTransform; var t2 = (TranslateTransform)N<Ellipse>("Glow2").RenderTransform;
        foreach (var t in new[] { t1, t2 }) {
            t.BeginAnimation(TranslateTransform.XProperty, null); t.BeginAnimation(TranslateTransform.YProperty, null);
            t.X = 0; t.Y = 0;
        }
    }
    void LoadIconsLater(List<Game> list) {
        if (list.Count == 0) return;
        W.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
            foreach (var g in list.Take(4)) { Icons.Load(g); ApplyIcon(g); }
            LoadIconsLater(list.Skip(4).ToList());
        }));
    }
    void ApplyIcon(Game g) {
        var u = (GameUi)g.Ui; var src = (ImageSource)g.Icon;
        u.Icon.Source = src; u.Icon.Margin = new Thickness(g.IconIsArt ? 0 : 26);
        if (u.CIcon != null) { u.CIcon.Source = src; u.CIcon.Margin = new Thickness(g.IconIsArt ? 0 : 12); }
        if (u.LIcon != null) u.LIcon.Source = src;
        if (u.Home != null) { u.Home.Icon.Source = src; u.Home.Icon.Margin = new Thickness(g.IconIsArt ? 0 : 52); }
        if (g == sel) { N<Image>("HeroIcon").Source = src; N<Image>("HeroArt").Source = src; N<Image>("GridIcon").Source = src; }
    }
    void Snapshot(string file) {
        var root = N<Border>("Root");
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using (var fs = File.Create(file)) enc.Save(fs);
    }

    // ---------- cards ----------
    void BuildCards() {
        string tileXaml = ResText("Tile.xaml");
        foreach (var g in Games) {
            var t = (Grid)XamlReader.Parse(tileXaml);
            var u = new GameUi {
                Tile = t, Box = (Grid)t.FindName("Box"), Glow = (Border)t.FindName("Glow"), GlowFx = (DropShadowEffect)t.FindName("GlowFx"),
                Face = (Border)t.FindName("Face"), Shade = (Border)t.FindName("Shade"), Sel = (Border)t.FindName("Sel"), Dot = (Ellipse)t.FindName("Dot"),
                Name = (TextBlock)t.FindName("Name"), Sub = (TextBlock)t.FindName("Sub"), Icon = (Image)t.FindName("Icon")
            };
            u.Scale = (ScaleTransform)u.Box.RenderTransform;
            u.ShineEl = (UIElement)t.FindName("Shine");
            u.Shine = (TranslateTransform)((TransformGroup)u.ShineEl.RenderTransform).Children[1];
            g.Ui = u;
            u.Name.Text = g.Name; u.Sub.Text = "";
            u.GlowFx.Color = Ui.Col(g.Accent); u.Glow.Background = Ui.Brush(g.Accent);
            u.Face.Background = Ui.Gradient(g.Accent, g.Accent2);
            g.Icon = null;
            t.Tag = g;
            t.MouseEnter += (s, e) => {
                if (g == sel) return;
                u.Shade.BeginAnimation(UIElement.OpacityProperty, Anim(0.1, 150));
                u.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1.05, 180)); u.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1.05, 180));
            };
            t.MouseLeave += (s, e) => {
                if (g == sel) return;
                u.Shade.BeginAnimation(UIElement.OpacityProperty, Anim(0.35, 250));
                u.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1, 250)); u.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1, 250));
            };
            t.MouseLeftButtonUp += (s, e) => CardClicked(g, e);
            t.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { PrimaryAction(g); e.Handled = true; } };
        }
    }
    void CardClicked(Game g, MouseButtonEventArgs e) { padUsed = false; HideRing(); if (g != sel) ShowGame(g); }
    // compact cards and list rows are made the first time their view is shown
    void BuildCompact() {
        foreach (var g in Games) {
            var u = (GameUi)g.Ui;
            if (u.CTile != null) continue;
            var box = new Grid { Width = 72, Height = 72 };
            var face = new Border { CornerRadius = new CornerRadius(18), ClipToBounds = true, Background = Ui.Gradient(g.Accent, g.Accent2) };
            u.CIcon = new Image { Source = (ImageSource)g.Icon, Margin = new Thickness(g.IconIsArt ? 0 : 12) };
            RenderOptions.SetBitmapScalingMode(u.CIcon, BitmapScalingMode.HighQuality);
            face.Child = u.CIcon;
            u.CDot = new Ellipse { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -3, -3, 0), Stroke = Ui.Brush("#090C0F"), StrokeThickness = 2.5, Fill = Ui.Brush(Ui.Dim) };
            box.Children.Add(face); box.Children.Add(u.CDot);
            u.CName = new TextBlock { Text = g.Name, FontSize = 11.5, FontFamily = Ui.Semi, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                MaxHeight = 30, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 8, 0, 0), Foreground = Ui.Brush(Ui.Text) };
            u.CSub = new TextBlock { FontSize = 10, Foreground = Ui.Brush(Ui.Muted), TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
            var stack = new StackPanel();
            stack.Children.Add(box); stack.Children.Add(u.CName); stack.Children.Add(u.CSub);
            u.CTile = new Border { Width = 116, Padding = new Thickness(8, 10, 8, 8), Margin = new Thickness(3), CornerRadius = new CornerRadius(14),
                Background = Brushes.Transparent, BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand, Child = stack, Tag = g };
            u.CTile.MouseEnter += (s, e) => { if (g != sel) u.CTile.Background = Ui.Brush("#14FFFFFF"); };
            u.CTile.MouseLeave += (s, e) => { if (g != sel) u.CTile.Background = Brushes.Transparent; };
            u.CTile.MouseLeftButtonUp += (s, e) => CardClicked(g, e);
            u.CTile.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { PrimaryAction(g); e.Handled = true; } };
        }
    }
    static readonly string[][] ListCols = {
        new[] { "", "44" }, new[] { "GAME", "*" }, new[] { "SOURCE", "190" }, new[] { "INSTALLED", "110" }, new[] { "LATEST", "130" },
        new[] { "STATUS", "180" }, new[] { "PLAYED", "100" }, new[] { "LAST PLAYED", "100" }
    };
    static Grid ListGrid() {
        var gr = new Grid();
        foreach (var c in ListCols)
            gr.ColumnDefinitions.Add(new ColumnDefinition { Width = c[1] == "*" ? new GridLength(1, GridUnitType.Star) : new GridLength(double.Parse(c[1])), MinWidth = c[1] == "*" ? 200 : 0 });
        return gr;
    }
    void BuildList() {
        var head = N<Grid>("ListHeader");
        if (head.Children.Count == 0) {
            var hg = ListGrid();
            // clicking a heading sorts by it
            var sorts = new[] { null, "name", "default", "installed", null, "installed", "recent", "recent" };
            for (int i = 1; i < ListCols.Length; i++) {
                var tb = new TextBlock { Text = ListCols[i][0], FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Muted), VerticalAlignment = VerticalAlignment.Center };
                string s = sorts[i];
                if (s != null) { tb.Cursor = Cursors.Hand; tb.ToolTip = "Sort"; tb.MouseLeftButtonUp += (o, e) => SetSort(s); }
                Grid.SetColumn(tb, i); hg.Children.Add(tb);
            }
            head.Children.Add(hg);
        }
        foreach (var g in Games) {
            var u = (GameUi)g.Ui;
            if (u.LRow != null) continue;
            var gr = ListGrid();
            u.LIcon = new Image { Source = (ImageSource)g.Icon, Width = 30, Height = 30, HorizontalAlignment = HorizontalAlignment.Left };
            RenderOptions.SetBitmapScalingMode(u.LIcon, BitmapScalingMode.HighQuality);
            u.LName = Cell(g.Name, 14, Ui.Text, 1); u.LName.FontFamily = Ui.Semi;
            u.LSource = Cell(Strip(g.Source), 11, Ui.Muted, 2);
            u.LInst = Cell("", 12.5, Ui.Text, 3); u.LLatest = Cell("", 12.5, Ui.Text, 4);
            var st = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            u.LDot = new Ellipse { Width = 8, Height = 8, Fill = Ui.Brush(Ui.Muted), VerticalAlignment = VerticalAlignment.Center };
            u.LStatus = new TextBlock { FontSize = 12.5, Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            st.Children.Add(u.LDot); st.Children.Add(u.LStatus); Grid.SetColumn(st, 5);
            u.LPlayed = Cell("", 12.5, Ui.Muted, 6); u.LLast = Cell("", 12.5, Ui.Muted, 7);
            gr.Children.Add(u.LIcon); foreach (var c in new UIElement[] { u.LName, u.LSource, u.LInst, u.LLatest, st, u.LPlayed, u.LLast }) gr.Children.Add(c);
            u.LRow = new Border { Child = gr, Height = 46, Padding = new Thickness(10, 0, 6, 0), CornerRadius = new CornerRadius(10), Background = Brushes.Transparent,
                Cursor = Cursors.Hand, Tag = g, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Margin = new Thickness(0, 1, 0, 1) };
            u.LRow.MouseEnter += (s, e) => { if (g != sel) u.LRow.Background = Ui.Brush("#10FFFFFF"); };
            u.LRow.MouseLeave += (s, e) => { if (g != sel) u.LRow.Background = Brushes.Transparent; };
            u.LRow.MouseLeftButtonUp += (s, e) => CardClicked(g, e);
            u.LRow.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { PrimaryAction(g); e.Handled = true; } };
        }
    }
    static TextBlock Cell(string text, double size, string color, int col) {
        var t = new TextBlock { Text = text, FontSize = size, Foreground = Ui.Brush(color), VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 12, 0) };
        Grid.SetColumn(t, col);
        return t;
    }
    static string Strip(string source) { int i = source.IndexOf("  •"); return i > 0 ? source.Substring(0, i) : source; }

    // ---------- status ----------
    struct St { public string Text, Color, Tile; }
    St StatusOf(Game g) {
        string v = g.Inst != null ? g.Inst.Label : "";
        
        switch (g.State) {
            case State.Checking: return new St { Text = "Checking...", Color = Ui.Muted, Tile = "Checking..." };
            case State.Missing: return new St { Text = "Not installed", Color = Ui.Muted, Tile = "Not installed" };
            case State.Installing: return new St { Text = "Installing...", Color = Ui.Gold, Tile = "Installing..." };
            case State.Uninstalling: return new St { Text = "Uninstalling...", Color = Ui.Gold, Tile = "Uninstalling..." };
            case State.Converting: return new St { Text = "Converting the NSP...", Color = Ui.Gold, Tile = "Converting NSP..." };
            case State.Offline: return new St { Text = "Couldn't check", Color = Ui.Red, Tile = v + "  •  offline" };
            case State.Problem: return new St { Text = g.Latest != null ? g.Latest.Problem : "Can't update", Color = Ui.Red, Tile = v + "  •  can't update" };
            case State.Update: return new St { Text = "Update available", Color = Ui.Gold, Tile = "Update  •  " + g.Latest.Tag };
            case State.Updating: return new St { Text = "Updating...", Color = Ui.Gold, Tile = "Updating..." };
            default: return new St { Text = "Up to date", Color = Ui.Teal, Tile = v };
        }
    }
    bool Pulses(Game g) { return g.State == State.Update || g.IsBusy; }
    void Pulse(UIElement el, bool on) {
        if (on) { var a = Calm(Forever(Anim(0.25, 900, 1, EasingMode.EaseInOut))); a.AutoReverse = true; el.BeginAnimation(UIElement.OpacityProperty, a); }
        else { el.BeginAnimation(UIElement.OpacityProperty, null); el.Opacity = 1; }
    }
    void UpdateGameUi(Game g) {
        var u = (GameUi)g.Ui; var st = StatusOf(g);
        bool gold = g.State == State.Update || g.IsBusy;
        u.Sub.Text = st.Tile; u.Sub.Foreground = Ui.Brush(gold ? Ui.Gold : Ui.Muted);
        u.Dot.Fill = Ui.Brush(st.Color); u.Dot.Visibility = g.State == State.Missing ? Visibility.Collapsed : Visibility.Visible;
        bool p = Pulses(g);
        if (u.CTile != null) {
            u.CSub.Text = st.Tile; u.CSub.Foreground = u.Sub.Foreground;
            u.CDot.Fill = u.Dot.Fill; u.CDot.Visibility = u.Dot.Visibility;
        }
        if (u.LRow != null) {
            u.LInst.Text = g.Inst != null ? g.Inst.Label : "-";
            u.LLatest.Text = g.Latest != null ? g.Latest.Tag : g.State == State.Checking ? "..." : "-";
            u.LLatest.Foreground = Ui.Brush(g.State == State.Update ? Ui.Gold : Ui.Text);
            u.LStatus.Text = st.Text; u.LStatus.Foreground = Ui.Brush(g.State == State.Missing ? Ui.Muted : Ui.Text);
            u.LDot.Fill = Ui.Brush(st.Color);
            double total = Playtime.Total(g.Id); var last = Playtime.Last(g.Id);
            u.LPlayed.Text = total > 30 ? Playtime.Format(total) : "-";
            u.LLast.Text = last != DateTime.MinValue ? Playtime.When(last) : "-";
        }
        if (p != u.Pulsing) {
            u.Pulsing = p; Pulse(u.Dot, p);
            if (u.CDot != null) Pulse(u.CDot, p);
            if (u.LDot != null) Pulse(u.LDot, p);
        }
    }
    void UpdateViews() {
        foreach (var g in Games) UpdateGameUi(g);
        UpdateHero();
        int n = Listed().Count(g => g.State == State.Update);
        var all = N<Button>("BtnAll");
        all.IsEnabled = n > 0 && !running;
        all.Visibility = n > 0 || running ? Visibility.Visible : Visibility.Collapsed;
        N<TextBlock>("BtnAllText").Text = n > 1 ? "Update all  (" + n + ")" : "Update all";
        UpdateChips();
        UpdateHomeChrome();
        ApplySort(false);
        ApplyFilter();
    }
    // the selected game's page and buttons
    void UpdateHero() {
        var g = sel;
        if (g == null) return;
        var st = StatusOf(g);
        N<TextBlock>("HeroInst").Text = g.Inst != null ? g.Inst.Label : "-";
        N<TextBlock>("HeroLatest").Text = g.Latest != null ? g.Latest.Tag : g.State == State.Checking ? "..." : g.State == State.Missing ? "Install to look up" : "-";
        N<TextBlock>("HeroStatus").Text = st.Text;
        double total = Playtime.Total(g.Id); var last = Playtime.Last(g.Id);
        var chip = N<Border>("PlayedChip");
        if (total > 30 || last != DateTime.MinValue) {
            N<TextBlock>("HeroPlayed").Text = Playtime.Format(total) + (last != DateTime.MinValue ? "  •  " + Playtime.When(last) : "");
            chip.Visibility = Visibility.Visible;
        } else chip.Visibility = Visibility.Collapsed;
        N<Ellipse>("StatusDot").Fill = Ui.Brush(st.Color);
        Pulse(N<Ellipse>("StatusDot"), g.State == State.Checking || Pulses(g));
        bool installMode = g.State == State.Missing || g.State == State.Installing || g.State == State.Converting;
        string playText = g.State == State.Converting ? "CONVERTING..." : g.State == State.Installing ? "INSTALLING..." : installMode ? "INSTALL" : "PLAY";
        string playIcon = installMode ? "" : "";
        bool playOk;
        string dummy;
        if (installMode) {
            playOk = g.State == State.Missing && !running;
            N<TextBlock>("HeroNote").Text = "You need: " + g.Needs;
            N<TextBlock>("HeroNote").Visibility = Visibility.Visible;
        } else {
            playOk = File.Exists(Engine.PlayFile(g, out dummy, false)) && g.State != State.Updating && g.State != State.Uninstalling;
            N<TextBlock>("HeroNote").Visibility = Visibility.Collapsed;
        }
        bool canUpdate = g.State == State.Update && !running;
        string upText = g.State == State.Update ? (g.Latest.Size > 0 ? (g.Latest.Size >= 1048576 ? string.Format("Update  ({0:N0} MB)", g.Latest.Size / 1048576) : string.Format("Update  ({0:N0} KB)", g.Latest.Size / 1024)) : "Update") : g.State == State.Updating ? "Updating..." : "Up to date";
        bool showUpdate = !installMode;
        bool showUninst = g.Inst != null && !installMode && g.State != State.Uninstalling;
        bool folderOk = Directory.Exists(g.Dir);
        // the page (row view) and the bar above the other views
        foreach (var pre in new[] { "", "G" }) {
            N<TextBlock>(pre == "" ? "PlayText" : "GPlayText").Text = playText;
            N<TextBlock>(pre == "" ? "PlayIcon" : "GPlayIcon").Text = playIcon;
            N<Button>(pre + "BtnPlay").IsEnabled = playOk;
            var up = N<Button>(pre + "BtnUpdate");
            up.IsEnabled = canUpdate; up.Visibility = showUpdate ? Visibility.Visible : Visibility.Collapsed;
            up.BorderBrush = Ui.Brush(g.State == State.Update ? Ui.Gold : "#33FFFFFF");
            up.Foreground = Ui.Brush(g.State == State.Update ? "#F2D27A" : Ui.Text);
            N<TextBlock>(pre + "BtnUpdateText").Text = pre == "G" && g.State != State.Update && g.State != State.Updating ? "Up to date" : upText;
            N<Button>(pre + "BtnFolder").IsEnabled = folderOk;
            var un = N<Button>(pre + "BtnUninstall");
            un.Visibility = showUninst ? Visibility.Visible : Visibility.Collapsed; un.IsEnabled = !running;
        }
        N<Button>("BtnCheck").IsEnabled = !running && !checking;
        N<Button>("BtnSettings").IsEnabled = !running;
        N<TextBlock>("GridName").Text = g.Name;
        N<TextBlock>("GridState").Text = st.Text + (g.Inst != null ? "  •  " + g.Inst.Label : "") + (g.Latest != null && g.State == State.Update ? "  →  " + g.Latest.Tag : "");
        N<Ellipse>("GridDot").Fill = Ui.Brush(st.Color);
        if (padUsed) FixFocus();
    }

    // ---------- library order, filter and view ----------
    List<Game> Listed() { return Games.Where(g => !g.Hidden).ToList(); }
    List<Game> Sorted() {
        IEnumerable<Game> l = Games;
        switch (sort) {
            case "installed": return l.OrderBy(Catalog.SeriesOf).ThenBy(g => g.Inst == null ? 1 : 0).ThenBy(g => g.Rank).ToList();
            case "name": return l.OrderBy(g => System.Text.RegularExpressions.Regex.Replace(g.Name, "^The ", ""), StringComparer.CurrentCultureIgnoreCase).ToList();
            case "recent": {
                // the most recently played first, then the never-played in the library's order
                return l.OrderByDescending(g => Playtime.Last(g.Id)).ThenBy(Catalog.SeriesOf).ThenBy(g => g.Rank).ToList();
            }
            default: return l.OrderBy(Catalog.SeriesOf).ThenBy(g => g.Rank).ToList();
        }
    }
    static string SortLabel(string s) { return s == "installed" ? "Installed first" : s == "recent" ? "Recently played" : s == "name" ? "A–Z" : "Series"; }
    Panel ViewPanel() {
        switch (view) {
            case View.Grid: return N<WrapPanel>("GridTiles");
            case View.Compact: return N<WrapPanel>("CompactTiles");
            case View.List: return N<StackPanel>("ListRows");
            case View.Home: return homeTiles;
            default: return N<StackPanel>("Tiles");
        }
    }
    FrameworkElement ElementOf(Game g) {
        var u = (GameUi)g.Ui;
        return view == View.Compact ? (FrameworkElement)u.CTile : view == View.List ? (FrameworkElement)u.LRow : view == View.Home ? (FrameworkElement)u.Home.Root : u.Tile;
    }
    void ApplySort(bool force) {
        var o = Sorted();
        sortText.Text = "Sort: " + SortLabel(sort);
        var panel = ViewPanel();
        if (!force && panel.Children.Count > 0 && o.SequenceEqual(order)) return;
        order = o;
        foreach (var p in new Panel[] { N<StackPanel>("Tiles"), N<WrapPanel>("GridTiles"), N<WrapPanel>("CompactTiles"), N<StackPanel>("ListRows") }) p.Children.Clear();
        if (homeTiles != null) homeTiles.Children.Clear();
        if (view == View.Home) { foreach (var g in o) homeTiles.Children.Add(((GameUi)g.Ui).Home.Root); AddHomeSlots(); order = o; QueueScroll(); return; }
        int prev = -1;
        foreach (var g in o) {
            var u = (GameUi)g.Ui;
            int s = Catalog.SeriesOf(g);
            if (view == View.Grid) u.Tile.Margin = new Thickness(12, 10, 12, 14);
            else if (view == View.Row) u.Tile.Margin = new Thickness(prev >= 0 && s != prev && sort != "name" && sort != "recent" ? 36 : 4, 0, 4, 0);
            prev = s;
            panel.Children.Add(ElementOf(g));
        }
        QueueScroll();
    }
    bool Shown(Game g) {
        if (g.Hidden) return false;
        string q = N<TextBox>("SearchBox").Text.Trim();
        if (q.Length > 0 && (g.Name + " " + g.Short + " " + g.Source + " " + g.Id).IndexOf(q, StringComparison.CurrentCultureIgnoreCase) < 0) return false;
        switch (filter) {
            case "installed": return g.Inst != null;
            case "updates": return g.State == State.Update || g.State == State.Updating;
            case "new": return g.Inst == null;
            default: return true;
        }
    }
    List<Game> ShownGames() { return order.Where(g => ElementOf(g).Visibility == Visibility.Visible).ToList(); }
    void ApplyFilter() {
        int shown = 0;
        foreach (var g in order) {
            bool on = Shown(g);
            ElementOf(g).Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on) shown++;
        }
        int total = Listed().Count;
        N<TextBlock>("RowCount").Text = shown == total ? "•  " + total + " GAMES" : "•  " + shown + " OF " + total;
        var nm = N<TextBlock>("NoMatch");
        nm.Visibility = shown > 0 ? Visibility.Collapsed : Visibility.Visible;
        string q = N<TextBox>("SearchBox").Text.Trim();
        nm.Text = q.Length > 0 ? "No games match \"" + q + "\"." : "No games here.";
        if (shown > 0 && sel != null && ElementOf(sel).Visibility != Visibility.Visible) { var f = ShownGames().FirstOrDefault(); if (f != null) ShowGame(f); }
    }
    void SetFilter(string id) { filter = id; UpdateChips(); ApplyFilter(); QueueScroll(); }
    void CycleFilter(int dir) {
        var ids = new[] { "all", "installed", "updates", "new" };
        int i = (Array.IndexOf(ids, filter) + dir + ids.Length) % ids.Length;
        SetFilter(ids[i]);
    }
    void SetSort(string s) {
        sort = s; ApplySort(false);
        Settings.Set("SortMode", sort); Settings.Save();
        var st = new ScaleTransform(1, 1); sortChip.RenderTransformOrigin = new Point(0.5, 0.5); sortChip.RenderTransform = st;
        var a = Anim(1, 260, 0.9); st.BeginAnimation(ScaleTransform.ScaleXProperty, a); st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }
    void CycleSort() {
        var ids = new[] { "default", "installed", "recent", "name" };
        SetSort(ids[(Array.IndexOf(ids, sort) + 1) % ids.Length]);
    }

    // the four views; the library area covers the page in all but the row view
    void SetView(View v, bool save) {
        view = v;
        if (v == View.Compact) BuildCompact();
        if (v == View.List) BuildList();
        if (v == View.Home) BuildHome();
        bool row = v == View.Row;
        var lib = (FrameworkElement)N<ScrollViewer>("TileScroll").Parent;
        Grid.SetRow(lib, row ? 2 : 1); Grid.SetRowSpan(lib, row ? 1 : 2);
        N<StackPanel>("HeroContent").Visibility = row ? Visibility.Visible : Visibility.Collapsed;
        N<ScrollViewer>("TileScroll").Visibility = row ? Visibility.Visible : Visibility.Collapsed;
        N<ScrollViewer>("GridScroll").Visibility = v == View.Grid ? Visibility.Visible : Visibility.Collapsed;
        N<ScrollViewer>("CompactScroll").Visibility = v == View.Compact ? Visibility.Visible : Visibility.Collapsed;
        N<Grid>("ListView").Visibility = v == View.List ? Visibility.Visible : Visibility.Collapsed;
        N<Grid>("GridInfo").Visibility = row || v == View.Home ? Visibility.Collapsed : Visibility.Visible;
        SetHomeMode(v == View.Home);
        foreach (var kv in viewButtons) {
            bool on = kv.Key == v;
            kv.Value.Background = Ui.Brush(on ? "#F2EFE6" : "#00000000");
            ((TextBlock)kv.Value.Child).Foreground = Ui.Brush(on ? "#0A0E11" : "#A9B6BE");
        }
        foreach (var g in Games) UpdateGameUi(g);
        ApplySort(true);
        ApplyFilter();
        if (sel != null) MarkSelected(null, sel);
        UpdateHero();
        if (zone != "cards" && !row) { zone = "cards"; HideRing(); }
        SetHints();
        if (save && args.Snapshot == null) { Settings.Set("ViewMode", ViewName(v)); Settings.Save(); }
    }
    void CycleView(int dir) { SetView((View)(((int)view + dir + 5) % 5), true); }

    // ---------- selection ----------
    void MarkSelected(Game prev, Game g) {
        foreach (var o in prev == null ? Games : (IEnumerable<Game>)new[] { prev, g }) {
            if (o == null) continue;
            var u = (GameUi)o.Ui; bool on = o == g;
            double s = on ? 1.12 : 1.0;
            u.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(s, 320)); u.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(s, 320));
            u.Sel.BeginAnimation(UIElement.OpacityProperty, Anim(on ? 1 : 0, 250));
            if (on && prev != null) {
                u.Shine.BeginAnimation(TranslateTransform.XProperty, Anim(260, 750, -40, EasingMode.EaseInOut));
                var so = new DoubleAnimationUsingKeyFrames { Duration = Dur(750) };
                so.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.15))); so.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.7))); so.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                u.ShineEl.BeginAnimation(UIElement.OpacityProperty, so);
            }
            // the glow (a blurred shadow) is drawn only for the selected card
            if (on) { u.Glow.Visibility = Visibility.Visible; u.Glow.BeginAnimation(UIElement.OpacityProperty, Anim(0.75, 400)); }
            else if (u.Glow.Visibility == Visibility.Visible) {
                var ga = Anim(0, 300); var glow = u.Glow; var me = o;
                ga.Completed += (s2, e2) => { if (sel != me) glow.Visibility = Visibility.Collapsed; };
                u.Glow.BeginAnimation(UIElement.OpacityProperty, ga);
            }
            u.Shade.BeginAnimation(UIElement.OpacityProperty, Anim(on ? 0 : 0.35, 250));
            u.Name.BeginAnimation(UIElement.OpacityProperty, Anim(on ? 1 : 0.7, 250));
            if (u.CTile != null) { u.CTile.BorderBrush = on ? Ui.Brush("#F2EFE6") : Brushes.Transparent; u.CTile.Background = on ? Ui.Brush("#1FFFFFFF") : Brushes.Transparent; }
            if (u.LRow != null) { u.LRow.Background = on ? Ui.Brush("#1CFFFFFF") : Brushes.Transparent; u.LRow.BorderBrush = on ? Ui.Brush(o.Accent) : Brushes.Transparent; }
        }
        if (view == View.Home) MarkHome(prev, g);
    }
    void ShowGame(Game g) {
        var prev = sel; sel = g;
        MarkSelected(prev, g);   // only the two cards that change (all of them the first time)
        // backdrop colours
        ((GradientStop)W.FindName("HeroStop1")).BeginAnimation(GradientStop.ColorProperty, ColorAnim(Ui.Alpha(g.Accent, 0x70), 700));
        ((GradientStop)W.FindName("HeroStop2")).BeginAnimation(GradientStop.ColorProperty, ColorAnim(Ui.Alpha(g.Accent2, 0x26), 700));
        foreach (var l in new[] { new[] { "Glow1", g.Accent }, new[] { "Glow2", g.Accent2 } }) {
            ((GradientStop)W.FindName(l[0] + "A")).BeginAnimation(GradientStop.ColorProperty, ColorAnim(Ui.Alpha(l[1], 0xFF), 700));
            ((GradientStop)W.FindName(l[0] + "M")).BeginAnimation(GradientStop.ColorProperty, ColorAnim(Ui.Alpha(l[1], 0x80), 700));
            ((GradientStop)W.FindName(l[0] + "B")).BeginAnimation(GradientStop.ColorProperty, ColorAnim(Ui.Alpha(l[1], 0x00), 700));
        }
        var title = N<TextBlock>("HeroTitle");
        title.Text = g.Name; title.FontSize = g.Name.Length > 26 ? 42 : 56;
        var ink = Ui.Col(Ui.Text);
        var tg = new LinearGradientBrush(ink, Theme.Mix(ink, Ui.Col(g.Accent), 0.55), 0); tg.Freeze();
        title.Foreground = tg;
        ((DropShadowEffect)W.FindName("PlayGlow")).Color = Theme.Mix(Colors.White, Ui.Col(g.Accent), 0.6);
        N<TextBlock>("HeroSource").Text = g.Source; N<TextBlock>("HeroSource").Foreground = Ui.Brush(g.Accent);
        var src = (ImageSource)g.Icon;
        N<Image>("HeroIcon").Source = src; N<Image>("HeroArt").Source = src; N<Image>("GridIcon").Source = src;
        N<TextBlock>("NotesGame").Text = g.Name.ToUpper(); N<TextBlock>("NotesGame").Foreground = Ui.Brush(g.Accent);
        N<Border>("TabNotesLine").Background = Ui.Brush(g.Accent); N<Border>("TabActivityLine").Background = Ui.Brush(g.Accent);
        if (N<Grid>("NotesLayer").Visibility == Visibility.Visible) FillNotes(g); else notesFor = null;
        UpdateHero();
        QueueScroll();
        if (view == View.Row) {
            var hc = N<StackPanel>("HeroContent");
            hc.BeginAnimation(UIElement.OpacityProperty, Anim(1, 420, 0));
            ((TranslateTransform)hc.RenderTransform).BeginAnimation(TranslateTransform.XProperty, Anim(0, 520, 40));
            var art = N<Image>("HeroArt");
            art.BeginAnimation(UIElement.OpacityProperty, Anim(0.2, 900, 0));
            var sc = (ScaleTransform)((TransformGroup)art.RenderTransform).Children[0];
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1, 1100, 1.16)); sc.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1, 1100, 1.16));
        } else {
            var gi = N<Grid>("GridInfo");
            gi.BeginAnimation(UIElement.OpacityProperty, Anim(1, 260, 0.4));
        }
    }
    void StepGame(int dir) {
        var list = ShownGames(); if (list.Count == 0) return;
        int i = list.IndexOf(sel);
        int j = i < 0 ? 0 : Math.Max(0, Math.Min(list.Count - 1, i + dir));
        if (list[j] != sel) ShowGame(list[j]);
    }
    // grid views: up / down go to the nearest card in the row above or below; list: the next row
    void MoveVertical(int dir) {
        if (view == View.List) { StepGame(dir); return; }
        var list = ShownGames(); if (sel == null || list.Count == 0) return;
        Func<Game, Point> pos = o => { var v = VisualTreeHelper.GetOffset(ElementOf(o)); return new Point(v.X, v.Y); };
        var p0 = pos(sel);
        Game best = null; double? bestRow = null; double bestDx = double.MaxValue;
        foreach (var o in list) {
            if (o == sel) continue;
            var p = pos(o); double dy = p.Y - p0.Y;
            if ((dir > 0 && dy <= 20) || (dir < 0 && dy >= -20)) continue;
            if (bestRow == null || Math.Abs(dy) < Math.Abs(bestRow.Value) - 20) { bestRow = dy; bestDx = double.MaxValue; best = null; }
            if (Math.Abs(dy - bestRow.Value) <= 20) { double dx = Math.Abs(p.X - p0.X); if (dx < bestDx) { bestDx = dx; best = o; } }
        }
        if (best != null) ShowGame(best);
    }

    // ---------- the row glides so the selected card is in the middle ----------
    double? scrollTo;
    DispatcherTimer scrollTimer;
    void ScrollToSelected() {
        var g = sel; if (g == null) return;
        var el = ElementOf(g);
        if (el.Visibility != Visibility.Visible) return;
        if (view == View.Home) { ScrollHome(); return; }
        if (view != View.Row) { el.BringIntoView(); return; }
        var sv = N<ScrollViewer>("TileScroll"); var tiles = N<StackPanel>("Tiles");
        if (sv.ViewportWidth <= 0 || !tiles.IsAncestorOf(el)) return;
        double x = el.TransformToAncestor(tiles).Transform(new Point(0, 0)).X + tiles.Margin.Left;
        double target = Math.Max(0, Math.Min(x + el.ActualWidth / 2 - sv.ViewportWidth / 2, sv.ScrollableWidth));
        scrollTo = target;
        if (scrollTimer == null) {
            scrollTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
            scrollTimer.Tick += (s, e) => {
                if (scrollTo == null) { scrollTimer.Stop(); return; }
                double cur = sv.HorizontalOffset, next = cur + (scrollTo.Value - cur) * 0.2;
                if (Math.Abs(scrollTo.Value - next) < 0.5) { next = scrollTo.Value; scrollTo = null; }
                sv.ScrollToHorizontalOffset(next);
            };
        }
        scrollTimer.Start();
    }
    void QueueScroll() { W.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ScrollToSelected)); }

    // ---------- chips: filters, sort and view ----------
    readonly Dictionary<string, Border> filterChips = new Dictionary<string, Border>();
    readonly Dictionary<View, Border> viewButtons = new Dictionary<View, Border>();
    Border sortChip; TextBlock sortText;
    static readonly string[][] FilterDefs = { new[] { "all", "All" }, new[] { "installed", "Installed" }, new[] { "updates", "Updates" }, new[] { "new", "Not installed" } };
    Border Chip(UIElement child, double padL) {
        return new Border { CornerRadius = new CornerRadius(15), Height = 30, Cursor = Cursors.Hand, Padding = new Thickness(padL, 0, 14, 0),
            Margin = new Thickness(0, 0, 6, 0), BorderThickness = new Thickness(1), Background = Ui.Brush("#3D0E1418"), BorderBrush = Ui.Brush("#33FFFFFF"), Child = child };
    }
    void BuildChips() {
        var chips = N<StackPanel>("Chips");
        foreach (var f in FilterDefs) {
            var t = new TextBlock { Text = f[1], FontSize = 12, VerticalAlignment = VerticalAlignment.Center, FontFamily = Ui.Semi };
            var b = Chip(t, 14); string id = f[0];
            b.MouseLeftButtonUp += (s, e) => SetFilter(id);
            filterChips[id] = b; chips.Children.Add(b);
        }
        var sr = new StackPanel { Orientation = Orientation.Horizontal };
        sr.Children.Add(new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0), Foreground = Ui.Brush("#A9B6BE") });
        sortText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Brush(Ui.Text), FontFamily = Ui.Semi };
        sr.Children.Add(sortText);
        sortChip = Chip(sr, 12); sortChip.Margin = new Thickness(10, 0, 0, 0); sortChip.ToolTip = "Sort the library (LS on a controller)";
        sortChip.MouseLeftButtonUp += (s, e) => CycleSort();
        chips.Children.Add(sortChip);
        // view switch: four buttons in one pill
        var vs = new StackPanel { Orientation = Orientation.Horizontal };
        var defs = new[] { new object[] { View.Home, "\uE80F", "Home screen" }, new object[] { View.Row, "", "Row and game page" }, new object[] { View.Grid, "", "Large grid" },
                           new object[] { View.Compact, "", "Compact grid" }, new object[] { View.List, "", "List" } };
        foreach (var d in defs) {
            var v = (View)d[0];
            var b = new Border { Width = 34, Height = 26, CornerRadius = new CornerRadius(13), Cursor = Cursors.Hand, ToolTip = (string)d[2] + "  (Ctrl+G or RS)",
                Background = Brushes.Transparent, Child = new TextBlock { Text = (string)d[1], FontFamily = Ui.Mdl, FontSize = 12.5,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Brush("#A9B6BE") } };
            b.MouseLeftButtonUp += (s, e) => { if (view != v) SetView(v, true); };
            viewButtons[v] = b; vs.Children.Add(b);
        }
        var pill = new Border { CornerRadius = new CornerRadius(15), Height = 30, Padding = new Thickness(1), Margin = new Thickness(6, 0, 0, 0),
            BorderThickness = new Thickness(1), Background = Ui.Brush("#3D0E1418"), BorderBrush = Ui.Brush("#33FFFFFF"), Child = vs };
        chips.Children.Add(pill);
    }
    void UpdateChips() {
        var list = Listed();
        foreach (var f in FilterDefs) {
            bool on = f[0] == filter;
            int c = f[0] == "installed" ? list.Count(g => g.Inst != null) : f[0] == "updates" ? list.Count(g => g.State == State.Update) : f[0] == "new" ? list.Count(g => g.Inst == null) : list.Count;
            var b = filterChips[f[0]]; var t = (TextBlock)b.Child;
            t.Text = f[0] == "all" ? f[1] : f[1] + "  " + c;
            b.Background = Ui.Brush(on ? "#F2EFE6" : "#3D0E1418"); b.BorderBrush = Ui.Brush(on ? "#F2EFE6" : "#33FFFFFF");
            t.Foreground = Ui.Brush(on ? "#0A0E11" : c == 0 ? Ui.Dim : Ui.Text);
        }
    }

    // ---------- full screen: the whole monitor, taskbar and docks included; on top only while active ----------
    void SetFullscreen(bool on) {
        if (on == isFull && fullInit) return;
        fullInit = true; isFull = on;
        var hwnd = new WindowInteropHelper(W).Handle;
        if (on) {
            if (W.WindowState == WindowState.Normal) normalRect = new Rect(W.Left, W.Top, W.Width, W.Height);
            var px = System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;
            var src = PresentationSource.FromVisual(W);
            var m = src != null ? src.CompositionTarget.TransformFromDevice : Matrix.Identity;
            var tl = m.Transform(new Point(px.Left, px.Top)); var br = m.Transform(new Point(px.Right, px.Bottom));
            W.WindowState = WindowState.Normal; W.ResizeMode = ResizeMode.NoResize;
            W.Left = tl.X; W.Top = tl.Y; W.Width = br.X - tl.X; W.Height = br.Y - tl.Y;
            W.Topmost = W.IsActive;
            IconX.WindowCorners(hwnd, false);
            N<TextBlock>("FullIcon").Text = ""; N<Button>("BtnFull").ToolTip = "Leave full screen (F11)";
        } else {
            W.Topmost = false; W.WindowState = WindowState.Normal;
            if (normalRect.Width > 0) { W.Left = normalRect.X; W.Top = normalRect.Y; W.Width = normalRect.Width; W.Height = normalRect.Height; }
            W.ResizeMode = ResizeMode.CanResize;
            IconX.WindowCorners(hwnd, true);
            N<TextBlock>("FullIcon").Text = ""; N<Button>("BtnFull").ToolTip = "Full screen (F11)";
        }
        QueueScroll(); QueueRing();
    }
    void ToggleFullscreen() { SetFullscreen(!isFull); Settings.Set("Fullscreen", isFull ? "True" : ""); Settings.Save(); }

    // ---------- progress and status line ----------
    bool indet;
    void SetIndeterminate(bool on) {
        var shimmer = N<Rectangle>("Shimmer"); var tt = (TranslateTransform)shimmer.RenderTransform;
        if (on) {
            N<Border>("Fill").BeginAnimation(FrameworkElement.WidthProperty, Anim(0, 200));
            shimmer.BeginAnimation(UIElement.OpacityProperty, Anim(1, 200));
            tt.BeginAnimation(TranslateTransform.XProperty, Calm(Forever(Anim(N<Grid>("Track").ActualWidth + 220, 1400, -220, EasingMode.EaseInOut))));
        } else {
            shimmer.BeginAnimation(UIElement.OpacityProperty, Anim(0, 200));
            tt.BeginAnimation(TranslateTransform.XProperty, null);
        }
        indet = on;
    }
    void SetProgress(double f) {
        if (indet) SetIndeterminate(false);
        N<Border>("Fill").BeginAnimation(FrameworkElement.WidthProperty, Anim(N<Grid>("Track").ActualWidth * Math.Max(0, Math.Min(1, f)), 300));
    }
    void SetStep(string text, string color = Ui.Text, string detail = "", string pct = "") {
        N<TextBlock>("StepText").Text = text; N<TextBlock>("StepText").Foreground = Ui.Brush(color);
        N<TextBlock>("DetailText").Text = detail; N<TextBlock>("PctText").Text = pct;
    }

    // ---------- events ----------
    void PrimaryAction(Game g) {
        if (g != sel) ShowGame(g);
        if (g.State == State.Missing) TryAction("Installing " + g.Name, () => StartInstall(g));
        else if (!g.IsBusy) TryAction("Starting " + g.Name, () => StartGame(g));
    }
    void TryAction(string what, Action a) {
        try { a(); }
        catch (Exception e) {
            SetIndeterminate(false); SetStep(what + " failed: " + e.Message, Ui.Red); Log(what + " failed: " + e, Ui.Red);
            running = false; UpdateViews();
        }
    }
    void OpenFolder(string dir) { try { Directory.CreateDirectory(dir); System.Diagnostics.Process.Start("explorer.exe", Engine.Q(dir)); } catch { } }
    void WireEvents() {
        foreach (var pre in new[] { "", "G" }) {
            N<Button>(pre + "BtnPlay").Click += (s, e) => PrimaryAction(sel);
            N<Button>(pre + "BtnUpdate").Click += (s, e) => TryAction("Updating " + sel.Name, () => StartQueue(new List<Game> { sel }));
            N<Button>(pre + "BtnNotes").Click += (s, e) => OpenNotes("notes");
            N<Button>(pre + "BtnFolder").Click += (s, e) => OpenFolder(sel.Dir);
            N<Button>(pre + "BtnBackups").Click += (s, e) => OpenFolder(sel.Backups);
            N<Button>(pre + "BtnUninstall").Click += (s, e) => OpenUninstall(sel);
        }
        var glow = (DropShadowEffect)W.FindName("PlayGlow");
        N<Button>("BtnPlay").MouseEnter += (s, e) => glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, Anim(50, 250));
        N<Button>("BtnPlay").MouseLeave += (s, e) => glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, Anim(30, 400));
        N<Button>("BtnAll").Click += (s, e) => TryAction("Updating", () => StartQueue(Listed().Where(g => g.State == State.Update).ToList()));
        N<Button>("BtnCheck").Click += (s, e) => CheckAll(true);
        N<Button>("BtnSettings").Click += (s, e) => OpenSettings();
        N<Button>("BtnFull").Click += (s, e) => ToggleFullscreen();
        N<Button>("BtnMin").Click += (s, e) => W.WindowState = WindowState.Minimized;
        N<Button>("BtnClose").Click += (s, e) => W.Close();
        var bar = N<Grid>("TitleBar");
        bar.MouseLeftButtonDown += (s, e) => {
            if (e.OriginalSource is TextBox || isFull) return;
            if (e.ClickCount == 2) { W.WindowState = W.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; return; }
            try { W.DragMove(); } catch { }
        };
        // search: typing anywhere goes into the search box
        var box = N<TextBox>("SearchBox");
        N<Border>("SearchPill").MouseLeftButtonDown += (s, e) => box.Focus();
        box.TextChanged += (s, e) => {
            bool has = box.Text.Length > 0;
            N<TextBlock>("SearchHint").Visibility = has ? Visibility.Collapsed : Visibility.Visible;
            N<Button>("BtnClearSearch").Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilter();
            var first = ShownGames().FirstOrDefault();
            if (has && first != null && first != sel) ShowGame(first); else QueueScroll();
        };
        box.GotKeyboardFocus += (s, e) => N<Border>("SearchPill").BorderBrush = Ui.Brush("#99FFFFFF");
        box.LostKeyboardFocus += (s, e) => N<Border>("SearchPill").BorderBrush = Ui.Brush("#33FFFFFF");
        N<Button>("BtnClearSearch").Click += (s, e) => { box.Text = ""; Keyboard.ClearFocus(); };
        W.PreviewTextInput += (s, e) => {
            if (box.IsKeyboardFocused || AnyDialog()) return;
            if (e.Text.Length == 1 && (char.IsLetterOrDigit(e.Text[0]) || "&-.".IndexOf(e.Text[0]) >= 0)) {
                box.Focus(); box.Text += e.Text; box.CaretIndex = box.Text.Length; e.Handled = true;
            }
        };
        W.PreviewKeyDown += OnKey;
        W.Closing += (s, e) => {
            if (running && args.Snapshot == null) {
                var r = MessageBox.Show(W, "An update is still running. Closing this window won't stop the current game's update, but any game after it in the queue won't be updated.\n\nClose anyway?", "Recomp Launcher", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) { e.Cancel = true; return; }
            }
            if (args.Snapshot == null && sel != null) { Settings.Set("LastGame", sel.Id); Settings.Save(); }
        };
        W.Activated += (s, e) => { if (isFull) W.Topmost = true; padActive = true; };
        W.Deactivated += (s, e) => { W.Topmost = false; padActive = false; };
        W.SourceInitialized += (s, e) => {
            var hwnd = new WindowInteropHelper(W).Handle;
            try {
                string exe = Path.Combine(Paths.Root, "Recomp Launcher.exe");
                WindowAppId.Set(hwnd, "RecompLauncher.App", "\"" + exe + "\"", "Recomp Launcher", exe + ",0");
            } catch { }
            IconX.WindowCorners(hwnd, true);
        };
        // on a big screen everything scales up together, like a console menu on a TV (made for about 800 px high)
        var root = N<Border>("Root");
        root.SizeChanged += (s, e) => {
            double k = Math.Max(1.0, root.ActualHeight / 800.0);
            foreach (var n in new[] { "Main", "NotesLayer", "SettingsLayer", "RomLayer", "UninstLayer" }) ((FrameworkElement)W.FindName(n)).LayoutTransform = new ScaleTransform(k, k);
            N<Border>("RomCard").MaxHeight = Math.Max(300, root.ActualHeight / k - 40);
            var art = N<Image>("HeroArt"); art.Width = root.ActualHeight * 0.85; art.Height = art.Width;
            QueueScroll();
        };
    }
    bool AnyDialog() {
        return N<Grid>("SettingsLayer").Visibility == Visibility.Visible || N<Grid>("RomLayer").Visibility == Visibility.Visible ||
               N<Grid>("UninstLayer").Visibility == Visibility.Visible;
    }
    void OnKey(object s, KeyEventArgs e) {
        var k = e.Key; var box = N<TextBox>("SearchBox");
        bool notes = N<Grid>("NotesLayer").Visibility == Visibility.Visible;
        if (padUsed) { padUsed = false; HideRing(); SetHints(); }
        if (N<Grid>("UninstLayer").Visibility == Visibility.Visible) { if (k == Key.Escape) { CloseUninstall(); e.Handled = true; } return; }
        if (FbVisible()) { if (FbKey(k)) e.Handled = true; return; }
        if (N<Grid>("RomLayer").Visibility == Visibility.Visible) {
            if (k == Key.Escape) { CloseRomPicker(); e.Handled = true; }
            else if (k == Key.Enter && N<Button>("BtnRomInstall").IsEnabled && !(Keyboard.FocusedElement is TextBox)) { ConfirmRom(false); e.Handled = true; }
            return;
        }
        if (N<Grid>("SettingsLayer").Visibility == Visibility.Visible) { if (k == Key.Escape) { CloseSettings(); e.Handled = true; } return; }
        if (box.IsKeyboardFocused) {
            if (k == Key.Escape) { if (box.Text.Length > 0) box.Text = ""; else Keyboard.ClearFocus(); e.Handled = true; }
            else if (k == Key.Enter || k == Key.Down) { Keyboard.ClearFocus(); e.Handled = true; }
            return;
        }
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (view == View.Home && !notes && !ctrl && HomeKey(k)) { e.Handled = true; return; }
        switch (k) {
            case Key.F11: ToggleFullscreen(); e.Handled = true; break;
            case Key.Escape: if (notes) CloseNotes(); else if (isFull) ToggleFullscreen(); e.Handled = true; break;
            case Key.Up: if (!notes && view != View.Row) { MoveVertical(-1); e.Handled = true; } break;
            case Key.Down: if (!notes && view != View.Row) { MoveVertical(1); e.Handled = true; } break;
            case Key.Left: if (!notes) { StepGame(-1); e.Handled = true; } break;
            case Key.Right: if (!notes) { StepGame(1); e.Handled = true; } break;
            case Key.Home: if (!notes) { var f = ShownGames().FirstOrDefault(); if (f != null) ShowGame(f); e.Handled = true; } break;
            case Key.End: if (!notes) { var l = ShownGames().LastOrDefault(); if (l != null) ShowGame(l); e.Handled = true; } break;
            case Key.Enter: if (!notes && sel != null) { PrimaryAction(sel); e.Handled = true; } break;
            case Key.F5: if (!running && !checking) CheckAll(true); e.Handled = true; break;
            case Key.G: if (ctrl) { CycleView(1); e.Handled = true; } break;
            case Key.D1: case Key.D2: case Key.D3: case Key.D4: case Key.D5:
                if (ctrl) { SetView((View)(k - Key.D1), true); e.Handled = true; } break;
            case Key.F: if (ctrl) { box.Focus(); e.Handled = true; } break;
        }
    }
}
}
