// Personalise: the picture at the top left of the Home screen, its frame colour and a name beside it. The picture is
// the launcher's logo, any game's artwork, or a picture of your own (any character you like), kept in the avatars
// folder; "Add a picture" copies one there. Picks apply at once. Settings: Avatar ("", "game:<id>" or
// "pic:<file name>"), AvatarFrame ("" for the theme's, "none" or a colour), PlayerName, all of the current profile
// (Profiles.cs).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace RecompLauncher {

partial class Shell {
    TextBlock profTitle; Button profDone;
    Grid profLayer; Border profCard; StackPanel profRows; TextBox profName; Ellipse profPreview;
    Border homeAvatar; Ellipse homeAvatarFace; TextBlock homeName;
    readonly List<FrameworkElement> profStops = new List<FrameworkElement>();
    readonly Dictionary<FrameworkElement, Action> profActions = new Dictionary<FrameworkElement, Action>();
    int profIdx = -1;
    static readonly string[] FrameColours = { "", "#ffffff", "#0ab4dc", "#3fb8a0", "#d9b45a", "#f0453a", "#ff8fb8", "#8a57d6", "#2d2d2d", "none" };
    static string AvatarDir { get { return Path.Combine(Paths.Root, "avatars"); } }
    bool ProfileOpen { get { return profLayer != null && profLayer.Visibility == Visibility.Visible; } }

