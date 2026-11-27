// TV & Movies: a provider's lists, tidied.
//   Language tags   "NL ◉ VIDEOLAND", "|PL| TVP 1", "EN - BBC One", "UK: Sky News": the tag is read (NL, EN, PL...),
//                   kept with the item, and taken off its name; SHOUTING names are written normally
//   Same name twice categories with the same name in two languages get "· NL" / "· EN" after it
//   Languages       each profile can show only the languages you pick (Manage profiles → the profile → Languages)
//   Posters         films and series the provider sends no picture for get one from Cinemeta, on the tile itself
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RecompLauncher {

static class TvTidy {
    static readonly HashSet<string> Codes = new HashSet<string>("TA TAM TE TEL HI HIN ML MAL KN KAN BN BEN NL NLD BE EN ENG UK US USA GB CA AU IE PL POL DE GER AT CH FR FRA ES SPA IT ITA PT BR TR TUR AR ARA RU RUS UA RO BG HR RS BA AL GR SE NO DK FI HU CZ SK IN PK AF IR KU LAT EX MX KR JP CN TH ASIA".Split(' '));
    static readonly Regex Prefix = new Regex(@"^\W*?\|?\s*([A-Z0-9]{2,4})\s*\|?\s*(?:[|:\-–★☆◉●•►▶»#*~❖✦✪]+\s*|\s+(?=[\p{So}\p{Sm}]))");
    static readonly Regex Suffix = new Regex(@"(?:[\(\[|]\s*([A-Z]{2,3})\s*[\)\]|]|[-–|:]\s*([A-Z]{2,3}))\s*$");
    // language and country words in a category's name ("FRENCH FILMS", "Arabic Movies", "EX-YU"): for categories only,
    // so a film called "The French Connection" stays where it is
    static readonly string[][] Words = {
        new[] { "NL", @"\b(dutch|nederlands|nederland|holland|vlaams|flemish)\b" }, new[] { "EN", @"\b(english|british|american)\b" },
        new[] { "PL", @"\b(polish|polska|polskie|polski)\b" }, new[] { "FR", @"\b(french|france|fran[cç]ais|francais|vf|vostfr)\b" },
        new[] { "DE", @"\b(german|germany|deutsch|deutschland)\b" }, new[] { "TR", @"\b(turk\w*|t[uü]rk\w*)\b" }, new[] { "AR", @"\b(arab\w*|arabic|osn|mbc)\b" },
        new[] { "ES", @"\b(spain|spanish|espa[nñ]\w*|latino|latin|castellano)\b" }, new[] { "IT", @"\b(italia\w*|italian)\b" },
        new[] { "PT", @"\b(portug\w*|brasil|brazil\w*)\b" }, new[] { "RU", @"\b(russia\w*|russian)\b" }, new[] { "IN", @"\b(india\w*|hindi|tamil|telugu|malayalam|punjabi|bollywood|bangla\w*)\b" },
        new[] { "PK", @"\b(pakistan\w*|urdu)\b" }, new[] { "AF", @"\b(africa\w*|nigeria\w*)\b" }, new[] { "KU", @"\b(kurd\w*)\b" }, new[] { "AL", @"\b(albania\w*|shqip\w*)\b" },
        new[] { "EX", @"\b(ex[- ]?yu|balkan\w*|serbia\w*|croatia\w*|bosnia\w*|slovenia\w*)\b" }, new[] { "RO", @"\b(romania\w*)\b" }, new[] { "BG", @"\b(bulgaria\w*)\b" },
        new[] { "GR", @"\b(greek|greece)\b" }, new[] { "SE", @"\b(swed\w*|nordic|scandi\w*|norw\w*|danish|denmark|finnish|finland)\b" }, new[] { "HU", @"\b(hungar\w*)\b" },
        new[] { "CZ", @"\b(czech\w*|slovak\w*)\b" }, new[] { "IR", @"\b(persian|iran\w*|farsi)\b" }, new[] { "AS", @"\b(asia\w*|chinese|china|japan\w*|korea\w*|thai|filipino|pinoy|vietnam\w*|anime|[kcj]-?drama\w*|dorama|donghua|manhwa)\b" } };
    // a writing system that isn't Latin tells the language too
    static string ScriptOf(string s) {
        if (string.IsNullOrEmpty(s)) return null;
        if (Regex.IsMatch(s, @"[\u0600-\u06FF]")) return "AR"; if (Regex.IsMatch(s, @"[\u0400-\u04FF]")) return "RU"; if (Regex.IsMatch(s, @"[\u0370-\u03FF]")) return "GR";
        if (Regex.IsMatch(s, @"[\u0590-\u05FF]")) return "HE"; if (Regex.IsMatch(s, @"[\u0900-\u0DFF]")) return "IN"; if (Regex.IsMatch(s, @"[\u0E00-\u0E7F\u3040-\u30FF\u4E00-\u9FFF\uAC00-\uD7AF]")) return "AS";
        return null;
    }
    static string Norm(string c) {
        switch (c) {
            case "UK": case "US": case "USA": case "GB": case "CA": case "AU": case "IE": case "ENG": return "EN";
            // Indian languages (Tamil, Telugu, Hindi, Malayalam, Kannada, Bengali): one choice in Languages, so their dubs can be hidden
            case "TA": case "TAM": case "TE": case "TEL": case "HI": case "HIN": case "ML": case "MAL": case "KN": case "KAN": case "BN": case "BEN": return "IN";
            case "NLD": return "NL"; case "POL": return "PL"; case "GER": return "DE"; case "FRA": return "FR"; case "SPA": return "ES";
            case "ITA": return "IT"; case "TUR": return "TR"; case "ARA": return "AR"; case "RUS": return "RU"; case "LAT": case "MX": return "ES"; case "BR": return "PT"; case "KR": case "JP": case "CN": case "TH": case "ASIA": return "AS";
        }
        return c;
    }
    // a category's language: its tag at the start or the end, a language word in it, or its script
    public static string LangOfGroup(string s) {
        var l = LangOf(s); if (l != null) return l;
        var m = Suffix.Match(s ?? ""); string c = m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : null;
        if (c != null && Codes.Contains(c)) return Norm(c);
        foreach (var w in Words) if (Regex.IsMatch(s ?? "", w[1], RegexOptions.IgnoreCase)) return w[0];
        return ScriptOf(s);
    }
    // a title's language: only its tag or its script
    public static string LangOfTitle(string s) { return LangOf(s) ?? ScriptOf(s); }
    // the language tag at the start of a name, as one code per language (EN for UK/US/GB...)
    public static string LangOf(string s) {
        var m = Prefix.Match(s ?? ""); if (!m.Success || !Codes.Contains(m.Groups[1].Value)) return null;
        switch (m.Groups[1].Value) {
            case "UK": case "US": case "USA": case "GB": case "CA": case "AU": case "IE": case "ENG": return "EN";
            case "NLD": return "NL"; case "POL": return "PL"; case "GER": return "DE"; case "FRA": return "FR"; case "SPA": return "ES";
            case "ITA": return "IT"; case "TUR": return "TR"; case "ARA": return "AR"; case "RUS": return "RU";
        }
        return Norm(m.Groups[1].Value);
    }
    public static string NameOfLang(string code) {
        switch (code) {
            case "NL": return "Dutch"; case "EN": return "English"; case "PL": return "Polish"; case "BE": return "Belgian"; case "DE": return "German"; case "FR": return "French";
            case "ES": return "Spanish"; case "IT": return "Italian"; case "PT": return "Portuguese"; case "TR": return "Turkish"; case "AR": return "Arabic"; case "RU": return "Russian";
            case "IN": return "Indian"; case "PK": return "Pakistani"; case "AF": return "African"; case "KU": return "Kurdish"; case "AL": return "Albanian"; case "EX": return "Balkan (Ex-Yu)";
            case "RO": return "Romanian"; case "BG": return "Bulgarian"; case "GR": return "Greek"; case "SE": return "Scandinavian"; case "HU": return "Hungarian"; case "CZ": return "Czech / Slovak";
            case "IR": return "Persian"; case "AS": return "Asian (K-drama, anime, Chinese drama)"; case "HE": return "Hebrew"; case "UA": return "Ukrainian"; case "HR": return "Croatian"; case "RS": return "Serbian";
        }
        return code;
    }
    // the name without its tag and symbols, written normally
    public static string Clean(string s) {
        if (string.IsNullOrEmpty(s)) return s;
        string t = s; var m = Prefix.Match(t);
        if (m.Success) t = t.Substring(m.Length);
        t = Regex.Replace(t, @"^[\s\p{So}\p{Sm}|:\-–•*#~]+", "").Trim();
        if (t.Length == 0) return s.Trim();
        // SHOUTING written normally; short words stay as they are (TVP, NPO, HD, BBC)
        if (t.Length > 3 && t.Any(char.IsLetter) && !t.Any(char.IsLower))
            t = Regex.Replace(t, @"\p{L}{4,}", w => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w.Value.ToLowerInvariant()));
        return t;
    }
    // a whole provider tidied once its lists are in
    public static void Account(TvAccount a) {
        var all = (a.Live ?? new List<TvItem>()).Concat(a.Movies ?? new List<TvItem>()).Concat(a.Series ?? new List<TvItem>()).ToList();
        var rawGroups = all.Select(x => x.Group).Where(g => !string.IsNullOrEmpty(g)).Distinct().ToList();
        foreach (var x in all) {
            x.Lang = LangOfTitle(x.Title) ?? LangOfGroup(x.Group); x.Title = Clean(x.Title);
            if (x.Episodes != null) foreach (var e in x.Episodes) { e.Lang = x.Lang; e.Title = Clean(e.Title); e.Group = x.Title; }
        }
        // categories: clean names; the same name in two languages gets its language after it
        var clean = rawGroups.ToDictionary(r => r, Clean);
        var twice = new HashSet<string>(clean.GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase).Where(g => g.Select(kv => LangOfGroup(kv.Key)).Distinct().Count() > 1).SelectMany(g => g.Select(kv => kv.Key)));
        var final = rawGroups.ToDictionary(r => r, r => clean[r] + (twice.Contains(r) && LangOfGroup(r) != null ? "  ·  " + LangOfGroup(r) : ""));
        foreach (var x in all) { string f; if (x.Group != null && final.TryGetValue(x.Group, out f)) x.Group = f; }
        foreach (var cats in new[] { a.LiveCats, a.MovieCats, a.SeriesCats }) {
            if (cats == null) continue;
            foreach (var k in cats.Keys.ToList()) { string f; cats[k] = final.TryGetValue(cats[k], out f) ? f : Clean(cats[k]); }
        }
    }
    // the languages a provider has, with how much of each
    public static List<KeyValuePair<string, int>> Langs(TvAccount a) {
        var all = (a.Live ?? new List<TvItem>()).Concat(a.Movies ?? new List<TvItem>()).Concat(a.Series ?? new List<TvItem>());
        return all.Where(x => x.Lang != null).GroupBy(x => x.Lang).OrderByDescending(g => g.Count()).Select(g => new KeyValuePair<string, int>(g.Key, g.Count())).ToList();
    }
}

