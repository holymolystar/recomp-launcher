// The console each game was first made for (games.json "Platform"; store games are PC): a console filter in All games,
// and, as an option (Settings > General > Home folders), Home's row in folders, one per console (GBA, NDS, Switch...),
// sorted by themselves. The favourites stay on their own at the start of the row, before the folders. A folder is a
// tile like the launcher's pages (Apps.cs): A opens it, and the row then holds that console's games, after a Back tile.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace RecompLauncher {

partial class Shell {
    // newest first, the handhelds after the home consoles of each maker
    static readonly string[] PlatformOrder = { "Switch 2", "Switch", "Wii U", "Wii", "GameCube", "N64", "SNES", "NES", "3DS", "NDS", "GBA", "GBC", "Game Boy",
        "PS5", "PS4", "PS3", "PS2", "PS1", "Xbox 360", "Xbox", "PC" };
    public static string PlatformOf(Game g) { return g.App != null ? null : !string.IsNullOrEmpty(g.Platform) ? g.Platform : g.ByStore ? "PC" : null; }
    static int PlatformRank(string p) { int i = Array.IndexOf(PlatformOrder, p); return i < 0 ? 98 : i; }
    // the folder's own colours (our own picks, one pair per console)
    static string[] PlatformColours(string p) {
        switch (p) {
            case "Switch 2": return new[] { "#e8453c", "#3a3f47" };
            case "Switch": return new[] { "#e60012", "#00a3e0" };
            case "Wii U": return new[] { "#1aa0d8", "#5fd0f0" };
            case "Wii": return new[] { "#8fa4b8", "#d6e2ec" };
            case "GameCube": return new[] { "#5a4fa0", "#8f84d6" };
            case "N64": return new[] { "#2f8f4e", "#f2c230" };
            case "SNES": return new[] { "#6b5fb0", "#b4abd8" };
            case "NES": return new[] { "#b23a3a", "#6e6e6e" };
            case "3DS": case "NDS": return new[] { "#4a5a6a", "#9fb0c0" };
            case "GBA": return new[] { "#4b3fa8", "#8a7fe0" };
            case "GBC": return new[] { "#d8a020", "#7fc04a" };
            case "Game Boy": return new[] { "#7f8a3a", "#c3cc7a" };
            case "PS5": case "PS4": case "PS3": case "PS2": case "PS1": return new[] { "#1f3f8f", "#4f7fd6" };
            case "Xbox 360": case "Xbox": return new[] { "#2f8f2f", "#7fcf5f" };
            default: return new[] { "#3a4a5a", "#7a8a9a" };
        }
    }

