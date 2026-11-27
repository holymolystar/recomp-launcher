// TV & Movies: what you make your own (kept in tv-prefs.json, next to tv.json):
//   Already watched   films and episodes you finished (or marked), with a tick
//   Tags              your own groups of favourites ("Sport", "Kids"...), each a row and a category
//   Categories        your order, your names, hidden ones, and how each page is sorted (per profile and page)
//   Hidden channels   out of sight until you show them again
//   Last watched      the live channels you watched last, per profile
//   Start page        the tab a profile opens on
// What's in My list, a tag or Last watched is remembered with its name and picture, so those rows show straight away.
// The menus: X (or a right-click) on a tile gives the item's actions; X (or a right-click) on a category gives the
// category's. Both are ListPages, like the rest of the launcher's menus.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RecompLauncher {

static class TvMine {
    static string FilePath { get { return Path.Combine(Paths.Root, "tv-prefs.json"); } }
    public static readonly HashSet<string> Watched = new HashSet<string>();
    public static readonly Dictionary<string, List<string>> Tags = new Dictionary<string, List<string>>();   // item key -> its tags
    public static readonly List<string> TagNames = new List<string>();
    public static readonly Dictionary<string, Dictionary<string, object>> Snaps = new Dictionary<string, Dictionary<string, object>>();
    public static readonly Dictionary<string, List<string>> CatOrder = new Dictionary<string, List<string>>();
    public static readonly Dictionary<string, string> Renames = new Dictionary<string, string>();
    public static readonly HashSet<string> HiddenCats = new HashSet<string>(), HiddenItems = new HashSet<string>();
    public static readonly Dictionary<string, string> Sorts = new Dictionary<string, string>(), StartPages = new Dictionary<string, string>(), Themes = new Dictionary<string, string>();   // Themes: TvThemes.cs
    public static string LastProfile = "";   // the profile picked last ("free" for Free TV): loaded ahead when the launcher starts
    public static readonly Dictionary<string, List<string>> LastLive = new Dictionary<string, List<string>>();
    // the guide: a profile's own guide link, time shift (hours) and how often it's fetched again; channels matched by hand
    public static readonly Dictionary<string, string> EpgUrls = new Dictionary<string, string>(), EpgMap = new Dictionary<string, string>();
    public static readonly Dictionary<string, int> EpgShifts = new Dictionary<string, int>(), EpgHours = new Dictionary<string, int>();
    public static int PreviewMs = 900;
    // per provider profile: the languages to show (empty: all)
    public static readonly Dictionary<string, List<string>> Langs = new Dictionary<string, List<string>>();
    public static readonly HashSet<string> LangStrict = new HashSet<string>();   // these profiles hide what has no language
    public static readonly HashSet<string> LangAsked = new HashSet<string>();    // these profiles were asked for their languages once
    // your own details for an item (Title, Image, Plot), and the originals while the launcher runs
    public static readonly Dictionary<string, Dictionary<string, object>> Meta = new Dictionary<string, Dictionary<string, object>>();
    public static readonly Dictionary<string, string[]> MetaOrig = new Dictionary<string, string[]>();
    // reminders: Key (the channel), Title, Channel, Start (ISO), Account
    public static readonly List<Dictionary<string, object>> Reminders = new List<Dictionary<string, object>>();
    static bool loaded;