partial class Shell {
    // ---------- which languages a profile shows ----------
    bool TvLangOk(TvItem x) {
        if (x == null || x.Account == null) return true;
        List<string> l; if (!TvMine.Langs.TryGetValue(x.Account.Id, out l) || l.Count == 0) return true;
        if (x.Lang == null) return !TvMine.LangStrict.Contains(x.Account.Id);
        return l.Contains(x.Lang);
    }
    TvAccount tvLangAcc;
    ListRow TvLangRow(TvAccount a) {
        List<string> l; bool some = TvMine.Langs.TryGetValue(a.Id, out l) && l.Count > 0;
        return new ListRow { Glyph = "", C1 = "#2f8fe0", C2 = "#2f8fe0", Title = "Languages: " + (some ? string.Join(", ", l.Select(TvTidy.NameOfLang)) : "all"), Text = "Only the channels, films and series in the languages you pick",
            Do = () => { tvLangAcc = TvStore.Accounts.FirstOrDefault(x => x.Id == a.Id) ?? a; tvActMode = "langs"; TvOpenActions(); if (!tvLangAcc.Loaded) { var acc = tvLangAcc; ThreadPool.QueueUserWorkItem(_ => { try { TvSource.LoadAccount(acc); } catch { } W.Dispatcher.BeginInvoke(new Action(() => { if (ListPageOpen && lpOpen == tvActPage && tvActMode == "langs") TvFillActions(); })); }); } } };
    }
    bool TvFillLangs(ListPage p, List<ListRow> rows) {
        if (tvActMode != "langs") return false;
        var a = tvLangAcc; List<string> on; if (!TvMine.Langs.TryGetValue(a.Id, out on)) TvMine.Langs[a.Id] = on = new List<string>();
        p.Title.Text = a.Name + "  •  Languages";
        p.Sub.Text = !a.Loaded ? "Looking at what " + a.Name + " has..." : on.Count == 0 ? "Everything shows now. Pick the languages you want; the rest stays out of sight (nothing is removed from your provider)." : "Showing: " + string.Join(", ", on.Select(TvTidy.NameOfLang)) + ". Things without a language tag always show.";
        if (a.Loaded) {
            foreach (var kv in TvTidy.Langs(a)) {
                string code = kv.Key; bool sel = on.Contains(code);
                rows.Add(new ListRow { Glyph = sel ? "" : "", C1 = sel ? "#3fb8a0" : "#33424d", C2 = sel ? "#2f8fe0" : "#33424d", Title = TvTidy.NameOfLang(code) + (TvTidy.NameOfLang(code) != code ? "  (" + code + ")" : ""), Text = kv.Value + " channels, films and series" + (sel ? "  •  shown" : ""),
                    Do = () => { if (!on.Remove(code)) on.Add(code); TvMine.Save(); TvFillActions(); } });
            }
            int none = (a.Live ?? new List<TvItem>()).Concat(a.Movies ?? new List<TvItem>()).Concat(a.Series ?? new List<TvItem>()).Count(x => x.Lang == null);
            bool strict = TvMine.LangStrict.Contains(a.Id);
            rows.Add(new ListRow { Glyph = strict ? "\uE7B3" : "\uE890", C1 = "#c28a1e", C2 = "#e0b33c", Title = "Without a language tag: " + (strict ? "hidden" : "shown"), Text = none + " channels, films and series have no language in their name or category  •  pick to change",
                Do = () => { if (!TvMine.LangStrict.Remove(a.Id)) TvMine.LangStrict.Add(a.Id); TvMine.Save(); TvFillActions(); } });
            rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "All languages", Do = () => { on.Clear(); TvMine.Save(); TvFillActions(); } });
        }
        rows.Add(new ListRow { Glyph = "", C1 = "#3fb8a0", C2 = "#2f8fe0", Title = "Done", Do = () => { CloseListPage(); if (TvOpen && !TvProfilesOpen) TvShowPage(tvPage); } });
        return true;
    }

    // ---------- posters from Cinemeta: the standard for a provider's films and series ----------
    // (the provider's own picture stays when Cinemeta doesn't know the title)
    static readonly SemaphoreSlim tvArtGate = new SemaphoreSlim(4);
    void TvArtLater(TvItem it, Grid g, TextBlock name, int px) {
        if (it == null || !(it.Kind == "movie" || it.Kind == "series")) return;
        if (it.Account == null && !string.IsNullOrEmpty(it.Image)) return;   // the free films already have theirs
        string had = it.Image;
        ThreadPool.QueueUserWorkItem(_ => {
            Dictionary<string, object> m = null;
            tvArtGate.Wait();
            try { m = Cinemeta.Find(it); if (m != null) Cinemeta.Enrich(it); } catch { } finally { tvArtGate.Release(); }
            string poster = m != null ? Json.Str(m, "poster") : null;
            if (!string.IsNullOrEmpty(poster)) { poster = poster.Replace("/poster/small/", "/poster/medium/"); it.Image = poster; }
            if (string.IsNullOrEmpty(it.Image) || it.Image == had) return;
            W.Dispatcher.BeginInvoke(new Action(() => {
                // the provider's picture on the tile gets Cinemeta's, or the tile gets its first picture
                var img = g.Children.OfType<Image>().FirstOrDefault();
                if (img == null) { img = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 }; RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality); g.Children.Insert(Math.Min(g.Children.Count, 2), img); }
                TvLoadImage(img, it.Image, px, () => { if (name != null) name.Visibility = Visibility.Collapsed; });
            }));
        });
    }
}
}
