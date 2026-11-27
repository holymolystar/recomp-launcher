// TV & Movies: a profile's settings, full screen. On the left the profile's picture (pick it to change it), its name,
// and the sections; on the right the settings of the picked section, as big cards:
//   Profile        name, picture, the tab it opens on, removing it
//   Login          an IPTV profile's login or playlist link, and signing in again
//   Languages      which languages show (with their flags), and whether things without a language do
//   TV Guide       the guide's link, time shift, how often it's fetched, the preview
//   Player         audio and subtitle languages, VLC, decoding, OpenSubtitles
//   Safety         PIN lock, hiding adult content, where recordings and downloads go
// Controller: up / down the sections, right or A into the cards, A picks, left or B back, B again closes.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace RecompLauncher {

partial class Shell {
    Grid tvProfPage; StackPanel tvPpNav, tvPpCards; WrapPanel tvPpGrid; Grid tvPpAvatarBox; TextBlock tvPpName, tvPpKind, tvPpTitle, tvPpSub; ScrollViewer tvPpScroll;
    readonly List<Border> tvPpNavRows = new List<Border>(), tvPpCardRows = new List<Border>(); readonly List<Action> tvPpActs = new List<Action>();
    List<string[]> tvPpSections = new List<string[]>(); int tvPpSec, tvPpIdx, tvPpCols = 1; bool tvPpInCards; bool tvPpRemoveAsk;
    bool TvProfPageOpen { get { return tvProfPage != null && tvProfPage.Visibility == Visibility.Visible; } }

