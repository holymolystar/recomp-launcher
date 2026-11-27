// TV & Movies: more of a streaming service.
//   Subtitle style      Subtitles → Style: size, colour, a dark box behind the words, and higher up (for films with
//                       their own words at the bottom). Per profile (settings: TvSubStyle|<profile>).
//   Night mode, boost   Audio → Night mode evens out the loud and the quiet (dynaudnorm); Louder turns it up past 100%.
//                       Both stay on until turned off (settings: TvNight, TvBoost).
//   Seek info           skipping shows where you are, of how long, and the chapter's name when the file has chapters
//   Skip recap          an episode with a chapter called recap / previously gets "Skip recap"; "Skip intro" jumps to
//                       the end of an intro / opening chapter when there is one
//   Credits             a chapter called credits / ending (or, once you've pressed "Next episode" during a series'
//                       credits, that same moment in its other episodes) brings the next-episode card then, with ten
//                       seconds to go instead of at the very end
//   Collections         films of one series ("Name", "Name 2", "Name III", "Name: Part Two") as one tile on the film
//                       pages; it opens their own row, in order
//   New this week       a provider's films and series added in the last seven days, as a row
//   Channel numbers     watching live TV: type a number (keyboard or numpad) to go to that channel in the list; F (or Y)
//                       goes back to the channel before. The controls show the number, and what's on now and next.
//   Favourite channels  in TV Live and the TV Guide, the channels in My list come first
//   Search on the phone the search page's "Type on your phone" (or the Start button): a QR code; what you type on the
//                       phone shows up on the TV as you type
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace RecompLauncher {

partial class Shell {
    // ================= subtitle style =================
    static readonly string[] TvSubSizes = { "Small", "Normal", "Large", "Huge" }; static readonly int[] TvSubSizePx = { 38, 46, 56, 68 };
    static readonly string[] TvSubColours = { "White", "Yellow", "Light blue" }; static readonly string[] TvSubColourHex = { "#FFFFFF", "#FFE45C", "#9AD8FF" };
    // size, colour, box (0/1), higher (0/1), sound descriptions hidden (0/1: [door slams], (laughs), "JOHN:")
    int[] TvSubStyle() {
        var v = (Settings.Get("TvSubStyle|" + TvMine.Profile(tvAcc)) ?? "").Split(',');
        int[] r = { 1, 0, 0, 0, 0 };
        for (int i = 0; i < 5 && i < v.Length; i++) { int n; if (int.TryParse(v[i], out n)) r[i] = n; }
        r[0] = Math.Max(0, Math.Min(3, r[0])); r[1] = Math.Max(0, Math.Min(2, r[1]));
        return r;
    }
    void TvSubStyleSave(int[] s) { Settings.Set("TvSubStyle|" + TvMine.Profile(tvAcc), string.Join(",", s)); Settings.Save(); }
    void TvSubStyleApply(IntPtr ctx) {
        if (ctx == IntPtr.Zero) return;
        var s = TvSubStyle();
        Mpv.Set(ctx, "sub-font-size", TvSubSizePx[s[0]].ToString());
        Mpv.Set(ctx, "sub-color", TvSubColourHex[s[1]]);
        Mpv.Set(ctx, "sub-border-style", s[2] == 1 ? "opaque-box" : "outline-and-shadow");
        Mpv.Set(ctx, "sub-back-color", s[2] == 1 ? "#B0000000" : "#00000000");
        Mpv.Set(ctx, "sub-pos", s[3] == 1 ? "86" : "100");
        Mpv.Set(ctx, "sub-ass-override", s[1] != 0 || s[2] == 1 || s[0] != 1 ? "force" : "yes");   // the style also for subtitles with their own
        // mpv's own filter for subtitles made for the hard of hearing: [door slams], (laughs) and "JOHN:" go
        Mpv.Set(ctx, "sub-filter-sdh", s[4] == 1 ? "yes" : "no");
        Mpv.Set(ctx, "sub-filter-sdh-harder", s[4] == 1 ? "yes" : "no");
    }
    static string TvSubStyleText(int[] s) { return TvSubSizes[s[0]] + " · " + TvSubColours[s[1]] + (s[2] == 1 ? " · box" : "") + (s[3] == 1 ? " · higher" : "") + (s[4] == 1 ? " · no sound descriptions" : ""); }
    void TvSubStyleItems(List<Tuple<string, bool, Action>> items) {
        var s = TvSubStyle();
        tvTrackTitle.Text = "Subtitle style";
        Action<int, int> step = (i, max) => { var c = TvSubStyle(); c[i] = (c[i] + 1) % max; TvSubStyleSave(c); TvSubStyleApply(tvMpv); Mpv.Command(tvMpv, "show-text", "Subtitles: " + TvSubStyleText(c), "1500"); TvTracks("substyle"); };
        items.Add(Tuple.Create("Size: " + TvSubSizes[s[0]], false, (Action)(() => step(0, 4))));
        items.Add(Tuple.Create("Colour: " + TvSubColours[s[1]], false, (Action)(() => step(1, 3))));
        items.Add(Tuple.Create("Dark box behind the words: " + (s[2] == 1 ? "on" : "off"), false, (Action)(() => step(2, 2))));
        items.Add(Tuple.Create("Position: " + (s[3] == 1 ? "higher up" : "at the bottom"), false, (Action)(() => step(3, 2))));
        // the filter works as subtitles are read: the ones showing are read again so it shows at once
        items.Add(Tuple.Create("Sound descriptions ([door slams], JOHN:): " + (s[4] == 1 ? "hidden" : "shown"), false, (Action)(() => { step(4, 2); Mpv.Command(tvMpv, "sub-reload"); })));
        items.Add(Tuple.Create("Done", false, (Action)(() => { })));
    }

    // ================= night mode and boost =================
    void TvAudioFxApply(IntPtr ctx) {
        if (ctx == IntPtr.Zero) return;
        Mpv.Set(ctx, "af", Settings.Get("TvNight") == "on" ? "lavfi=[dynaudnorm=f=250:g=15:p=0.8:m=12]" : "");
        bool boost = Settings.Get("TvBoost") == "on";
        Mpv.Set(ctx, "volume-max", "200"); Mpv.Set(ctx, "volume", boost ? "150" : "100");
    }
    void TvAudioFxItems(List<Tuple<string, bool, Action>> items) {
        bool night = Settings.Get("TvNight") == "on", boost = Settings.Get("TvBoost") == "on";
        items.Add(Tuple.Create("Night mode: " + (night ? "on" : "off") + "  ·  evens out loud and quiet", night, (Action)(() => {
            Settings.Set("TvNight", night ? "" : "on"); Settings.Save(); TvAudioFxApply(tvMpv); Mpv.Command(tvMpv, "show-text", "Night mode " + (night ? "off" : "on"), "1500"); })));
        items.Add(Tuple.Create("Louder (150%): " + (boost ? "on" : "off"), boost, (Action)(() => {
            Settings.Set("TvBoost", boost ? "" : "on"); Settings.Save(); TvAudioFxApply(tvMpv); Mpv.Command(tvMpv, "show-text", "Louder " + (boost ? "off" : "on"), "1500"); })));
    }

    // ================= seek info =================
    void TvSeekInfo() {
        if (tvMpv == IntPtr.Zero) return;
        Mpv.Command(tvMpv, "show-progress");
        Mpv.Command(tvMpv, "show-text", "${time-pos} / ${duration}${?chapter-metadata/title:   ·   ${chapter-metadata/title}}", "1500");
    }

    // ================= chapters: recap, intro, credits =================
    readonly List<Tuple<double, double, string>> tvChapters = new List<Tuple<double, double, string>>();   // start, end, title
    void TvChaptersLoad() {
        tvChapters.Clear();
        if (tvMpv == IntPtr.Zero) return;
        int n = (int)Math.Max(0, Mpv.Num(tvMpv, "chapter-list/count")); double dur = Mpv.Num(tvMpv, "duration");
        var starts = new List<Tuple<double, string>>();
        for (int i = 0; i < n; i++) starts.Add(Tuple.Create(Mpv.Num(tvMpv, "chapter-list/" + i + "/time"), (Mpv.Get(tvMpv, "chapter-list/" + i + "/title") ?? "").ToLowerInvariant()));
        for (int i = 0; i < starts.Count; i++) tvChapters.Add(Tuple.Create(starts[i].Item1, i + 1 < starts.Count ? starts[i + 1].Item1 : dur, starts[i].Item2));
    }
    Tuple<double, double, string> TvChapterLike(string pattern) { return tvChapters.FirstOrDefault(c => Regex.IsMatch(c.Item3, pattern)); }
    const string TvRecapWords = @"\b(recap|previously|vorige keer|eerder)\b", TvIntroWords = @"\b(intro|opening|theme|titles|generique)\b", TvCreditsWords = @"\b(credits|ending|end titles|outro|aftiteling)\b";

    Border tvRecapBtn;
    void TvRecapTick(double pos) {
        if (tvOverlayRoot == null) return;
        var c = TvChapterLike(TvRecapWords);
        bool want = c != null && pos >= c.Item1 && pos < c.Item2 - 2;
        if (tvRecapBtn == null) {
            tvRecapBtn = new Border { Child = new TextBlock { Text = "Skip recap", FontSize = 17, FontFamily = Ui.Semi, Foreground = Brushes.Black }, Background = Brushes.White, CornerRadius = new CornerRadius(8), Padding = new Thickness(22, 10, 22, 11),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 230, 20), Cursor = Cursors.Hand, Visibility = Visibility.Collapsed };   // left of "Skip intro"
            tvRecapBtn.MouseLeftButtonUp += (s, e) => { e.Handled = true; TvSkipRecap(); };
            Grid.SetRow(tvRecapBtn, 1); tvOverlayRoot.Children.Add(tvRecapBtn);
        }
        var v = want ? Visibility.Visible : Visibility.Collapsed;
        if (tvRecapBtn.Visibility != v) { tvRecapBtn.Visibility = v; if (want) TvWake(); }
    }
    bool TvRecapUp { get { return tvRecapBtn != null && tvRecapBtn.Visibility == Visibility.Visible; } }
    void TvSkipRecap() {
        var c = TvChapterLike(TvRecapWords); if (c == null || tvMpv == IntPtr.Zero) return;
        Mpv.Command(tvMpv, "seek", c.Item2.ToString("0.###", CultureInfo.InvariantCulture), "absolute"); Mpv.Command(tvMpv, "show-text", "Recap skipped", "1500");
        tvRecapBtn.Visibility = Visibility.Collapsed;
    }
    // the end of the intro chapter, when the file has one (else the usual length)
    bool TvIntroChapterEnd(double pos, out double end) {
        end = 0; var c = TvChapterLike(TvIntroWords);
        if (c == null || pos >= c.Item2) return false;
        end = c.Item2; return true;
    }
    // where the credits start: a credits chapter, else what was learnt for this series (-1: not known)
    double TvCreditsAt(double dur) {
        var c = TvChapterLike(TvCreditsWords); if (c != null && c.Item1 > dur * 0.6) return c.Item1;
        var it = tvPlaying; if (it == null || dur <= 0) return -1;
        double left; if (double.TryParse(Settings.Get("TvCredits|" + (it.Group ?? it.Title)) ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out left) && left > 20 && left < dur * 0.3) return dur - left;
        return -1;
    }
    // "Next episode" pressed while the end is still a while off: that's where this series' credits start
    void TvNextByHand() {
        var it = tvPlaying;
        if (it != null && tvMpv != IntPtr.Zero) {
            double pos = Mpv.Num(tvMpv, "time-pos"), dur = Mpv.Num(tvMpv, "duration"), left = dur - pos;
            if (dur > 300 && left > 25 && left < dur * 0.3) { Settings.Set("TvCredits|" + (it.Group ?? it.Title), Math.Round(left).ToString(CultureInfo.InvariantCulture)); Settings.Save(); }
        }
        TvNextNow();
    }

    // ================= collections =================
    static readonly Regex TvSequel = new Regex(@"^(.*?)(?:\s*[:\-–]?\s*(?:part|chapter|deel|hoofdstuk|vol\.?|volume)\s+(?:\d+|[ivx]+|one|two|three|four|five)\b.*|\s+(?:[2-9]|1[0-9]|ii|iii|iv|v|vi|vii|viii|ix|x)(?:\s*[:\-–].*)?)$", RegexOptions.IgnoreCase);
    static string TvSeriesBase(string title, out bool sequel) {
        string y; string t = Cinemeta.CleanTitle(title, out y);
        var m = TvSequel.Match(t); sequel = m.Success && m.Groups[1].Value.Trim().Length >= 3;
        return sequel ? m.Groups[1].Value.Trim() : t;
    }
    // collections among films: two or more different titles of one series, at least one of them numbered
    List<TvItem> TvCollections(IEnumerable<TvItem> films, int max) {
        var groups = new Dictionary<string, List<TvItem>>(); var hasSequel = new HashSet<string>(); var names = new Dictionary<string, string>();
        foreach (var f in films) {
            if (f == null || f.Kind != "movie" || string.IsNullOrEmpty(f.Title)) continue;
            bool sq; string b = TvSeriesBase(f.Title, out sq); string k = Regex.Replace(TvFold(b), @"[^a-z0-9]", ""); if (k.Length < 3) continue;
            List<TvItem> l; if (!groups.TryGetValue(k, out l)) { groups[k] = l = new List<TvItem>(); names[k] = b; }
            l.Add(f); if (sq) hasSequel.Add(k);
        }
        var r = new List<TvItem>();
        foreach (var kv in groups.Where(g => hasSequel.Contains(g.Key))) {
            // one copy of each film (a provider may have it in several languages)
            var one = kv.Value.GroupBy(f => { string y; return Regex.Replace(TvFold(Cinemeta.CleanTitle(f.Title, out y)), @"[^a-z0-9]", ""); }).Select(g => g.First())
                .OrderBy(f => { int y; return int.TryParse(f.Year ?? "", out y) ? y : 9999; }).ThenBy(f => f.Title).ToList();
            if (one.Count < 2) continue;
            var first = one.FirstOrDefault(f => !string.IsNullOrEmpty(f.Backdrop)) ?? one[0];
            r.Add(new TvItem { Kind = "collection", Id = "col:" + kv.Key, Title = names[kv.Key] + " collection", Image = one.Select(f => f.Image).FirstOrDefault(i => !string.IsNullOrEmpty(i)), Backdrop = first.Backdrop,
                Plot = one.Count + " films: " + string.Join(", ", one.Select(f => { string y; return Cinemeta.CleanTitle(f.Title, out y) + (string.IsNullOrEmpty(f.Year) ? "" : " (" + f.Year + ")"); })), Episodes = one, Account = one[0].Account, Group = "Collections" });
        }
        return r.OrderByDescending(c => c.Episodes.Count).ThenBy(c => c.Title).Take(max).ToList();
    }
    string tvCollectionFrom;
    void TvOpenCollection(TvItem col) {
        if (col == null || col.Episodes == null) return;
        tvCollectionFrom = tvPage == "collection" ? tvCollectionFrom : tvPage;
        tvPage = "collection"; tvGen++;
        tvPageTitle.Text = col.Title;
        TvPageIn();
        TvSetRows(new List<TvRow> { TvRowOf(col.Title, col.Episodes, false) });
    }

    // ================= new this week =================
    static List<TvItem> TvNewThisWeek(IEnumerable<TvItem> l) {
        long since = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 7 * 86400;
        return l.Where(x => x.Added > since).OrderByDescending(x => x.Added).Take(40).ToList();
    }

    // ================= channel numbers, the last channel, now and next =================
    string tvChanDigits = ""; DispatcherTimer tvChanTimer; TvItem tvPrevChannel;
    bool TvChannelDigit(int d) {
        if (!TvPlaying || tvPlaying == null || tvPlaying.Kind != "live" || tvPlayList == null) return false;
        if (tvChanDigits.Length >= 4) tvChanDigits = "";
        tvChanDigits += d.ToString();
        Mpv.Command(tvMpv, "show-text", "Channel " + tvChanDigits + "_", "1600");
        if (tvChanTimer == null) { tvChanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) }; tvChanTimer.Tick += (s, e) => { tvChanTimer.Stop(); TvChannelGo(); }; }
        tvChanTimer.Stop(); tvChanTimer.Start();
        return true;
    }
    void TvChannelGo() {
        int n; string typed = tvChanDigits; tvChanDigits = "";
        if (!int.TryParse(typed, out n) || tvPlayList == null || !TvPlaying) return;
        if (n < 1 || n > tvPlayList.Count) { Mpv.Command(tvMpv, "show-text", "There's no channel " + n + " (1 to " + tvPlayList.Count + ")", "2000"); Sound.Play(Sfx.Bump); return; }
        var ch = tvPlayList[n - 1]; if (ch.Key == tvPlaying.Key) return;
        Sound.Play(Sfx.Select); TvPlay(ch, tvPlayList);
    }
    // F or Y: back to the channel before
    bool TvLastChannel() {
        if (tvPlaying == null || tvPlaying.Kind != "live") return false;
        if (tvPrevChannel == null) { Mpv.Command(tvMpv, "show-text", "No channel before this one yet", "1500"); return true; }
        Sound.Play(Sfx.Select); TvPlay(tvPrevChannel, tvPlayList);
        return true;
    }
    // remembered when another channel starts (TvPlay)
    void TvChannelChanging(TvItem next) {
        if (next != null && next.Kind == "live" && tvPlaying != null && tvPlaying.Kind == "live" && tvPlaying.Key != next.Key) tvPrevChannel = tvPlaying;
    }
    // the controls' line: the channel's number, and what's on now and next (from the guide, looked up off the window's thread)
    DateTime tvNowNextAt;
    void TvNowNext(TvItem ch) {
        if (ch == null || ch.Kind != "live") return;
        tvNowNextAt = DateTime.Now;
        int num = tvPlayList != null ? tvPlayList.FindIndex(x => x.Key == ch.Key) + 1 : 0;
        string head = "LIVE" + (num > 0 ? "  ·  " + num : "") + (ch.Group != null ? "  ·  " + ch.Group : "");
        tvPlaySub.Text = head;
        var acc = ch.Account;
        ThreadPool.QueueUserWorkItem(_ => {
            string line = null;
            try {
                var g = TvFree.Guide(acc); var l = g != null ? TvFree.For(g, ch) : null;
                if (l != null) {
                    var now = l.FirstOrDefault(p => p.Start <= DateTime.Now && p.Stop > DateTime.Now); var next = l.FirstOrDefault(p => p.Start >= DateTime.Now);
                    if (now != null) line = "Now: " + now.Title + " (until " + now.Stop.ToString("HH:mm") + ")" + (next != null ? "   ·   Next: " + next.Title : "");
                }
            } catch { }
            W.Dispatcher.BeginInvoke(new Action(() => { if (tvPlaying == ch && line != null) tvPlaySub.Text = head + "   ·   " + line; }));
        });
    }
    // twice a second from TvTick: the line again every minute
    void TvNowNextTick() { if (tvPlaying != null && tvPlaying.Kind == "live" && (DateTime.Now - tvNowNextAt).TotalSeconds > 60) TvNowNext(tvPlaying); }

    // ================= favourite channels first =================
    static List<TvItem> TvFavsFirst(IEnumerable<TvItem> l) { return l.OrderBy(x => TvStore.IsFav(x) ? 0 : 1).ToList(); }   // (OrderBy keeps the order within each)

    // ================= search on the phone =================
    string tvPairMode = "wizard";
    void TvPairSearchStart() { tvPairMode = "search"; TvPairOpenCard(); }
    void TvPairFromPhone(string q) {
        if (!TvSearchOpen) return;
        tvQuery = (q ?? "").Trim(); if (tvQuery.Length > 40) tvQuery = tvQuery.Substring(0, 40);
        if (TvPairOpen) { tvPairCard.Visibility = Visibility.Collapsed; SetHints(); }   // the phone is connected: the results show
        TvSearchTextShow(); TvSearchRun();
    }
    const string TvPairSearchPage = @"<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>Search on the TV</title>
