// The Album: "Take a screenshot" in the in-game menu saves a picture of the game's screen (the menu steps aside for
// it) to Pictures\Recomp Launcher\<game>\. Album on Home shows them, newest first, game by game (LB / RB or the
// pills); A or a click shows one large, left / right go through them. The phone remote has them too (Remote.cs).
// A game in exclusive full screen is minimised while the menu is open, so its screenshot is of the desktop.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace RecompLauncher {

static class Album {
    public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Recomp Launcher"); } }
    // every screenshot, newest first: (file, game's folder name)
    public static List<Tuple<string, string>> All() {
        var r = new List<Tuple<string, string>>();
        try {
            if (!Directory.Exists(Dir)) return r;
            foreach (var d in Directory.GetDirectories(Dir))
                foreach (var f in Directory.GetFiles(d, "*.png")) r.Add(Tuple.Create(f, Path.GetFileName(d)));
        } catch { }
        return r.OrderByDescending(x => File.GetLastWriteTimeUtc(x.Item1)).ToList();
    }
    public static string Save(string game, System.Drawing.Rectangle area) {
        string safe = new string((game ?? "Screenshots").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray()).Trim();
        string dir = Path.Combine(Dir, safe.Length > 0 ? safe : "Screenshots");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss") + ".png");
        using (var bmp = new System.Drawing.Bitmap(area.Width, area.Height))
        using (var g = System.Drawing.Graphics.FromImage(bmp)) {
            g.CopyFromScreen(area.Location, System.Drawing.Point.Empty, area.Size);
            bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
        }
        return file;
    }
    public static ImageSource Load(string file, int width) {
        try {
            var b = new BitmapImage(); b.BeginInit(); b.UriSource = new Uri(file); b.CacheOption = BitmapCacheOption.OnLoad; if (width > 0) b.DecodePixelWidth = width; b.EndInit(); b.Freeze();
            return b;
        } catch { return null; }
    }
}

partial class Shell {
    // ---------- taking one, from the in-game menu ----------
    void TakeScreenshot() {
        var screen = ovGameWindow != IntPtr.Zero ? System.Windows.Forms.Screen.FromHandle(ovGameWindow) : System.Windows.Forms.Screen.PrimaryScreen;
        string name = ovBrowser ? "Browser" : playing != null ? playing.Name : "Screenshots";
        ov.Hide();
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        t.Tick += (s, e) => {
            t.Stop();
            string err = null;
            try { Album.Save(name, screen.Bounds); } catch (Exception ex) { err = ex.Message; }
            ov.Show(); ov.Activate();
            Sound.Play(err == null ? Sfx.Select : Sfx.Bump);
            ovNote.Text = err == null ? "Saved to the Album (Album on Home)." : "The screenshot didn't save: " + err;
        };
        t.Start();
    }

