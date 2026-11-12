// News (on Home): what's new for your games, newest first. For the recomps and ports, their releases (the notes the
// launcher already fetched); for your installed Steam games, the game's own news from Steam (no key needed). A picks
// one: a release opens its notes, Steam news opens in the browser.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RecompLauncher {

class NewsItem { public Game G; public DateTime When; public string Title, Text, Url, Tag; }

partial class Shell {
    Grid nwLayer; StackPanel nwList; TextBlock nwSub; readonly List<Border> nwRows = new List<Border>(); List<NewsItem> nwItems = new List<NewsItem>(); int nwIdx;
    static readonly Dictionary<string, List<NewsItem>> steamNews = new Dictionary<string, List<NewsItem>>();
    bool NewsOpen { get { return nwLayer != null && nwLayer.Visibility == Visibility.Visible; } }
    static string Plain(string s, int max) {
        s = Regex.Replace(s ?? "", @"\[/?[a-z0-9]+[^\]]*\]|<[^>]+>|[#*_`>]+", " ");   // Steam's [tags], HTML, markdown marks
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s.Length > max ? s.Substring(0, max - 1).TrimEnd() + "…" : s;
    }
    void OpenNews() {
        if (nwLayer == null) {
            Border card; StackPanel body;
            nwLayer = Card("Nw", 820, VerticalAlignment.Center, out card, out body);
            body.Children.Add(new TextBlock { Text = "NEWS", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal) });
            body.Children.Add(new TextBlock { Text = "What's new for your games", FontSize = 22, FontFamily = Ui.Semi, Margin = new Thickness(0, 2, 0, 2) });
            nwSub = new TextBlock { FontSize = 12, Foreground = Ui.Brush(Ui.Muted), Margin = new Thickness(0, 0, 0, 12) };
            body.Children.Add(nwSub);
            nwList = new StackPanel();
            body.Children.Add(new ScrollViewer { Height = 470, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = nwList, Focusable = false });
            var close = MakeButton("Close", 40); close.HorizontalAlignment = HorizontalAlignment.Right; close.Margin = new Thickness(0, 12, 0, 0);
            close.Click += (s, e) => CloseNews();
            body.Children.Add(close);
            ((Rectangle)nwLayer.Children[0]).MouseLeftButtonUp += (s, e) => CloseNews();
        }
        nwIdx = 0;
        FillNews();
        ShowCard("NwLayer", "NwCard");
        // the Steam games' news, in the background
        var steam = Games.Where(g => g.Steam != null && g.Inst != null && !g.Hidden && !steamNews.ContainsKey(g.Steam)).ToList();
        if (steam.Count > 0) {
            nwSub.Text = "Getting news from Steam...";
            ThreadPool.QueueUserWorkItem(_ => {
                foreach (var g in steam) {
                    var l = new List<NewsItem>();
                    try {
                        using (var wc = new WebClient()) {
                            wc.Encoding = System.Text.Encoding.UTF8;
                            var d = Json.Parse(wc.DownloadString("https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?count=3&maxlength=400&format=json&appid=" + g.Steam)) as Dictionary<string, object>;
                            var an = d != null ? Json.Obj(d, "appnews") : null; object arr;
                            if (an != null && an.TryGetValue("newsitems", out arr) && arr is object[])
                                foreach (Dictionary<string, object> n in (object[])arr)
                                    l.Add(new NewsItem { G = g, Title = Json.Str(n, "title"), Text = Plain(Json.Str(n, "contents"), 220), Url = Json.Str(n, "url"),
                                        When = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(Json.Num(n, "date")).ToLocalTime() });
                        }
                    } catch { }
                    lock (steamNews) steamNews[g.Steam] = l;
                }
                W.Dispatcher.BeginInvoke(new Action(() => { if (NewsOpen) FillNews(); }));
            });
        }
    }
    void CloseNews() { if (!NewsOpen) return; Sound.Play(Sfx.Back); HideCard("NwLayer", "NwCard"); }
    void FillNews() {
        var items = new List<NewsItem>();
        foreach (var g in Games.Where(x => !x.Hidden && !x.ByStore && x.Inst != null)) {
            var rel = g.Latest ?? ReleaseCache.Get(g.Id); if (rel == null) continue;
            foreach (var r in new[] { rel }.Concat(rel.Older ?? new List<Release>()).Take(3)) {
                DateTime when; if (!DateTime.TryParse(r.Date, out when)) continue;
                items.Add(new NewsItem { G = g, When = when.ToLocalTime(), Tag = r.Tag, Title = g.Name + " " + r.Tag, Text = Plain(r.Notes, 220) });
            }
        }
        lock (steamNews) foreach (var l in steamNews.Values) items.AddRange(l);
        nwItems = items.Where(x => (DateTime.Now - x.When).TotalDays < 120).OrderByDescending(x => x.When).Take(40).ToList();
        nwSub.Text = nwItems.Count + " stories from the last four months";
        nwList.Children.Clear(); nwRows.Clear();
        for (int i = 0; i < nwItems.Count; i++) {
            var n = nwItems[i]; int idx = i;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(8), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left,
                Background = Ui.Gradient(n.G.Accent, n.G.Accent2), Child = new Image { Source = (ImageSource)n.G.Icon, Stretch = Stretch.UniformToFill } });
            var t = new StackPanel();
            t.Children.Add(new TextBlock { Text = n.G.Name.ToUpperInvariant() + "  •  " + Playtime.When(n.When), FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal) });
            t.Children.Add(new TextBlock { Text = n.Title, FontSize = 15, FontFamily = Ui.Semi, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) });
            if (!string.IsNullOrEmpty(n.Text)) t.Children.Add(new TextBlock { Text = n.Text, FontSize = 12.5, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(t, 1); row.Children.Add(t);
            var b = new Border { Child = row, Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(12), Cursor = System.Windows.Input.Cursors.Hand, Background = Ui.Brush("#00000000") };
            b.MouseLeftButtonUp += (s, e) => { nwIdx = idx; OpenNewsItem(); };
            nwRows.Add(b); nwList.Children.Add(b);
        }
        if (nwItems.Count == 0) nwList.Children.Add(new TextBlock { Text = "No news yet. Install a game, or check for updates, and its news shows up here.", FontSize = 13.5, Foreground = Ui.Brush(Ui.Muted) });
        MarkNews();
    }
    void MarkNews() {
        for (int i = 0; i < nwRows.Count; i++) nwRows[i].Background = Ui.Brush(i == nwIdx && padUsed ? "#1C252C" : "#00000000");
        if (padUsed && nwIdx < nwRows.Count) nwRows[nwIdx].BringIntoView();
    }
    void OpenNewsItem() {
        if (nwIdx < 0 || nwIdx >= nwItems.Count) return;
        var n = nwItems[nwIdx];
        Sound.Play(Sfx.Select);
        CloseNews();
        if (n.Url != null) StartBrowser(n.Url);
        else { ShowGame(n.G); OpenNotes("notes"); }
    }
    void NewsPad(int bit) {
        if (bit == Pad.B) { CloseNews(); return; }
        if (bit == Pad.A) { OpenNewsItem(); return; }
        int was = nwIdx;
        if (bit == Pad.Up) nwIdx = Math.Max(0, nwIdx - 1);
        if (bit == Pad.Down) nwIdx = Math.Min(nwRows.Count - 1, nwIdx + 1);
        if (nwIdx != was) { Sound.Play(Sfx.Move); MarkNews(); }
    }
}
}