    // ---------- the picture on the Home screen ----------
    // the round picture and the name beside it, top left; a click opens the profiles (Players.cs)
    FrameworkElement BuildHomeAvatar() {
        homeAvatarFace = new Ellipse { Width = 42, Height = 42 };
        homeAvatar = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(24), BorderThickness = new Thickness(3), Background = Ui.Brush(Raised),
            Child = homeAvatarFace, VerticalAlignment = VerticalAlignment.Center, Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.25 } };
        homeName = new TextBlock { FontSize = 16, FontFamily = Ui.Semi, Foreground = Ui.Brush(Ui.Text), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(36, 10, 0, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = "Profiles" };
        row.Children.Add(homeAvatar); row.Children.Add(homeName);
        row.MouseLeftButtonUp += (s, e) => OpenPlayers();
        ApplyAvatar();
        return row;
    }
    void ApplyAvatar() {
        if (homeAvatar == null) return;
        var brush = AvatarBrush(Profiles.Get("Avatar"));
        homeAvatarFace.Fill = brush;
        if (profPreview != null) profPreview.Fill = brush;
        string f = Profiles.Get("AvatarFrame");
        var frame = f == "none" ? Brushes.Transparent : f == "" ? Ui.Brush(Raised) : Ui.Brush(f);
        homeAvatar.BorderBrush = frame;
        homeAvatar.Background = f == "none" ? Brushes.Transparent : Ui.Brush(Raised);
        // with more than one profile there's always a name ("Player 2" until one is typed)
        string name = Profiles.All.Count > 1 ? Profiles.DisplayName(Profiles.Current) : Profiles.Get("PlayerName");
        homeName.Text = name;
        homeName.Visibility = name.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    Brush AvatarBrush(string key) { return AvatarBrush(key, Profiles.Get("Character")); }
    // a profile's picture: its own character when that's the pick
    Brush AvatarBrush(PlayerProfile p) { return AvatarBrush(p.Avatar, p.Character); }
    Brush AvatarBrush(string key, string character) {
        var src = (key == "char" ? AvatarArt.Portrait(AvatarDef.Parse(character), 192) : AvatarSource(key)) ?? N<Image>("LogoImg").Source;
        var b = new ImageBrush(src) { Stretch = Stretch.UniformToFill };
        b.Freeze();
        return b;
    }
    ImageSource AvatarSource(string key) {
        key = key ?? "";
        try {
            if (key == "char") return AvatarArt.Portrait(AvatarDef.Parse(Profiles.Get("Character")), 192);
            if (key.StartsWith("game:")) { var g = Catalog.Find(key.Substring(5)); return g != null ? g.Icon as ImageSource : null; }
            if (key.StartsWith("pic:")) {
                string p = Path.Combine(AvatarDir, key.Substring(4));
                if (!File.Exists(p)) return null;
                var img = new BitmapImage();
                img.BeginInit(); img.UriSource = new Uri(p); img.CacheOption = BitmapCacheOption.OnLoad; img.DecodePixelWidth = 192; img.EndInit(); img.Freeze();
                return img;
            }
        } catch { }
        return null;
    }
    void SetAvatar(string key) {
        Sound.Play(Sfx.Select);
        Profiles.Set("Avatar", key); Profiles.Save();
        ApplyAvatar(); MarkProfile();
    }
    void SetFrame(string colour) {
        Sound.Play(Sfx.Select);
        Profiles.Set("AvatarFrame", colour); Profiles.Save();
        ApplyAvatar(); MarkProfile();
    }

    // ---------- the Personalise window ----------
    void BuildProfile() {
        if (profLayer != null) return;
        profLayer = new Grid { Visibility = Visibility.Collapsed, Opacity = 0 };
        var dim = new Rectangle { Fill = Ui.Brush("#C0050709") };
        dim.MouseLeftButtonUp += (s, e) => CloseProfile();
        profLayer.Children.Add(dim);
        profCard = new Border { Width = 700, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, CornerRadius = new CornerRadius(18),
            Background = Ui.Brush("#11181D"), BorderBrush = Ui.Brush("#26333C"), BorderThickness = new Thickness(1), Padding = new Thickness(34, 30, 34, 28),
            RenderTransformOrigin = new Point(0.5, 0.5), Effect = new DropShadowEffect { BlurRadius = 60, ShadowDepth = 0, Opacity = 0.7, Color = Colors.Black } };
        var tg = new TransformGroup(); tg.Children.Add(new ScaleTransform()); tg.Children.Add(new TranslateTransform());
        profCard.RenderTransform = tg;
        var stack = new StackPanel();

        // header: the picture as it looks now, the name box, a close button
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        profPreview = new Ellipse { Width = 78, Height = 78, Stroke = Ui.Brush("#26333C"), StrokeThickness = 1 };
        head.Children.Add(profPreview);
        var titles = new StackPanel { Margin = new Thickness(22, 0, 40, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "PERSONALISE", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal) });
        profTitle = new TextBlock { Text = "Your picture and name", FontSize = 24, FontFamily = Ui.Semi, Margin = new Thickness(0, 2, 0, 8) };
        titles.Children.Add(profTitle);
        var nameRow = new Grid();
        profName = new TextBox { Height = 34, FontSize = 13.5, Padding = new Thickness(10, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 24,
            Background = Ui.Brush("#1C252C"), Foreground = Ui.Brush(Ui.Text), BorderBrush = Ui.Brush("#26333C"), CaretBrush = Ui.Brush(Ui.Text) };
        var nameHint = new TextBlock { Text = "Your name (shown beside the picture)", FontSize = 13, Foreground = Ui.Brush(Ui.Dim), Margin = new Thickness(13, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        profName.TextChanged += (s, e) => {
            nameHint.Visibility = profName.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
            Profiles.Set("PlayerName", profName.Text.Trim()); ApplyAvatar();
        };
        nameRow.Children.Add(profName); nameRow.Children.Add(nameHint);
        titles.Children.Add(nameRow);
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);
        var close = new Button { Style = (Style)W.FindResource("Chrome"), Content = "", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -8, -14, 0) };
        close.Click += (s, e) => CloseProfile();
        Grid.SetColumn(close, 1);
        head.Children.Add(close);
        stack.Children.Add(head);

        profRows = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        stack.Children.Add(new ScrollViewer { MaxHeight = 390, Margin = new Thickness(0, 14, 0, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Content = profRows });

        var done = MakeButton("Done", 44); done.Background = Ui.Brush(Ui.Teal); done.Foreground = Ui.Brush("#0A0E11"); done.Padding = new Thickness(28, 0, 28, 0);
        done.HorizontalAlignment = HorizontalAlignment.Right; done.Margin = new Thickness(0, 20, 0, 0);
        done.Click += (s, e) => CloseProfile();
        done.Tag = "done";
        profDone = done;
        stack.Children.Add(done);

        profCard.Child = stack;
        profLayer.Children.Add(profCard);
        profLayer.LayoutTransform = N<Grid>("Main").LayoutTransform;
        N<Grid>("Clipped").Children.Add(profLayer);
        W.RegisterName("ProfileLayer", profLayer);
        W.RegisterName("ProfileCard", profCard);
    }
    // the choices, made again each time the window opens (pictures may have been added to the folder)
    void FillProfile() {
        profRows.Children.Clear(); profStops.Clear(); profActions.Clear();

        profRows.Children.Add(Heading("FRAME", true));
        var frames = new WrapPanel();
        foreach (var c in FrameColours) {
            string colour = c;
            var dot = new Grid { Width = 40, Height = 40, Margin = new Thickness(0, 0, 10, 8), Cursor = Cursors.Hand, Background = Brushes.Transparent, Tag = "frame:" + c };
            var ring = new Ellipse { StrokeThickness = 2.5 };
            var fill = new Ellipse { Margin = new Thickness(6), Fill = c == "none" ? (Brush)Brushes.Transparent : c == "" ? Ui.Brush(Raised) : Ui.Brush(c), Stroke = Ui.Brush("#26333C"), StrokeThickness = 1 };
            dot.Children.Add(ring); dot.Children.Add(fill);
            if (c == "none") dot.Children.Add(new Line { X1 = 12, Y1 = 28, X2 = 28, Y2 = 12, Stroke = Ui.Brush(Ui.Muted), StrokeThickness = 1.5 });
            dot.ToolTip = c == "" ? "The theme's" : c == "none" ? "No frame" : null;
            AddProfStop(dot, () => SetFrame(colour));
            frames.Children.Add(dot);
        }
        profRows.Children.Add(frames);

        profRows.Children.Add(Heading("YOUR PICTURES", false));
        profRows.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = Ui.Brush(Ui.Muted), Margin = new Thickness(0, 0, 0, 8),
            Text = "Any character you like: add a picture (PNG or JPG, square works best). It's copied to the avatars folder, where you can also drop pictures yourself." });
        var pics = new WrapPanel();
        pics.Children.Add(AvatarChoice("", N<Image>("LogoImg").Source, "The launcher's logo"));
        try {
            if (Directory.Exists(AvatarDir))
                foreach (var f in Directory.GetFiles(AvatarDir).Where(IsPicture).Where(f => !Path.GetFileNameWithoutExtension(f).Equals("preview", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f)) {
                    var src = AvatarSource("pic:" + Path.GetFileName(f));
                    if (src != null) pics.Children.Add(AvatarChoice("pic:" + Path.GetFileName(f), src, Path.GetFileNameWithoutExtension(f)));
                }
        } catch { }
        bool hasChar = Profiles.Get("Character").Length > 0;
        if (hasChar) pics.Children.Add(AvatarChoice("char", AvatarSource("char"), "Your character"));
        pics.Children.Add(ActionChoice("", hasChar ? "Edit your character" : "Make a character", OpenCreator));
        pics.Children.Add(ActionChoice("", "Add a picture", AddPicture));
        pics.Children.Add(ActionChoice("","Open the avatars folder", () => { Directory.CreateDirectory(AvatarDir); OpenFolder(AvatarDir); }));
        profRows.Children.Add(pics);

        profRows.Children.Add(Heading("FROM YOUR GAMES", false));
        var games = new WrapPanel();
        foreach (var g in Games.Where(x => x.Icon != null).OrderBy(Catalog.SeriesOf).ThenBy(x => x.Rank))
            games.Children.Add(AvatarChoice("game:" + g.Id, (ImageSource)g.Icon, g.Name));
        profRows.Children.Add(games);

        foreach (var b in ((StackPanel)profCard.Child).Children.OfType<Button>()) AddProfStop(b, () => CloseProfile());
        MarkProfile();
    }
    static bool IsPicture(string f) {
        var e = Path.GetExtension(f).ToLowerInvariant();
        return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".bmp" || e == ".gif";
    }
    FrameworkElement AvatarChoice(string key, ImageSource src, string tip) {
        var g = new Grid { Width = 66, Height = 66, Margin = new Thickness(0, 0, 10, 10), Cursor = Cursors.Hand, Background = Brushes.Transparent, Tag = "avatar:" + key, ToolTip = tip };
        g.Children.Add(new Ellipse { StrokeThickness = 3 });
        var b = new ImageBrush(src) { Stretch = Stretch.UniformToFill }; b.Freeze();
        g.Children.Add(new Ellipse { Margin = new Thickness(6), Fill = b });
        AddProfStop(g, () => SetAvatar(key));
        return g;
    }
    FrameworkElement ActionChoice(string glyph, string tip, Action a) {
        var g = new Grid { Width = 66, Height = 66, Margin = new Thickness(0, 0, 10, 10), Cursor = Cursors.Hand, Background = Brushes.Transparent, Tag = "action", ToolTip = tip };
        g.Children.Add(new Ellipse { StrokeThickness = 3 });
        g.Children.Add(new Ellipse { Margin = new Thickness(6), Fill = Ui.Brush("#1C252C"), Stroke = Ui.Brush("#26333C"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 2 } });
        g.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.Mdl, FontSize = 18, Foreground = Ui.Brush(Ui.Muted), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        AddProfStop(g, () => { Sound.Play(Sfx.Select); a(); });
        return g;
    }
    void AddProfStop(FrameworkElement el, Action a) {
        profStops.Add(el); profActions[el] = a;
        if (!(el is Button)) el.MouseLeftButtonUp += (s, e) => { profIdx = profStops.IndexOf(el); a(); };
    }
    // the picked picture and frame get a ring; the controller's place gets a glow
    void MarkProfile() {
        string av = "avatar:" + Profiles.Get("Avatar"), fr = "frame:" + Profiles.Get("AvatarFrame");
        for (int i = 0; i < profStops.Count; i++) {
            var el = profStops[i];
            var tag = el.Tag as string;
            var g = el as Grid;
            if (g != null && g.Children.Count > 0 && g.Children[0] is Ellipse) ((Ellipse)g.Children[0]).Stroke = tag == av || tag == fr ? Ui.Brush(Ui.Teal) : Brushes.Transparent;
            el.Effect = padUsed && i == profIdx ? SetGlow : null;
        }
    }
    void AddPicture() {
        using (var d = new System.Windows.Forms.OpenFileDialog { Title = "Choose a picture", Filter = "Pictures (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif" }) {
            if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            try {
                Directory.CreateDirectory(AvatarDir);
                string name = Path.GetFileName(d.FileName), dest = Path.Combine(AvatarDir, name);
                if (!string.Equals(Path.GetFullPath(d.FileName), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) {
                    for (int n = 2; File.Exists(dest); n++) dest = Path.Combine(AvatarDir, Path.GetFileNameWithoutExtension(name) + " " + n + Path.GetExtension(name));
                    File.Copy(d.FileName, dest);
                }
                Profiles.Set("Avatar", "pic:" + Path.GetFileName(dest)); Profiles.Save();
                ApplyAvatar(); FillProfile();
            } catch (Exception e) { Log("Couldn't add the picture: " + e.Message, Ui.Red); }
        }
    }

    void OpenProfile() {
        if (homeAvatar == null || ProfileOpen) return;
        BuildProfile();
        FillProfile();
        profName.Text = Profiles.Get("PlayerName");
        ApplyAvatar();
        // the controller starts on the picture in use
        string av = "avatar:" + Profiles.Get("Avatar");
        profIdx = Math.Max(0, profStops.FindIndex(el => el.Tag as string == av));
        MarkProfile();
        ShowCard("ProfileLayer", "ProfileCard");
        if (padUsed) profStops[profIdx].BringIntoView();
    }
    void CloseProfile() {
        if (!ProfileOpen) return;
        Profiles.Set("PlayerName", profName.Text.Trim()); Profiles.Save();
        Keyboard.ClearFocus();
        HideCard("ProfileLayer", "ProfileCard");
    }
    // controller: arrows go to the nearest choice that way, A picks, B closes
    void ProfilePad(int bit) {
        if (bit == Pad.B || bit == Pad.Y) { CloseProfile(); return; }
        if (bit == Pad.A) { if (profIdx >= 0 && profIdx < profStops.Count) profActions[profStops[profIdx]](); return; }
        int dx = bit == Pad.Left ? -1 : bit == Pad.Right ? 1 : 0, dy = bit == Pad.Up ? -1 : bit == Pad.Down ? 1 : 0;
        if (dx == 0 && dy == 0) return;
        if (profIdx < 0) { profIdx = 0; MarkProfile(); return; }
        Func<FrameworkElement, Point> centre = el => el.TransformToAncestor(profCard).Transform(new Point(el.ActualWidth / 2, el.ActualHeight / 2));
        var c0 = centre(profStops[profIdx]);
        int best = -1; double bestScore = double.MaxValue;
        for (int i = 0; i < profStops.Count; i++) {
            if (i == profIdx || !profStops[i].IsVisible) continue;
            var c = centre(profStops[i]);
            double along = (c.X - c0.X) * dx + (c.Y - c0.Y) * dy, across = Math.Abs((c.X - c0.X) * dy) + Math.Abs((c.Y - c0.Y) * dx);
            if (along < 8) continue;
            double score = along + across * (dy != 0 ? 0.6 : 3);
            if (score < bestScore) { bestScore = score; best = i; }
        }
        if (best < 0) { Sound.Play(Sfx.Bump); return; }
        Sound.Play(Sfx.Move);
        profIdx = best; MarkProfile();
        profStops[profIdx].BringIntoView();
    }
}
}