<style>body{margin:0;font-family:system-ui,-apple-system,Segoe UI,sans-serif;background:#0b0b0d;color:#fff}main{max-width:460px;margin:0 auto;padding:24px 18px}h1{font-size:24px;margin:6px 0 4px}p{color:#aaa;font-size:14px;line-height:1.45}
input{width:100%;box-sizing:border-box;padding:16px;border-radius:14px;border:1px solid #333;background:#16161a;color:#fff;font-size:18px;margin-top:14px}#msg{margin-top:14px;text-align:center;color:#aaa;font-size:14px}
label{display:block;font-size:13px;color:#aaa;margin:14px 0 6px}</style></head>
<body><main><h1>Search on the TV</h1><p>Type here; the results show on the TV as you type.</p>
<input id='q' autocomplete='off' autocapitalize='off' autocorrect='off' placeholder='A film, a series, a channel' autofocus>
<div id='cbox'><label>The code on the TV</label><input id='code' inputmode='numeric' maxlength='6'></div><div id='msg'></div></main>
<script>var q=new URLSearchParams(location.search);if(q.get('c')){document.getElementById('code').value=q.get('c');document.getElementById('cbox').style.display='none';}
var t=null;document.getElementById('q').addEventListener('input',function(){clearTimeout(t);t=setTimeout(send,300);});
function send(){fetch('/send',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({code:document.getElementById('code').value.trim(),q:document.getElementById('q').value})}).then(function(r){return r.text();}).then(function(x){document.getElementById('msg').textContent=x;}).catch(function(){document.getElementById('msg').textContent='The TV couldn\'t be reached. Is the phone on the same Wi-Fi?';});}</script></body></html>";
}
}