    void TvOpenProfilePage() {
        if (tvProfPage == null) TvBuildProfilePage();
        tvPpSections = tvAccFree
            ? new List<string[]> { new[] { "profile", "", "Profile" }, new[] { "guide", "", "TV Guide" }, new[] { "player", "", "Player" }, new[] { "safety", "", "Safety and storage" } }
            : new List<string[]> { new[] { "profile", "", "Profile" }, new[] { "login", "", "Login" }, new[] { "langs", "", "Languages" }, new[] { "guide", "", "TV Guide" }, new[] { "player", "", "Player" }, new[] { "safety", "", "Safety and storage" } };
        tvPpSec = 0; tvPpIdx = 0; tvPpInCards = false; tvPpRemoveAsk = false;
        tvProfPage.Visibility = Visibility.Visible; tvProfPage.BeginAnimation(UIElement.OpacityProperty, Anim(1, 220, 0));
        Sound.Play(Sfx.Open);
        // a provider's languages need its lists
        if (!tvAccFree && tvAccOrig != null && !tvAccOrig.Loaded) { var acc = tvAccOrig; ThreadPool.QueueUserWorkItem(_ => { try { TvSource.LoadAccount(acc); } catch { } W.Dispatcher.BeginInvoke(new Action(() => { if (TvProfPageOpen) TvFillProfilePage(); })); }); }
        TvFillProfilePage();
    }
    void TvCloseProfilePage() {
        if (!TvProfPageOpen) return;
        tvProfPage.Visibility = Visibility.Collapsed; Sound.Play(Sfx.Back);
        tvLangFirst = false;
        if (TvProfilesOpen) FillTvProfiles(); else { TvApplyProfile(); if (TvOpen) TvShowPage(tvPage); }
        SetHints();
    }
    void TvBuildProfilePage() {
        tvProfPage = new Grid { Background = TvTheme.Brush("#F7000000"), Visibility = Visibility.Collapsed };
        Grid.SetColumnSpan(tvProfPage, 2); Panel.SetZIndex(tvProfPage, 880);
        var glow = new RadialGradientBrush(Color.FromArgb(0x40, 0xFF, 0x3D, 0x1F), Color.FromArgb(0, 0xFF, 0x3D, 0x1F)) { Center = new Point(0.12, 0.1), GradientOrigin = new Point(0.12, 0.1), RadiusX = 0.6, RadiusY = 0.8 };
        tvProfPage.Children.Add(new Border { Background = glow, IsHitTestVisible = false });
        var cols = new Grid { Margin = new Thickness(56, 40, 56, 30) };
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // left: back, the picture, the name, the sections
        var left = new StackPanel();
        var back = new Border { Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 16, Foreground = TvTheme.Brush("#B3FFFFFF"), VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = "Back", FontSize = 16, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#B3FFFFFF"), Margin = new Thickness(10, 0, 0, 0) } } },
            Cursor = Cursors.Hand, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(4, 6, 10, 6) };
        back.MouseLeftButtonUp += (s, e) => TvCloseProfilePage();
        left.Children.Add(back);
        tvPpAvatarBox = new Grid { Width = 150, Height = 150, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 26, 0, 0), Cursor = Cursors.Hand, ToolTip = "Change the picture" };
        tvPpAvatarBox.MouseLeftButtonUp += (s, e) => { tvPpSec = 0; tvPpInCards = true; tvPpIdx = 0; TvFillProfilePage(); };
        left.Children.Add(tvPpAvatarBox);
        tvPpName = new TextBlock { FontSize = 30, FontFamily = TvFont, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 16, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        tvPpKind = new TextBlock { FontSize = 14.5, Foreground = TvTheme.Brush("#8CFFFFFF"), Margin = new Thickness(0, 4, 0, 26) };
        left.Children.Add(tvPpName); left.Children.Add(tvPpKind);
        tvPpNav = new StackPanel(); left.Children.Add(tvPpNav);
        cols.Children.Add(left);
        // right: the section's cards
        var right = new Grid { Margin = new Thickness(40, 0, 0, 0) }; Grid.SetColumn(right, 1);
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var head = new StackPanel { Margin = new Thickness(0, 44, 0, 18) };
        tvPpTitle = new TextBlock { FontSize = 34, FontFamily = TvFont, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
        tvPpSub = new TextBlock { FontSize = 15, Foreground = TvTheme.Brush("#99FFFFFF"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
        head.Children.Add(tvPpTitle); head.Children.Add(tvPpSub); right.Children.Add(head);
        var body = new StackPanel { Margin = new Thickness(0, 0, 20, 40) };
        tvPpCards = new StackPanel { MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
        tvPpGrid = new WrapPanel { MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
        body.Children.Add(tvPpGrid); body.Children.Add(tvPpCards);
        tvPpScroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false }; Grid.SetRow(tvPpScroll, 1);
        right.Children.Add(tvPpScroll);
        cols.Children.Add(right);
        tvProfPage.Children.Add(cols);
        tvLayer.Children.Add(tvProfPage);
    }

    // ---------- filling it ----------
    TvAccount TvPpAcc { get { return tvAccFree ? null : (TvStore.Accounts.FirstOrDefault(x => tvAccEdit != null && x.Id == tvAccEdit.Id) ?? tvAccOrig); } }
    string TvPpAvatar { get { var a = TvPpAcc; return tvAccFree ? TvStore.FreeAvatar : a != null ? a.Avatar ?? "a1" : "a1"; } }
    string TvPpName { get { var a = TvPpAcc; return tvAccFree ? TvStore.FreeName : a != null ? a.Name : tvAccEdit.Name; } }
    void TvFillProfilePage() {
        if (!TvProfPageOpen) return;
        // left
        tvPpAvatarBox.Children.Clear();
        tvPpAvatarBox.Children.Add(TvAvatar(TvPpAvatar, 150));
        tvPpAvatarBox.Children.Add(new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Child = new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 16, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        tvPpName.Text = TvPpName;
        tvPpKind.Text = tvAccFree ? "Free films, series and TV" : TvPpAcc != null && TvPpAcc.Type == "m3u" ? "IPTV  •  playlist link" : "IPTV  •  login";
        tvPpNav.Children.Clear(); tvPpNavRows.Clear();
        for (int i = 0; i < tvPpSections.Count; i++) {
            var sec = tvPpSections[i]; int idx = i;
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = sec[1], FontFamily = Ui.Mdl, FontSize = 17, Width = 28, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = sec[2], FontSize = 17, FontFamily = Ui.Semi, Margin = new Thickness(10, 0, 0, 1), VerticalAlignment = VerticalAlignment.Center });
            var b = new Border { Child = sp, Height = 50, Padding = new Thickness(18, 0, 18, 0), CornerRadius = new CornerRadius(25), Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            TvOnHover(b, () => { if (tvPpSec != idx || tvPpInCards) { tvPpSec = idx; tvPpInCards = false; tvPpIdx = 0; tvPpRemoveAsk = false; TvFillProfilePage(); } });
            b.MouseLeftButtonUp += (s, e) => { tvPpSec = idx; tvPpInCards = false; tvPpIdx = 0; TvFillProfilePage(); };
            tvPpNavRows.Add(b); tvPpNav.Children.Add(b);
        }
        // right
        tvPpCards.Children.Clear(); tvPpGrid.Children.Clear(); tvPpCardRows.Clear(); tvPpActs.Clear(); tvPpCols = 1;
        var rows = new List<ListRow>(); string id = tvPpSections[tvPpSec][0];
        switch (id) {
            case "profile": TvPpProfileRows(rows); break;
            case "login": TvPpLoginRows(rows); break;
            case "langs": TvPpLangRows(rows); break;
            case "guide": tvPpTitle.Text = "TV Guide"; tvPpSub.Text = "Where the listings come from, and how the guide behaves."; rows.AddRange(TvGuideRows()); break;
            case "player": tvPpTitle.Text = "Player"; tvPpSub.Text = "Audio and subtitles to start with, and how things play. Rows marked \"every profile\" count for all profiles."; rows.AddRange(TvPlayerRows()); break;
            case "safety": tvPpTitle.Text = "Safety and storage"; tvPpSub.Text = "A PIN for this profile, keeping adult content out, and where recordings and downloads are saved."; rows.AddRange(TvSafeRows()); break;
        }
        foreach (var r in rows) TvPpCard(r);
        if (tvPpIdx >= tvPpCardRows.Count) tvPpIdx = Math.Max(0, tvPpCardRows.Count - 1);
        TvMarkProfilePage();
    }
    void TvPpProfileRows(List<ListRow> rows) {
        tvPpTitle.Text = "Profile"; tvPpSub.Text = "How this profile looks on \"Who's watching?\", and the tab it opens on.";
        // the pictures first, as a grid of their own
        rows.Add(new ListRow { Title = "__avatars__" });
        rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Name", Text = TvPpName, Do = () => OpenOsk("A name for this profile", TvPpName, null, v => {
            v = (v ?? "").Trim(); if (v.Length == 0) return;
            if (tvAccFree) TvStore.FreeName = v; else { var a = TvPpAcc; if (a != null) a.Name = v; tvAccEdit.Name = v; }
            TvStore.Save(); TvFillProfilePage(); }, null) });
        rows.Add(TvStartRow()); rows.Add(TvThemeRow());
        if (!tvAccFree) rows.Add(new ListRow { Glyph = "", C1 = "#c0453c", C2 = "#c0453c", Title = tvPpRemoveAsk ? "Pick again to remove " + TvPpName : "Remove this profile",
            Text = tvPpRemoveAsk ? "Its login and its settings go from this PC (nothing changes at your provider)" : "Its login is removed from this PC",
            Do = () => {
                if (!tvPpRemoveAsk) { tvPpRemoveAsk = true; TvFillProfilePage(); return; }
                string rid = tvAccEdit.Id; TvStore.Accounts.RemoveAll(x => x.Id == rid); TvStore.Save(); if (tvAcc != null && tvAcc.Id == rid) tvAcc = null;
                tvPpRemoveAsk = false; TvCloseProfilePage();
            } });
    }
    void TvPpLoginRows(List<ListRow> rows) {
        var a = tvAccEdit;
        tvPpTitle.Text = "Login"; tvPpSub.Text = "From your provider. Kept on this PC only, encrypted with your Windows account." + (a.Status != null ? "\n\n" + a.Status : "");
        Func<string, bool, string> show = (v, secret) => string.IsNullOrEmpty(v) ? "(empty: pick to type it)" : secret ? new string('•', Math.Min(12, v.Length)) : v;
        rows.Add(new ListRow { Glyph = "", C1 = "#2f8fe0", C2 = "#2f8fe0", Title = "Kind: " + (a.Type == "m3u" ? "Playlist link (M3U)" : "Login (Xtream)"), Text = "Pick to switch", Do = () => { a.Type = a.Type == "m3u" ? "xtream" : "m3u"; TvFillProfilePage(); } });
        if (a.Type == "m3u") rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Playlist link", Text = show(a.Url, true), Do = () => OpenOsk("The playlist (M3U) link", a.Url, null, v => { a.Url = (v ?? "").Trim(); TvFillProfilePage(); }, null, false, true) });
        else {
            rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Server address", Text = show(a.Server, false), Do = () => OpenOsk("Server address (for example http://example-tv.com:8080)", a.Server, null, v => { a.Server = TvCleanServer(v); TvFillProfilePage(); }, null, false, true) });
            rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Username", Text = show(a.User, false), Do = () => OpenOsk("Username", a.User, null, v => { a.User = (v ?? "").Trim(); TvFillProfilePage(); }, null, false, true) });
            rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Password", Text = show(a.Pass, true), Do = () => OpenOsk("Password", a.Pass, null, v => { a.Pass = v ?? ""; TvFillProfilePage(); }, null, true) });
        }
        rows.Add(new ListRow { Glyph = "", C1 = "#3fb8a0", C2 = "#2f8fe0", Title = "Save and sign in", Text = "Checks the login with your provider", Do = TvSaveAccount });
    }
    // Languages, one press away: the globe in TV & Movies' bar opens this profile's languages (and the first time an
    // IPTV profile opens with more than one language, it asks: "Which languages do you watch?")
    bool tvLangFirst;
    void TvOpenLangs(bool first) {
        if (tvAcc == null) { Toast("Languages", "The free profile shows every language. A language filter is for an IPTV profile: pick one in Who's watching?"); return; }
        tvLangFirst = first;
        TvAccountForm(tvAcc);
        int i = tvPpSections.FindIndex(s => s[0] == "langs"); if (i < 0) return;
        tvPpSec = i; tvPpInCards = true; tvPpIdx = 0; TvFillProfilePage();
    }
    void TvAskLangsOnce(TvAccount a) {
        if (a == null || TvMine.LangAsked.Contains(a.Id)) return;
        List<string> l; if (TvMine.Langs.TryGetValue(a.Id, out l) && l.Count > 0) return;
        TvMine.LangAsked.Add(a.Id); TvMine.Save();
        if (!a.Loaded || TvTidy.Langs(a).Count < 2) return;
        TvOpenLangs(true);
    }
    void TvPpLangRows(List<ListRow> rows) {
        var a = TvPpAcc; tvPpTitle.Text = tvLangFirst ? "Which languages do you watch?" : "Languages";
        if (a == null) return;
        List<string> on; if (!TvMine.Langs.TryGetValue(a.Id, out on)) TvMine.Langs[a.Id] = on = new List<string>();
        if (tvLangFirst && a.Loaded) { tvPpSub.Text = "Turn on the languages you want: only their channels, films and series show. The rest stays out of sight. Change it any time with the globe at the top of TV & Movies."; }
        else tvPpSub.Text = !a.Loaded ? "Looking at what " + a.Name + " has..." : on.Count == 0 ? "Everything shows now. Pick the languages you want; the rest stays out of sight (nothing changes at your provider)." : "Showing: " + string.Join(", ", on.Select(TvTidy.NameOfLang)) + ".";
        if (!a.Loaded) return;
        foreach (var kv in TvTidy.Langs(a)) {
            string code = kv.Key; bool sel = on.Contains(code);
            rows.Add(new ListRow { Pic = TvFlag(code), Title = TvTidy.NameOfLang(code), Text = kv.Value + " channels, films and series", Right = sel ? "On" : "Off", Bold = sel,
                Do = () => { if (!on.Remove(code)) on.Add(code); TvMine.Save(); TvFillProfilePage(); } });
        }
        int none = (a.Live ?? new List<TvItem>()).Concat(a.Movies ?? new List<TvItem>()).Concat(a.Series ?? new List<TvItem>()).Count(x => x.Lang == null);
        bool strict = TvMine.LangStrict.Contains(a.Id);
        rows.Add(new ListRow { Glyph = "", C1 = "#c28a1e", C2 = "#e0b33c", Title = "Without a language", Text = none + " channels, films and series have no language in their name or category", Right = strict ? "Hidden" : "Shown",
            Do = () => { if (!TvMine.LangStrict.Remove(a.Id)) TvMine.LangStrict.Add(a.Id); TvMine.Save(); TvFillProfilePage(); } });
        rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "All languages", Text = "Turns the language filter off", Do = () => { on.Clear(); TvMine.LangStrict.Remove(a.Id); TvMine.Save(); TvFillProfilePage(); } });
    }

    // ---------- the cards ----------
    void TvPpCard(ListRow r) {
        if (r.Title == "__avatars__") { TvPpAvatarCards(); return; }
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        FrameworkElement icon;
        if (r.Pic != null) icon = new Border { Width = 48, Height = 34, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = new Image { Source = r.Pic, Stretch = Stretch.UniformToFill }, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = 0.5 } };
        else icon = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Background = TvTheme.Gradient(r.C1 ?? "#33424d", r.C2 ?? "#33424d"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = r.Glyph ?? "", FontFamily = Ui.Mdl, FontSize = 18, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        g.Children.Add(icon);
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(t, 1);
        t.Children.Add(new TextBlock { Text = r.Title, FontSize = 17.5, FontFamily = Ui.Semi, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrEmpty(r.Text)) t.Children.Add(new TextBlock { Text = r.Text, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), Opacity = 0.65 });
        g.Children.Add(t);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) }; Grid.SetColumn(right, 2);
        if (!string.IsNullOrEmpty(r.Right)) {
            bool on = r.Right == "On" || r.Right == "Hidden";
            right.Children.Add(new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 3, 12, 4), Background = TvTheme.Brush(on ? TvAccent : "#33FFFFFF"), Child = new TextBlock { Text = r.Right, FontSize = 13, FontFamily = Ui.Semi, Foreground = Brushes.White } });
        } else right.Children.Add(new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 14, Opacity = 0.5 });
        g.Children.Add(right);
        var card = new Border { Child = g, Padding = new Thickness(18, 14, 20, 14), CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 0, 10), Background = TvTheme.Brush("#161618"), BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(2), Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
        int idx = tvPpCardRows.Count; var act = r.Do;
        TvOnHover(card, () => { if (tvPpIdx != idx || !tvPpInCards) { tvPpIdx = idx; tvPpInCards = true; TvMarkProfilePage(); } });
        card.MouseLeftButtonUp += (s, e) => { tvPpIdx = idx; tvPpInCards = true; Sound.Play(Sfx.Select); if (act != null) act(); };
        tvPpCardRows.Add(card); tvPpActs.Add(act); tvPpCards.Children.Add(card);
    }
    // the profile pictures as a grid (and one from this PC)
    void TvPpAvatarCards() {
        var cur = TvPpAvatar; tvPpCols = 7;
        for (int i = 0; i <= TvAvatars.Length; i++) {
            bool file = i == TvAvatars.Length; string aid = file ? null : "a" + i;
            var face = new Grid { Width = 92, Height = 92 };
            if (file) {
                face.Children.Add(new Ellipse { Fill = TvTheme.Brush("#1FFFFFFF"), Stroke = TvTheme.Brush("#40FFFFFF"), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 } });
                face.Children.Add(new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 26, Foreground = TvTheme.Brush("#D9FFFFFF"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ToolTip = "A picture from this PC" });
            } else face.Children.Add(TvAvatar(aid, 92));
            var ring = new Border { Child = face, CornerRadius = new CornerRadius(52), Padding = new Thickness(4), BorderThickness = new Thickness(3), BorderBrush = aid != null && aid == cur ? TvTheme.Brush(TvAccent) : Brushes.Transparent, Margin = new Thickness(0, 0, 14, 14), Cursor = Cursors.Hand,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
            int idx = tvPpCardRows.Count;
            Action act = file ? (Action)(() => {
                var d = new Microsoft.Win32.OpenFileDialog { Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif", Title = "A profile picture" };
                if (d.ShowDialog() != true) return;
                try {
                    string dir = System.IO.Path.Combine(Paths.Root, "tvavatars"); System.IO.Directory.CreateDirectory(dir);
                    string to = System.IO.Path.Combine(dir, (tvAccEdit.Id ?? "p") + "-" + DateTime.Now.Ticks + System.IO.Path.GetExtension(d.FileName).ToLowerInvariant());
                    System.IO.File.Copy(d.FileName, to, true); TvPpSetAvatar(to);
                } catch (Exception e) { SetStep("TV: that picture couldn't be used: " + e.Message, Ui.Red); }
            }) : () => TvPpSetAvatar(aid);
            TvOnHover(ring, () => { if (tvPpIdx != idx || !tvPpInCards) { tvPpIdx = idx; tvPpInCards = true; TvMarkProfilePage(); } });
            ring.MouseLeftButtonUp += (s, e) => { tvPpIdx = idx; Sound.Play(Sfx.Select); act(); };
            tvPpCardRows.Add(ring); tvPpActs.Add(act); tvPpGrid.Children.Add(ring);
        }
    }
    void TvPpSetAvatar(string av) {
        if (tvAccFree) TvStore.FreeAvatar = av; else { var a = TvPpAcc; if (a != null) a.Avatar = av; tvAccEdit.Avatar = av; }
        TvStore.Save(); TvFillProfilePage();
    }
    void TvMarkProfilePage() {
        for (int i = 0; i < tvPpNavRows.Count; i++) {
            bool cur = i == tvPpSec, on = cur && !tvPpInCards;
            tvPpNavRows[i].Background = on ? Brushes.White : cur ? TvTheme.Brush("#3A3A3C") : Brushes.Transparent;
            foreach (var t in ((StackPanel)tvPpNavRows[i].Child).Children.OfType<TextBlock>()) t.Foreground = on ? Brushes.Black : cur ? Brushes.White : TvTheme.Brush("#A6FFFFFF");
        }
        for (int i = 0; i < tvPpCardRows.Count; i++) {
            bool on = tvPpInCards && i == tvPpIdx; var c = tvPpCardRows[i]; bool avatar = c.Child is Grid && ((Grid)c.Child).Width == 92;
            if (avatar) { if (on) c.BorderBrush = Brushes.White; else c.BorderBrush = ((Grid)c.Child).Children.Count > 0 && i < TvAvatars.Length && "a" + i == TvPpAvatar ? TvTheme.Brush(TvAccent) : Brushes.Transparent; }
            else { c.BorderBrush = on ? Brushes.White : Brushes.Transparent; c.Background = TvTheme.Brush(on ? "#232326" : "#161618"); }
            var st = (ScaleTransform)c.RenderTransform; double k = on ? (avatar ? 1.08 : 1.015) : 1;
            if (Math.Abs(st.ScaleX - k) > 0.001) { st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(k, 130)); st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(k, 130)); }
            if (on && !tvHoverMark) c.BringIntoView();
        }
        SetHints();
    }
    // the avatars come first in the profile section: index 0..12 are pictures (after the Name card, which is drawn below them)
    void TvProfilePagePad(int bit) {
        if (!tvPpInCards) {
            if (bit == Pad.Up || bit == Pad.Down) { int j = tvPpSec + (bit == Pad.Up ? -1 : 1); if (j >= 0 && j < tvPpSections.Count) { tvPpSec = j; tvPpIdx = 0; tvPpRemoveAsk = false; Sound.Play(Sfx.Move); TvFillProfilePage(); } else Sound.Play(Sfx.Bump); }
            else if (bit == Pad.Right || bit == Pad.A) { if (tvPpCardRows.Count > 0) { tvPpInCards = true; tvPpIdx = 0; Sound.Play(Sfx.Move); TvMarkProfilePage(); } }
            else if (bit == Pad.B) TvCloseProfilePage();
            return;
        }
        int n = tvPpCardRows.Count, grid = tvPpGrid.Children.Count;
        bool inGrid = tvPpIdx < grid && grid > 0;
        if (bit == Pad.B) { tvPpInCards = false; Sound.Play(Sfx.Back); TvMarkProfilePage(); return; }
        if (bit == Pad.A) { if (tvPpIdx < tvPpActs.Count && tvPpActs[tvPpIdx] != null) { Sound.Play(Sfx.Select); tvPpActs[tvPpIdx](); } return; }
        int to = tvPpIdx;
        if (inGrid) {
            int col = tvPpIdx % tvPpCols;
            if (bit == Pad.Left) { if (col == 0) { tvPpInCards = false; Sound.Play(Sfx.Move); TvMarkProfilePage(); return; } to--; }
            else if (bit == Pad.Right) to = Math.Min(grid - 1, to + 1);
            else if (bit == Pad.Up) to = to - tvPpCols >= 0 ? to - tvPpCols : to;
            else if (bit == Pad.Down) to = to + tvPpCols < grid ? to + tvPpCols : grid;
        } else {
            if (bit == Pad.Left) { tvPpInCards = false; Sound.Play(Sfx.Move); TvMarkProfilePage(); return; }
            if (bit == Pad.Up) to = to - 1 >= grid ? to - 1 : Math.Max(0, grid - 1);
            else if (bit == Pad.Down) to = to + 1;
        }
        if (to < 0 || to >= n) { Sound.Play(Sfx.Bump); return; }
        if (to != tvPpIdx) { tvPpIdx = to; Sound.Play(Sfx.Move); TvMarkProfilePage(); } else Sound.Play(Sfx.Bump);
    }

    // ---------- flags, drawn (Windows shows flag emoji as letters) ----------
    static readonly Dictionary<string, ImageSource> tvFlags = new Dictionary<string, ImageSource>();
    static ImageSource TvFlag(string code) {
        ImageSource got; if (tvFlags.TryGetValue(code ?? "", out got)) return got;
        var dg = new DrawingGroup(); const double W2 = 60, H2 = 40;
        using (var dc = dg.Open()) {
            Action<string, double, double, double, double> box = (c, x, y, w, h) => dc.DrawRectangle(new SolidColorBrush((Color)ColorConverter.ConvertFromString(c)), null, new Rect(x, y, w, h));
            Action<string[]> rows3 = cs => { for (int i = 0; i < cs.Length; i++) box(cs[i], 0, H2 * i / cs.Length, W2, H2 / cs.Length + 0.5); };
            Action<string[]> cols3 = cs => { for (int i = 0; i < cs.Length; i++) box(cs[i], W2 * i / cs.Length, 0, W2 / cs.Length + 0.5, H2); };
            switch (code) {
                case "NL": rows3(new[] { "#AE1C28", "#FFFFFF", "#21468B" }); break;
                case "PL": rows3(new[] { "#FFFFFF", "#DC143C" }); break;
                case "DE": rows3(new[] { "#000000", "#DD0000", "#FFCE00" }); break;
                case "RU": rows3(new[] { "#FFFFFF", "#0039A6", "#D52B1E" }); break;
                case "BG": rows3(new[] { "#FFFFFF", "#00966E", "#D62612" }); break;
                case "HU": rows3(new[] { "#CE2939", "#FFFFFF", "#477050" }); break;
                case "UA": rows3(new[] { "#0057B7", "#FFD700" }); break;
                case "FR": cols3(new[] { "#0055A4", "#FFFFFF", "#EF4135" }); break;
                case "IT": cols3(new[] { "#009246", "#FFFFFF", "#CE2B37" }); break;
                case "BE": cols3(new[] { "#000000", "#FDDA24", "#EF3340" }); break;
                case "RO": cols3(new[] { "#002B7F", "#FCD116", "#CE1126" }); break;
                case "IE": cols3(new[] { "#169B62", "#FFFFFF", "#FF883E" }); break;
                case "ES": box("#AA151B", 0, 0, W2, H2); box("#F1BF00", 0, H2 / 4, W2, H2 / 2); break;
                case "PT": box("#FF0000", 0, 0, W2, H2); box("#006600", 0, 0, W2 * 0.4, H2); dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x00)), null, new Point(W2 * 0.4, H2 / 2), 7, 7); break;
                case "TR": box("#E30A17", 0, 0, W2, H2); dc.DrawEllipse(Brushes.White, null, new Point(24, 20), 10, 10); dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xE3, 0x0A, 0x17)), null, new Point(27, 20), 8, 8); dc.DrawEllipse(Brushes.White, null, new Point(37, 20), 3, 3); break;
                case "GR": for (int i = 0; i < 9; i++) box(i % 2 == 0 ? "#0D5EAF" : "#FFFFFF", 0, H2 * i / 9, W2, H2 / 9 + 0.5); box("#0D5EAF", 0, 0, 22, H2 * 5 / 9); box("#FFFFFF", 9, 0, 4, H2 * 5 / 9); box("#FFFFFF", 0, 9, 22, 4); break;
                case "SE": box("#006AA7", 0, 0, W2, H2); box("#FECC00", 18, 0, 7, H2); box("#FECC00", 0, 16, W2, 7); break;
                case "AS": box("#FFFFFF", 0, 0, W2, H2); dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xBC, 0x00, 0x2D)), null, new Point(30, 20), 11, 11); break;
                case "AR": box("#007A3D", 0, 0, W2, H2); box("#FFFFFF", 0, H2 / 3, W2, H2 / 3); box("#000000", 0, H2 * 2 / 3, W2, H2 / 3 + 0.5); box("#CE1126", 0, 0, 16, H2); break;
                case "IN": rows3(new[] { "#FF9933", "#FFFFFF", "#138808" }); dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(0, 0, 0x80)), 1.5), new Point(30, 20), 5, 5); break;
                case "EN": {
                    // the Union flag
                    box("#012169", 0, 0, W2, H2);
                    var white = new Pen(Brushes.White, 8); var red = new Pen(new SolidColorBrush(Color.FromRgb(0xC8, 0x10, 0x2E)), 3);
                    dc.DrawLine(white, new Point(0, 0), new Point(W2, H2)); dc.DrawLine(white, new Point(W2, 0), new Point(0, H2));
                    dc.DrawLine(red, new Point(0, 0), new Point(W2, H2)); dc.DrawLine(red, new Point(W2, 0), new Point(0, H2));
                    box("#FFFFFF", 0, 14, W2, 12); box("#FFFFFF", 24, 0, 12, H2); box("#C8102E", 0, 16.5, W2, 7); box("#C8102E", 26.5, 0, 7, H2);
                    break;
                }
                default: {
                    box("#2C2C30", 0, 0, W2, H2);
                    var ft = new FormattedText(code ?? "?", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Ui.Semi, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 18, Brushes.White, 1);
                    dc.DrawText(ft, new Point((W2 - ft.Width) / 2, (H2 - ft.Height) / 2)); break;
                }
            }
        }
        dg.ClipGeometry = new RectangleGeometry(new Rect(0, 0, W2, H2)); dg.Freeze();
        var img = new DrawingImage(dg); img.Freeze(); tvFlags[code ?? ""] = img; return img;
    }
}
}
