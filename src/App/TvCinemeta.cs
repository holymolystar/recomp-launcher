// TV & Movies: Cinemeta (v3-cinemeta.strem.io, the free film and series database Stremio uses; no key needed).
// For a film or series being looked at (the big preview, its page) it fills in what's missing: a wide background
// picture, the title's logo, the poster, cast and director, the IMDb rating, the length, genres and the story.
// Found by name and year; every answer (also "not found") is kept in cache\cinemeta.json, so it's asked once.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace RecompLauncher {

static class Cinemeta {
    static readonly Dictionary<string, Dictionary<string, object>> cache = new Dictionary<string, Dictionary<string, object>>();
    static readonly object gate = new object(); static bool loaded;
    // how many answers it has (goes up as more are looked up)
    public static int Count { get { lock (gate) { Load(); return cache.Count; } } }
    static string FilePath { get { return Path.Combine(Paths.Root, @"cache\cinemeta.json"); } }
    const string Base = "https://v3-cinemeta.strem.io/";

    static void Load() {
        if (loaded) return; loaded = true;
        var d = Json.ReadFile(FilePath); if (d == null) return;
        foreach (var kv in d) { var v = kv.Value as Dictionary<string, object>; if (v != null) cache[kv.Key] = v; }
    }
    static bool dirty; static DateTime savedAt = DateTime.MinValue;
    // what's still to be written (answers are saved at most every 5 seconds)
    public static void Flush() { lock (gate) { if (dirty) { Save(); savedAt = DateTime.Now; dirty = false; } } }
    static void Save() { try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)); Json.WriteFile(FilePath, cache.ToDictionary(kv => kv.Key, kv => (object)kv.Value)); } catch { } }

    // a provider's title without its extras: "EN - The Matrix (1999) [4K]" -> "The Matrix", 1999
    public static string CleanTitle(string t, out string year) {
        year = null; t = t ?? "";
        var y = Regex.Match(t, @"[\(\[\s-](19[2-9]\d|20[0-4]\d)[\)\]\s]*$|\((19[2-9]\d|20[0-4]\d)\)");
        if (y.Success) year = y.Groups[1].Success ? y.Groups[1].Value : y.Groups[2].Value;
        t = Regex.Replace(t, @"^\s*(\|[^|]{1,8}\|\s*|[A-Z]{2,4}\s*[-:|]\s+|\[[^\]]{1,10}\]\s*)", "");
        t = Regex.Replace(t, @"\s*[\(\[]\s*(19|20)\d\d\s*[\)\]]", "");
        t = Regex.Replace(t, @"\s*[\[\(]?\b(4K|UHD|FHD|HD|SD|1080p|720p|2160p|HEVC|x265|x264|MULTI|VOSTFR|DUBBED|SUBBED|NL|EN)\b[\]\)]?", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s+-\s*(19|20)\d\d\s*$", "");
        return t.Trim(' ', '-', ':', '|', '.');
    }
    static string Key(string type, string title, string year) { return type + "|" + Regex.Replace(title.ToLowerInvariant(), @"[^a-z0-9]", "") + "|" + (year ?? ""); }

    static string KeyOf(TvItem it, out string type, out string title, out string year) {
        type = it.Kind == "series" ? "series" : "movie"; title = CleanTitle(it.Title, out year);
        if (year == null) year = Regex.Match(it.Year ?? "", @"(19|20)\d\d").Value; if (year == "") year = null;
        return title.Length < 2 ? null : Key(type, title, year);
    }
    // what Cinemeta said before (null when nothing, or never asked): never asks
    public static Dictionary<string, object> Cached(TvItem it) {
        string type, title, year, key = KeyOf(it, out type, out title, out year); if (key == null) return null;
        lock (gate) { Load(); Dictionary<string, object> hit; return cache.TryGetValue(key, out hit) && !hit.ContainsKey("none") ? hit : null; }
    }
    // whether Cinemeta was asked about it already (found or not)
    public static bool Asked(TvItem it) {
        string type, title, year, key = KeyOf(it, out type, out title, out year); if (key == null) return true;
        lock (gate) { Load(); return cache.ContainsKey(key); }
    }
    // what Cinemeta knows (null when nothing): asks only once per film or series
    public static Dictionary<string, object> Find(TvItem it) {
        string type, title, year, key = KeyOf(it, out type, out title, out year);
        if (key == null) return null;
        // (answers kept before the country was kept are asked again once, for "original audio first")
        lock (gate) { Load(); Dictionary<string, object> hit; if (cache.TryGetValue(key, out hit) && (hit.ContainsKey("none") || hit.ContainsKey("country"))) return hit.ContainsKey("none") ? null : hit; }
        Dictionary<string, object> meta = null;
        try {
            var res = Json.Parse(TvFree.Get(Base + "catalog/" + type + "/top/search=" + Uri.EscapeDataString(title) + ".json", 15000)) as Dictionary<string, object>;
            var metas = (res != null && res.ContainsKey("metas") ? res["metas"] as System.Collections.IEnumerable : null) ?? new object[0];
            string want = Regex.Replace(title.ToLowerInvariant(), @"[^a-z0-9]", "");
            var pick = metas.Cast<object>().OfType<Dictionary<string, object>>().Take(10).FirstOrDefault(m => {
                string n = Regex.Replace((Json.Str(m, "name") ?? "").ToLowerInvariant(), @"[^a-z0-9]", "");
                if (n != want) return false;
                if (year == null) return true;
                var ry = Regex.Match(Json.Str(m, "releaseInfo") ?? "", @"(19|20)\d\d"); return !ry.Success || Math.Abs(int.Parse(ry.Value) - int.Parse(year)) <= 1;
            });
            if (pick != null) {
                var full = Json.Parse(TvFree.Get(Base + "meta/" + type + "/" + Json.Str(pick, "id") + ".json", 15000)) as Dictionary<string, object>;
                meta = Json.Obj(full, "meta");
            }
        } catch { return null; }   // not kept: asked again next time
        var keep = meta == null ? new Dictionary<string, object> { { "none", true } } : new Dictionary<string, object> {
            { "id", Json.Str(meta, "id") }, { "poster", Json.Str(meta, "poster") }, { "background", Json.Str(meta, "background") }, { "logo", Json.Str(meta, "logo") },
            { "rating", Json.Str(meta, "imdbRating") }, { "runtime", Json.Str(meta, "runtime") }, { "year", Json.Str(meta, "releaseInfo") ?? Json.Str(meta, "year") },
            { "description", Json.Str(meta, "description") }, { "genres", string.Join(", ", Json.Strings(meta, "genres").Take(3)) },
            { "cast", string.Join(", ", Json.Strings(meta, "cast").Take(6)) }, { "director", string.Join(", ", Json.Strings(meta, "director").Take(2)) }, { "trailer", TrailerIn(meta) },
            { "country", Json.Str(meta, "country") ?? "" } };
        lock (gate) { cache[key] = keep; dirty = true; if ((DateTime.Now - savedAt).TotalSeconds > 5) { Save(); savedAt = DateTime.Now; dirty = false; } }
        return meta == null ? null : keep;
    }
    // a film's or series' trailer on YouTube (its video id), or null when it has none; looked up once and kept
    public static string Trailer(TvItem it) {
        var m = Find(it); if (m == null) return null;
        string id = Json.Str(m, "id"), type = it.Kind == "series" ? "series" : "movie";
        lock (gate) { if (m.ContainsKey("trailer")) { var t = Json.Str(m, "trailer"); return string.IsNullOrEmpty(t) ? null : t; } }
        if (string.IsNullOrEmpty(id)) return null;
        string yt = "";
        try {
            var meta = Json.Obj(Json.Parse(TvFree.Get(Base + "meta/" + type + "/" + id + ".json", 15000)) as Dictionary<string, object>, "meta");
            if (meta != null) yt = TrailerIn(meta);
        } catch { return null; }   // not kept: asked again next time
        lock (gate) { m["trailer"] = yt; dirty = true; if ((DateTime.Now - savedAt).TotalSeconds > 5) { Save(); savedAt = DateTime.Now; dirty = false; } }
        return yt.Length == 0 ? null : yt;
    }
    // the first trailer Cinemeta lists ("" when none)
    static string TrailerIn(Dictionary<string, object> meta) {
        foreach (var key in new[] { "trailerStreams", "trailers" }) {
            var l = meta.ContainsKey(key) ? meta[key] as System.Collections.IEnumerable : null; if (l == null) continue;
            foreach (var o in l.OfType<Dictionary<string, object>>()) {
                string v = Json.Str(o, "ytId") ?? Json.Str(o, "source");
                if (!string.IsNullOrEmpty(v) && Regex.IsMatch(v, @"^[A-Za-z0-9_-]{11}$")) return v;
            }
        }
        return "";
    }
    // fills in what the item is missing
    public static bool Enrich(TvItem it) {
        var m = Find(it); if (m == null) return false;
        Func<string, string> s = k => { var v = Json.Str(m, k); return string.IsNullOrWhiteSpace(v) ? null : v; };
        if (it.Backdrop == null && s("background") != null) it.Backdrop = s("background");
        if (string.IsNullOrEmpty(it.Image) && s("poster") != null) it.Image = s("poster").Replace("/poster/small/", "/poster/medium/");
        if (it.Logo == null && s("logo") != null) it.Logo = s("logo");
        if ((string.IsNullOrEmpty(it.Rating) || it.Rating == "0") && s("rating") != null) it.Rating = s("rating");
        if (string.IsNullOrEmpty(it.Duration) && s("runtime") != null) { var mm = Regex.Match(s("runtime"), @"(\d+)\s*min"); if (mm.Success) { int n = int.Parse(mm.Groups[1].Value); it.Duration = n >= 60 ? n / 60 + "h " + n % 60 + "m" : n + "m"; } }
        if (string.IsNullOrEmpty(it.Year) && s("year") != null) it.Year = Regex.Match(s("year"), @"(19|20)\d\d").Value;
        if (string.IsNullOrEmpty(it.Genre) && s("genres") != null) it.Genre = s("genres");
        if (string.IsNullOrEmpty(it.Plot) && s("description") != null) it.Plot = s("description");
        if ((string.IsNullOrEmpty(it.Cast) || it.Cast.StartsWith("Directed by")) && s("cast") != null) it.Cast = s("cast");
        if (s("director") != null) it.Director = s("director");
        return true;
    }
}

partial class Shell {
    readonly HashSet<string> tvEnriched = new HashSet<string>();
    // looked up in the background, then "then" on the window's thread (once per item)
    void TvEnrichAsync(TvItem it, Action then) {
        if (it == null || (it.Kind != "movie" && it.Kind != "series" && !(it.Kind == "file" && it.Season == 0)) || it.Id == null && it.Title == null) return;
        lock (tvEnriched) { if (!tvEnriched.Add(it.Key)) return; }
        ThreadPool.QueueUserWorkItem(_ => { bool ok = false; try { ok = Cinemeta.Enrich(it); } catch { } if (ok) W.Dispatcher.BeginInvoke(then); });
    }
}
}
