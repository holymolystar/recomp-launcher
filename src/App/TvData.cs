// TV & Movies: where its lists come from. Kept in tv.json next to the launcher (never in git):
//   your IPTV logins: an Xtream Codes login (server, username, password: live TV, movies and series with
//     categories, posters and episodes) or a playlist link (M3U). Passwords and links are encrypted with your Windows
//     account (DPAPI), so they only open on this PC, for you, and only ever go to that provider's server
//   free live TV: the public iptv-org lists of free-to-air channels (the Netherlands, and world news)
//   free to watch: legal free services (NPO Start, KIJK, Plex...) that open in the launcher's browser
//   your library: the video files in your own folders (Settings: TvFolders, default your Videos folder); series
//     are found by their S01E02 names
//   what you were watching (to resume) and your favourites
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RecompLauncher {

class TvAccount {
    public string Id, Name, Type = "xtream", Server, User, Pass, Url;   // Type: xtream or m3u
    public string Avatar;                       // its profile picture: "a3" (one of the built-in ones) or a picture file
    public string[] Formats = new string[0];   // the stream formats the provider allows (m3u8, ts)
    public string Status;                       // what the last sign-in said
    // what it has, filled when it's opened
    public List<TvItem> Live, Movies, Series;
    public Dictionary<string, string> LiveCats, MovieCats, SeriesCats;
    public bool Loaded, Loading;
    public string Base { get { return (Server ?? "").TrimEnd('/'); } }
}

class TvItem {
    public string Kind;          // live, movie, series, episode, service, file
    public string Id, Title, Group, Image, Backdrop, Plot, Year, Rating, Duration, Url, Ext, EpgId, Genre, Cast;
    public int Season, Episode;
    public string Logo, Director;
    public string Lang;           // a provider's language tag (NL, EN, PL...), read from the name (TvTidy.cs) // from Cinemeta: the title's logo, who directed it
    public long Added;            // when the provider added it (a film) or last added to it (a series), Unix seconds: the New badges
    public int ArchiveDays;       // a provider's channel that keeps past programmes (TV Archive), this many days
    public TvAccount Account;    // null: free, or your library
    public List<TvItem> Episodes; // a series from your library: its episodes
    public string[] GenreList; public int GenreSeen;   // its genres as the rows sort them (TvGenres.cs)
    public string Key { get { return (Account != null ? Account.Id : Kind == "file" ? "lib" : "free") + ":" + Kind + ":" + (Id ?? Url ?? Title); } }
}

static class TvStore {
    static string FilePath { get { return Path.Combine(Paths.Root, "tv.json"); } }
    public static readonly List<TvAccount> Accounts = new List<TvAccount>();
    // key -> position and length in seconds, and when (to resume, and for Continue watching)
    public static readonly Dictionary<string, Dictionary<string, object>> Progress = new Dictionary<string, Dictionary<string, object>>();
    public static readonly List<string> Favs = new List<string>();
    // the free profile, always there: its name and picture
    public static string FreeName = "Free TV", FreeAvatar = "a0";
    static bool loaded;

    static string Protect(string s) {
        if (string.IsNullOrEmpty(s)) return "";
        try { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), Encoding.UTF8.GetBytes("RecompLauncher.TV"), DataProtectionScope.CurrentUser)); } catch { return ""; }
    }
    static string Unprotect(string s) {
        if (string.IsNullOrEmpty(s)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), Encoding.UTF8.GetBytes("RecompLauncher.TV"), DataProtectionScope.CurrentUser)); } catch { return ""; }
    }
    public static void Load() {
        if (loaded) return; loaded = true;
        var d = Json.ReadFile(FilePath); if (d == null) return;
        object list;
        if (d.TryGetValue("Accounts", out list) && list is System.Collections.IEnumerable)
            foreach (var o in ((System.Collections.IEnumerable)list).OfType<Dictionary<string, object>>())
                Accounts.Add(new TvAccount { Id = Json.Str(o, "Id"), Name = Json.Str(o, "Name"), Type = Json.Str(o, "Type") ?? "xtream", Server = Json.Str(o, "Server"),
                    User = Json.Str(o, "User"), Pass = Unprotect(Json.Str(o, "Pass")), Url = Unprotect(Json.Str(o, "Url")), Formats = Json.Strings(o, "Formats"), Avatar = Json.Str(o, "Avatar") });
        FreeName = Json.Str(d, "FreeName") ?? FreeName; FreeAvatar = Json.Str(d, "FreeAvatar") ?? FreeAvatar;
        var p = Json.Obj(d, "Progress");
        if (p != null) foreach (var kv in p) { var v = kv.Value as Dictionary<string, object>; if (v != null) Progress[kv.Key] = v; }
        Favs.AddRange(Json.Strings(d, "Favs"));
    }
    public static int Version;   // counts the saves: what's made ahead from this is made again (TvReady.cs)
    public static void Save() {
        System.Threading.Interlocked.Increment(ref Version);
        try {
            // only the newest 200 things watched are kept
            var keep = Progress.OrderByDescending(kv => Json.Str(kv.Value, "When")).Take(200).ToDictionary(kv => kv.Key, kv => (object)kv.Value);
            Json.WriteFile(FilePath, new Dictionary<string, object> {
                { "Accounts", Accounts.Select(a => (object)new Dictionary<string, object> { { "Id", a.Id }, { "Name", a.Name }, { "Type", a.Type }, { "Server", a.Server ?? "" },
                    { "User", a.User ?? "" }, { "Pass", Protect(a.Pass) }, { "Url", Protect(a.Url) }, { "Formats", a.Formats.ToList() }, { "Avatar", a.Avatar ?? "" } }).ToList() },
                { "FreeName", FreeName }, { "FreeAvatar", FreeAvatar },
                { "Progress", keep }, { "Favs", Favs.ToList() } });
        } catch { }
    }
    public static void SetProgress(TvItem it, double pos, double dur) {
        if (it == null || it.Kind == "live" || it.Kind == "service") return;
        Progress[it.Key] = new Dictionary<string, object> { { "Pos", Math.Round(pos) }, { "Dur", Math.Round(dur) }, { "When", DateTime.Now.ToString("o") },
            { "Title", it.Title ?? "" }, { "Image", it.Image ?? "" }, { "Kind", it.Kind }, { "Url", it.Url ?? "" }, { "Ext", it.Ext ?? "" },
            { "Account", it.Account != null ? it.Account.Id : "" }, { "Group", it.Group ?? "" } };
    }
    public static double ResumeAt(TvItem it) {
        Dictionary<string, object> p;
        if (it == null || !Progress.TryGetValue(it.Key, out p)) return 0;
        double pos = Json.Num(p, "Pos"), dur = Json.Num(p, "Dur");
        return pos > 30 && (dur <= 0 || pos < dur - 60) ? pos : 0;   // from the start again once it's (almost) watched
    }
    // Continue watching: what was started and not finished, newest first
    public static List<TvItem> Continue(TvAccount only = null, bool filter = false) {
        var r = new List<TvItem>();
        foreach (var kv in Progress.OrderByDescending(kv => Json.Str(kv.Value, "When"))) {
            var p = kv.Value; double pos = Json.Num(p, "Pos"), dur = Json.Num(p, "Dur");
            // each profile its own; finished ones are out
            if (filter && (Json.Str(p, "Account") ?? "") != (only != null ? only.Id : "")) continue;
            if (TvMine.Watched.Contains(kv.Key)) continue;
            if (pos < 30 || (dur > 0 && pos > dur - 60)) continue;
            var acc = Accounts.FirstOrDefault(a => a.Id == Json.Str(p, "Account"));
            string[] k = kv.Key.Split(new[] { ':' }, 3);
            r.Add(new TvItem { Kind = Json.Str(p, "Kind"), Id = k.Length == 3 ? k[2] : null, Title = Json.Str(p, "Title"), Image = Json.Str(p, "Image"), Url = Json.Str(p, "Url"),
                Ext = Json.Str(p, "Ext"), Account = acc, Group = Json.Str(p, "Group") });
            if (r.Count >= 20) break;
        }
        return r;
    }
    public static bool IsFav(TvItem it) { return it != null && Favs.Contains(it.Key); }
    public static void ToggleFav(TvItem it) { if (it == null) return; if (!Favs.Remove(it.Key)) { Favs.Insert(0, it.Key); TvMine.Remember(it); } Save(); TvMine.Save(); }
}

