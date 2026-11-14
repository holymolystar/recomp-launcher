// The home screen's other pages, in the same console style:
//  - All games in three layouts: big tiles, small tiles and a list (the header's layout buttons, RS or Ctrl+G)
//  - the game page: one game's versions, status and play time, with Start / Install, Update, What's new, its folder,
//    its save backups and Uninstall. Y on a controller, right-click, I or the menu key opens it; B / Esc goes back.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace RecompLauncher {

class HomeRowUi { public Border Root, Sel; public Image Icon; public Border Face; public TextBlock Name, Source, Versions, Status, Played; public Ellipse Dot; }
class PageButton { public Border Root; public TextBlock Glyph, Text; public Action Do; public bool Enabled = true; }

partial class Shell {
    const double SmallTileSize = 112;
    string allLayout = "big";   // big, small, list
    StackPanel homeLayoutPills;
    string homePage = "home";   // home, all, game
    string gameBack = "home";

    // ---------- All games layouts ----------
    FrameworkElement AllElement(Game g) {
        var u = (GameUi)g.Ui;
        return allLayout == "small" ? (FrameworkElement)u.HomeSmall.Root : allLayout == "list" ? (FrameworkElement)u.HomeRow.Root : u.HomeAll.Root;
    }
    void BuildLayoutPills(Panel host) {
        homeLayoutPills = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 0) };
        foreach (var d in new[] { new[] { "big", "", "Big tiles" }, new[] { "small", "", "Small tiles" }, new[] { "list", "", "List" } }) {
            string id = d[0];
            var b = new Border { Width = 40, Height = 32, CornerRadius = new CornerRadius(16), Margin = new Thickness(4, 0, 0, 0), Cursor = Cursors.Hand, Tag = id, ToolTip = d[2] + "  (RS or Ctrl+G)",
                Child = new TextBlock { Text = d[1], FontFamily = Ui.Mdl, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = Theme.Current.Light ? 0.15 : 0.3 } };
            b.MouseLeftButtonUp += (s, e) => SetAllLayout(id);
            homeLayoutPills.Children.Add(b);
        }
        host.Children.Insert(0, homeLayoutPills);
        allLayout = new[] { "big", "small", "list" }.Contains(Settings.Get("AllLayout")) ? Settings.Get("AllLayout") : "big";
        MarkLayoutPills();
    }
    void MarkLayoutPills() {
        foreach (Border b in homeLayoutPills.Children) {
            bool on = (string)b.Tag == allLayout;
            b.Background = Ui.Brush(on ? Ui.Teal : Raised);
            ((TextBlock)b.Child).Foreground = Ui.Brush(on ? (Theme.Current.Light ? "#ffffff" : "#0A0E11") : Ui.Text);
        }
    }
    void SetAllLayout(string id) {
        allLayout = id;
        MarkLayoutPills();
        if (args.Snapshot == null) { Settings.Set("AllLayout", id); Settings.Save(); }
        ApplySort(true); ApplyFilter(); MarkHome(null, sel); SizeListRows();
        homeAllTiles.BeginAnimation(UIElement.OpacityProperty, Anim(1, 200, 0));
    }
    void CycleAllLayout() {
        Sound.Play(Sfx.Move);
        var ids = new[] { "big", "small", "list" };
        SetAllLayout(ids[(Array.IndexOf(ids, allLayout) + 1) % 3]);
    }
    // list rows fill the page's width
    void SizeListRows() {
        if (homeAllScroll == null) return;
        double w = Math.Max(400, homeAllScroll.ViewportWidth - homeAllTiles.Margin.Left - homeAllTiles.Margin.Right - 4);
        foreach (var g in Games) { var r = ((GameUi)g.Ui).HomeRow; if (r != null) r.Root.Width = w; }
    }

    HomeRowUi MakeHomeRow(Game g) {
        var r = new HomeRowUi();
        var gr = new Grid();
        foreach (var w in new[] { 64.0, -1, 230, 170, 120 }) gr.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
        r.Icon = new Image { Source = (ImageSource)g.Icon, Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(r.Icon, BitmapScalingMode.HighQuality);
        r.Face = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(2), ClipToBounds = true, Background = Ui.Gradient(g.Accent, g.Accent2), Child = r.Icon, HorizontalAlignment = HorizontalAlignment.Left };
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) };
        r.Name = new TextBlock { Text = g.Name, FontSize = 16, Foreground = Ui.Brush(Ui.Text), TextTrimming = TextTrimming.CharacterEllipsis };
        r.Source = new TextBlock { Text = Strip(g.Source), FontSize = 11, Foreground = Ui.Brush(Ui.Muted), Margin = new Thickness(0, 2, 0, 0) };
        names.Children.Add(r.Name); names.Children.Add(r.Source);
        Grid.SetColumn(names, 1);
        r.Versions = new TextBlock { FontSize = 13, Foreground = Ui.Brush(Ui.Text), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 12, 0) };
        Grid.SetColumn(r.Versions, 2);
        var st = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        r.Dot = new Ellipse { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center };
        r.Status = new TextBlock { FontSize = 13, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        st.Children.Add(r.Dot); st.Children.Add(r.Status);
        Grid.SetColumn(st, 3);
        r.Played = new TextBlock { FontSize = 13, Foreground = Ui.Brush(Ui.Muted), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 14, 0) };
        Grid.SetColumn(r.Played, 4);
        gr.Children.Add(r.Face); gr.Children.Add(names); gr.Children.Add(r.Versions); gr.Children.Add(st); gr.Children.Add(r.Played);
        r.Sel = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(4), BorderBrush = Ui.Brush(Ui.Teal), Margin = new Thickness(-6), Opacity = 0, IsHitTestVisible = false };
        var host = new Grid();
        host.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Ui.Brush(Raised), Padding = new Thickness(8, 8, 8, 8), Child = gr,
            Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = Theme.Current.Light ? 0.12 : 0.3 } });
        host.Children.Add(r.Sel);
        r.Root = new Border { Child = host, Margin = new Thickness(0, 0, 0, 10), Cursor = Cursors.Hand, Background = Brushes.Transparent, Tag = g };
        r.Root.MouseLeftButtonUp += (s, e) => { padUsed = false; if (g == sel) PrimaryAction(g); else ShowGame(g); };
        r.Root.MouseRightButtonUp += (s, e) => { ShowGame(g); OpenGamePage(); e.Handled = true; };
        return r;
    }
    void UpdateHomeRow(Game g) {
        var r = ((GameUi)g.Ui).HomeRow; if (r == null) return;
        var st = StatusOf(g);
        r.Versions.Text = g.Inst == null ? (g.Latest != null ? "Latest " + g.Latest.Tag : "-") : g.Inst.Label + (g.State == State.Update && g.Latest != null ? "  →  " + g.Latest.Tag : "");
        r.Versions.Foreground = Ui.Brush(g.State == State.Update ? Ui.Gold : g.Inst == null ? Ui.Muted : Ui.Text);
        r.Status.Text = st.Text; r.Status.Foreground = Ui.Brush(g.Inst == null ? Ui.Muted : Ui.Text);
        r.Dot.Fill = Ui.Brush(st.Color);
        double total = Playtime.Total(g.Id); var last = Playtime.Last(g.Id);
        r.Played.Text = total > 30 ? Playtime.Format(total) + (last != DateTime.MinValue ? "\n" + Playtime.When(last) : "") : "";
        r.Root.Opacity = g.Inst == null ? 0.8 : 1;
    }
    void MarkHomeRow(Game o, bool on) {
        var r = ((GameUi)o.Ui).HomeRow; if (r == null) return;
        r.Name.Foreground = Ui.Brush(on ? Ui.Teal : Ui.Text);
        if (on) { var a = Calm(Forever(Anim(0.55, 1000, 1, EasingMode.EaseInOut))); a.AutoReverse = true; r.Sel.BeginAnimation(UIElement.OpacityProperty, homePage == "all" ? (AnimationTimeline)a : Anim(0, 100)); }
        else r.Sel.BeginAnimation(UIElement.OpacityProperty, Anim(0, 120));
    }

    // ---------- the game page ----------
    Grid gamePage; Border gpFace; Image gpIcon; TextBlock gpSource, gpName, gpInst, gpLatest, gpStatus, gpPlayed, gpNeeds, gpSteam, gpSize; Ellipse gpDot; Border gpPlayedChip, gpSteamChip, gpSizeChip; Image gpHero; Game gpHeroFor;
    readonly Dictionary<string, long> sizes = new Dictionary<string, long>(); readonly HashSet<string> sizing = new HashSet<string>();
    // how much space the game takes (the store's own count, else its folder added up once, in the background) and
    // what's free on its drive
    string SizeText(Game g) {
        if (g.Inst == null || string.IsNullOrEmpty(g.Dir) || !Directory.Exists(g.Dir)) return null;
        long size;
        if (!sizes.TryGetValue(g.Id, out size)) {
            long known = Stores.KnownSize(g);
            if (known > 0) sizes[g.Id] = size = known;
            else {
                if (sizing.Add(g.Id)) {
                    string dir = g.Dir;
                    System.Threading.ThreadPool.QueueUserWorkItem(_ => {
                        long n = 0;
                        try { foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories)) { try { n += f.Length; } catch { } } } catch { }
                        W.Dispatcher.BeginInvoke(new Action(() => { sizes[g.Id] = n; if (sel == g && homePage == "game") UpdateGamePage(); }));
                    });
                }
                size = -1;
            }
        }
        string free = "";
        try { var d = new DriveInfo(Path.GetPathRoot(g.Dir)); free = "  \u2022  " + Gb(d.AvailableFreeSpace) + " free on " + d.Name.TrimEnd('\\'); } catch { }
        return (size < 0 ? "adding up..." : Gb(size)) + free;
    }
    static string Gb(long b) { return b >= 1073741824L ? (b / 1073741824.0).ToString(b >= 107374182400L ? "0" : "0.0") + " GB" : Math.Max(1, b / 1048576) + " MB"; }
    readonly List<PageButton> gpButtons = new List<PageButton>(); int gpIdx; WrapPanel gpButtonRow;
    void BuildGamePage() {
        gamePage = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(60, 4, 60, 0) };
        Grid.SetRow(gamePage, 1); Grid.SetRowSpan(gamePage, 5);
        gamePage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        gamePage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        gamePage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gpIcon = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(gpIcon, BitmapScalingMode.HighQuality);
        gpFace = new Border { Width = 300, Height = 300, CornerRadius = new CornerRadius(3), ClipToBounds = true, Child = gpIcon, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 30, 0, 0),
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = Theme.Current.Light ? 0.22 : 0.45 } };
        // the game's wide picture behind its page, faded into the background (Steam, Epic and GOG games)
        gpHero = new Image { Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-60, -120, -60, 0), Opacity = 0, IsHitTestVisible = false,
            OpacityMask = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromArgb(150, 0, 0, 0), 0), new GradientStop(Color.FromArgb(70, 0, 0, 0), 0.55), new GradientStop(Colors.Transparent, 1) }, 90) };
        Grid.SetColumnSpan(gpHero, 3);
        gamePage.Children.Add(gpHero);
        gamePage.Children.Add(gpFace);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 26, 0, 0) };
        Grid.SetColumn(right, 2);
        gpSource = new TextBlock { FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal) };
        gpName = new TextBlock { FontSize = 36, FontFamily = Ui.Semi, Foreground = Ui.Brush(Ui.Text), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(-2, 4, 0, 0) };
        right.Children.Add(gpSource); right.Children.Add(gpName);
        var chips = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        Func<string, TextBlock, Border> chip = (label, value) => {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (label != null) sp.Children.Add(new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Muted), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            sp.Children.Add(value);
            return new Border { CornerRadius = new CornerRadius(15), Background = Ui.Brush(Raised), Padding = new Thickness(13, 6, 13, 6), Margin = new Thickness(0, 0, 8, 8), Child = sp,
                Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = Theme.Current.Light ? 0.12 : 0.25 } };
        };
        gpInst = new TextBlock { FontSize = 13.5, Foreground = Ui.Brush(Ui.Text) }; gpLatest = new TextBlock { FontSize = 13.5, Foreground = Ui.Brush(Ui.Text) };
        gpPlayed = new TextBlock { FontSize = 13.5, Foreground = Ui.Brush(Ui.Text) };
        gpDot = new Ellipse { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        gpStatus = new TextBlock { FontSize = 13.5, Foreground = Ui.Brush(Ui.Text) };
        chips.Children.Add(chip("INSTALLED", gpInst)); chips.Children.Add(chip("LATEST", gpLatest));
        var stChip = chip(null, gpStatus); ((StackPanel)stChip.Child).Children.Insert(0, gpDot); chips.Children.Add(stChip);
        gpPlayedChip = chip("PLAYED", gpPlayed); chips.Children.Add(gpPlayedChip);
        gpSteam = new TextBlock { FontSize = 13.5, Foreground = Ui.Brush(Ui.Text) };
        gpSteamChip = chip("ON STEAM", gpSteam); chips.Children.Add(gpSteamChip);
        gpSize = new TextBlock { FontSize = 13.5, Foreground = Ui.Brush(Ui.Text) };
        gpSizeChip = chip("SIZE", gpSize); chips.Children.Add(gpSizeChip);
        right.Children.Add(chips);
        gpNeeds = new TextBlock { FontSize = 13, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 6, 0, 0), LineHeight = 19 };
        right.Children.Add(gpNeeds);
        gpButtonRow = new WrapPanel { Margin = new Thickness(0, 24, 0, 0) };
        AddPageButton("", "Start", () => { if (sel != null) PrimaryAction(sel); });
        AddPageButton("", "Update", () => { if (sel != null && sel.State == State.Update && !running) TryAction("Updating " + sel.Name, () => StartQueue(new List<Game> { sel })); });
        AddPageButton("", "What's new", () => OpenNotes("notes"));
        AddPageButton("", "Game folder", () => OpenFolder(sel.Dir));
        AddPageButton("", "Save backups", () => OpenFolder(sel.Backups));
        AddPageButton("", "Uninstall", () => { if (sel != null && sel.Inst != null && !running) OpenUninstall(sel); });
        AddPageButton("", "Add to favourites", () => { if (sel != null) ToggleFavourite(sel); });
        AddPageButton("", "Collections", () => { if (sel != null) OpenCollections(sel); });
        AddPageButton("", "Language", () => { if (sel != null && sel.Steam != null) SteamLanguage(sel); });
        AddPageButton("\uED1A", "Hide", () => { if (sel != null) HideGame(sel); });
        AddPageButton("\uE9F9", "Achievements", () => { if (sel != null) OpenAchievements(sel); });
        AddPageButton("\uE7FC", "In-game menu", () => { if (sel != null) ToggleGuide(sel); });
        AddPageButton("\uE9D9", "Test versions", () => { if (sel != null) ToggleTestVersions(sel); });
        right.Children.Add(gpButtonRow);
        gamePage.Children.Add(right);
        homeLayer.Children.Add(gamePage);
    }
    void AddPageButton(string glyph, string text, Action action) {
        var pb = new PageButton { Do = action };
        pb.Glyph = new TextBlock { Text = glyph, FontFamily = Ui.Mdl, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 10, 0) };
        pb.Text = new TextBlock { Text = text, FontSize = 14.5, FontFamily = Ui.Semi, VerticalAlignment = VerticalAlignment.Center };
        var sp = new StackPanel { Orientation = Orientation.Horizontal }; sp.Children.Add(pb.Glyph); sp.Children.Add(pb.Text);
        pb.Root = new Border { Height = 50, CornerRadius = new CornerRadius(25), Padding = new Thickness(22, 0, 24, 0), Margin = new Thickness(0, 0, 12, 12), Child = sp, Cursor = Cursors.Hand,
            BorderThickness = new Thickness(3), BorderBrush = Brushes.Transparent, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1),
            Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = Theme.Current.Light ? 0.15 : 0.3 } };
        int idx = gpButtons.Count;
        pb.Root.MouseEnter += (s, e) => { if (pb.Enabled && !(padUsed && gpIdx == idx)) pb.Root.BorderBrush = Ui.Brush(Ui.Teal); };
        pb.Root.MouseLeave += (s, e) => { if (!(padUsed && gpIdx == idx)) pb.Root.BorderBrush = Brushes.Transparent; };
        pb.Root.MouseLeftButtonDown += (s, e) => { var st = (ScaleTransform)pb.Root.RenderTransform; st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(0.95, 80)); st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(0.95, 80)); };
        pb.Root.MouseLeftButtonUp += (s, e) => { var st = (ScaleTransform)pb.Root.RenderTransform; st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1, 140)); st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1, 140)); if (pb.Enabled) pb.Do(); };
        gpButtons.Add(pb);
        gpButtonRow.Children.Add(pb.Root);
    }
    void UpdateGamePage() {
        if (gamePage == null || sel == null) return;
        var g = sel; var st = StatusOf(g);
        gpIcon.Source = (ImageSource)g.Icon; gpIcon.Margin = new Thickness(g.IconIsArt ? 0 : 72);
        gpFace.Background = Ui.Gradient(g.Accent, g.Accent2);
        gpSource.Text = g.Source;
        gpName.Text = g.Name;
        gpInst.Text = g.Inst != null ? g.Inst.Label : "-";
        gpLatest.Text = g.Latest != null ? g.Latest.Tag : g.State == State.Checking ? "..." : g.Inst == null ? "looked up when you install" : "-";
        gpLatest.Foreground = Ui.Brush(g.State == State.Update ? Ui.Gold : Ui.Text);
        gpStatus.Text = st.Text; gpDot.Fill = Ui.Brush(st.Color);
        double total = Playtime.Total(g.Id); var last = Playtime.Last(g.Id);
        gpPlayedChip.Visibility = total > 30 || last != DateTime.MinValue ? Visibility.Visible : Visibility.Collapsed;
        gpPlayed.Text = Playtime.Format(total) + (last != DateTime.MinValue ? "  •  " + Playtime.When(last) : "");
        // what Steam counted (all the play, also outside the launcher), when you last played, and achievements
        var ss = SteamGames.StatsOf(g);
        var bits = new List<string>();
        if (ss != null) {
            if (ss.Minutes > 0) bits.Add(Playtime.Format(ss.Minutes * 60.0));
            if (ss.Last != DateTime.MinValue) bits.Add("last " + Playtime.When(ss.Last));
            if (ss.AchTotal > 0) bits.Add(ss.Achieved + " / " + ss.AchTotal + " achievements");
        }
        gpSteam.Text = string.Join("  •  ", bits);
        gpSteamChip.Visibility = bits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        string sz = SizeText(g);
        gpSize.Text = sz ?? ""; gpSizeChip.Visibility = sz != null ? Visibility.Visible : Visibility.Collapsed;
        if (gpHeroFor != g) {
            gpHeroFor = g;
            var hero = Stores.Hero(g);
            gpHero.Source = hero;
            gpHero.BeginAnimation(UIElement.OpacityProperty, hero != null ? Anim(Theme.Current.Light ? 0.35 : 0.5, 400, 0) : null);
            if (hero == null) gpHero.Opacity = 0;
        }
        bool missing = g.State == State.Missing || g.State == State.Installing || g.State == State.Converting;
        gpNeeds.Text = missing ? "You need: " + g.Needs : "";
        gpNeeds.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
        // Start / Install, Update, What's new, folder, backups, Uninstall
        string dummy;
        bool playOk = missing ? g.State == State.Missing && !running : g.ByStore ? g.Inst != null : File.Exists(Engine.PlayFile(g, out dummy, false)) && !g.IsBusy;
        SetPageButton(0, missing ? "" : "", g.State == State.Installing ? "Installing..." : missing ? "Install" : "Start", playOk, true);
        SetPageButton(1, "", g.State == State.Update ? (g.Latest.Size >= 1048576 ? string.Format("Update  ({0:N0} MB)", g.Latest.Size / 1048576) : string.Format("Update  ({0:N0} KB)", g.Latest.Size / 1024)) : g.State == State.Updating ? "Updating..." : "Up to date",
            g.State == State.Update && !running, !missing);
        SetPageButton(2, "", "What's new", true, true);
        SetPageButton(3, "", "Game folder", Directory.Exists(g.Dir), true);
        SetPageButton(4, "", "Save backups", true, true);
        SetPageButton(5, "", "Uninstall", g.Inst != null && !running && !missing, g.Inst != null && !missing);
        bool fav = Profiles.IsFav(g.Id);
        SetPageButton(6, fav ? "\uE735" : "\uE734", fav ? "Favourite" : "Add to favourites", true, true);
        if (fav) gpButtons[6].Glyph.Foreground = Ui.Brush(Ui.Gold);
        int inCols = Profiles.CollectionNames().Count(c => Profiles.InCollection(c).Contains(g.Id));
        SetPageButton(7, "\uE8F1", inCols > 0 ? "Collections  (" + inCols + ")" : "Add to a collection", true, true);
        // Steam games: Steam installs, updates and uninstalls them, and their language is picked in Steam
        string lang = g.Steam != null && g.Inst != null && g.Languages.Length > 0 ? SteamGames.Language(g) : null;
        SetPageButton(8, "\uE774", "Language: " + (lang ?? "?"), lang != null, lang != null);
        SetPageButton(9, "\uED1A", "Hide", true, true);
        bool hasAch = ss != null && ss.AchTotal > 0;
        SetPageButton(10, "\uE9F9", hasAch ? "Achievements  (" + ss.Achieved + " / " + ss.AchTotal + ")" : "Achievements", hasAch, hasAch);
        // which button opens the launcher's menu in this game (some games use the Xbox button themselves)
        bool menuOn = g.Inst != null && Settings.Get("PadMenu") != "off";
        SetPageButton(11, "\uE7FC", "In-game menu: " + (GuideLeftTo(g) ? "View + Menu" : "Xbox button"), menuOn, menuOn);
        // test versions (pre-releases): for a game from GitHub or its own site, and not one that only has those
        bool canTest = g.Repo != null && !g.Pre && !g.ByStore && g.App == null;
        SetPageButton(12, "\uE9D9", "Test versions: " + (Engine.TestVersions(g) ? "On" : "Off"), canTest && !running, canTest);
        if (g.ByStore) {
            SetPageButton(0, missing ? "\uE896" : "\uE768", missing ? "Install with " + (g.Store == "epic" ? "Epic" : g.StoreName) : "Start", playOk, true);
            SetPageButton(1, "", "", false, false);
            SetPageButton(4, "", "", false, false);
            SetPageButton(5, "\uE74D", "Uninstall", g.Inst != null, g.Inst != null);
        }
        if (gpIdx >= gpButtons.Count || !gpButtons[gpIdx].Enabled) gpIdx = Math.Max(0, gpButtons.FindIndex(b => b.Enabled && b.Root.Visibility == Visibility.Visible));
        MarkPageButtons();
    }
    void SetPageButton(int i, string glyph, string text, bool enabled, bool shown) {
        var b = gpButtons[i];
        b.Glyph.Text = glyph; b.Text.Text = text; b.Enabled = enabled;
        b.Root.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        bool primary = i == 0, gold = i == 1 && enabled;
        b.Root.Background = Ui.Brush(primary && enabled ? Ui.Teal : gold ? Ui.Gold : Raised);
        string ink = Theme.Current.Light ? "#ffffff" : "#0A0E11";
        b.Text.Foreground = Ui.Brush(primary && enabled || gold ? ink : Ui.Text);
        b.Glyph.Foreground = Ui.Brush(primary && enabled || gold ? ink : i == 5 ? "#e8453c" : i == 2 ? "#e8453c" : i == 3 ? "#2f8fe0" : i == 4 ? "#8a57d6" : Ui.Muted);
        b.Root.Opacity = enabled ? 1 : 0.45;
        b.Root.Cursor = enabled ? Cursors.Hand : Cursors.Arrow;
    }
    void MarkPageButtons() {
        for (int i = 0; i < gpButtons.Count; i++) gpButtons[i].Root.BorderBrush = padUsed && i == gpIdx ? Ui.Brush(Ui.Text) : Brushes.Transparent;
    }
    void OpenGamePage() {
        if (sel == null || homeLayer == null) return;
        if (sel.App != null) { PrimaryAction(sel); return; }   // an app has no game page: it opens
        if (homePage != "game") Sound.Play(Sfx.Open);
        gameBack = homePage == "game" ? gameBack : homePage;
        homePage = "game";
        gpIdx = 0;
        ShowHomePage();
    }
    void CloseGamePage() { Sound.Play(Sfx.Back); homePage = gameBack; ShowHomePage(); }
    bool GamePageMove(string dir) {
        var vis = Enumerable.Range(0, gpButtons.Count).Where(i => gpButtons[i].Root.Visibility == Visibility.Visible && gpButtons[i].Enabled).ToList();
        int at = vis.IndexOf(gpIdx); if (at < 0) at = 0;
        int was = gpIdx;
        if (dir == "left" || dir == "up") { if (vis.Count > 0) gpIdx = vis[Math.Max(0, at - 1)]; }
        else if (dir == "right" || dir == "down") { if (vis.Count > 0) gpIdx = vis[Math.Min(vis.Count - 1, at + 1)]; }
        else if (dir == "ok") { if (gpIdx < gpButtons.Count && gpButtons[gpIdx].Enabled) { Sound.Play(Sfx.Select); gpButtons[gpIdx].Do(); } else Sound.Play(Sfx.Bump); return true; }
        else if (dir == "back") { CloseGamePage(); return true; }
        else return false;
        Sound.Play(gpIdx != was ? Sfx.Move : Sfx.Bump);
        MarkPageButtons();
        return true;
    }
}
}
