// Friends, the launcher's own: every profile has a friend code (RL-XXXX-XXXX); friends add each other by code and see
// what the others are playing. It all goes through a small server of your own (server\friends in the repository,
// set up on Cloudflare's free plan; the address is in Settings > Friends, or in friends-server.txt beside the
// launcher, which the installer carries to friends who install your build).
// While the launcher runs it says once a minute that you're here, and what you're playing (Settings > Friends:
// Show what I'm playing). Guest has no code. The Friends tile opens the list: requests first, then friends playing,
// online, offline. A accepts a request; Y declines it or (pressed twice) removes a friend; X adds a friend by code.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace RecompLauncher {

class NetFriend { public string Id, Code, Name, Look, Playing; public bool Online; public DateTime Since, Seen; }

static class FriendsNet {
    // the server: Settings, else friends-server.txt beside the launcher (put there by the installer)
    public static string Server {
        get {
            string s = Settings.Get("FriendsServer");
            if (string.IsNullOrEmpty(s)) try { string f = Path.Combine(Paths.Root, "friends-server.txt"); if (File.Exists(f)) s = File.ReadAllText(f).Trim(); } catch { }
            return string.IsNullOrEmpty(s) ? null : s.TrimEnd('/');
        }
    }
    public static bool Ready { get { return Server != null && !Profiles.IsGuest; } }
    static string Key(string what) { return "Friend" + what + ":" + Profiles.Current.Id; }
    public static string MyCode { get { return Profiles.IsGuest ? null : NullIfEmpty(Settings.Get(Key("Code"))); } }
    static string NullIfEmpty(string s) { return string.IsNullOrEmpty(s) ? null : s; }

    static Dictionary<string, object> Post(string path, Dictionary<string, object> body, bool authed) {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        using (var wc = new WebClient()) {
            wc.Encoding = Encoding.UTF8;
            wc.Headers[HttpRequestHeader.ContentType] = "application/json";
            wc.Headers[HttpRequestHeader.UserAgent] = "RecompLauncher";
            if (authed) wc.Headers[HttpRequestHeader.Authorization] = "Bearer " + Settings.Get(Key("Id")) + ":" + Settings.Get(Key("Secret"));
            try { return Json.Parse(wc.UploadString(Server + path, "POST", Json.Write(body ?? new Dictionary<string, object>()))) as Dictionary<string, object>; }
            catch (WebException e) {
                // the server's own message ("No one has that code.") when it sent one
                try { using (var r = new StreamReader(e.Response.GetResponseStream())) { var d = Json.Parse(r.ReadToEnd()) as Dictionary<string, object>; if (d != null) return d; } } catch { }
                throw;
            }
        }
    }
    // this profile's code, made the first time (on the server)
    public static bool EnsureRegistered() {
        if (!Ready) return false;
        if (MyCode != null && NullIfEmpty(Settings.Get(Key("Secret"))) != null) return true;
        var r = Post("/register", new Dictionary<string, object> { { "name", Profiles.DisplayName(Profiles.Current) }, { "look", Profiles.Current.Character ?? "" } }, false);
        if (r == null || Json.Str(r, "code") == null) return false;
        Settings.Set(Key("Id"), Json.Str(r, "id")); Settings.Set(Key("Secret"), Json.Str(r, "secret")); Settings.Set(Key("Code"), Json.Str(r, "code"));
        Settings.Save();
        return true;
    }
    public static void Presence(string playing) {
        if (!EnsureRegistered()) return;
        bool share = Settings.Get("FriendsShare") != "off";
        Post("/me", new Dictionary<string, object> { { "name", Profiles.DisplayName(Profiles.Current) }, { "look", Profiles.Current.Character ?? "" }, { "playing", share ? playing : null }, { "share", share } }, true);
    }
    public static void List(out List<NetFriend> friends, out List<NetFriend> incoming, out List<NetFriend> outgoing) {
        friends = new List<NetFriend>(); incoming = new List<NetFriend>(); outgoing = new List<NetFriend>();
        if (!EnsureRegistered()) return;
        var r = Post("/list", null, true);
        Func<string, List<NetFriend>> read = key => {
            var l = new List<NetFriend>(); object o;
            if (r != null && r.TryGetValue(key, out o) && o is System.Collections.IEnumerable)
                foreach (var d in ((System.Collections.IEnumerable)o).OfType<Dictionary<string, object>>())
                    l.Add(new NetFriend { Id = Json.Str(d, "id"), Code = Json.Str(d, "code"), Name = Json.Str(d, "name"), Look = Json.Str(d, "look"), Playing = Json.Str(d, "playing"),
                        Online = Json.Bool(d, "online"), Since = FromMs(Json.Num(d, "since")), Seen = FromMs(Json.Num(d, "seen")) });
            return l;
        };
        friends = read("friends"); incoming = read("incoming"); outgoing = read("outgoing");
    }
    static DateTime FromMs(double ms) { return ms <= 0 ? DateTime.MinValue : new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime(); }
    // a message for the player: null when it worked
    public static string Add(string code, out string state, out string name) {
        state = name = null;
        if (!EnsureRegistered()) return "Friends aren't set up yet: Settings > Friends.";
        var r = Post("/add", new Dictionary<string, object> { { "code", code } }, true);
        if (r == null) return "The friends server didn't answer.";
        if (Json.Str(r, "error") != null) return Json.Str(r, "error");
        state = Json.Str(r, "state"); name = Json.Str(r, "name");
        return null;
    }
    public static void Accept(string id) { Post("/accept", new Dictionary<string, object> { { "id", id } }, true); }
    public static void Remove(string id) { Post("/remove", new Dictionary<string, object> { { "id", id } }, true); }
}

