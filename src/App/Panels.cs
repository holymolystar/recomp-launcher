// Overlays: What's new / Activity, Settings, Choose your game (with the NSP converter), Uninstall, and the file
// browser a controller uses instead of Windows' dialogs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

using Path = System.IO.Path;

namespace RecompLauncher {

class Toggle { public Grid Root; public Border Track; public Ellipse Knob; public bool On; public Action Changed; }
class FolderRow { public Game G; public string Key, Label, File; public bool New; public TextBox Box; public TextBlock Check; public Button Btn; }
class FbItem { public string Kind, Path, Name, Note; }

partial class Shell {
    FlowDocument notes, activity;
    Game notesFor;
    string tab = "notes";

    void BuildPanels() {
        notes = NewDoc(); activity = NewDoc();
        N<Button>("BtnNotesClose").Click += (s, e) => CloseNotes();
        N<Rectangle>("NotesDim").MouseLeftButtonDown += (s, e) => CloseNotes();
        N<Grid>("TabNotes").MouseLeftButtonUp += (s, e) => { if (tab != "notes") SetTab("notes"); };
        N<Grid>("TabActivity").MouseLeftButtonUp += (s, e) => { if (tab != "activity") SetTab("activity"); };
        BuildSettings();
        BuildUninstall();
        BuildRomPicker();
        BuildFileBrowser();
    }

    // ---------- release notes and activity ----------
    static FlowDocument NewDoc() {
        return new FlowDocument { FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), FontSize = 13, Foreground = Ui.Brush("#C9CCC8"),
            PagePadding = new Thickness(12, 0, 12, 6), TextAlignment = TextAlignment.Left };
    }
    static void AddInlines(Paragraph p, string line, string color = null) {
        line = Regex.Replace(line, @"\[([^\]]+)\]\([^)]+\)", "$1").Replace("`", "");
        var parts = Regex.Split(line, @"\*\*");
        for (int i = 0; i < parts.Length; i++) {
            if (parts[i].Length == 0) continue;
            var r = new Run(parts[i]);
            if (i % 2 == 1) { r.FontWeight = FontWeights.SemiBold; r.Foreground = Ui.Brush(Ui.Text); }
            if (color != null) r.Foreground = Ui.Brush(color);
            p.Inlines.Add(r);
        }
    }
    void FillNotes(Game g) {
        notesFor = g;
        notes.Blocks.Clear();
        var date = N<TextBlock>("NotesDate"); date.Text = "";
        if (g.Latest == null) {
            var p = new Paragraph();
            AddInlines(p, g.State == State.Checking || lookingUp == g ? "Loading the release notes..." : "Couldn't load the release notes.", Ui.Muted);
            notes.Blocks.Add(p); return;
        }
        DateTime d;
        date.Text = DateTime.TryParse(g.Latest.Date, out d) ? g.Latest.Tag + "  \u2022  " + d.ToLocalTime().ToString("MMM d, yyyy") : g.Latest.Tag;
        foreach (var raw in (g.Latest.Notes ?? "").Replace("\r", "").Split('\n')) {
            // GitHub's notes can hold HTML (<details>, <b>) and code fences: shown as plain text
            string line = Regex.Replace(raw, @"<[^>]+>", "").Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").TrimEnd();
            if (line.Trim().Length == 0 || Regex.IsMatch(line, @"^>\s*\[!") || Regex.IsMatch(line, @"^\s*```")) continue;
            var p = new Paragraph { Margin = new Thickness(0, 0, 0, 6) };
            Match m;
            if ((m = Regex.Match(line, @"^#{1,6}\s+(.*)")).Success) { p.Margin = new Thickness(0, 10, 0, 6); p.FontSize = 14.5; p.FontWeight = FontWeights.SemiBold; AddInlines(p, m.Groups[1].Value, g.Accent); }
            else if ((m = Regex.Match(line, @"^\s*[-*]\s+(.*)")).Success) {
                p.Margin = new Thickness(16, 0, 0, 6); p.TextIndent = -14;
                p.Inlines.Add(new Run("\u2022   ") { Foreground = Ui.Brush(g.Accent) });
                AddInlines(p, m.Groups[1].Value);
            } else if ((m = Regex.Match(line, @"^>\s?(.*)")).Success) AddInlines(p, m.Groups[1].Value, Ui.Muted);
            else AddInlines(p, line);
            notes.Blocks.Add(p);
        }
    }
    void Log(string text, string color = null) {
        var p = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
        p.Inlines.Add(new Run(DateTime.Now.ToString("HH:mm:ss") + "    ") { Foreground = Ui.Brush(Ui.Dim), FontSize = 11.5 });
        var r = new Run(text ?? ""); if (color != null) r.Foreground = Ui.Brush(color);
        p.Inlines.Add(r);
        activity.Blocks.Add(p);
        if (tab == "activity") p.BringIntoView();
    }
    void SetTab(string which) {
        tab = which; bool on = which == "notes";
        N<TextBlock>("TabNotesText").Foreground = Ui.Brush(on ? Ui.Text : Ui.Dim);
        N<TextBlock>("TabActivityText").Foreground = Ui.Brush(on ? Ui.Dim : Ui.Text);
        N<Border>("TabNotesLine").BeginAnimation(UIElement.OpacityProperty, Anim(on ? 1 : 0, 200));
        N<Border>("TabActivityLine").BeginAnimation(UIElement.OpacityProperty, Anim(on ? 0 : 1, 200));
        N<TextBlock>("NotesDate").Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        var v = N<FlowDocumentScrollViewer>("Viewer");
        v.Document = on ? notes : activity;
        v.BeginAnimation(UIElement.OpacityProperty, Anim(1, 250, 0));
        if (!on && activity.Blocks.LastBlock != null) activity.Blocks.LastBlock.BringIntoView();
    }
    Game lookingUp;
    void OpenNotes(string which) {
        var g = sel;
        // a game that isn't installed isn't checked at start: its notes are looked up now
        if (g.Latest == null && g.State == State.Missing && lookingUp != g) {
            lookingUp = g;
            Lookup(g, (r, err) => {
                lookingUp = null;
                if (r != null && r.Tag != null) { g.Latest = r; UpdateViews(); }
                if (notesFor == g) FillNotes(g);
            });
        }
        if (notesFor != g) FillNotes(g);
        if (which != null) SetTab(which);
        var layer = N<Grid>("NotesLayer");
        if (layer.Visibility == Visibility.Visible) return;
        layer.Visibility = Visibility.Visible;
        layer.BeginAnimation(UIElement.OpacityProperty, Anim(1, 200, 0));
        var panel = N<Border>("NotesPanel");
        ((TranslateTransform)panel.RenderTransform).BeginAnimation(TranslateTransform.XProperty, Anim(0, 380, panel.Width));
        HideRing(); SetHints();
    }
    void CloseNotes() {
        var layer = N<Grid>("NotesLayer");
        if (layer.Visibility != Visibility.Visible) return;
        var a = Anim(0, 200, null, EasingMode.EaseIn);
        a.Completed += (s, e) => { layer.Visibility = Visibility.Collapsed; SetHints(); QueueRing(); };
        layer.BeginAnimation(UIElement.OpacityProperty, a);
        var panel = N<Border>("NotesPanel");
        ((TranslateTransform)panel.RenderTransform).BeginAnimation(TranslateTransform.XProperty, Anim(panel.Width, 260, null, EasingMode.EaseIn));
    }

