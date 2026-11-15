// A Steam game's achievements, from the game page (Achievements button). Steam keeps a part of the list on this PC
// (userdata\<you>\config\librarycache\<id>.json: up to 12 unlocked and 12 still locked, with their icons and how many
// players have them); with a Steam Web API key (Settings > Steam friends) the launcher asks Steam for all of them.
// Unlocked ones come first (newest first), then the locked ones (the most common first).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace RecompLauncher {

class Achievement { public string Id, Name, Desc, Icon; public bool Done, Hidden; public DateTime When = DateTime.MinValue; public double Rare = -1; }

static class SteamAchievements {
    static DateTime FromUnix(double t) { return t > 0 ? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(t).ToLocalTime() : DateTime.MinValue; }
    // what Steam keeps on this PC: (part of the list, the totals)
    public static List<Achievement> Local(Game g, out int total, out int done) {
        total = 0; done = 0;
        var l = new List<Achievement>();
        string dir = SteamGames.UserConfig(); if (dir == null) return l;
        string f = Path.Combine(dir, @"librarycache\" + g.Steam + ".json");
        if (!File.Exists(f)) return l;
        var root = Json.Parse(File.ReadAllText(f)) as object[];
        if (root == null) return l;
        foreach (var pair in root.OfType<object[]>()) {
            if (pair.Length < 2 || (pair[0] as string) != "achievements") continue;
            var data = Json.Obj((Dictionary<string, object>)pair[1], "data");
            if (data == null) continue;
            total = (int)Json.Num(data, "nTotal"); done = (int)Json.Num(data, "nAchieved");
            foreach (var key in new[] { "vecHighlight", "vecAchievedHidden", "vecUnachieved" }) {
                object arr;
                if (!data.TryGetValue(key, out arr) || !(arr is object[])) continue;
                foreach (Dictionary<string, object> a in (object[])arr) {
                    var x = new Achievement { Id = Json.Str(a, "strID"), Name = Json.Str(a, "strName"), Desc = Json.Str(a, "strDescription"), Icon = Json.Str(a, "strImage"),
                        Done = Json.Bool(a, "bAchieved"), Hidden = Json.Bool(a, "bHidden"), When = FromUnix(Json.Num(a, "rtUnlocked")), Rare = Json.Num(a, "flAchieved") };
                    if (!l.Any(y => y.Id == x.Id)) l.Add(x);
                }
            }
        }
        return l;
    }
    // all of them, from Steam's Web API (needs the key)
    public static List<Achievement> Web(Game g, string key) {
        string me = SteamFriends.MyId(); if (me == null) throw new Exception("Steam isn't set up on this PC.");
        using (var wc = new WebClient()) {
            wc.Encoding = System.Text.Encoding.UTF8;
            string k = Uri.EscapeDataString(key);
            var schema = Json.Parse(wc.DownloadString("https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?l=english&key=" + k + "&appid=" + g.Steam)) as Dictionary<string, object>;
            var list = new List<Achievement>();
            var game = schema != null ? Json.Obj(schema, "game") : null;
            var stats = game != null ? Json.Obj(game, "availableGameStats") : null;
            object arr;
            if (stats != null && stats.TryGetValue("achievements", out arr) && arr is object[])
                foreach (Dictionary<string, object> a in (object[])arr)
                    list.Add(new Achievement { Id = Json.Str(a, "name"), Name = Json.Str(a, "displayName"), Desc = Json.Str(a, "description"), Icon = Json.Str(a, "icon"), Hidden = Json.Num(a, "hidden") > 0 });
            var mine = Json.Parse(wc.DownloadString("https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/?key=" + k + "&steamid=" + me + "&appid=" + g.Steam)) as Dictionary<string, object>;
            var ps = mine != null ? Json.Obj(mine, "playerstats") : null;
            if (ps != null && ps.TryGetValue("achievements", out arr) && arr is object[])
                foreach (Dictionary<string, object> a in (object[])arr) {
                    var x = list.FirstOrDefault(y => y.Id == Json.Str(a, "apiname"));
                    if (x != null) { x.Done = Json.Num(a, "achieved") > 0; x.When = FromUnix(Json.Num(a, "unlocktime")); }
                }
            try {
                var rare = Json.Parse(wc.DownloadString("https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid=" + g.Steam)) as Dictionary<string, object>;
                var ap = rare != null ? Json.Obj(rare, "achievementpercentages") : null;
                if (ap != null && ap.TryGetValue("achievements", out arr) && arr is object[])
                    foreach (Dictionary<string, object> a in (object[])arr) {
                        var x = list.FirstOrDefault(y => y.Id == Json.Str(a, "name")); double p;
                        if (x != null && double.TryParse(Convert.ToString(a["percent"], System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out p)) x.Rare = p;
                    }
            } catch { }
            return list;
        }
    }
}

partial class Shell {
    Grid acLayer; StackPanel acRows; TextBlock acTitle, acSub; Border acBar; ScrollViewer acScroll; Game acFor;
    bool AchievementsOpen { get { return acLayer != null && acLayer.Visibility == Visibility.Visible; } }
    void OpenAchievements(Game g) {
        if (g.Steam == null) return;
        if (acLayer == null) {
            Border card; StackPanel body;
            acLayer = Card("Ac", 640, VerticalAlignment.Center, out card, out body);
            body.Children.Add(new TextBlock { Text = "ACHIEVEMENTS", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal) });
            acTitle = new TextBlock { FontSize = 22, FontFamily = Ui.Semi, Margin = new Thickness(0, 2, 0, 2), TextWrapping = TextWrapping.Wrap };
            body.Children.Add(acTitle);
            acSub = new TextBlock { FontSize = 12, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            body.Children.Add(acSub);
            var track = new Grid { Height = 6, Margin = new Thickness(0, 0, 0, 14) };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Ui.Brush("#1C252C") });
            acBar = new Border { CornerRadius = new CornerRadius(3), Background = Ui.Brush("#8fc93a"), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            track.Children.Add(acBar);
            body.Children.Add(track);
            acRows = new StackPanel();
            acScroll = new ScrollViewer { MaxHeight = 430, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Content = acRows };
            body.Children.Add(acScroll);
            var done = MakeButton("Done", 40); done.Background = Ui.Brush(Ui.Teal); done.Foreground = Ui.Brush("#0A0E11"); done.HorizontalAlignment = HorizontalAlignment.Right; done.Margin = new Thickness(0, 14, 0, 0);
            done.Click += (s, e) => CloseAchievements();
            body.Children.Add(done);
            ((Rectangle)acLayer.Children[0]).MouseLeftButtonUp += (s, e) => CloseAchievements();
        }
        acFor = g;
        acTitle.Text = g.Name;
        int total, got;
        var local = SteamAchievements.Local(g, out total, out got);
        string key = Settings.Get("SteamApiKey").Trim();
        FillAchievements(local, total, got, key.Length == 0 ? "Steam keeps up to 12 unlocked and 12 locked ones on this PC. With a Steam Web API key (Settings > Steam friends) you see all of them." : "Asking Steam for all of them...");
        Sound.Play(Sfx.Open);
        ShowCard("AcLayer", "AcCard");
        if (key.Length == 0) return;
        ThreadPool.QueueUserWorkItem(_ => {
            List<Achievement> all = null; string err = null;
            try { all = SteamAchievements.Web(g, key); } catch (Exception e) { err = e.Message; }
            W.Dispatcher.BeginInvoke(new Action(() => {
                if (acFor != g) return;
                if (all != null && all.Count > 0) FillAchievements(all, all.Count, all.Count(a => a.Done), null);
                else acSub.Text = acSub.Text.Replace("Asking Steam for all of them...", err != null ? "Steam didn't send the full list (" + err + "), so these are the ones kept on this PC." : "");
            }));
        });
    }
    void FillAchievements(List<Achievement> l, int total, int got, string note) {
        acSub.Text = (total > 0 ? got + " of " + total + " unlocked (" + Math.Round(100.0 * got / total) + "%)" : "This game has no achievements, or Steam hasn't shown them yet.") + (note != null ? ". " + note : "");
        acBar.Width = total > 0 ? 578.0 * got / total : 0;
        acRows.Children.Clear();
        foreach (var a in l.Where(x => x.Done).OrderByDescending(x => x.When).Concat(l.Where(x => !x.Done).OrderByDescending(x => x.Rare))) {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10), Opacity = a.Done ? 1 : 0.62 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var pic = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(8), ClipToBounds = true, Background = Ui.Brush("#1C252C"), HorizontalAlignment = HorizontalAlignment.Left };
            if (a.Icon != null) try {
                var img = new BitmapImage(); img.BeginInit(); img.UriSource = new Uri(a.Icon); img.DecodePixelWidth = 64; img.EndInit();
                pic.Child = new Image { Source = img, Stretch = Stretch.UniformToFill };   // a locked one shows faded (the row)
            } catch { }
            row.Children.Add(pic);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = a.Name ?? a.Id, FontSize = 14, FontFamily = Ui.Semi, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = !a.Done && a.Hidden && string.IsNullOrEmpty(a.Desc) ? "Hidden achievement" : a.Desc ?? "", FontSize = 12, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(text, 1); row.Children.Add(text);
            var right = new TextBlock { FontSize = 11.5, Foreground = Ui.Brush(a.Done ? "#8fc93a" : Ui.Muted), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextAlignment = TextAlignment.Right,
                Text = a.Done ? (a.When != DateTime.MinValue ? a.When.ToString("d MMM yyyy") : "Unlocked") : a.Rare >= 0 ? a.Rare.ToString("0.#") + "% of players" : "" };
            Grid.SetColumn(right, 2); row.Children.Add(right);
            acRows.Children.Add(row);
        }
        acScroll.ScrollToTop();
    }
    void AchievementsPad(int bit) {
        if (bit == Pad.B || bit == Pad.A || bit == Pad.Start) { CloseAchievements(); return; }
        if (bit == Pad.Down) acScroll.ScrollToVerticalOffset(acScroll.VerticalOffset + 112);
        if (bit == Pad.Up) acScroll.ScrollToVerticalOffset(acScroll.VerticalOffset - 112);
    }
    void CloseAchievements() { if (!AchievementsOpen) return; Sound.Play(Sfx.Back); HideCard("AcLayer", "AcCard"); }
}
}
