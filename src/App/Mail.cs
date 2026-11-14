// Mail: a console-style inbox of what happened while you weren't looking. A new release for one of your games, an
// install that finished, a play-time milestone (1, 5, 10, 25, 50, 100, 250 hours), a launcher update. Kept in
// mail.json (newest 100). Home shows an envelope with the number unread; Mail is also a tile at the end of the row.
// A on a letter opens its game; X marks them all read.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RecompLauncher {

class Letter { public string Key, Kind, Title, Text, Game; public DateTime When; public bool Read; }

static class MailBox {
    static List<Letter> all;
    static string FilePath { get { return Path.Combine(Paths.Root, "mail.json"); } }
    public static event Action Changed;
    public static List<Letter> All { get { Load(); return all; } }
    static void Load() {
        if (all != null) return;
        all = new List<Letter>();
        var d = Json.ReadFile(FilePath);
        object list;
        if (d != null && d.TryGetValue("Mail", out list) && list is System.Collections.IEnumerable)
            foreach (var o in ((System.Collections.IEnumerable)list).OfType<Dictionary<string, object>>()) {
                DateTime w; DateTime.TryParse(Json.Str(o, "When"), out w);
                all.Add(new Letter { Key = Json.Str(o, "Key"), Kind = Json.Str(o, "Kind"), Title = Json.Str(o, "Title"), Text = Json.Str(o, "Text"), Game = Json.Str(o, "Game"), When = w, Read = Json.Bool(o, "Read") });
            }
    }
    static void Save() {
        try {
            Json.WriteFile(FilePath, new Dictionary<string, object> { { "Mail", all.Take(100).Select(l => (object)new Dictionary<string, object> {
                { "Key", l.Key }, { "Kind", l.Kind }, { "Title", l.Title }, { "Text", l.Text ?? "" }, { "Game", l.Game ?? "" }, { "When", l.When.ToString("o") }, { "Read", l.Read } }).ToList() } });
        } catch { }
        if (Changed != null) Changed();
    }
    // a letter, once: the same key never comes twice
    public static void Add(string key, string kind, string title, string text, string game) {
        Load();
        if (all.Any(l => l.Key == key)) return;
        all.Insert(0, new Letter { Key = key, Kind = kind, Title = title, Text = text, Game = game, When = DateTime.Now });
        Save();
    }
    public static int Unread { get { Load(); return all.Count(l => !l.Read); } }
    public static void MarkRead(Letter l) { if (l.Read) return; l.Read = true; Save(); }
    public static void MarkAllRead() { Load(); foreach (var l in all) l.Read = true; Save(); }
}

partial class Shell {
    ListPage mailPage; Border homeMail; TextBlock homeMailCount;
    void OpenMail() {
        if (mailPage == null) {
            mailPage = MakeListPage("Mail", "MAIL", "Your mail", 760, "Mark all read");
            mailPage.Fill = FillMail;
            mailPage.OnExtra = () => { MailBox.MarkAllRead(); FillMail(); };
        }
        OpenListPage(mailPage);
    }
    void FillMail() {
        var rows = new List<ListRow>();
        foreach (var l in MailBox.All.Take(60)) {
            var letter = l; var g = l.Game != null ? Catalog.Find(l.Game) : null;
            string glyph = l.Kind == "update" ? "" : l.Kind == "install" ? "" : l.Kind == "milestone" ? "" : l.Kind == "system" ? "" : "";
            string c1 = l.Kind == "milestone" ? "#f4a312" : l.Kind == "update" ? "#2f8fe0" : l.Kind == "install" ? "#3fb8a0" : "#5b6670";
            rows.Add(new ListRow { Glyph = glyph, C1 = c1, C2 = c1, Pic = g != null && g.IconIsArt ? (ImageSource)g.Icon : null,
                Title = (l.Read ? "" : "●  ") + l.Title, Text = l.Text, Right = Playtime.When(l.When), Bold = !l.Read,
                Do = () => {
                    MailBox.MarkRead(letter);
                    if (g != null) { CloseListPage(); ShowGame(g); if (view == View.Home) OpenGamePage(); }
                    else FillMail();
                } });
        }
        int n = MailBox.Unread;
        mailPage.Sub.Text = n == 0 ? "All read." : n + (n == 1 ? " new letter" : " new letters");
        mailPage.Extra.IsEnabled = n > 0;
        FillRows(mailPage, rows, "No mail yet. New releases for your games, finished installs and play-time milestones arrive here.");
    }
    // the envelope at the top of Home, with the number unread
    FrameworkElement BuildHomeMail() {
        homeMailCount = new TextBlock { FontSize = 11, FontFamily = Ui.Semi, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var dot = new Border { MinWidth = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Ui.Brush("#e8453c"), Padding = new Thickness(5, 0, 5, 1), Child = homeMailCount,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -6, -10, 0) };
        var g = new Grid();
        g.Children.Add(new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 19, Foreground = Ui.Brush(Ui.Text), VerticalAlignment = VerticalAlignment.Center });
        g.Children.Add(dot);
        homeMail = new Border { Child = g, Margin = new Thickness(0, 2, 22, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Mail" };
        homeMail.MouseLeftButtonUp += (s, e) => OpenMail();
        MailBox.Changed += () => W.Dispatcher.BeginInvoke(new Action(UpdateHomeMail));
        UpdateHomeMail();
        return homeMail;
    }
    void UpdateHomeMail() {
        if (homeMail == null) return;
        int n = MailBox.Unread;
        homeMailCount.Text = n > 9 ? "9+" : n.ToString();
        ((Border)homeMailCount.Parent).Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    // ---------- what sends mail ----------
    void MailUpdates() {
        foreach (var g in Games.Where(x => x.State == State.Update && x.Latest != null && !x.Hidden && x.App == null))
            MailBox.Add("update:" + g.Id + ":" + g.Latest.Tag, "update", g.Name + " " + g.Latest.Tag + " is out", "An update for your game. Open it to see what's new, or update from Home.", g.Id);
    }
    void MailInstalled(Game g) {
        if (g == null || g.Latest == null) return;
        MailBox.Add("install:" + g.Id + ":" + g.Latest.Tag, "install", g.Name + " is installed", "Version " + g.Latest.Tag + " is ready. " + (string.IsNullOrEmpty(g.Needs) ? "" : "It needs your own game files the first time."), g.Id);
    }
    static readonly int[] MilestoneHours = { 1, 5, 10, 25, 50, 100, 250, 500 };
    void MailMilestone(Game g, double before) {
        if (g == null) return;
        double now = Playtime.Total(g.Id) / 3600.0, was = before / 3600.0;
        foreach (int h in MilestoneHours)
            if (was < h && now >= h)
                MailBox.Add("hours:" + Profiles.Current.Id + ":" + g.Id + ":" + h, "milestone", h + (h == 1 ? " hour" : " hours") + " in " + g.Name,
                    Profiles.DisplayName(Profiles.Current) + " has played " + g.Name + " for " + h + (h == 1 ? " hour" : " hours") + ". Nice!", g.Id);
    }
}
}
