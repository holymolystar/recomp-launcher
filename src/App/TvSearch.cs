// TV & Movies: search, as on a streaming service. Its own page: a letter grid on the left, the results on the right,
// filling in as you type (a quarter of a second after the last letter).
//   Before typing   your last searches, and what's popular on the page you came from
//   Finding         titles without accents, capitals or "The" in front ("amelie" finds "Amélie"); the start of any word
//                   ("god" finds "The Godfather"); one letter wrong is forgiven once there are four; cast and directors
//                   too. Best first: the whole title, then its start, then a word's start, then anywhere, then near ones.
//   Fast            each profile's titles are made searchable once (simplified, split into words) in the background,
//                   so a provider with tens of thousands of films still answers at once
//   Filters         the profile's languages and kids setting (TvAllowed) apply
// Controller: arrows on the letters, A types, X deletes, Y a space, RB (or right at the edge) to the results, LB back,
// B leaves the results / closes search. A keyboard types straight in (Backspace deletes, Escape closes). The mouse
// clicks letters and results.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace RecompLauncher {

partial class Shell {
    Grid tvSearchPage; TextBlock tvSearchText, tvSearchHead; WrapPanel tvSearchGrid; StackPanel tvSearchRecent; ScrollViewer tvSearchScroll;
    readonly List<Border> tvKeys = new List<Border>(); readonly List<string> tvKeyVals = new List<string>();
    readonly List<Border> tvResTiles = new List<Border>(); List<TvItem> tvResItems = new List<TvItem>();
    readonly List<Border> tvRecentRows = new List<Border>(); readonly List<string> tvRecentVals = new List<string>();
    string tvSearchZone = "keys"; int tvKeyIdx, tvResIdx, tvRecentIdx; Border tvResMarked, tvKeyMarked;
    string tvQuery = ""; DispatcherTimer tvSearchTimer; int tvSearchGen;
    List<TvItem> tvSearchPopular = new List<TvItem>();
    const int TvKeyCols = 6;
    int TvResCols = 5;   // posters in a row: as many as fit (TvSearchFit)
    const double TvResW = 168, TvResH = 252;
    // one poster's room: its width, its 3-pixel edge on both sides, the gap after it
    const double TvResStep = TvResW + 6 + 18;
    // as many posters in a row as the page has room for; the panel exactly that wide, so the controller's rows are the screen's
    void TvSearchFit() {
        double room = tvSearchScroll.ActualWidth - 12; if (room <= 0) return;
        int cols = Math.Max(2, (int)Math.Floor(room / TvResStep));
        if (cols == TvResCols && Math.Abs(tvSearchGrid.Width - cols * TvResStep) < 0.5) return;
        TvResCols = cols; tvSearchGrid.Width = cols * TvResStep;
    }
    bool TvSearchOpen { get { return tvSearchPage != null && tvSearchPage.Visibility == Visibility.Visible; } }

