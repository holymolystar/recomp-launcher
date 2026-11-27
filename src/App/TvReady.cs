// TV & Movies, ready before you look: the pages of a profile (For You, Movies, Shows, and TV Live's categories)
// are worked out while it loads (TvPrefetch.cs) and kept, so going from tab to tab shows them at once. What's kept
// is made again after you change something (your lists, hidden or renamed categories, sorting, what you watched):
// TvMine.Version and TvStore.Version count those changes.
using System;
using System.Collections.Generic;
using System.Linq;

namespace RecompLauncher {

class TvBrowseData { public List<KeyValuePair<string, List<TvItem>>> Groups; public string Err, Scope; public int RealFrom, RealTo; }

partial class Shell {
    class TvKept<T> { public string Ver; public T Data; }
    readonly Dictionary<string, TvKept<List<TvRow>>> tvRowsKept = new Dictionary<string, TvKept<List<TvRow>>>();
    readonly Dictionary<string, TvKept<TvBrowseData>> tvBrowseKept = new Dictionary<string, TvKept<TvBrowseData>>();
    static readonly string[] TvReadyPages = { "home", "iptv-movies", "iptv-series" };
    static readonly string[] TvReadyModes = { "live", "movies", "series" };

    // what the pages depend on, as it is now
    string TvVer { get { return TvMine.Version + "|" + TvStore.Version + "|" + tvShowHiddenCats + "|" + TvKidsOn; } }

    // the rows, as plain lists (the window's parts are made fresh each time they're shown)
    static List<TvRow> TvCopyRows(IEnumerable<TvRow> rows) {
        return rows.Select(r => new TvRow { Title = r.Title, Items = r.Items != null ? r.Items.ToList() : new List<TvItem>(), Wide = r.Wide, Top10 = r.Top10 }).ToList();
    }
    List<TvRow> TvRowsFromCache(TvAccount acc, string page) {
        lock (tvRowsKept) {
            TvKept<List<TvRow>> k;
            return tvRowsKept.TryGetValue(TvMine.Profile(acc) + "|" + page, out k) && k.Ver == TvVer ? TvCopyRows(k.Data) : null;
        }
    }
    void TvRowsToCache(TvAccount acc, string page, List<TvRow> rows, string ver) {
        if (rows == null) return;
        lock (tvRowsKept) tvRowsKept[TvMine.Profile(acc) + "|" + page] = new TvKept<List<TvRow>> { Ver = ver, Data = TvCopyRows(rows) };
    }
    TvBrowseData TvBrowseFromCache(TvAccount acc, string mode) {
        lock (tvBrowseKept) {
            TvKept<TvBrowseData> k;
            if (!tvBrowseKept.TryGetValue(TvMine.Profile(acc) + "|" + mode, out k) || k.Ver != TvVer) return null;
            var d = k.Data;
            return new TvBrowseData { Groups = d.Groups.Select(g => new KeyValuePair<string, List<TvItem>>(g.Key, g.Value.ToList())).ToList(), Err = d.Err, Scope = d.Scope, RealFrom = d.RealFrom, RealTo = d.RealTo };
        }
    }
    void TvBrowseToCache(TvAccount acc, string mode, TvBrowseData d, string ver) {
        if (d == null || d.Err != null) return;
        lock (tvBrowseKept) tvBrowseKept[TvMine.Profile(acc) + "|" + mode] = new TvKept<TvBrowseData> { Ver = ver, Data = d };
    }

    // The rows makers pick the big featured title as they go; made ahead (on another thread) they leave the one on
    // screen alone. The page picks its own when it's shown (TvSetRows).
    [ThreadStatic] static bool tvAhead;
    void TvFeature(TvItem x) { if (!tvAhead) tvFeatured = x; }

    // every page of this profile, worked out now (on the loading thread)
    void TvMakeAhead(TvAccount acc, Func<bool> stop) {
        tvAhead = true;
        try {
            foreach (var page in TvReadyPages) {
                if (stop()) return;
                string ver = TvVer;
                try {
                    List<TvRow> rows;
                    if (acc != null) {
                        if (page == "home") rows = TvProviderHomeRows(acc);
                        else { var list = page == "iptv-movies" ? acc.Movies : acc.Series; if (list == null || list.Count == 0) continue; rows = TvIptvRows(acc, page); }
                    } else rows = page == "home" ? TvFreeHomeRows() : page == "iptv-movies" ? TvFreeMovieRows() : TvFreeShowRows();
                    TvRowsToCache(acc, page, rows, ver);
                } catch { }
            }
            foreach (var mode in TvReadyModes) {
                if (stop()) return;
                string ver = TvVer;
                try { TvBrowseToCache(acc, mode, TvBrowseCompute(acc, mode), ver); } catch { }
            }
        } finally { tvAhead = false; }
    }

    // the pictures those pages show first: posters of every row and channel logos of every category
    List<string> TvAheadPictures(TvAccount acc) {
        var urls = new List<string>();
        Action<IEnumerable<TvItem>, int> take = (l, n) => { foreach (var x in l.Take(n)) if (!string.IsNullOrEmpty(x.Image) && x.Image.StartsWith("http")) urls.Add(x.Image); };
        lock (tvRowsKept) foreach (var kv in tvRowsKept.Where(k => k.Key.StartsWith(TvMine.Profile(acc) + "|"))) foreach (var r in kv.Value.Data) take(r.Items, 16);
        lock (tvBrowseKept) foreach (var kv in tvBrowseKept.Where(k => k.Key.StartsWith(TvMine.Profile(acc) + "|"))) foreach (var g in kv.Value.Data.Groups) take(g.Value, 24);
        return urls.Distinct().Take(2500).ToList();
    }
}
}
