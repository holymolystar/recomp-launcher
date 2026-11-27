// TV & Movies: subtitles, made easy.
//   Dutch by default   a film or episode without Dutch subtitles in it gets them from OpenSubtitles by itself, once it
//   English extra      starts, and English ones too (in the Subtitles menu, not shown unless picked; shown when there's
//                      no Dutch). With your OpenSubtitles account (a profile's settings); matched by the film's IMDb
//                      number from Cinemeta, the most downloaded first. Kept, so next time they're there at once and
//                      don't count against the day's downloads. Settings key TvAutoSubs = off: never.
//   Timing             the Timing button (or X in the Subtitles menu, T or Z / X on the keyboard): a tenth or a whole
//                      second earlier or later, sync to a line as it's spoken, or back in sync; the offset shows all
//                      along. Remembered per film or episode, with its subtitles.
// Kept in subtitles\ next to the launcher (the files, named with their language) and subtitles\subs.json: item key -> per
// language (File_nl, Name_nl, File_en, Name_en) and the timing (Delay). Downloaded once; every time after, they're just there.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace RecompLauncher {

static class TvSubStore {
    // a folder of their own next to the launcher (not in cache\, so nothing that tidies up the cache takes them)
    static string Dir { get { var d = Path.Combine(Paths.Root, "subtitles"); Directory.CreateDirectory(d); return d; } }
    static string FilePath { get { return Path.Combine(Dir, "subs.json"); } }
    static Dictionary<string, object> data; static readonly object gate = new object();
    static Dictionary<string, object> All() { if (data == null) data = Json.ReadFile(FilePath) ?? new Dictionary<string, object>(); return data; }
    public static Dictionary<string, object> Of(string key) { lock (gate) { object v; return All().TryGetValue(key ?? "", out v) ? v as Dictionary<string, object> : null; } }
    public static void Put(string key, string field, object value) {
        lock (gate) {
            var d = Of(key); if (d == null) { d = new Dictionary<string, object>(); All()[key] = d; }
            d[field] = value;
            try { Json.WriteFile(FilePath, All()); } catch { }
        }
    }
    // "12345.nl.srt": the language in the name, so the player knows it (and puts Dutch on by itself next time)
    public static string NewFile(string id, string lang, string ext) {
        string l = Regex.Replace((lang ?? "").ToLowerInvariant(), "[^a-z-]", ""); 
        return Path.Combine(Dir, id + (l.Length > 0 ? "." + l : "") + (string.IsNullOrEmpty(ext) ? ".srt" : ext));
    }
}

partial class Shell {
    bool tvSubHintShown;

    // ---------- before it plays: the kept subtitles and their timing ----------
    void TvSubsBeforeLoad(IntPtr ctx, TvItem it) {
        var s = it != null && it.Kind != "live" ? TvSubStore.Of(it.Key) : null;
        // the kept Dutch and English files come with it; the profile's languages (Dutch first) pick which shows
        var files = s == null ? new List<string>() : TvSubLangs.Select(l => Json.Str(s, "File_" + l)).Where(f => !string.IsNullOrEmpty(f) && File.Exists(f)).ToList();
        Mpv.Set(ctx, "sub-files", string.Join(";", files));
        double delay = s != null ? Json.Num(s, "Delay") : 0;
        Mpv.Set(ctx, "sub-delay", delay.ToString("0.0##", CultureInfo.InvariantCulture));
    }