    public static void Load() {
        if (loaded) return; loaded = true;
        var d = Json.ReadFile(FilePath); if (d == null) return;
        foreach (var s in Json.Strings(d, "Watched")) Watched.Add(s);
        TagNames.AddRange(Json.Strings(d, "TagNames"));
        foreach (var kv in Obj(d, "Tags")) Tags[kv.Key] = Json.Strings(new Dictionary<string, object> { { "v", kv.Value } }, "v").ToList();
        foreach (var kv in Obj(d, "Snaps")) { var v = kv.Value as Dictionary<string, object>; if (v != null) Snaps[kv.Key] = v; }
        foreach (var kv in Obj(d, "CatOrder")) CatOrder[kv.Key] = Json.Strings(new Dictionary<string, object> { { "v", kv.Value } }, "v").ToList();
        foreach (var kv in Obj(d, "Renames")) Renames[kv.Key] = Convert.ToString(kv.Value);
        foreach (var s in Json.Strings(d, "HiddenCats")) HiddenCats.Add(s);
        foreach (var s in Json.Strings(d, "HiddenItems")) HiddenItems.Add(s);
        foreach (var kv in Obj(d, "Sorts")) Sorts[kv.Key] = Convert.ToString(kv.Value);
        foreach (var kv in Obj(d, "StartPages")) StartPages[kv.Key] = Convert.ToString(kv.Value);
        foreach (var kv in Obj(d, "Themes")) Themes[kv.Key] = Convert.ToString(kv.Value);
        LastProfile = Json.Str(d, "LastProfile") ?? "";
        foreach (var kv in Obj(d, "LastLive")) LastLive[kv.Key] = Json.Strings(new Dictionary<string, object> { { "v", kv.Value } }, "v").ToList();
        foreach (var kv in Obj(d, "EpgUrls")) EpgUrls[kv.Key] = Convert.ToString(kv.Value);
        foreach (var kv in Obj(d, "EpgMap")) EpgMap[kv.Key] = Convert.ToString(kv.Value);
        foreach (var kv in Obj(d, "EpgShifts")) EpgShifts[kv.Key] = (int)Json.Num(Obj(d, "EpgShifts"), kv.Key);
        foreach (var kv in Obj(d, "EpgHours")) EpgHours[kv.Key] = (int)Json.Num(Obj(d, "EpgHours"), kv.Key);
        if (d.ContainsKey("PreviewMs")) PreviewMs = (int)Json.Num(d, "PreviewMs");
        foreach (var kv in Obj(d, "Langs")) Langs[kv.Key] = Json.Strings(new Dictionary<string, object> { { "v", kv.Value } }, "v").ToList();
        foreach (var s in Json.Strings(d, "LangStrict")) LangStrict.Add(s);
        foreach (var s in Json.Strings(d, "LangAsked")) LangAsked.Add(s);
        foreach (var kv in Obj(d, "Meta")) { var v = kv.Value as Dictionary<string, object>; if (v != null) Meta[kv.Key] = v; }
        object rl; if (d.TryGetValue("Reminders", out rl) && rl is System.Collections.IEnumerable) foreach (var r in ((System.Collections.IEnumerable)rl).OfType<Dictionary<string, object>>()) Reminders.Add(r);
    }
    static Dictionary<string, object> Obj(Dictionary<string, object> d, string k) { return Json.Obj(d, k) ?? new Dictionary<string, object>(); }
    public static int Version;   // counts the saves: what's made ahead from this is made again (TvReady.cs)
    public static void Save() {
        System.Threading.Interlocked.Increment(ref Version);
        try {
            // only what's still used is kept: the snapshots of My list, the tags, Last watched and the hidden channels
            var used = new HashSet<string>(TvStore.Favs.Concat(Tags.Keys).Concat(LastLive.Values.SelectMany(x => x)).Concat(HiddenItems).Concat(Reminders.Select(r => Json.Str(r, "Key"))));
            foreach (var k in Snaps.Keys.Where(k => !used.Contains(k)).ToList()) Snaps.Remove(k);
            Json.WriteFile(FilePath, new Dictionary<string, object> {
                { "Watched", Watched.ToList() }, { "TagNames", TagNames.ToList() },
                { "Tags", Tags.Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key, kv => (object)kv.Value) },
                { "Snaps", Snaps.ToDictionary(kv => kv.Key, kv => (object)kv.Value) },
                { "CatOrder", CatOrder.ToDictionary(kv => kv.Key, kv => (object)kv.Value) }, { "Renames", Renames.ToDictionary(kv => kv.Key, kv => (object)kv.Value) },
                { "HiddenCats", HiddenCats.ToList() }, { "HiddenItems", HiddenItems.ToList() },
                { "Sorts", Sorts.ToDictionary(kv => kv.Key, kv => (object)kv.Value) }, { "StartPages", StartPages.ToDictionary(kv => kv.Key, kv => (object)kv.Value) }, { "Themes", Themes.ToDictionary(kv => kv.Key, kv => (object)kv.Value) }, { "LastProfile", LastProfile },
                { "LastLive", LastLive.ToDictionary(kv => kv.Key, kv => (object)kv.Value) },
                { "EpgUrls", EpgUrls.ToDictionary(kv => kv.Key, kv => (object)kv.Value) }, { "EpgMap", EpgMap.ToDictionary(kv => kv.Key, kv => (object)kv.Value) },
                { "EpgShifts", EpgShifts.ToDictionary(kv => kv.Key, kv => (object)kv.Value) }, { "EpgHours", EpgHours.ToDictionary(kv => kv.Key, kv => (object)kv.Value) },
                { "PreviewMs", PreviewMs }, { "Langs", Langs.ToDictionary(kv => kv.Key, kv => (object)kv.Value) }, { "LangStrict", LangStrict.ToList() }, { "LangAsked", LangAsked.ToList() }, { "Reminders", Reminders.ToList() }, { "Meta", Meta.Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key, kv => (object)kv.Value) } });
        } catch { }
    }

    // ---------- an item, remembered ----------
    public static void Remember(TvItem it) {
        if (it == null) return;
        Snaps[it.Key] = new Dictionary<string, object> { { "Kind", it.Kind }, { "Id", it.Id }, { "Title", it.Title }, { "Image", it.Image }, { "Backdrop", it.Backdrop }, { "Url", it.Url },
            { "Ext", it.Ext }, { "Group", it.Group }, { "Genre", it.Genre }, { "Year", it.Year }, { "Plot", it.Plot }, { "EpgId", it.EpgId }, { "Rating", it.Rating },
            { "Account", it.Account != null ? it.Account.Id : "" }, { "Season", it.Season }, { "Episode", it.Episode }, { "Lang", it.Lang } };
    }
    public static TvItem Recall(string key) {
        Dictionary<string, object> p; if (!Snaps.TryGetValue(key, out p)) return null;
        var acc = TvStore.Accounts.FirstOrDefault(a => a.Id == Json.Str(p, "Account"));
        return new TvItem { Kind = Json.Str(p, "Kind"), Id = Json.Str(p, "Id"), Title = Json.Str(p, "Title"), Image = Json.Str(p, "Image"), Backdrop = Json.Str(p, "Backdrop"), Url = Json.Str(p, "Url"),
            Ext = Json.Str(p, "Ext"), Group = Json.Str(p, "Group"), Genre = Json.Str(p, "Genre"), Year = Json.Str(p, "Year"), Plot = Json.Str(p, "Plot"), EpgId = Json.Str(p, "EpgId"), Rating = Json.Str(p, "Rating"),
            Account = acc, Season = (int)Json.Num(p, "Season"), Episode = (int)Json.Num(p, "Episode"), Lang = Json.Str(p, "Lang") ?? TvTidy.LangOfTitle(Json.Str(p, "Title")) ?? TvTidy.LangOfGroup(Json.Str(p, "Group")) };
    }
    // the remembered items for some keys, of this profile only
    public static List<TvItem> Items(IEnumerable<string> keys, TvAccount profile) {
        string pre = profile != null ? profile.Id + ":" : null;
        return keys.Where(k => pre != null ? k.StartsWith(pre) : !TvStore.Accounts.Any(a => k.StartsWith(a.Id + ":"))).Select(Recall).Where(x => x != null).ToList();
    }
    public static string Profile(TvAccount a) { return a != null ? a.Id : "free"; }

    // ---------- watched ----------
    public static bool IsWatched(TvItem it) { return it != null && Watched.Contains(it.Key); }
    public static void SetWatched(TvItem it, bool on) {
        if (it == null) return;
        if (on) { Watched.Add(it.Key); TvStore.Progress.Remove(it.Key); } else Watched.Remove(it.Key);
        Save(); TvStore.Save();
    }

    // ---------- tags ----------
    public static List<string> TagsOf(TvItem it) { List<string> l; return it != null && Tags.TryGetValue(it.Key, out l) ? l : new List<string>(); }
    public static void ToggleTag(TvItem it, string tag) {
        List<string> l; if (!Tags.TryGetValue(it.Key, out l)) Tags[it.Key] = l = new List<string>();
        if (!l.Remove(tag)) { l.Add(tag); Remember(it); if (!TagNames.Contains(tag)) TagNames.Add(tag); }
        Save();
    }
    public static List<TvItem> Tagged(string tag, TvAccount profile) { return Items(Tags.Where(kv => kv.Value.Contains(tag)).Select(kv => kv.Key), profile); }

    // ---------- last watched channels ----------
    public static void AddLastLive(TvItem ch, TvAccount profile) {
        if (ch == null || ch.Kind != "live") return;
        string p = Profile(profile); List<string> l; if (!LastLive.TryGetValue(p, out l)) LastLive[p] = l = new List<string>();
        l.Remove(ch.Key); l.Insert(0, ch.Key); if (l.Count > 20) l.RemoveRange(20, l.Count - 20);
        Remember(ch); Save();
    }
    public static List<TvItem> LastLiveOf(TvAccount profile) { List<string> l; return LastLive.TryGetValue(Profile(profile), out l) ? l.Select(Recall).Where(x => x != null).ToList() : new List<TvItem>(); }

    // ---------- a page's categories: order, names, hidden, sorting ----------
    public static string Scope(TvAccount a, string mode) { return Profile(a) + "|" + mode; }
    public static string NameOf(string scope, string cat) { string n; return Renames.TryGetValue(scope + "|" + cat, out n) ? n : cat; }
    public static bool CatHidden(string scope, string cat) { return HiddenCats.Contains(scope + "|" + cat); }
    public static string SortOf(string scope) { string s; return Sorts.TryGetValue(scope, out s) ? s : "default"; }
    public static List<string> Order(string scope, List<string> natural) {
        List<string> o; if (!CatOrder.TryGetValue(scope, out o)) return natural;
        return natural.OrderBy(n => { int i = o.IndexOf(n); return i < 0 ? 100000 + natural.IndexOf(n) : i; }).ToList();
    }
    public static void Move(string scope, List<string> current, string cat, int by) {
        var l = current.ToList(); int i = l.IndexOf(cat); if (i < 0) return;
        int j = by == int.MinValue ? 0 : Math.Max(0, Math.Min(l.Count - 1, i + by));
        l.RemoveAt(i); l.Insert(j, cat); CatOrder[scope] = l; Save();
    }
    public static void ResetScope(string scope) {
        CatOrder.Remove(scope); Sorts.Remove(scope);
        foreach (var k in Renames.Keys.Where(k => k.StartsWith(scope + "|")).ToList()) Renames.Remove(k);
        HiddenCats.RemoveWhere(k => k.StartsWith(scope + "|"));
        Save();
    }
    public static IEnumerable<TvItem> Sorted(IEnumerable<TvItem> l, string sort) {
        switch (sort) {
            case "az": return l.OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase);
            case "new": return l.OrderByDescending(x => x.Year ?? "");
            case "rating": return l.OrderByDescending(x => { double r; return double.TryParse(x.Rating ?? "", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r) ? r : 0; });
        }
        return l;
    }
    public static string SortName(string s) { return s == "az" ? "A to Z" : s == "new" ? "Newest first" : s == "rating" ? "Best rated first" : "As the provider gives it"; }
    public static int ShiftOf(string profile) { int v; return EpgShifts.TryGetValue(profile, out v) ? v : 0; }
    public static int HoursOf(string profile) { int v; return EpgHours.TryGetValue(profile, out v) && v > 0 ? v : 6; }
    public static bool HasReminder(TvItem ch, DateTime start) { return ch != null && Reminders.Any(r => Json.Str(r, "Key") == ch.Key && Json.Str(r, "Start") == start.ToString("o")); }
    public static void ToggleReminder(TvItem ch, TvFree.Prog p) {
        var have = Reminders.FirstOrDefault(r => Json.Str(r, "Key") == ch.Key && Json.Str(r, "Start") == p.Start.ToString("o"));
        if (have != null) Reminders.Remove(have);
        else { Reminders.Add(new Dictionary<string, object> { { "Key", ch.Key }, { "Title", p.Title ?? "" }, { "Channel", ch.Title ?? "" }, { "Start", p.Start.ToString("o") }, { "Account", ch.Account != null ? ch.Account.Id : "" } }); Remember(ch); }
        Save();
    }
    public static string NextSort(string s) { return s == "default" ? "az" : s == "az" ? "new" : s == "new" ? "rating" : "default"; }
}
}
