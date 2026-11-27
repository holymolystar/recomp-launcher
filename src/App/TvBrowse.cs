// TV & Movies: the rows, the way a streaming service lays them out. For the free profile: the free films and series
// (TvFree.cs) and the free channels; for an IPTV profile: the provider's films, series and channels, sorted the same
// way. Top 10 rows get the big numbers, a film's page gets "More like this".
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
    static readonly string[] TvGenreRows = { "Comedy", "Horror", "Sci-Fi", "Film noir", "Crime", "Mystery", "Thriller", "Western", "Adventure", "Romance", "Musical", "War", "Drama", "Silent" };
    static string TvGenreTitle(string g) {
        switch (g) { case "Sci-Fi": return "Sci-fi classics"; case "Film noir": return "Film noir"; case "Silent": return "Silent films"; case "Drama": return "Dramas"; case "Musical": return "Musicals"; case "War": return "War films";
            case "Western": return "Westerns"; case "Horror": return "Horror"; case "Comedy": return "Comedies"; case "Thriller": return "Thrillers"; case "Mystery": return "Mysteries"; case "Romance": return "Romance"; }
        return g + " films";
    }
    TvItem tvFeatured;
    // a film to show big, a different one each day
    static TvItem TvPickFeatured(IEnumerable<TvItem> pool) {
        // the ones with a wide background first (they look best big)
        var l = pool.Where(x => !string.IsNullOrEmpty(x.Backdrop) && !string.IsNullOrEmpty(x.Image)).Take(30).ToList();
        if (l.Count == 0) l = pool.Where(x => !string.IsNullOrEmpty(x.Image)).Take(30).ToList(); if (l.Count == 0) return null;
        return l[new Random(DateTime.Today.DayOfYear * 7 + DateTime.Today.Year).Next(l.Count)];
    }
    static List<TvItem> TvDayShuffle(IEnumerable<TvItem> l, int seed) { var r = new Random(DateTime.Today.DayOfYear * 31 + seed); return l.OrderBy(x => r.Next()).ToList(); }

    // ---------- the free profile ----------
    List<TvRow> TvFreeHomeRows() {
        TvFree.Load(null);
        var rows = new List<TvRow>();
        var films = TvFree.Films; var shows = TvFree.Shows;
        TvFeature(TvPickFeatured(films));
        TvMyRows(rows, null);
        if (films.Count >= 10) rows.Add(new TvRow { Title = "Top 10 films today", Items = films.Take(10).ToList(), Top10 = true });
        var rec = TvRecommended(films, null); if (rec.Count > 0) rows.Add(TvRowOf("Recommended for you", rec, false));
        rows.AddRange(TvBecauseRows(films, x => true, 2));
        var trend = TvTrending(); if (trend.Count > 0) rows.Add(TvRowOf("Trending this week", trend, false));
        var decs = TvDecadeGroups(films); if (decs.Count > 0) rows.Add(TvCategoryRow("movies", decs.Select(d => d.Key), "Explore by decade"));
        if (films.Count > 10) rows.Add(TvRowOf("More popular films", TvDayShuffle(films.Skip(10).Take(60), 1).Take(30), false));
        var classic = shows.Where(s => s.Group == "Classic TV").ToList(); if (classic.Count > 0) rows.Add(TvRowOf("Classic TV series", classic, false));
        var free = TvSources; List<TvItem> nl;
        if (free.TryGetValue("NPO", out nl)) rows.Add(TvRowOf("Live on NPO", nl, true));
        int n = 0;
        foreach (var g in TvGenreRows) {
            var l = films.Where(f => (f.Genre ?? "").Contains(g)).ToList();
            if (l.Count < 5) continue;
            rows.Add(TvRowOf(TvGenreTitle(g), TvDayShuffle(l, n + 3), false));
            if (++n == 3) { var cart = shows.Where(s => s.Group == "Cartoons").ToList(); if (cart.Count > 0) rows.Add(TvRowOf("Cartoons", cart, false)); }
        }
        if (free.TryGetValue("Regional", out nl)) rows.Add(TvRowOf("Regional TV, live", nl.Take(30), true));
        return rows;
    }
    List<TvRow> TvFreeMovieRows() {
        TvFree.Load(null);
        var films = TvFree.Films; var rows = new List<TvRow>();
        TvFeature(TvPickFeatured(TvDayShuffle(films, 9)));
        rows.Add(TvCategoryRow("movies", new[] { "Popular" }.Concat(TvGenreRows.Where(g => films.Count(f => (f.Genre ?? "").Contains(g)) > 0).Select(TvGenreTitle)).Concat(new[] { "All films" })));
        var mine = TvMine.Items(TvStore.Favs, null).Where(x => x.Kind == "movie").ToList(); if (mine.Count > 0) rows.Add(TvRowOf("My list", mine, false));
        if (films.Count >= 10) rows.Add(new TvRow { Title = "Top 10 films today", Items = films.Take(10).ToList(), Top10 = true });
        var rec = TvRecommended(films, null); if (rec.Count > 0) rows.Add(TvRowOf("Recommended for you", rec, false));
        rows.AddRange(TvBecauseRows(films, x => true, 2));
        var trend = TvTrending(); if (trend.Count > 0) rows.Add(TvRowOf("Trending this week", trend, false));
        var decs = TvDecadeGroups(films); if (decs.Count > 0) rows.Add(TvCategoryRow("movies", decs.Select(d => d.Key), "Explore by decade"));
        rows.Add(TvRowOf("Popular", films.Skip(10).Take(40), false));
        { var cols = TvCollections(films, 20); if (cols.Count >= 2) rows.Add(TvRowOf("Collections", cols, false)); }   // (TvExtras.cs)
        int k = 0; foreach (var g in TvGenreRows) { var l = films.Where(f => (f.Genre ?? "").Contains(g)).ToList(); if (l.Count >= 4) rows.Add(TvRowOf(TvGenreTitle(g), TvDayShuffle(l, k++), false)); }
        rows.Add(TvRowOf("All free films", films.OrderBy(f => f.Title), false));
        if (films.Count == 0) W.Dispatcher.BeginInvoke(new Action(() => { tvStatus.Text = "The free films couldn't be loaded right now. Check the internet connection and open Movies again."; tvStatus.Visibility = Visibility.Visible; }));
        return rows;
    }
    List<TvRow> TvFreeShowRows() {
        TvFree.Load(null);
        var shows = TvFree.Shows; var rows = new List<TvRow>();
        TvFeature(shows.FirstOrDefault(s => s.Ext != "lowres" && s.Image != null) ?? shows.FirstOrDefault());
        rows.Add(TvCategoryRow("series", new[] { "Classic TV", "Cartoons", "Sitcoms", "Westerns", "Mystery", "Sci-fi", "All series" }));
        var mine = TvMine.Items(TvStore.Favs, null).Where(x => x.Kind == "series").ToList(); if (mine.Count > 0) rows.Add(TvRowOf("My list", mine, false));
        rows.Add(TvRowOf("Classic TV series", shows.Where(s => s.Group == "Classic TV"), false));
        rows.Add(TvRowOf("Cartoons", shows.Where(s => s.Group == "Cartoons"), false));
        foreach (var g in new[] { "Comedy", "Western", "Mystery", "Sci-Fi" }) { var l = shows.Where(s => s.Genre == g).ToList(); if (l.Count >= 2) rows.Add(TvRowOf(g == "Comedy" ? "Sitcoms" : g == "Western" ? "Westerns" : g == "Mystery" ? "Mystery and suspense" : "Sci-fi", l, false)); }
        return rows;
    }

    // ---------- an IPTV profile ----------
    List<TvRow> TvProviderHomeRows(TvAccount a) {
        if (!a.Loaded) TvSource.LoadAccount(a);
        var rows = new List<TvRow>();
        TvFeature(TvPickFeatured(a.Movies.Take(60)));
        TvMyRows(rows, a);
        if (a.Movies.Count >= 10) rows.Add(new TvRow { Title = "Top 10 new films", Items = a.Movies.Take(10).ToList(), Top10 = true });
        { var nw = TvNewThisWeek(a.Movies.Concat(a.Series)); if (nw.Count >= 3) rows.Add(TvRowOf("New this week", nw, false)); }   // (TvExtras.cs)
        if (a.Movies.Count > 10) rows.Add(TvRowOf("New on " + a.Name, a.Movies.Skip(10).Take(40), false));
        var rec = TvRecommended(a.Movies.Concat(a.Series).ToList(), a); if (rec.Count > 0) rows.Add(TvRowOf("Recommended for you", rec, false));
        rows.AddRange(TvBecauseRows(a.Movies.Concat(a.Series).ToList(), x => x.Kind == "movie" || x.Kind == "series", 2));
        { var cols = TvCollections(a.Movies, 20); if (cols.Count >= 2) rows.Add(TvRowOf("Collections", cols, false)); }   // film series as one tile (TvExtras.cs)
        var decs = TvDecadeGroups(a.Movies); if (decs.Count > 0) rows.Add(TvCategoryRow("movies", decs.Select(d => d.Key), "Explore by decade"));
        if (a.Series.Count > 0) rows.Add(TvRowOf("Series", a.Series.Take(40), false));
        if (a.Live.Count > 0) rows.Add(TvRowOf("Live TV", a.Live.Take(30), true));
        foreach (var g in a.Movies.GroupBy(m => m.Group ?? "Other").OrderByDescending(g => g.Count()).Take(6)) rows.Add(TvRowOf(g.Key, g.Take(60), false));
        foreach (var g in a.Series.GroupBy(m => m.Group ?? "Other").OrderByDescending(g => g.Count()).Take(3)) rows.Add(TvRowOf(g.Key, g.Take(60), false));
        return rows;
    }

    // Continue watching, My list (in your order), the channels watched last, and a row per tag: this profile's own
    void TvMyRows(List<TvRow> rows, TvAccount a) {
        var cont = TvStore.Continue(a, true); if (cont.Count > 0) rows.Add(TvRowOf("Continue watching", cont, false));
        var mine = TvMine.Items(TvStore.Favs, a); if (mine.Count > 0) rows.Add(TvRowOf("My list", mine.Take(60), mine.All(x => x.Kind == "live")));
        var last = TvMine.LastLiveOf(a); if (last.Count > 0) rows.Add(TvRowOf("Last watched channels", last, true));
        foreach (var tag in TvMine.TagNames) { var l = TvMine.Tagged(tag, a); if (l.Count > 0) rows.Add(TvRowOf("★ " + tag, l, l.All(x => x.Kind == "live"))); }
        var dls = TvDownloadItems(); if (dls.Count > 0) rows.Add(TvRowOf("Downloads", dls, false));
        var recs = TvRecordingFiles(); if (recs.Count > 0) rows.Add(TvRowOf("Your recordings", recs, true));
    }
    // ---------- Top 10: the big number beside the poster ----------
    UIElement TvTop10(Border tile, int i) {
        var holder = new Grid { Margin = new Thickness(0, 0, 10, 0) };
        var ft = new FormattedText((i + 1).ToString(), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(TvFont, FontStyles.Normal, FontWeights.Black, FontStretches.Condensed), 210, Brushes.Black, 1);
        var path = new System.Windows.Shapes.Path { Data = ft.BuildGeometry(new Point(0, 0)), Fill = TvTheme.Brush("#0A0A0A"), Stroke = TvTheme.Brush("#9A9A9A"), StrokeThickness = 3.5, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, -36), IsHitTestVisible = false };
        holder.Children.Add(path);
        double numW = i == 9 ? 170 : 104;
        tile.Margin = new Thickness(numW - (i == 9 ? 40 : 26), 0, 0, 0); tile.HorizontalAlignment = HorizontalAlignment.Left;
        holder.Children.Add(tile);
        return holder;
    }

    // ---------- "More like this" on a film's page ----------
    List<TvItem> tvDetailMore = new List<TvItem>();
    List<TvItem> TvSimilar(TvItem it) {
        var pool = it.Account != null ? it.Account.Movies ?? new List<TvItem>() : TvFree.Films;
        string g = (it.Genre ?? "").Split(',').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0);
        var same = pool.Where(x => x != it && x.Key != it.Key && ((g != null && (x.Genre ?? "").Contains(g)) || (it.Account != null && x.Group == it.Group))).ToList();
        var l = TvDayShuffle(same, it.Title != null ? it.Title.Length : 0).Take(14).ToList();
        if (l.Count < 6) l.AddRange(pool.Where(x => x != it && !l.Contains(x)).Take(14 - l.Count));
        return l;
    }
    void TvMoreCard(TvItem m, int idx) {
        double w = 128, h = 192;
        var pic = new Grid { Width = w, Height = h };
        int hsh = Math.Abs((m.Title ?? "").GetHashCode()); string[] pal = { "#5b2a86", "#1b4f72", "#7b241c", "#145a32", "#784212", "#4a235a" };
        pic.Children.Add(new Border { Background = TvTheme.Gradient(pal[hsh % pal.Length], "#0b1114") });
        pic.Children.Add(new TextBlock { Text = m.Title, FontSize = 14, FontFamily = Ui.Semi, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10) });
        if (!string.IsNullOrEmpty(m.Image)) { var img = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 }; RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality); TvLoadImage(img, m.Image, 260, null); pic.Children.Add(img); }
        pic.Clip = new RectangleGeometry(new Rect(0, 0, w, h), 9, 9);
        var frame = new Border { Child = pic, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(3), BorderBrush = Brushes.Transparent, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
        var card = new StackPanel { Width = w + 6, Margin = new Thickness(0, 0, 16, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        card.Children.Add(frame);
        card.Children.Add(new TextBlock { Text = m.Title, FontSize = 13.5, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#D9FFFFFF"), Margin = new Thickness(3, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        card.MouseLeftButtonUp += (s, e) => { Sound.Play(Sfx.Select); TvOpenDetail(m); };
        TvOnHover(card, () => { if (tvEpIdx != idx || tvDetailRow != 2) { tvEpIdx = idx; tvDetailRow = 2; MarkTvDetail(); } });
        tvEpisodeRows.Add(frame); tvEpisodes.Children.Add(card);
    }

    // ---------- pictures that need the launcher's own name to be fetched (Wikipedia's), kept on disk ----------
    static readonly object tvImgGate = new object();
    static bool TvNeedsAgent(string url) { return url.Contains("wikimedia.org") || url.Contains("wikipedia.org") || url.Contains("archive.org/") || url.Contains("tvmaze.com") || url.Contains("metahub.space"); }
    // the big film or series at the top of a page, picked when its rows are shown
    TvItem TvFeaturedFor(string page) {
        var a = tvAcc;
        if (a == null) {
            if (page == "home") return TvPickFeatured(TvFree.Films);
            if (page == "iptv-movies") return TvPickFeatured(TvDayShuffle(TvFree.Films, 9));
            if (page == "iptv-series") return TvFree.Shows.FirstOrDefault(s => s.Ext != "lowres" && s.Image != null) ?? TvFree.Shows.FirstOrDefault();
            return null;
        }
        if (!a.Loaded) return null;
        if (page == "home" || page == "iptv-movies") return TvPickFeatured(a.Movies.Take(60));
        if (page == "iptv-series") return TvPickFeatured(a.Series.Take(60));
        return null;
    }
    static void TvLoadViaDisk(Image img, string url, int px, Action ok) {
        string dir = Path.Combine(Paths.Root, @"cache\tvimg"), file;
        using (var md5 = System.Security.Cryptography.MD5.Create()) file = Path.Combine(dir, BitConverter.ToString(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(url))).Replace("-", "") + ".img");
        bool there = File.Exists(file);
        int gen = tvImgGen;
        // the picture is decoded off the window's thread (a page has hundreds), then put on its tile
        Action decode = () => {
            if (gen != tvImgGen) return;   // its page was left before its turn came
            try {
                var bi = new BitmapImage(); bi.BeginInit(); bi.UriSource = new Uri(file); bi.DecodePixelWidth = px; bi.CacheOption = BitmapCacheOption.OnLoad; bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile; bi.EndInit(); bi.Freeze();
                img.Dispatcher.BeginInvoke(new Action(() => {
                    img.Source = bi;
                    // already on disk: there at once; fetched just now: faded in
                    if (there) { img.BeginAnimation(UIElement.OpacityProperty, null); img.Opacity = 1; } else img.BeginAnimation(UIElement.OpacityProperty, Anim(1, 250, 0));
                    if (ok != null) ok();
                }));
            } catch { }
        };
        if (there) { TvDecodeLater(decode); return; }
        ThreadPool.QueueUserWorkItem(_ => {
            try {
                var b = TvFree.GetBytes(url);
                lock (tvImgGate) { Directory.CreateDirectory(dir); if (!File.Exists(file)) File.WriteAllBytes(file, b); }
                TvDecodeLater(decode);
            } catch { }
        });
    }
    // pictures waiting to be decoded, worked through by a few threads of their own (the thread pool is busy with
    // downloads and look-ups); tvImgGen goes up when a page is left, so its pictures still waiting are skipped
    static volatile int tvImgGen;
    static System.Collections.Concurrent.BlockingCollection<Action> tvDecodeQueue;
    static void TvDecodeLater(Action a) {
        if (tvDecodeQueue == null) lock (tvImgGate) if (tvDecodeQueue == null) {
            var q = new System.Collections.Concurrent.BlockingCollection<Action>();
            for (int i = 0; i < 3; i++) new Thread(() => { foreach (var job in q.GetConsumingEnumerable()) { try { job(); } catch { } } }) { IsBackground = true, Name = "tv-pictures" }.Start();
            tvDecodeQueue = q;
        }
        tvDecodeQueue.Add(a);
    }
}
}