// ---------- reading the lists ----------
static class TvSource {
    // what the launcher calls itself to a provider: your own user agent when one is set (TV & Movies settings), else an
    // ordinary browser's, like the one a playlist link works in (some providers turn away anything else)
    public const string BrowserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
    public static string Agent { get { TvPlayerPrefs.Load(); return string.IsNullOrEmpty(TvPlayerPrefs.UserAgent) ? BrowserAgent : TvPlayerPrefs.UserAgent; } }
    // big playlists (tens of thousands of lines) take a while: up to 3 minutes
    static string Fetch(string url) {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
        var r = (HttpWebRequest)WebRequest.Create(url);
        r.UserAgent = Agent; r.Accept = "*/*"; r.Timeout = 180000; r.ReadWriteTimeout = 180000; r.AllowAutoRedirect = true;
        r.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        using (var resp = r.GetResponse()) using (var s = resp.GetResponseStream()) using (var sr = new StreamReader(s, Encoding.UTF8)) return sr.ReadToEnd();
    }
    static string Api(TvAccount a, string action, string extra = "") {
        return a.Base + "/player_api.php?username=" + Uri.EscapeDataString(a.User ?? "") + "&password=" + Uri.EscapeDataString(a.Pass ?? "") + (action != null ? "&action=" + action : "") + extra;
    }
    static IEnumerable<Dictionary<string, object>> List(object o) {
        var e = o as System.Collections.IEnumerable; if (e == null || o is string) return Enumerable.Empty<Dictionary<string, object>>();
        return e.Cast<object>().OfType<Dictionary<string, object>>();
    }
    // signs in: true when the provider says the login is active
    public static bool SignIn(TvAccount a, out string message) {
        message = null;
        try {
            if (a.Type == "m3u") {
                var items = ParseM3u(Fetch(a.Url), a);
                message = items.Count + " channels and videos in the playlist."; return items.Count > 0;
            }
            Dictionary<string, object> d = null; string why = null;
            try { d = Json.Parse(Fetch(Api(a, null))) as Dictionary<string, object>; } catch (Exception e) { why = e.Message; }
            var ui = Json.Obj(d, "user_info");
            if (ui == null || Json.Num(ui, "auth") != 1) {
                // the login route is off (or turned away): the playlist link it came from still works for many providers
                if (!string.IsNullOrEmpty(a.Url)) {
                    var items = ParseM3u(Fetch(a.Url), a);
                    if (items.Count > 0) { a.Type = "m3u"; message = items.Count + " channels and videos, from the playlist link (your provider's login route didn't answer, so TV Archive and its guide aren't available)."; return true; }
                }
                message = why != null ? "Couldn't reach the server: " + why : "The server didn't accept this login."; return false;
            }
            a.Formats = Json.Strings(ui, "allowed_output_formats");
            string status = Json.Str(ui, "status") ?? "";
            double exp = Json.Num(ui, "exp_date");
            message = (status.Length > 0 ? status : "Signed in") + (exp > 0 ? ", until " + new DateTime(1970, 1, 1).AddSeconds(exp).ToLocalTime().ToString("d MMMM yyyy") : "") +
                      (Json.Num(ui, "max_connections") > 0 ? "  •  " + Json.Num(ui, "max_connections") + " screens" : "");
            return status.Length == 0 || status.Equals("Active", StringComparison.OrdinalIgnoreCase);
        } catch (Exception e) { message = "Couldn't reach the server: " + e.Message; return false; }
    }
    // everything an account has (one request per kind)
    public static void LoadAccount(TvAccount a) {
        if (a.Type == "m3u") {
            var items = ParseM3u(Fetch(a.Url), a);
            a.Live = items.Where(i => i.Kind == "live").ToList(); a.Movies = items.Where(i => i.Kind == "movie").ToList();
            // a playlist's episodes, gathered into series by their names
            a.Series = items.Where(i => i.Kind == "episode").GroupBy(i => i.Group + "|" + SeriesName(i.Title)).Select(gr => new TvItem {
                Kind = "series", Id = "m3u-" + gr.Key, Title = SeriesName(gr.First().Title), Group = gr.First().Group, Image = gr.First().Image, Account = a,
                Episodes = gr.OrderBy(e => e.Season).ThenBy(e => e.Episode).ToList() }).ToList();
            a.LiveCats = Cats(a.Live); a.MovieCats = Cats(a.Movies); a.SeriesCats = Cats(a.Series);
            TvTidy.Account(a); a.Loaded = true; return;
        }
        Func<string, Dictionary<string, string>> cats = action => List(Json.Parse(Fetch(Api(a, action)))).GroupBy(c => Json.Str(c, "category_id") ?? "").ToDictionary(g => g.Key, g => Json.Str(g.First(), "category_name") ?? "Other");
        a.LiveCats = cats("get_live_categories"); a.MovieCats = cats("get_vod_categories"); a.SeriesCats = cats("get_series_categories");
        string live = a.Formats.Contains("m3u8") || a.Formats.Length == 0 ? "m3u8" : "ts";
        a.Live = List(Json.Parse(Fetch(Api(a, "get_live_streams")))).Select(s => new TvItem { Kind = "live", Account = a, Id = Json.Str(s, "stream_id"), Title = Json.Str(s, "name"),
            Image = Json.Str(s, "stream_icon"), Group = CatName(a.LiveCats, Json.Str(s, "category_id")), EpgId = Json.Str(s, "epg_channel_id"), Ext = live, ArchiveDays = Json.Num(s, "tv_archive") > 0 ? Math.Max(1, (int)Json.Num(s, "tv_archive_duration")) : 0,
            Url = a.Base + "/live/" + a.User + "/" + a.Pass + "/" + Json.Str(s, "stream_id") + "." + live }).ToList();
        a.Movies = List(Json.Parse(Fetch(Api(a, "get_vod_streams")))).Select(s => new TvItem { Kind = "movie", Account = a, Id = Json.Str(s, "stream_id"), Title = Json.Str(s, "name"),
            Image = Json.Str(s, "stream_icon"), Group = CatName(a.MovieCats, Json.Str(s, "category_id")), Rating = Json.Str(s, "rating"), Ext = Json.Str(s, "container_extension") ?? "mp4",
            Year = YearOf(Json.Str(s, "name")), Plot = Json.Str(s, "added"), Added = (long)Json.Num(s, "added"),
            Url = a.Base + "/movie/" + a.User + "/" + a.Pass + "/" + Json.Str(s, "stream_id") + "." + (Json.Str(s, "container_extension") ?? "mp4") })
            .OrderByDescending(m => m.Plot).ToList();   // newest added first ("added" is a time)
        foreach (var m in a.Movies) m.Plot = null;
        a.Series = List(Json.Parse(Fetch(Api(a, "get_series")))).Select(s => new TvItem { Kind = "series", Account = a, Id = Json.Str(s, "series_id"), Title = Json.Str(s, "name"),
            Image = Json.Str(s, "cover"), Group = CatName(a.SeriesCats, Json.Str(s, "category_id")), Plot = Json.Str(s, "plot"), Rating = Json.Str(s, "rating"),
            Year = Json.Str(s, "releaseDate") != null && Json.Str(s, "releaseDate").Length >= 4 ? Json.Str(s, "releaseDate").Substring(0, 4) : YearOf(Json.Str(s, "name")),
            Genre = Json.Str(s, "genre"), Cast = Json.Str(s, "cast"), Backdrop = FirstOf(s, "backdrop_path"), Added = (long)Json.Num(s, "last_modified") }).ToList();
        TvTidy.Account(a); a.Loaded = true;
    }
    static string FirstOf(Dictionary<string, object> d, string key) { var v = Json.Strings(d, key); return v.FirstOrDefault(x => !string.IsNullOrEmpty(x)); }
    static Dictionary<string, string> Cats(List<TvItem> items) { return items.Select(i => i.Group ?? "Other").Distinct().ToDictionary(x => x, x => x); }
    static string CatName(Dictionary<string, string> cats, string id) { string n; return id != null && cats.TryGetValue(id, out n) ? n : "Other"; }
    static string YearOf(string s) { var m = Regex.Match(s ?? "", @"\b(19[3-9]\d|20[0-4]\d)\b"); return m.Success ? m.Groups[1].Value : null; }
    // a movie's details (plot, backdrop, length), asked for when its page opens
    public static void MovieInfo(TvItem m) {
        if (m.Account == null || m.Account.Type != "xtream") return;
        try {
            var d = Json.Parse(Fetch(Api(m.Account, "get_vod_info", "&vod_id=" + m.Id))) as Dictionary<string, object>;
            var i = Json.Obj(d, "info"); if (i == null) return;
            m.Plot = Json.Str(i, "plot") ?? Json.Str(i, "description"); m.Genre = Json.Str(i, "genre"); m.Cast = Json.Str(i, "cast");
            m.Duration = Json.Str(i, "duration"); m.Rating = Json.Str(i, "rating") ?? m.Rating;
            m.Backdrop = FirstOf(i, "backdrop_path") ?? m.Backdrop;
            if (string.IsNullOrEmpty(m.Image)) m.Image = Json.Str(i, "movie_image");
            string rel = Json.Str(i, "releasedate"); if (rel != null && rel.Length >= 4) m.Year = rel.Substring(0, 4);
        } catch { }
    }
    // a series' seasons and episodes
    public static void SeriesInfo(TvItem s) {
        if (s.Episodes != null) return;
        if (s.Account == null && s.Id != null && s.Id.StartsWith("ia-series:")) { TvFree.Episodes(s); return; }
        s.Episodes = new List<TvItem>();
        if (s.Account == null || s.Account.Type != "xtream") return;
        try {
            var d = Json.Parse(Fetch(Api(s.Account, "get_series_info", "&series_id=" + s.Id))) as Dictionary<string, object>;
            var info = Json.Obj(d, "info");
            if (info != null) { s.Plot = Json.Str(info, "plot") ?? s.Plot; s.Backdrop = FirstOf(info, "backdrop_path") ?? s.Backdrop; s.Genre = Json.Str(info, "genre") ?? s.Genre; }
            var eps = Json.Obj(d, "episodes");
            if (eps == null) return;
            foreach (var season in eps)
                foreach (var e in List(season.Value)) {
                    var ei = Json.Obj(e, "info");
                    string ext = Json.Str(e, "container_extension") ?? "mp4";
                    s.Episodes.Add(new TvItem { Kind = "episode", Account = s.Account, Id = Json.Str(e, "id"), Title = TvTidy.Clean(Json.Str(e, "title")), Lang = s.Lang, Group = s.Title,
                        Season = (int)Json.Num(e, "season"), Episode = (int)Json.Num(e, "episode_num"), Ext = ext, Plot = ei != null ? Json.Str(ei, "plot") : null,
                        Image = ei != null ? Json.Str(ei, "movie_image") : null, Duration = ei != null ? Json.Str(ei, "duration") : null,
                        Url = s.Account.Base + "/series/" + s.Account.User + "/" + s.Account.Pass + "/" + Json.Str(e, "id") + "." + ext });
                }
            s.Episodes = s.Episodes.OrderBy(e => e.Season).ThenBy(e => e.Episode).ToList();
        } catch { }
    }
    // now and next on a live channel (Xtream's short guide; the titles come base64-encoded)
    public static string[] NowNext(TvItem ch) {
        if (ch.Account == null || ch.Account.Type != "xtream") return null;
        try {
            var d = Json.Parse(Fetch(Api(ch.Account, "get_short_epg", "&stream_id=" + ch.Id + "&limit=2"))) as Dictionary<string, object>;
            var l = List(d != null && d.ContainsKey("epg_listings") ? d["epg_listings"] : null).ToList();
            Func<Dictionary<string, object>, string> line = x => {
                string t = Json.Str(x, "title") ?? ""; try { t = Encoding.UTF8.GetString(Convert.FromBase64String(t)); } catch { }
                DateTime st; string when = DateTime.TryParse(Json.Str(x, "start"), out st) ? st.ToString("HH:mm") + "  " : "";
                return when + t;
            };
            return l.Select(line).ToArray();
        } catch { return null; }
    }