    // ---------- the page ----------
    void BuildTvSearch() {
        tvSearchPage = new Grid { Visibility = Visibility.Collapsed, Background = TvTheme.Brush(TvBg) };
        Grid.SetColumnSpan(tvSearchPage, 2);
        tvSearchPage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(520) });
        tvSearchPage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // left: what you typed, the letters, your last searches
        var left = new StackPanel { Margin = new Thickness(64, 0, 24, 0) };
        var box = new Border { Background = TvTheme.Brush("#1C1C1E"), BorderBrush = TvTheme.Brush("#3A3A3D"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(18, 12, 18, 12), Margin = new Thickness(0, 0, 0, 22), Width = TvKeyCols * 66 - 8, HorizontalAlignment = HorizontalAlignment.Left };
        var boxRow = new StackPanel { Orientation = Orientation.Horizontal };
        boxRow.Children.Add(new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 18, Foreground = TvTheme.Brush("#99FFFFFF"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        tvSearchText = new TextBlock { FontSize = 22, FontFamily = Ui.Semi, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 320 };
        boxRow.Children.Add(tvSearchText); box.Child = boxRow;
        left.Children.Add(box);
        var keys = new WrapPanel { Width = TvKeyCols * 66, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var k in "abcdefghijklmnopqrstuvwxyz1234567890".Select(c => c.ToString()).Concat(new[] { "space", "del", "clear" })) {
            string val = k; bool wide = k.Length > 1;
            var t = new TextBlock { Text = k == "space" ? "Space" : k == "del" ? "" : k == "clear" ? "Clear" : k.ToUpperInvariant(), FontFamily = k == "del" ? Ui.Mdl : Ui.Semi, FontSize = wide ? 16 : 21, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { Width = wide ? 124 : 58, Height = 58, Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(8), Background = TvTheme.Brush("#26FFFFFF"), Child = t, Cursor = Cursors.Hand };
            int idx = tvKeys.Count;
            b.MouseLeftButtonUp += (s, e) => { tvSearchZone = "keys"; tvKeyIdx = idx; TvSearchMark(); TvSearchKeyPress(val); };
            tvKeys.Add(b); tvKeyVals.Add(val); keys.Children.Add(b);
        }
        left.Children.Add(keys);
        // typing on the phone: a QR code to scan (Start on the controller)
        var phone = new TextBlock { Text = "Type on your phone  (Start)", FontSize = 15, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#B3FFFFFF"), Margin = new Thickness(2, 8, 0, 0), Cursor = Cursors.Hand };
        phone.MouseLeftButtonUp += (s, e) => TvPairSearchStart();
        left.Children.Add(phone);
        left.Children.Add(new TextBlock { Text = "Your searches", FontSize = 15, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#99FFFFFF"), Margin = new Thickness(2, 22, 0, 8) });
        tvSearchRecent = new StackPanel();
        left.Children.Add(tvSearchRecent);
        // the letters always fit the screen's height: smaller on a short screen, never bigger
        tvSearchPage.Children.Add(new Viewbox { Child = left, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 104, 0, 24) });
        // right: the results
        var right = new Grid { Margin = new Thickness(8, 104, 40, 0) }; Grid.SetColumn(right, 1);
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        tvSearchHead = new TextBlock { FontSize = 24, FontFamily = TvFont, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(6, 0, 0, 14) };
        right.Children.Add(tvSearchHead);
        tvSearchGrid = new WrapPanel { Margin = new Thickness(6, 8, 6, 60), Width = TvResCols * TvResStep };
        tvSearchGrid.HorizontalAlignment = HorizontalAlignment.Left;
        tvSearchScroll = new ScrollViewer { Content = tvSearchGrid, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };   // the whole column wide: TvSearchFit measures it
        Grid.SetRow(tvSearchScroll, 1); right.Children.Add(tvSearchScroll);
        tvSearchScroll.SizeChanged += (s, e) => TvSearchFit();
        tvSearchPage.Children.Add(right);
        // under the details and the player, over the pages
        int at = tvLayer.Children.IndexOf(tvDetail); if (at < 0) at = tvLayer.Children.Count;
        tvLayer.Children.Insert(at, tvSearchPage);
        tvSearchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        tvSearchTimer.Tick += (s, e) => { tvSearchTimer.Stop(); TvSearchRun(); };
    }

    void TvSearchShow() {
        if (tvSearchPage == null) BuildTvSearch();
        TvPeekHide();
        // what's popular on the page you came from: its Top 10, else its first row
        var top = tvRowList.FirstOrDefault(r => r.Top10) ?? tvRowList.FirstOrDefault(r => r.Items.Any(x => x.Kind == "movie" || x.Kind == "series"));
        tvSearchPopular = top != null ? top.Items.Where(x => x.Kind == "movie" || x.Kind == "series" || x.Kind == "live").Take(20).ToList() : new List<TvItem>();
        tvQuery = tvSearch ?? ""; tvSearchZone = "keys"; tvKeyIdx = 0; tvResIdx = 0; tvRecentIdx = 0;
        tvSearchPage.Visibility = Visibility.Visible;
        tvSearchPage.BeginAnimation(UIElement.OpacityProperty, TvCalm ? null : Anim(1, 200, 0)); if (TvCalm) tvSearchPage.Opacity = 1;
        TvSearchFillRecent();
        TvSearchIndex(tvAcc, null);   // made searchable in the background (once per profile)
        TvSearchTextShow(); TvSearchRun();
        TvSearchMark(); SetHints();
    }
    void TvSearchClose() {
        if (!TvSearchOpen) return;
        tvSearchPage.Visibility = Visibility.Collapsed; tvSearchTimer.Stop(); tvSearchGen++;
        if (tvQuery.Trim().Length > 0) TvSearchRemember(tvQuery.Trim());
        tvSearch = tvQuery.Trim();
        if (tvPairMode == "search") { TvPairStop(); if (tvPairCard != null) tvPairCard.Visibility = Visibility.Collapsed; tvPairMode = "wizard"; }   // the phone stops typing here
        Sound.Play(Sfx.Back); MarkTv(); SetHints();
    }
    void TvSearchTextShow() { tvSearchText.Text = tvQuery.Length > 0 ? tvQuery + "▏" : "▏"; tvSearchText.Foreground = Brushes.White; }

    // ---------- typing ----------
    void TvSearchKeyPress(string val) {
        if (val == "del") { if (tvQuery.Length > 0) tvQuery = tvQuery.Substring(0, tvQuery.Length - 1); else { Sound.Play(Sfx.Bump); return; } }
        else if (val == "clear") tvQuery = "";
        else if (val == "space") { if (tvQuery.Length == 0 || tvQuery.EndsWith(" ")) { Sound.Play(Sfx.Bump); return; } tvQuery += " "; }
        else { if (tvQuery.Length >= 40) { Sound.Play(Sfx.Bump); return; } tvQuery += val; }
        Sound.Play(Sfx.Move);
        TvSearchTextShow();
        tvSearchTimer.Stop(); tvSearchTimer.Start();
    }
    // a real keyboard while search is open: letters and digits type, Backspace deletes, Escape closes
    bool TvSearchKey(Key k) {
        if (!TvSearchOpen || TvDetailOpen || TvPlaying) return false;   // a page or the player over it gets the keys
        if (k >= Key.A && k <= Key.Z) { TvSearchKeyPress(((char)('a' + (k - Key.A))).ToString()); return true; }
        if (k >= Key.D0 && k <= Key.D9) { TvSearchKeyPress(((char)('0' + (k - Key.D0))).ToString()); return true; }
        if (k >= Key.NumPad0 && k <= Key.NumPad9) { TvSearchKeyPress(((char)('0' + (k - Key.NumPad0))).ToString()); return true; }
        if (k == Key.Space && tvSearchZone == "keys") { TvSearchKeyPress("space"); return true; }
        if (k == Key.Back) { TvSearchKeyPress("del"); return true; }
        if (k == Key.Escape) { TvSearchClose(); return true; }
        return false;
    }

    // ---------- the controller ----------
    void TvSearchPad(int bit) {
        if (bit == Pad.Start) { TvPairSearchStart(); return; }   // type on the phone (TvExtras.cs)
        if (bit == Pad.X) { TvSearchKeyPress("del"); return; }
        if (bit == Pad.Y && tvSearchZone != "results") { TvSearchKeyPress("space"); return; }
        if (bit == Pad.RB) { if (tvResTiles.Count > 0) { tvSearchZone = "results"; Sound.Play(Sfx.Move); TvSearchMark(); } else Sound.Play(Sfx.Bump); return; }
        if (bit == Pad.LB) { tvSearchZone = "keys"; Sound.Play(Sfx.Move); TvSearchMark(); return; }
        if (tvSearchZone == "keys") {
            int row = TvKeyRow(tvKeyIdx), rows = TvKeyRow(tvKeys.Count - 1);
            if (bit == Pad.Left) { if (TvKeyCol(tvKeyIdx) > 0) tvKeyIdx--; else Sound.Play(Sfx.Bump); }
            else if (bit == Pad.Right) {
                bool edge = tvKeyIdx == tvKeys.Count - 1 || TvKeyRow(tvKeyIdx + 1) != row;
                if (!edge) tvKeyIdx++; else if (tvResTiles.Count > 0) { tvSearchZone = "results"; tvResIdx = Math.Min(tvResIdx, tvResTiles.Count - 1); } else Sound.Play(Sfx.Bump);
            }
            else if (bit == Pad.Up) { if (row > 0) tvKeyIdx = TvKeyAt(row - 1, TvKeyCol(tvKeyIdx)); else Sound.Play(Sfx.Bump); }
            else if (bit == Pad.Down) {
                if (row < rows) tvKeyIdx = TvKeyAt(row + 1, TvKeyCol(tvKeyIdx));
                else if (tvRecentRows.Count > 0) { tvSearchZone = "recent"; tvRecentIdx = 0; } else Sound.Play(Sfx.Bump);
            }
            else if (bit == Pad.A) { TvSearchKeyPress(tvKeyVals[tvKeyIdx]); return; }
            else if (bit == Pad.B) { TvSearchClose(); return; }
            Sound.Play(Sfx.Move); TvSearchMark(); return;
        }
        if (tvSearchZone == "recent") {
            if (bit == Pad.Up) { if (tvRecentIdx > 0) tvRecentIdx--; else { tvSearchZone = "keys"; tvKeyIdx = TvKeyAt(TvKeyRow(tvKeys.Count - 1), 0); } }
            else if (bit == Pad.Down) { if (tvRecentIdx < tvRecentRows.Count - 1) tvRecentIdx++; else { Sound.Play(Sfx.Bump); return; } }
            else if (bit == Pad.A) { tvQuery = tvRecentVals[tvRecentIdx]; Sound.Play(Sfx.Select); TvSearchTextShow(); TvSearchRun(); return; }
            else if (bit == Pad.B || bit == Pad.Right) { tvSearchZone = bit == Pad.Right && tvResTiles.Count > 0 ? "results" : "keys"; }
            else return;
            Sound.Play(Sfx.Move); TvSearchMark(); return;
        }
        // the results
        int c = tvResIdx % TvResCols;
        if (bit == Pad.Left) { if (c > 0) tvResIdx--; else tvSearchZone = "keys"; }
        else if (bit == Pad.Right) { if (c < TvResCols - 1 && tvResIdx < tvResTiles.Count - 1) tvResIdx++; else { Sound.Play(Sfx.Bump); return; } }
        else if (bit == Pad.Up) { if (tvResIdx >= TvResCols) tvResIdx -= TvResCols; else { Sound.Play(Sfx.Bump); return; } }
        else if (bit == Pad.Down) { if (tvResIdx + TvResCols < tvResTiles.Count) tvResIdx += TvResCols; else if (tvResIdx / TvResCols < (tvResTiles.Count - 1) / TvResCols) tvResIdx = tvResTiles.Count - 1; else { Sound.Play(Sfx.Bump); return; } }
        else if (bit == Pad.A) { TvSearchPick(tvResIdx); return; }
        else if (bit == Pad.Y) { var it = tvResItems[tvResIdx]; TvStore.ToggleFav(it); Sound.Play(Sfx.Select); SetStep(TvStore.IsFav(it) ? it.Title + " is in My list." : it.Title + " is out of My list.", Ui.Teal); return; }
        else if (bit == Pad.B) tvSearchZone = "keys";
        else return;
        Sound.Play(Sfx.Move); TvSearchMark();
    }
    int TvKeyRow(int i) { return i < 36 ? i / TvKeyCols : 6; }
    // the column on screen: the three wide keys at the bottom (Space, delete, Clear) are two columns each
    int TvKeyCol(int i) { return i < 36 ? i % TvKeyCols : (i - 36) * 2; }
    int TvKeyAt(int row, int col) { if (row >= 6) return 36 + Math.Min(2, col / 2); return row * TvKeyCols + Math.Min(TvKeyCols - 1, col); }

    void TvSearchPick(int i) {
        if (i < 0 || i >= tvResItems.Count) return;
        if (tvQuery.Trim().Length > 0) TvSearchRemember(tvQuery.Trim());
        var row = new TvRow { Title = "Search", Items = tvResItems };
        TvPick(tvResItems[i], row);   // a channel plays, anything else opens its page (over this one)
    }

    // ---------- what's marked ----------
    void TvSearchMark() {
        for (int i = 0; i < tvKeys.Count; i++) {
            bool on = tvSearchZone == "keys" && i == tvKeyIdx;
            tvKeys[i].Background = on ? Brushes.White : TvTheme.Brush("#26FFFFFF");
            ((TextBlock)tvKeys[i].Child).Foreground = on ? Brushes.Black : Brushes.White;
        }
        for (int i = 0; i < tvRecentRows.Count; i++) {
            bool on = tvSearchZone == "recent" && i == tvRecentIdx;
            tvRecentRows[i].Background = on ? Brushes.White : Brushes.Transparent;
            ((TextBlock)tvRecentRows[i].Child).Foreground = on ? Brushes.Black : TvTheme.Brush("#D9FFFFFF");
        }
        Border want = tvSearchZone == "results" && tvResIdx < tvResTiles.Count ? tvResTiles[tvResIdx] : null;
        if (want != tvResMarked) {
            if (tvResMarked != null) TvResMarkOne(tvResMarked, false);
            if (want != null) TvResMarkOne(want, true);
            tvResMarked = want;
        }
        if (want != null) want.BringIntoView(new Rect(-20, -40, want.ActualWidth + 40, want.ActualHeight + 80));
    }
    void TvResMarkOne(Border t, bool on) {
        var st = t.RenderTransform as ScaleTransform; double k = on ? 1.08 : 1;
        if (st != null) { if (TvCalm) { st.ScaleX = st.ScaleY = k; } else { st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(k, 160)); st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(k, 160)); } }
        t.BorderBrush = on ? Brushes.White : Brushes.Transparent;
        Panel.SetZIndex(t, on ? 10 : 0);
    }

    // ---------- your last searches (kept with the launcher's settings) ----------
    List<string> TvSearchRecentList() { return (Settings.Get("TvRecentSearches") ?? "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).Take(6).ToList(); }
    void TvSearchRemember(string q) {
        var l = TvSearchRecentList(); l.RemoveAll(x => string.Equals(x, q, StringComparison.OrdinalIgnoreCase)); l.Insert(0, q);
        Settings.Set("TvRecentSearches", string.Join("\n", l.Take(6))); Settings.Save();
    }
    void TvSearchFillRecent() {
        tvSearchRecent.Children.Clear(); tvRecentRows.Clear(); tvRecentVals.Clear();
        foreach (var q in TvSearchRecentList()) {
            string val = q;
            var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand, Width = TvKeyCols * 66 - 8, HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = q, FontSize = 17, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#D9FFFFFF"), TextTrimming = TextTrimming.CharacterEllipsis } };
            b.MouseLeftButtonUp += (s, e) => { tvQuery = val; TvSearchTextShow(); TvSearchRun(); };
            tvRecentRows.Add(b); tvRecentVals.Add(val); tvSearchRecent.Children.Add(b);
        }
        ((UIElement)((StackPanel)tvSearchRecent.Parent).Children[((StackPanel)tvSearchRecent.Parent).Children.IndexOf(tvSearchRecent) - 1]).Visibility = tvRecentRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- making a profile searchable ----------
    class TvFind { public TvItem It; public string T; public string[] W; public string P; public int Kind; }
    readonly Dictionary<string, List<TvFind>> tvFindIndex = new Dictionary<string, List<TvFind>>();
    readonly object tvFindGate = new object();
    static string TvFold(string s) {
        if (string.IsNullOrEmpty(s)) return "";
        var d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD); var sb = new StringBuilder(d.Length);
        foreach (char c in d) {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        var t = string.Join(" ", sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        foreach (var lead in new[] { "the ", "a ", "an ", "de ", "het ", "le ", "la ", "les " }) if (t.StartsWith(lead) && t.Length > lead.Length + 2) { t = t.Substring(lead.Length); break; }
        return t;
    }
    string TvFindKey(TvAccount a) { return a == null ? "free" : a.Id; }
    // made once per profile in the background; "then" runs on the window's thread when it's ready
    void TvSearchIndex(TvAccount a, Action then) {
        string key = TvFindKey(a);
        lock (tvFindGate) { if (tvFindIndex.ContainsKey(key)) { if (then != null) then(); return; } }
        ThreadPool.QueueUserWorkItem(_ => {
            var all = new List<TvItem>();
            try {
                if (a != null) {
                    if (!a.Loaded) TvSource.LoadAccount(a);
                    all.AddRange(a.Movies ?? new List<TvItem>()); all.AddRange(a.Series ?? new List<TvItem>()); all.AddRange(a.Live ?? new List<TvItem>());
                } else {
                    all.AddRange(TvFree.Films); all.AddRange(TvFree.Shows);
                    if (tvLibMovies != null) all.AddRange(tvLibMovies); if (tvLibSeries != null) all.AddRange(tvLibSeries);
                    all.AddRange(TvSources.Values.SelectMany(x => x));
                }
            } catch { }
            var list = new List<TvFind>(all.Count);
            foreach (var it in all) {
                if (it == null || string.IsNullOrEmpty(it.Title)) continue;
                var t = TvFold(it.Title);
                list.Add(new TvFind { It = it, T = t, W = t.Split(' '), P = TvFold((it.Cast ?? "") + " " + (it.Director ?? "")), Kind = it.Kind == "movie" || it.Kind == "series" ? 0 : it.Kind == "live" ? 2 : 1 });
            }
            lock (tvFindGate) tvFindIndex[key] = list;
            W.Dispatcher.BeginInvoke(new Action(() => {
                if (then != null) then();
                if (TvSearchOpen && TvFindKey(tvAcc) == key && tvQuery.Trim().Length > 0) TvSearchRun();
            }));
        });
    }
    // one letter wrong, missing or extra
    static bool TvNear1(string a, string b) {
        if (Math.Abs(a.Length - b.Length) > 1) return false;
        int i = 0, j = 0, d = 0;
        while (i < a.Length && j < b.Length) {
            if (a[i] == b[j]) { i++; j++; continue; }
            if (++d > 1) return false;
            if (a.Length > b.Length) i++; else if (b.Length > a.Length) j++; else { i++; j++; }
        }
        return d + (a.Length - i) + (b.Length - j) <= 1;
    }
    static int TvScore(TvFind f, string q, string[] qw) {
        if (f.T == q) return 1000;
        if (f.T.StartsWith(q)) return 800 - Math.Min(200, f.T.Length);
        // every word typed starts one of the title's words
        bool all = true;
        foreach (var w in qw) { bool hit = false; foreach (var tw in f.W) if (tw.StartsWith(w)) { hit = true; break; } if (!hit) { all = false; break; } }
        if (all) return 600 - Math.Min(200, f.T.Length);
        if (q.Length >= 3 && f.T.Contains(q)) return 400 - Math.Min(200, f.T.Length);
        if (q.Length >= 4) {
            // one letter off, in a word or the start of one (still typing)
            foreach (var w in qw) {
                if (w.Length < 4) continue;
                foreach (var tw in f.W) if (TvNear1(w, tw) || (tw.Length > w.Length && TvNear1(w, tw.Substring(0, w.Length)))) return 250 - Math.Min(200, f.T.Length);
            }
            if (f.P.Length > 0 && f.P.Contains(q)) return 150;
        }
        return 0;
    }

    // ---------- searching ----------
    void TvSearchRun() {
        if (!TvSearchOpen) return;
        string q = TvFold(tvQuery); int gen = ++tvSearchGen; var acc = tvAcc;
        if (q.Length == 0) {
            tvSearchHead.Text = tvSearchPopular.Count > 0 ? "Popular right now" : "Type to search films, series and channels";
            TvSearchShowResults(tvSearchPopular.Where(TvAllowed).ToList());
            return;
        }
        List<TvFind> idx; lock (tvFindGate) tvFindIndex.TryGetValue(TvFindKey(acc), out idx);
        if (idx == null) { tvSearchHead.Text = "Getting ready to search..."; return; }   // TvSearchIndex runs it again when it's done
        var qw = q.Split(' ');
        ThreadPool.QueueUserWorkItem(_ => {
            var hits = new List<KeyValuePair<int, TvFind>>();
            foreach (var f in idx) { int s = TvScore(f, q, qw); if (s > 0) hits.Add(new KeyValuePair<int, TvFind>(s - f.Kind * 30, f)); }
            var best = hits.OrderByDescending(h => h.Key).ThenBy(h => h.Value.T).Select(h => h.Value.It)
                .GroupBy(x => x.Key).Select(g => g.First()).Take(160).ToList();
            W.Dispatcher.BeginInvoke(new Action(() => {
                if (gen != tvSearchGen || !TvSearchOpen) return;
                var shown = best.Where(TvAllowed).Take(60).ToList();
                tvSearchHead.Text = shown.Count == 0 ? "Nothing found for \"" + tvQuery.Trim() + "\"" : "Results for \"" + tvQuery.Trim() + "\"";
                TvSearchShowResults(shown);
            }));
        });
    }
    void TvSearchShowResults(List<TvItem> items) {
        tvSearchGrid.Children.Clear(); tvResTiles.Clear(); tvResItems = items; tvResMarked = null;
        for (int i = 0; i < items.Count; i++) { var t = TvSearchTile(items[i], i); tvResTiles.Add(t); tvSearchGrid.Children.Add(t); }
        tvSearchScroll.ScrollToVerticalOffset(0);
        if (tvResIdx >= tvResTiles.Count) tvResIdx = Math.Max(0, tvResTiles.Count - 1);
        if (tvSearchZone == "results" && tvResTiles.Count == 0) tvSearchZone = "keys";
        TvSearchMark();
    }
    Border TvSearchTile(TvItem it, int i) {
        bool chan = it.Kind == "live";
        var g = new Grid { Width = TvResW, Height = TvResH, ClipToBounds = true };
        int hsh = Math.Abs((it.Title ?? "").GetHashCode()); string[] pal = { "#5b2a86", "#1b4f72", "#7b241c", "#145a32", "#784212", "#4a235a" };
        g.Children.Add(new Border { Background = chan ? TvTheme.Gradient("#2C2C30", "#161618") : TvTheme.Gradient(pal[hsh % pal.Length], "#0b1114") });
        var name = new TextBlock { Text = it.Title, FontSize = 15, FontFamily = Ui.Semi, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            VerticalAlignment = chan ? VerticalAlignment.Bottom : VerticalAlignment.Center, Margin = new Thickness(12), TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = TvResH - 24 };
        g.Children.Add(name);
        if (!string.IsNullOrEmpty(it.Image)) {
            var img = new Image { Stretch = chan ? Stretch.Uniform : Stretch.UniformToFill, Margin = chan ? new Thickness(28, 40, 28, 70) : new Thickness(0), Opacity = 0 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            TvLoadImage(img, it.Image, (int)(TvResW * 1.5), () => { if (!chan) name.Visibility = Visibility.Collapsed; });
            g.Children.Add(img);
        }
        if (chan) g.Children.Add(new Border { Background = TvTheme.Brush(TvAccent), CornerRadius = new CornerRadius(0, 0, 6, 6), Padding = new Thickness(8, 2, 8, 3), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = "LIVE", FontSize = 10.5, FontFamily = Ui.Semi, FontWeight = FontWeights.Bold, Foreground = Brushes.White } });
        g.Clip = new RectangleGeometry(new Rect(0, 0, TvResW, TvResH), 10, 10);
        var tile = new Border { Child = g, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 18, 18), BorderThickness = new Thickness(3), BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
        tile.MouseEnter += (s, e) => { if (MouseMode) { tvSearchZone = "results"; tvResIdx = i; TvSearchMark(); } };
        tile.MouseLeftButtonUp += (s, e) => { tvSearchZone = "results"; tvResIdx = i; TvSearchMark(); TvSearchPick(i); };
        tile.MouseRightButtonUp += (s, e) => { e.Handled = true; TvItemActions(it); };
        return tile;
    }
}
}
