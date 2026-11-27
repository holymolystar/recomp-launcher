// Music in the in-game menu: the song Spotify is playing (Spotify shows "Artist - Song" as its window title while it
// plays) and Previous / Play-Pause / Next, sent as the keyboard's media keys, so no Spotify login is needed and other
// music apps that answer the media keys work too (their song name only shows for Spotify).
// In the menu: LB previous, X play / pause, RB next (or click the buttons).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace RecompLauncher {

static class Music {
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, int flags, IntPtr extra);
    const byte Next = 0xB0, Prev = 0xB1, PlayPause = 0xB3;
    public static void Press(byte vk) { keybd_event(vk, 0, 1, IntPtr.Zero); keybd_event(vk, 0, 1 | 2, IntPtr.Zero); }
    public static void NextSong() { Press(Next); }
    public static void PrevSong() { Press(Prev); }
    public static void Toggle() { Press(PlayPause); }
    static string lastSong, lastArtist;
    // Spotify's song now: false when Spotify isn't running; paused while it shows its own name instead of a song
    public static bool Spotify(out string song, out string artist, out bool paused) {
        song = null; artist = null; paused = false;
        try {
            var w = Process.GetProcessesByName("Spotify").Select(p => { try { return p.MainWindowTitle; } catch { return ""; } }).FirstOrDefault(t => !string.IsNullOrEmpty(t));
            if (w == null) return false;
            int dash = w.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0 && !w.StartsWith("Spotify", StringComparison.OrdinalIgnoreCase)) {
                lastArtist = artist = w.Substring(0, dash); lastSong = song = w.Substring(dash + 3);
            } else { paused = true; song = lastSong; artist = lastArtist; }
            return true;
        } catch { return false; }
    }
    // the song playing now in any music app this knows (Spotify first): false when none of them is open. Each shows
    // the song in its window title ("Artist - Song" before its own name); when it shows only its name, it's stopped.
    static readonly string[][] Others = {
        // process, what follows the song in its title, the app's name
        new[] { "vlc", " - VLC media player", "VLC" }, new[] { "foobar2000", "[foobar2000]", "foobar2000" }, new[] { "MusicBee", " - MusicBee", "MusicBee" },
        new[] { "AIMP", "", "AIMP" }, new[] { "iTunes", "", "iTunes" }, new[] { "AppleMusic", "", "Apple Music" }, new[] { "Deezer", "", "Deezer" }, new[] { "TIDAL", "", "TIDAL" } };
    public static bool NowPlaying(out string song, out string artist, out string app, out bool paused) {
        app = null;
        if (Spotify(out song, out artist, out paused)) { app = "Spotify"; return true; }
        foreach (var o in Others) {
            string t;
            try { t = Process.GetProcessesByName(o[0]).Select(p => { try { return p.MainWindowTitle; } catch { return ""; } }).FirstOrDefault(x => !string.IsNullOrEmpty(x)); } catch { t = null; }
            if (t == null) continue;
            app = o[2]; song = null; artist = null; paused = false;
            if (o[1].Length == 0 || !t.TrimEnd().EndsWith(o[1].Trim(), StringComparison.OrdinalIgnoreCase)) return true;   // open, but no song in its title
            t = t.TrimEnd(); t = t.Substring(0, t.Length - o[1].Trim().Length).TrimEnd(' ', '-');
            int dash = t.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0) { artist = t.Substring(0, dash); song = t.Substring(dash + 3); } else song = t;
            return true;
        }
        return false;
    }
    // the music apps on this PC, with how to open each: [name, program or link]
    public static List<string[]> Players() {
        var l = new List<string[]>();
        Func<string, string> ex = p => Environment.ExpandEnvironmentVariables(p);
        try {
            if (File.Exists(ex(@"%APPDATA%\Spotify\Spotify.exe"))) l.Add(new[] { "Spotify", ex(@"%APPDATA%\Spotify\Spotify.exe") });
            else if (Microsoft.Win32.Registry.ClassesRoot.OpenSubKey("spotify") != null) l.Add(new[] { "Spotify", "spotify:" });
        } catch { }
        var known = new[] {
            new[] { "VLC", @"%ProgramFiles%\VideoLAN\VLC\vlc.exe", @"%ProgramFiles(x86)%\VideoLAN\VLC\vlc.exe" },
            new[] { "foobar2000", @"%ProgramFiles%\foobar2000\foobar2000.exe", @"%ProgramFiles(x86)%\foobar2000\foobar2000.exe" },
            new[] { "MusicBee", @"%ProgramFiles(x86)%\MusicBee\MusicBee.exe", @"%ProgramFiles%\MusicBee\MusicBee.exe" },
            new[] { "AIMP", @"%ProgramFiles%\AIMP\AIMP.exe", @"%ProgramFiles(x86)%\AIMP\AIMP.exe" },
            new[] { "iTunes", @"%ProgramFiles%\iTunes\iTunes.exe", @"%ProgramFiles(x86)%\iTunes\iTunes.exe" },
            new[] { "Deezer", @"%LOCALAPPDATA%\Programs\deezer-desktop\Deezer.exe", "" },
            new[] { "TIDAL", @"%LOCALAPPDATA%\TIDAL\TIDAL.exe", "" } };
        foreach (var k in known) {
            string hit = k.Skip(1).Where(p => p.Length > 0).Select(ex).FirstOrDefault(File.Exists);
            if (hit != null) l.Add(new[] { k[0], hit });
        }
        return l;
    }
    public static void Open(string what) {
        try { Process.Start(new ProcessStartInfo(what) { UseShellExecute = true }); } catch { }
    }
}

