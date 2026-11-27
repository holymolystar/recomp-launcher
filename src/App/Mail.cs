// Mail: a console-style inbox of what happened while you weren't looking. A new release for one of your games, an
// install that finished, a play-time milestone (1, 5, 10, 25, 50, 100, 250 hours), a launcher update. Kept in
// mail.json (newest 100). Home shows an envelope with the number unread; Mail is also a tile at the end of the row.
// A reads a letter (its game, or deleting it, from there), Y deletes it, X marks them all read, LB / RB a page.
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
    public static void Delete(Letter l) { Load(); if (all.Remove(l)) Save(); }
}

partial class Shell {
    ListPage mailPage, letterPage; Border homeMail; TextBlock homeMailCount;
    Letter letterOpen; bool letterLeaving;
    void OpenMail() { OpenMail(-1); }
    void OpenMail(int idx) {
        if (mailPage == null) {
            mailPage = MakeListPage("Mail", "MAIL", "Your mail", 760, "Mark all read");
            mailPage.Fill = FillMail;
            mailPage.OnExtra = () => { MailBox.MarkAllRead(); FillMail(); };
        }
        OpenListPage(mailPage);
        if (idx >= 0 && mailPage.Rows.Count > 0) { mailPage.Idx = Math.Min(idx, mailPage.Rows.Count - 1); MarkRows(mailPage); SetHints(); }
    }
    void FillMail() {
        var rows = new List<ListRow>();
        foreach (var l in MailBox.All.Take(60)) {
            var letter = l; var g = l.Game != null ? Catalog.Find(l.Game) : null;
            string glyph = l.Kind == "update" ? "" : l.Kind == "install" ? "" : l.Kind == "milestone" ? "" : l.Kind == "system" ? "" : "";
            string c1 = l.Kind == "milestone" ? "#f4a312" : l.Kind == "update" ? "#2f8fe0" : l.Kind == "install" ? "#3fb8a0" : "#5b6670";
            rows.Add(new ListRow { Glyph = glyph, C1 = c1, C2 = c1, Pic = g != null && g.IconIsArt ? (ImageSource)g.Icon : null,
                Title = (l.Read ? "" : "●  ") + l.Title, Text = l.Text, Right = Playtime.When(l.When), Bold = !l.Read,
                Do = () => OpenLetter(letter),
                Alt = () => { int at = mailPage.Idx; MailBox.Delete(letter); FillMail(); mailPage.Idx = Math.Min(at, Math.Max(0, mailPage.Rows.Count - 1)); MarkRows(mailPage); SetHints(); },
                AltLabel = "Delete" });
        }
        int n = MailBox.Unread;
        // first in the list, so the controller gets there with up / down and A (X does it too)
        if (n > 0) rows.Insert(0, new ListRow { Glyph = "", C1 = "#3fb8a0", C2 = "#2f8fe0", Title = "Mark all as read", Text = n + (n == 1 ? " unread letter" : " unread letters") + "  ·  or press X",
            Do = () => { MailBox.MarkAllRead(); FillMail(); mailPage.Idx = 0; MarkRows(mailPage); SetHints(); } });
        mailPage.Sub.Text = n == 0 ? "All read." : n + (n == 1 ? " new letter" : " new letters");
        mailPage.Extra.IsEnabled = n > 0;
        FillRows(mailPage, rows, "No mail yet. New releases for your games, finished installs and play-time milestones arrive here.");
    }
    // one letter, to read: its whole text, and what you can do with it
    void OpenLetter(Letter l) {
        int back = mailPage.Idx;
        MailBox.MarkRead(l);
        if (letterPage == null) {
            letterPage = MakeListPage("Letter", "LETTER", "", 640, null);
            letterPage.BackLabel = "Back";
            letterPage.OnClose = () => { if (!letterLeaving) W.Dispatcher.BeginInvoke(new Action(() => OpenMail(letterBack))); };
        }
        letterOpen = l; letterBack = back; letterLeaving = false;
        var g = l.Game != null ? Catalog.Find(l.Game) : null;
        letterPage.Fill = () => {
            letterPage.Title.Text = l.Title;
            letterPage.Sub.Text = l.When.ToString("dddd d MMMM yyyy, HH:mm") + "\n\n" + (l.Text ?? "");
            letterPage.Sub.FontSize = 14; letterPage.Sub.Foreground = Ui.Brush(Ui.Text);
            var rows = new List<ListRow>();
            if (g != null) rows.Add(new ListRow { Glyph = "", C1 = "#2f8fe0", C2 = "#2f8fe0", Pic = g.IconIsArt ? (ImageSource)g.Icon : null, Title = "Go to " + g.Name, Text = "Its game page: play, update or see what's new.",
                Do = () => { letterLeaving = true; CloseListPage(); ShowGame(g); if (view == View.Home) OpenGamePage(); } });
            rows.Add(new ListRow { Glyph = "", C1 = "#c0453c", C2 = "#c0453c", Title = "Delete this letter",
                Do = () => { MailBox.Delete(l); CloseListPage(); } });
            rows.Add(new ListRow { Glyph = "", C1 = "#5b6670", C2 = "#5b6670", Title = "Back to Mail", Do = () => CloseListPage() });
            FillRows(letterPage, rows, "");
        };
        OpenListPage(letterPage);
    }
    int letterBack;
    // the envelope at the top of Home, with the number unread
    FrameworkElement BuildHomeMail() {
        homeMailCount = new TextBlock { FontSize = 11, FontFamily = Ui.Semi, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var dot = new Border { MinWidth = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Ui.Brush("#e8453c"), Padding = new Thickness(5, 0, 5, 1), Child = homeMailCount,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -6, -10, 0) };
        var g = new Grid();
        g.Children.Add(new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 19, Foreground = Ui.Brush(Ui.Text), VerticalAlignment = VerticalAlignment.Center });
        g.Children.Add(dot);
        homeMail = new Border { Child = g, Margin = new Thickness(0, 2, 12, 0), Padding = new Thickness(9, 5, 9, 5), CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Mail" };
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
    // ---------- watched projects: a letter a day about how they're coming along ----------
    // For a game with "Watch" in games.json (one that can't be played yet, like Leonida): once a day, from its GitHub
    // page, whether it's playable yet (its README's status table: a row about booting or being playable, with a tick or
    // a cross), how many changes it had in the last day and the newest one, and whether a release has a Windows download.
    string mailWatchDay; bool mailWatchBusy;
    void MailWatch() {
        string day = DateTime.Now.ToString("yyyy-MM-dd");
        if (mailWatchBusy || mailWatchDay == day) return;
        var watched = Games.Where(g => g.Watch && g.Repo != null && !g.Repo.StartsWith("http")).ToList();
        if (watched.Count == 0) { mailWatchDay = day; return; }
        mailWatchBusy = true;
        var todo = watched.Where(g => !MailBox.All.Any(l => l.Key == "watch:" + g.Id + ":" + day)).ToList();
        System.Threading.ThreadPool.QueueUserWorkItem(_ => {
            bool allOk = true; var letters = new List<Tuple<Game, string[]>>();
            foreach (var g in todo) {
                try { letters.Add(Tuple.Create(g, WatchLetter(g))); }
                catch { allOk = false; }   // no internet now: tried again later today
            }
            // the letters go in on the window's thread (Mail may be open, reading the list)
            W.Dispatcher.BeginInvoke(new Action(() => {
                foreach (var x in letters) MailBox.Add("watch:" + x.Item1.Id + ":" + day, x.Item2[0], x.Item2[1], x.Item2[2], x.Item1.Id);
                mailWatchBusy = false; if (allOk) mailWatchDay = day;
            }));
        });
    }
    static string GitHubGet(string url) {
        System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
        var r = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
        r.UserAgent = "RecompLauncher"; r.Accept = url.Contains("raw.githubusercontent") ? "*/*" : "application/vnd.github+json"; r.Timeout = 20000;
        using (var resp = r.GetResponse()) using (var sr = new System.IO.StreamReader(resp.GetResponseStream(), System.Text.Encoding.UTF8)) return sr.ReadToEnd();
    }
    // kind, title, text
    static string[] WatchLetter(Game g) {
        string api = "https://api.github.com/repos/" + g.Repo;
        var info = Json.Parse(GitHubGet(api)) as Dictionary<string, object>;
        string branch = Json.Str(info, "default_branch") ?? "main";
        // playable yet? the README's status row
        string playable = null;
        try {
            string readme = GitHubGet("https://raw.githubusercontent.com/" + g.Repo + "/" + branch + "/README.md");
            foreach (var line in readme.Split('\n')) {
                if (!line.TrimStart().StartsWith("|") || !System.Text.RegularExpressions.Regex.IsMatch(line, @"\b(boot\w*|playable)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                if (line.Contains("✅")) { playable = "yes"; break; }
                if (line.Contains("❌")) { playable = "no"; break; }
                if (line.Contains("⚠") || line.Contains("🔶")) { playable = "partly"; break; }
            }
        } catch { }
        // the last day's changes
        int changes = 0; string newest = null;
        try {
            var commits = Json.Parse(GitHubGet(api + "/commits?per_page=100&since=" + DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ssZ"))) as System.Collections.IEnumerable;
            var list = commits == null ? new List<Dictionary<string, object>>() : commits.Cast<object>().OfType<Dictionary<string, object>>().ToList();
            changes = list.Count;
            // the newest real change (merges say nothing about what changed)
            foreach (var x in list) { string m = (Json.Str(Json.Obj(x, "commit"), "message") ?? "").Split('\n')[0].Trim(); if (m.Length > 0 && !m.StartsWith("Merge ")) { newest = m; break; } }
        } catch { }
        // a release with a Windows download?
        string download = null;
        try {
            var rels = Json.Parse(GitHubGet(api + "/releases?per_page=5")) as System.Collections.IEnumerable;
            foreach (var rel in (rels ?? new object[0]).Cast<object>().OfType<Dictionary<string, object>>()) {
                var assets = ((rel.ContainsKey("assets") ? rel["assets"] as System.Collections.IEnumerable : null) ?? new object[0]).Cast<object>().OfType<Dictionary<string, object>>();
                if (assets.Any(a => System.Text.RegularExpressions.Regex.IsMatch(Json.Str(a, "name") ?? "", @"win|\.exe$|x64", System.Text.RegularExpressions.RegexOptions.IgnoreCase))) { download = Json.Str(rel, "tag_name"); break; }
            }
        } catch { }
        string name = g.Name + " (" + g.Repo.Split('/')[1] + ")";
        string title = download != null ? name + ": a Windows download is out (" + download + ")"
            : playable == "yes" ? name + " is playable!"
            : playable == "partly" ? name + ": partly working, not done yet"
            : name + ": not done yet";
        var text = new System.Text.StringBuilder();
        text.Append(playable == "yes" ? "Its status says it's playable. " : playable == "no" ? "Its status says it can't be played yet. " : playable == "partly" ? "Its status says parts work, but it isn't playable start to finish yet. " : "");
        text.Append(download != null ? "There's a release with a Windows download: open the game page to install it. " : "No Windows download yet. ");
        text.Append(changes == 0 ? "No changes in the last day." : changes + (changes >= 100 ? "+" : "") + (changes == 1 ? " change" : " changes") + " in the last day; the newest: \"" + (newest != null && newest.Length > 120 ? newest.Substring(0, 117) + "..." : newest) + "\".");
        double stars = Json.Num(info, "stargazers_count"); if (stars > 0) text.Append(" " + stars.ToString("#,0") + " people follow it.");
        return new[] { download != null || playable == "yes" ? "update" : "system", title, text.ToString() };
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