    // ---------- Home in folders ----------
    bool FoldersOn { get { return Settings.Get("HomeFolders") == "on"; } }
    string homeFolder;   // the console whose folder is open on Home, or null
    static bool IsFolder(Game g) { return g.Id.StartsWith("folder:"); }
    static bool IsFolderBack(Game g) { return g.Id == "folder-back"; }
    void AddFolders() {
        if (Games.Exists(x => x.Id == "folder-back")) return;
        Catalog.SeriesRank["folders"] = 98;   // after the games, before the launcher's pages
        var back = new Game { Id = "folder-back", Name = "Back", Short = "Back", Source = "RECOMP LAUNCHER", Series = "folders", Rank = 4000, Exe = "", CheckFile = "",
            Accent = "#5b6670", Accent2 = "#8a959e", SettingsLabel = "Back", Needs = "", App = CloseConsoleFolder };
        back.Icon = AppFace("", "#5b6670", "#8a959e"); back.IconIsArt = true;
        Catalog.Games.Add(back);
        int r = 4001;
        foreach (var p in PlatformOrder) {
            string pp = p; var c = PlatformColours(p);
            var g = new Game { Id = "folder:" + p, Name = p, Short = p, Source = "RECOMP LAUNCHER", Series = "folders", Rank = r++, Exe = "", CheckFile = "",
                Accent = c[0], Accent2 = c[1], SettingsLabel = p + " folder", Needs = "", App = () => OpenConsoleFolder(pp) };
            g.Icon = FolderFace(p, new List<Game>()); g.IconIsArt = true;
            Catalog.Games.Add(g);
        }
    }
    // the games a folder holds on Home: the installed ones (and one being installed) of that console
    List<Game> FolderGames(string p) {
        return Games.Where(g => g.App == null && !g.Hidden && (g.Inst != null || g.IsBusy) && PlatformOf(g) == p)
            .OrderByDescending(g => Playtime.Last(g.Id)).ThenBy(g => g.Rank).ToList();
    }
    // whether a tile shows on Home's row while the folders are on
    bool ShownInFolders(Game g) {
        if (IsFolderBack(g)) return homeFolder != null;
        if (IsFolder(g)) return homeFolder == null && FolderGames(g.Name).Count > 0;
        if (g.App != null) return homeFolder == null;
        if (!(g.Inst != null || g.IsBusy)) return false;
        return homeFolder == null ? Profiles.IsFav(g.Id) : PlatformOf(g) == homeFolder;
    }
    // the row's order with the folders on: Back, the favourites, the folders (by console), the launcher's pages
    List<Game> FolderRow() {
        return Games.OrderBy(g => IsFolderBack(g) ? 0 : g.App == null ? 1 : IsFolder(g) ? 2 : 3)
            .ThenByDescending(g => g.App == null && Profiles.IsFav(g.Id))
            .ThenByDescending(g => g.App == null ? Playtime.Last(g.Id) : DateTime.MinValue)
            .ThenBy(g => IsFolder(g) ? PlatformRank(g.Name) : 0).ThenBy(g => g.Rank).ToList();
    }
    // the folders' faces show the games in them; the first folder after the favourites stands a little apart
    void RefreshFolders() {
        bool favs = homeFolder == null && Games.Any(g => g.App == null && (g.Inst != null || g.IsBusy) && !g.Hidden && Profiles.IsFav(g.Id));
        bool first = true;
        foreach (var g in Games.Where(IsFolder).OrderBy(g => PlatformRank(g.Name))) {
            var inside = FolderGames(g.Name);
            var u = g.Ui as GameUi; if (u == null || u.Home == null) continue;
            if (inside.Count == 0) continue;
            g.Icon = FolderFace(g.Name, inside);
            u.Home.Icon.Source = (ImageSource)g.Icon;
            foreach (var t in FindNames(u.Home.Root)) t.Text = g.Name + "  •  " + inside.Count + (inside.Count == 1 ? " game" : " games");
            u.Home.Root.Margin = new Thickness(first && favs ? 40 : 0, 0, 9, 14);
            first = false;
        }
    }
    // the games' pictures load after the row is built: the folders draw again once they're in
    bool folderRefreshQueued;
    void QueueFolderRefresh() {
        if (!FoldersOn || folderRefreshQueued) return;
        folderRefreshQueued = true;
        W.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { folderRefreshQueued = false; RefreshFolders(); }));
    }
    static IEnumerable<TextBlock> FindNames(DependencyObject root) {
        // the tile's captions (the name over the tile and the one on it)
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) {
            var c = VisualTreeHelper.GetChild(root, i);
            var t = c as TextBlock;
            if (t != null && t.FontFamily != Ui.Mdl && t.Text.Length > 0 && !(t.Text.Length == 1)) yield return t;
            foreach (var x in FindNames(c)) yield return x;
        }
    }
    void OpenConsoleFolder(string p) {
        homeFolder = p;
        ApplySort(true); ApplyFilter();
        var first = order.FirstOrDefault(g => Shown(g) && !IsFolderBack(g)) ?? order.FirstOrDefault(Shown);
        if (first != null) ShowGame(first);
        Sound.Play(Sfx.Open); SetHints();
    }
    void CloseConsoleFolder() {
        if (homeFolder == null) return;
        string was = homeFolder; homeFolder = null;
        ApplySort(true); ApplyFilter();
        var f = Games.FirstOrDefault(g => g.Id == "folder:" + was);
        if (f != null && Shown(f)) ShowGame(f);
        Sound.Play(Sfx.Back); SetHints();
    }

    // a folder: the console's colours, a folder shape, and the first four games' pictures inside it
    static ImageSource FolderFace(string p, List<Game> inside) {
        var c = PlatformColours(p);
        var dg = new DrawingGroup();
        using (var dc = dg.Open()) {
            var c1 = (Color)ColorConverter.ConvertFromString(c[0]); var c2 = (Color)ColorConverter.ConvertFromString(c[1]);
            var bg = new LinearGradientBrush(c1, c2, 45); bg.Freeze();
            dc.DrawRectangle(bg, null, new Rect(0, 0, 256, 256));
            // the folder: a tab on top, a lighter body
            var body = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)); body.Freeze();
            var back = new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)); back.Freeze();
            var tab = new StreamGeometry();
            using (var g = tab.Open()) {
                g.BeginFigure(new Point(28, 82), true, true);
                g.LineTo(new Point(28, 64), true, false); g.ArcTo(new Point(38, 54), new Size(10, 10), 0, false, SweepDirection.Clockwise, true, false);
                g.LineTo(new Point(96, 54), true, false); g.LineTo(new Point(112, 70), true, false); g.LineTo(new Point(218, 70), true, false);
                g.ArcTo(new Point(228, 80), new Size(10, 10), 0, false, SweepDirection.Clockwise, true, false); g.LineTo(new Point(228, 82), true, false);
            }
            tab.Freeze();
            dc.DrawGeometry(back, null, tab);
            var shadow = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)); shadow.Freeze();
            dc.DrawRoundedRectangle(shadow, null, new Rect(28, 81, 200, 156), 10, 10);
            dc.DrawRoundedRectangle(body, null, new Rect(28, 78, 200, 156), 10, 10);
            // up to four of its games, in a little grid on the folder
            var pics = inside.Take(4).ToList();
            double s = 64, gap = 8, x0 = 128 - s - gap / 2, y0 = 90;
            for (int i = 0; i < 4; i++) {
                var r = new Rect(x0 + (i % 2) * (s + gap), y0 + (i / 2) * (s + gap), s, s);
                if (i < pics.Count) {
                    var gm = pics[i];
                    dc.PushClip(new RectangleGeometry(r, 6, 6));
                    var gb = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(gm.Accent), (Color)ColorConverter.ConvertFromString(gm.Accent2), 45); gb.Freeze();
                    dc.DrawRectangle(gb, null, r);
                    var src = gm.Icon as ImageSource;
                    if (src != null) {
                        if (gm.IconIsArt) dc.DrawImage(src, Fill(src, r));
                        else { var ir = r; ir.Inflate(-s * 0.2, -s * 0.2); dc.DrawImage(src, ir); }
                    }
                    dc.Pop();
                } else {
                    var empty = new SolidColorBrush(Color.FromArgb(0x22, c1.R, c1.G, c1.B)); empty.Freeze();
                    dc.DrawRoundedRectangle(empty, null, r, 6, 6);
                }
            }
            // the console's name, big, above the folder
            var ft = new FormattedText(p.ToUpperInvariant(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal), 30, Brushes.White);
            ft.MaxTextWidth = 220; ft.Trimming = TextTrimming.CharacterEllipsis; ft.MaxLineCount = 1;
            var shade = new FormattedText(p.ToUpperInvariant(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal), 30, new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0)));
            shade.MaxTextWidth = 220; shade.Trimming = TextTrimming.CharacterEllipsis; shade.MaxLineCount = 1;
            dc.DrawText(shade, new Point(29, 10 + 2));
            dc.DrawText(ft, new Point(28, 10));
            var shine = new RadialGradientBrush(Color.FromArgb(60, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)) { Center = new Point(0.3, 0.15), GradientOrigin = new Point(0.3, 0.15), RadiusX = 0.8, RadiusY = 0.8 }; shine.Freeze();
            dc.DrawRectangle(shine, null, new Rect(0, 0, 256, 256));
        }
        dg.Freeze();
        var img = new DrawingImage(dg); img.Freeze();
        return img;
    }
    // a picture covering a square, cut to fit (as UniformToFill)
    static Rect Fill(ImageSource src, Rect r) {
        double w = src.Width, h = src.Height; if (w <= 0 || h <= 0) return r;
        double k = Math.Max(r.Width / w, r.Height / h);
        return new Rect(r.X + (r.Width - w * k) / 2, r.Y + (r.Height - h * k) / 2, w * k, h * k);
    }
}
}
