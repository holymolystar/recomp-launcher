// TV & Movies: browsing by category. The categories down the left, what's in the picked one beside them:
//   TV Live  the IPTV provider's live categories in the server's order (the free profile: its own groups, NPO first),
//            as channel tiles, with what's on now for a provider's channel
//   World    iptv-org's worldwide list by category
//   Movies / Shows (from their Categories row)  the provider's categories, or the free films' genres, as posters
// Controller: up / down categories, right into the tiles, A watches or opens, Y My list, B back.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace RecompLauncher {

partial class Shell {
    Grid tvLive; StackPanel tvLiveCats; WrapPanel tvLiveGrid; ScrollViewer tvLiveCatScroll, tvLiveScroll; TextBlock tvLiveTitle, tvLiveStatus;
    List<KeyValuePair<string, List<TvItem>>> tvLiveGroups = new List<KeyValuePair<string, List<TvItem>>>();
    readonly List<Border> tvLiveCatRows = new List<Border>(), tvLiveTiles = new List<Border>();
    int tvLiveCat, tvLiveIdx, tvLiveGen; bool tvLiveInGrid, tvHoverMark; DispatcherTimer tvLiveNowTimer; string tvBrowseMode = "live";
    const double LiveTileW = 204, LiveTileH = 138, PosterW = 156, PosterH = 234;
    bool TvBrowseChannels { get { return tvBrowseMode == "live" || tvBrowseMode == "world"; } }
    double TvBrowseTileW { get { return TvBrowseChannels ? LiveTileW : PosterW; } }
    // the mouse over a tile picks it (only when the mouse really moves, not when the page moves under it)
    Point? tvHoverLast; bool tvMouseNav;
    void TvOnHover(FrameworkElement el, Action a) {
        el.MouseMove += (s, e) => {
            // the first sample only sets where the mouse is (things appearing under a still mouse aren't a move)
            var p = e.GetPosition(W); var last = tvHoverLast; tvHoverLast = p;
            if (last == null || Math.Abs(p.X - last.Value.X) + Math.Abs(p.Y - last.Value.Y) < 2) return;
            tvMouseNav = true; tvHoverMark = true; try { a(); } finally { tvHoverMark = false; }
        };
    }
    bool TvLiveOpen { get { return tvLive != null && tvLive.Visibility == Visibility.Visible; } }
    List<TvItem> TvLiveList { get { return tvLiveCat < tvLiveGroups.Count ? tvLiveGroups[tvLiveCat].Value : new List<TvItem>(); } }

