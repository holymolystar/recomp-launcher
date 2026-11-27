// TV & Movies: the menus for an item and for a category (TvMine.cs keeps what they change).
//   An item (X or a right-click on a tile, or "More" on its page): play or open, My list, tags, watched or not, move it
//   in My list, hide a channel.
//   A category (X or a right-click in the list of categories): move it, rename it, hide it, how the page is sorted,
//   show the hidden ones, reset the page.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace RecompLauncher {

partial class Shell {
    ListPage tvActPage; string tvActMode; TvItem tvActItem; string tvActScope, tvActCat; List<string> tvActCats; bool tvShowHiddenCats;

    void TvOpenActions() {
        if (tvActPage == null) {
            tvActPage = MakeListPage("TvActions", "TV & MOVIES", "", 560, null);
            tvActPage.Layer.SetValue(Panel.ZIndexProperty, 900);
            tvActPage.Fill = TvFillActions;
        }
        OpenListPage(tvActPage);
    }
    void TvItemActions(TvItem it) {
        if (it == null || it.Kind == "add" || it.Kind == "account" || it.Kind == "category" || it.Kind == "folder" || it.Kind == "addfolder") return;
        tvActItem = it; tvActMode = "item"; Sound.Play(Sfx.Open); TvOpenActions();
    }
    void TvCatActions(string scope, string cat, List<string> cats) {
        tvActScope = scope; tvActCat = cat; tvActCats = cats; tvActMode = "cat"; Sound.Play(Sfx.Open); TvOpenActions();
    }
    void TvFillActions() {
        var p = tvActPage; var rows = new List<ListRow>();
        if (TvFillLiveActions(p, rows) || TvFillLangs(p, rows)) { FillRows(p, rows, ""); return; }
        if (tvActMode == "item" || tvActMode == "tags") {
            var it = tvActItem;
            p.Title.Text = it.Title;
            if (tvActMode == "tags") {
                p.Sub.Text = "Tags make your own groups of favourites. Each tag gets its own row on For You and its own category.";
                var mine = TvMine.TagsOf(it);
                foreach (var tag in TvMine.TagNames) {
                    string t = tag;
                    rows.Add(new ListRow { Glyph = mine.Contains(t) ? "" : "", C1 = mine.Contains(t) ? "#3fb8a0" : "#33424d", C2 = mine.Contains(t) ? "#2f8fe0" : "#33424d", Title = t,
                        Text = mine.Contains(t) ? "In this tag  •  pick to take it out" : "Pick to add it", Do = () => { TvMine.ToggleTag(it, t); TvFillActions(); } });
                }
                rows.Add(new ListRow { Glyph = "", C1 = "#3fb8a0", C2 = "#2f8fe0", Title = "New tag...", Do = () => OpenOsk("A name for the tag (for example Sport)", "", null, v => {
                    v = (v ?? "").Trim(); if (v.Length == 0) return; if (!TvMine.TagsOf(it).Contains(v)) TvMine.ToggleTag(it, v); TvFillActions(); }, null) });
                rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Back", Do = () => { tvActMode = "item"; TvFillActions(); } });
                FillRows(p, rows, ""); return;
            }
            bool live = it.Kind == "live", fav = TvStore.IsFav(it), watched = TvMine.IsWatched(it);
            var tags = TvMine.TagsOf(it);
            p.Sub.Text = string.Join("  •  ", new[] { live ? "Live channel" : it.Kind == "series" ? "Series" : it.Kind == "episode" ? "Episode" : "Film", it.Group, tags.Count > 0 ? "Tags: " + string.Join(", ", tags) : null }.Where(x => !string.IsNullOrEmpty(x)));
            rows.Add(new ListRow { Glyph = "", C1 = "#3fb8a0", C2 = "#2f8fe0", Title = live ? "Watch" : it.Kind == "series" ? "Open" : "Play", Do = () => { CloseListPage(); if (live || it.Kind == "episode") TvPlay(it, null); else TvOpenDetail(it); } });
            rows.Add(new ListRow { Glyph = fav ? "" : "", C1 = "#c28a1e", C2 = "#e0b33c", Title = fav ? "Remove from My list" : "Add to My list", Do = () => { TvStore.ToggleFav(it); TvAfterActions(); TvFillActions(); } });
            if (fav) {
                rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Move to the front of My list", Do = () => { TvStore.Favs.Remove(it.Key); TvStore.Favs.Insert(0, it.Key); TvStore.Save(); TvAfterActions(); TvFillActions(); } });
                rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Move to the back of My list", Do = () => { TvStore.Favs.Remove(it.Key); TvStore.Favs.Add(it.Key); TvStore.Save(); TvAfterActions(); TvFillActions(); } });
            }
            if (!live && it.Kind != "series") {
                rows.Add(new ListRow { Glyph = "", C1 = "#2f8fe0", C2 = "#2f8fe0", Title = "Play next", Text = "After what's playing now", Do = () => { TvQueueAdd(it, true); CloseListPage(); } });
                rows.Add(new ListRow { Glyph = "", C1 = "#2f8fe0", C2 = "#2f8fe0", Title = "Add to the queue", Text = tvQueue.Count > 0 ? tvQueue.Count + " in the queue" : null, Do = () => { TvQueueAdd(it, false); CloseListPage(); } });
            }
            if (live) rows.Add(new ListRow { Glyph = "", C1 = "#2f8fe0", C2 = "#2f8fe0", Title = "Watch in the multi-view", Text = "Up to four channels at once" + (tvMultiItems.Count > 0 ? "  •  " + tvMultiItems.Count + " in it now" : ""), Do = () => TvMultiAdd(it) });
            if (it.Kind == "series" || it.Kind == "episode") {
                string sk = it.Kind == "series" ? it.Title : (it.Group ?? it.Title); TvPlayerPrefs.Load(); int iv; if (!TvPlayerPrefs.Intro.TryGetValue(sk ?? "", out iv)) iv = 85;
                rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Skip intro jumps: " + (iv <= 0 ? "off" : iv + " seconds"), Text = "For " + sk + "  •  pick to change",
                    Do = () => { int[] opts = { 30, 45, 60, 85, 90, 120, 0 }; int k = Array.IndexOf(opts, iv); TvPlayerPrefs.Intro[sk ?? ""] = opts[(k + 1) % opts.Length]; TvPlayerPrefs.Save(); TvFillActions(); } });
            }
            rows.Add(new ListRow { Glyph = "", C1 = "#8b6cff", C2 = "#3b1e9e", Title = "Tags...", Text = tags.Count > 0 ? string.Join(", ", tags) : "Put it in your own groups", Do = () => { tvActMode = "tags"; TvFillActions(); } });
            foreach (var who in TvCastOf(it).Take(3)) { var w2 = who; rows.Add(new ListRow { Glyph = "\uE77B", C1 = "#33424d", C2 = "#33424d", Title = "More with " + w2, Do = () => TvOpenPerson(w2) }); }
            TvMetaRows(it, rows);
            if (!live && it.Kind != "series") rows.Add(new ListRow { Glyph = watched ? "" : "", C1 = "#33424d", C2 = "#33424d", Title = watched ? "Mark as not watched" : "Mark as watched",
                Do = () => { TvMine.SetWatched(it, !watched); TvAfterActions(); TvFillActions(); } });
            if (it.Kind == "series" && it.Episodes != null && it.Episodes.Count > 0) {
                bool all = it.Episodes.All(TvMine.IsWatched);
                rows.Add(new ListRow { Glyph = all ? "" : "", C1 = "#33424d", C2 = "#33424d", Title = all ? "Mark the whole series as not watched" : "Mark the whole series as watched",
                    Do = () => { foreach (var e in it.Episodes) { if (all) TvMine.Watched.Remove(e.Key); else { TvMine.Watched.Add(e.Key); TvStore.Progress.Remove(e.Key); } } TvMine.Save(); TvStore.Save(); TvAfterActions(); TvFillActions(); } });
            }
            if (live) {
            TvSafeActionRows(it, rows);
                TvLiveActionRows(it, rows);
                bool hidden = TvMine.HiddenItems.Contains(it.Key);
                rows.Add(new ListRow { Glyph = hidden ? "" : "", C1 = "#c0453c", C2 = "#c0453c", Title = hidden ? "Show this channel again" : "Hide this channel",
                    Text = hidden ? null : "Out of TV Live and the guide; the Hidden category brings it back", Do = () => { if (hidden) TvMine.HiddenItems.Remove(it.Key); else { TvMine.HiddenItems.Add(it.Key); TvMine.Remember(it); } TvMine.Save(); CloseListPage(); TvAfterActions(); } });
            }
        } else {
            // a category
            string scope = tvActScope, cat = tvActCat; var cats = tvActCats;
            string name = TvMine.NameOf(scope, cat); bool hidden = TvMine.CatHidden(scope, cat), renamed = name != cat; string sort = TvMine.SortOf(scope);
            p.Title.Text = name; p.Sub.Text = (renamed ? "Called \"" + cat + "\" by the provider.  " : "") + "These changes are for this profile and this page only.";
            rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Move up", Do = () => { TvMine.Move(scope, cats, cat, -1); tvActCats = TvMine.Order(scope, cats); TvAfterActions(); TvFillActions(); } });
            rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Move down", Do = () => { TvMine.Move(scope, cats, cat, 1); tvActCats = TvMine.Order(scope, cats); TvAfterActions(); TvFillActions(); } });
            rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Move to the top", Do = () => { TvMine.Move(scope, cats, cat, int.MinValue); tvActCats = TvMine.Order(scope, cats); TvAfterActions(); TvFillActions(); } });
            rows.Add(new ListRow { Glyph = "", C1 = "#2f8fe0", C2 = "#2f8fe0", Title = "Rename...", Text = renamed ? "Now: " + name : null, Do = () => OpenOsk("A new name for " + cat, name, null, v => {
                v = (v ?? "").Trim(); if (v.Length == 0 || v == cat) TvMine.Renames.Remove(scope + "|" + cat); else TvMine.Renames[scope + "|" + cat] = v; TvMine.Save(); TvAfterActions(); TvFillActions(); }, null) });
            if (renamed) rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Back to \"" + cat + "\"", Do = () => { TvMine.Renames.Remove(scope + "|" + cat); TvMine.Save(); TvAfterActions(); TvFillActions(); } });
            rows.Add(new ListRow { Glyph = hidden ? "" : "", C1 = "#c0453c", C2 = "#c0453c", Title = hidden ? "Show this category again" : "Hide this category",
                Do = () => {
                    if (hidden) TvMine.HiddenCats.Remove(scope + "|" + cat); else TvMine.HiddenCats.Add(scope + "|" + cat);
                    TvMine.Save(); CloseListPage(); TvAfterActions();
                    // shown again: its posters and details are fetched like the rest (channels come back by themselves)
                    if (hidden && tvAcc != null && (scope.EndsWith("|movies") || scope.EndsWith("|series"))) TvPrefetch(tvAcc);
                } });
            rows.Add(new ListRow { Glyph = "", C1 = "#8b6cff", C2 = "#3b1e9e", Title = "Sorting: " + TvMine.SortName(sort), Text = "Pick to change: " + TvMine.SortName(TvMine.NextSort(sort)),
                Do = () => { TvMine.Sorts[scope] = TvMine.NextSort(sort); TvMine.Save(); TvAfterActions(); TvFillActions(); } });
            rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = tvShowHiddenCats ? "Don't show hidden categories" : "Show hidden categories",
                Do = () => { tvShowHiddenCats = !tvShowHiddenCats; CloseListPage(); TvAfterActions(); } });
            rows.Add(new ListRow { Glyph = "", C1 = "#c0453c", C2 = "#c0453c", Title = "Reset this page", Text = "The provider's order and names again, nothing hidden", Do = () => { TvMine.ResetScope(scope); CloseListPage(); TvAfterActions(); } });
        }
        FillRows(p, rows, "");
    }
    // ---------- the tab a profile opens on ----------
    static readonly string[] TvStartTabs = { "home", "iptv-movies", "iptv-series", "iptv-live", "guide" };
    string TvStartPage() { string s; return TvMine.StartPages.TryGetValue(TvMine.Profile(tvAcc), out s) && TvStartTabs.Contains(s) ? s : "home"; }
    ListRow TvStartRow() {
        string id = tvAccFree ? "free" : tvAccEdit.Id, cur;
        if (!TvMine.StartPages.TryGetValue(id, out cur) || !TvStartTabs.Contains(cur)) cur = "home";
        string name = TvRailDef.First(d => d[0] == cur)[2];
        return new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Opens on: " + name, Text = "Pick to change which tab this profile starts on",
            Do = () => { int i = Array.IndexOf(TvStartTabs, cur); TvMine.StartPages[id] = TvStartTabs[(i + 1) % TvStartTabs.Length]; TvMine.Save(); TvFillAccountForm(); } };
    }
    // ---------- the profile's theme (TvThemes.cs) ----------
    ListRow TvThemeRow() {
        string id = tvAccFree ? "free" : tvAccEdit.Id;
        var cur = TvTheme.Of(id);
        return new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Theme: " + cur.Name, Text = cur.Desc + "  •  pick to change",
            Do = () => {
                var next = TvTheme.All[(Array.IndexOf(TvTheme.All, cur) + 1) % TvTheme.All.Length];
                TvMine.Themes[id] = next.Id; TvMine.Save();
                if (TvMine.Profile(tvAcc) == id) TvTheme.Use(next);   // the profile being watched: right away
                TvFillAccountForm();
            } };
    }
    // what's on screen follows the change
    void TvAfterActions() {
        if (TvGuideOpen) TvGuideRebuild();
        else if (TvLiveOpen) { string cur = tvLiveCat < tvLiveOrig.Count ? tvLiveOrig[tvLiveCat] : null; bool inGrid = tvLiveInGrid; int idx = tvLiveIdx; TvOpenBrowse(tvBrowseMode, cur, inGrid ? idx : -1); }
        else if (TvDetailOpen && tvDetailItem != null) TvFillDetail(tvDetailItem);
        else if (tvFocus != null) ShowTvHero(tvFocus);
    }
}
}
