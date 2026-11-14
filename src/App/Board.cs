// The message board (Board on Home): a diary the launcher keeps by itself, day by day, and notes you leave for each
// other. Each game played for a minute or more ("Alex played Tears of the Kingdom for 2 h 10 min"), each game updated
// ("The Wind Waker HD was updated to 0.5.2"), each note. The last two weeks are a strip of days (a dot where
// something happened); left / right pick a day, X writes a note (the on-screen keyboard works with the controller).
// Kept in board.json (the last 2,000 entries). Its play entries also give each profile's "this week" chart.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace RecompLauncher {

class BoardEntry { public DateTime When; public string Profile, Kind, Game, Text; public double Secs; }

static class Board {
    static string File_ { get { return Path.Combine(Paths.Root, "board.json"); } }
    static List<BoardEntry> all;
    public static List<BoardEntry> All {
        get {
            if (all != null) return all;
            all = new List<BoardEntry>();
            try {
                var d = Json.Parse(File.Exists(File_) ? File.ReadAllText(File_) : "[]") as object[];
                foreach (Dictionary<string, object> o in d ?? new object[0]) {
                    DateTime w; DateTime.TryParse(Json.Str(o, "when"), null, System.Globalization.DateTimeStyles.RoundtripKind, out w);
                    all.Add(new BoardEntry { When = w, Profile = Json.Str(o, "profile"), Kind = Json.Str(o, "kind"), Game = Json.Str(o, "game"), Text = Json.Str(o, "text"), Secs = Json.Num(o, "secs") });
                }
            } catch { }
            return all;
        }
    }
    public static void Add(string kind, string profile, string game, string text, double secs = 0) {
        All.Add(new BoardEntry { When = DateTime.Now, Profile = profile, Kind = kind, Game = game, Text = text, Secs = secs });
        while (all.Count > 2000) all.RemoveAt(0);
        try {
            File.WriteAllText(File_, Json.Write(all.Select(e => new Dictionary<string, object> {
                { "when", e.When.ToString("o") }, { "profile", e.Profile ?? "" }, { "kind", e.Kind }, { "game", e.Game ?? "" }, { "text", e.Text ?? "" }, { "secs", e.Secs } }).ToList()));
        } catch { }
    }
    // a profile's play time on each of the last 7 days (oldest first), from the diary
    public static double[] Week(string profile) {
        var r = new double[7];
        foreach (var e in All.Where(x => x.Kind == "play" && x.Profile == profile)) {
            int ago = (DateTime.Today - e.When.Date).Days;
            if (ago >= 0 && ago < 7) r[6 - ago] += e.Secs;
        }
        return r;
    }
}

