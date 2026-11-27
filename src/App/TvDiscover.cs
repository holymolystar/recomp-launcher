// TV & Movies: finding something to watch.
//   Recommended for you   films like the ones you watched, started or put in My list (their genres, or the provider's
//                         categories), not the ones you've seen
//   Trending this week    the free films most watched on the Internet Archive this week (asked once a day)
//   Explore by decade     the 1930s, 1940s... as categories, for the free films and a provider's films alike
//   People                a film's cast as links (on its page and in its menu); an actor's page with everything they're
//                         in; search finds people too
//   Your own details      a different title, picture or story for anything (kept in tv-prefs.json), and back again
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace RecompLauncher {

partial class Shell {
    // ---------- recommended ----------
    List<TvItem> TvRecommended(List<TvItem> pool, TvAccount acc) {
        var seen = new HashSet<string>(TvMine.Watched);
        var signal = TvMine.Items(TvStore.Favs.Concat(TvMine.Watched).Concat(TvStore.Progress.Keys), acc).Where(x => x.Kind == "movie" || x.Kind == "series" || x.Kind == "file").ToList();
        // the ones in the pool (they know their genres) count too
        var keys = new HashSet<string>(TvStore.Favs.Concat(TvMine.Watched).Concat(TvStore.Progress.Keys));
        signal.AddRange(pool.Where(x => keys.Contains(x.Key)));
        if (signal.Count == 0) return new List<TvItem>();
        var weight = new Dictionary<string, int>();
        foreach (var s in signal) foreach (var g in TvTraits(s)) { int w; weight.TryGetValue(g, out w); weight[g] = w + 1; }
        return pool.Where(x => !keys.Contains(x.Key) && !seen.Contains(x.Key))
            .Select(x => new { x, score = TvTraits(x).Sum(g => { int w; return weight.TryGetValue(g, out w) ? w : 0; }) })
            .Where(z => z.score > 0).OrderByDescending(z => z.score).ThenBy(z => z.x.Title).Take(30).Select(z => z.x).ToList();
    }
    static IEnumerable<string> TvTraits(TvItem x) {
        foreach (var g in (x.Genre ?? "").Split(new[] { ',', '/', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)) yield return "g:" + g.Trim().ToLowerInvariant();
        if (x.Account != null && !string.IsNullOrEmpty(x.Group)) yield return "c:" + x.Group;
        var d = TvDecade(x); if (d != null) yield return "d:" + d;
    }

    // ---------- trending this week (free films) ----------
    static List<string> tvTrendIds;
    static List<TvItem> TvTrending() {
        if (tvTrendIds == null) {
            string file = Path.Combine(Paths.Root, @"cache\tvtrend.json");
            try {
                if (File.Exists(file) && (DateTime.Now - File.GetLastWriteTime(file)).TotalHours < 24) tvTrendIds = Json.Strings(Json.ReadFile(file), "Ids").ToList();
                else {
                    string url = "https://archive.org/advancedsearch.php?q=" + Uri.EscapeDataString("collection:feature_films AND mediatype:movies") + "&fl%5B%5D=identifier&sort%5B%5D=week+desc&rows=400&output=json";
                    var res = Json.Parse(TvFree.Get(url)) as Dictionary<string, object>;
                    var docs = Json.Obj(res, "response"); var list = docs != null && docs.ContainsKey("docs") ? docs["docs"] as System.Collections.IEnumerable : null;
                    tvTrendIds = (list ?? new object[0]).Cast<object>().OfType<Dictionary<string, object>>().Select(d => Json.Str(d, "identifier")).Where(x => x != null).ToList();
                    Directory.CreateDirectory(Path.GetDirectoryName(file)); Json.WriteFile(file, new Dictionary<string, object> { { "Ids", tvTrendIds } });
                }
            } catch { tvTrendIds = new List<string>(); }
        }
        var byId = TvFree.Films.GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First());
        return tvTrendIds.Select(i => { TvItem f; return i != null && byId.TryGetValue(i, out f) ? f : null; }).Where(f => f != null).Take(30).ToList();
    }

    // ---------- decades ----------
    static string TvDecade(TvItem x) {
        var m = Regex.Match(x.Year ?? "", @"(19|20)\d\d"); if (!m.Success) m = Regex.Match(x.Title ?? "", @"\b(19[2-9]\d|20[0-4]\d)\b"); if (!m.Success) return null;
        int y = int.Parse(m.Value); return (y / 10 * 10) + "s";
    }
    static List<KeyValuePair<string, List<TvItem>>> TvDecadeGroups(IEnumerable<TvItem> all) {
        return all.Select(x => new { x, d = TvDecade(x) }).Where(z => z.d != null).GroupBy(z => z.d).Where(g => g.Count() >= 3).OrderByDescending(g => g.Key)
            .Select(g => new KeyValuePair<string, List<TvItem>>(g.Key, g.Select(z => z.x).ToList())).ToList();
    }

    // ---------- people ----------
    // the cast: the provider's list, or the names after "starring" in the story
    static List<string> TvCastOf(TvItem it) {
        var r = new List<string>();
        if (!string.IsNullOrEmpty(it.Cast) && !it.Cast.StartsWith("Directed by")) r.AddRange(it.Cast.Split(',').Select(x => x.Trim()).Where(x => x.Length > 1));
        var m = Regex.Match(it.Plot ?? "", @"\b(?:starring|stars|starred|featuring)\s+([^.;]+?)(?:[.;]| as | with | in (?:the|a|his|her)\b|$)", RegexOptions.IgnoreCase);
        if (m.Success) foreach (var n in Regex.Split(m.Groups[1].Value, @",\s*|\s+and\s+")) { var t = n.Trim(); if (Regex.IsMatch(t, @"^[A-Z][\w'.-]+(\s+[A-Z][\w'.-]+){1,3}$") && !r.Contains(t)) r.Add(t); }
        return r.Take(8).ToList();
    }
    static bool TvHas(TvItem x, string person) {
        return (x.Cast ?? "").IndexOf(person, StringComparison.OrdinalIgnoreCase) >= 0 || (x.Plot ?? "").IndexOf(person, StringComparison.OrdinalIgnoreCase) >= 0;
    }
    // the cast on a film's page, as links
    void TvDetailPeople(TvItem it) {
        tvDetailCast.Inlines.Clear();
        var cast = TvCastOf(it); string dir = !string.IsNullOrEmpty(it.Director) ? "Directed by " + it.Director : it.Cast != null && it.Cast.StartsWith("Directed by") ? it.Cast : null;
        if (cast.Count > 0) {
            tvDetailCast.Inlines.Add(new Run("Starring  ") { Foreground = TvTheme.Brush("#99FFFFFF") });
            for (int i = 0; i < Math.Min(4, cast.Count); i++) {
                string name = cast[i];
                var h = new Hyperlink(new Run(name)) { Foreground = Brushes.White, TextDecorations = null, FontFamily = Ui.Semi, Cursor = System.Windows.Input.Cursors.Hand };
                h.Click += (s, e) => TvOpenPerson(name);
                h.MouseEnter += (s, e) => h.TextDecorations = TextDecorations.Underline; h.MouseLeave += (s, e) => h.TextDecorations = null;
                tvDetailCast.Inlines.Add(h);
                if (i < Math.Min(4, cast.Count) - 1) tvDetailCast.Inlines.Add(new Run(", "));
            }
        }
        if (dir != null) { if (cast.Count > 0) tvDetailCast.Inlines.Add(new LineBreak()); tvDetailCast.Inlines.Add(new Run(dir) { Foreground = TvTheme.Brush("#99FFFFFF") }); }
    }
    // everything with someone in it, as a page of posters
    void TvOpenPerson(string name) {
        if (TvDetailOpen) { tvDetail.Visibility = Visibility.Collapsed; tvDetailItem = null; }
        if (ListPageOpen) CloseListPage();
        tvPerson = name; TvOpenBrowse("people", null);
    }
    string tvPerson;
    List<KeyValuePair<string, List<TvItem>>> TvPersonGroups(TvAccount acc) {
        var r = new List<KeyValuePair<string, List<TvItem>>>(); string who = tvPerson ?? "";
        List<TvItem> films, series;
        if (acc != null) { if (!acc.Loaded) TvSource.LoadAccount(acc); films = acc.Movies ?? new List<TvItem>(); series = acc.Series ?? new List<TvItem>(); }
        else { TvFree.Load(null); films = TvFree.Films; series = TvFree.Shows; }
        var f = films.Where(x => TvHas(x, who)).ToList(); var s = series.Where(x => TvHas(x, who)).ToList();
        if (f.Count > 0) r.Add(new KeyValuePair<string, List<TvItem>>("Films with " + who, f));
        if (s.Count > 0) r.Add(new KeyValuePair<string, List<TvItem>>("Series with " + who, s));
        if (r.Count == 0) r.Add(new KeyValuePair<string, List<TvItem>>(who, new List<TvItem>()));
        return r;
    }

    // ---------- your own details ----------
    // what you changed, put on the item (the original kept to go back)
    static void TvApplyMeta(TvItem it) {
        if (it == null) return;
        Dictionary<string, object> m; if (!TvMine.Meta.TryGetValue(it.Key, out m)) return;
        if (!TvMine.MetaOrig.ContainsKey(it.Key)) TvMine.MetaOrig[it.Key] = new[] { it.Title, it.Image, it.Plot };
        string t = Json.Str(m, "Title"), i = Json.Str(m, "Image"), p = Json.Str(m, "Plot");
        if (!string.IsNullOrEmpty(t)) it.Title = t; if (!string.IsNullOrEmpty(i)) { it.Image = i; it.Backdrop = null; } if (!string.IsNullOrEmpty(p)) it.Plot = p;
    }
    void TvSetMeta(TvItem it, string field, string value) {
        Dictionary<string, object> m; if (!TvMine.Meta.TryGetValue(it.Key, out m)) TvMine.Meta[it.Key] = m = new Dictionary<string, object>();
        if (!TvMine.MetaOrig.ContainsKey(it.Key)) TvMine.MetaOrig[it.Key] = new[] { it.Title, it.Image, it.Plot };
        if (string.IsNullOrWhiteSpace(value)) m.Remove(field); else m[field] = value.Trim();
        var o = TvMine.MetaOrig[it.Key];
        it.Title = Json.Str(m, "Title") ?? o[0]; it.Image = Json.Str(m, "Image") ?? o[1]; it.Plot = Json.Str(m, "Plot") ?? o[2];
        TvMine.Save();
    }
    void TvResetMeta(TvItem it) {
        string[] o; if (TvMine.MetaOrig.TryGetValue(it.Key, out o)) { it.Title = o[0]; it.Image = o[1]; it.Plot = o[2]; }
        TvMine.Meta.Remove(it.Key); TvMine.Save();
    }
    void TvMetaRows(TvItem it, List<ListRow> rows) {
        bool changed = TvMine.Meta.ContainsKey(it.Key);
        rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Change its name", Do = () => OpenOsk("A name for it", it.Title, null, v => { TvSetMeta(it, "Title", v); TvAfterActions(); TvFillActions(); }, null) });
        rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Change its picture", Text = "A picture link, or a picture from this PC",
            Do = () => {
                if (!padUsed || MouseMode) {
                    var d = new Microsoft.Win32.OpenFileDialog { Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif", Title = "A picture for " + it.Title };
                    if (d.ShowDialog() == true) {
                        string dir = Path.Combine(Paths.Root, "tvavatars"); Directory.CreateDirectory(dir);
                        string to = Path.Combine(dir, "art-" + DateTime.Now.Ticks + Path.GetExtension(d.FileName).ToLowerInvariant()); File.Copy(d.FileName, to, true);
                        TvSetMeta(it, "Image", to); TvAfterActions(); TvFillActions();
                    }
                } else OpenOsk("A picture's link (https://...)", "", null, v => { TvSetMeta(it, "Image", v); TvAfterActions(); TvFillActions(); }, null, false, true);
            } });
        rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Change its story", Do = () => OpenOsk("The story", it.Plot ?? "", null, v => { TvSetMeta(it, "Plot", v); TvAfterActions(); TvFillActions(); }, null, false, true) });
        if (changed) rows.Add(new ListRow { Glyph = "", C1 = "#c0453c", C2 = "#c0453c", Title = "Back to the original details", Do = () => { TvResetMeta(it); TvAfterActions(); TvFillActions(); } });
    }
}
}
