// Favourites, collections, search and quick jumps in the library.
//   Favourites: RB on a home tile, or the game page's star; favourites lead the home row and have a filter.
//   Collections: the game page's Collections button opens a list of the profile's collections with a switch each, and
//   New collection (named with the on-screen keyboard). A collection with no games left goes away. Each one is a
//   filter in All games.
//   Search: RB in All games opens the on-screen keyboard; the games narrow down as you type. B clears it.
//   Letter jump: LT / RT in All games go to the previous / next first letter, with the letter shown big.
// The on-screen keyboard (Osk) is shared: d-pad to a key, A types, X deletes, Y adds a space, Menu is done, B cancels;
// a real keyboard types into it too.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace RecompLauncher {

partial class Shell {
    // ---------- favourites ----------
    void ToggleFavourite(Game g) {
        Profiles.ToggleFav(g.Id);
        bool on = Profiles.IsFav(g.Id);
        Sound.Play(on ? Sfx.Select : Sfx.Back);
        SetStep(on ? g.Name + " is a favourite: it leads the home row." : g.Name + " isn't a favourite any more.", on ? Ui.Teal : Ui.Muted);
        UpdateHomeTile(g);
        ApplySort(true); ApplyFilter(); UpdateChips();
        if (view == View.Home) MarkHome(null, sel);
        if (homePage == "game") UpdateGamePage();
        SetHints();
    }

    // Which games are hidden. Games found in the store libraries (Steam, Epic, GOG) are hidden until you show them
    // (Settings > General > Store library games: "auto", the default, or "show"): those you show are kept in
    // ShownStore, the other hidden games in Hidden.
    static bool AutoHideStores { get { return Settings.Get("StoreAutoHide") != "show"; } }
    static bool InList(string key, string id) { return ("," + Settings.Get(key) + ",").Contains("," + id + ","); }
    void LoadHidden() {
        foreach (var g in Games) g.Hidden = g.FromLibrary && AutoHideStores ? !InList("ShownStore", g.Id) : InList("Hidden", g.Id);
    }
    void SaveHidden() {
        Settings.Set("Hidden", string.Join(",", Games.Where(x => x.Hidden && !(x.FromLibrary && AutoHideStores)).Select(x => x.Id)));
        Settings.Set("ShownStore", string.Join(",", Games.Where(x => x.FromLibrary && !x.Hidden).Select(x => x.Id)));
    }
    // Does the game keep the Xbox / PS / Home button for itself? Its games.json says so (OwnGuide, e.g. Super Mario
    // Odyssey), and the game page's "In-game menu" button switches it per game (kept in GuideLeft / GuideTaken).
    static bool GuideLeftTo(Game g) {
        if (InList("GuideLeft", g.Id)) return true;
        if (InList("GuideTaken", g.Id)) return false;
        return g.OwnGuide;
    }
    void ToggleGuide(Game g) {
        bool left = !GuideLeftTo(g);
        Func<string, bool, string> with = (key, on) => string.Join(",", Settings.Get(key).Split(',').Where(x => x.Length > 0 && x != g.Id).Concat(on ? new[] { g.Id } : new string[0]));
        Settings.Set("GuideLeft", with("GuideLeft", left)); Settings.Set("GuideTaken", with("GuideTaken", !left));
        Settings.Save();
        Sound.Play(Sfx.Select);
        SetStep(left ? g.Name + " keeps the Xbox button: the launcher's in-game menu opens with View + Menu there." : "The Xbox button opens the launcher's in-game menu in " + g.Name + " (View + Menu works too).", Ui.Teal);
        if (homePage == "game") UpdateGamePage();
    }
    // hide a game from the library (Settings > Library brings it back); nothing is uninstalled
    void HideGame(Game g) {
        g.Hidden = true;
        SaveHidden();
        Settings.Save();
        Sound.Play(Sfx.Back);
        SetStep(g.Name + " is hidden. Settings > Library shows it again.", Ui.Muted);
        if (homePage == "game") { homePage = gameBack; ShowHomePage(); }
        ApplySort(true); ApplyFilter(); UpdateChips();
        var next = Games.FirstOrDefault(x => !x.Hidden && Shown(x));
        if (next != null) ShowGame(next);
        if (view == View.Home) MarkHome(null, sel);
        SetHints();
    }