    // ---------- overlay cards ----------
    void ShowCard(string layer, string card) {
        var l = N<Grid>(layer);
        l.Visibility = Visibility.Visible;
        l.BeginAnimation(UIElement.OpacityProperty, Anim(1, 220, 0));
        var tg = (TransformGroup)N<Border>(card).RenderTransform;
        var sc = (ScaleTransform)tg.Children[0];
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1, 380, 0.94)); sc.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1, 380, 0.94));
        ((TranslateTransform)tg.Children[1]).BeginAnimation(TranslateTransform.YProperty, Anim(0, 380, 18));
        HideRing(); SetHints();
    }
    void HideCard(string layer, string card) {
        var l = N<Grid>(layer);
        var a = Anim(0, 180, null, EasingMode.EaseIn);
        a.Completed += (s, e) => { l.Visibility = Visibility.Collapsed; SetHints(); QueueRing(); };
        l.BeginAnimation(UIElement.OpacityProperty, a);
        ((TranslateTransform)((TransformGroup)N<Border>(card).RenderTransform).Children[1]).BeginAnimation(TranslateTransform.YProperty, Anim(12, 180, null, EasingMode.EaseIn));
    }
    Button MakeButton(string text, double h = 38) {
        return new Button { Style = (Style)W.FindResource("Btn"), Content = text, Height = h, FontSize = 13, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(8, 0, 0, 0) };
    }

    // ---------- switches ----------
    Toggle NewToggle(string label, string desc) {
        var row = new Grid { Margin = new Thickness(0, 6, 0, 6), Background = Ui.Brush("#00000000"), Cursor = Cursors.Hand };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(new TextBlock { Text = label, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = label, FontFamily = Ui.Semi });
        if (!string.IsNullOrEmpty(desc)) text.Children.Add(new TextBlock { Text = desc, FontSize = 11.5, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap });
        var knob = new Ellipse { Width = 18, Height = 18, Fill = Ui.Brush("#F2EFE6"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 0, 0), RenderTransform = new TranslateTransform() };
        var track = new Border { Width = 44, Height = 24, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Center, Child = knob };
        Grid.SetColumn(track, 1);
        row.Children.Add(text); row.Children.Add(track);
        var t = new Toggle { Root = row, Track = track, Knob = knob };
        row.Tag = t;
        row.MouseLeftButtonUp += (s, e) => SetToggle(t, !t.On, false);
        SetToggle(t, false, true);
        return t;
    }
    void SetToggle(Toggle t, bool on, bool now) {
        t.On = on;
        t.Track.Background = Ui.Brush(on ? Ui.Teal : "#2A3640");
        ((TranslateTransform)t.Knob.RenderTransform).BeginAnimation(TranslateTransform.XProperty, Anim(on ? 20 : 0, now ? 0 : 180));
        if (t.Changed != null && !now) t.Changed();
    }

    // ---------- settings ----------
    Toggle optClose, optFull, optStartup, optAuto;
    PasswordBox tokenBox; TextBlock tokenCheck;
    readonly Dictionary<string, Toggle> showToggles = new Dictionary<string, Toggle>();
    readonly List<FolderRow> folderRows = new List<FolderRow>();
    StackPanel showButtons; UniformGrid showGrid;
    static string StartupLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Recomp Launcher.lnk"); } }
    TextBlock Heading(string text, bool first) {
        return new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal), Margin = new Thickness(0, first ? 4 : 18, 0, 4) };
    }
    void BuildSettings() {
        var rows = N<StackPanel>("SetRows");
        rows.Children.Add(Heading("THEME", true));
        rows.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = Ui.Brush(Ui.Muted), Margin = new Thickness(0, 0, 0, 8),
            Text = "Pick a look. The launcher reopens with it when you save." });
        themeGrid = new UniformGrid { Columns = 3 };
        foreach (var th in Theme.All) themeGrid.Children.Add(ThemeSwatch(th));
        rows.Children.Add(themeGrid);
        rows.Children.Add(Heading("OPTIONS", false));
        optClose = NewToggle("Close the launcher when a game starts", "Play time is still counted while you play.");
        optFull = NewToggle("Open in full screen", "F11, the full screen button or View on a controller switch it any time.");
        optStartup = NewToggle("Open when Windows starts", "Adds Recomp Launcher to your Startup folder.");
        optAuto = NewToggle("Check for updates every 3 hours", "While the launcher is open. Only installed games are checked.");
        foreach (var t in new[] { optClose, optFull, optStartup, optAuto }) rows.Children.Add(t.Root);

        rows.Children.Add(Heading("GITHUB TOKEN (OPTIONAL)", false));
        rows.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = Ui.Brush(Ui.Muted), Margin = new Thickness(0, 0, 0, 8),
            Text = "Without one, GitHub allows 60 update checks an hour, and some games then show \"Couldn't check\". With a token it's 5,000. Make one at github.com: your picture > Settings > Developer settings > Personal access tokens > Fine-grained tokens > Generate new token. Give it any name, Repository access: Public repositories, no permissions. Generate it, copy it, paste it here. It can't change anything on your account." });
        tokenBox = new PasswordBox { Height = 38, FontSize = 13, Padding = new Thickness(10, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center,
            Background = Ui.Brush("#0A0E11"), Foreground = Ui.Brush(Ui.Text), BorderBrush = Ui.Brush("#26333C"), CaretBrush = Ui.Brush(Ui.Text) };
        tokenBox.LostKeyboardFocus += (s, e) => TestToken();
        var site = MakeButton("Open GitHub");
        site.Click += (s, e) => { try { System.Diagnostics.Process.Start("https://github.com/settings/personal-access-tokens/new"); } catch { } };
        var tl = new Grid();
        tl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); tl.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(site, 1); tl.Children.Add(tokenBox); tl.Children.Add(site);
        rows.Children.Add(tl);
        tokenCheck = new TextBlock { FontSize = 11.5, Margin = new Thickness(2, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        rows.Children.Add(tokenCheck);

        rows.Children.Add(Heading("GAMES IN THE LIBRARY", false));
        rows.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = Ui.Brush(Ui.Muted), Margin = new Thickness(0, 0, 0, 6),
            Text = "Turn a game off to hide it from the library. Nothing is uninstalled: turn it back on any time." });
        showButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        foreach (var sb in new[] { new[] { "Show all", "all" }, new[] { "Only installed", "installed" } }) {
            var b = MakeButton(sb[0], 32); b.FontSize = 12.5; b.Margin = new Thickness(0, 0, 8, 0); string mode = sb[1];
            b.Click += (s, e) => { foreach (var g in Games) SetToggle(showToggles[g.Id], mode == "all" || g.Inst != null, false); };
            showButtons.Children.Add(b);
        }
        rows.Children.Add(showButtons);
        showGrid = new UniformGrid { Columns = 2 };
        foreach (var g in Games.OrderBy(Catalog.SeriesOf).ThenBy(x => x.Rank)) {
            var t = NewToggle(g.Name, null); t.Root.Margin = new Thickness(0, 4, 18, 4);
            showToggles[g.Id] = t; showGrid.Children.Add(t.Root);
        }
        rows.Children.Add(showGrid);

        rows.Children.Add(Heading("GAME FOLDERS", false));
        var defs = new List<FolderRow>();
        foreach (var g in Games.OrderBy(Catalog.SeriesOf).ThenBy(x => x.Rank)) {
            defs.Add(new FolderRow { G = g, Key = g.SettingsKey, Label = g.SettingsLabel, File = g.CheckFile, New = true });
            if (g.Id == "totk") defs.Add(new FolderRow { Key = "TotkUpdaterDir", Label = "TotK Updater (installs TotK updates)", File = "update-worker.ps1", New = false });
        }
        foreach (var r in defs) {
            var row = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var head = new Grid();
            head.Children.Add(new TextBlock { Text = r.Label, FontSize = 13, FontFamily = Ui.Semi });
            r.Check = new TextBlock { FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
            head.Children.Add(r.Check);
            var line = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            r.Box = new TextBox { Style = (Style)W.FindResource("PathBox"), Height = 38 };
            r.Btn = MakeButton("Browse"); Grid.SetColumn(r.Btn, 1);
            var rr = r;
            r.Btn.Click += (s, e) => { if (padUsed) OpenFbFolder(rr); else BrowseInto(rr.Box, rr.Label + " folder (the one with " + rr.File + ")"); };
            r.Box.TextChanged += (s, e) => CheckPathLabel(rr);
            line.Children.Add(r.Box); line.Children.Add(r.Btn);
            row.Children.Add(head); row.Children.Add(line);
            rows.Children.Add(row);
            folderRows.Add(r);
        }
        N<Button>("BtnSetClose").Click += (s, e) => CloseSettings();
        N<Button>("BtnSetCancel").Click += (s, e) => CloseSettings();
        N<Rectangle>("SettingsDim").MouseLeftButtonDown += (s, e) => CloseSettings();
        N<Button>("BtnSetSave").Click += (s, e) => SaveSettingsPanel();
        N<Button>("BtnSetDefaults").Click += (s, e) => { foreach (var r in folderRows) r.Box.Text = Settings.Default(r.Key); };
    }
    // ---------- theme picker: a small preview of each theme in its own colours ----------
    UniformGrid themeGrid; string pickedTheme;
    Button ThemeSwatch(ThemeDef th) {
        Func<string, SolidColorBrush> raw = h => { var b = new SolidColorBrush(Theme.Parse(h.TrimStart('#'))); b.Freeze(); return b; };
        Color bg = Theme.Parse(th.Bg.TrimStart('#')), ink = Theme.Parse(th.Ink.TrimStart('#'));
        var face = new Grid { Height = 74, ClipToBounds = true };
        face.Children.Add(new Border { CornerRadius = new CornerRadius(10),
            Background = new LinearGradientBrush(Theme.Mix(bg, Theme.Parse(th.Accent.TrimStart('#')), 0.28), bg, 35) });
        // a mini card and two buttons in the theme's accents
        var mini = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 12), VerticalAlignment = VerticalAlignment.Bottom };
        mini.Children.Add(new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(7), Background = new LinearGradientBrush(Theme.Parse(th.Accent.TrimStart('#')), Theme.Parse(th.Accent2.TrimStart('#')), 45) });
        mini.Children.Add(new Border { Width = 40, Height = 12, CornerRadius = new CornerRadius(6), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Background = raw(th.Ink) });
        mini.Children.Add(new Border { Width = 22, Height = 12, CornerRadius = new CornerRadius(6), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Background = raw(th.Accent2) });
        face.Children.Add(mini);
        face.Children.Add(new TextBlock { Text = th.Name, FontSize = 13, FontFamily = Ui.Semi, Foreground = new SolidColorBrush(ink), Margin = new Thickness(12, 9, 0, 0) });
        var card = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), Padding = new Thickness(2), Child = face, BorderBrush = Brushes.Transparent };
        var b2 = new Button { Content = card, Margin = new Thickness(0, 0, 10, 10), Cursor = Cursors.Hand, ToolTip = th.Desc, Tag = th.Id, Focusable = false };
        b2.Template = (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"Button\"><ContentPresenter/></ControlTemplate>");
        b2.Click += (s, e) => PickTheme(th.Id);
        return b2;
    }
    void PickTheme(string id) {
        pickedTheme = id;
        foreach (Button b in themeGrid.Children) {
            bool on = (string)b.Tag == id;
            ((Border)b.Content).BorderBrush = on ? Ui.Brush(Ui.Text) : Brushes.Transparent;
        }
    }
    void CheckPathLabel(FolderRow r) {
        string dir = Paths.Expand(r.Box.Text.Trim());
        var l = r.Check;
        if (dir.Length == 0) { l.Text = "Enter a folder"; l.Foreground = Ui.Brush(Ui.Red); return; }
        bool found = r.File.IndexOfAny(new[] { '*', '?' }) >= 0 ? Directory.Exists(dir) && Directory.GetFiles(dir, r.File).Length > 0 : File.Exists(Path.Combine(dir, r.File));
        if (found) { l.Text = "\u2713  " + r.File + " found"; l.Foreground = Ui.Brush(Ui.Teal); }
        else if (r.New) { l.Text = "Not installed here yet: Install puts it here"; l.Foreground = Ui.Brush(Ui.Muted); }
        else if (Directory.Exists(dir)) { l.Text = r.File + " isn't in this folder"; l.Foreground = Ui.Brush(Ui.Gold); }
        else { l.Text = "This folder doesn't exist"; l.Foreground = Ui.Brush(Ui.Red); }
    }
    void OpenSettings() {
        foreach (var r in folderRows) { r.Box.Text = Settings.Get(r.Key); CheckPathLabel(r); }
        SetToggle(optClose, Settings.On("CloseOnPlay"), true);
        SetToggle(optFull, Settings.Get("Fullscreen") == "True", true);
        SetToggle(optStartup, File.Exists(StartupLink), true);
        SetToggle(optAuto, Settings.On("AutoCheck"), true);
        tokenBox.Password = Settings.Get("GitHubToken");
        tokenCheck.Text = tokenBox.Password.Length > 0 ? "A token is saved." : ""; tokenCheck.Foreground = Ui.Brush(Ui.Muted);
        foreach (var g in Games) SetToggle(showToggles[g.Id], !g.Hidden, true);
        PickTheme(Theme.Current.Id);
        fb.Visibility = Visibility.Collapsed;
        setStops = SetStops(); MarkSetStop(-1);
        if (padUsed) SelectSetStop(0);
        ShowCard("SettingsLayer", "SettingsCard");
        ((RotateTransform)N<TextBlock>("GearIcon").RenderTransform).BeginAnimation(RotateTransform.AngleProperty, Anim(90, 500, 0));
    }
    void CloseSettings() {
        HideCard("SettingsLayer", "SettingsCard");
        ((RotateTransform)N<TextBlock>("GearIcon").RenderTransform).BeginAnimation(RotateTransform.AngleProperty, Anim(0, 500));
    }
    void BrowseInto(TextBox box, string title) {
        using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = title }) {
            if (Directory.Exists(box.Text)) d.SelectedPath = box.Text;
            if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) box.Text = d.SelectedPath;
        }
    }
    void SaveSettingsPanel() {
        foreach (var r in folderRows) Settings.Set(r.Key, r.Box.Text.Trim().TrimEnd('\\'));
        Settings.Set("CloseOnPlay", optClose.On ? "on" : "off");
        Settings.Set("AutoCheck", optAuto.On ? "on" : "off");
        Settings.Set("Fullscreen", optFull.On ? "True" : "");
        if (optFull.On != isFull) SetFullscreen(optFull.On);
        Settings.Set("GitHubToken", tokenBox.Password.Trim());
        try {
            if (optStartup.On && !File.Exists(StartupLink)) MakeShortcut(StartupLink);
            else if (!optStartup.On && File.Exists(StartupLink)) File.Delete(StartupLink);
        } catch (Exception e) { Log("Couldn't change the Startup folder: " + e.Message, Ui.Red); }
        foreach (var g in Games) g.Hidden = !showToggles[g.Id].On;
        Settings.Set("Hidden", string.Join(",", Games.Where(g => g.Hidden).Select(g => g.Id)));
        bool newTheme = pickedTheme != null && pickedTheme != Theme.Current.Id;
        if (newTheme) Settings.Set("Theme", pickedTheme);
        Settings.Save();
        // a new theme: the launcher opens again in it (the window is built in its colours)
        if (newTheme) {
            Settings.Set("LastGame", sel.Id); Settings.Save();
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location, "-Select " + sel.Id) { WorkingDirectory = Paths.Root }); } catch { }
            W.Close();
            return;
        }
        SetAutoCheck();
        ApplySettings();
        foreach (var g in Games) { Icons.Load(g); ApplyIcon(g); }
        CloseSettings();
        ShowGame(sel);
        CheckAll(false);
    }
    static void MakeShortcut(string link) {
        string exe = Path.Combine(Paths.Root, "Recomp Launcher.exe");
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        dynamic sc = shell.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { link });
        sc.TargetPath = exe; sc.WorkingDirectory = Paths.Root; sc.IconLocation = exe + ",0"; sc.Description = "Recomp Launcher";
        sc.Save();
    }
    void TestToken() {
        string tok = tokenBox.Password.Trim();
        if (tok.Length == 0) { tokenCheck.Text = ""; return; }
        tokenCheck.Text = "Checking the token..."; tokenCheck.Foreground = Ui.Brush(Ui.Muted);
        System.Threading.Tasks.Task.Factory.StartNew(() => { try { return Engine.TokenLimit(tok); } catch { return null; } })
            .ContinueWith(t => W.Dispatcher.BeginInvoke(new Action(() => {
                var parts = t.Result != null ? t.Result.Split('|') : null;
                if (parts != null && double.Parse(parts[0]) > 60) {
                    tokenCheck.Text = string.Format("\u2713  The token works: {0:N0} checks an hour ({1:N0} left).", double.Parse(parts[0]), double.Parse(parts[1]));
                    tokenCheck.Foreground = Ui.Brush(Ui.Teal);
                } else { tokenCheck.Text = "GitHub didn't accept this token: check that it was copied whole, or make a new one."; tokenCheck.Foreground = Ui.Brush(Ui.Red); }
            })));
    }
    // controller in Settings: a list of stops (switches, buttons, the folders' Browse buttons)
    List<FrameworkElement> setStops = new List<FrameworkElement>();
    int setIdx = -1;
    List<FrameworkElement> SetStops() {
        var s = new List<FrameworkElement>();
        foreach (FrameworkElement b in themeGrid.Children) s.Add(b);
        foreach (var t in new[] { optClose, optFull, optStartup, optAuto }) s.Add(t.Root);
        foreach (FrameworkElement b in showButtons.Children) s.Add(b);
        foreach (FrameworkElement c in showGrid.Children) s.Add(c);
        foreach (var r in folderRows) s.Add(r.Btn);
        s.Add(N<Button>("BtnSetDefaults")); s.Add(N<Button>("BtnSetCancel")); s.Add(N<Button>("BtnSetSave"));
        return s;
    }
    static readonly DropShadowEffect SetGlow = new DropShadowEffect { Color = Ui.Col("#3FB8A0"), BlurRadius = 18, ShadowDepth = 0, Opacity = 1 };
    void MarkSetStop(int i) {
        for (int k = 0; k < setStops.Count; k++) {
            var el = setStops[k]; bool on = k == i;
            if (el.Tag is Toggle) ((Grid)el).Background = Ui.Brush(on ? "#203039" : "#00000000");
            else el.Effect = on ? SetGlow : null;
        }
    }
    void SelectSetStop(int i) {
        if (setStops.Count == 0) return;
        setIdx = Math.Max(0, Math.Min(setStops.Count - 1, i));
        MarkSetStop(setIdx);
        setStops[setIdx].BringIntoView();
    }

    // ---------- uninstall ----------
    Toggle keepSaves; Game uninstGame;
    void BuildUninstall() {
        keepSaves = NewToggle("Keep a backup of my saves and settings", "");
        N<StackPanel>("UninstToggleHost").Children.Add(keepSaves.Root);
        N<Button>("BtnUninstCancel").Click += (s, e) => CloseUninstall();
        N<Rectangle>("UninstDim").MouseLeftButtonDown += (s, e) => CloseUninstall();
        N<Button>("BtnUninstGo").Click += (s, e) => TryAction("Uninstalling " + uninstGame.Name, ConfirmUninstall);
    }
    void OpenUninstall(Game g) {
        uninstGame = g;
        N<Image>("UninstIcon").Source = (ImageSource)g.Icon;
        N<TextBlock>("UninstTitle").Text = "Uninstall " + g.Name + "?";
        N<TextBlock>("UninstPath").Text = g.Dir;
        var size = N<TextBlock>("UninstSize"); size.Text = "Working out the size...";
        bool saves = !string.IsNullOrEmpty(g.Saves);
        keepSaves.Root.Visibility = saves ? Visibility.Visible : Visibility.Collapsed;
        SetToggle(keepSaves, saves, true);
        N<TextBlock>("UninstNote").Text = saves
            ? "Kept: " + string.Join(", ", g.Saves.Split(';').Select(p => Path.GetFileName(p.TrimEnd('\\')))) + ", copied to " + g.Backups + " (the Backups button opens it). Play time is kept too."
            : g.Id == "mkwii" ? "This removes Wheel Wizard. Its data and the Mario Kart Wii it built are kept elsewhere (Wheel Wizard manages them): uninstall those in Wheel Wizard first if you want them gone."
            : "Play time is kept.";
        string warn = g.Dir.IndexOf(@"\Quiver\", StringComparison.OrdinalIgnoreCase) >= 0 ? "This folder belongs to Quiver: Quiver will see the game as missing."
                    : g.Id == "totk" ? "The TotK Updater and its backups stay; only the game folder goes."
                    : g.Id == "hookshot" ? "Hookshot's built game in " + Paths.Expand(@"%USERPROFILE%\Games\Hookshot") + " stays: delete it yourself if you want the space back." : "";
        var w = N<TextBlock>("UninstWarn"); w.Text = warn; w.Visibility = warn.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        N<Button>("BtnUninstGo").IsEnabled = true;
        string dir = g.Dir;
        System.Threading.Tasks.Task.Factory.StartNew(() => {
            long bytes = 0; int files = 0;
            try { foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories)) { bytes += f.Length; files++; } } catch { }
            return Tuple.Create(bytes, files);
        }).ContinueWith(t => W.Dispatcher.BeginInvoke(new Action(() => {
            if (uninstGame != null && uninstGame.Dir == dir) size.Text = string.Format("{0:N1} GB in {1:N0} files", t.Result.Item1 / 1073741824.0, t.Result.Item2);
        })));
        ShowCard("UninstLayer", "UninstCard");
    }
    void CloseUninstall() { HideCard("UninstLayer", "UninstCard"); }
    void ConfirmUninstall() {
        var g = uninstGame; if (g == null) return;
        if (Engine.RunsFrom(g.Dir)) { MessageBox.Show(W, "Close " + g.Name + " first: it's still running.", "Recomp Launcher", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        CloseUninstall();
        RunJobs(new List<Job> { new Job { G = g, Kind = "uninstall", KeepSaves = keepSaves.On && !string.IsNullOrEmpty(g.Saves) } });
    }

    // ---------- choose your game (before a first install) ----------
    Game romGame; string romBetter;
    void BuildRomPicker() {
        N<Button>("BtnRomFile").Click += (s, e) => PickRomFile();
        N<Button>("BtnRomFolder").Click += (s, e) => PickRomFolder();
        N<TextBox>("RomPath").TextChanged += (s, e) => CheckRomPath();
        N<Button>("BtnRomInstall").Click += (s, e) => TryAction("Installing " + romGame.Name, () => ConfirmRom(false));
        N<Button>("BtnRomSkip").Click += (s, e) => TryAction("Installing " + romGame.Name, () => ConfirmRom(true));
        N<Button>("BtnRomCancel").Click += (s, e) => CloseRomPicker();
        N<Button>("BtnRomClose").Click += (s, e) => CloseRomPicker();
        N<Rectangle>("RomDim").MouseLeftButtonDown += (s, e) => CloseRomPicker();
        N<Button>("BtnNspToggle").Click += (s, e) => {
            bool open = N<Border>("NspPanel").Visibility != Visibility.Visible;
            ShowNspPanel(open);
            if (open) W.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => N<Border>("NspPanel").BringIntoView()));
        };
        N<Button>("BtnNspBase").Click += (s, e) => PickInto(N<TextBox>("NspBase"), "Choose the " + romGame.Name + " base game NSP", "Switch package (*.nsp)|*.nsp");
        N<Button>("BtnNspUpdate").Click += (s, e) => PickInto(N<TextBox>("NspUpdate"), "Choose the " + romGame.Name + " update NSP", "Switch package (*.nsp)|*.nsp");
        N<Button>("BtnNspKeys").Click += (s, e) => PickInto(N<TextBox>("NspKeys"), "Choose prod.keys (dumped from your Switch)", "Switch keys (prod.keys)|prod.keys;*.keys");
        N<Button>("BtnNspOut").Click += (s, e) => {
            using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "Where to save the exefs and romfs folders" })
                if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) N<TextBox>("NspOut").Text = Path.Combine(d.SelectedPath, romGame.Id);
        };
        foreach (var n in new[] { "NspBase", "NspUpdate", "NspKeys", "NspOut" }) N<TextBox>(n).TextChanged += (s, e) => CheckNsp();
        N<Button>("BtnNspConvert").Click += (s, e) => TryAction("Converting the " + romGame.Name + " NSP", StartConvert);
    }
    void OpenRomPicker(Game g) {
        var spec = g.Rom; romGame = g;
        N<TextBlock>("RomGame").Text = g.Name.ToUpper() + "  \u2022  " + (g.Latest != null ? g.Latest.Tag : ""); N<TextBlock>("RomGame").Foreground = Ui.Brush(g.Accent);
        N<Image>("RomIcon").Source = (ImageSource)g.Icon;
        N<TextBlock>("RomTitle").Text = spec.Kind == "none" ? "Before you install" : "Choose your game";
        N<TextBlock>("RomNeeds").Text = g.Needs;
        var fm = N<WrapPanel>("RomFormats"); fm.Children.Clear();
        foreach (var f in spec.Formats) {
            fm.Children.Add(new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 4, 10, 5), Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush(Ui.Alpha(g.Accent, 0x2E)), BorderBrush = new SolidColorBrush(Ui.Alpha(g.Accent, 0x80)), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = f.StartsWith(".") ? f.ToUpper() : f, FontSize = 12, FontFamily = Ui.Semi } });
        }
        bool pick = spec.Kind != "none";
        foreach (var n in new[] { "RomPickLabel", "RomPickRow", "RomCheck", "BtnRomSkip" }) ((FrameworkElement)W.FindName(n)).Visibility = pick ? Visibility.Visible : Visibility.Collapsed;
        N<Button>("BtnRomFile").Visibility = spec.Kind == "file" || spec.Kind == "both" ? Visibility.Visible : Visibility.Collapsed;
        N<Button>("BtnRomFolder").Visibility = spec.Kind == "folder" || spec.Kind == "both" ? Visibility.Visible : Visibility.Collapsed;
        string how = spec.Handoff == "tod" ? "Your disc then goes straight to its installer."
                   : g.Rebuild != null ? "Then Hookshot Builder opens: choose your game there and keep its own game folder (" + Paths.Expand(@"%USERPROFILE%\Games\Hookshot") + "); Play starts the game from there."
                   : spec.Kind == "none" ? "Then the game's own setup opens and asks for your game."
                   : "Then the game's setup opens: paste the path there with Ctrl+V (it's copied for you).";
        N<TextBlock>("RomWhere").Text = g.Id == "totk"
            ? "Install downloads the TotK setup and opens it: choose your files there, and " + g.Dir + " as the install folder (or change it in Settings)."
            : "Installs into " + g.Dir + " (change it in Settings). The download is checked against its SHA-256 first. " + how;
        N<TextBox>("RomPath").Text = Roms.Get(g.Id) ?? "";
        CheckRomPath();
        N<Button>("BtnNspToggle").Visibility = spec.HasNsp ? Visibility.Visible : Visibility.Collapsed;
        if (spec.HasNsp) {
            N<TextBlock>("NspHint").Text = spec.NspHint;
            N<StackPanel>("NspUpdateRow").Visibility = spec.NspUpdate ? Visibility.Visible : Visibility.Collapsed;
            N<TextBlock>("NspUpdateLabel").Text = "UPDATE (.NSP)" + (spec.NspUpdateId != null ? "  \u2022  [" + spec.NspUpdateId + "]" : "");
            if (N<TextBox>("NspKeys").Text.Length == 0) N<TextBox>("NspKeys").Text = FindKeys();
            N<TextBox>("NspOut").Text = Path.Combine(Paths.Root, "dumps", g.Id);
        }
        ShowNspPanel(false);
        fb.Visibility = Visibility.Collapsed;
        ShowCard("RomLayer", "RomCard");
    }
    void CloseRomPicker() { HideCard("RomLayer", "RomCard"); }
    void CheckRomPath() {
        var g = romGame; if (g == null) return;
        if (g.Rom.Kind == "none") { N<Button>("BtnRomInstall").IsEnabled = true; return; }
        var r = RomCheck.Test(g.Rom, N<TextBox>("RomPath").Text);
        N<TextBlock>("RomCheck").Text = r.Text; N<TextBlock>("RomCheck").Foreground = Ui.Brush(r.Color);
        N<Button>("BtnRomInstall").IsEnabled = r.Ok;
        romBetter = r.Better;
    }
    string StartDir(string cur) {
        cur = (cur ?? "").Trim('"');
        if (Directory.Exists(cur)) return cur;
        if (File.Exists(cur)) return Path.GetDirectoryName(cur);
        return null;
    }
    void PickRomFile() {
        if (padUsed) { OpenFb(); return; }
        using (var d = new System.Windows.Forms.OpenFileDialog { Title = "Choose your " + romGame.Name + " game file", Filter = (romGame.Rom.Filter ?? "Game files|*.*") + "|All files (*.*)|*.*" }) {
            var sd = StartDir(N<TextBox>("RomPath").Text); if (sd != null) d.InitialDirectory = sd;
            if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) N<TextBox>("RomPath").Text = d.FileName;
        }
    }
    void PickRomFolder() {
        if (padUsed) { OpenFb(); return; }
        using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose the " + romGame.Name + " folder: " + string.Join(", ", romGame.Rom.Formats.Where(f => !f.StartsWith("."))) }) {
            var sd = StartDir(N<TextBox>("RomPath").Text); if (sd != null) d.SelectedPath = sd;
            if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                N<TextBox>("RomPath").Text = d.SelectedPath;
                if (romBetter != null) N<TextBox>("RomPath").Text = romBetter;   // the game is one folder deeper
            }
        }
    }
    void PickInto(TextBox box, string title, string filter) {
        using (var d = new System.Windows.Forms.OpenFileDialog { Title = title, Filter = filter + "|All files (*.*)|*.*" }) {
            var sd = StartDir(box.Text); if (sd != null) d.InitialDirectory = sd;
            if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) box.Text = d.FileName;
        }
    }
    void ConfirmRom(bool skip) {
        var g = romGame; if (g == null) return;
        g.RomPath = null;
        if (!skip && g.Rom.Kind != "none") { g.RomPath = N<TextBox>("RomPath").Text.Trim().Trim('"'); Roms.Set(g.Id, g.RomPath); }
        CloseRomPicker();
        RunJobs(new List<Job> { new Job { G = g, Kind = g.Id == "totk" ? "setup" : "install" } });
    }

    // ---------- NSP to exefs + romfs ----------
    static string FindKeys() {
        string a = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), sys = Path.GetPathRoot(Environment.SystemDirectory);
        var c = new[] { Roms.Get("_keys"), Path.Combine(sys, @"Eden\prod.keys"), Path.Combine(a, @"eden\keys\prod.keys"), Path.Combine(a, @"yuzu\keys\prod.keys"),
            Path.Combine(a, @"suyu\keys\prod.keys"), Path.Combine(a, @"citron\keys\prod.keys"), Path.Combine(a, @"sudachi\keys\prod.keys"),
            Path.Combine(a, @"Ryujinx\system\prod.keys"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".switch\prod.keys") };
        return c.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p)) ?? "";
    }
    static string TitleKeys(string keys) {
        if (string.IsNullOrEmpty(keys)) return "";
        string p = Path.Combine(Path.GetDirectoryName(keys), "title.keys");
        return File.Exists(p) ? p : "";
    }
    void ShowNspPanel(bool on) {
        N<Border>("NspPanel").Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        N<TextBlock>("NspToggleIcon").Text = on ? "\uE70E" : "\uE70D";
        if (on) CheckNsp();
    }
    string T(string name) { return N<TextBox>(name).Text.Trim().Trim('"'); }
    void CheckNsp() {
        var g = romGame; if (g == null || !g.Rom.HasNsp) return;
        var s = g.Rom;
        string b = T("NspBase"), u = T("NspUpdate"), k = T("NspKeys"), o = T("NspOut");
        string bad = null, warn = null, why;
        if (b.Length == 0) bad = "Choose the base game NSP.";
        else if (!File.Exists(b)) bad = "The base game NSP doesn't exist.";
        else if (!b.EndsWith(".nsp", StringComparison.OrdinalIgnoreCase)) bad = "Only .nsp works here (not .nsz or .xci).";
        else if ((why = RomCheck.NspProblem(b)) != null) bad = "The base game file " + why;
        else if (s.NspUpdate && u.Length == 0) bad = "Choose the update NSP too.";
        else if (s.NspUpdate && !File.Exists(u)) bad = "The update NSP doesn't exist.";
        else if (s.NspUpdate && (why = RomCheck.NspProblem(u)) != null) bad = "The update file " + why;
        else if (k.Length == 0 || !File.Exists(k)) bad = "Choose your prod.keys.";
        else if (o.Length == 0) bad = "Choose where to save the exefs and romfs.";
        else if (o.Length > 80) bad = "That folder's path is too long for Windows (" + o.Length + " characters, 80 at most): choose a shorter one, such as C:\\Games\\" + g.Id + ".";
        else {
            var bn = Regex.Match(Path.GetFileName(b), @"\[(0100[0-9A-Fa-f]{12})\]"); var bv = Regex.Match(Path.GetFileName(b), @"\[v(\d+)\]");
            if (bn.Success && !bn.Groups[1].Value.Equals(s.TitleId, StringComparison.OrdinalIgnoreCase)) warn = "The base NSP's name says [" + bn.Groups[1].Value.ToUpper() + "]; " + g.Name + " is [" + s.TitleId + "].";
            else if (bv.Success && bv.Groups[1].Value != "0") warn = "The base NSP's name says [v" + bv.Groups[1].Value + "]: that looks like an update, not the base game.";
            if (s.NspUpdate && warn == null) {
                var un = Regex.Match(Path.GetFileName(u), @"\[(0100[0-9A-Fa-f]{12})\]");
                if (un.Success && !un.Groups[1].Value.Equals(s.NspUpdateId, StringComparison.OrdinalIgnoreCase)) warn = "The update NSP's name says [" + un.Groups[1].Value.ToUpper() + "]; the " + g.Name + " update is [" + s.NspUpdateId + "].";
            }
        }
        N<Button>("BtnNspConvert").IsEnabled = bad == null;
        string tk = bad == null && TitleKeys(k).Length > 0 ? ", with title.keys from the same folder" : "";
        N<TextBlock>("NspCheck").Text = bad ?? warn ?? "\u2713  Ready" + tk;
        N<TextBlock>("NspCheck").Foreground = Ui.Brush(bad != null ? Ui.Muted : warn != null ? Ui.Gold : Ui.Teal);
    }
    void StartConvert() {
        var g = romGame;
        var n = new NspJob { Base = T("NspBase"), Update = g.Rom.NspUpdate ? T("NspUpdate") : "", Keys = T("NspKeys"), Out = T("NspOut") };
        n.TitleKeys = TitleKeys(n.Keys);
        Roms.Set("_keys", n.Keys);
        CloseRomPicker();
        RunJobs(new List<Job> { new Job { G = g, Kind = "convert", Nsp = n } });
    }

    // ---------- file browser (for a controller: Windows' dialogs can't be used with one) ----------
    Grid fb; TextBlock fbKind, fbTitle, fbPath, fbCheck; StackPanel fbList; ScrollViewer fbScroll; Button fbUse, fbCancel;
    string fbDir, fbMode = "rom"; FolderRow fbRow;
    List<FbItem> fbItems = new List<FbItem>(); List<Border> fbRows = new List<Border>(); int fbIdx;
    void BuildFileBrowser() {
        fb = (Grid)XamlReader.Parse(ResText("FileBrowser.xaml"));
        Func<string, object> f = n => fb.FindName(n);
        fbKind = (TextBlock)f("FbKind"); fbTitle = (TextBlock)f("FbTitle"); fbPath = (TextBlock)f("FbPath"); fbCheck = (TextBlock)f("FbCheck");
        fbList = (StackPanel)f("FbList"); fbScroll = (ScrollViewer)f("FbScroll"); fbUse = (Button)f("FbUse"); fbCancel = (Button)f("FbCancel");
        fbUse.Style = (Style)W.FindResource("Btn"); fbUse.Background = Ui.Brush(Ui.Teal); fbUse.Foreground = Ui.Brush("#0A0E11");
        fbCancel.Style = (Style)W.FindResource("Btn");
        fbUse.Click += (s, e) => UseFbFolder(); fbCancel.Click += (s, e) => CloseFb();
        ((Rectangle)f("FbDim")).MouseLeftButtonDown += (s, e) => { CloseFb(); e.Handled = true; };
        N<Grid>("RomLayer").Children.Add(fb);
    }
    bool FbVisible() { return fb.Visibility == Visibility.Visible; }
    bool FbFiles() { return fbMode == "rom" && (romGame.Rom.Kind == "file" || romGame.Rom.Kind == "both"); }
    bool FbFolders() { return fbMode == "setfolder" || romGame.Rom.Kind == "folder" || romGame.Rom.Kind == "both"; }
    void OpenFbFolder(FolderRow r) {
        fbMode = "setfolder"; fbRow = r;
        // the browser lives on the game picker's layer: lift it over Settings while it's used there
        var rl = N<Grid>("RomLayer"); rl.Visibility = Visibility.Visible; rl.Opacity = 1;
        foreach (UIElement c in rl.Children) if (c != fb) c.Visibility = Visibility.Collapsed;
        Panel.SetZIndex(rl, 10);
        fbKind.Text = r.Label.ToUpper(); fbTitle.Text = "Choose the folder";
        fb.Visibility = Visibility.Visible;
        LoadFb(Directory.Exists(r.Box.Text) ? r.Box.Text : null, null);
    }
    void OpenFb() {
        fbMode = "rom";
        fbKind.Text = romGame.Name.ToUpper();
        fbTitle.Text = FbFiles() ? "Choose your game file" : "Choose your game folder";
        fb.Visibility = Visibility.Visible;
        string cur = StartDir(N<TextBox>("RomPath").Text);
        LoadFb(cur, null);
        SetHints();
    }
    void CloseFb() {
        fb.Visibility = Visibility.Collapsed;
        if (fbMode == "setfolder") {
            var rl = N<Grid>("RomLayer"); rl.Visibility = Visibility.Collapsed; Panel.SetZIndex(rl, 0);
            foreach (UIElement c in rl.Children) if (c != fb) c.Visibility = Visibility.Visible;
            fbMode = "rom";
        }
        SetHints();
    }
    void LoadFb(string dir, string pick) {
        fbDir = dir; fbItems = new List<FbItem>();
        if (dir == null) {
            fbPath.Text = "This PC";
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var recent = new List<string>();
            if (fbMode == "rom") { string r = StartDir(Roms.Get(romGame.Id)); if (r != null) recent.Add(r); }
            foreach (var p in recent) fbItems.Add(new FbItem { Kind = "dir", Path = p, Name = Path.GetFileName(p.TrimEnd('\\')), Note = "Used before" });
            foreach (var p in new[] { Path.Combine(home, "Downloads"), Path.Combine(home, "Desktop"), Path.Combine(home, "Documents") })
                if (Directory.Exists(p)) fbItems.Add(new FbItem { Kind = "dir", Path = p, Name = Path.GetFileName(p) });
            foreach (var d in DriveInfo.GetDrives()) {
                try { if (d.IsReady) fbItems.Add(new FbItem { Kind = "drive", Path = d.RootDirectory.FullName, Name = d.Name.TrimEnd('\\') + "  " + d.VolumeLabel,
                    Note = string.Format("{0:N0} GB free", d.AvailableFreeSpace / 1073741824.0) }); } catch { }
            }
        } else {
            fbPath.Text = dir;
            fbItems.Add(new FbItem { Kind = "up", Path = Path.GetDirectoryName(dir.TrimEnd('\\')), Name = ".. (up one folder)" });
            try {
                foreach (var d in new DirectoryInfo(dir).GetDirectories().Where(x => (x.Attributes & FileAttributes.Hidden) == 0).OrderBy(x => x.Name))
                    fbItems.Add(new FbItem { Kind = "dir", Path = d.FullName, Name = d.Name });
                if (FbFiles()) {
                    var ext = romGame.Rom.Ext;
                    foreach (var fi in new DirectoryInfo(dir).GetFiles().Where(x => ext.Contains(x.Extension.ToLowerInvariant())).OrderBy(x => x.Name))
                        fbItems.Add(new FbItem { Kind = "file", Path = fi.FullName, Name = fi.Name, Note = string.Format("{0:N1} GB", fi.Length / 1073741824.0) });
                }
            } catch (Exception e) { fbItems.Add(new FbItem { Kind = "none", Name = "Can't open this folder: " + e.Message }); }
        }
        fbList.Children.Clear(); fbRows = new List<Border>();
        foreach (var it in fbItems) {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            string glyph = it.Kind == "file" ? "\uE8A5" : it.Kind == "drive" ? "\uEDA2" : it.Kind == "up" ? "\uE74A" : "\uED25";
            row.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.Mdl, FontSize = 14, Foreground = Ui.Brush(it.Kind == "file" ? Ui.Teal : Ui.Muted), VerticalAlignment = VerticalAlignment.Center });
            var name = new TextBlock { Text = it.Name, FontSize = 13.5, Foreground = Ui.Brush("#E8E6DF"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(name, 1); row.Children.Add(name);
            if (it.Note != null) { var n = new TextBlock { Text = it.Note, FontSize = 11.5, Foreground = Ui.Brush(Ui.Dim), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(n, 2); row.Children.Add(n); }
            var b = new Border { Child = row, Padding = new Thickness(10, 7, 12, 7), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var item = it; int idx = fbRows.Count;
            b.MouseLeftButtonUp += (s, e) => { SelectFb(idx); };
            b.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { SelectFb(idx); EnterFb(); } };
            fbRows.Add(b); fbList.Children.Add(b);
        }
        int start = pick != null ? Math.Max(0, fbItems.FindIndex(i => string.Equals(i.Path, pick, StringComparison.OrdinalIgnoreCase))) : (dir != null && fbItems.Count > 1 ? 1 : 0);
        SelectFb(start);
        CheckFbHere();
    }
    void SelectFb(int i) {
        if (fbRows.Count == 0) return;
        fbIdx = Math.Max(0, Math.Min(fbRows.Count - 1, i));
        for (int k = 0; k < fbRows.Count; k++) fbRows[k].Background = Ui.Brush(k == fbIdx ? "#203039" : "#00000000");
        fbRows[fbIdx].BringIntoView();
    }
    void CheckFbHere() {
        bool can = fbDir != null && FbFolders();
        fbUse.Visibility = can ? Visibility.Visible : Visibility.Collapsed;
        if (fbMode == "rom" && fbDir != null && FbFolders()) {
            var r = RomCheck.Test(romGame.Rom, fbDir);
            fbCheck.Text = r.Ok ? "\u2713  This folder has the game in it: X (or Use this folder) chooses it." : r.Text; fbCheck.Foreground = Ui.Brush(r.Ok ? Ui.Teal : Ui.Muted);
        } else fbCheck.Text = fbDir == null ? "Pick a place to look in." : FbFiles() ? "Pick your game file (" + string.Join(", ", romGame.Rom.Ext) + ")." : "";
        if (fbCheck.Foreground == null) fbCheck.Foreground = Ui.Brush(Ui.Muted);
    }
    void EnterFb() {
        if (fbIdx >= fbItems.Count) return;
        var it = fbItems[fbIdx];
        if (it.Kind == "file") { N<TextBox>("RomPath").Text = it.Path; CloseFb(); return; }
        if (it.Kind == "up") { BackFb(); return; }
        if (it.Kind == "none") return;
        LoadFb(it.Path, null);
    }
    void BackFb() {
        if (fbDir == null) { CloseFb(); return; }
        string parent = Path.GetDirectoryName(fbDir.TrimEnd('\\'));
        LoadFb(parent == null || parent.Length == 0 ? null : parent, fbDir);
    }
    void UseFbFolder() {
        if (fbDir == null) return;
        if (fbMode == "setfolder") { fbRow.Box.Text = fbDir; CloseFb(); return; }
        N<TextBox>("RomPath").Text = fbDir;
        if (romBetter != null && Paths.Under(romBetter, fbDir)) N<TextBox>("RomPath").Text = romBetter;
        CloseFb();
    }
    bool FbKey(Key k) {
        switch (k) {
            case Key.Up: SelectFb(fbIdx - 1); break;
            case Key.Down: SelectFb(fbIdx + 1); break;
            case Key.PageUp: SelectFb(fbIdx - 8); break;
            case Key.PageDown: SelectFb(fbIdx + 8); break;
            case Key.Enter: EnterFb(); break;
            case Key.Right: { var it = fbIdx < fbItems.Count ? fbItems[fbIdx] : null; if (it != null && (it.Kind == "dir" || it.Kind == "drive")) EnterFb(); break; }
            case Key.Back: BackFb(); break;
            case Key.Left: if (fbDir != null) BackFb(); break;
            case Key.Escape: CloseFb(); break;
            default: return false;
        }
        SetHints();
        return true;
    }
}

