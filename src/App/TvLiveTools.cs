// TV & Movies: the live-TV extras.
//   TV Archive     a provider (Xtream) that keeps past programmes: in the guide they get a clock and play with A; a
//                  channel's menu lists them too ("TV Archive")
//   Reminders      on an upcoming programme: a note (and a sound) when it starts, with "Watch now"
//   Guide listings a channel with the wrong listings, or none: pick the right ones by hand (or none at all)
//   Guide order    the provider's order, starting soonest first, or A to Z
//   Channels       while watching live TV: the list of channels with what's on, over the picture
//   Settings       per profile: the guide's link, a time shift, how often it's fetched, and the preview's delay
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace RecompLauncher {

partial class Shell {
    TvFree.Prog tvActProg; List<KeyValuePair<string, string>> tvEpgPick; string tvEpgQuery;

    // ---------- TV Archive ----------
    static string TvArchiveUrl(TvItem ch, TvFree.Prog p) {
        var a = ch.Account; if (a == null || a.Type != "xtream" || ch.ArchiveDays <= 0 || p.RawStart == null || p.RawStart.Length < 12) return null;
        int mins = (int)Math.Ceiling((p.Stop - p.Start).TotalMinutes); string r = p.RawStart;
        string when = r.Substring(0, 4) + "-" + r.Substring(4, 2) + "-" + r.Substring(6, 2) + ":" + r.Substring(8, 2) + "-" + r.Substring(10, 2);
        return a.Base + "/timeshift/" + Uri.EscapeDataString(a.User ?? "") + "/" + Uri.EscapeDataString(a.Pass ?? "") + "/" + mins + "/" + when + "/" + ch.Id + ".ts";
    }
    static bool TvCanReplay(TvItem ch, TvFree.Prog p) {
        return p != null && p.Title != null && p.Stop <= DateTime.Now && ch.ArchiveDays > 0 && p.Start > DateTime.Now.AddDays(-ch.ArchiveDays) && TvArchiveUrl(ch, p) != null;
    }
    void TvReplay(TvItem ch, TvFree.Prog p) {
        TvPlay(new TvItem { Kind = "archive", Id = ch.Id + "@" + p.RawStart, Title = p.Title, Group = ch.Title + "  •  " + p.Start.ToString("ddd d MMM  HH:mm"), Url = TvArchiveUrl(ch, p), Image = ch.Image, Account = ch.Account, Plot = p.Desc }, null);
    }
    // a channel's past programmes, from the guide
    void TvArchiveMenu(TvItem ch) {
        tvActItem = ch; tvActMode = "archive"; tvEpgPick = null; TvOpenActions();
        var acc = ch.Account;
        new Thread(() => {
            List<TvFree.Prog> l = null; try { l = TvFree.For(TvFree.Guide(acc), ch); } catch { }
            W.Dispatcher.BeginInvoke(new Action(() => { tvArchiveList = (l ?? new List<TvFree.Prog>()).Where(p => TvCanReplay(ch, p)).OrderByDescending(p => p.Start).Take(80).ToList(); if (ListPageOpen && lpOpen == tvActPage && tvActMode == "archive") TvFillActions(); }));
        }) { IsBackground = true }.Start();
    }
    List<TvFree.Prog> tvArchiveList;
    bool TvFillLiveActions(ListPage p, List<ListRow> rows) {
        var ch = tvActItem;
        if (tvActMode == "archive") {
            p.Title.Text = ch.Title + "  •  TV Archive"; p.Sub.Text = tvArchiveList == null ? "Looking in the guide..." : tvArchiveList.Count == 0 ? "Nothing to replay in the guide for this channel (the provider keeps " + ch.ArchiveDays + " days)." : "Past programmes, the newest first. They play from your provider's archive.";
            if (tvArchiveList != null) foreach (var pr in tvArchiveList) { var q = pr; rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = q.Title, Text = q.Start.ToString("ddd d MMM  HH:mm") + " – " + q.Stop.ToString("HH:mm"), Do = () => { CloseListPage(); TvReplay(ch, q); } }); }
            rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Back", Do = () => { tvActMode = "item"; TvFillActions(); } });
            return true;
        }
        if (tvActMode == "prog") {
            var pr = tvActProg; var now = DateTime.Now; bool future = pr.Start > now;
            p.Title.Text = pr.Title ?? "No guide information";
            p.Sub.Text = ch.Title + "  •  " + pr.Start.ToString("ddd d MMM  HH:mm") + " – " + pr.Stop.ToString("HH:mm") + (string.IsNullOrEmpty(pr.Desc) ? "" : "\n\n" + pr.Desc);
            if (future) {
                bool has = TvMine.HasReminder(ch, pr.Start);
                rows.Add(new ListRow { Glyph = "", C1 = "#c28a1e", C2 = "#e0b33c", Title = has ? "Remove the reminder" : "Remind me when it starts", Text = has ? null : "A note and a sound at " + pr.Start.ToString("HH:mm") + ", with Watch now",
                    Do = () => { TvMine.ToggleReminder(ch, pr); TvStartReminders(); CloseListPage(); TvAfterActions(); } });
            } else if (pr.Stop <= now) rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = TvCanReplay(ch, pr) ? "Replay it" : "Can't be replayed", Text = TvCanReplay(ch, pr) ? null : "Your provider doesn't keep this channel's past programmes",
                Do = () => { if (TvCanReplay(ch, pr)) { CloseListPage(); TvReplay(ch, pr); } } });
            TvSafeProgRows(ch, pr, rows);
            rows.Add(new ListRow { Glyph = "", C1 = "#3fb8a0", C2 = "#2f8fe0", Title = "Watch " + ch.Title + " now", Do = () => { CloseListPage(); TvPlay(ch, tvGuideChans); } });
            return true;
        }
        if (tvActMode == "epg") {
            p.Title.Text = ch.Title + "  •  Guide listings";
            string mapped; bool has = TvMine.EpgMap.TryGetValue(ch.Key, out mapped);
            p.Sub.Text = tvEpgPick == null ? "Looking in the guide..." : "Pick the guide channel that belongs to " + ch.Title + "." + (tvEpgQuery != null ? "  Search: " + tvEpgQuery : "");
            rows.Add(new ListRow { Glyph = "", C1 = "#3fb8a0", C2 = "#2f8fe0", Title = "Automatic", Text = !has ? "In use: matched by its guide id and name" : null, Do = () => { TvMine.EpgMap.Remove(ch.Key); TvMine.Save(); CloseListPage(); TvAfterActions(); } });
            rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "No listings", Text = has && mapped == "" ? "In use" : null, Do = () => { TvMine.EpgMap[ch.Key] = ""; TvMine.Save(); CloseListPage(); TvAfterActions(); } });
            rows.Add(new ListRow { Glyph = "", C1 = "#2f8fe0", C2 = "#2f8fe0", Title = "Search the guide...", Do = () => OpenOsk("A guide channel's name", tvEpgQuery ?? ch.Title, null, v => { tvEpgQuery = (v ?? "").Trim(); TvFillActions(); }, null) });
            if (tvEpgPick != null) {
                var words = (tvEpgQuery ?? ch.Title ?? "").ToLowerInvariant().Split(new[] { ' ', '-', '.', '|' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 1 && w != "hd" && w != "fhd").ToList();
                var hits = tvEpgPick.Select(kv => new { kv, score = words.Count(w => kv.Value.ToLowerInvariant().Contains(w) || kv.Key.Contains(w)) }).Where(x => x.score > 0).OrderByDescending(x => x.score).ThenBy(x => x.kv.Value.Length).Take(40);
                foreach (var h in hits) { var id = h.kv.Key; rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = h.kv.Value, Text = (has && mapped == id ? "In use  •  " : "") + id, Do = () => { TvMine.EpgMap[ch.Key] = id; TvMine.Save(); CloseListPage(); TvAfterActions(); } }); }
            }
            rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Back", Do = () => { tvActMode = "item"; TvFillActions(); } });
            return true;
        }
        if (tvActMode == "remind") {
            var r = tvRemindNow;
            p.Title.Text = "Starting now: " + Json.Str(r, "Title"); p.Sub.Text = "On " + Json.Str(r, "Channel") + ", at " + DateTime.Parse(Json.Str(r, "Start")).ToString("HH:mm") + ".";
            rows.Add(new ListRow { Glyph = "", C1 = "#3fb8a0", C2 = "#2f8fe0", Title = "Watch now", Do = () => { CloseListPage(); TvRemindWatch(r); } });
            rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Not now", Do = CloseListPage });
            return true;
        }
        return false;
    }
    // the rows a live channel adds to its menu
    void TvLiveActionRows(TvItem ch, List<ListRow> rows) {
        if (ch.ArchiveDays > 0) rows.Add(new ListRow { Glyph = "", C1 = "#2f8fe0", C2 = "#2f8fe0", Title = "TV Archive", Text = "Replay past programmes (" + ch.ArchiveDays + " days)", Do = () => TvArchiveMenu(ch) });
        rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Guide listings...", Text = TvMine.EpgMap.ContainsKey(ch.Key) ? "Matched by hand" : "Automatic", Do = () => TvEpgPicker(ch) });
        if (TvGuideOpen && tvGuideFrom != null && tvGuideFromAcc == tvAcc)
            rows.Add(new ListRow { Glyph = "\uE8FD", C1 = "#2f8fe0", C2 = "#2f8fe0", Title = "Guide: all channels", Text = "Now showing only " + tvGuideFromName + " (the category open in TV Live)", Do = () => { tvGuideFrom = null; CloseListPage(); TvOpenGuide(); } });
        if (TvGuideOpen) {
            string scope = TvMine.Scope(tvAcc, "guide"), s = TvMine.SortOf(scope);
            string name = s == "soon" ? "Starting soonest first" : s == "az" ? "A to Z" : "The provider's order";
            rows.Add(new ListRow { Glyph = "", C1 = "#8b6cff", C2 = "#3b1e9e", Title = "Guide order: " + name, Text = "Pick to change",
                Do = () => { TvMine.Sorts[scope] = s == "default" ? "soon" : s == "soon" ? "az" : "default"; TvMine.Save(); CloseListPage(); TvOpenGuide(); } });
        }
    }
    void TvEpgPicker(TvItem ch) {
        tvActItem = ch; tvActMode = "epg"; tvEpgPick = null; tvEpgQuery = null; TvFillActions();
        var acc = ch.Account;
        new Thread(() => {
            List<KeyValuePair<string, string>> l = null;
            try { TvFree.Guide(acc); Dictionary<string, string> names; lock (TvFree.GuideChannels) TvFree.GuideChannels.TryGetValue(TvFree.GuideKey(acc), out names); l = names != null ? names.ToList() : new List<KeyValuePair<string, string>>(); } catch { l = new List<KeyValuePair<string, string>>(); }
            W.Dispatcher.BeginInvoke(new Action(() => { tvEpgPick = l; if (ListPageOpen && lpOpen == tvActPage && tvActMode == "epg") TvFillActions(); }));
        }) { IsBackground = true }.Start();
    }

    // ---------- in the guide: A on a programme ----------
    void TvGuideAct(int row, int col) {
        if (row >= tvGuideRows.Count) return;
        var r = tvGuideRows[row]; var p = r.Progs[Math.Min(col, r.Progs.Count - 1)]; var now = DateTime.Now;
        if (p.Title == null || (p.Start <= now && p.Stop > now)) { TvGuideWatch(); return; }
        if (p.Stop <= now && TvCanReplay(r.Ch, p)) { Sound.Play(Sfx.Select); TvReplay(r.Ch, p); return; }
        tvActItem = r.Ch; tvActProg = p; tvActMode = "prog"; Sound.Play(Sfx.Open); TvOpenActions();
    }
    // the guide built again where you were (after a reminder or new listings)
    void TvGuideRebuild() {
        if (!TvGuideOpen || tvGuideChans == null) return;
        int row = tvGuideRow, col = tvGuideCol;
        tvGuideList.Children.Clear(); tvGuideRows.Clear();
        TvGuideBuildMore(Math.Max(40, row + 20));
        TvGuideFocus(row, null, false, col);
    }
    // Not in a category you hid (TV Live, Movies, Shows). Hidden categories stay out of the guide, the channel list while
    // watching and what's fetched beforehand (TvPrefetch.cs), unless "Show hidden categories" is on.
    Func<TvItem, bool> TvNotInHiddenCat(TvAccount acc, string mode) {
        string scope = TvMine.Scope(acc, mode);
        if (tvShowHiddenCats || !TvMine.HiddenCats.Any(h => h.StartsWith(scope + "|"))) return x => true;
        if (acc == null && mode == "live") {
            // free channels: their category is the list they come from; one that's also in a shown list stays
            var src = TvSources;
            var shown = new HashSet<string>(src.Where(kv => !TvMine.CatHidden(scope, TvLiveName(kv.Key))).SelectMany(kv => kv.Value).Select(x => x.Url ?? ""));
            return x => shown.Contains(x.Url ?? "");
        }
        return x => !TvMine.CatHidden(scope, x.Group ?? "Other");
    }
    // the channels in the guide's order: as the provider gives them (those with listings first), starting soonest, A to Z
    List<TvItem> TvGuideOrder(List<TvItem> chans, Dictionary<string, List<TvFree.Prog>> data) {
        var catOk = TvNotInHiddenCat(tvAcc, "live");
        chans = chans.Where(c => !TvMine.HiddenItems.Contains(c.Key) && TvAllowed(c) && catOk(c)).ToList();
        string s = TvMine.SortOf(TvMine.Scope(tvAcc, "guide")); var now = DateTime.Now;
        if (s == "az") return chans.OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (s == "soon") return chans.OrderBy(c => { var l = TvFree.For(data, c); var n = l != null ? l.FirstOrDefault(p => p.Start >= now) : null; return n != null ? n.Start : DateTime.MaxValue; }).ToList();
        return data.Count > 0 ? chans.Where(c => TvFree.For(data, c) != null).Concat(chans.Where(c => TvFree.For(data, c) == null)).ToList() : chans;
    }

    // ---------- reminders ----------
    DispatcherTimer tvRemindTimer; Dictionary<string, object> tvRemindNow;
    void TvStartReminders() {
        TvStore.Load(); TvMine.Load();
        TvSafeStore.Load(); if (TvSafeStore.Schedule.Count > 0) TvRecTimerOn();
        if (tvRemindTimer == null) { tvRemindTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) }; tvRemindTimer.Tick += (s, e) => TvCheckReminders(); }
        if (TvMine.Reminders.Count > 0) tvRemindTimer.Start(); else tvRemindTimer.Stop();
        TvCheckReminders();
    }
    void TvCheckReminders() {
        var now = DateTime.Now; bool changed = false;
        foreach (var r in TvMine.Reminders.ToList()) {
            DateTime st; if (!DateTime.TryParse(Json.Str(r, "Start"), null, System.Globalization.DateTimeStyles.RoundtripKind, out st)) { TvMine.Reminders.Remove(r); changed = true; continue; }
            st = st.ToLocalTime();
            if (now > st.AddMinutes(30)) { TvMine.Reminders.Remove(r); changed = true; continue; }
            if (now >= st.AddMinutes(-1)) {
                TvMine.Reminders.Remove(r); changed = true;
                Sound.Play(Sfx.Open);
                SetStep("Starting now: " + Json.Str(r, "Title") + " on " + Json.Str(r, "Channel") + ".", Ui.Teal);
                Notify("Starting now", Json.Str(r, "Title") + " on " + Json.Str(r, "Channel"));
                if (!TvPlaying) { tvRemindNow = r; tvActMode = "remind"; TvOpenActions(); }
            }
        }
        if (changed) { TvMine.Save(); if (TvMine.Reminders.Count == 0 && tvRemindTimer != null) tvRemindTimer.Stop(); if (TvGuideOpen) TvGuideRebuild(); }
    }
    void TvRemindWatch(Dictionary<string, object> r) {
        var ch = TvMine.Recall(Json.Str(r, "Key")); if (ch == null) return;
        if (!TvOpen) OpenTv();
        int idx = ch.Account == null ? 0 : TvStore.Accounts.IndexOf(ch.Account) + 1;
        if (TvProfilesOpen) TvProfileAct(Math.Max(0, idx), false); else if (tvAcc != ch.Account) { tvAcc = ch.Account; TvApplyProfile(); }
        TvPlay(ch, null);
    }

    // ---------- the guide's settings, in a profile's settings ----------
    IEnumerable<ListRow> TvGuideRows() {
        string id = tvAccFree ? "free" : tvAccEdit.Id, url; TvMine.EpgUrls.TryGetValue(id, out url);
        int shift = TvMine.ShiftOf(id), hours = TvMine.HoursOf(id), prev = TvMine.PreviewMs;
        Func<TvAccount> acc = () => tvAccFree ? null : TvStore.Accounts.FirstOrDefault(a => a.Id == id) ?? tvAccEdit;
        yield return new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Guide link: " + (string.IsNullOrEmpty(url) ? "the usual one" : "your own"), Text = string.IsNullOrEmpty(url) ? (tvAccFree ? "open-epg.com's Dutch listings" : "your provider's own guide") + "  •  pick to use another XMLTV link (.xml or .gz)" : url,
            Do = () => OpenOsk("An XMLTV guide link (empty: the usual one)", url ?? "", null, v => { v = (v ?? "").Trim(); if (v.Length == 0) TvMine.EpgUrls.Remove(id); else TvMine.EpgUrls[id] = v; TvMine.Save(); TvFree.ForgetGuide(acc()); TvFillAccountForm(); }, null, false, true) };
        yield return new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Guide time shift: " + (shift > 0 ? "+" : "") + shift + " h", Text = "When the guide runs early or late; pick to change",
            Do = () => { TvMine.EpgShifts[id] = shift >= 5 ? -5 : shift + 1; TvMine.Save(); TvFree.ForgetGuide(acc()); TvFillAccountForm(); } };
        yield return new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Guide fetched again: every " + hours + " hours", Text = "Pick to change",
            Do = () => { TvMine.EpgHours[id] = hours == 3 ? 6 : hours == 6 ? 12 : hours == 12 ? 24 : 3; TvMine.Save(); TvFillAccountForm(); } };
        yield return new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Fetch the guide now", Do = () => { TvFree.ForgetGuide(acc()); SetStep("TV: the guide is fetched again the next time it's opened.", Ui.Teal); } };
        yield return new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Guide preview: " + (prev <= 0 ? "off" : "after " + (prev / 1000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " s"), Text = "The picked channel playing small in the guide (every profile)",
            Do = () => { TvMine.PreviewMs = prev == 500 ? 900 : prev == 900 ? 2000 : prev == 2000 ? 0 : 500; TvMine.Save(); TvFillAccountForm(); } };
    }

    // ---------- in the player: the channel list (categories, and the channels with what's on) ----------
    Border tvMiniCard; StackPanel tvMiniCats, tvMiniList; readonly List<Border> tvMiniCatRows = new List<Border>(), tvMiniRows = new List<Border>();
    List<KeyValuePair<string, List<TvItem>>> tvMiniGroups = new List<KeyValuePair<string, List<TvItem>>>(); List<TvItem> tvMiniItems = new List<TvItem>();
    int tvMiniCat, tvMiniIdx; bool tvMiniInCh; Dictionary<string, List<TvFree.Prog>> tvMiniGuide;
    bool TvMiniOpen { get { return tvMiniCard != null && tvMiniCard.Visibility == Visibility.Visible; } }
    // the profile's channels by category (the provider's order; the free profile's own groups)
    List<KeyValuePair<string, List<TvItem>>> TvMiniData() {
        if (TvLiveOpen && TvBrowseChannels && tvLiveGroups.Count > 0) return tvLiveGroups.Where(g => g.Value.Count > 0 && g.Value.All(x => x.Kind == "live")).Select(g => new KeyValuePair<string, List<TvItem>>(g.Key, g.Value)).ToList();
        var r = new List<KeyValuePair<string, List<TvItem>>>();
        if (tvAcc != null && tvAcc.Loaded) {
            var all = (tvAcc.Live ?? new List<TvItem>()).Where(x => TvAllowed(x) && !TvMine.HiddenItems.Contains(x.Key)).ToList();
            var order = (tvAcc.LiveCats != null ? tvAcc.LiveCats.Values : Enumerable.Empty<string>()).Distinct().ToList();
            foreach (var g in all.Select(c => c.Group ?? "Other").Distinct()) if (!order.Contains(g)) order.Add(g);
            var by = all.GroupBy(c => c.Group ?? "Other").ToDictionary(g => g.Key, g => g.ToList());
            string scope = TvMine.Scope(tvAcc, "live");
            foreach (var n in order) { List<TvItem> l; if (by.TryGetValue(n, out l) && l.Count > 0 && (tvShowHiddenCats || !TvMine.CatHidden(scope, n))) r.Add(new KeyValuePair<string, List<TvItem>>(n, l)); }
        } else if (tvAcc == null) {
            string scope = TvMine.Scope(null, "live");
            foreach (var kv in TvSources) if (tvShowHiddenCats || !TvMine.CatHidden(scope, TvLiveName(kv.Key))) r.Add(new KeyValuePair<string, List<TvItem>>(TvLiveName(kv.Key), kv.Value));
        }
        if (r.Count == 0 && tvPlayList != null) r.Add(new KeyValuePair<string, List<TvItem>>("Channels", tvPlayList));
        return r;
    }
    void TvOpenMini() {
        if (tvPlaying == null || tvPlaying.Kind != "live") return;
        if (tvMiniCard == null) {
            tvMiniCats = new StackPanel(); tvMiniList = new StackPanel();
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(new ScrollViewer { Content = tvMiniCats, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Margin = new Thickness(0, 0, 10, 0) });
            var right = new ScrollViewer { Content = tvMiniList, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false }; Grid.SetColumn(right, 1); grid.Children.Add(right);
            tvMiniCard = new Border { Child = grid, Width = 820, Background = TvTheme.Brush("#F2101012"), BorderBrush = TvTheme.Brush("#2EFFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(12),
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(36, 10, 0, 10), Visibility = Visibility.Collapsed,
                Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 0, Opacity = 0.7, Color = Colors.Black } };
            Grid.SetRow(tvMiniCard, 1); tvOverlayRoot.Children.Add(tvMiniCard);
        }
        tvMiniGroups = TvMiniData();
        tvMiniCat = Math.Max(0, tvMiniGroups.FindIndex(g => g.Value.Any(x => x.Key == tvPlaying.Key)));
        tvMiniCats.Children.Clear(); tvMiniCatRows.Clear();
        for (int i = 0; i < tvMiniGroups.Count; i++) {
            int idx = i; var kv = tvMiniGroups[i];
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new TextBlock { Text = kv.Key, FontSize = 16, FontFamily = Ui.Semi, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var n = new TextBlock { Text = kv.Value.Count.ToString(), FontSize = 13, FontFamily = Ui.Semi, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) }; Grid.SetColumn(n, 1); g.Children.Add(n);
            var b = new Border { Child = g, Height = 46, Padding = new Thickness(16, 0, 14, 0), CornerRadius = new CornerRadius(23), Margin = new Thickness(0, 0, 0, 3), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            TvOnHover(b, () => { if (tvMiniCat != idx || tvMiniInCh) { tvMiniInCh = false; TvMiniShowCat(idx); } });
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; tvMiniInCh = false; TvMiniShowCat(idx); };
            tvMiniCatRows.Add(b); tvMiniCats.Children.Add(b);
        }
        tvMiniInCh = true;
        tvMiniCard.Visibility = Visibility.Visible; Sound.Play(Sfx.Open);
        TvMiniShowCat(tvMiniCat);
        tvMiniIdx = Math.Max(0, tvMiniItems.FindIndex(x => x.Key == tvPlaying.Key)); TvMarkMini();
        TvWake();
        // what's on, from the guide
        var acc = tvAcc;
        if (tvMiniGuide == null) new Thread(() => { Dictionary<string, List<TvFree.Prog>> g = null; try { g = TvFree.Guide(acc); } catch { } W.Dispatcher.BeginInvoke(new Action(() => { tvMiniGuide = g; if (TvMiniOpen) TvMiniShowCat(tvMiniCat, true); })); }) { IsBackground = true }.Start();
    }
    void TvMiniShowCat(int i, bool keep = false) {
        if (i < 0 || i >= tvMiniGroups.Count) return;
        tvMiniCat = i; int was = tvMiniIdx;
        tvMiniItems = tvMiniGroups[i].Value.Take(300).ToList();
        tvMiniList.Children.Clear(); tvMiniRows.Clear(); var now = DateTime.Now;
        for (int k = 0; k < tvMiniItems.Count; k++) {
            var ch = tvMiniItems[k]; int idx = k;
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var logo = new Image { Width = 44, Height = 30, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left }; if (!string.IsNullOrEmpty(ch.Image)) TvLoadImage(logo, ch.Image, 90, null); g.Children.Add(logo);
            var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(t, 1);
            t.Children.Add(new TextBlock { Text = ch.Title, FontSize = 15.5, FontFamily = Ui.Semi, TextTrimming = TextTrimming.CharacterEllipsis });
            string on = ""; if (tvMiniGuide != null) { var l = TvFree.For(tvMiniGuide, ch); var p = l != null ? l.FirstOrDefault(x => x.Start <= now && x.Stop > now) : null; if (p != null) on = p.Start.ToString("HH:mm") + "  " + p.Title; }
            t.Children.Add(new TextBlock { Text = on, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.75, Visibility = on.Length > 0 ? Visibility.Visible : Visibility.Collapsed });
            g.Children.Add(t);
            var b = new Border { Child = g, Height = 56, Padding = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            TvOnHover(b, () => { if (tvMiniIdx != idx || !tvMiniInCh) { tvMiniInCh = true; tvMiniIdx = idx; TvMarkMini(); } });
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; tvMiniIdx = idx; tvMiniInCh = true; TvMiniPick(); };
            tvMiniRows.Add(b); tvMiniList.Children.Add(b);
        }
        tvMiniIdx = keep ? Math.Min(was, Math.Max(0, tvMiniRows.Count - 1)) : 0;
        TvMarkMini();
    }
    void TvMarkMini() {
        for (int i = 0; i < tvMiniCatRows.Count; i++) {
            bool cur = i == tvMiniCat, on = cur && !tvMiniInCh;
            tvMiniCatRows[i].Background = on ? Brushes.White : cur ? TvTheme.Brush("#3A3A3C") : Brushes.Transparent;
            foreach (var t in ((Grid)tvMiniCatRows[i].Child).Children.OfType<TextBlock>()) t.Foreground = on ? Brushes.Black : cur ? Brushes.White : TvTheme.Brush("#A6FFFFFF");
            if (cur && !tvHoverMark) tvMiniCatRows[i].BringIntoView();
        }
        for (int i = 0; i < tvMiniRows.Count; i++) {
            bool on = tvMiniInCh && i == tvMiniIdx, cur = tvPlaying != null && tvMiniItems[i].Key == tvPlaying.Key;
            tvMiniRows[i].Background = on ? Brushes.White : cur ? TvTheme.Brush("#33FFFFFF") : Brushes.Transparent;
            foreach (var t in ((StackPanel)((Grid)tvMiniRows[i].Child).Children[1]).Children.OfType<TextBlock>()) t.Foreground = on ? Brushes.Black : Brushes.White;
            if (on && !tvHoverMark) tvMiniRows[i].BringIntoView();
        }
    }
    void TvMiniPick() {
        if (tvMiniIdx >= tvMiniItems.Count) return;
        var ch = tvMiniItems[tvMiniIdx]; tvMiniCard.Visibility = Visibility.Collapsed;
        if (tvPlaying != null && ch.Key == tvPlaying.Key) return;
        Sound.Play(Sfx.Select); TvPlay(ch, tvMiniItems);
    }
    // up / down: move; right or A on a category: its channels; A on a channel: watch it; left / B in the channels: back to
    // the categories; B in the categories: leave the player
    bool TvMiniPad(int bit) {
        if (!TvMiniOpen) return false;
        if (!tvMiniInCh) {
            if (bit == Pad.Up || bit == Pad.Down) { int j = tvMiniCat + (bit == Pad.Up ? -1 : 1); if (j >= 0 && j < tvMiniGroups.Count) { Sound.Play(Sfx.Move); TvMiniShowCat(j); } else Sound.Play(Sfx.Bump); }
            else if (bit == Pad.Right || bit == Pad.A) { if (tvMiniRows.Count > 0) { tvMiniInCh = true; tvMiniIdx = 0; Sound.Play(Sfx.Move); TvMarkMini(); } }
            else if (bit == Pad.B || bit == Pad.Back) { tvMiniCard.Visibility = Visibility.Collapsed; TvStop(); }
            else if (bit == Pad.X) { Sound.Play(Sfx.Back); tvMiniCard.Visibility = Visibility.Collapsed; }
            return true;
        }
        if (bit == Pad.Up || bit == Pad.Down) { int j = tvMiniIdx + (bit == Pad.Up ? -1 : 1); if (j >= 0 && j < tvMiniRows.Count) { tvMiniIdx = j; Sound.Play(Sfx.Move); TvMarkMini(); } else Sound.Play(Sfx.Bump); }
        else if (bit == Pad.A) TvMiniPick();
        else if (bit == Pad.Left || bit == Pad.B || bit == Pad.Back) { tvMiniInCh = false; Sound.Play(Sfx.Move); TvMarkMini(); }
        else if (bit == Pad.X) { Sound.Play(Sfx.Back); tvMiniCard.Visibility = Visibility.Collapsed; }
        return true;
    }
}
}
