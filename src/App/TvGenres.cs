// TV & Movies, the way a streaming service sorts films and series: rows by genre (Action, Comedies, Thrillers...)
// instead of only the provider's own categories, and "Because you watched ..." rows of titles like the ones you watched.
// A title's genres come from what the provider says, else from Cinemeta (what was looked up before, cache\cinemeta.json;
// the profile's loading looks up more, TvPrefetch.cs), else from the name of the provider's category ("Horror", "Kids").
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RecompLauncher {

partial class Shell {
    // the genres rows are made for, in the order they're shown, with a film's and a series' row title
    static readonly string[][] TvGenreDefs = {
        new[] { "action", "Action films", "Action series" }, new[] { "comedy", "Comedies", "Comedy series" }, new[] { "thriller", "Thrillers", "Thriller series" },
        new[] { "scifi", "Sci-fi & fantasy", "Sci-fi & fantasy series" }, new[] { "horror", "Horror films", "Horror series" }, new[] { "drama", "Dramas", "Drama series" },
        new[] { "crime", "Crime films", "Crime series" }, new[] { "romance", "Romantic films", "Romantic series" }, new[] { "animation", "Animated films", "Animated series" },
        new[] { "family", "Family films", "Kids & family" }, new[] { "adventure", "Adventures", "Adventure series" }, new[] { "documentary", "Documentaries", "Docuseries" },
        new[] { "mystery", "Mysteries", "Mystery series" }, new[] { "reality", "Reality films", "Reality TV" }, new[] { "war", "War films", "War series" },
        new[] { "history", "History films", "History series" }, new[] { "western", "Westerns", "Western series" }, new[] { "music", "Music films", "Music series" } };

    // a genre's name as Cinemeta or a provider writes it, as one of the above (null: none of them)
    static IEnumerable<string> TvGenreCanon(string g) {
        g = (g ?? "").Trim().ToLowerInvariant();
        if (g.Length == 0) yield break;
        if (g.Contains("action")) yield return "action";
        if (g.Contains("adventure") || g.Contains("avontuur")) yield return "adventure";
        if (g.Contains("comed") || g.Contains("komedie") || g.Contains("sitcom")) yield return "comedy";
        if (g.Contains("thrill")) yield return "thriller";
        if (g.Contains("sci") || g.Contains("fantas")) yield return "scifi";
        if (g.Contains("horror")) yield return "horror";
        if (g.Contains("drama")) yield return "drama";
        if (g.Contains("crime") || g.Contains("misdaad")) yield return "crime";
        if (g.Contains("romanc") || g.Contains("romant")) yield return "romance";
        if (g.Contains("anim") || g.Contains("cartoon") || g.Contains("tekenfilm")) yield return "animation";
        if (g.Contains("family") || g.Contains("famil") || g.Contains("kids") || g.Contains("kinder") || g.Contains("children") || g.Contains("disney")) yield return "family";
        if (g.Contains("docu")) yield return "documentary";
        if (g.Contains("myster")) yield return "mystery";
        if (g.Contains("reality")) yield return "reality";
        if (Regex.IsMatch(g, @"\bwar\b") || g.Contains("oorlog")) yield return "war";
        if (g.Contains("histor") || g.Contains("biograph")) yield return "history";
        if (g.Contains("western")) yield return "western";
        if (g.Contains("music") || g.Contains("muziek")) yield return "music";
    }
    // a title's genres (worked out once, and again only when Cinemeta has learned something new)
    static string[] TvGenresOf(TvItem x) {
        if (x == null) return new string[0];
        if (x.GenreList != null && (x.GenreList.Length > 0 || x.GenreSeen == Cinemeta.Count)) return x.GenreList;
        string from = x.Genre;
        if (string.IsNullOrEmpty(from) && (x.Kind == "movie" || x.Kind == "series")) {
            var m = Cinemeta.Cached(x); var g = m != null ? Json.Str(m, "genres") : null;
            if (!string.IsNullOrEmpty(g)) { x.Genre = g; from = g; }
        }
        var l = (from ?? "").Split(new[] { ',', '/', ';', '|', '&' }, StringSplitOptions.RemoveEmptyEntries).SelectMany(TvGenreCanon).ToList();
        // the provider's category, when it's named after a genre
        if (l.Count == 0 && x.Account != null) l.AddRange(TvGenreCanon(x.Group));
        x.GenreSeen = Cinemeta.Count;
        return x.GenreList = l.Distinct().ToArray();
    }

    // the same film or series, however the provider names it
    static string TvSameTitle(TvItem x) { string y; string t = Cinemeta.CleanTitle(x.Title, out y); return Regex.Replace(t.ToLowerInvariant(), @"[^a-z0-9]", ""); }
    // a row per genre, for a page of films or of series (at least 8 titles each), newest first as the provider has them
    List<TvRow> TvGenreRowsFor(List<TvItem> list, bool series) {
        var rows = new List<TvRow>();
        var by = new Dictionary<string, List<TvItem>>();
        var seen = new HashSet<string>();   // a title the provider has more than once (HD, 4K, another language) once per row
        foreach (var x in list) foreach (var g in TvGenresOf(x)) {
            List<TvItem> l; if (!by.TryGetValue(g, out l)) by[g] = l = new List<TvItem>();
            if (l.Count < 60 && seen.Add(g + "|" + TvSameTitle(x))) l.Add(x);
        }
        foreach (var d in TvGenreDefs) { List<TvItem> l; if (by.TryGetValue(d[0], out l) && l.Count >= 8) rows.Add(TvRowOf(series ? d[2] : d[1], l, false)); }
        return rows;
    }

    // "Because you watched ...": for what you watched last (and then what's in My list), titles like it
    List<TvRow> TvBecauseRows(List<TvItem> pool, Func<TvItem, bool> kindOk, int max) {
        var rows = new List<TvRow>();
        var byKey = new Dictionary<string, TvItem>();
        foreach (var x in pool) if (!byKey.ContainsKey(x.Key)) byKey[x.Key] = x;
        // what you watched, newest first; then My list
        var watchedKeys = TvStore.Progress.OrderByDescending(kv => Json.Str(kv.Value, "When")).Select(kv => kv.Key).Concat(TvMine.Watched).ToList();
        var seeds = watchedKeys.Select(k => Tuple.Create(k, true)).Concat(TvStore.Favs.Select(k => Tuple.Create(k, false)));
        var skip = new HashSet<string>(watchedKeys.Concat(TvStore.Favs));
        var used = new HashSet<string>(); var index = new Dictionary<TvItem, int>(); for (int i = 0; i < pool.Count; i++) if (!index.ContainsKey(pool[i])) index[pool[i]] = i;
        foreach (var s in seeds) {
            if (rows.Count >= max) break;
            TvItem seed; if (!byKey.TryGetValue(s.Item1, out seed) || !kindOk(seed) || !used.Add(seed.Key)) continue;
            var sg = TvGenresOf(seed);
            if (sg.Length == 0) { try { if (Cinemeta.Enrich(seed)) { seed.GenreList = null; sg = TvGenresOf(seed); } } catch { } }
            string dec = TvDecade(seed);
            // the same director, or the same actors, count too
            Func<string, string[]> ppl = v => (v ?? "").Split(',').Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 2).ToArray();
            var sDir = ppl(seed.Director); var sCast = ppl(seed.Cast).Take(4).ToArray();
            var like = pool.Where(x => kindOk(x) && !skip.Contains(x.Key) && !used.Contains(x.Key) && x.Key != seed.Key)
                .Select(x => {
                    int shared = sg.Length == 0 ? 0 : TvGenresOf(x).Count(g => sg.Contains(g));
                    bool group = x.Group != null && x.Group == seed.Group;
                    int people = (sDir.Length > 0 && ppl(x.Director).Any(d => sDir.Contains(d)) ? 3 : 0) + Math.Min(2, sCast.Length > 0 ? ppl(x.Cast).Count(c => sCast.Contains(c)) : 0);
                    return new { x, shared, score = shared * 3 + people * 2 + (group ? 1 : 0) + (dec != null && TvDecade(x) == dec ? 1 : 0) };
                })
                .Where(z => sg.Length > 0 ? z.shared > 0 || z.score >= 6 : z.score > 0)
                .OrderByDescending(z => z.score).ThenBy(z => index[z.x]).Select(z => z.x)
                .GroupBy(TvSameTitle).Select(g => g.First()).Where(x => TvSameTitle(x) != TvSameTitle(seed)).Take(20).ToList();
            if (like.Count < 6) continue;
            foreach (var x in like) used.Add(x.Key);
            string name; name = Cinemeta.CleanTitle(seed.Title, out name);
            rows.Add(TvRowOf((s.Item2 ? "Because you watched " : "More like ") + name, like, false));
        }
        return rows;
    }
}
}