    // ---------- small cards: collections and the keyboard ----------
    Grid Card(string name, double width, VerticalAlignment where, out Border card, out StackPanel body) {
        var layer = new Grid { Visibility = Visibility.Collapsed, Opacity = 0 };
        layer.Children.Add(new Rectangle { Fill = Ui.Brush("#C0050709") });
        card = new Border { Width = width, VerticalAlignment = where, HorizontalAlignment = HorizontalAlignment.Center, CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 0, where == VerticalAlignment.Bottom ? 40 : 0),
            Background = Ui.Brush("#11181D"), BorderBrush = Ui.Brush("#26333C"), BorderThickness = new Thickness(1), Padding = new Thickness(30, 26, 30, 24),
            RenderTransformOrigin = new Point(0.5, 0.5), Effect = new DropShadowEffect { BlurRadius = 60, ShadowDepth = 0, Opacity = 0.7, Color = Colors.Black } };
        var tg = new TransformGroup(); tg.Children.Add(new ScaleTransform()); tg.Children.Add(new TranslateTransform()); card.RenderTransform = tg;
        body = new StackPanel(); card.Child = body;
        layer.Children.Add(card);
        layer.LayoutTransform = N<Grid>("Main").LayoutTransform;
        N<Grid>("Clipped").Children.Add(layer);
        W.RegisterName(name + "Layer", layer); W.RegisterName(name + "Card", card);
        return layer;
    }

    // ---------- test versions (pre-releases) of one game: offered as updates while this is on ----------
    void ToggleTestVersions(Game g) {
        bool on = !Engine.TestVersions(g);
        Settings.Set("TestVersions:" + g.Id, on ? "on" : ""); Settings.Save();
        SetStep(on ? g.Name + ": test versions on. A newer test version shows as an update; they can have bugs." : g.Name + ": test versions off. The next full release shows as an update.", Ui.Teal);
        UpdateGamePage();
        CheckAll(true);   // look again now
    }
    // ---------- collections ----------
    Grid colLayer; StackPanel colRows; TextBlock colGame; Game colFor;
    readonly List<FrameworkElement> colStops = new List<FrameworkElement>();
    int colIdx;
    bool ColOpen { get { return colLayer != null && colLayer.Visibility == Visibility.Visible; } }
    void OpenCollections(Game g) {
        if (colLayer == null) {
            Border card; StackPanel body;
            colLayer = Card("Col", 520, VerticalAlignment.Center, out card, out body);
            body.Children.Add(new TextBlock { Text = "COLLECTIONS", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal) });
            colGame = new TextBlock { FontSize = 22, FontFamily = Ui.Semi, Margin = new Thickness(0, 2, 0, 4), TextWrapping = TextWrapping.Wrap };
            body.Children.Add(colGame);
            body.Children.Add(new TextBlock { Text = "Put this game in your own groups. Each collection is a filter in All games.", FontSize = 12, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            colRows = new StackPanel();
            body.Children.Add(new ScrollViewer { MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Content = colRows });
            ((Rectangle)colLayer.Children[0]).MouseLeftButtonUp += (s, e) => CloseCollections();
        }
        colFor = g;
        colGame.Text = g.Name;
        FillCollections();
        colIdx = 0; MarkCol();
        ShowCard("ColLayer", "ColCard");
    }
    void FillCollections() {
        colRows.Children.Clear(); colStops.Clear();
        foreach (var name in Profiles.CollectionNames()) {
            string n = name;
            var t = NewToggle(n, Profiles.InCollection(n).Count + (Profiles.InCollection(n).Count == 1 ? " game" : " games"));
            SetToggle(t, Profiles.InCollection(n).Contains(colFor.Id), true);
            t.Changed = () => { Profiles.SetInCollection(n, colFor.Id, t.On); pillIds = null; UpdateChips(); ApplyFilter(); };
            colRows.Children.Add(t.Root); colStops.Add(t.Root);
        }
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var add = MakeButton("+  New collection", 40); add.Margin = new Thickness(0);
        add.Click += (s, e) => OpenOsk("Name the collection", "", null, name => {
            name = (name ?? "").Trim();
            if (name.Length == 0) return;
            Profiles.SetInCollection(name, colFor.Id, true);
            pillIds = null; UpdateChips(); ApplyFilter();
            FillCollections(); colIdx = colStops.Count - 2; MarkCol();
        }, null);
        var done = MakeButton("Done", 40); done.Background = Ui.Brush(Ui.Teal); done.Foreground = Ui.Brush("#0A0E11");
        done.Click += (s, e) => CloseCollections();
        bar.Children.Add(add); bar.Children.Add(done);
        colRows.Children.Add(bar);
        colStops.Add(add); colStops.Add(done);
    }
    void MarkCol() {
        for (int i = 0; i < colStops.Count; i++) {
            var el = colStops[i]; bool on = i == colIdx && padUsed;
            if (el.Tag is Toggle) ((Grid)el).Background = Ui.Brush(on ? "#203039" : "#00000000"); else el.Effect = on ? SetGlow : null;
        }
    }
    void CloseCollections() {
        if (!ColOpen) return;
        HideCard("ColLayer", "ColCard");
        if (homePage == "game") UpdateGamePage();
    }
    void ColPad(int bit) {
        if (bit == Pad.B) { CloseCollections(); return; }
        if (bit == Pad.A) {
            var el = colStops[colIdx]; var t = el.Tag as Toggle;
            if (t != null) SetToggle(t, !t.On, false); else Press(el as Button);
            return;
        }
        int dx = bit == Pad.Left ? -1 : bit == Pad.Right ? 1 : 0, dy = bit == Pad.Up ? -1 : bit == Pad.Down ? 1 : 0;
        if (dx == 0 && dy == 0) return;
        int n = Nearest(colStops, colIdx, dx, dy, (Visual)N<Border>("ColCard"));
        Sound.Play(n >= 0 ? Sfx.Move : Sfx.Bump);
        if (n >= 0) { colIdx = n; MarkCol(); colStops[colIdx].BringIntoView(); }
    }

    // ---------- the on-screen keyboard ----------
    Grid oskLayer; TextBlock oskTitle, oskText;
    readonly List<Border> oskKeys = new List<Border>();
    int oskIdx; string oskValue, oskBefore;
    Action<string> oskLive, oskDone; Action oskCancel;
    bool OskOpen { get { return oskLayer != null && oskLayer.Visibility == Visibility.Visible; } }
    static readonly string[] OskRows = { "1234567890", "QWERTYUIOP", "ASDFGHJKL'", "ZXCVBNM-&." };
    void OpenOsk(string title, string start, Action<string> live, Action<string> done, Action cancel) {
        if (oskLayer == null) {
            Border card; StackPanel body;
            oskLayer = Card("Osk", 760, VerticalAlignment.Bottom, out card, out body);
            oskTitle = new TextBlock { FontSize = 13, FontFamily = Ui.Semi, Foreground = Ui.Brush(Ui.Teal) };
            body.Children.Add(oskTitle);
            var box = new Border { CornerRadius = new CornerRadius(10), Background = Ui.Brush("#0A0E11"), BorderBrush = Ui.Brush("#26333C"), BorderThickness = new Thickness(1), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 8, 0, 14) };
            oskText = new TextBlock { FontSize = 20, FontFamily = Ui.Semi, MinHeight = 28 };
            box.Child = oskText; body.Children.Add(box);
            var rows = new StackPanel();
            foreach (var r in OskRows) {
                var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                foreach (var ch in r) line.Children.Add(OskKey(ch.ToString(), 58));
                rows.Children.Add(line);
            }
            var last = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            last.Children.Add(OskKey("Space", 260)); last.Children.Add(OskKey("⌫ Delete", 150)); last.Children.Add(OskKey("Done", 150));
            rows.Children.Add(last);
            body.Children.Add(rows);
            var hints = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            foreach (var h in new[] { new[] { "A", "Type" }, new[] { "X", "Delete" }, new[] { "Y", "Space" }, new[] { "≡", "Done" }, new[] { "B", "Cancel" } }) hints.Children.Add(Hint(h[0], h[1]));
            body.Children.Add(hints);
            ((Rectangle)oskLayer.Children[0]).MouseLeftButtonUp += (s, e) => OskFinish(false);
            W.PreviewTextInput += (s, e) => {
                if (!OskOpen || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
                OskType(e.Text); e.Handled = true;
            };
        }
        oskTitle.Text = title;
        oskValue = start ?? ""; oskBefore = oskValue;
        oskLive = live; oskDone = done; oskCancel = cancel;
        oskIdx = 10; OskShow(); MarkOsk();   // on Q
        Panel.SetZIndex(oskLayer, 9500);   // over everything, the creator and the plaza too
        ShowCard("OskLayer", "OskCard");
    }
    Border OskKey(string label, double width) {
        var b = new Border { Width = width, Height = 50, Margin = new Thickness(3), CornerRadius = new CornerRadius(10), Background = Ui.Brush("#1C252C"), Cursor = Cursors.Hand, Tag = label,
            Child = new TextBlock { Text = label, FontSize = label.Length == 1 ? 18 : 14, FontFamily = Ui.Semi, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        int idx = oskKeys.Count;
        b.MouseLeftButtonUp += (s, e) => { oskIdx = idx; MarkOsk(); OskPress(label); };
        oskKeys.Add(b);
        return b;
    }
    void OskPress(string label) {
        if (label == "Space") OskType(" ");
        else if (label.EndsWith("Delete")) OskBack();
        else if (label == "Done") OskFinish(true);
        else OskType(label.ToLowerInvariant().Length == 1 && char.IsLetter(label[0]) && oskValue.Length > 0 ? label.ToLowerInvariant() : label);
    }
    void OskType(string s) { if (oskValue.Length >= 40) { Sound.Play(Sfx.Bump); return; } oskValue += s; Sound.Play(Sfx.Move); OskShow(); }
    void OskBack() { if (oskValue.Length == 0) { Sound.Play(Sfx.Bump); return; } oskValue = oskValue.Substring(0, oskValue.Length - 1); Sound.Play(Sfx.Back); OskShow(); }
    void OskShow() { oskText.Text = oskValue + "│"; if (oskLive != null) oskLive(oskValue); }
    void MarkOsk() {
        for (int i = 0; i < oskKeys.Count; i++) {
            bool on = i == oskIdx;
            oskKeys[i].Background = Ui.Brush(on ? Ui.Teal : "#1C252C");
            ((TextBlock)oskKeys[i].Child).Foreground = Ui.Brush(on ? (Theme.Current.Light ? "#ffffff" : "#0A0E11") : Ui.Text);
        }
    }
    void OskFinish(bool ok) {
        if (!OskOpen) return;
        HideCard("OskLayer", "OskCard");
        if (ok) { if (oskDone != null) oskDone(oskValue); }
        else { if (oskLive != null) oskLive(oskBefore); if (oskCancel != null) oskCancel(); }
    }
    void OskPad(int bit) {
        if (bit == Pad.B) { OskFinish(false); return; }
        if (bit == Pad.Start) { Sound.Play(Sfx.Select); OskFinish(true); return; }
        if (bit == Pad.X) { OskBack(); return; }
        if (bit == Pad.Y) { OskType(" "); return; }
        if (bit == Pad.A) { OskPress((string)oskKeys[oskIdx].Tag); return; }
        int dx = bit == Pad.Left ? -1 : bit == Pad.Right ? 1 : 0, dy = bit == Pad.Up ? -1 : bit == Pad.Down ? 1 : 0;
        if (dx == 0 && dy == 0) return;
        var stops = oskKeys.Cast<FrameworkElement>().ToList();
        int n = Nearest(stops, oskIdx, dx, dy, (Visual)N<Border>("OskCard"));
        Sound.Play(n >= 0 ? Sfx.Move : Sfx.Bump);
        if (n >= 0) { oskIdx = n; MarkOsk(); }
    }
    bool OskKeyDown(Key k) {
        if (k == Key.Escape) { OskFinish(false); return true; }
        if (k == Key.Enter) { OskFinish(true); return true; }
        if (k == Key.Back) { OskBack(); return true; }
        int bit = k == Key.Left ? Pad.Left : k == Key.Right ? Pad.Right : k == Key.Up ? Pad.Up : k == Key.Down ? Pad.Down : 0;
        if (bit != 0) { OskPad(bit); return true; }
        return false;
    }

    // ---------- search in All games ----------
    string SearchText() { return N<TextBox>("SearchBox").Text.Trim(); }
    void OpenSearch() {
        OpenOsk("Search games", N<TextBox>("SearchBox").Text, text => { N<TextBox>("SearchBox").Text = text; ApplyFilter(); UpdateHomeFilters(); },
            text => { var l = ShownGames(); if (l.Count > 0 && !l.Contains(sel)) ShowGame(l[0]); }, null);
    }
    void ClearSearch() { N<TextBox>("SearchBox").Text = ""; ApplyFilter(); UpdateHomeFilters(); Sound.Play(Sfx.Back); SetHints(); }
    void ShowSearch() {
        if (homeAllCount == null) return;
        string q = SearchText();
        if (q.Length > 0) homeAllCount.Text += "  •  “" + q + "”";
    }

    // ---------- letter jump (LT / RT in All games) ----------
    TextBlock jumpLetter;
    static char FirstLetter(Game g) {
        string n = Regex.Replace(g.Name, "^(The |A )", "", RegexOptions.IgnoreCase).Trim();
        char c = n.Length > 0 ? char.ToUpperInvariant(n[0]) : '#';
        return char.IsLetter(c) ? c : '#';
    }
    void JumpLetter(int dir) {
        var list = ShownGames(); if (list.Count == 0 || sel == null) return;
        int i = Math.Max(0, list.IndexOf(sel)); char cur = FirstLetter(list[i]);
        int j = i;
        if (dir > 0) { while (j < list.Count && FirstLetter(list[j]) == cur) j++; if (j >= list.Count) { Sound.Play(Sfx.Bump); return; } }
        else {
            // the start of this letter's run, or, when already there, the start of the one before
            while (j > 0 && FirstLetter(list[j - 1]) == cur) j--;
            if (j == i) { if (j == 0) { Sound.Play(Sfx.Bump); return; } char prev = FirstLetter(list[j - 1]); j--; while (j > 0 && FirstLetter(list[j - 1]) == prev) j--; }
        }
        Sound.Play(Sfx.Move);
        ShowGame(list[j]);
        if (jumpLetter == null) {
            jumpLetter = new TextBlock { FontSize = 120, FontFamily = Ui.Semi, Foreground = Ui.Brush(Ui.Text), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false, Opacity = 0, Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Opacity = 0.5 } };
            Grid.SetRowSpan(jumpLetter, 2);
            homeAllPage.Children.Add(jumpLetter);
        }
        jumpLetter.Text = FirstLetter(list[j]).ToString();
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0.85, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0.85, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(800))));
        jumpLetter.BeginAnimation(UIElement.OpacityProperty, a);
    }
}
}
