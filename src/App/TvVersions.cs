// TV & Movies: no dubs where it can be helped.
//   Original audio first   a film's or series' country (from Cinemeta: Parasite, South Korea) gives its own language;
//                          when the file has an audio track in it, that one plays (then the profile's usual languages).
//                          Settings key TvOriginalAudio = off turns it off. A file with only a dub stays a dub: the
//                          sound is part of what the provider sends.
//   Versions               a provider often has the same film or series more than once, in different language
//                          categories ("KR · Parasite", "EN · Parasite", "TA · Parasite"). The page shows "Versions" when
//                          there are others; each says its language and whether it's the original or a dub.
// Language tags like TA (Tamil) and HI (Hindi) count as Indian (TvTidy.cs), so Languages can hide those dubs.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Controls;

namespace RecompLauncher {

partial class Shell {
    // a country's own language: for the player (mpv's alang) and as the launcher's language tag (TvTidy)
    static readonly string[][] TvCountryLangs = {
        // country, mpv languages, tag
        new[] { "south korea", "ko,kor", "AS" }, new[] { "korea", "ko,kor", "AS" }, new[] { "japan", "ja,jpn", "AS" }, new[] { "china", "zh,chi,zho,cmn", "AS" },
        new[] { "hong kong", "yue,zh,chi,zho", "AS" }, new[] { "taiwan", "zh,chi,zho,cmn", "AS" }, new[] { "thailand", "th,tha", "AS" }, new[] { "philippines", "tl,fil,tgl", "AS" },
        new[] { "india", "hi,hin,ta,tam,te,tel", "IN" }, new[] { "france", "fr,fra,fre", "FR" }, new[] { "germany", "de,deu,ger", "DE" }, new[] { "west germany", "de,deu,ger", "DE" },
        new[] { "austria", "de,deu,ger", "DE" }, new[] { "italy", "it,ita", "IT" }, new[] { "spain", "es,spa", "ES" }, new[] { "mexico", "es,spa", "ES" }, new[] { "argentina", "es,spa", "ES" },
        new[] { "colombia", "es,spa", "ES" }, new[] { "chile", "es,spa", "ES" }, new[] { "brazil", "pt,por", "PT" }, new[] { "portugal", "pt,por", "PT" },
        new[] { "netherlands", "nl,nld,dut", "NL" }, new[] { "belgium", "nl,nld,dut,fr,fra,fre", "NL" }, new[] { "sweden", "sv,swe", "SE" }, new[] { "denmark", "da,dan", "SE" },
        new[] { "norway", "no,nor,nob,nno", "SE" }, new[] { "finland", "fi,fin", "SE" }, new[] { "iceland", "is,isl,ice", "SE" }, new[] { "poland", "pl,pol", "PL" },
        new[] { "russia", "ru,rus", "RU" }, new[] { "soviet union", "ru,rus", "RU" }, new[] { "ukraine", "uk,ukr", "UA" }, new[] { "turkey", "tr,tur", "TR" }, new[] { "türkiye", "tr,tur", "TR" },
        new[] { "iran", "fa,per,fas", "IR" }, new[] { "egypt", "ar,ara", "AR" }, new[] { "lebanon", "ar,ara", "AR" }, new[] { "greece", "el,ell,gre", "GR" },
        new[] { "hungary", "hu,hun", "HU" }, new[] { "czech republic", "cs,ces,cze", "CZ" }, new[] { "czechia", "cs,ces,cze", "CZ" }, new[] { "romania", "ro,ron,rum", "RO" },
        new[] { "united states", "en,eng", "EN" }, new[] { "usa", "en,eng", "EN" }, new[] { "united kingdom", "en,eng", "EN" }, new[] { "uk", "en,eng", "EN" },
        new[] { "canada", "en,eng", "EN" }, new[] { "australia", "en,eng", "EN" }, new[] { "ireland", "en,eng", "EN" }, new[] { "new zealand", "en,eng", "EN" } };
    static string[] TvCountryLang(TvItem it) {
        if (it != null && it.Kind == "episode") it = new TvItem { Kind = "series", Title = it.Group, Account = it.Account };   // an episode: its series
        var m = Cinemeta.Cached(it); string c = m != null ? (Json.Str(m, "country") ?? "") : "";
        if (c.Length == 0) return null;
        string first = c.Split(',')[0].Trim().ToLowerInvariant();
        return TvCountryLangs.FirstOrDefault(x => x[0] == first);
    }

    // ---------- original audio first (TvLoad, after the profile's own languages are set) ----------
    void TvOriginalAudio(IntPtr ctx, TvItem it) {
        if (ctx == IntPtr.Zero || it == null || it.Kind == "live" || Settings.Get("TvOriginalAudio") == "off") return;
        var cl = TvCountryLang(it); if (cl == null) return;
        TvPlayerPrefs.Load();
        string mine = TvPlayerPrefs.Get(TvPlayerPrefs.Alang, TvMine.Profile(tvAcc), ""); if (mine.Length == 0) mine = "nl,nld,dut,en,eng";
        Mpv.Set(ctx, "alang", cl[1] + "," + mine);
    }