    void BuildTvLive() {
        tvLive = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 100, 0, 0), Background = TvTheme.Brush(TvBg) };
        Grid.SetColumnSpan(tvLive, 2);
        tvLive.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        tvLive.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // the categories
        tvLiveCats = new StackPanel { Margin = new Thickness(0, 4, 0, 40) };
        tvLiveCatScroll = new ScrollViewer { Content = tvLiveCats, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Margin = new Thickness(36, 0, 10, 0) };
        tvLive.Children.Add(tvLiveCatScroll);
        // the channels
        var right = new Grid { Margin = new Thickness(14, 0, 0, 0) }; Grid.SetColumn(right, 1);
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        tvLiveTitle = new TextBlock { FontSize = 26, FontFamily = TvFont, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(8, 0, 0, 14) };
        right.Children.Add(tvLiveTitle);
        tvLiveGrid = new WrapPanel { Margin = new Thickness(0, 6, 30, 60) };
        tvLiveScroll = new ScrollViewer { Content = tvLiveGrid, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false };
        tvLiveScroll.ScrollChanged += (s, e) => { if (tvLiveScroll.VerticalOffset + tvLiveScroll.ViewportHeight > tvLiveScroll.ExtentHeight - 500) TvLiveBuildMore(36); };
        Grid.SetRow(tvLiveScroll, 1); right.Children.Add(tvLiveScroll);
        tvLiveStatus = new TextBlock { FontSize = 16, Foreground = TvTheme.Brush("#99FFFFFF"), Margin = new Thickness(8, 20, 40, 0), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetRow(tvLiveStatus, 1); right.Children.Add(tvLiveStatus);
        tvLive.Children.Add(right);
        tvLayer.Children.Insert(tvLayer.Children.IndexOf(tvBar), tvLive);
        tvLiveNowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        tvLiveNowTimer.Tick += (s, e) => { tvLiveNowTimer.Stop(); TvLiveNow(); };
    }

    // ---------- opening ----------
    List<string> tvLiveOrig = new List<string>(); string tvLiveScope; int tvLiveRealFrom, tvLiveRealTo;
    void TvOpenLive() { TvOpenBrowse("live", null); }
    void TvOpenBrowse(string mode, string startCat, int startIdx = -1) {
        if (tvLive == null) BuildTvLive();
        tvBrowseMode = mode;
        tvContent.Visibility = Visibility.Collapsed; tvLive.Visibility = Visibility.Visible;
        tvZone = "live"; tvLiveInGrid = false; int gen = ++tvLiveGen;
        tvLiveCats.Children.Clear(); tvLiveCatRows.Clear(); tvLiveGrid.Children.Clear(); tvLiveTiles.Clear(); tvLiveGroups.Clear();
        tvLiveTitle.Text = ""; tvLiveStatus.Text = mode == "world" ? "Loading channels from all over the world (about 11,000, this takes a moment)..." : TvBrowseChannels ? "Loading the channels..." : "Loading..."; tvLiveStatus.Visibility = Visibility.Visible;
        var acc = tvAcc;
        // made ahead (TvReady.cs) or before: straight away; otherwise worked out in the background
        Action<TvBrowseData> show = d => {
            var groups = d.Groups; string err = d.Err, scope = d.Scope; int realFrom = d.RealFrom, realTo = d.RealTo;
            if (gen != tvLiveGen || !TvLiveOpen) return;
            if (err != null || groups.Count <= 1 && (groups.Count == 0 || groups[0].Value.Count == 0)) { tvLiveStatus.Text = err != null ? "Couldn't load the channels: " + err : "No channels here."; return; }
            foreach (var g in groups) foreach (var x in g.Value) TvApplyMeta(x);
            // only the profile's languages (and nothing adult when that's hidden): categories left empty go
            groups = groups.Select(g => new KeyValuePair<string, List<TvItem>>(g.Key, TvFavsFirst(g.Value.Where(TvAllowed)))).ToList();   // My list's channels first (TvExtras.cs)
            tvLiveGroups = groups; tvLiveOrig = groups.Select(g => g.Key).ToList(); tvLiveScope = scope; tvLiveRealFrom = realFrom; tvLiveRealTo = realTo;
            for (int i = 0; i < groups.Count; i++) tvLiveCats.Children.Add(TvLiveCatRow(i));
            int start = startCat != null ? Math.Max(0, groups.FindIndex(g => g.Key == startCat)) : 0;
            TvLiveShowCat(start);
            if (startIdx >= 0 && tvLiveTiles.Count > 0) { tvLiveInGrid = true; tvLiveIdx = Math.Min(startIdx, TvLiveList.Count - 1); while (tvLiveIdx >= tvLiveTiles.Count) TvLiveBuildMore(36); TvLiveMark(); }
            else if (startCat != null && startIdx != -2) { tvLiveInGrid = tvLiveTiles.Count > 0; TvLiveMark(); }
        };
        var ready = TvBrowseFromCache(acc, mode);
        if (ready != null) { show(ready); SetHints(); return; }
        new Thread(() => {
            string ver = TvVer; var d = TvBrowseCompute(acc, mode);
            if (d.Err == null) TvBrowseToCache(acc, mode, d, ver);
            W.Dispatcher.BeginInvoke(new Action(() => show(d)));
        }) { IsBackground = true }.Start();
        SetHints();
    }
    // a page of TV Live (or of films or series by category): its categories in your order, names, hidden and sorting
    TvBrowseData TvBrowseCompute(TvAccount acc, string mode) {
        var groups = new List<KeyValuePair<string, List<TvItem>>>(); string err = null; string scope = TvMine.Scope(acc, mode); int realFrom = 0, realTo = 0;
        bool chans = mode == "live" || mode == "world";
        try {
                List<TvItem> all;
                if (mode == "people") { groups.AddRange(TvPersonGroups(acc)); all = groups.SelectMany(g => g.Value).ToList(); }
                else if (mode == "world") {
                    if (tvWorld == null) tvWorld = TvSource.WorldChannels();
                    foreach (var kv in tvWorld) groups.Add(new KeyValuePair<string, List<TvItem>>(kv.Key, kv.Value));
                    all = tvWorld.SelectMany(kv => kv.Value).ToList();
                } else if (mode == "movies" || mode == "series") {
                    bool films = mode == "movies";
                    if (acc != null) {
                        if (!acc.Loaded) TvSource.LoadAccount(acc);
                        all = (films ? acc.Movies : acc.Series) ?? new List<TvItem>();
                        var cats = films ? acc.MovieCats : acc.SeriesCats;
                        var order = (cats != null ? cats.Values : Enumerable.Empty<string>()).Distinct().ToList();
                        foreach (var g in all.Select(c => c.Group ?? "Other").Distinct()) if (!order.Contains(g)) order.Add(g);
                        var byCat = all.GroupBy(c => c.Group ?? "Other").ToDictionary(g => g.Key, g => g.ToList());
                        if (films && all.Count > 0) groups.Add(new KeyValuePair<string, List<TvItem>>("New", all.Take(60).ToList()));
                        foreach (var name in order) { List<TvItem> l; if (byCat.TryGetValue(name, out l) && l.Count > 0) groups.Add(new KeyValuePair<string, List<TvItem>>(name, l)); }
                    } else {
                        TvFree.Load(null);
                        all = films ? TvFree.Films : TvFree.Shows;
                        if (films) {
                            groups.Add(new KeyValuePair<string, List<TvItem>>("Popular", all.Take(30).ToList()));
                            foreach (var g in TvGenreRows) { var l = all.Where(f => (f.Genre ?? "").Contains(g)).ToList(); if (l.Count > 0) groups.Add(new KeyValuePair<string, List<TvItem>>(TvGenreTitle(g), l)); }
                        } else {
                            groups.Add(new KeyValuePair<string, List<TvItem>>("Classic TV", all.Where(s => s.Group == "Classic TV").ToList()));
                            groups.Add(new KeyValuePair<string, List<TvItem>>("Cartoons", all.Where(s => s.Group == "Cartoons").ToList()));
                            foreach (var g in new[] { "Comedy", "Western", "Mystery", "Sci-Fi" }) { var l = all.Where(s => s.Genre == g).ToList(); if (l.Count > 0) groups.Add(new KeyValuePair<string, List<TvItem>>(g == "Comedy" ? "Sitcoms" : g == "Western" ? "Westerns" : g == "Mystery" ? "Mystery" : "Sci-fi", l)); }
                        }
                    }
                    if (films) groups.AddRange(TvDecadeGroups(all));
                } else if (acc != null) {
                    if (!acc.Loaded) TvSource.LoadAccount(acc);
                    all = acc.Live ?? new List<TvItem>();
                    // the provider's categories, in the server's order
                    var order = (acc.LiveCats != null ? acc.LiveCats.Values : Enumerable.Empty<string>()).Distinct().ToList();
                    foreach (var g in all.Select(c => c.Group ?? "Other").Distinct()) if (!order.Contains(g)) order.Add(g);
                    var byCat = all.GroupBy(c => c.Group ?? "Other").ToDictionary(g => g.Key, g => g.ToList());
                    foreach (var name in order) { List<TvItem> l; if (byCat.TryGetValue(name, out l) && l.Count > 0) groups.Add(new KeyValuePair<string, List<TvItem>>(name, l)); }
                } else {
                    var free = TvSources;
                    all = free.Values.SelectMany(x => x).GroupBy(x => x.Url).Select(g => g.First()).ToList();
                    foreach (var kv in free) groups.Add(new KeyValuePair<string, List<TvItem>>(TvLiveName(kv.Key), kv.Value));
                }
                Func<TvItem, bool> kindOk = x => (mode == "movies" ? (x.Kind == "movie" || x.Kind == "file") : mode == "series" ? x.Kind == "series" : x.Kind == "live") && TvAllowed(x);
                Func<string, List<TvItem>, KeyValuePair<string, List<TvItem>>> kv2 = (k, l) => new KeyValuePair<string, List<TvItem>>(k, l);
                // your order, your names, what you hid, how it's sorted
                var natural = groups.Select(g => g.Key).Distinct().ToList(); string sort = TvMine.SortOf(scope);
                var byKey = groups.GroupBy(g => g.Key).ToDictionary(g => g.Key, g => g.First().Value);
                var mine = new List<KeyValuePair<string, List<TvItem>>>();
                foreach (var name in TvMine.Order(scope, natural)) {
                    if (TvMine.CatHidden(scope, name) && !tvShowHiddenCats) continue;
                    if (!TvAllowedName(name)) continue;
                    mine.Add(kv2(name, TvMine.Sorted(byKey[name].Where(x => !TvMine.HiddenItems.Contains(x.Key) && TvAllowed(x)), sort).ToList()));
                }
                // first: My list (in your order), Last watched, your tags
                var special = new List<KeyValuePair<string, List<TvItem>>>();
                var favs = TvMine.Items(TvStore.Favs, acc).Concat(all.Where(x => TvStore.IsFav(x) && !TvMine.Snaps.ContainsKey(x.Key))).Where(kindOk).ToList();
                if (favs.Count > 0) special.Add(kv2("My list", favs));
                if (mode == "live") { var last = TvMine.LastLiveOf(acc).Where(TvAllowed).ToList(); if (last.Count > 0) special.Add(kv2("Last watched", last)); }
                foreach (var tag in TvMine.TagNames) { var l = TvMine.Tagged(tag, acc).Where(kindOk).ToList(); if (l.Count > 0) special.Add(kv2("★ " + tag, l)); }
                // last: everything, and the hidden channels
                var tail = new List<KeyValuePair<string, List<TvItem>>>();
                if (mode == "people") { groups = mine; realFrom = 0; realTo = 0; goto done; }
                var shown = all.Where(x => !TvMine.HiddenItems.Contains(x.Key) && TvAllowed(x));
                tail.Add(kv2(mode == "movies" ? "All films" : mode == "series" ? "All series" : "All channels", (mode == "movies" || mode == "series" ? TvMine.Sorted(shown.OrderBy(x => x.Title), sort == "default" ? "az" : sort) : shown).ToList()));
                if (chans) { var hid = TvMine.Items(TvMine.HiddenItems, acc).Where(x => x.Kind == "live").ToList(); if (hid.Count > 0) tail.Add(kv2("Hidden channels", hid)); }
                mine.RemoveAll(g => g.Value.Count == 0);
                groups = special.Concat(mine).Concat(tail).ToList();
                realFrom = special.Count; realTo = special.Count + mine.Count;
                done:;
        } catch (Exception e) { err = e.Message; }
        return new TvBrowseData { Groups = groups, Err = err, Scope = scope, RealFrom = realFrom, RealTo = realTo };
    }
    static string TvLiveName(string k) {
        switch (k) { case "NPO": return "NPO"; case "Regional": return "Regional"; case "Music": return "Music"; case "Local TV": return "Local TV"; case "Parliament": return "Parliament";
            case "België": return "Belgium"; case "Nieuws (wereld)": return "News"; case "Muziek": return "Music (world)"; case "Kids": return "Kids"; }
        return k;
    }
    // the TV Guide follows the category that was open in TV Live
    List<TvItem> tvGuideFrom; string tvGuideFromName; TvAccount tvGuideFromAcc;
    void TvCloseLive() {
        if (tvLive == null || !TvLiveOpen) return;
        if (tvBrowseMode == "live" && tvLiveCat < tvLiveGroups.Count && tvLiveGroups[tvLiveCat].Value.Count > 0) { tvGuideFrom = tvLiveGroups[tvLiveCat].Value; tvGuideFromName = TvCatLabel(tvLiveCat); tvGuideFromAcc = tvAcc; }
        tvLive.Visibility = Visibility.Collapsed; tvContent.Visibility = Visibility.Visible; tvLiveGen++;
    }
    Border TvLiveCatRow(int i) {
        var kv = tvLiveGroups[i];
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock { Text = TvCatLabel(i), FontSize = 16.5, FontFamily = Ui.Semi, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var n = new TextBlock { Text = kv.Value.Count.ToString(), FontSize = 13.5, FontFamily = Ui.Semi, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        Grid.SetColumn(n, 1); g.Children.Add(n);
        var b = new Border { Child = g, Height = 46, Padding = new Thickness(18, 0, 16, 0), CornerRadius = new CornerRadius(23), Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        int idx = i;
        b.MouseLeftButtonUp += (s, e) => { tvLiveInGrid = false; tvZone = "live"; Sound.Play(Sfx.Select); TvLiveShowCat(idx); };
        b.MouseEnter += (s, e) => { if (idx != tvLiveCat) b.Background = TvTheme.Brush("#1FFFFFFF"); };
        b.MouseLeave += (s, e) => TvLiveMark();
        b.MouseRightButtonUp += (s, e) => { e.Handled = true; TvCatActionsFor(idx); };
        tvLiveCatRows.Add(b);
        return b;
    }
    // a category's name as shown: your name for it, and "(hidden)" while hidden ones are shown
    string TvCatLabel(int i) {
        string k = tvLiveGroups[i].Key;
        if (i < tvLiveRealFrom || i >= tvLiveRealTo) return k;
        return TvMine.NameOf(tvLiveScope, k) + (TvMine.CatHidden(tvLiveScope, k) ? "  (hidden)" : "");
    }
    void TvCatActionsFor(int i) {
        if (i < tvLiveRealFrom || i >= tvLiveRealTo) { Sound.Play(Sfx.Bump); SetStep("TV: My list, Last watched, your tags and All are always here; the other categories can be moved, renamed and hidden.", Ui.Teal); return; }
        TvCatActions(tvLiveScope, tvLiveOrig[i], tvLiveOrig.Skip(tvLiveRealFrom).Take(tvLiveRealTo - tvLiveRealFrom).ToList());
    }
    void TvLiveShowCat(int i) {
        if (i < 0 || i >= tvLiveGroups.Count) return;
        tvLiveCat = i; tvLiveIdx = 0; tvImgGen++;
        tvLiveGrid.Children.Clear(); tvLiveTiles.Clear();
        var kv = tvLiveGroups[i];
        tvLiveTitle.Text = TvCatLabel(i);
        tvLiveStatus.Visibility = kv.Value.Count == 0 ? Visibility.Visible : Visibility.Collapsed; tvLiveStatus.Text = "No channels in this category.";
        tvLiveScroll.ScrollToVerticalOffset(0);
        TvLiveBuildMore(48);
        TvLiveMark();
    }
    void TvLiveBuildMore(int n) {
        var list = TvLiveList; int upto = Math.Min(list.Count, tvLiveTiles.Count + n);
        for (int i = tvLiveTiles.Count; i < upto; i++) { var t = TvBrowseChannels ? TvLiveTile(list[i], i) : TvPosterTile(list[i], i); tvLiveTiles.Add(t); tvLiveGrid.Children.Add(t); }
    }
    Border TvLiveTile(TvItem ch, int i) {
        var g = new Grid { Width = LiveTileW, Height = LiveTileH };
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var back = new Border { Background = TvTheme.Gradient("#2A2A2E", "#151517") }; Grid.SetRowSpan(back, 2); g.Children.Add(back);
        var logoName = new TextBlock { Text = ch.Title, FontSize = 17, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#B3FFFFFF"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14), MaxHeight = 70 };
        g.Children.Add(logoName); Grid.SetRowSpan(logoName, 1);
        if (!string.IsNullOrEmpty(ch.Image)) {
            var img = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(40, 18, 40, 6), Opacity = 0 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            TvLoadImage(img, ch.Image, 240, () => logoName.Visibility = Visibility.Collapsed);
            g.Children.Add(img);
        }
        var text = new StackPanel { Margin = new Thickness(14, 0, 14, 12) }; Grid.SetRow(text, 1);
        text.Children.Add(new TextBlock { Text = ch.Title, FontSize = 14.5, FontFamily = Ui.Semi, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
        var now = new TextBlock { FontSize = 12.5, Foreground = TvTheme.Brush("#99FFFFFF"), TextTrimming = TextTrimming.CharacterEllipsis, Text = ch.Group ?? "", Margin = new Thickness(0, 2, 0, 0) };
        text.Children.Add(now);
        g.Children.Add(text);
        if (TvStore.IsFav(ch)) g.Children.Add(new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 14, Foreground = TvTheme.Brush("#F5B50A"), Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top });
        g.Clip = new RectangleGeometry(new Rect(0, 0, LiveTileW, LiveTileH), 10, 10);
        var b = new Border { Child = g, CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(3), BorderBrush = Brushes.Transparent, Margin = new Thickness(6, 6, 10, 10), Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1), Tag = now };
        int idx = i;
        TvOnHover(b, () => { if (tvLiveIdx != idx || !tvLiveInGrid) { tvLiveInGrid = true; tvZone = "live"; tvLiveIdx = idx; TvLiveMark(); } });
        b.MouseLeftButtonUp += (s, e) => { tvLiveIdx = idx; TvLiveWatch(); };
        b.MouseRightButtonUp += (s, e) => { e.Handled = true; tvLiveIdx = idx; TvItemActions(ch); };
        return b;
    }
    void TvLiveMark() {
        for (int i = 0; i < tvLiveCatRows.Count; i++) {
            bool cur = i == tvLiveCat, on = cur && !tvLiveInGrid && tvZone == "live";
            var r = tvLiveCatRows[i];
            r.Background = on ? Brushes.White : cur ? TvTheme.Brush("#3A3A3C") : Brushes.Transparent;
            foreach (var t in ((Grid)r.Child).Children.OfType<TextBlock>()) t.Foreground = on ? Brushes.Black : cur ? Brushes.White : TvTheme.Brush("#A6FFFFFF");
            if (on && !tvHoverMark) r.BringIntoView();
        }
        for (int i = 0; i < tvLiveTiles.Count; i++) {
            bool on = tvLiveInGrid && tvZone == "live" && i == tvLiveIdx;
            var t = tvLiveTiles[i]; t.BorderBrush = on ? Brushes.White : Brushes.Transparent;
            var st = (ScaleTransform)t.RenderTransform; double k = on ? 1.05 : 1;
            if (Math.Abs(st.ScaleX - k) > 0.001) { st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(k, 140)); st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(k, 140)); }
            Panel.SetZIndex(t, on ? 10 : 0);
            if (on && !tvHoverMark) t.BringIntoView(new Rect(0, -30, t.ActualWidth, t.ActualHeight + 60));
        }
        if (tvLiveInGrid && TvBrowseChannels) { tvLiveNowTimer.Stop(); tvLiveNowTimer.Start(); }
        SetHints();
    }
    // what's on now on the picked channel (a provider's short guide)
    void TvLiveNow() {
        if (!tvLiveInGrid || tvLiveIdx >= tvLiveTiles.Count) return;
        var ch = TvLiveList[tvLiveIdx]; var line = tvLiveTiles[tvLiveIdx].Tag as TextBlock; if (line == null || ch.Account == null) return;
        new Thread(() => { var nn = TvSource.NowNext(ch); W.Dispatcher.BeginInvoke(new Action(() => { if (nn != null && nn.Length > 0) { line.Text = "Now  " + nn[0]; line.Foreground = TvTheme.Brush("#E6FFFFFF"); } })); }) { IsBackground = true }.Start();
    }
    int TvLiveCols { get { double w = tvLiveGrid.ActualWidth; return Math.Max(1, (int)(w / (TvBrowseTileW + 16))); } }
    void TvLivePad(int bit) {
        if (!tvLiveInGrid) {
            if (bit == Pad.Up) { if (tvLiveCat > 0) { Sound.Play(Sfx.Move); TvLiveShowCat(tvLiveCat - 1); } else TvToRail(); }
            else if (bit == Pad.Down) { if (tvLiveCat < tvLiveGroups.Count - 1) { Sound.Play(Sfx.Move); TvLiveShowCat(tvLiveCat + 1); } else Sound.Play(Sfx.Bump); }
            else if (bit == Pad.Right || bit == Pad.A) { if (tvLiveTiles.Count > 0) { tvLiveInGrid = true; tvLiveIdx = 0; Sound.Play(Sfx.Move); TvLiveMark(); } else Sound.Play(Sfx.Bump); }
            else if (bit == Pad.B) { if (TvBrowseChannels) TvToRail(); else { Sound.Play(Sfx.Back); TvShowPage(tvPage); } }
            else if (bit == Pad.X) TvCatActionsFor(tvLiveCat);
            else Sound.Play(Sfx.Bump);
            return;
        }
        int cols = TvLiveCols, n = TvLiveList.Count;
        if (bit == Pad.Left) { if (tvLiveIdx % cols == 0) { tvLiveInGrid = false; Sound.Play(Sfx.Move); TvLiveMark(); } else { tvLiveIdx--; Sound.Play(Sfx.Move); TvLiveMark(); } }
        else if (bit == Pad.Right) { if (tvLiveIdx + 1 < n && (tvLiveIdx + 1) % cols != 0) { tvLiveIdx++; Sound.Play(Sfx.Move); TvLiveMark(); } else Sound.Play(Sfx.Bump); }
        else if (bit == Pad.Down) { if (tvLiveIdx + cols < n) { tvLiveIdx += cols; if (tvLiveIdx >= tvLiveTiles.Count - cols * 2) TvLiveBuildMore(36); Sound.Play(Sfx.Move); TvLiveMark(); } else Sound.Play(Sfx.Bump); }
        else if (bit == Pad.Up) { if (tvLiveIdx - cols >= 0) { tvLiveIdx -= cols; Sound.Play(Sfx.Move); TvLiveMark(); } else TvToRail(); }
        else if (bit == Pad.A) TvLiveWatch();
        else if (bit == Pad.X) { if (tvLiveIdx < TvLiveList.Count) TvItemActions(TvLiveList[tvLiveIdx]); }
        else if (bit == Pad.Y) { var ch = TvLiveList[tvLiveIdx]; TvStore.ToggleFav(ch); Sound.Play(Sfx.Select); SetStep(TvStore.IsFav(ch) ? ch.Title + " is in My list." : ch.Title + " is out of My list.", Ui.Teal); }
        else if (bit == Pad.B) { tvLiveInGrid = false; Sound.Play(Sfx.Back); TvLiveMark(); }
    }
    void TvLiveWatch() {
        var list = TvLiveList; if (tvLiveIdx >= list.Count) return;
        if (TvBrowseChannels) TvPlay(list[tvLiveIdx], list); else TvPick(list[tvLiveIdx], null);
    }
    // "✓ Watched" on a tile
    static Border TvWatchedChip() {
        return new Border { Background = TvTheme.Brush("#D9101012"), CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2, 8, 3), Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = "✓  Watched", FontSize = 11.5, FontFamily = Ui.Semi, Foreground = Brushes.White } };
    }
    // a film's or series' poster in a category
    Border TvPosterTile(TvItem it, int i) {
        var g = new Grid { Width = PosterW, Height = PosterH };
        int hsh = Math.Abs((it.Title ?? "").GetHashCode()); string[] pal = { "#5b2a86", "#1b4f72", "#7b241c", "#145a32", "#784212", "#4a235a", "#1a5276", "#6e2c00" };
        g.Children.Add(new Border { Background = TvTheme.Gradient(pal[hsh % pal.Length], "#0b1114") });
        g.Children.Add(new TextBlock { Text = it.Title, FontSize = 15, FontFamily = Ui.Semi, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12) });
        var nameText = (TextBlock)g.Children[1];
        if (!string.IsNullOrEmpty(it.Image)) { var img = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 }; RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality); TvLoadImage(img, it.Image, 320, null); g.Children.Add(img); }
        TvArtLater(it, g, nameText, 320);
        if (TvStore.IsFav(it)) g.Children.Add(new TextBlock { Text = "\uE735", FontFamily = Ui.Mdl, FontSize = 14, Foreground = TvTheme.Brush("#F5B50A"), Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top });
        if (TvMine.IsWatched(it)) g.Children.Add(TvWatchedChip());
        g.Clip = new RectangleGeometry(new Rect(0, 0, PosterW, PosterH), 10, 10);
        var col = new StackPanel(); col.Children.Add(g);
        col.Children.Add(new TextBlock { Text = it.Title, FontSize = 13.5, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#D9FFFFFF"), Margin = new Thickness(4, 8, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = PosterW });
        var b = new Border { Child = col, CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(3), BorderBrush = Brushes.Transparent, Margin = new Thickness(4, 6, 8, 12), Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.4), RenderTransform = new ScaleTransform(1, 1) };
        int idx = i;
        TvOnHover(b, () => { if (tvLiveIdx != idx || !tvLiveInGrid) { tvLiveInGrid = true; tvZone = "live"; tvLiveIdx = idx; TvLiveMark(); } });
        b.MouseLeftButtonUp += (s, e) => { tvLiveIdx = idx; TvLiveWatch(); };
        b.MouseRightButtonUp += (s, e) => { e.Handled = true; tvLiveIdx = idx; TvItemActions(it); };
        return b;
    }
    // the Categories row on Movies and Shows: one tile per category
    TvRow TvCategoryRow(string mode, IEnumerable<string> names, string title = "Categories") {
        var items = names.Where(n => !string.IsNullOrEmpty(n)).Distinct().Select(n => new TvItem { Kind = "category", Title = n, Group = mode }).ToList();
        return TvRowOf(title, items, true);
    }
    void TvLiveHints(List<string[]> spec) {
        if (!tvLiveInGrid) { spec.Add(new[] { "↕", "Category" }); spec.Add(new[] { "A", TvBrowseChannels ? "Channels" : "Open" }); if (tvLiveCat >= tvLiveRealFrom && tvLiveCat < tvLiveRealTo) spec.Add(new[] { "X", "Edit category" }); spec.Add(new[] { "B", TvBrowseChannels ? "Top" : "Back" }); return; }
        spec.Add(new[] { "A", TvBrowseChannels ? "Watch" : "Open" }); spec.Add(new[] { "X", "More" }); spec.Add(new[] { "Y", "My list" }); spec.Add(new[] { "B", "Categories" });
    }
}
}
