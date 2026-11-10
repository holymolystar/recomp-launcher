// Music in the in-game menu: the song Spotify is playing (Spotify shows "Artist - Song" as its window title while it
// plays) and Previous / Play-Pause / Next, sent as the keyboard's media keys, so no Spotify login is needed and other
// music apps that answer the media keys work too (their song name only shows for Spotify).
// In the menu: LB previous, X play / pause, RB next (or click the buttons).
using System;
using System.Diagnostics;
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
