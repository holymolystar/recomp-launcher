// A page that is a list (Mail, System, Storage, the launcher's update notes): a card with a title and rows of
// picture, name, a line of text and something on the right. Controller: up / down move, A does the row's thing,
// X the page's extra button, B closes. Keyboard: arrows, Enter, Escape.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RecompLauncher {

class ListRow {
    public string Glyph, Title, Text, Right, C1 = "#3a4a5a", C2 = "#7a8a9a";
    public ImageSource Pic;
    public bool Bold, Dim;
    public Action Do;
    public Action Alt; public string AltLabel;   // a second thing the row does: Y, or a right-click
}
class ListPage {
    public string Id; public Grid Layer; public StackPanel List; public TextBlock Sub; public Button Extra;
    public readonly List<Border> Rows = new List<Border>(); public List<ListRow> Items = new List<ListRow>();
    public int Idx; public Action Fill, OnExtra, OnClose; public string ExtraLabel;
}

partial class Shell {
    ListPage lpOpen;
    bool ListPageOpen { get { return lpOpen != null && lpOpen.Layer.Visibility == Visibility.Visible; } }
    ListPage MakeListPage(string id, string kicker, string title, double width, string extra) {
        var p = new ListPage { Id = id, ExtraLabel = extra };
        Border card; StackPanel body;
        p.Layer = Card(id, width, VerticalAlignment.Center, out card, out body);
        body.Children.Add(new TextBlock { Text = kicker, FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal) });
        body.Children.Add(new TextBlock { Text = title, FontSize = 22, FontFamily = Ui.Semi, Margin = new Thickness(0, 2, 0, 2) });
        p.Sub = new TextBlock { FontSize = 12, Foreground = Ui.Brush(Ui.Muted), Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap };
        body.Children.Add(p.Sub);
        p.List = new StackPanel();
        body.Children.Add(new ScrollViewer { MaxHeight = 390, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = p.List, Focusable = false });
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        if (extra != null) {
            p.Extra = MakeButton(extra, 40); p.Extra.Margin = new Thickness(0, 0, 10, 0);
            p.Extra.Click += (s, e) => { if (p.OnExtra != null) p.OnExtra(); };
            bar.Children.Add(p.Extra);
        }
        var close = MakeButton("Close", 40);
        close.Click += (s, e) => CloseListPage();
        bar.Children.Add(close);
        body.Children.Add(bar);
        ((Rectangle)p.Layer.Children[0]).MouseLeftButtonUp += (s, e) => CloseListPage();
        return p;
    }
    void OpenListPage(ListPage p) {
        if (ListPageOpen && lpOpen != p) HideCard(lpOpen.Id + "Layer", lpOpen.Id + "Card");
        lpOpen = p; p.Idx = 0;
        if (p.Fill != null) p.Fill();
        ShowCard(p.Id + "Layer", p.Id + "Card");
        SetHints();
    }
    void CloseListPage() {
        if (!ListPageOpen) return;
        var p = lpOpen;
        Sound.Play(Sfx.Back);
        HideCard(p.Id + "Layer", p.Id + "Card");
        if (p.OnClose != null) p.OnClose();
        SetHints();
    }
    // the rows: an empty list shows its note instead
    void FillRows(ListPage p, List<ListRow> items, string empty) {
        p.Items = items; p.List.Children.Clear(); p.Rows.Clear();
        p.Idx = Math.Max(0, Math.Min(p.Idx, items.Count - 1));
        for (int i = 0; i < items.Count; i++) {
            var it = items[i]; int idx = i;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            UIElement face = it.Pic != null ? (UIElement)new Image { Source = it.Pic, Stretch = Stretch.UniformToFill }
                : new TextBlock { Text = it.Glyph ?? "", FontFamily = Ui.Mdl, FontSize = 20, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(10), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Background = Ui.Gradient(it.C1, it.C2), Child = face, Opacity = it.Dim ? 0.55 : 1 });
            var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(new TextBlock { Text = it.Title, FontSize = 15, FontFamily = it.Bold ? Ui.Semi : W.FontFamily, FontWeight = it.Bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
                Foreground = Ui.Brush(it.Dim ? Ui.Muted : Ui.Text) });
            if (!string.IsNullOrEmpty(it.Text)) t.Children.Add(new TextBlock { Text = it.Text, FontSize = 12.5, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(t, 1); row.Children.Add(t);
            if (!string.IsNullOrEmpty(it.Right)) {
                var r = new TextBlock { Text = it.Right, FontSize = 13, Foreground = Ui.Brush(Ui.Muted), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), TextAlignment = TextAlignment.Right };
                Grid.SetColumn(r, 2); row.Children.Add(r);
            }
            var b = new Border { Child = row, Padding = new Thickness(12, 9, 14, 9), Margin = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(12),
                Cursor = it.Do != null ? System.Windows.Input.Cursors.Hand : null, Background = Brushes.Transparent };
            b.MouseLeftButtonUp += (s, e) => { p.Idx = idx; MarkRows(p); if (it.Do != null) { Sound.Play(Sfx.Select); it.Do(); } };
            b.MouseRightButtonUp += (s, e) => { p.Idx = idx; MarkRows(p); if (it.Alt != null) { Sound.Play(Sfx.Select); it.Alt(); } e.Handled = true; };
            p.Rows.Add(b); p.List.Children.Add(b);
        }
        if (items.Count == 0) p.List.Children.Add(new TextBlock { Text = empty, FontSize = 13.5, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 4, 4, 10) });
        MarkRows(p);
        if (ListPageOpen && lpOpen == p) SetHints();   // the rows may have come after the page opened
    }
    void MarkRows(ListPage p) {
        for (int i = 0; i < p.Rows.Count; i++) p.Rows[i].Background = Ui.Brush(i == p.Idx && padUsed ? "#1C252C" : "#00000000");
        if (padUsed && p.Idx < p.Rows.Count) p.Rows[p.Idx].BringIntoView();
    }
    void ListPagePad(int bit) {
        var p = lpOpen;
        if (bit == Pad.B) { CloseListPage(); return; }
        if (bit == Pad.X) { if (p.OnExtra != null && p.Extra != null && p.Extra.IsEnabled) { Sound.Play(Sfx.Select); p.OnExtra(); } return; }
        if (bit == Pad.A) { if (p.Idx < p.Items.Count && p.Items[p.Idx].Do != null) { Sound.Play(Sfx.Select); p.Items[p.Idx].Do(); } return; }
        if (bit == Pad.Y) { if (p.Idx < p.Items.Count && p.Items[p.Idx].Alt != null) { Sound.Play(Sfx.Select); p.Items[p.Idx].Alt(); } return; }
        int was = p.Idx;
        if (bit == Pad.Up) p.Idx = Math.Max(0, p.Idx - 1);
        else if (bit == Pad.Down) p.Idx = Math.Min(p.Rows.Count - 1, p.Idx + 1);
        else return;
        Sound.Play(p.Idx != was ? Sfx.Move : Sfx.Bump);
        MarkRows(p);
    }
    void ListPageHints(List<string[]> spec) {
        var p = lpOpen;
        if (p.Idx < p.Items.Count && p.Items[p.Idx].Do != null) spec.Add(new[] { "A", "OK" });
        if (p.Idx < p.Items.Count && p.Items[p.Idx].Alt != null) spec.Add(new[] { "Y", p.Items[p.Idx].AltLabel ?? "More" });
        if (p.Extra != null && p.Extra.IsEnabled) spec.Add(new[] { "X", p.ExtraLabel });
        spec.Add(new[] { "B", "Close" });
    }
}
}