    // ---------- M3U playlists ----------
    static readonly Regex Attr = new Regex("([a-zA-Z-]+)=\"([^\"]*)\"");
    public static List<TvItem> ParseM3u(string text, TvAccount a) {
        var r = new List<TvItem>(); TvItem cur = null;
        foreach (var raw in (text ?? "").Split('\n')) {
            string line = raw.Trim();
            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase)) {
                cur = new TvItem { Account = a };
                foreach (Match m in Attr.Matches(line)) {
                    string k = m.Groups[1].Value.ToLowerInvariant(), v = m.Groups[2].Value;
                    if (k == "tvg-logo") cur.Image = v; else if (k == "group-title") cur.Group = v; else if (k == "tvg-id") cur.EpgId = v;
                }
                int comma = QuoteAwareComma(line); cur.Title = comma >= 0 ? line.Substring(comma + 1).Trim() : "Channel";
                // the free lists' notes in the names ("(1080p)", "[Geo-blocked]") go
                if (a == null) cur.Title = Regex.Replace(cur.Title, @"\s*\((\d{3,4}[pi])\)|\s*\[[^\]]*\]", "").Trim();
            } else if (line.Length > 0 && !line.StartsWith("#") && cur != null) {
                cur.Url = line; cur.Id = line;
                string path = line.ToLowerInvariant();
                cur.Ext = Path.GetExtension(path.Split('?')[0]).TrimStart('.');
                var se = Regex.Match(cur.Title ?? "", @"S(\d{1,2})\s*E(\d{1,3})", RegexOptions.IgnoreCase);
                cur.Kind = path.Contains("/series/") || se.Success ? "episode" : path.Contains("/movie/") || cur.Ext == "mkv" || cur.Ext == "mp4" || cur.Ext == "avi" ? "movie" : "live";
                if (se.Success) { cur.Season = int.Parse(se.Groups[1].Value); cur.Episode = int.Parse(se.Groups[2].Value); }
                if (string.IsNullOrEmpty(cur.Group)) cur.Group = "Channels";
                if (cur.Kind == "live" && string.IsNullOrEmpty(cur.Ext)) cur.Ext = "m3u8";
                r.Add(cur); cur = null;
            }
        }
        return r;
    }
    static int QuoteAwareComma(string line) { bool q = false; for (int i = 0; i < line.Length; i++) { if (line[i] == '"') q = !q; else if (line[i] == ',' && !q) return i; } return -1; }
    static string SeriesName(string title) { var m = Regex.Match(title ?? "", @"^(.*?)[\s._-]*S\d{1,2}\s*E\d{1,3}", RegexOptions.IgnoreCase); return (m.Success ? m.Groups[1].Value : title ?? "").Trim(' ', '-', '.'); }

    // ---------- free live TV: iptv-org's public lists of free-to-air channels ----------
    public static readonly string[][] FreeLists = {
        new[] { "Nederland", "https://iptv-org.github.io/iptv/countries/nl.m3u" },
        new[] { "België", "https://iptv-org.github.io/iptv/countries/be.m3u" },
        new[] { "Nieuws (wereld)", "https://iptv-org.github.io/iptv/categories/news.m3u" },
        new[] { "Muziek", "https://iptv-org.github.io/iptv/categories/music.m3u" },
        new[] { "Kids", "https://iptv-org.github.io/iptv/categories/kids.m3u" },
    };
    public static List<TvItem> FreeChannels(string url, string group) {
        var items = ParseM3u(Fetch(url), null).Where(i => i.Kind == "live").ToList();
        foreach (var i in items) { i.Genre = i.Group; i.Group = group; i.Title = Regex.Replace(i.Title ?? "", @"\s*\((\d{3,4}[pi])\)|\s*\[(Geo-blocked|Not 24/7)\]", "").Trim(); }
        // the same channel in several qualities: the first one
        return items.GroupBy(i => Regex.Replace(i.Title ?? "", @"\s*\(.*?\)|\s*\[.*?\]", "").Trim().ToLowerInvariant()).Select(g => g.First()).Take(300).ToList();
    }

    // every channel in iptv-org's worldwide list (about 11,000), sorted into rows by category. Pay channels and
    // channels marked as geo-blocked (they won't play from here) are left out; each row keeps its first 250.
    public const string WorldList = "https://iptv-org.github.io/iptv/index.m3u";
    public static List<KeyValuePair<string, List<TvItem>>> WorldChannels() {
        var items = ParseM3u(Fetch(WorldList), null).Where(i => i.Kind == "live" && !(i.Title ?? "").Contains("[Geo-blocked]")).ToList();
        foreach (var i in items) {
            string cat = ((i.Group ?? "").Split(';').FirstOrDefault(c => c.Trim().Length > 0 && c.Trim() != "Undefined") ?? "Other").Trim();
            i.Genre = cat; i.Group = cat;
            i.Title = Regex.Replace(i.Title ?? "", @"\s*\((\d{3,4}[pi])\)|\s*\[(Not 24/7)\]", "").Trim();
        }
        return items.Where(i => !PayTv.Any(p => (i.Title ?? "").ToLowerInvariant().StartsWith(p)))
            .GroupBy(i => i.Group)
            .Select(g => new KeyValuePair<string, List<TvItem>>(g.Key, g.GroupBy(i => Regex.Replace(i.Title, @"\s*\(.*?\)|\s*\[.*?\]", "").Trim().ToLowerInvariant()).Select(x => x.First()).Take(250).ToList()))
            .OrderBy(kv => kv.Key == "Other" ? 1 : 0).ThenByDescending(kv => kv.Value.Count).ToList();
    }

    // the Dutch list sorted into rows: NPO, the regional broadcasters, music, parliament, local TV. Pay channels that
    // turn up in it (sports, film and kids' channels) are left out: they're not free-to-air.
    static readonly string[] PayTv = { "espn", "discovery", "disney", "nickelodeon", "national geographic", "history", "star channel", "canal+", "film1", "filmbox", "bbc nl", "sbs6", "veronica" };
    static readonly string[] Regional = { "omroep brabant", "omroep gelderland", "omroep zeeland", "omroep west", "omrop fryslan", "omroep flevoland", "nh", "rtv utrecht", "rtv rijnmond",
        "rtv oost", "rtv drenthe", "rtv noord", "l1 tv", "at5", "omroep brabant radio", "rtv rijnmond extra", "rtv noord extra" };
    public static string DutchGroup(TvItem i) {
        string t = (i.Title ?? "").ToLowerInvariant(), g = (i.Genre ?? "").ToLowerInvariant();
        if (PayTv.Any(p => t.StartsWith(p))) return null;
        if (t.StartsWith("npo") || t.StartsWith("bvn")) return "NPO";
        if (Regional.Any(r => t == r || t.StartsWith(r + " "))) return "Regional";
        if (t.StartsWith("tweede kamer")) return "Parliament";
        if (g.Contains("music") || t.Contains("radio") || t.StartsWith("xite") || t == "tmf" || t == "qmusic") return "Music";
        return "Local TV";
    }
    public static int DutchOrder(string g) { return Array.IndexOf(new[] { "NPO", "Regional", "Music", "Local TV", "Parliament" }, g); }

    // ---------- free to watch (legal, free, for the Netherlands): open in the launcher's browser ----------
    public static readonly string[][] Services = {
        // group, name, address, colour, colour, line
        new[] { "Nederlandse omroepen", "NPO Start", "https://npo.nl/start", "#ff6d00", "#ffa040", "Series, documentaries and films from the NPO: free" },
        new[] { "Nederlandse omroepen", "NPO Live", "https://npo.nl/start/live", "#ff6d00", "#e53935", "NPO 1, 2 and 3 and more, live" },
        new[] { "Nederlandse omroepen", "KIJK", "https://www.kijk.nl", "#2d1b69", "#7a3cff", "SBS6, Net5, Veronica and more: free with ads" },
        new[] { "Gratis films en series", "Plex", "https://watch.plex.tv", "#1f1f1f", "#e5a00d", "Free movies, series and live channels, with ads" },
        new[] { "Gratis films en series", "Rakuten TV", "https://www.rakuten.tv/nl", "#bf0000", "#ff4d4d", "Free movies with ads (and films to rent)" },
        new[] { "Gratis films en series", "Pluto TV", "https://pluto.tv", "#1b1b1b", "#fff200", "Free channels and on-demand, where it's offered" },
        new[] { "Gratis films en series", "YouTube", "https://www.youtube.com", "#c4302b", "#ff5a4f", "Free films and series with ads, and everything else" },
        new[] { "Gratis films en series", "Internet Archive", "https://archive.org/details/feature_films", "#2b2b2b", "#8a8a8a", "Classic public-domain films" },
    };

    // ---------- your library: the video files in your folders ----------
    static readonly string[] VideoExt = { ".mp4", ".mkv", ".avi", ".m4v", ".mov", ".webm", ".ts", ".wmv", ".mpg", ".mpeg" };
    public static string[] LibraryFolders() {
        string v = Settings.Get("TvFolders");
        if (string.IsNullOrWhiteSpace(v)) return new[] { Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) };
        return v.Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
    }
    public static void ScanLibrary(out List<TvItem> movies, out List<TvItem> series) {
        var files = new List<FileInfo>();
        foreach (var dir in LibraryFolders()) {
            if (!Directory.Exists(dir)) continue;
            try {
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories)) {
                    if (VideoExt.Contains(f.Extension.ToLowerInvariant()) && f.Length > 20 * 1024 * 1024) files.Add(f);
                    if (files.Count >= 5000) break;
                }
            } catch { }
        }
        var eps = new List<TvItem>(); movies = new List<TvItem>();
        foreach (var f in files) {
            string name = Path.GetFileNameWithoutExtension(f.Name);
            var m = Regex.Match(name, @"^(.*?)[\s._-]*(?:S(\d{1,2})[\s._-]*E(\d{1,3})|(\d{1,2})x(\d{2}))", RegexOptions.IgnoreCase);
            var it = new TvItem { Kind = "file", Url = f.FullName, Id = f.FullName, Ext = f.Extension.TrimStart('.').ToLowerInvariant(), Plot = f.LastWriteTime.ToString("o") };
            if (m.Success && m.Groups[1].Value.Trim().Length > 0) {
                it.Group = Clean(m.Groups[1].Value);
                it.Season = int.Parse(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[4].Value);
                it.Episode = int.Parse(m.Groups[3].Success ? m.Groups[3].Value : m.Groups[5].Value);
                it.Title = "S" + it.Season.ToString("00") + "E" + it.Episode.ToString("00") + "  " + Clean(name.Substring(m.Length));
                eps.Add(it);
            } else {
                // the year and what follows it go ("Film.Name.2019.1080p"), unless the name starts with it (a recording's date)
                it.Title = Clean(Regex.Replace(name, @"(?<=\w.{2,})[\s._(\[-]+(19[3-9]\d|20[0-4]\d)\b.*$", ""));
                if (it.Title.Length < 3) it.Title = Clean(name);
                it.Year = YearOf(name);
                movies.Add(it);
            }
        }
        movies = movies.OrderByDescending(x => x.Plot).ToList();
        series = eps.GroupBy(e => e.Group.ToLowerInvariant()).Select(g => new TvItem { Kind = "series", Id = "lib-" + g.Key, Title = g.First().Group, Group = "Your series",
            Episodes = g.OrderBy(e => e.Season).ThenBy(e => e.Episode).ToList(), Plot = null }).OrderBy(s => s.Title).ToList();
        foreach (var x in movies) x.Plot = null;
        foreach (var s in series) foreach (var e in s.Episodes) e.Plot = null;
    }
    static string Clean(string s) {
        s = Regex.Replace(s ?? "", @"[._]", " ");
        s = Regex.Replace(s, @"\b(1080p|720p|2160p|4k|x264|x265|h264|h265|hevc|bluray|web-?dl|webrip|hdtv|aac|dts)\b.*$", "", RegexOptions.IgnoreCase);
        return Regex.Replace(s, @"\s+", " ").Trim(' ', '-');
    }
}
}