partial class Shell {
    Grid bdLayer; StackPanel bdDays, bdList; TextBlock bdDayTitle; int bdDay; Button bdNote, bdClose;
    bool BoardOpen { get { return bdLayer != null && bdLayer.Visibility == Visibility.Visible; } }
    void OpenBoard() {
        if (bdLayer == null) {
            Border card; StackPanel body;
            bdLayer = Card("Bd", 760, VerticalAlignment.Center, out card, out body);
            body.Children.Add(new TextBlock { Text = "MESSAGE BOARD", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal) });
            bdDayTitle = new TextBlock { FontSize = 22, FontFamily = Ui.Semi, Margin = new Thickness(0, 2, 0, 12) };
            body.Children.Add(bdDayTitle);
            bdDays = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            body.Children.Add(bdDays);
            bdList = new StackPanel();
            body.Children.Add(new ScrollViewer { Height = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = bdList, Focusable = false });
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            bdNote = MakeButton("Write a note", 40); bdNote.Click += (s, e) => WriteNote();
            bdClose = MakeButton("Close", 40); bdClose.Margin = new Thickness(8, 0, 0, 0); bdClose.Click += (s, e) => CloseBoard();
            bar.Children.Add(bdNote); bar.Children.Add(bdClose);
            body.Children.Add(bar);
            ((Rectangle)bdLayer.Children[0]).MouseLeftButtonUp += (s, e) => CloseBoard();
        }
        bdDay = 0;
        FillBoard();
        ShowCard("BdLayer", "BdCard");
    }
    void CloseBoard() { if (!BoardOpen) return; Sound.Play(Sfx.Back); HideCard("BdLayer", "BdCard"); }
    // bdDay: 0 today, 1 yesterday ... 13
    void FillBoard() {
        var day = DateTime.Today.AddDays(-bdDay);
        bdDayTitle.Text = bdDay == 0 ? "Today" : bdDay == 1 ? "Yesterday" : day.ToString("dddd d MMMM");
        bdDays.Children.Clear();
        for (int i = 13; i >= 0; i--) {
            var d = DateTime.Today.AddDays(-i); int idx = i;
            bool any = Board.All.Any(e => e.When.Date == d);
            var col = new StackPanel { Width = 46, Cursor = System.Windows.Input.Cursors.Hand };
            col.Children.Add(new TextBlock { Text = d.ToString("ddd").Substring(0, 2), FontSize = 11, Foreground = Ui.Brush(Ui.Muted), HorizontalAlignment = HorizontalAlignment.Center });
            var num = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Center,
                Background = i == bdDay ? Ui.Brush(Ui.Teal) : Ui.Brush("#00000000"),
                Child = new TextBlock { Text = d.Day.ToString(), FontSize = 14, FontFamily = Ui.Semi, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Ui.Brush(i == bdDay ? (Theme.Current.Light ? "#ffffff" : "#0A0E11") : Ui.Text) } };
            col.Children.Add(num);
            col.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Ui.Brush(any ? Ui.Gold : "#00000000"), HorizontalAlignment = HorizontalAlignment.Center });
            col.MouseLeftButtonUp += (s, e) => { bdDay = idx; Sound.Play(Sfx.Move); FillBoard(); };
            bdDays.Children.Add(col);
        }
        bdList.Children.Clear();
        var items = Board.All.Where(e => e.When.Date == day).OrderByDescending(e => e.When).ToList();
        foreach (var e in items) {
            var p = Profiles.All.FirstOrDefault(x => x.Id == e.Profile);
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (p != null) row.Children.Add(new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left,
                Child = new Image { Source = AvatarArt.Portrait(AvatarDef.Parse(p.Character), 80) } });
            else row.Children.Add(new TextBlock { Text = e.Kind == "update" ? "" : "", FontFamily = Ui.Mdl, FontSize = 20, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Brush(Ui.Teal) });
            var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            string who = p != null ? Profiles.DisplayName(p) : "";
            var g = Catalog.Find(e.Game ?? "");
            string text = e.Kind == "play" ? who + " played " + (g != null ? g.Name : e.Game) + " for " + Playtime.Format(e.Secs)
                        : e.Kind == "note" ? e.Text : e.Text;
            t.Children.Add(new TextBlock { Text = text, FontSize = e.Kind == "note" ? 15 : 14, FontFamily = e.Kind == "note" ? Ui.Semi : W.FontFamily, TextWrapping = TextWrapping.Wrap });
            if (e.Kind == "note" && who.Length > 0) t.Children.Add(new TextBlock { Text = "a note from " + who, FontSize = 11.5, Foreground = Ui.Brush(Ui.Muted) });
            Grid.SetColumn(t, 1); row.Children.Add(t);
            var time = new TextBlock { Text = e.When.ToString("HH:mm"), FontSize = 12, Foreground = Ui.Brush(Ui.Muted), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(time, 2); row.Children.Add(time);
            bdList.Children.Add(row);
        }
        if (items.Count == 0) bdList.Children.Add(new TextBlock { Text = bdDay == 0 ? "Nothing yet today. Play something, or leave a note." : "Nothing on this day.", FontSize = 13.5, Foreground = Ui.Brush(Ui.Muted) });
    }
    void WriteNote() {
        OpenOsk("A note on the board", "", null, text => {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            Board.Add("note", Profiles.Current.Id, null, text);
            bdDay = 0; Sound.Play(Sfx.Select); FillBoard();
        }, null);
    }
    void BoardPad(int bit) {
        if (bit == Pad.B) { CloseBoard(); return; }
        if (bit == Pad.X || bit == Pad.A) { WriteNote(); return; }
        int was = bdDay;
        if (bit == Pad.Left || bit == Pad.LB) bdDay = Math.Min(13, bdDay + 1);
        if (bit == Pad.Right || bit == Pad.RB) bdDay = Math.Max(0, bdDay - 1);
        if (bdDay != was) { Sound.Play(Sfx.Move); FillBoard(); } else if (bit == Pad.Left || bit == Pad.Right) Sound.Play(Sfx.Bump);
    }
}
}
