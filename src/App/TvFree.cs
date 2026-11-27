// TV & Movies: free films and series, and the TV guide.
//   Films   the Internet Archive's public-domain feature films, the most watched first, each matched to its Wikipedia
//           page for a poster, the story and its genres (films without a match, and adult ones, are left out).
//   Series  public-domain TV series and cartoons on the Internet Archive, named below; their episodes are looked up
//           when a series is opened. They play straight from archive.org.
//   Guide   what's on: open-epg.com's Dutch listings for the free channels, a provider's own XMLTV (xmltv.php) for an
//           IPTV profile. Both are kept for a few hours in cache\.
// The catalogue is kept in cache\tvfree.json for a week.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace RecompLauncher {

static class TvFree {
    public static List<TvItem> Films = new List<TvItem>(), Shows = new List<TvItem>();
    public static bool Loaded;
    static readonly object gate = new object();
    static string CacheDir { get { var d = Path.Combine(Paths.Root, "cache"); Directory.CreateDirectory(d); return d; } }
    const string Agent = "RecompLauncher/2.5 (Windows desktop app)";

    public static string Get(string url, int timeout = 30000) {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        var r = (HttpWebRequest)WebRequest.Create(url);
        r.UserAgent = Agent; r.Accept = "*/*"; r.Timeout = timeout; r.ReadWriteTimeout = timeout;
        r.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        using (var resp = r.GetResponse()) using (var s = resp.GetResponseStream()) using (var sr = new StreamReader(s, Encoding.UTF8)) return sr.ReadToEnd();
    }
    public static byte[] GetBytes(string url) {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        var r = (HttpWebRequest)WebRequest.Create(url); r.UserAgent = Agent; r.Timeout = 30000;
        using (var resp = r.GetResponse()) using (var s = resp.GetResponseStream()) using (var ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); }
    }
    static IEnumerable<Dictionary<string, object>> Docs(object o) {
        var e = o as System.Collections.IEnumerable; if (e == null || o is string) return Enumerable.Empty<Dictionary<string, object>>();
        return e.Cast<object>().OfType<Dictionary<string, object>>();
    }
    static string Search(string q, string fields, int rows, string sort = "downloads desc") {
        return "https://archive.org/advancedsearch.php?q=" + Uri.EscapeDataString(q) + string.Concat(fields.Split(',').Select(f => "&fl%5B%5D=" + f)) + "&sort%5B%5D=" + Uri.EscapeDataString(sort) + "&rows=" + rows + "&output=json";
    }
    static readonly Regex Adult = new Regex(@"\b(sex\w*|nud\w*|erotic\w*|strip\w*|porn\w*|burlesque|exploitation|lingerie|topless|adult|xxx|playboy|bikini|naked|stag|peep\w*|fetish|bondage|seduc\w*|lust\w*)\b", RegexOptions.IgnoreCase);

    // ---------- the catalogue ----------
    public static void Load(Action<string> say) {
        lock (gate) {
            if (Loaded) return;
            string file = Path.Combine(CacheDir, "tvfree.json");
            var d = Json.ReadFile(file);
            DateTime when;
            if (d != null && DateTime.TryParse(Json.Str(d, "When"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out when) && (DateTime.Now - when).TotalDays < 7 && Json.Num(d, "Version") >= 5) {
                Films = Docs(d.ContainsKey("Films") ? d["Films"] : null).Select(FromJson).ToList();
                Shows = Docs(d.ContainsKey("Shows") ? d["Shows"] : null).Select(FromJson).ToList();
                if (Films.Count > 0) {
                    Loaded = true;
                    // the series' pictures didn't come last time (Wikipedia was busy): only those again, an hour later
                    if (Shows.All(s => s.EpgId == null) || (Shows.All(s => s.Ext == "lowres") && (DateTime.Now - when).TotalHours > 1)) {
                        try { var again = FetchShows(); if (again.Any(s => s.Ext != "lowres")) { Shows = again; Json.WriteFile(file, new Dictionary<string, object> { { "Version", 5 }, { "When", DateTime.Now.ToString("o") },
                            { "Films", Films.Select(ToJson).ToList() }, { "Shows", Shows.Select(ToJson).ToList() } }); } } catch { }
                    }
                    return;
                }
            }
            if (say != null) say("Getting the free films and series (the first time takes about a minute)...");
            Shows = FetchShows(); Films = FetchFilms();
            Loaded = true;
            try {
                Json.WriteFile(file, new Dictionary<string, object> { { "Version", 5 }, { "When", DateTime.Now.ToString("o") },
                    { "Films", Films.Select(ToJson).ToList() }, { "Shows", Shows.Select(ToJson).ToList() } });
            } catch { }
        }
    }
    static object ToJson(TvItem i) {
        return new Dictionary<string, object> { { "Kind", i.Kind }, { "Id", i.Id }, { "Title", i.Title }, { "Year", i.Year }, { "Plot", i.Plot }, { "Image", i.Image }, { "Backdrop", i.Backdrop },
            { "Genre", i.Genre }, { "Duration", i.Duration }, { "Url", i.Url }, { "Cast", i.Cast }, { "Group", i.Group }, { "Ext", i.Ext }, { "EpgId", i.EpgId }, { "Logo", i.Logo }, { "Director", i.Director }, { "Rating", i.Rating } };
    }
    static TvItem FromJson(Dictionary<string, object> o) {
        return new TvItem { Kind = Json.Str(o, "Kind"), Id = Json.Str(o, "Id"), Title = Json.Str(o, "Title"), Year = Json.Str(o, "Year"), Plot = Json.Str(o, "Plot"), Image = Json.Str(o, "Image"),
            Backdrop = Json.Str(o, "Backdrop"), Genre = Json.Str(o, "Genre"), Duration = Json.Str(o, "Duration"), Url = Json.Str(o, "Url"), Cast = Json.Str(o, "Cast"), Group = Json.Str(o, "Group"), Ext = Json.Str(o, "Ext"), EpgId = Json.Str(o, "EpgId"), Logo = Json.Str(o, "Logo"), Director = Json.Str(o, "Director"), Rating = Json.Str(o, "Rating") };
    }

    static List<TvItem> FetchFilms() {
        var res = Json.Parse(Get(Search("collection:feature_films AND mediatype:movies", "identifier,title,year,subject,runtime,downloads", 450))) as Dictionary<string, object>;
        var docs = Docs(Json.Obj(res, "response") != null && Json.Obj(res, "response").ContainsKey("docs") ? Json.Obj(res, "response")["docs"] : null).ToList();
        var cands = new List<TvItem>();
        var seen = new HashSet<string>();
        foreach (var x in docs) {
            string title = Clean(Json.Str(x, "title")), subj = string.Join(" ", Json.Strings(x, "subject"));
            if (string.IsNullOrEmpty(title) || Adult.IsMatch(title) || Adult.IsMatch(subj)) continue;
            string key = Regex.Replace(title.ToLowerInvariant(), @"[^a-z0-9]", "");
            if (!seen.Add(key)) continue;
            string year = Regex.Match(Json.Str(x, "year") ?? "", @"\d{4}").Value;
            cands.Add(new TvItem { Kind = "movie", Id = Json.Str(x, "identifier"), Title = title, Year = year.Length == 4 ? year : null, Duration = Runtime(Json.Str(x, "runtime")), Url = "ia:" + Json.Str(x, "identifier") });
        }
        // Wikipedia: "Title (1940 film)", then "Title (film)", then "Title"
        // Cinemeta first (the poster, a wide background, the title's logo, cast, director, rating, length, genres), six at a time
        System.Threading.Tasks.Parallel.ForEach(cands, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 6 }, c => {
            try {
                var m = Cinemeta.Find(c); if (m == null) return;
                string desc = Json.Str(m, "description") ?? "", gen = Json.Str(m, "genres") ?? "";
                if (Adult.IsMatch(desc) || Regex.IsMatch(gen, "Adult", RegexOptions.IgnoreCase)) return;
                if (string.IsNullOrEmpty(Json.Str(m, "poster"))) return;
                Cinemeta.Enrich(c);
                c.Plot = desc.Length > 0 ? desc : c.Plot;
                c.Genre = CinemetaGenres(gen, c.Year);
                if (!string.IsNullOrEmpty(c.Director)) c.Cast = c.Cast ?? null;
                c.Ext = "ok";
            } catch { }
        });
        // Wikipedia for the ones Cinemeta doesn't know
        var left = cands.Where(c => c.Ext != "ok").ToList();
        foreach (var pass in new Func<TvItem, string>[] { c => c.Year != null ? c.Title + " (" + c.Year + " film)" : null, c => c.Title + " (film)", c => c.Title }) {
            var ask = left.Select(c => new { c, t = pass(c) }).Where(a => a.t != null).ToList();
            var got = Wiki(ask.Select(a => a.t));
            foreach (var a in ask) {
                Dictionary<string, object> p; if (!got.TryGetValue(a.t, out p)) continue;
                string desc = Json.Str(p, "description") ?? "", ext = Json.Str(p, "extract") ?? "";
                if (!Regex.IsMatch(desc + " " + ext.Split('.')[0], @"\bfilm\b|\bmovie\b|\bserial\b", RegexOptions.IgnoreCase)) continue;
                if (Regex.IsMatch(desc, @"\b(series|franchise|novel|character|disambiguation)\b", RegexOptions.IgnoreCase) && !Regex.IsMatch(desc, @"\bfilm\b", RegexOptions.IgnoreCase)) continue;
                if (a.c.Year != null) { int y = int.Parse(a.c.Year); if (!Regex.IsMatch(desc + " " + ext, @"\b(" + (y - 1) + "|" + y + "|" + (y + 1) + @")\b")) continue; }
                if (Adult.IsMatch(desc) || Regex.IsMatch(ext, @"\b(sexploitation|pornograph\w*|nudist|nudie)\b", RegexOptions.IgnoreCase)) continue;
                var img = Json.Obj(p, "thumbnail"); if (img == null) continue;
                a.c.Image = Json.Str(img, "source"); a.c.Plot = ext.Trim();
                if (a.c.Year == null) a.c.Year = Regex.Match(desc, @"\b(19|20)\d\d\b").Value.NullIfEmpty();
                var by = Regex.Match(desc, @"\bby (.+)$"); if (by.Success) a.c.Cast = "Directed by " + by.Groups[1].Value;
                a.c.Genre = Genres(desc + ". " + ext);
                a.c.Ext = "ok";
            }
            left = left.Where(c => c.Ext != "ok").ToList();
        }
        return cands.Where(c => c.Ext == "ok").ToList();
    }
    static void Log(string s) { try { File.AppendAllText(Path.Combine(CacheDir, "tvfree-log.txt"), DateTime.Now.ToString("s") + "  " + s + Environment.NewLine); } catch { } }
    // Cinemeta's genres in the launcher's own names (silent films by their year)
    static string CinemetaGenres(string gen, string year) {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "Sci-Fi", "Sci-Fi" }, { "Science Fiction", "Sci-Fi" }, { "Film-Noir", "Film noir" }, { "Film Noir", "Film noir" }, { "Music", "Musical" }, { "Musical", "Musical" },
            { "Comedy", "Comedy" }, { "Horror", "Horror" }, { "Western", "Western" }, { "Crime", "Crime" }, { "Mystery", "Mystery" }, { "Thriller", "Thriller" }, { "Adventure", "Adventure" }, { "Romance", "Romance" },
            { "War", "War" }, { "Drama", "Drama" }, { "Animation", "Animation" }, { "Documentary", "Documentary" }, { "Action", "Adventure" }, { "Fantasy", "Sci-Fi" }, { "Family", "Adventure" } };
        var l = gen.Split(',').Select(g => g.Trim()).Where(g => map.ContainsKey(g)).Select(g => map[g]).Distinct().ToList();
        int y; if (int.TryParse(year ?? "", out y) && y < 1929) l.Insert(0, "Silent");
        return l.Count > 0 ? string.Join(", ", l.Take(3)) : "Drama";
    }
    static string NullIfEmpty(this string s) { return string.IsNullOrEmpty(s) ? null : s; }
    static string Clean(string t) { t = Regex.Replace(t ?? "", @"\s*[\(\[]\s*(19|20)\d\d\s*[\)\]]\s*$", ""); t = Regex.Replace(t, @"\s+(HD|720p|1080p|DVD|VHS)$", "", RegexOptions.IgnoreCase); return t.Trim(' ', '\'', '"', '-', '.'); }
    static string Runtime(string r) {
        if (string.IsNullOrEmpty(r)) return null;
        var m = Regex.Match(r, @"^(\d+):(\d\d):(\d\d)$"); int min;
        if (m.Success) min = int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value);
        else { var n = Regex.Match(r, @"(\d+)\s*(min|m)\b"); var hm = Regex.Match(r, @"^(\d+):(\d\d)$"); if (n.Success) min = int.Parse(n.Groups[1].Value); else if (hm.Success) min = int.Parse(hm.Groups[1].Value); else return null; }
        if (min < 20 || min > 400) return null;
        return min >= 60 ? (min / 60) + "h " + (min % 60) + "m" : min + "m";
    }
    static readonly string[][] GenreWords = {
        new[] { "animated", "Animation" }, new[] { "cartoon", "Animation" }, new[] { "science fiction", "Sci-Fi" }, new[] { "sci-fi", "Sci-Fi" }, new[] { "horror", "Horror" }, new[] { "western", "Western" },
        new[] { "noir", "Film noir" }, new[] { "comedy", "Comedy" }, new[] { "musical", "Musical" }, new[] { "war film", "War" }, new[] { "thriller", "Thriller" }, new[] { "crime", "Crime" },
        new[] { "mystery", "Mystery" }, new[] { "adventure", "Adventure" }, new[] { "romantic", "Romance" }, new[] { "romance", "Romance" }, new[] { "documentary", "Documentary" },
        new[] { "silent", "Silent" }, new[] { "drama", "Drama" } };
    static string Genres(string text) {
        string first = Regex.Split(text, @"(?<=\.)\s").Take(2).Aggregate("", (a, b) => a + " " + b).ToLowerInvariant();
        var g = GenreWords.Where(w => first.Contains(w[0])).Select(w => w[1]).Distinct().Take(2).ToList();
        return g.Count > 0 ? string.Join(", ", g) : "Drama";
    }
    // Wikipedia, twenty titles a time: the asked title -> its page (poster, short description, first sentences)
    static Dictionary<string, Dictionary<string, object>> Wiki(IEnumerable<string> titles) {
        var r = new Dictionary<string, Dictionary<string, object>>();
        var all = titles.Distinct().ToList();
        for (int i = 0; i < all.Count; i += 20) {
            var chunk = all.Skip(i).Take(20).ToList();
            try {
                string url = "https://en.wikipedia.org/w/api.php?action=query&format=json&redirects=1&prop=pageimages%7Cdescription%7Cextracts&exintro=1&explaintext=1&exsentences=3&exlimit=20&piprop=thumbnail&pithumbsize=600&pilimit=20&pilicense=any&titles="
                    + Uri.EscapeDataString(string.Join("|", chunk));
                // Wikipedia slows down quick askers: a pause between asks, and a few tries
                string text = null;
                for (int tries = 0; tries < 4 && text == null; tries++) { try { System.Threading.Thread.Sleep(tries == 0 ? 500 : 6000 * tries); text = Get(url); } catch (Exception e) { Log("wiki: " + e.Message); } }
                if (text == null) continue;
                var q = Json.Obj(Json.Parse(text) as Dictionary<string, object>, "query"); if (q == null) continue;
                var map = new Dictionary<string, string>();
                foreach (var key in new[] { "normalized", "redirects" })
                    if (q.ContainsKey(key)) foreach (var n in Docs(q[key])) map[Json.Str(n, "from")] = Json.Str(n, "to");
                var pages = Json.Obj(q, "pages"); if (pages == null) continue;
                var byTitle = pages.Values.OfType<Dictionary<string, object>>().Where(p => !p.ContainsKey("missing")).GroupBy(p => Json.Str(p, "title")).ToDictionary(g => g.Key, g => g.First());
                foreach (var t in chunk) {
                    string f = t; for (int k = 0; k < 3 && map.ContainsKey(f); k++) f = map[f];
                    Dictionary<string, object> p; if (byTitle.TryGetValue(f, out p)) r[t] = p;
                }
            } catch { }
        }
        return r;
    }

    // ---------- series: public-domain TV and cartoons ----------
    // name, its Wikipedia page, what to look for on the Internet Archive, the genre, its number on TVmaze (tvmaze.com: a free
    // TV database, no key needed; posters, stories and every episode's name, number, summary and still)
    static readonly string[][] SeriesList = {
        new[] { "The Beverly Hillbillies", "The Beverly Hillbillies", "Beverly Hillbillies", "Comedy", "2139" },
        new[] { "The Dick Van Dyke Show", "The Dick Van Dyke Show", "Dick Van Dyke Show", "Comedy", "3327" },
        new[] { "Bonanza", "Bonanza", "Bonanza", "Western", "4378" },
        new[] { "The Lone Ranger", "The Lone Ranger (TV series)", "Lone Ranger", "Western", "2146" },
        new[] { "One Step Beyond", "Alcoa Presents: One Step Beyond", "One Step Beyond", "Mystery", "18552" },
        new[] { "Petticoat Junction", "Petticoat Junction", "Petticoat Junction", "Comedy", "2134" },
        new[] { "The Adventures of Sherlock Holmes", "Sherlock Holmes (1954 TV series)", "Sherlock Holmes 1954", "Mystery", "22618" },
        new[] { "Flash Gordon", "Flash Gordon (1954 TV series)", "Space Adventures of Flash Gordon", "Sci-Fi", "34934" },
        new[] { "The Andy Griffith Show", "The Andy Griffith Show", "Andy Griffith Show", "Comedy", "3853" },
        new[] { "The Lucy Show", "The Lucy Show", "Lucy Show", "Comedy", "14377" },
        new[] { "Make Room for Daddy", "The Danny Thomas Show", "Make Room for Daddy", "Comedy", "12740" },
        new[] { "Superman", "Superman (1940s cartoons)", "Superman Fleischer", "Animation", "0" },
        new[] { "Popeye the Sailor", "Popeye the Sailor (film series)", "Popeye", "Animation", "6496" },
        new[] { "Betty Boop", "Betty Boop", "Betty Boop", "Animation", "34953" },
    };
    static List<TvItem> FetchShows() {
        var wiki = Wiki(SeriesList.Select(s => s[1]));
        var r = new List<TvItem>();
        foreach (var s in SeriesList) {
            var it = new TvItem { Kind = "series", Id = "ia-series:" + s[2], Title = s[0], Genre = s[3], Group = s[3] == "Animation" ? "Cartoons" : "Classic TV" };
            if (s.Length > 4 && s[4] != "0") {
                try {
                    System.Threading.Thread.Sleep(300);
                    var m = Json.Parse(Get("https://api.tvmaze.com/shows/" + s[4])) as Dictionary<string, object>;
                    var img = Json.Obj(m, "image"); if (img != null) it.Image = Json.Str(img, "original") ?? Json.Str(img, "medium");
                    it.Plot = Html(Json.Str(m, "summary"));
                    var y = Regex.Match(Json.Str(m, "premiered") ?? "", @"^\d{4}"); if (y.Success) it.Year = y.Value;
                    it.EpgId = "tvmaze:" + s[4];
                    if (it.Image != null) { try { Cinemeta.Enrich(it); } catch { } r.Add(it); continue; }
                } catch (Exception e) { Log("tvmaze: " + e.Message); }
            }
            Dictionary<string, object> p;
            if (!wiki.ContainsKey(s[1])) Log("no Wikipedia page for " + s[1] + " (" + wiki.Count + " found)");
            if (wiki.TryGetValue(s[1], out p)) {
                it.Plot = (Json.Str(p, "extract") ?? "").Trim();
                var img = Json.Obj(p, "thumbnail"); if (img != null) it.Image = Json.Str(img, "source");
                var y = Regex.Match(Json.Str(p, "description") ?? "", @"\b(19|20)\d\d\b"); if (y.Success) it.Year = y.Value;
            }
            // a picture from the archive when Wikipedia has no free one
            if (it.Image == null) {
                try {
                    var res = Json.Parse(Get(Search("title:(" + s[2] + ") AND mediatype:movies", "identifier", 1))) as Dictionary<string, object>;
                    var d = Docs(Json.Obj(res, "response")["docs"]).FirstOrDefault();
                    if (d != null) { it.Image = "https://archive.org/services/img/" + Json.Str(d, "identifier"); it.Ext = "lowres"; }
                } catch { }
            }
            r.Add(it);
        }
        return r;
    }
    static string Html(string s) { if (s == null) return null; s = Regex.Replace(s, "<[^>]+>", ""); return WebUtility.HtmlDecode(s).Trim(); }
    static readonly Regex Junk = new Regex(@"\b(promo|trailer|dvd|vcd|3d|subtitled|commercial|intro|opening|theme song|bloopers?|outtakes?|radio|extras?|bonus|featurette|interview|reunion)\b", RegexOptions.IgnoreCase);
    static readonly Regex Bundle = new Regex(@"\b(episodes|complete|collection|tv show|tv-show|series|season \d+$|pd)\b", RegexOptions.IgnoreCase);
    // a series' episodes: single episodes as they are, and bundles (a whole season in one item) opened up
    public static void Episodes(TvItem s) {
        s.Episodes = new List<TvItem>();
        string q = s.Id.Substring("ia-series:".Length);
        try {
            var res = Json.Parse(Get(Search("title:(" + q + ") AND mediatype:movies", "identifier,title", 90))) as Dictionary<string, object>;
            var docs = Docs(Json.Obj(res, "response")["docs"]).ToList();
            var eps = new List<TvItem>(); var seen = new HashSet<string>(); int bundles = 0;
            string nameKey = Regex.Replace(s.Title.ToLowerInvariant().Replace("the ", ""), @"[^a-z0-9]", "");
            foreach (var d in docs) {
                string id = Json.Str(d, "identifier"), t = Clean(Json.Str(d, "title"));
                if (Junk.IsMatch(t)) continue;
                string rest = EpisodeName(t, s.Title, q);
                bool bundle = Bundle.IsMatch(t) || Regex.Replace(rest.ToLowerInvariant().Replace("the ", ""), @"[^a-z0-9]", "") == nameKey || rest.Length == 0;
                if (bundle) {
                    if (bundles >= 3) continue; bundles++;
                    foreach (var f in Videos(id)) {
                        string n = EpisodeName(Clean(Regex.Replace(Path.GetFileNameWithoutExtension(f), @"[_\.]+", " ")), s.Title, q);
                        string k = Regex.Replace(n.ToLowerInvariant(), @"[^a-z0-9]", ""); if (k.Length == 0 || !seen.Add(k)) continue;
                        eps.Add(new TvItem { Kind = "episode", Id = "ia:" + id + "/" + f, Url = "ia:" + id + "/" + f, Title = n, Group = s.Title, Image = "https://archive.org/services/img/" + id });
                    }
                } else {
                    string k = Regex.Replace(rest.ToLowerInvariant(), @"[^a-z0-9]", ""); if (k.Length == 0 || !seen.Add(k)) continue;
                    eps.Add(new TvItem { Kind = "episode", Id = "ia:" + id, Url = "ia:" + id, Title = rest, Group = s.Title, Image = "https://archive.org/services/img/" + id });
                }
            }
            // numbered where the names say so ("Ep01", "#31", "S3 E23", "Season 2, Episode 17"), in that order
            foreach (var e in eps) {
                // "1x27" first: season 1, episode 27
                var sx = Regex.Match(e.Title, @"\b(\d{1,2})x(\d{1,3})\b");
                if (sx.Success) {
                    e.Season = int.Parse(sx.Groups[1].Value); e.Episode = int.Parse(sx.Groups[2].Value);
                    e.Title = Regex.Replace(e.Title, @"\s*[-–:,]?\s*(Ep(isode)?\.?\s*)?\d{1,2}x\d{1,3}\s*[-–:,]?\s*", " ", RegexOptions.IgnoreCase).Trim(' ', '-', ':', '"', '\'');
                    if (e.Title.Length == 0) e.Title = "Episode " + e.Episode;
                    continue;
                }
                var se = Regex.Match(e.Title, @"S(?:eason)?\s*(\d+)\s*[,:]?\s*E(?:p(?:isode)?)?\.?\s*(\d+)", RegexOptions.IgnoreCase);
                var ep = Regex.Match(e.Title, @"(?:\bEp(?:isode)?\.?\s*|#)(\d+)", RegexOptions.IgnoreCase);
                e.Season = se.Success ? int.Parse(se.Groups[1].Value) : 1;
                e.Episode = se.Success ? int.Parse(se.Groups[2].Value) : ep.Success ? int.Parse(ep.Groups[1].Value) : 0;
                e.Title = Regex.Replace(e.Title, @"^\s*(S(?:eason)?\s*\d+\s*[,:]?\s*E(?:p(?:isode)?)?\.?\s*\d+|(?:Ep(?:isode)?\.?\s*|#)\d+)\s*[:\-–,]?\s*", "", RegexOptions.IgnoreCase).Trim(' ', '-', ':', '"', '\'');
                if (e.Title.Length == 0) e.Title = "Episode " + e.Episode;
            }
            // TVmaze knows every episode: by its name (or its number) it gets its proper name, number, summary and still
            if (s.EpgId != null && s.EpgId.StartsWith("tvmaze:")) {
                try {
                    var list = Docs(Json.Parse(Get("https://api.tvmaze.com/shows/" + s.EpgId.Substring(7) + "/episodes"))).ToList();
                    Func<string, string> key = x => Regex.Replace((x ?? "").ToLowerInvariant().Replace("&", "and"), @"[^a-z0-9]", "");
                    var byName = list.GroupBy(x => key(Json.Str(x, "name"))).Where(g => g.Key.Length >= 4).ToDictionary(g => g.Key, g => g.First());
                    var byNum = list.GroupBy(x => (int)Json.Num(x, "season") + "x" + (int)Json.Num(x, "number")).ToDictionary(g => g.Key, g => g.First());
                    foreach (var e in eps) {
                        string k = key(Regex.Replace(e.Title, @"\s*\(\d\)$|\bS\d+\s*E\d+\b", ""));
                        Dictionary<string, object> hit = null;
                        if (k.Length >= 4 && !byName.TryGetValue(k, out hit)) hit = byName.Where(kv => k.Contains(kv.Key) || kv.Key.Contains(k)).Select(kv => kv.Value).FirstOrDefault();
                        if (hit == null && e.Episode > 0) byNum.TryGetValue(e.Season + "x" + e.Episode, out hit);
                        if (hit == null) continue;
                        e.Title = Json.Str(hit, "name") ?? e.Title; e.Season = (int)Json.Num(hit, "season"); e.Episode = (int)Json.Num(hit, "number");
                        e.Plot = Html(Json.Str(hit, "summary")) ?? e.Plot;
                        var im = Json.Obj(hit, "image"); if (im != null) e.Image = Json.Str(im, "original") ?? Json.Str(im, "medium");
                        var rt = Json.Num(hit, "runtime"); if (rt > 0) e.Duration = (int)rt + "m";
                        var ad = Regex.Match(Json.Str(hit, "airdate") ?? "", @"^\d{4}"); if (ad.Success) e.Year = ad.Value;
                    }
                } catch (Exception ex) { Log("tvmaze episodes: " + ex.Message); }
            }
            // the same episode twice (a copy with ".ia" on its name, the same number twice), and names that are only a number: once / out
            foreach (var e in eps) e.Title = Regex.Replace(e.Title, @"(\s+|_)ia$", "", RegexOptions.IgnoreCase).Trim();
            eps = eps.Where(e => !Regex.IsMatch(e.Title, @"^\d+$")).ToList();
            var keep = new List<TvItem>(); var seenNum = new HashSet<string>(); var seenName = new HashSet<string>();
            foreach (var e in eps) {
                string nk = Regex.Replace(e.Title.ToLowerInvariant(), @"[^a-z0-9]", "");
                if (nk.Length > 0 && !seenName.Add(nk)) continue;
                if (e.Episode > 0 && !seenNum.Add(e.Season + "x" + e.Episode)) e.Episode = 0;
                keep.Add(e);
            }
            eps = keep;
            int n2 = 0;
            s.Episodes = eps.OrderBy(e => e.Season).ThenBy(e => e.Episode == 0 ? 1 : 0).ThenBy(e => e.Episode).ThenBy(e => e.Title).ToList();
            foreach (var g in s.Episodes.GroupBy(e => e.Season)) { var used = new HashSet<int>(g.Where(e => e.Episode > 0).Select(e => e.Episode)); n2 = 0; foreach (var e in g) if (e.Episode == 0) { do n2++; while (used.Contains(n2)); e.Episode = n2; used.Add(n2); } }
            s.Episodes = s.Episodes.OrderBy(e => e.Season).ThenBy(e => e.Episode).ToList();
        } catch { }
    }
    static string EpisodeName(string t, string series, string q) {
        string r = t;
        foreach (var name in new[] { series, series.StartsWith("The ") ? series.Substring(4) : series, q }.OrderByDescending(x => x.Length))
            r = Regex.Replace(r, "^\\W*" + Regex.Escape(name) + "\\W*", "", RegexOptions.IgnoreCase);
        r = Regex.Replace(r, @"^(\(?\s*(19|20)\d\d\s*\)?\s*[-:]?\s*)", "");
        return r.Trim(' ', '-', ':', '"', '\'', '–', '|');
    }
    // the video files in an archive item, the best copy of each
    static List<string> Videos(string id) {
        var d = Json.Parse(Get("https://archive.org/metadata/" + id)) as Dictionary<string, object>;
        var files = Docs(d != null && d.ContainsKey("files") ? d["files"] : null).ToList();
        var vids = files.Where(f => Regex.IsMatch(Json.Str(f, "name") ?? "", @"\.(mp4|m4v|mkv|avi|ogv|mpg|mpeg)$", RegexOptions.IgnoreCase) && !(Json.Str(f, "name") ?? "").Contains("/")).ToList();
        // one per episode: the same name in several formats -> the mp4 (the "h.264" one when there are two), else the biggest
        return vids.GroupBy(f => Regex.Replace(Path.GetFileNameWithoutExtension(Json.Str(f, "name")), @"(\.ia|_512kb|\.h264)$", "", RegexOptions.IgnoreCase))
            .Select(g => g.OrderBy(f => (Json.Str(f, "name") ?? "").EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                          .ThenBy(f => (Json.Str(f, "format") ?? "").IndexOf("h.264", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1)
                          .ThenByDescending(f => Json.Num(f, "size")).First())
            .Select(f => Json.Str(f, "name")).OrderBy(x => x).Take(80).ToList();
    }
    // "ia:identifier" or "ia:identifier/file" -> a link mpv can play
    public static string Resolve(string url) {
        if (url == null || !url.StartsWith("ia:")) return url;
        string rest = url.Substring(3); int slash = rest.IndexOf('/');
        string id = slash < 0 ? rest : rest.Substring(0, slash), file = slash < 0 ? Videos(id).FirstOrDefault() : rest.Substring(slash + 1);
        if (file == null) throw new Exception("this one has no video to play");
        return "https://archive.org/download/" + id + "/" + string.Join("/", file.Split('/').Select(Uri.EscapeDataString));
    }

    // ---------- the TV guide ----------
    public class Prog { public DateTime Start, Stop; public string Title, Desc, RawStart; }
    // the guide's channels by id, with their names (for matching a channel by hand)
    public static readonly Dictionary<string, Dictionary<string, string>> GuideChannels = new Dictionary<string, Dictionary<string, string>>();
    static readonly Dictionary<string, Dictionary<string, List<Prog>>> guides = new Dictionary<string, Dictionary<string, List<Prog>>>();
    static readonly Dictionary<string, DateTime> guideWhen = new Dictionary<string, DateTime>();
    public static string NameKey(string n) { n = Regex.Replace((n ?? "").ToLowerInvariant(), @"\.(nl|be)$|\b(hd|fhd|sd|uhd|4k|h265)\b|\(.*?\)|\[.*?\]", ""); return Regex.Replace(n, @"[^a-z0-9]", ""); }
    // channel key (its guide id and its name, simplified) -> what's on, from a few hours ago to tomorrow
    public static string GuideKey(TvAccount a) {
        string prof = TvMine.Profile(a), custom; TvMine.EpgUrls.TryGetValue(prof, out custom);
        return (a == null ? "free-nl" : "acc-" + a.Id) + (string.IsNullOrEmpty(custom) ? "" : "-" + (custom.GetHashCode() & 0xffffff).ToString("x")) + "|" + TvMine.ShiftOf(prof);
    }
    // a profile's guide fetched again on the next look
    public static void ForgetGuide(TvAccount a) {
        lock (guides) { foreach (var k in guides.Keys.Where(k => k.StartsWith(a == null ? "free-nl" : "acc-" + a.Id)).ToList()) { guides.Remove(k); guideWhen.Remove(k); } }
        foreach (var f in Directory.GetFiles(CacheDir, "epg-" + (a == null ? "free-nl" : "acc-" + a.Id) + "*.xml")) try { File.Delete(f); } catch { }
    }
    public static Dictionary<string, List<Prog>> Guide(TvAccount a) {
        string prof = TvMine.Profile(a), custom; TvMine.EpgUrls.TryGetValue(prof, out custom);
        string key = GuideKey(a); int shift = TvMine.ShiftOf(prof);
        lock (guides) {
            DateTime w; if (guides.ContainsKey(key) && guideWhen.TryGetValue(key, out w) && (DateTime.Now - w).TotalHours < Math.Min(3, TvMine.HoursOf(prof))) return guides[key];
        }
        string file = Path.Combine(CacheDir, "epg-" + key.Split('|')[0] + ".xml");
        if (!File.Exists(file) || (DateTime.Now - File.GetLastWriteTime(file)).TotalHours > TvMine.HoursOf(prof)) {
            string url = !string.IsNullOrEmpty(custom) ? custom : a == null ? "https://www.open-epg.com/files/netherlands1.xml"
                : a.Type == "xtream" ? a.Base + "/xmltv.php?username=" + Uri.EscapeDataString(a.User ?? "") + "&password=" + Uri.EscapeDataString(a.Pass ?? "") : null;
            if (url == null) return new Dictionary<string, List<Prog>>();
            var bytes = GetBytes(url);
            if (bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b) using (var gz = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress)) using (var ms = new MemoryStream()) { gz.CopyTo(ms); bytes = ms.ToArray(); }
            File.WriteAllBytes(file, bytes);
        }
        Dictionary<string, string> names;
        var r = ReadXmlTv(file, out names);
        // a time shift: every programme moved by the same hours
        if (shift != 0) foreach (var l in r.Values.Distinct()) foreach (var p in l) { p.Start = p.Start.AddHours(shift); p.Stop = p.Stop.AddHours(shift); }
        lock (guides) { guides[key] = r; guideWhen[key] = DateTime.Now; GuideChannels[key] = names; }
        return r;
    }
    static Dictionary<string, List<Prog>> ReadXmlTv(string file, out Dictionary<string, string> channelNames) {
        var byId = new Dictionary<string, List<Prog>>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DateTime from = DateTime.Now.AddHours(-4), to = DateTime.Now.AddHours(26);
        using (var xr = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreWhitespace = true, IgnoreComments = true })) {
            while (xr.Read()) {
                if (xr.NodeType != XmlNodeType.Element) continue;
                if (xr.Name == "channel") {
                    string id = xr.GetAttribute("id");
                    using (var sub = xr.ReadSubtree()) while (sub.Read()) if (sub.NodeType == XmlNodeType.Element && sub.Name == "display-name") { names[id] = sub.ReadElementContentAsString(); break; }
                } else if (xr.Name == "programme") {
                    string ch = xr.GetAttribute("channel"); DateTime st = XmlTime(xr.GetAttribute("start")), sp = XmlTime(xr.GetAttribute("stop"));
                    if (ch == null || sp < from || st > to || st == DateTime.MinValue) { xr.Skip(); continue; }
                    string raw = xr.GetAttribute("start") ?? "";
                    var p = new Prog { Start = st, Stop = sp, RawStart = raw.Length >= 12 ? raw.Substring(0, 12) : null };
                    using (var sub = xr.ReadSubtree()) while (sub.Read()) {
                        if (sub.NodeType != XmlNodeType.Element) continue;
                        if (sub.Name == "title" && p.Title == null) p.Title = sub.ReadElementContentAsString();
                        else if (sub.Name == "desc" && p.Desc == null) p.Desc = sub.ReadElementContentAsString();
                    }
                    List<Prog> l; if (!byId.TryGetValue(ch, out l)) byId[ch] = l = new List<Prog>(); l.Add(p);
                }
            }
        }
        channelNames = byId.Keys.ToDictionary(k => k.ToLowerInvariant(), k => { string n; return names.TryGetValue(k, out n) ? n : k; });
        var r = new Dictionary<string, List<Prog>>();
        foreach (var kv in byId) {
            var l = kv.Value.OrderBy(p => p.Start).ToList();
            r[kv.Key.ToLowerInvariant()] = l;
            string n; if (names.TryGetValue(kv.Key, out n)) { var k = NameKey(n); if (k.Length > 0 && !r.ContainsKey("n:" + k)) r["n:" + k] = l; }
            var k2 = NameKey(kv.Key); if (k2.Length > 0 && !r.ContainsKey("n:" + k2)) r["n:" + k2] = l;
        }
        return r;
    }
    static DateTime XmlTime(string s) {
        if (string.IsNullOrEmpty(s) || s.Length < 14) return DateTime.MinValue;
        DateTime t; if (!DateTime.TryParseExact(s.Substring(0, 14), "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return DateTime.MinValue;
        var m = Regex.Match(s.Substring(14), @"([+-])(\d\d)(\d\d)");
        if (m.Success) { var off = new TimeSpan(int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), 0); t = m.Groups[1].Value == "+" ? t - off : t + off; return DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime(); }
        return t;
    }
    // a channel's programmes: by its guide id, else by its name
    public static List<Prog> For(Dictionary<string, List<Prog>> g, TvItem ch) {
        List<Prog> l; string mapped;
        // matched by hand
        if (TvMine.EpgMap.TryGetValue(ch.Key, out mapped)) return mapped == "" ? null : g.TryGetValue(mapped, out l) ? l : null;
        if (!string.IsNullOrEmpty(ch.EpgId)) { string id = ch.EpgId.ToLowerInvariant(); if (g.TryGetValue(id, out l)) return l; if (g.TryGetValue(Regex.Replace(id, "@.*$", ""), out l)) return l; }
        if (!string.IsNullOrEmpty(ch.EpgId) && g.TryGetValue("n:" + NameKey(System.Text.RegularExpressions.Regex.Replace(ch.EpgId, "@.*$", "")), out l)) return l;
        if (g.TryGetValue("n:" + NameKey(ch.Title), out l)) return l;
        return null;
    }
}
}
