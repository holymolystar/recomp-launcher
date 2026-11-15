// Friends: your Steam friends who are online and what they're playing, from Steam's Web API with your own key
// (Settings > Steam friends; made at steamcommunity.com/dev/apikey, it only reads public profile data). Your Steam
// id comes from the account that used Steam on this PC last (userdata\<account id>). The Friends button on Home opens
// the list: playing first, then online, then away; it refreshes each time it opens (and at most once a minute).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace RecompLauncher {

class Friend { public string Name, Game, Avatar; public int State; }

static class SteamFriends {
    // the Steam id (64 bit) of the account that used Steam on this PC last
    public static string MyId() {
        try {
            var d = new System.IO.DirectoryInfo(System.IO.Path.Combine(SteamGames.Root, "userdata"));
            if (!d.Exists) return null;
            var best = d.GetDirectories().Where(x => Regex.IsMatch(x.Name, "^\\d+$") && x.Name != "0")
                .OrderByDescending(x => { var f = new System.IO.FileInfo(System.IO.Path.Combine(x.FullName, @"config\localconfig.vdf")); return f.Exists ? f.LastWriteTimeUtc : DateTime.MinValue; }).FirstOrDefault();
            return best != null ? (76561197960265728L + long.Parse(best.Name)).ToString() : null;
        } catch { return null; }
    }
    // online friends (state 1 online, 2 busy, 3 away, 4 snooze, 5/6 looking to trade or play); throws on a bad key
    public static List<Friend> Online(string key, out int total) {
        string me = MyId();
        if (me == null) throw new Exception("Steam isn't set up on this PC.");
        using (var wc = new WebClient()) {
            wc.Encoding = System.Text.Encoding.UTF8;
            var list = Json.Parse(wc.DownloadString("https://api.steampowered.com/ISteamUser/GetFriendList/v1/?relationship=friend&key=" + Uri.EscapeDataString(key) + "&steamid=" + me)) as Dictionary<string, object>;
            var ids = new List<string>();
            var fl = list != null ? Json.Obj(list, "friendslist") : null;
            object arr;
            if (fl != null && fl.TryGetValue("friends", out arr) && arr is object[])
                foreach (Dictionary<string, object> f in (object[])arr) ids.Add(Json.Str(f, "steamid"));
            total = ids.Count;
            var r = new List<Friend>();
            for (int i = 0; i < ids.Count; i += 100) {
                var sum = Json.Parse(wc.DownloadString("https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key=" + Uri.EscapeDataString(key) + "&steamids=" + string.Join(",", ids.Skip(i).Take(100)))) as Dictionary<string, object>;
                var resp = sum != null ? Json.Obj(sum, "response") : null;
                if (resp == null || !resp.TryGetValue("players", out arr) || !(arr is object[])) continue;
                foreach (Dictionary<string, object> p in (object[])arr) {
                    int st = (int)Json.Num(p, "personastate");
                    if (st == 0) continue;
                    r.Add(new Friend { Name = Json.Str(p, "personaname"), Game = Json.Str(p, "gameextrainfo"), Avatar = Json.Str(p, "avatarmedium"), State = st });
                }
            }
            return r.OrderBy(f => f.Game == null ? 1 : 0).ThenBy(f => f.State == 3 || f.State == 4 ? 1 : 0).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }
}

partial class Shell {
    Grid frLayer; StackPanel frRows; TextBlock frSub; DateTime frAt = DateTime.MinValue; List<Friend> frLast; int frTotal;
    bool FriendsOpen { get { return frLayer != null && frLayer.Visibility == Visibility.Visible; } }
    void OpenFriends() {
        if (frLayer == null) {
            Border card; StackPanel body;
            frLayer = Card("Fr", 560, VerticalAlignment.Center, out card, out body);
            body.Children.Add(new TextBlock { Text = "STEAM", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal) });
            body.Children.Add(new TextBlock { Text = "Friends", FontSize = 22, FontFamily = Ui.Semi, Margin = new Thickness(0, 2, 0, 2) });
            frSub = new TextBlock { FontSize = 12, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
            body.Children.Add(frSub);
            frRows = new StackPanel();
            body.Children.Add(new ScrollViewer { MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Content = frRows });
            var done = MakeButton("Done", 40); done.Background = Ui.Brush(Ui.Teal); done.Foreground = Ui.Brush("#0A0E11"); done.HorizontalAlignment = HorizontalAlignment.Right; done.Margin = new Thickness(0, 14, 0, 0);
            done.Click += (s, e) => CloseFriends();
            body.Children.Add(done);
            ((Rectangle)frLayer.Children[0]).MouseLeftButtonUp += (s, e) => CloseFriends();
        }
        Sound.Play(Sfx.Open);
        ShowCard("FrLayer", "FrCard");
        string key = Settings.Get("SteamApiKey").Trim();
        if (key.Length == 0) {
            frSub.Text = "To see who's online, the launcher needs a Steam Web API key of your own. Make one at steamcommunity.com/dev/apikey (any domain name, e.g. localhost), then paste it in Settings > Steam friends.";
            frRows.Children.Clear();
            var open = MakeButton("Open the key page", 40); open.HorizontalAlignment = HorizontalAlignment.Left;
            open.Click += (s, e) => Stores.Open("https://steamcommunity.com/dev/apikey");
            frRows.Children.Add(open);
            return;
        }
        if (frLast != null) FillFriends(frLast);
        if ((DateTime.Now - frAt).TotalSeconds < 60 && frLast != null) return;
        frSub.Text = frLast == null ? "Looking..." : frSub.Text;
        ThreadPool.QueueUserWorkItem(_ => {
            List<Friend> l = null; string err = null; int total = 0;
            try { l = SteamFriends.Online(key, out total); }
            catch (WebException we) { var r = we.Response as HttpWebResponse; err = r != null && (r.StatusCode == HttpStatusCode.Forbidden || r.StatusCode == HttpStatusCode.Unauthorized) ? "Steam didn't accept the key, or your friends list is private. Check Settings > Steam friends." : "Couldn't reach Steam: " + we.Message; }
            catch (Exception e) { err = e.Message; }
            W.Dispatcher.BeginInvoke(new Action(() => {
                if (err != null) { frSub.Text = err; return; }
                frLast = l; frTotal = total; frAt = DateTime.Now;
                FillFriends(l);
            }));
        });
    }
    void FillFriends(List<Friend> l) {
        int playing = l.Count(f => f.Game != null);
        frSub.Text = l.Count == 0 ? "None of your " + frTotal + " friends are online." : l.Count + " of " + frTotal + " online" + (playing > 0 ? ", " + playing + " playing" : "") + ".";
        frRows.Children.Clear();
        foreach (var f in l) {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var pic = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(8), ClipToBounds = true, Background = Ui.Brush("#1C252C"), HorizontalAlignment = HorizontalAlignment.Left,
                BorderThickness = new Thickness(2), BorderBrush = Ui.Brush(f.Game != null ? "#8fc93a" : f.State == 3 || f.State == 4 ? "#7d8a93" : "#57cbde") };
            if (f.Avatar != null) try { pic.Child = new Image { Source = new BitmapImage(new Uri(f.Avatar)), Stretch = Stretch.UniformToFill }; } catch { }
            row.Children.Add(pic);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = f.Name, FontSize = 14.5, FontFamily = Ui.Semi, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = f.Game != null ? "Playing " + f.Game : f.State == 3 || f.State == 4 ? "Away" : f.State == 2 ? "Busy" : "Online", FontSize = 12,
                Foreground = Ui.Brush(f.Game != null ? "#8fc93a" : Ui.Muted), TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(text, 1); row.Children.Add(text);
            frRows.Children.Add(row);
        }
    }
    void CloseFriends() { if (!FriendsOpen) return; Sound.Play(Sfx.Back); HideCard("FrLayer", "FrCard"); }
}
}