    // ---------- the Album page ----------
    Grid alLayer; WrapPanel alGrid; StackPanel alPills; Border alViewer; Image alBig; TextBlock alBigInfo; string alGame; int alIdx; List<Tuple<string, string>> alShots = new List<Tuple<string, string>>();
    readonly List<Border> alTiles = new List<Border>();
    bool AlbumOpen { get { return alLayer != null && alLayer.Visibility == Visibility.Visible; } }
    void OpenAlbum() {
        if (alLayer == null) {
            Border card; StackPanel body;
            alLayer = Card("Al", 960, VerticalAlignment.Center, out card, out body);
            var head = new Grid();
            head.Children.Add(new TextBlock { Text = "ALBUM", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal) });
            var folder = MakeButton("Open folder", 34); folder.HorizontalAlignment = HorizontalAlignment.Right;
            folder.Click += (s, e) => { try { Directory.CreateDirectory(Album.Dir); System.Diagnostics.Process.Start(Album.Dir); } catch { } };
            head.Children.Add(folder);
            body.Children.Add(head);
            alPills = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 12) };
            body.Children.Add(new ScrollViewer { Content = alPills, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false });
            var area = new Grid { Height = 480 };
            alGrid = new WrapPanel();
            area.Children.Add(new ScrollViewer { Content = alGrid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
            alBig = new Image { Stretch = Stretch.Uniform };
            alBigInfo = new TextBlock { FontSize = 12.5, Foreground = Ui.Brush(Ui.Muted), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            var vs = new DockPanel();
            DockPanel.SetDock(alBigInfo, Dock.Bottom); vs.Children.Add(alBigInfo); vs.Children.Add(alBig);
            alViewer = new Border { Background = Ui.Brush("#05080A"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Child = vs, Visibility = Visibility.Collapsed };
            alViewer.MouseLeftButtonUp += (s, e) => { alViewer.Visibility = Visibility.Collapsed; SetHints(); };
            area.Children.Add(alViewer);
            body.Children.Add(area);
            ((Rectangle)alLayer.Children[0]).MouseLeftButtonUp += (s, e) => CloseAlbum();
        }
        alGame = null; alIdx = 0; alViewer.Visibility = Visibility.Collapsed;
        FillAlbum();
        ShowCard("AlLayer", "AlCard");
    }
    void CloseAlbum() { if (!AlbumOpen) return; Sound.Play(Sfx.Back); HideCard("AlLayer", "AlCard"); }
    void FillAlbum() {
        var all = Album.All();
        var games = all.Select(x => x.Item2).Distinct().ToList();
        alPills.Children.Clear();
        foreach (var name in new string[] { null }.Concat(games)) {
            string n = name; bool on = n == alGame;
            var pill = new Border { CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 5, 14, 6), Margin = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand,
                Background = Ui.Brush(on ? Ui.Teal : "#1C252C"),
                Child = new TextBlock { Text = (n ?? "All") + "  " + all.Count(x => n == null || x.Item2 == n), FontSize = 12.5, FontFamily = Ui.Semi, Foreground = Ui.Brush(on ? (Theme.Current.Light ? "#ffffff" : "#0A0E11") : Ui.Text) } };
            pill.MouseLeftButtonUp += (s, e) => { alGame = n; alIdx = 0; Sound.Play(Sfx.Move); FillAlbum(); };
            alPills.Children.Add(pill);
        }
        alShots = all.Where(x => alGame == null || x.Item2 == alGame).ToList();
        alGrid.Children.Clear(); alTiles.Clear();
        for (int i = 0; i < alShots.Count; i++) {
            int idx = i;
            var tile = new Border { Width = 214, Height = 121, Margin = new Thickness(0, 0, 12, 12), CornerRadius = new CornerRadius(6), ClipToBounds = true, Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(3), BorderBrush = Brushes.Transparent, Background = Ui.Brush("#05080A"),
                Child = new Image { Source = Album.Load(alShots[i].Item1, 240), Stretch = Stretch.UniformToFill } };
            tile.MouseLeftButtonUp += (s, e) => { alIdx = idx; MarkAlbum(); ShowShot(); };
            alTiles.Add(tile); alGrid.Children.Add(tile);
        }
        if (alShots.Count == 0) alGrid.Children.Add(new TextBlock { Text = "No screenshots yet. While playing, open the in-game menu (the Xbox / PS / Home button) and pick Take a screenshot.", FontSize = 14, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap, Width = 860 });
        MarkAlbum();
    }
    void MarkAlbum() {
        for (int i = 0; i < alTiles.Count; i++) alTiles[i].BorderBrush = i == alIdx ? Ui.Brush(Ui.Teal) : Brushes.Transparent;
        if (alIdx >= 0 && alIdx < alTiles.Count) alTiles[alIdx].BringIntoView();
    }
    void ShowShot() {
        if (alIdx < 0 || alIdx >= alShots.Count) return;
        var f = alShots[alIdx];
        alBig.Source = Album.Load(f.Item1, 1600);
        alBigInfo.Text = f.Item2 + "  •  " + File.GetLastWriteTime(f.Item1).ToString("d MMMM yyyy, HH:mm") + "  •  " + (alIdx + 1) + " of " + alShots.Count;
        alViewer.Visibility = Visibility.Visible;
        Sound.Play(Sfx.Select); SetHints();
    }
    void AlbumPad(int bit) {
        bool big = alViewer.Visibility == Visibility.Visible;
        if (bit == Pad.B) { if (big) { alViewer.Visibility = Visibility.Collapsed; Sound.Play(Sfx.Back); SetHints(); } else CloseAlbum(); return; }
        if (bit == Pad.A) { if (!big) ShowShot(); return; }
        if (bit == Pad.LB || bit == Pad.RB) {
            var games = new string[] { null }.Concat(alShots.Count >= 0 ? Album.All().Select(x => x.Item2).Distinct() : new string[0]).ToList();
            int i = games.IndexOf(alGame);
            alGame = games[(i + (bit == Pad.RB ? 1 : -1) + games.Count) % games.Count]; alIdx = 0;
            alViewer.Visibility = Visibility.Collapsed; Sound.Play(Sfx.Move); FillAlbum(); return;
        }
        int was = alIdx, per = 4;
        if (bit == Pad.Left) alIdx = Math.Max(0, alIdx - 1);
        if (bit == Pad.Right) alIdx = Math.Min(alShots.Count - 1, alIdx + 1);
        if (!big && bit == Pad.Up) alIdx = Math.Max(0, alIdx - per);
        if (!big && bit == Pad.Down) alIdx = Math.Min(alShots.Count - 1, alIdx + per);
        if (alIdx != was) { Sound.Play(Sfx.Move); MarkAlbum(); if (big) ShowShot(); }
    }
}
}
