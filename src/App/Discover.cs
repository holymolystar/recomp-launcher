// Discover (the page of every game, formerly All games): two dropdowns instead of rows of pills. "Show" picks All,
// Installed, Not installed, Updates, Favourites or a collection (LB steps through them); the console one picks the
// console a game was made for (LS steps through), with Steam, Epic Games and GOG under PC.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace RecompLauncher {

partial class Shell {
    // ---------- the console filter ("PC:steam" is the PC games from one store) ----------
    string consoleFilter = "all";
    bool InConsole(Game g) {
        if (consoleFilter == "all") return true;
        int c = consoleFilter.IndexOf(':');
        if (c > 0) return PlatformOf(g) == consoleFilter.Substring(0, c) && StoreOf(g) == consoleFilter.Substring(c + 1);
        return PlatformOf(g) == consoleFilter;
    }
    static readonly string[][] PcStores = { new[] { "recomp", "Recomps and ports" }, new[] { "steam", "Steam" }, new[] { "epic", "Epic Games" }, new[] { "gog", "GOG" } };
    List<string> ConsolesInLibrary() {
        return Listed().Select(PlatformOf).Where(p => p != null).Distinct().OrderBy(PlatformRank).ToList();
    }
    // id, label, count, and "sub" for a store under PC
    List<string[]> ConsoleChoices() {
        var list = Listed();
        var l = new List<string[]> { new[] { "all", "All consoles", "", "" } };
        foreach (var p in ConsolesInLibrary()) {
            string pp = p;
            l.Add(new[] { p, p, list.Count(g => PlatformOf(g) == pp).ToString(), "" });
            if (p != "PC") continue;
            var st = PcStores.Where(s => list.Any(g => PlatformOf(g) == "PC" && StoreOf(g) == s[0])).ToList();
            if (st.Count > 1)
                foreach (var s in st) { string sid = s[0]; l.Add(new[] { "PC:" + sid, s[1], list.Count(g => PlatformOf(g) == "PC" && StoreOf(g) == sid).ToString(), "sub" }); }
        }
        return l;
    }
    void SetConsole(string p) {
        consoleFilter = p ?? "all"; Sound.Play(Sfx.Move);
        UpdateChips(); ApplyFilter(); QueueScroll(); SetHints();
    }
    void CycleConsole(int dir) {
        var l = ConsoleChoices().Select(c => c[0]).ToList();
        int i = (Math.Max(0, l.IndexOf(consoleFilter)) + dir + l.Count) % l.Count;
        SetConsole(l[i]);
    }