partial class Shell {
    ListPage frPage; List<NetFriend> frFriends = new List<NetFriend>(), frIn = new List<NetFriend>(), frOut = new List<NetFriend>();
    string frNote; NetFriend frRemoveAsk; DispatcherTimer frBeat;
    // the presence beat: at start, once a minute, and when a game starts or stops
    void StartFriendsBeat() {
        if (frBeat != null) return;
        frBeat = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        frBeat.Tick += (s, e) => SendPresence();
        frBeat.Start();
        SendPresence();
    }
    void SendPresence() {
        if (!FriendsNet.Ready) return;
        string game = playing != null ? playing.Name : null;
        ThreadPool.QueueUserWorkItem(_ => { try { FriendsNet.Presence(game); } catch { } });
    }
    void OpenFriendList() {
        if (frPage == null) {
            frPage = MakeListPage("Fl", "FRIENDS", "Friends", 780, "Add a friend");
            frPage.Fill = FillFriendList;
            frPage.OnExtra = AddFriendByCode;
            frPage.OnClose = () => { frRemoveAsk = null; };
        }
        frNote = FriendsNet.Ready ? "Looking for your friends..." : null; frRemoveAsk = null;
        OpenListPage(frPage);
        RefreshFriendList();
    }
    void RefreshFriendList() {
        if (!FriendsNet.Ready) { FillFriendList(); return; }
        ThreadPool.QueueUserWorkItem(_ => {
            string err = null; List<NetFriend> f = null, i = null, o = null;
            try { FriendsNet.List(out f, out i, out o); } catch (Exception e) { err = "Couldn't reach the friends server (" + e.Message + ")."; }
            W.Dispatcher.BeginInvoke(new Action(() => {
                if (err == null) { frFriends = f; frIn = i; frOut = o; frNote = null; } else frNote = err;
                if (ListPageOpen && lpOpen == frPage) FillFriendList();
            }));
        });
    }
    void FillFriendList() {
        var rows = new List<ListRow>();
        if (!FriendsNet.Ready) {
            frPage.Sub.Text = Profiles.IsGuest ? "Guest has no friend code: pick your own profile to see your friends." : "Friends go through a small server of your own (free, about five minutes to set up).";
            frPage.Extra.IsEnabled = false;
            if (!Profiles.IsGuest)
                rows.Add(new ListRow { Glyph = "", C1 = "#5fae3a", C2 = "#a8d86b", Title = "Set up friends", Text = "Settings > Friends: the server's address. The steps are in server\\friends\\README.md.", Do = () => { CloseListPage(); if (!running) OpenSettings("Friends"); } });
        } else {
            frPage.Extra.IsEnabled = true;
            frPage.Sub.Text = (frNote != null ? frNote + "  " : "") + "Your friend code: " + (FriendsNet.MyCode ?? "...") + "  •  share it so friends can add you.";
            foreach (var r in frIn) {
                var req = r;
                rows.Add(new ListRow { Pic = FriendFace(r.Look), Glyph = "", C1 = "#f4a312", C2 = "#f2d24b", Title = r.Name + " wants to be friends", Text = "A: accept   •   Y: decline", Right = r.Code, Bold = true,
                    Do = () => FriendAction(() => FriendsNet.Accept(req.Id), "You and " + req.Name + " are friends now."), Alt = () => FriendAction(() => FriendsNet.Remove(req.Id), "Declined."), AltLabel = "Decline" });
            }
            foreach (var r in frFriends.OrderByDescending(x => x.Playing != null).ThenByDescending(x => x.Online).ThenByDescending(x => x.Seen)) {
                var fr = r;
                string text = r.Playing != null ? "Playing " + r.Playing + (r.Since > DateTime.MinValue ? "  •  " + Minutes(DateTime.Now - r.Since) : "")
                    : r.Online ? "Online" : r.Seen > DateTime.MinValue ? "Last online " + Playtime.When(r.Seen) : "Offline";
                if (frRemoveAsk == r) text = "Remove " + r.Name + " from your friends? Press Y again.";
                rows.Add(new ListRow { Pic = FriendFace(r.Look), Glyph = "", C1 = r.Online ? "#5fae3a" : "#5b6670", C2 = r.Online ? "#a8d86b" : "#8a959e", Title = r.Name, Text = text, Right = r.Code,
                    Dim = !r.Online, Bold = r.Playing != null, AltLabel = "Remove",
                    Alt = () => { if (frRemoveAsk != fr) { frRemoveAsk = fr; FillFriendList(); return; } frRemoveAsk = null; FriendAction(() => FriendsNet.Remove(fr.Id), fr.Name + " is off your list."); } });
            }
            foreach (var r in frOut) {
                var o = r;
                rows.Add(new ListRow { Pic = FriendFace(r.Look), Glyph = "", C1 = "#5b6670", C2 = "#8a959e", Title = r.Name, Text = "Asked: waiting for them to accept   •   Y: cancel", Right = r.Code, Dim = true,
                    Alt = () => FriendAction(() => FriendsNet.Remove(o.Id), "Cancelled."), AltLabel = "Cancel" });
            }
        }
        // Steam friends, when a Steam key is set
        if (!string.IsNullOrEmpty(Settings.Get("SteamApiKey")))
            rows.Add(new ListRow { Glyph = "", C1 = "#1b2838", C2 = "#2a475e", Title = "Steam friends", Text = "Who's online on Steam", Do = () => { CloseListPage(); OpenFriends(); } });
        FillRows(frPage, rows, "No friends yet. Press Add a friend and type a friend's code, or give them yours.");
    }
    static string Minutes(TimeSpan t) { return t.TotalMinutes < 60 ? Math.Max(1, (int)t.TotalMinutes) + " min" : (int)t.TotalHours + " h " + t.Minutes + " min"; }
    // a friend's character, small (cached by its look)
    static readonly Dictionary<string, ImageSource> friendFaces = new Dictionary<string, ImageSource>();
    static ImageSource FriendFace(string look) {
        if (string.IsNullOrEmpty(look)) return null;
        ImageSource img;
        if (friendFaces.TryGetValue(look, out img)) return img;
        try { img = AvatarArt.Render(AvatarDef.Parse(look), new AvatarState { Backdrop = true }, AvatarArt.Crop.Head, 96, 96); } catch { img = null; }
        friendFaces[look] = img;
        return img;
    }
    void FriendAction(Action a, string done) {
        ThreadPool.QueueUserWorkItem(_ => {
            string err = null;
            try { a(); } catch (Exception e) { err = e.Message; }
            W.Dispatcher.BeginInvoke(new Action(() => { SetStep(err == null ? done : "Couldn't reach the friends server: " + err, err == null ? Ui.Teal : Ui.Red); RefreshFriendList(); }));
        });
    }
    void AddFriendByCode() {
        if (!FriendsNet.Ready) return;
        OpenOsk("Your friend's code (RL-XXXX-XXXX)", "RL-", null, text => {
            text = (text ?? "").Trim();
            if (text.Length < 4) return;
            ThreadPool.QueueUserWorkItem(_ => {
                string msg, state, name;
                try { msg = FriendsNet.Add(text, out state, out name); } catch (Exception e) { msg = "Couldn't reach the friends server: " + e.Message; state = name = null; }
                W.Dispatcher.BeginInvoke(new Action(() => {
                    if (msg != null) { SetStep(msg, Ui.Gold); frNote = msg; }
                    else { frNote = state == "friends" ? "You and " + name + " are friends now." : "Asked " + name + ". You'll be friends when they accept."; SetStep(frNote, Ui.Teal); }
                    RefreshFriendList();
                }));
            });
        }, null);
    }
}
}