    // ---------- once it plays: Dutch (shown) and English (extra) when the film doesn't have them ----------
    static readonly string[] TvSubLangs = { "nl", "en" };
    static readonly Dictionary<string, string> TvSubLangName = new Dictionary<string, string> { { "nl", "Nederlands" }, { "en", "English" } };
    void TvAutoSubs(TvItem it) {
        if (it == null || it.Kind == "live" || tvMpv == IntPtr.Zero || Settings.Get("TvAutoSubs") == "off") return;
        TvPlayerPrefs.Load();
        if (TvPlayerPrefs.Get(TvPlayerPrefs.Slang, TvMine.Profile(tvAcc), "auto") == "off") return;
        // what the file (and the kept ones) already have
        var have = new HashSet<string>(); bool anyShown = false;
        int count = (int)Math.Max(0, Mpv.Num(tvMpv, "track-list/count"));
        for (int i = 0; i < count; i++) {
            if (Mpv.Get(tvMpv, "track-list/" + i + "/type") != "sub") continue;
            string lang = (Mpv.Get(tvMpv, "track-list/" + i + "/lang") ?? "").ToLowerInvariant();
            if (Regex.IsMatch(lang, "^(nl|nld|dut)$")) have.Add("nl"); else if (Regex.IsMatch(lang, "^(en|eng)$")) have.Add("en");
            if (Mpv.Get(tvMpv, "track-list/" + i + "/selected") == "yes") anyShown = true;
        }
        var kept = TvSubStore.Of(it.Key);
        foreach (var l in TvSubLangs) if (kept != null && File.Exists(Json.Str(kept, "File_" + l) ?? "")) have.Add(l);
        var want = TvSubLangs.Where(l => !have.Contains(l)).ToList();
        if (want.Count == 0) return;
        if (string.IsNullOrEmpty(TvPlayerPrefs.OsKey)) {
            if (!tvSubHintShown) {
                tvSubHintShown = true;
                string what = want.Count == 2 ? "Dutch or English" : want[0] == "nl" ? "Dutch" : "English";
                Mpv.Command(tvMpv, "show-text", "No " + what + " subtitles in this one. To get them by themselves: Manage profiles → your profile → Player → OpenSubtitles.", "6000");
            }
            return;
        }
        new Thread(() => {
            var got = new Dictionary<string, string[]>();   // language -> file, name
            try {
                var res = TvOsFind(it, string.Join(",", want));
                foreach (var l in want) {
                    var best = res.Where(r => (Json.Str(Json.Obj(r, "attributes"), "language") ?? "").StartsWith(l, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(r => Json.Num(Json.Obj(r, "attributes"), "download_count")).FirstOrDefault();
                    if (best == null) continue;
                    var a = Json.Obj(best, "attributes");
                    var f = ((a.ContainsKey("files") ? a["files"] as System.Collections.IEnumerable : null) ?? new object[0]).Cast<object>().OfType<Dictionary<string, object>>().FirstOrDefault();
                    if (f == null) continue;
                    try { got[l] = new[] { TvOsDownload(Json.Str(f, "file_id"), l), TvSubLangName[l] + "  –  " + (Json.Str(a, "release") ?? "OpenSubtitles") }; } catch { }
                }
            } catch { }
            W.Dispatcher.BeginInvoke(new Action(() => {
                if (got.Count == 0 || tvMpv == IntPtr.Zero || tvPlaying != it) return;
                foreach (var l in TvSubLangs) {
                    string[] g; if (!got.TryGetValue(l, out g)) continue;
                    TvSubStore.Put(it.Key, "File_" + l, g[0]); TvSubStore.Put(it.Key, "Name_" + l, g[1]);
                    // Dutch is shown; English is there to pick (and shown when no Dutch was found and nothing else shows)
                    bool show = l == "nl" || (!anyShown && !got.ContainsKey("nl") && !have.Contains("nl"));
                    Mpv.Command(tvMpv, "sub-add", g[0], show ? "select" : "auto", g[1]);
                    if (show) anyShown = true;
                }
                Mpv.Command(tvMpv, "show-text", got.ContainsKey("nl") ? "Dutch subtitles found online" + (got.ContainsKey("en") ? " (English too, under Subtitles)" : "") : "English subtitles found online (no Dutch ones)", "3000");
            }));
        }) { IsBackground = true }.Start();
    }

    // OpenSubtitles: the film or episode by its IMDb number when Cinemeta knows it, else by its name and year
    List<Dictionary<string, object>> TvOsFind(TvItem it, string langs) {
        bool ep = it.Kind == "episode";
        var m = Cinemeta.Cached(ep ? new TvItem { Kind = "series", Title = it.Group, Account = it.Account } : it);
        string imdb = m != null ? Regex.Match(Json.Str(m, "id") ?? "", @"^tt(\d+)$").Groups[1].Value : "";
        string q = Regex.Replace(ep ? (it.Group ?? it.Title) : it.Title ?? "", @"\s*[\(\[].*?[\)\]]", "").Trim();
        string path = "subtitles?languages=" + langs +
            (imdb.Length > 0 ? (ep ? "&parent_imdb_id=" : "&imdb_id=") + imdb.TrimStart('0') : "&query=" + Uri.EscapeDataString(q) + (!string.IsNullOrEmpty(it.Year) && !ep ? "&year=" + it.Year : "")) +
            (ep && it.Season > 0 ? "&season_number=" + it.Season + "&episode_number=" + it.Episode : "");
        var d = Json.Parse(OsCall("GET", path, null, null)) as Dictionary<string, object>;
        return ((d != null && d.ContainsKey("data") ? d["data"] as System.Collections.IEnumerable : null) ?? new object[0]).Cast<object>().OfType<Dictionary<string, object>>().Take(30).ToList();
    }
    // one subtitle file, downloaded (signed in when there's a username: more downloads a day)
    string TvOsDownload(string fileId, string lang) {
        if (tvOsToken == null && !string.IsNullOrEmpty(TvPlayerPrefs.OsUser)) {
            var l = Json.Parse(OsCall("POST", "login", Json.Write(new Dictionary<string, object> { { "username", TvPlayerPrefs.OsUser }, { "password", TvPlayerPrefs.OsPass } }), null)) as Dictionary<string, object>;
            tvOsToken = Json.Str(l, "token");
        }
        var d = Json.Parse(OsCall("POST", "download", "{\"file_id\": " + fileId + "}", tvOsToken)) as Dictionary<string, object>;
        string link = Json.Str(d, "link"); if (link == null) throw new Exception(Json.Str(d, "message") ?? "no link");
        string file = TvSubStore.NewFile(fileId, lang, Path.GetExtension(Json.Str(d, "file_name") ?? ".srt"));
        File.WriteAllBytes(file, TvFree.GetBytes(link));
        return file;
    }

    // ---------- timing ----------
    // Easy to reach: the Timing button on the controls, X again in the Subtitles menu, T on the keyboard; and without any
    // menu Z / X on the keyboard (Shift: a whole second). In the menu: ← → a tenth, LB RB a second, Y back in sync, and
    // "sync to a line": press as someone says it and the subtitles jump to match. The offset shows at the top all along.
    int tvSubTimeIdx = 4;
    static readonly double[] TvSubSteps = { -1, -0.1, 0.1, 1 };
    void TvSubTimeItems(List<Tuple<string, bool, Action>> items) {
        TvSubTimeTitle();
        tvTrackHint.Text = "← →  0.1 s     LB RB  1 s     Y  back in sync\nSync: press just as someone says the line.\nKeyboard: Z / X  0.1 s,  Shift + Z / X  1 s";
        tvTrackHint.Visibility = System.Windows.Visibility.Visible;
        var rows = new List<Tuple<string, Action>> {
            Tuple.Create("Sync: show the next line now", (Action)(() => TvSubStep(1))),
            Tuple.Create("Sync: show the last line again", (Action)(() => TvSubStep(-1))),
            Tuple.Create("1 second earlier", (Action)(() => TvSubShift(TvSubSteps[0], false))),
            Tuple.Create("0.1 second earlier", (Action)(() => TvSubShift(TvSubSteps[1], false))),
            Tuple.Create("0.1 second later", (Action)(() => TvSubShift(TvSubSteps[2], false))),
            Tuple.Create("1 second later", (Action)(() => TvSubShift(TvSubSteps[3], false))),
            Tuple.Create("Back in sync (no offset)", (Action)(() => TvSubShift(0, true))),
        };
        for (int i = 0; i < rows.Count; i++) {
            int ii = i; var a = rows[i].Item2;
            // the menu stays open (rebuilt quietly) so you can keep going
            items.Add(Tuple.Create(rows[i].Item1, i == tvSubTimeIdx, (Action)(() => { tvSubTimeIdx = ii; a(); tvTrackRefresh = true; TvTracks("subtime"); })));
        }
        items.Add(Tuple.Create("Done", false, (Action)(() => { tvSubTimeIdx = 4; })));
    }
    // the menu's title: the offset as it is now
    void TvSubTimeTitle() {
        if (tvTrackKind == "subtime") tvTrackTitle.Text = "Subtitle timing  ·  " + TvSubDelayText(TvSubDelay());
        if (tvTrackKind == "sub" && tvSubDelayVal != null) tvSubDelayVal.Text = TvSubDelayShort(TvSubDelay());
    }
    static string TvSubDelayShort(double d) {
        if (Math.Abs(d) < 0.005) return "0.0 s";
        return (d > 0 ? "+" : "−") + Math.Abs(d).ToString("0.0#", CultureInfo.InvariantCulture) + " s";
    }
    // The Subtitles menu's Delay row, as in a streaming app: [ −  +0.5 s  + ] on the right, live with every click or
    // press (← → a tenth, LB RB a second, Y back to 0); the menu stays open. A on the row: sync to a line.
    System.Windows.Controls.TextBlock tvSubDelayVal;
    void TvSubDelayRow(System.Windows.Controls.Border row) {
        var g = (System.Windows.Controls.Grid)row.Child;
        g.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
        var pill = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = System.Windows.VerticalAlignment.Center };
        var wrap = new System.Windows.Controls.Border { Child = pill, Background = TvTheme.Brush("#3A3A3C"), CornerRadius = new System.Windows.CornerRadius(15), Height = 32 };
        Func<string, string, Action, System.Windows.Controls.Border> btn = (glyph, tip, act) => {
            var b = new System.Windows.Controls.Border { Width = 34, Height = 32, CornerRadius = new System.Windows.CornerRadius(15), Background = System.Windows.Media.Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = tip,
                Child = new System.Windows.Controls.TextBlock { Text = glyph, FontSize = 18, FontFamily = Ui.Semi, Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new System.Windows.Thickness(0, -2, 0, 0) } };
            b.MouseEnter += (s, e) => b.Background = TvTheme.Brush("#59FFFFFF"); b.MouseLeave += (s, e) => b.Background = System.Windows.Media.Brushes.Transparent;
            b.MouseLeftButtonDown += (s, e) => e.Handled = true;
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; Sound.Play(Sfx.Move); act(); TvWake(); };   // the menu stays open
            return b;
        };
        pill.Children.Add(btn("−", "Subtitles earlier (0.1 s)", () => TvSubShift(-0.1, false)));
        tvSubDelayVal = new System.Windows.Controls.TextBlock { MinWidth = 66, TextAlignment = System.Windows.TextAlignment.Center, FontSize = 15.5, FontFamily = Ui.Semi, Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Text = TvSubDelayShort(TvSubDelay()), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Back to 0" };
        tvSubDelayVal.MouseLeftButtonDown += (s, e) => e.Handled = true;
        tvSubDelayVal.MouseLeftButtonUp += (s, e) => { e.Handled = true; Sound.Play(Sfx.Move); TvSubShift(0, true); TvWake(); };
        pill.Children.Add(tvSubDelayVal);
        pill.Children.Add(btn("+", "Subtitles later (0.1 s)", () => TvSubShift(0.1, false)));
        System.Windows.Controls.Grid.SetColumn(wrap, 2); g.Children.Add(wrap);
        tvTrackHint.Text = "Delay:  ← →  0.1 s     LB RB  1 s     Y  back to 0     A  sync to a line"; tvTrackHint.Visibility = System.Windows.Visibility.Visible;
    }
    // the Timing button and T: straight to the Delay row (the full timing menu when nothing has subtitles yet)
    void TvSubOpenDelay() {
        TvTracks("sub");
        if (tvSubTimingRow >= 0) { tvTrackIdx = tvSubTimingRow; TvMarkTracks(); } else { tvTrackRefresh = true; TvTracks("subtime"); }
    }
    string TvSubTimingLabel() { return "Subtitle delay: " + TvSubDelayShort(TvSubDelay()) + "  ·  change..."; }
    double TvSubDelay() {
        double d; return tvMpv != IntPtr.Zero && double.TryParse(Mpv.Get(tvMpv, "sub-delay"), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0;
    }
    static string TvSubDelayText(double d) {
        if (Math.Abs(d) < 0.005) return "in sync";
        return (d > 0 ? "+" : "−") + Math.Abs(d).ToString("0.0#", CultureInfo.InvariantCulture) + " s (" + (d > 0 ? "later" : "earlier") + ")";
    }
    // earlier (negative) or later; reset: back to 0. Shown on the picture and kept for this film or episode
    void TvSubShift(double by, bool reset) {
        if (tvMpv == IntPtr.Zero) return;
        double now = reset ? 0 : Math.Round(TvSubDelay() + by, 2);
        Mpv.Set(tvMpv, "sub-delay", now.ToString("0.0##", CultureInfo.InvariantCulture));
        TvSubSaved(now);
    }
    void TvSubSaved(double now) {
        Mpv.Command(tvMpv, "show-text", "Subtitles " + TvSubDelayText(now), "1500");
        if (tvPlaying != null && tvPlaying.Kind != "live") TvSubStore.Put(tvPlaying.Key, "Delay", now);
        TvSubTimeTitle();
    }
    // sync to a line: mpv moves the subtitles so the next (1) or the last (-1) line shows right now
    void TvSubStep(int dir) {
        if (tvMpv == IntPtr.Zero) return;
        double before = TvSubDelay(); var ctx = tvMpv; var it = tvPlaying;
        Mpv.Command(tvMpv, "sub-step", dir.ToString());
        // the new offset comes back from mpv a moment later
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        t.Tick += (s, e) => {
            t.Stop();
            if (tvMpv != ctx || tvPlaying != it) return;
            double now = TvSubDelay();
            if (Math.Abs(now - before) < 0.001) { Mpv.Command(tvMpv, "show-text", "No " + (dir > 0 ? "next" : "last") + " subtitle line to sync to here", "2000"); return; }
            TvSubSaved(Math.Round(now, 3));
        };
        t.Start();
    }
    // Z / X on the keyboard while watching (Shift: a whole second)
    bool TvSubKey(int dir) {
        if (tvMpv == IntPtr.Zero || tvPlaying == null) return false;
        bool big = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0;
        TvActivity(); TvSubShift(dir * (big ? 1 : 0.1), false);
        return true;
    }
}
}