    // ---------- the two dropdowns ----------
    Border showPill, consolePill, sortPill; TextBlock showPillText, consolePillText, sortPillText;
    Border MakeDropPill(string glyph, out TextBlock text) {
        text = new TextBlock { FontSize = 13, FontFamily = Ui.Semi, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.Mdl, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) });
        row.Children.Add(text);
        row.Children.Add(new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 2, 0, 0) });
        return new Border { CornerRadius = new CornerRadius(16), Height = 32, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand, Child = row };
    }
    // the dropdowns and their button hints, after the layout buttons
    void EnsureDropPills() {
        if (showPill != null) return;
        showPill = MakeDropPill("", out showPillText);
        showPill.MouseLeftButtonUp += (s, e) => OpenDrop(0);
        consolePill = MakeDropPill("", out consolePillText);
        consolePill.MouseLeftButtonUp += (s, e) => OpenDrop(1);
        int at = homeFilterPills.Children.IndexOf(homeLayoutPills) + 1;
        homeFilterPills.Children.Insert(at++, showPill);
        homeFilterPills.Children.Insert(at++, PillHint("LB"));
        homeFilterPills.Children.Insert(at++, consolePill);
        homeFilterPills.Children.Insert(at++, PillHint("LS"));
        sortPill = MakeDropPill("\uE8CB", out sortPillText);
        sortPill.MouseLeftButtonUp += (s, e) => OpenDrop(2);
        homeFilterPills.Children.Insert(at++, sortPill);
    }
    static TextBlock PillHint(string t) { return new TextBlock { Text = t, FontSize = 11, Foreground = Ui.Brush(Ui.Dim), Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center }; }
    void UpdateDropPills() {
        if (showPill == null) return;
        var list = Listed();
        var def = HomeFilters().FirstOrDefault(f => f[0] == filter);
        showPillText.Text = filter == "all" ? "All games" : (def != null ? def[1] : filter) + "  " + list.Count(g => InFilter(g, filter));
        PaintPill(showPill, filter != "all");
        var c = ConsoleChoices().FirstOrDefault(x => x[0] == consoleFilter);
        consolePillText.Text = c == null || consoleFilter == "all" ? "All consoles" : (c[3] == "sub" ? "PC • " : "") + c[1] + "  " + c[2];
        PaintPill(consolePill, consoleFilter != "all");
        sortPillText.Text = SortLabel(sort);
        PaintPill(sortPill, false);
    }
    static readonly string[] SortChoices = { "default", "recent", "played", "installed", "name" };
    // play time in the launcher, or Steam's own count when that is more
    static double PlayedSeconds(Game g) {
        double t = Playtime.Total(g.Id);
        if (g.Steam != null) { var s = SteamGames.StatsOf(g); if (s != null && s.Minutes * 60.0 > t) t = s.Minutes * 60.0; }
        return t;
    }
    void PaintPill(Border b, bool on) {
        b.Background = Ui.Brush(on ? Ui.Teal : Raised);
        var fg = Ui.Brush(on ? (Theme.Current.Light ? "#ffffff" : "#0A0E11") : Ui.Text);
        foreach (var t in ((StackPanel)b.Child).Children.OfType<TextBlock>()) t.Foreground = fg;
    }

    // ---------- a dropdown list under a pill, in the launcher's own look ----------
    // which dropdown: 0 Show, 1 console, 2 sort
    void OpenDrop(int which) {
        EnsureDropPills();
        which = (which % 3 + 3) % 3;
        if (dropPopup != null) dropPopup.IsOpen = false;
        List<DropItem> items;
        if (which == 0) items = HomeFilters().Select(f => {
            string id = f[0];
            return new DropItem { Label = f[1], Count = id == "all" ? "" : Listed().Count(g => InFilter(g, id)).ToString(), On = filter == id, Do = () => SetFilter(id) };
        }).ToList();
        else if (which == 1) items = ConsoleChoices().Select(c => {
            string id = c[0];
            return new DropItem { Label = c[1], Count = c[2], On = consoleFilter == id, Sub = c[3] == "sub", Do = () => SetConsole(id) };
        }).ToList();
        else items = SortChoices.Select(c => {
            string id = c;
            return new DropItem { Label = SortLabel(id), Count = "", On = sort == id, Do = () => { Sound.Play(Sfx.Move); SetSort(id); ApplyFilter(); UpdateChips(); } };
        }).ToList();
        dropWhich = which;
        ShowDropdown(which == 0 ? showPill : which == 1 ? consolePill : sortPill, items);
    }
    // ---------- the controller in an open dropdown: up / down move, A picks, B closes, LB / RB the other menus ----------
    bool dropHooked;
    Popup dropPopup; List<Border> dropRows = new List<Border>(); List<DropItem> dropItems; int dropIdx, dropWhich;
    bool DropOpen { get { return dropPopup != null && dropPopup.IsOpen; } }
    void DropPad(int bit) {
        if (bit == Pad.Up || bit == Pad.Down) {
            int n = dropRows.Count; if (n == 0) return;
            int was = dropIdx;
            dropIdx = Math.Max(0, Math.Min(n - 1, dropIdx + (bit == Pad.Down ? 1 : -1)));
            Sound.Play(dropIdx != was ? Sfx.Move : Sfx.Bump);
            MarkDrop();
        }
        else if (bit == Pad.A) { var it = dropItems[dropIdx]; dropPopup.IsOpen = false; it.Do(); }
        else if (bit == Pad.B || bit == Pad.LS) { Sound.Play(Sfx.Back); dropPopup.IsOpen = false; }
        else if (bit == Pad.LB || bit == Pad.RB) OpenDrop(dropWhich + (bit == Pad.RB ? 1 : -1));
    }
    void MarkDrop() {
        for (int i = 0; i < dropRows.Count; i++)
            dropRows[i].Background = i == dropIdx && padUsed ? Ui.Brush(Theme.Current.Light ? "#22000000" : "#2EFFFFFF") : Brushes.Transparent;
        if (dropIdx < dropRows.Count) dropRows[dropIdx].BringIntoView();
    }
    class DropItem { public string Label, Count; public bool On, Sub; public Action Do; }
    void ShowDropdown(FrameworkElement anchor, List<DropItem> items) {
        Sound.Play(Sfx.Open);
        dropRows = new List<Border>(); dropItems = items;
        dropIdx = Math.Max(0, items.FindIndex(x => x.On));
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, VerticalOffset = 6, StaysOpen = true, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
        var rows = new StackPanel { MinWidth = 220 };
        foreach (var it in items) {
            var item = it;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var check = new TextBlock { Text = item.On ? "" : "", FontFamily = Ui.Mdl, FontSize = 12, Foreground = Ui.Brush(Ui.Teal), VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { Text = item.Label, FontSize = 14, FontFamily = item.On ? Ui.Semi : W.FontFamily, Foreground = Ui.Brush(item.On ? Ui.Teal : Ui.Text),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(item.Sub ? 18 : 0, 0, 16, 0) };
            var count = new TextBlock { Text = item.Count, FontSize = 13, Foreground = Ui.Brush(Ui.Muted), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1); Grid.SetColumn(count, 2);
            g.Children.Add(check); g.Children.Add(label); g.Children.Add(count);
            var row = new Border { Padding = new Thickness(10, 8, 12, 8), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = g };
            row.MouseEnter += (s, e) => row.Background = Ui.Brush(Theme.Current.Light ? "#14000000" : "#1FFFFFFF");
            row.MouseLeave += (s, e) => row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (s, e) => { popup.IsOpen = false; item.Do(); };
            rows.Children.Add(row); dropRows.Add(row);
        }
        var card = new Border { Background = Ui.Brush(Raised), CornerRadius = new CornerRadius(10), Padding = new Thickness(6), Margin = new Thickness(10),
            BorderBrush = Ui.Brush(Theme.Current.Light ? "#1A000000" : "#26FFFFFF"), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = Theme.Current.Light ? 0.18 : 0.45 },
            Child = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 460 } };
        // the same size as the rest of the launcher (which is scaled up on big screens)
        var main = W.FindName("Main") as FrameworkElement;
        var st = main != null ? main.LayoutTransform as ScaleTransform : null;
        if (st != null) card.LayoutTransform = new ScaleTransform(st.ScaleX, st.ScaleY);
        popup.Child = card;
        popup.Closed += (s, e) => { if (dropPopup == popup) SetHints(); };
        dropPopup = popup;
        // it closes on a click anywhere else in the launcher, or when the launcher loses the focus
        if (!dropHooked) {
            dropHooked = true;
            W.PreviewMouseDown += (s, e) => { if (DropOpen) { dropPopup.IsOpen = false; e.Handled = true; } };
            W.Deactivated += (s, e) => { if (DropOpen && !padUsed) dropPopup.IsOpen = false; };
        }
        popup.IsOpen = true;
        MarkDrop();
    }
}
}