partial class Shell {
    Border ovMusic; TextBlock ovSong, ovArtist, ovPlayGlyph; StackPanel ovMusicHints;
    // built into the in-game menu, above its hints
    FrameworkElement BuildOverlayMusic() {
        var host = new StackPanel();
        ovMusic = new Border { CornerRadius = new CornerRadius(14), Background = Ui.Brush("#1C252C"), Padding = new Thickness(14, 12, 12, 12), Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 20, Foreground = Ui.Brush("#1ed760"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        ovSong = new TextBlock { FontSize = 14, FontFamily = Ui.Semi, TextTrimming = TextTrimming.CharacterEllipsis };
        ovArtist = new TextBlock { FontSize = 12, Foreground = Ui.Brush(Ui.Muted), TextTrimming = TextTrimming.CharacterEllipsis };
        text.Children.Add(ovSong); text.Children.Add(ovArtist);
        Grid.SetColumn(text, 1); g.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        Func<string, Action, TextBlock> btn = (glyph, act) => {
            var t = new TextBlock { Text = glyph, FontFamily = Ui.Mdl, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = Ui.Brush("#26333C"), Margin = new Thickness(4, 0, 0, 0), Child = t, Cursor = Cursors.Hand };
            b.MouseLeftButtonUp += (s, e) => act();
            buttons.Children.Add(b);
            return t;
        };
        btn("", () => MusicDo(Music.PrevSong));
        ovPlayGlyph = btn("", () => MusicDo(Music.Toggle));
        btn("", () => MusicDo(Music.NextSong));
        Grid.SetColumn(buttons, 2); g.Children.Add(buttons);
        ovMusic.Child = g;
        host.Children.Add(ovMusic);
        ovMusicHints = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-14, 8, 0, 0), Visibility = Visibility.Collapsed };
        ovMusicHints.Children.Add(Hint("LB", "Previous")); ovMusicHints.Children.Add(Hint("X", "Play / pause")); ovMusicHints.Children.Add(Hint("RB", "Next"));
        host.Children.Add(ovMusicHints);
        return host;
    }
    void MusicDo(Action a) {
        a(); Sound.Play(Sfx.Select);
        // Spotify changes its title a moment later
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        t.Tick += (s, e) => { t.Stop(); UpdateOverlayMusic(); };
        t.Start();
    }
    void UpdateOverlayMusic() {
        if (ovMusic == null) return;
        string song, artist; bool paused;
        bool on = Music.Spotify(out song, out artist, out paused);
        ovMusic.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ovMusicHints.Visibility = ovMusic.Visibility;
        if (!on) return;
        ovSong.Text = song ?? (paused ? "Spotify: paused" : "Spotify");
        ovArtist.Text = song != null ? (artist ?? "") + (paused ? "  •  paused" : "") : "Press X to play";
        ovPlayGlyph.Text = paused ? "" : "";
    }
    // LB / X / RB in the menu; true when it was a music button
    bool OverlayMusicPad(int bit) {
        if (ovMusic == null || ovMusic.Visibility != Visibility.Visible) return false;
        if (bit == Pad.LB) { MusicDo(Music.PrevSong); return true; }
        if (bit == Pad.RB) { MusicDo(Music.NextSong); return true; }
        if (bit == Pad.X) { MusicDo(Music.Toggle); return true; }
        return false;
    }
}
}