    // ---------- versions: the same film or series elsewhere at the provider ----------
    readonly Dictionary<string, Dictionary<string, List<TvItem>>> tvVersionIndex = new Dictionary<string, Dictionary<string, List<TvItem>>>();
    readonly object tvVersionGate = new object();
    static string TvVersionKey(TvItem it, out string year) {
        string y; string t = Cinemeta.CleanTitle(it.Title, out y);
        year = y ?? Regex.Match(it.Year ?? "", @"(19|20)\d\d").Value; if (year == "") year = null;
        return (it.Kind == "series" ? "s|" : "m|") + Regex.Replace(TvFold(t), @"[^a-z0-9]", "");
    }
    // the other copies (null while the provider's list is being sorted; empty when there are none)
    List<TvItem> TvVersionsOf(TvItem it, Action ready) {
        if (it == null || it.Account == null || !(it.Kind == "movie" || it.Kind == "series")) return new List<TvItem>();
        var a = it.Account; string ik = a.Id + "|" + (a.Movies != null ? a.Movies.Count : 0) + "|" + (a.Series != null ? a.Series.Count : 0);
        Dictionary<string, List<TvItem>> idx;
        lock (tvVersionGate) tvVersionIndex.TryGetValue(ik, out idx);
        if (idx == null) {
            // sorted once per provider list, in the background
            ThreadPool.QueueUserWorkItem(_ => {
                var d = new Dictionary<string, List<TvItem>>();
                foreach (var x in (a.Movies ?? new List<TvItem>()).Concat(a.Series ?? new List<TvItem>())) {
                    if (string.IsNullOrEmpty(x.Title)) continue;
                    string y, k = TvVersionKey(x, out y); if (k.Length < 4) continue;
                    List<TvItem> l; if (!d.TryGetValue(k, out l)) d[k] = l = new List<TvItem>(); l.Add(x);
                }
                lock (tvVersionGate) tvVersionIndex[ik] = d;
                if (ready != null) W.Dispatcher.BeginInvoke(ready);
            });
            return null;
        }
        string year, key = TvVersionKey(it, out year);
        List<TvItem> same; if (!idx.TryGetValue(key, out same)) return new List<TvItem>();
        return same.Where(x => x.Key != it.Key).Where(x => { string y; TvVersionKey(x, out y); int a1, b1; return year == null || y == null || !int.TryParse(year, out a1) || !int.TryParse(y, out b1) || Math.Abs(a1 - b1) <= 1; })
            .Where(TvAllowed).Take(12).ToList();
    }
    // what a copy is: its language, and whether that's the original
    string TvVersionLabel(TvItem v, string[] orig) {
        string lang = v.Lang != null ? TvTidy.NameOfLang(v.Lang) : "Language not marked";
        if (v.Lang != null && orig != null) lang += v.Lang == orig[2] ? "  ·  Original" : "  ·  Dubbed";
        return lang;
    }
    ListPage tvVersionPage;
    void TvOpenVersions(TvItem it) {
        var others = TvVersionsOf(it, null) ?? new List<TvItem>();
        var orig = TvCountryLang(it);
        var all = new List<TvItem> { it }; all.AddRange(others);
        // the original first, then the rest as the provider has them
        all = all.OrderBy(v => orig != null && v.Lang == orig[2] ? 0 : v.Lang == null ? 2 : 1).ToList();
        if (tvVersionPage == null) {
            tvVersionPage = MakeListPage("TvVersions", "VERSIONS", "", 640, null);
            tvVersionPage.Layer.SetValue(Panel.ZIndexProperty, 900);
        }
        var p = tvVersionPage; var rows = new List<ListRow>();
        p.Title.Text = it.Title;
        p.Sub.Text = orig != null ? "Made in " + Json.Str(Cinemeta.Cached(it), "country") + ": the original is in " + TvTidy.NameOfLang(orig[2]) + ". A copy's sound is what your provider sends; the player picks the original language when the file has it."
            : "Your provider has this more than once. Pick the one to watch.";
        foreach (var v in all) {
            var vv = v; bool cur = v.Key == it.Key;
            rows.Add(new ListRow { Glyph = "", C1 = orig != null && v.Lang == orig[2] ? "#3fb8a0" : "#33424d", C2 = orig != null && v.Lang == orig[2] ? "#2f8fe0" : "#33424d",
                Title = TvVersionLabel(v, orig) + (cur ? "   (this one)" : ""), Text = (v.Group ?? "") + (string.IsNullOrEmpty(v.Year) ? "" : "  ·  " + v.Year),
                Do = () => { CloseListPage(); if (!cur) TvOpenDetail(vv); } });
        }
        p.Fill = () => FillRows(p, rows, "");
        OpenListPage(p);
    }
}
}
