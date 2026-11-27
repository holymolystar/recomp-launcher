// TV & Movies: keeping a profile safe, keeping things, and getting help.
//   PIN lock        a profile can need a PIN (4 digits or more) to open or change it
//   Hide adult      categories and titles with adult words stay out of a profile (for children)
//   Recording       record a live channel now, or a programme from the guide (it starts and stops by itself), into
//                   your recordings folder; a second, invisible player does it, so watching goes on as it was
//   Downloads       films and episodes saved to watch without internet; one at a time, with a cancel
//   Phone pairing   typing a long login on a controller is slow: scan the QR code and type it on your phone instead
//                   (only while that screen is open, with a one-time code, on this PC's home network)
//   Report          a problem report (without your login) copied to paste wherever you ask for help
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace RecompLauncher {

// what batch 5 keeps (tv-safe.json): PINs (hashed), which profiles hide adult content, scheduled recordings, downloads
static class TvSafeStore {
    static string FilePath { get { return Path.Combine(Paths.Root, "tv-safe.json"); } }
    public static readonly Dictionary<string, string> Pins = new Dictionary<string, string>(), Downloads = new Dictionary<string, string>();
    public static readonly HashSet<string> Kids = new HashSet<string>();
    public static readonly List<Dictionary<string, object>> Schedule = new List<Dictionary<string, object>>();
    public static string Folder = "";
    static bool loaded;
    public static void Load() {
        if (loaded) return; loaded = true;
        var d = Json.ReadFile(FilePath); if (d == null) return;
        var p = Json.Obj(d, "Pins"); if (p != null) foreach (var kv in p) Pins[kv.Key] = Convert.ToString(kv.Value);
        var dl = Json.Obj(d, "Downloads"); if (dl != null) foreach (var kv in dl) Downloads[kv.Key] = Convert.ToString(kv.Value);
        foreach (var k in Json.Strings(d, "Kids")) Kids.Add(k);
        object s; if (d.TryGetValue("Schedule", out s) && s is System.Collections.IEnumerable) foreach (var r in ((System.Collections.IEnumerable)s).OfType<Dictionary<string, object>>()) Schedule.Add(r);
        Folder = Json.Str(d, "Folder") ?? "";
    }
    public static void Save() {
        try {
            Json.WriteFile(FilePath, new Dictionary<string, object> { { "Pins", Pins.ToDictionary(kv => kv.Key, kv => (object)kv.Value) }, { "Downloads", Downloads.ToDictionary(kv => kv.Key, kv => (object)kv.Value) },
                { "Kids", Kids.ToList() }, { "Schedule", Schedule.ToList() }, { "Folder", Folder } });
        } catch { }
    }
    public static string Hash(string pin) { using (var h = SHA256.Create()) return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes("RecompLauncher.TV.pin:" + pin))); }
    public static string Root { get { var f = string.IsNullOrEmpty(Folder) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Recomp TV") : Folder; return f; } }
    public static string Recordings { get { var f = Path.Combine(Root, "Recordings"); Directory.CreateDirectory(f); return f; } }
    public static string DownloadDir { get { var f = Path.Combine(Root, "Downloads"); Directory.CreateDirectory(f); return f; } }
    public static string SafeName(string s) { s = Regex.Replace(s ?? "video", "[\\\\/:*?\"<>|]+", " ").Trim(); return s.Length > 90 ? s.Substring(0, 90) : s; }
}

partial class Shell {
    // ---------- PIN lock ----------
    // run "go" once the profile's PIN is right (or straight away when it has none)
    void TvWithPin(string profile, string name, Action go) {
        TvSafeStore.Load(); string h;
        if (!TvSafeStore.Pins.TryGetValue(profile, out h)) { go(); return; }
        OpenOsk("The PIN for " + name, "", null, v => {
            if (TvSafeStore.Hash((v ?? "").Trim()) == h) go();
            else { Sound.Play(Sfx.Bump); SetStep("TV: that's not the PIN for " + name + ".", Ui.Red); }
        }, null, true);
    }
    // ---------- hiding adult content ----------
    static readonly Regex TvAdult = new Regex(@"\b(adult|adults|xxx|18\s*\+|\+\s*18|porn\w*|erotic\w*|sex\w*|hentai|playboy|hustler|brazzers|for adults)\b", RegexOptions.IgnoreCase);
    bool TvKidsOn { get { TvSafeStore.Load(); return TvSafeStore.Kids.Contains(TvMine.Profile(tvAcc)); } }
    bool TvAllowed(TvItem x) { return TvLangOk(x) && (!TvKidsOn || x == null || !(TvAdult.IsMatch(x.Title ?? "") || TvAdult.IsMatch(x.Group ?? "") || TvAdult.IsMatch(x.Genre ?? ""))); }
    bool TvAllowedName(string cat) { return !TvKidsOn || !TvAdult.IsMatch(cat ?? ""); }

    // ---------- the settings rows (in a profile's settings) ----------
    IEnumerable<ListRow> TvSafeRows() {
        TvSafeStore.Load();
        string id = tvAccFree ? "free" : tvAccEdit.Id; bool pin = TvSafeStore.Pins.ContainsKey(id), kids = TvSafeStore.Kids.Contains(id);
        yield return new ListRow { Glyph = "", C1 = "#c28a1e", C2 = "#e0b33c", Title = "PIN lock: " + (pin ? "on" : "off"), Text = pin ? "Pick to turn it off" : "A PIN is needed to open or change this profile",
            Do = () => {
                if (pin) { TvSafeStore.Pins.Remove(id); TvSafeStore.Save(); TvFillAccountForm(); return; }
                OpenOsk("A PIN for this profile (4 digits or more)", "", null, v => {
                    v = (v ?? "").Trim(); if (v.Length < 4) { SetStep("TV: a PIN needs 4 digits or more.", Ui.Red); return; }
                    OpenOsk("The same PIN again", "", null, v2 => { if ((v2 ?? "").Trim() != v) { SetStep("TV: the PINs weren't the same; nothing changed.", Ui.Red); return; } TvSafeStore.Pins[id] = TvSafeStore.Hash(v); TvSafeStore.Save(); TvFillAccountForm(); }, null, true);
                }, null, true);
            } };
        yield return new ListRow { Glyph = "", C1 = "#c28a1e", C2 = "#e0b33c", Title = "Hide adult content: " + (kids ? "on" : "off"), Text = "Categories, channels and titles with adult words stay out of this profile",
            Do = () => { if (kids) TvSafeStore.Kids.Remove(id); else TvSafeStore.Kids.Add(id); TvSafeStore.Save(); TvFillAccountForm(); } };
        yield return new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Recordings and downloads go to:", Text = TvSafeStore.Root + "  •  every profile  •  pick to change",
            Do = () => {
                using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "A folder for your TV recordings and downloads", SelectedPath = TvSafeStore.Root })
                    if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) { TvSafeStore.Folder = d.SelectedPath; TvSafeStore.Save(); TvFillAccountForm(); }
            } };
    }

    // ---------- recording ----------
    class TvRec { public TvItem Ch; public IntPtr Ctx; public string File, Title; public DateTime StopAt; }
    readonly List<TvRec> tvRecs = new List<TvRec>(); DispatcherTimer tvRecTimer;
    TvRec TvRecording(TvItem ch) { return ch == null ? null : tvRecs.FirstOrDefault(r => r.Ch.Key == ch.Key); }
    void TvRecordStart(TvItem ch, string title, DateTime stopAt) {
        if (TvRecording(ch) != null) { SetStep("TV: " + ch.Title + " is already being recorded.", Ui.Teal); return; }
        if (!Mpv.Ready) { SetStep("TV: watch one channel first; the player is fetched then.", Ui.Teal); return; }
        try {
            string file = Path.Combine(TvSafeStore.Recordings, TvSafeStore.SafeName(ch.Title + "  " + DateTime.Now.ToString("yyyy-MM-dd HH.mm") + (string.IsNullOrEmpty(title) ? "" : "  " + title)) + ".ts");
            var ctx = Mpv.CreateHeadless(); TvApplyPlayerPrefs(ctx);
            Mpv.Set(ctx, "stream-record", file);
            Mpv.Command(ctx, "loadfile", TvUrl(ch), "replace");
            tvRecs.Add(new TvRec { Ch = ch, Ctx = ctx, File = file, Title = title, StopAt = stopAt });
            TvRecTimerOn();
            SetStep("TV: recording " + ch.Title + (string.IsNullOrEmpty(title) ? "" : " (" + title + ")") + " until " + stopAt.ToString("HH:mm") + ", into " + Path.GetFileName(file) + ".", Ui.Teal);
        } catch (Exception e) { SetStep("TV: recording didn't start: " + e.Message, Ui.Red); }
    }
    void TvRecordStop(TvRec r, bool say = true) {
        try { Mpv.Set(r.Ctx, "stream-record", ""); Mpv.Command(r.Ctx, "stop"); } catch { }
        var c = r.Ctx; r.Ctx = IntPtr.Zero; tvRecs.Remove(r);
        ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(800); Mpv.Destroy(c); });
        if (say) SetStep("TV: the recording of " + r.Ch.Title + " is saved in " + TvSafeStore.Recordings + ".", Ui.Teal);
    }
    void TvRecTimerOn() {
        if (tvRecTimer == null) { tvRecTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) }; tvRecTimer.Tick += (s, e) => TvRecTick(); }
        tvRecTimer.Start();
    }
    // stops what's done, starts what's planned
    void TvRecTick() {
        var now = DateTime.Now;
        foreach (var r in tvRecs.Where(x => now >= x.StopAt).ToList()) TvRecordStop(r);
        TvSafeStore.Load(); bool changed = false;
        foreach (var s in TvSafeStore.Schedule.ToList()) {
            DateTime st, sp; DateTime.TryParse(Json.Str(s, "Start"), null, System.Globalization.DateTimeStyles.RoundtripKind, out st); DateTime.TryParse(Json.Str(s, "Stop"), null, System.Globalization.DateTimeStyles.RoundtripKind, out sp);
            st = st.ToLocalTime(); sp = sp.ToLocalTime();
            if (now > sp) { TvSafeStore.Schedule.Remove(s); changed = true; continue; }
            if (now >= st.AddMinutes(-1)) {
                TvSafeStore.Schedule.Remove(s); changed = true;
                var ch = TvMine.Recall(Json.Str(s, "Key")); if (ch != null) TvRecordStart(ch, Json.Str(s, "Title"), sp.AddMinutes(2));
            }
        }
        if (changed) { TvSafeStore.Save(); if (TvGuideOpen) TvGuideRebuild(); }
        if (tvRecs.Count == 0 && TvSafeStore.Schedule.Count == 0) tvRecTimer.Stop();
    }
    bool TvScheduled(TvItem ch, DateTime start) { TvSafeStore.Load(); return TvSafeStore.Schedule.Any(s => Json.Str(s, "Key") == ch.Key && Json.Str(s, "Start") == start.ToString("o")); }
    void TvToggleSchedule(TvItem ch, TvFree.Prog p) {
        TvSafeStore.Load();
        var have = TvSafeStore.Schedule.FirstOrDefault(s => Json.Str(s, "Key") == ch.Key && Json.Str(s, "Start") == p.Start.ToString("o"));
        if (have != null) TvSafeStore.Schedule.Remove(have);
        else { TvSafeStore.Schedule.Add(new Dictionary<string, object> { { "Key", ch.Key }, { "Title", p.Title ?? "" }, { "Channel", ch.Title ?? "" }, { "Start", p.Start.ToString("o") }, { "Stop", p.Stop.ToString("o") } }); TvMine.Remember(ch); TvMine.Save(); }
        TvSafeStore.Save(); TvRecTimerOn();
    }
    // the recordings, as things to play
    List<TvItem> TvRecordingFiles() {
        try {
            return new DirectoryInfo(TvSafeStore.Recordings).GetFiles("*.ts").OrderByDescending(f => f.LastWriteTime).Take(40)
                .Select(f => new TvItem { Kind = "file", Id = "rec:" + f.Name, Title = Path.GetFileNameWithoutExtension(f.Name), Url = f.FullName, Group = "Your recordings", Year = f.LastWriteTime.ToString("d MMM yyyy") }).ToList();
        } catch { return new List<TvItem>(); }
    }

    // ---------- downloads ----------
    TvItem tvDlItem; double tvDlFrac; bool tvDlCancel; readonly List<TvItem> tvDlQueue = new List<TvItem>();
    string TvDownloaded(TvItem it) { TvSafeStore.Load(); string f; return it != null && TvSafeStore.Downloads.TryGetValue(it.Key, out f) && File.Exists(f) ? f : null; }
    void TvDownload(TvItem it) {
        if (TvDownloaded(it) != null) return;
        if (tvDlItem != null) { if (!tvDlQueue.Any(x => x.Key == it.Key) && tvDlItem.Key != it.Key) tvDlQueue.Add(it); SetStep("TV: " + it.Title + " downloads after " + tvDlItem.Title + ".", Ui.Teal); return; }
        tvDlItem = it; tvDlFrac = 0; tvDlCancel = false; TvMine.Remember(it); TvMine.Save();
        SetStep("TV: downloading " + it.Title + "...", Ui.Teal);
        new Thread(() => {
            string err = null, file = null;
            try {
                string url = it.Url != null && it.Url.StartsWith("ia:") ? TvFree.Resolve(it.Url) : it.Url;
                string ext = Path.GetExtension(new Uri(url).AbsolutePath); if (string.IsNullOrEmpty(ext) || ext.Length > 5) ext = "." + (it.Ext ?? "mp4");
                file = Path.Combine(TvSafeStore.DownloadDir, TvSafeStore.SafeName(it.Kind == "episode" ? (it.Group + " S" + it.Season + "E" + it.Episode + " " + it.Title) : it.Title) + ext);
                var req = (HttpWebRequest)WebRequest.Create(url); req.UserAgent = TvSource.Agent; req.Timeout = 30000;
                using (var resp = req.GetResponse()) using (var src = resp.GetResponseStream()) using (var dst = File.Create(file + ".part")) {
                    long total = resp.ContentLength, got = 0; var buf = new byte[1 << 16]; int n; DateTime said = DateTime.Now;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0) {
                        if (tvDlCancel) throw new OperationCanceledException();
                        dst.Write(buf, 0, n); got += n;
                        if (total > 0) tvDlFrac = (double)got / total;
                        if ((DateTime.Now - said).TotalSeconds > 3) { said = DateTime.Now; double f = tvDlFrac; W.Dispatcher.BeginInvoke(new Action(() => SetStep("TV: downloading " + it.Title + "  " + (int)(f * 100) + "%" + (total > 0 ? "  (" + (total / 1048576) + " MB)" : ""), Ui.Teal))); }
                    }
                }
                if (File.Exists(file)) File.Delete(file); File.Move(file + ".part", file);
            } catch (OperationCanceledException) { err = "cancelled"; } catch (Exception e) { err = e.Message; }
            if (err != null && file != null) try { File.Delete(file + ".part"); } catch { }
            W.Dispatcher.BeginInvoke(new Action(() => {
                tvDlItem = null;
                if (err == null) { TvSafeStore.Downloads[it.Key] = file; TvSafeStore.Save(); SetStep("TV: " + it.Title + " is downloaded; it plays without internet now.", Ui.Teal); }
                else SetStep(err == "cancelled" ? "TV: the download of " + it.Title + " is stopped." : "TV: " + it.Title + " couldn't be downloaded: " + err, err == "cancelled" ? Ui.Teal : Ui.Red);
                if (tvDlQueue.Count > 0) { var next = tvDlQueue[0]; tvDlQueue.RemoveAt(0); TvDownload(next); }
            }));
        }) { IsBackground = true }.Start();
    }
    void TvDeleteDownload(TvItem it) {
        string f = TvDownloaded(it); if (f == null) return;
        try { Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(f, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); } catch { }
        TvSafeStore.Downloads.Remove(it.Key); TvSafeStore.Save();
        SetStep("TV: the download of " + it.Title + " is in the Recycle Bin.", Ui.Teal);
    }
    List<TvItem> TvDownloadItems() { TvSafeStore.Load(); return TvMine.Items(TvSafeStore.Downloads.Where(kv => File.Exists(kv.Value)).Select(kv => kv.Key), tvAcc); }

    // ---------- the item menu's rows for all this ----------
    void TvSafeActionRows(TvItem it, List<ListRow> rows) {
        bool live = it.Kind == "live";
        if (live) {
            var r = TvRecording(it);
            rows.Add(new ListRow { Glyph = "", C1 = "#c0453c", C2 = "#e05b4f", Title = r != null ? "Stop recording" : "Record now", Text = r != null ? "Recording since it started; it stops by itself at " + r.StopAt.ToString("HH:mm") : "Up to 3 hours, into " + TvSafeStore.Recordings,
                Do = () => { if (r != null) TvRecordStop(r); else TvRecordStart(it, null, DateTime.Now.AddHours(3)); CloseListPage(); } });
        } else if (it.Kind == "file" && it.Id != null && it.Id.StartsWith("rec:")) {
            rows.Add(new ListRow { Glyph = "", C1 = "#c0453c", C2 = "#c0453c", Title = "Delete this recording", Text = "Goes to the Recycle Bin",
                Do = () => { try { Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(it.Url, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); } catch { } CloseListPage(); TvShowPage(tvPage); } });
        } else if (it.Kind == "movie" || it.Kind == "episode") {
            string have = TvDownloaded(it); bool busy = tvDlItem != null && tvDlItem.Key == it.Key, queued = tvDlQueue.Any(x => x.Key == it.Key);
            rows.Add(new ListRow { Glyph = have != null ? "" : busy ? "" : "", C1 = "#2f8fe0", C2 = "#2f8fe0",
                Title = have != null ? "Delete the download" : busy ? "Stop downloading (" + (int)(tvDlFrac * 100) + "%)" : queued ? "Waiting to download" : "Download", Text = have != null ? "It's saved on this PC; the file goes to the Recycle Bin" : busy || queued ? null : "Watch it without internet; into " + TvSafeStore.DownloadDir,
                Do = () => { if (have != null) TvDeleteDownload(it); else if (busy) tvDlCancel = true; else if (queued) tvDlQueue.RemoveAll(x => x.Key == it.Key); else TvDownload(it); CloseListPage(); } });
        }
        rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Report a problem", Text = "Copies a short report (without your login) to paste where you ask for help", Do = () => { TvReport(it); CloseListPage(); } });
    }
    // the guide: record a programme
    void TvSafeProgRows(TvItem ch, TvFree.Prog pr, List<ListRow> rows) {
        var now = DateTime.Now; if (pr.Title == null || pr.Stop <= now) return;
        if (pr.Start <= now) {
            var r = TvRecording(ch);
            rows.Add(new ListRow { Glyph = "", C1 = "#c0453c", C2 = "#e05b4f", Title = r != null ? "Stop recording" : "Record it (from now until " + pr.Stop.ToString("HH:mm") + ")",
                Do = () => { if (r != null) TvRecordStop(r); else TvRecordStart(ch, pr.Title, pr.Stop.AddMinutes(2)); CloseListPage(); } });
        } else {
            bool has = TvScheduled(ch, pr.Start);
            rows.Add(new ListRow { Glyph = "", C1 = "#c0453c", C2 = "#e05b4f", Title = has ? "Don't record it" : "Record it", Text = has ? null : "Starts by itself at " + pr.Start.ToString("HH:mm") + " and stops after " + pr.Stop.ToString("HH:mm") + " (the launcher needs to be open)",
                Do = () => { TvToggleSchedule(ch, pr); CloseListPage(); TvAfterActions(); } });
        }
    }

    // ---------- a problem report ----------
    void TvReport(TvItem it) {
        string host = ""; try { host = new Uri(TvUrl(it) ?? "").Host; } catch { }
        var sb = new StringBuilder();
        sb.AppendLine("Recomp Launcher TV & Movies problem report");
        sb.AppendLine("When: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        sb.AppendLine("What: " + it.Title + " (" + it.Kind + (it.Group != null ? ", " + it.Group : "") + ")");
        sb.AppendLine("Profile: " + (tvAcc == null ? "free" : (tvAcc.Type == "xtream" ? "IPTV login" : "playlist link")));
        sb.AppendLine("Server: " + (string.IsNullOrEmpty(host) ? "-" : host));
        sb.AppendLine("Player: " + (Mpv.Ready ? "mpv" : "not fetched yet") + (Vlc() != null ? ", VLC installed" : ""));
        sb.AppendLine("Last note: " + (tvLastNote ?? "-"));
        try { Clipboard.SetText(sb.ToString()); SetStep("TV: a problem report is copied; paste it (Ctrl+V) where you ask for help. It has no login or password in it.", Ui.Teal); }
        catch { SetStep("TV: the report couldn't be copied.", Ui.Red); }
    }
    string tvLastNote;
    // a stream that didn't start: what the provider says about the login, when it's a provider's
    void TvExplainFailure(TvItem it) {
        string msg = "TV: " + it.Title + " isn't playing right now (the stream may be offline, or only for some countries).";
        tvLastNote = msg;
        if (it.Account == null || it.Account.Type != "xtream") { SetStep(msg, Ui.Red); return; }
        SetStep("TV: " + it.Title + " didn't start; asking your provider why...", Ui.Red);
        var a = it.Account;
        new Thread(() => {
            string why; bool ok = TvSource.SignIn(a, out why);
            W.Dispatcher.BeginInvoke(new Action(() => {
                string m = !ok ? "TV: " + it.Title + " didn't start: " + why + " Check your subscription with your provider."
                    : Regex.IsMatch(why ?? "", @"screens") ? "TV: " + it.Title + " didn't start. Your login works (" + why + "); maybe another device is watching on it, or this channel is offline right now."
                    : "TV: " + it.Title + " didn't start. Your login works; this channel or film may be offline at your provider right now. Try again later, or Report a problem in its menu.";
                tvLastNote = m; SetStep(m, Ui.Red);
            }));
        }) { IsBackground = true }.Start();
    }

    // ---------- phone pairing (in the add-profile wizard) ----------
    TcpListener tvPair; string tvPairCode; Border tvPairCard;
    bool TvPairOpen { get { return tvPairCard != null && tvPairCard.Visibility == Visibility.Visible; } }
    const int PairPort = 8771;
    void TvPairStart() { tvPairMode = "wizard"; TvPairOpenCard(); }
    // the QR code card; tvPairMode says what the phone's page is for: "wizard" (a login) or "search" (TvExtras.cs)
    void TvPairOpenCard() {
        bool search = tvPairMode == "search";
        var ips = Dns.GetHostAddresses(Dns.GetHostName()).Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)).Select(a => a.ToString())
            .OrderBy(a => a.StartsWith("192.168.") ? 0 : a.StartsWith("10.") ? 1 : 2).ToList();
        if (ips.Count == 0) { SetStep("TV: this PC isn't on a network, so the phone can't reach it.", Ui.Red); return; }
        var b = new byte[4]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b);
        tvPairCode = (BitConverter.ToUInt32(b, 0) % 1000000).ToString("000000");
        try { TvPairStop(); tvPair = new TcpListener(IPAddress.Any, PairPort); tvPair.Start(); var l = tvPair; new Thread(() => TvPairLoop(l)) { IsBackground = true, Name = "TV pairing" }.Start(); }
        catch (Exception e) { SetStep("TV: pairing couldn't start: " + e.Message, Ui.Red); return; }
        string url = "http://" + ips[0] + ":" + PairPort + "/?c=" + tvPairCode;
        if (ListPageOpen) CloseListPage();
        // the card: the QR code, the address, the code
        var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        col.Children.Add(new TextBlock { Text = search ? "Search on your phone" : "Type it on your phone", FontSize = 28, FontFamily = TvFont, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
        col.Children.Add(new TextBlock { Text = search ? "Scan this with your phone's camera (the phone on the same Wi-Fi as this PC) and type what you're looking for: the results show here as you type." : "Scan this with your phone's camera (the phone on the same Wi-Fi as this PC). Fill in what your provider gave you and press Send; it comes here by itself.", FontSize = 15, Foreground = TvTheme.Brush("#B3FFFFFF"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 460, Margin = new Thickness(0, 10, 0, 18) });
        col.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(14), Padding = new Thickness(14), HorizontalAlignment = HorizontalAlignment.Center, Child = new Image { Source = QrCode.Picture(url), Width = 230, Height = 230 } });
        col.Children.Add(new TextBlock { Text = "Or open  " + "http://" + ips[0] + ":" + PairPort + "  and type the code  " + tvPairCode, FontSize = 14.5, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#D9FFFFFF"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 4) });
        col.Children.Add(new TextBlock { Text = "Windows may ask once whether the launcher may use your network: allow it for private networks.   B / Esc: back", FontSize = 12.5, Foreground = TvTheme.Brush("#80FFFFFF"), HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, TextAlignment = TextAlignment.Center });
        if (tvPairCard == null) {
            tvPairCard = new Border { Background = TvTheme.Brush("#F2101012"), Visibility = Visibility.Collapsed };
            Grid.SetColumnSpan(tvPairCard, 2); Panel.SetZIndex(tvPairCard, 960); tvLayer.Children.Add(tvPairCard);
            tvPairCard.MouseLeftButtonUp += (s, e) => { if (e.OriginalSource == tvPairCard) TvPairCancel(); };
        }
        tvPairCard.Child = new Border { Child = col, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(40), CornerRadius = new CornerRadius(20), Background = TvTheme.Brush("#1C1C1E") };
        tvPairCard.Visibility = Visibility.Visible; Sound.Play(Sfx.Open);
        SetHints();
    }
    void TvPairStop() { if (tvPair != null) { try { tvPair.Stop(); } catch { } tvPair = null; } }
    void TvPairCancel() { TvPairStop(); if (tvPairCard != null) tvPairCard.Visibility = Visibility.Collapsed; Sound.Play(Sfx.Back); if (tvPairMode == "wizard" && tvWizPage != null) OpenListPage(tvWizPage); SetHints(); }
    void TvPairLoop(TcpListener l) {
        while (true) {
            TcpClient c; try { c = l.AcceptTcpClient(); } catch { return; }
            ThreadPool.QueueUserWorkItem(_ => { try { TvPairServe(c); } catch { } finally { try { c.Close(); } catch { } } });
        }
    }
    void TvPairServe(TcpClient c) {
        c.ReceiveTimeout = 8000; var s = c.GetStream();
        var head = new StringBuilder(); var one = new byte[1];
        while (head.Length < 8192) { if (s.Read(one, 0, 1) <= 0) return; head.Append((char)one[0]); if (head.Length >= 4 && head.ToString(head.Length - 4, 4) == "\r\n\r\n") break; }
        var lines = head.ToString().Split('\n'); var first = lines[0].Trim().Split(' '); if (first.Length < 2) return;
        string method = first[0], path = first[1].Split('?')[0];
        if (method == "GET") { Reply(s, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(tvPairMode == "search" ? TvPairSearchPage : TvPairPage)); return; }
        if (method != "POST" || path != "/send") { Reply(s, 404, "text/plain", new byte[0]); return; }
        int len = 0; foreach (var h in lines) { var m = Regex.Match(h, @"^Content-Length:\s*(\d+)", RegexOptions.IgnoreCase); if (m.Success) len = Math.Min(16384, int.Parse(m.Groups[1].Value)); }
        var body = new byte[len]; int got = 0; while (got < len) { int n = s.Read(body, got, len - got); if (n <= 0) break; got += n; }
        var d = Json.Parse(Encoding.UTF8.GetString(body, 0, got)) as Dictionary<string, object>;
        if (d == null || Json.Str(d, "code") != tvPairCode) { Reply(s, 401, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("That code isn't right. Check the code on the TV.")); return; }
        // search: what's typed goes to the search page, letter by letter (TvExtras.cs)
        if (tvPairMode == "search") { string q = Json.Str(d, "q") ?? ""; W.Dispatcher.BeginInvoke(new Action(() => TvPairFromPhone(q))); Reply(s, 200, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("On the TV.")); return; }
        W.Dispatcher.BeginInvoke(new Action(() => {
            var a = tvWiz; if (a == null) return;
            string link = (Json.Str(d, "link") ?? "").Trim();
            if (link.Length > 0) { a.Type = "m3u"; a.Url = link; TvWizLinkCheck(); }
            else { a.Type = "xtream"; a.Server = TvCleanServer(Json.Str(d, "server")); a.User = (Json.Str(d, "user") ?? "").Trim(); a.Pass = Json.Str(d, "pass") ?? ""; }
            TvPairStop(); tvPairCard.Visibility = Visibility.Collapsed; Sound.Play(Sfx.Select);
            OpenListPage(tvWizPage); tvWizStep = 3; TvFillWizard(); TvWizSignIn();
        }));
        Reply(s, 200, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Sent! Look at the TV."));
    }
    const string TvPairPage = @"<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>Add your IPTV</title>
<style>body{margin:0;font-family:system-ui,-apple-system,Segoe UI,sans-serif;background:#0b0b0d;color:#fff}main{max-width:460px;margin:0 auto;padding:24px 18px}h1{font-size:24px;margin:6px 0 4px}p{color:#aaa;font-size:14px;line-height:1.45}
.tabs{display:flex;gap:8px;margin:18px 0}.tabs button{flex:1;padding:12px;border-radius:22px;border:1px solid #333;background:#1c1c1e;color:#bbb;font-size:15px;font-weight:600}.tabs button.on{background:#fff;color:#000}
label{display:block;font-size:13px;color:#aaa;margin:14px 0 6px}input{width:100%;box-sizing:border-box;padding:14px;border-radius:12px;border:1px solid #333;background:#16161a;color:#fff;font-size:16px}
.send{width:100%;margin-top:22px;padding:15px;border:0;border-radius:24px;background:#ff3d1f;color:#fff;font-size:17px;font-weight:700}#msg{margin-top:16px;text-align:center;font-weight:600}</style></head>
<body><main><h1>Add your IPTV</h1><p>Fill in what your provider gave you. It goes straight to the TV in your house and nowhere else.</p>
<div class='tabs'><button id='t1' class='on' onclick='tab(1)'>A login</button><button id='t2' onclick='tab(2)'>One long link</button></div>
<div id='f1'><label>Server address (like http://example-tv.com:8080)</label><input id='server' autocapitalize='off' autocorrect='off' inputmode='url'>
<label>Username</label><input id='user' autocapitalize='off' autocorrect='off'><label>Password</label><input id='pass' type='password'></div>
<div id='f2' style='display:none'><label>Playlist link (M3U)</label><input id='link' autocapitalize='off' autocorrect='off' inputmode='url'></div>
<div id='cbox'><label>The code on the TV</label><input id='code' inputmode='numeric' maxlength='6'></div>
<button class='send' onclick='send()'>Send to the TV</button><div id='msg'></div></main>
<script>var mode=1;var q=new URLSearchParams(location.search);if(q.get('c')){document.getElementById('code').value=q.get('c');document.getElementById('cbox').style.display='none';}
function tab(n){mode=n;document.getElementById('f1').style.display=n==1?'':'none';document.getElementById('f2').style.display=n==2?'':'none';document.getElementById('t1').className=n==1?'on':'';document.getElementById('t2').className=n==2?'on':'';}
function v(id){return document.getElementById(id).value.trim();}
function send(){var b={code:v('code')};if(mode==1){b.server=v('server');b.user=v('user');b.pass=document.getElementById('pass').value;if(!b.server||!b.user||!b.pass){msg('Fill in all three.');return;}}else{b.link=v('link');if(!b.link){msg('Paste the link first.');return;}}
fetch('/send',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(b)}).then(function(r){return r.text();}).then(msg).catch(function(){msg('The TV couldn\'t be reached. Is the phone on the same Wi-Fi?');});}
function msg(t){document.getElementById('msg').textContent=t;}</script></body></html>";
}
}