// Checks a picked file or folder against what the game's setup reads
class RomResult { public bool Ok; public string Text, Color, Better; }
static class RomCheck {
    // a Switch game's title ID, from exefs\main.npdm (the program ID in its ACI0 section)
    static string NpdmTitle(string dir) {
        string p = Path.Combine(dir, @"exefs\main.npdm");
        if (!File.Exists(p)) return null;
        try {
            var b = File.ReadAllBytes(p);
            int i = System.Text.Encoding.ASCII.GetString(b).IndexOf("ACI0");
            if (i < 0 || i + 0x18 > b.Length) return null;
            return BitConverter.ToUInt64(b, i + 0x10).ToString("X16");
        } catch { return null; }
    }
    static bool Match(RomSpec s, string dir, out string[] set, out string other) {
        set = null; other = null;
        foreach (var need in s.FolderNeeds) {
            if (need.All(n => File.Exists(Path.Combine(dir, n)) || Directory.Exists(Path.Combine(dir, n)))) {
                if (s.TitleId != null) { string id = NpdmTitle(dir); if (id != null && id != s.TitleId) { other = id; return false; } }
                set = need; return true;
            }
        }
        return false;
    }
    public static RomResult Test(RomSpec s, string path) {
        path = (path ?? "").Trim().Trim('"');
        if (path.Length == 0) return new RomResult { Text = "Choose your game file or folder.", Color = Ui.Muted };
        if (File.Exists(path)) {
            var fi = new FileInfo(path);
            if (s.Kind == "folder") return new RomResult { Text = "This game needs a folder, not a file: " + string.Join(", ", s.Formats) + ".", Color = Ui.Red };
            string ext = fi.Extension.ToLowerInvariant();
            if (!s.Ext.Contains(ext)) return new RomResult { Text = ext + " files aren't accepted. Use " + string.Join(", ", s.Ext) + (s.Kind == "both" ? " or a game folder" : "") + ".", Color = Ui.Red };
            string size = string.Format("{0:N1} GB", fi.Length / 1073741824.0);
            if ((ext == ".wux" || ext == ".wud") && !File.Exists(Path.ChangeExtension(fi.FullName, ".key")))
                return new RomResult { Ok = true, Color = Ui.Gold, Text = "\u2713  " + fi.Name + " (" + size + "). Its setup also asks for the disc key (put it next to the image as " + Path.GetFileNameWithoutExtension(fi.Name) + ".key to have it found) and the Wii U common key." };
            return new RomResult { Ok = true, Text = "\u2713  " + fi.Name + " (" + size + ")", Color = Ui.Teal };
        }
        if (!Directory.Exists(path)) return new RomResult { Text = "That file or folder doesn't exist.", Color = Ui.Red };
        if (s.Kind == "file") return new RomResult { Text = "This game needs a disc image file: " + string.Join(", ", s.Ext) + ".", Color = Ui.Red };
        string[] set; string other;
        if (Match(s, path, out set, out other))
            return new RomResult { Ok = true, Color = Ui.Teal, Text = "\u2713  Folder with " + string.Join(", ", set) + (s.TitleId != null ? ", title ID " + s.TitleId : "") };
        if (other != null) return new RomResult { Text = "This is a different game (title ID " + other + "); this one is " + s.TitleId + ".", Color = Ui.Red };
        try {
            foreach (var sub in Directory.GetDirectories(path)) {
                string[] s2; string o2;
                if (Match(s, sub, out s2, out o2)) return new RomResult { Text = "The game is one folder deeper, in \"" + Path.GetFileName(sub) + "\". Choose that folder.", Color = Ui.Gold, Better = sub };
            }
        } catch { }
        return new RomResult { Text = "This folder doesn't have the game in it: it needs " + string.Join(", or ", s.FolderNeeds.Select(n => string.Join(" + ", n))) + ".", Color = Ui.Red };
    }
    // every NSP starts with "PFS0": says why a file isn't one (archives renamed to .nsp are common), or null
    public static string NspProblem(string path) {
        var b = new byte[32]; long len;
        try { using (var fs = File.OpenRead(path)) { fs.Read(b, 0, 32); len = fs.Length; } } catch (Exception e) { return "can't be read: " + e.Message; }
        string magic = System.Text.Encoding.ASCII.GetString(b, 0, 4);
        if (magic == "PFS0") return null;
        string kind = b[0] == 0x37 && b[1] == 0x7A && b[2] == 0xBC && b[3] == 0xAF ? "7-Zip archive" : magic == "PK\u0003\u0004" ? "ZIP archive"
                    : magic == "Rar!" ? "RAR archive" : magic == "NCZS" || magic == "NCZB" ? "NSZ (compressed NSP)" : null;
        if (kind == "7-Zip archive") {
            ulong want = 32 + BitConverter.ToUInt64(b, 12) + BitConverter.ToUInt64(b, 20);
            if ((ulong)len < want) return string.Format("is a 7-Zip archive renamed to .nsp, and an incomplete download: {0:N0} of {1:N0} MB. Download it again, then unpack it and choose the .nsp inside.", len / 1048576.0, want / 1048576.0);
        }
        return kind != null ? "is a " + kind + " renamed to .nsp, not an NSP. Unpack it first (with 7-Zip, for example) and choose the .nsp that comes out." : "isn't an NSP (an NSP starts with PFS0).";
    }
}
}
