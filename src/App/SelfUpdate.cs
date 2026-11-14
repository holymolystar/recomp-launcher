// The launcher's own updates: at start (and with Check in Settings > General) it asks GitHub for the newest release of
// holymolystar/recomp-launcher. When it's newer than this build, Home shows a "New launcher" pill and Settings shows
// its notes and Update now: the installer is downloaded, the launcher closes, the installer runs silently over the
// installed copy (settings, profiles, saves and play time are kept), and the launcher opens again.
// A copy not installed by the installer (one built from the source) only links to the release page.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RecompLauncher {

static class LauncherVersion {
    public const string Current = "2.4.0";
    public const string Repo = "holymolystar/recomp-launcher";
}

partial class Shell {
    Release launcherLatest; bool launcherChecking;
    TextBlock luStatus, luNotes; Button luCheck, luUpdate, luNotesBtn; Border luPill;
    static bool InstalledCopy { get { return File.Exists(Path.Combine(Paths.Root, "unins000.exe")); } }
    bool LauncherNewer { get { var a = Versions.Parse(launcherLatest != null ? launcherLatest.Tag : null); var b = Versions.Parse(LauncherVersion.Current); return a != null && b != null && a > b; } }

    FrameworkElement LauncherUpdateRow() {
        var row = new Grid { Margin = new Thickness(0, 14, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(new TextBlock { Text = "Recomp Launcher " + LauncherVersion.Current, FontSize = 13, FontFamily = Ui.Semi });
        luStatus = new TextBlock { FontSize = 11.5, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap };
        text.Children.Add(luStatus);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        luNotesBtn = MakeButton("What's new", 32); luNotesBtn.FontSize = 12; luNotesBtn.Margin = new Thickness(6, 0, 0, 0);
        luNotesBtn.Click += (s, e) => { luNotes.Visibility = luNotes.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; };
        luCheck = MakeButton("Check", 32); luCheck.FontSize = 12; luCheck.Margin = new Thickness(6, 0, 0, 0);
        luCheck.Click += (s, e) => CheckLauncher(true);
        luUpdate = MakeButton("Update now", 32); luUpdate.FontSize = 12; luUpdate.Margin = new Thickness(6, 0, 0, 0); luUpdate.Background = Ui.Brush(Ui.Gold); luUpdate.Foreground = Ui.Brush("#0A0E11");
        luUpdate.Click += (s, e) => UpdateLauncher();
        bar.Children.Add(luNotesBtn); bar.Children.Add(luCheck); bar.Children.Add(luUpdate);
        Grid.SetColumn(bar, 1);
        row.Children.Add(text); row.Children.Add(bar);
        luNotes = new TextBlock { FontSize = 12, Foreground = Ui.Brush(Ui.Text), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
        Grid.SetRow(luNotes, 1); Grid.SetColumnSpan(luNotes, 2);
        row.RowDefinitions.Add(new RowDefinition()); row.RowDefinitions.Add(new RowDefinition());
        row.Children.Add(luNotes);
        ShowLauncherUpdate();
        return row;
    }
    void ShowLauncherUpdate() {
        if (luStatus == null) return;
        bool newer = LauncherNewer;
        luStatus.Text = launcherChecking ? "Checking for a newer launcher..." : launcherLatest == null ? "Checks for a newer launcher when it starts." :
            newer ? launcherLatest.Tag + " is out." + (InstalledCopy ? " Update now installs it and opens the launcher again; your settings, profiles and saves are kept." : " This copy wasn't installed with the installer: get it from the release page.")
                  : "This is the newest version.";
        luStatus.Foreground = Ui.Brush(newer ? Ui.Gold : Ui.Muted);
        luUpdate.Visibility = newer ? Visibility.Visible : Visibility.Collapsed;
        luUpdate.Content = InstalledCopy ? "Update now" : "Release page";
        luNotesBtn.Visibility = newer && !string.IsNullOrEmpty(launcherLatest.Notes) ? Visibility.Visible : Visibility.Collapsed;
        if (launcherLatest != null) luNotes.Text = PlainNotes(launcherLatest.Notes);
        // Home: a pill by the clock while a newer launcher is out
        if (luPill == null && homeUpdatesPill != null) {
            luPill = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(11, 3, 11, 4), Background = Ui.Brush(Ui.Teal), Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center, ToolTip = "A newer Recomp Launcher is out", Visibility = Visibility.Collapsed,
                Child = new TextBlock { Text = "New launcher", FontSize = 12.5, FontFamily = Ui.Semi, Foreground = Ui.Brush("#0A0E11") } };
            luPill.MouseLeftButtonUp += (s, e) => OpenSettings("General");
            ((Panel)homeUpdatesPill.Parent).Children.Insert(0, luPill);
        }
        if (luPill != null) luPill.Visibility = newer ? Visibility.Visible : Visibility.Collapsed;
    }
    static string PlainNotes(string md) {
        var lines = (md ?? "").Replace("\r", "").Split('\n').Select(l => Regex.Replace(l, @"^#+\s*", "").Replace("**", "").Replace("`", ""))
            .Select(l => Regex.Replace(l, @"^\s*[-*]\s+", "•  ")).Where(l => l.Trim().Length > 0);
        return string.Join("\n", lines.ToArray());
    }
    void CheckLauncher(bool loud) {
        if (launcherChecking) return;
        launcherChecking = true; ShowLauncherUpdate();
        Task.Factory.StartNew(() => {
            try {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                using (var wc = new WebClient()) {
                    wc.Headers[HttpRequestHeader.UserAgent] = "RecompLauncher/" + LauncherVersion.Current;
                    wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    string tok = Settings.Get("GitHubToken");
                    if (!string.IsNullOrEmpty(tok)) wc.Headers[HttpRequestHeader.Authorization] = "Bearer " + tok;
                    var d = Json.Parse(wc.DownloadString("https://api.github.com/repos/" + LauncherVersion.Repo + "/releases/latest")) as System.Collections.Generic.Dictionary<string, object>;
                    var r = new Release { Tag = Json.Str(d, "tag_name"), Notes = Json.Str(d, "body"), Date = Json.Str(d, "published_at"), Url = Json.Str(d, "html_url") };
                    var assets = d != null && d.ContainsKey("assets") ? ((System.Collections.IEnumerable)d["assets"]).OfType<System.Collections.Generic.Dictionary<string, object>>() : Enumerable.Empty<System.Collections.Generic.Dictionary<string, object>>();
                    var setup = assets.FirstOrDefault(a => Regex.IsMatch(Json.Str(a, "name") ?? "", @"^RecompLauncher-Setup-.*\.exe$", RegexOptions.IgnoreCase));
                    if (setup != null) { r.Sha = Json.Str(setup, "browser_download_url"); r.Size = Json.Num(setup, "size"); }
                    return r;
                }
            } catch { return null; }
        }).ContinueWith(t => W.Dispatcher.BeginInvoke(new Action(() => {
            launcherChecking = false;
            if (t.Result != null) launcherLatest = t.Result;
            ShowLauncherUpdate();
            if (LauncherNewer) { Log("Recomp Launcher " + launcherLatest.Tag + " is out (this is " + LauncherVersion.Current + ").", Ui.Gold); Notify("A new Recomp Launcher is out", launcherLatest.Tag + ": open the launcher to update."); }
            else if (loud && t.Result == null) luStatus.Text = "Couldn't reach GitHub to check.";
        })));
    }
    // downloads the installer, then closes; a small hidden helper runs the installer and opens the launcher again
    void UpdateLauncher() {
        if (!LauncherNewer) return;
        if (!InstalledCopy || launcherLatest.Sha == null) { try { Process.Start(launcherLatest.Url ?? "https://github.com/" + LauncherVersion.Repo + "/releases"); } catch { } return; }
        if (running) { luStatus.Text = "Wait for the game updates running now to finish first."; return; }
        string file = Path.Combine(Path.GetTempPath(), "RecompLauncher-Setup-" + launcherLatest.Tag.TrimStart('v') + ".exe");
        luUpdate.IsEnabled = false; luStatus.Text = "Downloading " + launcherLatest.Tag + "...";
        var wc = new WebClient();
        wc.Headers[HttpRequestHeader.UserAgent] = "RecompLauncher/" + LauncherVersion.Current;
        wc.DownloadProgressChanged += (s, e) => luStatus.Text = "Downloading " + launcherLatest.Tag + "... " + e.ProgressPercentage + "%";
        wc.DownloadFileCompleted += (s, e) => {
            wc.Dispose();
            if (e.Error != null || !File.Exists(file) || (launcherLatest.Size > 0 && new FileInfo(file).Length != (long)launcherLatest.Size)) {
                luUpdate.IsEnabled = true; luStatus.Text = "The download didn't finish: try again."; return;
            }
            string exe = Path.Combine(Paths.Root, "Recomp Launcher.exe");
            string script = "Start-Sleep -Seconds 2; Start-Process -Wait -FilePath '" + file.Replace("'", "''") + "' -ArgumentList '/SILENT','/SUPPRESSMSGBOXES','/NORESTART','/CLOSEAPPLICATIONS'; Start-Process -FilePath '" + exe.Replace("'", "''") + "'";
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"" + script.Replace("\"", "\\\"") + "\"") {
                UseShellExecute = false, CreateNoWindow = true };
            try { Process.Start(psi); } catch (Exception ex) { luUpdate.IsEnabled = true; luStatus.Text = "Couldn't start the installer: " + ex.Message; return; }
            Settings.Save();
            W.Close();
        };
        wc.DownloadFileAsync(new Uri(launcherLatest.Sha), file);
    }
}
}
