// TV & Movies: adding a profile, one step at a time.
//   1  What did your provider give you: a login (server, username, password; "Xtream") or one long link (M3U)?
//   2  Just those details. A playlist link that has the login inside it (get.php?username=...&password=...) is turned
//      into the login by itself: that gives TV Archive, the provider's guide and film details too.
//   3  Signing in, and what was found
//   4  A name and a picture
//   5  Subtitles found online (optional): OpenSubtitles, explained step by step
//   6  Done: open it
// Editing a profile later still uses the full form (TvAccountForm).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Controls;

namespace RecompLauncher {

partial class Shell {
    ListPage tvWizPage; int tvWizStep; TvAccount tvWiz; string tvWizNote; bool tvWizBusy, tvWizHelp;

    void TvWizardStart() {
        tvWiz = new TvAccount { Id = "acc" + DateTime.Now.Ticks, Name = "", Type = "", Avatar = "a" + ((TvStore.Accounts.Count + 1) % TvAvatars.Length) };
        tvWizStep = 1; tvWizNote = null; tvWizBusy = false; tvWizHelp = false;
        if (tvWizPage == null) {
            tvWizPage = MakeListPage("TvWizard", "ADD A PROFILE", "", 640, null);
            tvWizPage.Layer.SetValue(Panel.ZIndexProperty, 900);
            tvWizPage.Fill = TvFillWizard;
        }
        OpenListPage(tvWizPage);
    }
    void TvWizGo(int step) { tvWizStep = step; tvWizNote = null; tvWizHelp = false; Sound.Play(Sfx.Select); TvFillWizard(); }
    ListRow TvWizBack(int to) { return new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Back", Do = () => TvWizGo(to) }; }

    void TvFillWizard() {
        var p = tvWizPage; var a = tvWiz; var rows = new List<ListRow>();
        Func<string, string> show = v => string.IsNullOrEmpty(v) ? "(empty: pick to type it)" : v;
        switch (tvWizStep) {
        case 1:
            p.Title.Text = "Step 1 of 5  •  What did you get?";
            p.Sub.Text = tvWizHelp
                ? "A LOGIN looks like this in your provider's e-mail or app:\n    Server / URL / Host:  http://example-tv.com:8080\n    Username:  john123\n    Password:  ab12cd34\n\nA PLAYLIST LINK (M3U) is one long link, for example:\n    http://example-tv.com/get.php?username=john123&password=ab12cd34&type=m3u_plus\n    or a link ending in .m3u or .m3u8\n\nGot both? Pick the login: it gives the most (TV Archive, your provider's guide, film details and episode lists)."
                : "Your IPTV provider gave you one of these two. Pick the one you have; the next step asks only for that.";
            rows.Add(new ListRow { Glyph = "", C1 = "#3fb8a0", C2 = "#2f8fe0", Title = "A login", Text = "A server address, a username and a password (often called \"Xtream codes\")  •  the best choice",
                Do = () => { a.Type = "xtream"; TvWizGo(2); } });
            rows.Add(new ListRow { Glyph = "", C1 = "#8b6cff", C2 = "#3b1e9e", Title = "One long link (M3U playlist)", Text = "A link that starts with http and often has \"m3u\" in it",
                Do = () => { a.Type = "m3u"; TvWizGo(2); } });
            rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = tvWizHelp ? "Got it" : "Not sure? Show me what they look like", Do = () => { tvWizHelp = !tvWizHelp; TvFillWizard(); } });
            break;
        case 2:
            if (a.Type == "m3u") {
                p.Title.Text = "Step 2 of 5  •  Your playlist link";
                p.Sub.Text = "Type or paste the whole link exactly as your provider gave it (with a controller: the keyboard on screen; on this PC: Ctrl+V works in it)." + (tvWizNote != null ? "\n\n" + tvWizNote : "");
                rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Playlist link", Text = string.IsNullOrEmpty(a.Url) ? "(empty: pick to type it)" : TvShort(a.Url),
                    Do = () => OpenOsk("Your playlist (M3U) link", a.Url, null, v => { a.Url = (v ?? "").Trim(); TvWizLinkCheck(); TvFillWizard(); }, null, false, true) });
            } else {
                p.Title.Text = "Step 2 of 5  •  Your login";
                p.Sub.Text = "Copy these exactly from your provider (capitals matter). The server address is the part like http://example-tv.com:8080." + (tvWizNote != null ? "\n\n" + tvWizNote : "");
                rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Server address", Text = show(a.Server),
                    Do = () => OpenOsk("Server address (for example http://example-tv.com:8080)", a.Server, null, v => { a.Server = TvCleanServer(v); TvFillWizard(); }, null, false, true) });
                rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Username", Text = show(a.User),
                    Do = () => OpenOsk("Username", a.User, null, v => { a.User = (v ?? "").Trim(); TvFillWizard(); }, null, false, true) });
                rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Password", Text = string.IsNullOrEmpty(a.Pass) ? "(empty: pick to type it)" : new string('•', Math.Min(12, a.Pass.Length)),
                    Do = () => OpenOsk("Password", a.Pass, null, v => { a.Pass = v ?? ""; TvFillWizard(); }, null, true) });
            }
            rows.Insert(0, new ListRow { Glyph = "\uE8EA", C1 = "#3fb8a0", C2 = "#2f8fe0", Title = "Type it on your phone", Text = "Scan a code with your phone and fill it in there: much quicker than with a controller", Do = TvPairStart });
            bool ready = a.Type == "m3u" ? !string.IsNullOrEmpty(a.Url) : !string.IsNullOrEmpty(a.Server) && !string.IsNullOrEmpty(a.User) && !string.IsNullOrEmpty(a.Pass);
            rows.Add(new ListRow { Glyph = "", C1 = ready ? "#3fb8a0" : "#33424d", C2 = ready ? "#2f8fe0" : "#33424d", Title = "Next: sign in", Text = ready ? "Checks it with your provider" : "Fill in the rows above first",
                Do = () => { if (ready) { TvWizGo(3); TvWizSignIn(); } else Sound.Play(Sfx.Bump); } });
            rows.Add(TvWizBack(1));
            break;
        case 3:
            p.Title.Text = "Step 3 of 5  •  Signing in";
            p.Sub.Text = tvWizBusy ? "Talking to your provider... this can take up to half a minute." : tvWizNote ?? "";
            if (!tvWizBusy) {
                if (tvWizNote != null && tvWizNote.StartsWith("✓")) rows.Add(new ListRow { Glyph = "", C1 = "#3fb8a0", C2 = "#2f8fe0", Title = "Next", Do = () => TvWizGo(4) });
                else {
                    rows.Add(new ListRow { Glyph = "", C1 = "#2f8fe0", C2 = "#2f8fe0", Title = "Try again", Do = () => { TvFillWizard(); TvWizSignIn(); } });
                    rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Change the details", Do = () => TvWizGo(2) });
                }
            }
            break;
        case 4:
            p.Title.Text = "Step 4 of 5  •  Name and picture";
            p.Sub.Text = "How it shows on \"Who's watching?\". You can change both later in Manage profiles.";
            rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Name", Text = a.Name, Do = () => OpenOsk("A name for this profile", a.Name, null, v => { if (!string.IsNullOrWhiteSpace(v)) a.Name = v.Trim(); TvFillWizard(); }, null) });
            for (int i = 0; i < TvAvatars.Length; i++) { string id = "a" + i; rows.Add(new ListRow { Pic = TvAvatarImage(id), C1 = TvAvatars[i][0], C2 = TvAvatars[i][1], Title = "Picture " + (i + 1), Text = a.Avatar == id ? "Picked" : null, Do = () => { a.Avatar = id; TvFillWizard(); } }); }
            rows.Insert(1, new ListRow { Glyph = "", C1 = "#3fb8a0", C2 = "#2f8fe0", Title = "Next", Do = () => TvWizGo(5) });
            break;
        case 5:
            TvPlayerPrefs.Load(); bool has = !string.IsNullOrEmpty(TvPlayerPrefs.OsKey);
            p.Title.Text = "Step 5 of 5  •  Subtitles (optional)";
            p.Sub.Text = has ? "Online subtitles are already set up (" + (string.IsNullOrEmpty(TvPlayerPrefs.OsUser) ? "API key" : TvPlayerPrefs.OsUser) + "). While watching: Subtitles → Find online."
                : "Many streams already have subtitles: pick them under Subtitles while watching. For films or series without them, the launcher can find subtitles online at OpenSubtitles (free, needs your own account):\n\n" +
                  "  1.  On your phone or PC, go to opensubtitles.com and make a free account (Sign up).\n" +
                  "  2.  Once signed in, open your profile menu → API consumers (opensubtitles.com/consumers).\n" +
                  "  3.  Pick \"New consumer\", give it any name (for example Recomp Launcher), and save.\n" +
                  "  4.  Copy the API key it shows, then pick \"Set up\" below and type the key, your username and your password.\n\n" +
                  "While watching: Subtitles → Find online. The free account gets a number of downloads a day.";
            if (!has) rows.Add(new ListRow { Glyph = "", C1 = "#2f8fe0", C2 = "#2f8fe0", Title = "Set up OpenSubtitles", Text = "Your API key, username and password",
                Do = () => OpenOsk("Your OpenSubtitles API key", "", null, key => {
                    key = (key ?? "").Trim(); if (key.Length == 0) return;
                    OpenOsk("Your OpenSubtitles username", "", null, user => {
                        OpenOsk("Your OpenSubtitles password", "", null, pass => {
                            TvPlayerPrefs.OsKey = key; TvPlayerPrefs.OsUser = (user ?? "").Trim(); TvPlayerPrefs.OsPass = pass ?? ""; TvPlayerPrefs.Save(); tvOsToken = null; TvFillWizard();
                        }, null, true);
                    }, null, false, true);
                }, null, true, true) });
            rows.Add(new ListRow { Glyph = "", C1 = has ? "#3fb8a0" : "#33424d", C2 = has ? "#2f8fe0" : "#33424d", Title = has ? "Next" : "Skip for now", Text = has ? null : "You can set it up later in Manage profiles", Do = () => { TvWizSave(); TvWizGo(6); } });
            break;
        default:
            p.Title.Text = "Ready";
            p.Sub.Text = a.Name + " is added: its films, series and live TV, with the guide" + (a.Type == "xtream" ? " and TV Archive (when your provider keeps past programmes)" : "") + ".";
            rows.Add(new ListRow { Glyph = "", C1 = "#3fb8a0", C2 = "#2f8fe0", Title = "Open " + a.Name, Do = () => { CloseListPage(); int idx = TvStore.Accounts.IndexOf(a) + 1; if (TvProfilesOpen) TvProfileAct(idx, false); else { tvAcc = a; TvApplyProfile(); TvShowPage(TvStartPage()); } } });
            rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Close", Do = () => { CloseListPage(); if (TvProfilesOpen) FillTvProfiles(); } });
            break;
        }
        FillRows(p, rows, "");
    }
    static string TvShort(string url) { return url.Length > 60 ? url.Substring(0, 57) + "..." : url; }
    // "example.com:8080/", "http://example.com:8080/get.php?..." -> "http://example.com:8080"
    static string TvCleanServer(string v) {
        v = (v ?? "").Trim(); if (v.Length == 0) return v;
        if (!v.Contains("://")) v = "http://" + v;
        var m = Regex.Match(v, @"^(https?://[^/\s?#]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : v.TrimEnd('/');
    }
    // a playlist link with the login inside: use the login (more features)
    void TvWizLinkCheck() {
        var a = tvWiz; tvWizNote = null;
        var m = Regex.Match(a.Url ?? "", @"^(https?://[^/\s?#]+)/(?:get|player_api)\.php\?(?=.*\busername=([^&\s]+))(?=.*\bpassword=([^&\s]+))", RegexOptions.IgnoreCase);
        if (!m.Success) return;
        a.Type = "xtream"; a.Server = m.Groups[1].Value; a.User = Uri.UnescapeDataString(m.Groups[2].Value); a.Pass = Uri.UnescapeDataString(m.Groups[3].Value);
        tvWizNote = "✓ This link has your login inside it, so the launcher uses the login instead: that also gives TV Archive, your provider's guide and film details. The server, username and password are filled in below.";
    }
    void TvWizSignIn() {
        var a = tvWiz; tvWizBusy = true; tvWizNote = null; TvFillWizard();
        new Thread(() => {
            string msg; bool ok = TvSource.SignIn(a, out msg); string counts = "";
            if (ok) { try { TvSource.LoadAccount(a); counts = "\n\nFound " + (a.Live ?? new List<TvItem>()).Count + " channels, " + (a.Movies ?? new List<TvItem>()).Count + " films and " + (a.Series ?? new List<TvItem>()).Count + " series."; } catch { } }
            W.Dispatcher.BeginInvoke(new Action(() => {
                tvWizBusy = false;
                tvWizNote = ok ? "✓  Signed in. " + msg + counts
                    : "✗  " + msg + "\n\nCheck the details (capitals matter, and the server address needs its :port when your provider gave one). Some providers only work on one device at a time.";
                if (ok && string.IsNullOrEmpty(a.Name)) { var h = Regex.Match(a.Type == "m3u" ? a.Url ?? "" : a.Server ?? "", @"://(?:www\.)?([^/:.]+)"); a.Name = h.Success ? char.ToUpper(h.Groups[1].Value[0]) + h.Groups[1].Value.Substring(1) : "My IPTV"; }
                if (ListPageOpen && lpOpen == tvWizPage) TvFillWizard();
            }));
        }) { IsBackground = true }.Start();
    }
    void TvWizSave() {
        var a = tvWiz; if (TvStore.Accounts.Contains(a)) return;
        if (string.IsNullOrEmpty(a.Name)) a.Name = "My IPTV";
        TvStore.Accounts.Add(a); TvStore.Save();
        if (TvProfilesOpen) FillTvProfiles();
    }
}
}
