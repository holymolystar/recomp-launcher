// TV & Movies: getting ready beforehand. When TV opens (the free catalogue) and when a profile is picked (its
// channels, films and series), the launcher loads what the pages will need, in the background:
//   1  the lists        the provider's channels, films and series, or the free films and series
//   2  the TV guide     the profile's listings
//   3  the details      Cinemeta for the films and series at the front of every category (posters, backgrounds,
//                       logos, cast), six at a time
//   4  the pictures     those posters saved to disk, so the rows show them straight away
// A small pill at the bottom says what it's doing and how far along it is, then fades away. Picking another profile
// stops the one before.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RecompLauncher {

partial class Shell {
    Border tvPre, tvPreFill, tvPreTrack; TextBlock tvPreText; int tvPreGen, tvPreShownGen = -1;

    void TvBuildPre() {
        tvPreText = new TextBlock { FontSize = 13, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#E6FFFFFF"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        tvPreFill = new Border { Background = TvTheme.Brush(TvAccent), CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        tvPreTrack = new Border { Width = 180, Height = 4, CornerRadius = new CornerRadius(2), Background = TvTheme.Brush("#33FFFFFF"), Child = tvPreFill, VerticalAlignment = VerticalAlignment.Center };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 13, Foreground = TvTheme.Brush(TvAccent), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        sp.Children.Add(tvPreText); sp.Children.Add(tvPreTrack);
        tvPre = new Border { Child = sp, Background = TvTheme.Brush("#E61C1C1E"), BorderBrush = TvTheme.Brush("#2EFFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(16, 9, 16, 9),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 22), IsHitTestVisible = false, Opacity = 0, Visibility = Visibility.Collapsed };
        Grid.SetColumnSpan(tvPre, 2); Panel.SetZIndex(tvPre, 970);
        tvLayer.Children.Add(tvPre);
    }
    void TvPreSay(int gen, string text, double frac) {
        W.Dispatcher.BeginInvoke(new Action(() => {
            if (gen != tvPreGen || tvPre == null) return;
            // shown again for each new run: an earlier run's fade-out may have left it see-through
            if (tvPreShownGen != gen || tvPre.Visibility != Visibility.Visible) { tvPreShownGen = gen; tvPre.Visibility = Visibility.Visible; tvPre.BeginAnimation(UIElement.OpacityProperty, Anim(1, 200)); }
            tvPreText.Text = text; tvPreFill.Width = Math.Max(0, Math.Min(1, frac)) * tvPreTrack.Width;
        }));
    }
    // what waits for the loading to finish (TvPrefetchThen); run once, by whichever run finishes last
    readonly List<Action> tvPreAfter = new List<Action>();
    void TvPreDone(int gen) {
        W.Dispatcher.BeginInvoke(new Action(() => {
            if (gen != tvPreGen) return;
            var after = tvPreAfter.ToList(); tvPreAfter.Clear();
            foreach (var f in after) try { f(); } catch { }
            if (tvPre == null) return;
            tvPreText.Text = "Ready"; tvPreFill.Width = tvPreTrack.Width;
            var a = Anim(0, 600); a.BeginTime = TimeSpan.FromSeconds(1.2); a.Completed += (s, e) => { if (gen == tvPreGen) tvPre.Visibility = Visibility.Collapsed; };
            tvPre.BeginAnimation(UIElement.OpacityProperty, a);
        }));
    }

    // what this profile's pages will need, loaded ahead
    // Before TV is even opened: a little after the launcher starts, the profile you picked last is loaded the same way,
    // quietly (no pill), so TV & Movies opens with everything there.
    int tvWarmGen = -1; TvAccount tvWarmAcc;
    void TvWarmUp() {
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        t.Tick += (s, e) => {
            t.Stop();
            if (TvOpen) return;   // opened already: that loads it
            try {
                TvStore.Load(); TvMine.Load();
                string last = TvMine.LastProfile;
                var acc = last.Length == 0 || last == "free" ? null : TvStore.Accounts.FirstOrDefault(a => a.Id == last);
                if (last.Length > 0 && last != "free" && acc == null) return;   // that profile is gone
                tvWarmAcc = acc; TvPrefetch(acc, true); tvWarmGen = tvPreGen;
            } catch { }
        };
        t.Start();
    }
    // load a profile with the pill showing, then go on; the start-up warm-up of the same profile is waited for, not redone
    void TvPrefetchThen(TvAccount acc, Action after) {
        if (tvPre == null) TvBuildPre();
        tvPreAfter.Add(after);
        bool joining = tvWarmGen == tvPreGen && tvWarmAcc == acc;
        if (!joining) TvPrefetch(acc);
        TvPreSay(tvPreGen, "Getting " + (acc != null ? acc.Name : TvStore.FreeName) + " ready", 0.02);
    }
    void TvPrefetch(TvAccount acc, bool quiet = false) {
        if (tvPre == null && !quiet) TvBuildPre();
        int gen = ++tvPreGen;
        var hiddenCats = new HashSet<string>(TvMine.HiddenCats);   // as they are now (they change on this thread only)
        new Thread(() => {
            try {
                // 1  the lists
                List<TvItem> films, series;
                if (acc != null) {
                    if (!acc.Loaded) { TvPreSay(gen, "Getting " + acc.Name + "'s channels, films and series", 0.05); TvSource.LoadAccount(acc); }
                    films = acc.Movies ?? new List<TvItem>(); series = acc.Series ?? new List<TvItem>();
                    // nothing from the categories you hid
                    string fs = TvMine.Scope(acc, "movies"), ss = TvMine.Scope(acc, "series");
                    films = films.Where(x => !hiddenCats.Contains(fs + "|" + (x.Group ?? "Other"))).ToList(); series = series.Where(x => !hiddenCats.Contains(ss + "|" + (x.Group ?? "Other"))).ToList();
                } else {
                    if (!TvFree.Loaded) TvPreSay(gen, "Getting the free films and series", 0.05);
                    TvFree.Load(null); films = TvFree.Films; series = TvFree.Shows;
                }
                if (gen != tvPreGen) return;
                // 2  the guide
                TvPreSay(gen, "Getting the TV guide", 0.15);
                try { TvFree.Guide(acc); } catch { }
                if (gen != tvPreGen) return;
                // 3  the details: the front of every category, newest first (up to 600)
                var pick = new List<TvItem>(); var seen = new HashSet<string>();
                Action<IEnumerable<TvItem>> add = l => { foreach (var x in l) if (pick.Count < 600 && seen.Add(x.Key) && TvLangOk(x)) pick.Add(x); };
                add(films.Take(60)); add(series.Take(40));
                foreach (var g in films.GroupBy(x => x.Group ?? "")) add(g.Take(24));
                foreach (var g in series.GroupBy(x => x.Group ?? "")) add(g.Take(18));
                if (acc == null) pick.Clear();   // the free films have theirs already
                int done = 0;
                if (pick.Count > 0) {
                    Parallel.ForEach(pick, new ParallelOptions { MaxDegreeOfParallelism = 6 }, (x, st) => {
                        if (gen != tvPreGen) { st.Stop(); return; }
                        try { var m = Cinemeta.Find(x); if (m != null) { Cinemeta.Enrich(x); var p = Json.Str(m, "poster"); if (!string.IsNullOrEmpty(p)) x.Image = p.Replace("/poster/small/", "/poster/medium/"); } } catch { }
                        int n = Interlocked.Increment(ref done);
                        if (n % 6 == 0) TvPreSay(gen, "Getting posters and details  " + n + " / " + pick.Count, 0.2 + 0.5 * n / pick.Count);
                    });
                    Cinemeta.Flush();
                }
                if (gen != tvPreGen) return;
                // 3b the pages themselves (TvReady.cs), so going from tab to tab shows them at once
                TvPreSay(gen, "Getting the pages ready", 0.68);
                TvMakeAhead(acc, () => gen != tvPreGen);
                if (gen != tvPreGen) return;
                // 4  the pictures, onto the disk: the posters and channel logos those pages show first
                var pics = (acc != null ? pick : films.Take(80).Concat(series)).Select(x => x.Image).Where(u => !string.IsNullOrEmpty(u) && u.StartsWith("http"))
                    .Concat(TvAheadPictures(acc)).Distinct().ToList();
                done = 0;
                Parallel.ForEach(pics, new ParallelOptions { MaxDegreeOfParallelism = 10 }, (u, st) => {
                    if (gen != tvPreGen) { st.Stop(); return; }
                    TvSaveToDisk(u);
                    int n = Interlocked.Increment(ref done);
                    if (n % 6 == 0) TvPreSay(gen, "Getting pictures  " + n + " / " + pics.Count, 0.7 + 0.3 * n / Math.Max(1, pics.Count));
                });
                if (gen == tvPreGen) TvPreDone(gen);
                // ready: picking this profile from now on opens it at once (step 5 doesn't hold it up)
                W.Dispatcher.BeginInvoke(new Action(() => { if (tvWarmGen == gen) tvWarmGen = -1; }));
                // 5  quietly, once it's ready: details for more of the films and series (their genres sort the genre
                //    rows, TvGenres.cs), up to 500 a time, never asked twice; then the pages are made again with them
                if (acc != null && gen == tvPreGen) {
                    var more = films.Concat(series).Where(x => TvLangOk(x) && !Cinemeta.Asked(x)).Take(500).ToList();
                    int found = 0;
                    Parallel.ForEach(more, new ParallelOptions { MaxDegreeOfParallelism = 4 }, (x, st) => {
                        if (gen != tvPreGen) { st.Stop(); return; }
                        try { if (Cinemeta.Find(x) != null) { Interlocked.Increment(ref found); Cinemeta.Enrich(x); } } catch { }
                    });
                    Cinemeta.Flush();
                    if (found > 0 && gen == tvPreGen) TvMakeAhead(acc, () => gen != tvPreGen);
                }
            } catch { if (gen == tvPreGen) TvPreDone(gen); }
            W.Dispatcher.BeginInvoke(new Action(() => { if (tvWarmGen == gen) tvWarmGen = -1; }));   // the warm-up is done
        }) { IsBackground = true, Name = "TV getting ready" }.Start();
    }
    // a picture into the same disk cache the tiles read from (TvLoadViaDisk)
    static void TvSaveToDisk(string url) {
        try {
            string dir = Path.Combine(Paths.Root, @"cache\tvimg"), file;
            using (var md5 = System.Security.Cryptography.MD5.Create()) file = Path.Combine(dir, BitConverter.ToString(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(url))).Replace("-", "") + ".img");
            if (File.Exists(file)) return;
            var b = TvFree.GetBytes(url);
            lock (tvImgGate) { Directory.CreateDirectory(dir); if (!File.Exists(file)) File.WriteAllBytes(file, b); }
        } catch { }
    }
}
}
