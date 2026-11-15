// The launcher's own pages as tiles at the end of Home's row, after the games, like apps on a console: Characters,
// News, Board, Album, Friends, Browser and Controllers. Starting one (A, or a click) opens it. They're "games" with
// an App action: always there, never updated, not in All games, the game folders or the phone remote; Settings >
// Library can hide any of them.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace RecompLauncher {

partial class Shell {
    public static readonly Installed AppInstalledInfo = new Installed { Version = "", Sub = "" };
    // the tile's face: the app's colours with its sign, big and white
    static ImageSource AppFace(string glyph, string c1, string c2) {
        var dg = new DrawingGroup();
        using (var dc = dg.Open()) {
            var bg = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(c1), (Color)ColorConverter.ConvertFromString(c2), 45); bg.Freeze();
            dc.DrawRectangle(bg, null, new Rect(0, 0, 256, 256));
            var shine = new RadialGradientBrush(Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)) { Center = new Point(0.3, 0.2), GradientOrigin = new Point(0.3, 0.2), RadiusX = 0.8, RadiusY = 0.8 }; shine.Freeze();
            dc.DrawRectangle(shine, null, new Rect(0, 0, 256, 256));
            var ft = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe MDL2 Assets"), 104, Brushes.White);
            dc.DrawText(ft, new Point(128 - ft.Width / 2, 112 - ft.Height / 2));
        }
        dg.Freeze();
        var img = new DrawingImage(dg); img.Freeze();
        return img;
    }
    void AddApps() {
        if (Games.Exists(x => x.App != null)) return;
        Catalog.SeriesRank["apps"] = 99;   // after every game
        var apps = new[] {
            new object[] { "app-characters", "Characters", "", "#e8453c", "#f2a33a", (Action)OpenPlaza },
            new object[] { "app-mail", "Mail", "", "#e85a9b", "#f6a6c8", (Action)OpenMail },
            new object[] { "app-news", "News", "", "#2f8fe0", "#57cbde", (Action)OpenNews },
            new object[] { "app-board", "Board", "", "#f4a312", "#f2d24b", (Action)OpenBoard },
            new object[] { "app-album", "Album", "", "#3fb8a0", "#8fd3b8", (Action)OpenAlbum },
            new object[] { "app-friends", "Friends", "", "#5fae3a", "#a8d86b", (Action)OpenFriendList },
            new object[] { "app-browser", "Browser", "", "#3a6fd8", "#6ea8ff", (Action)OpenBrowserCard },
            new object[] { "app-controllers", "Controllers", "", "#5b6670", "#8a959e", (Action)OpenControllers },
            new object[] { "app-system", "System", "", "#3a4a5a", "#7d8a93", (Action)OpenSystem },
        };
        int rank = 5000;
        foreach (var a in apps) {
            var g = new Game { Id = (string)a[0], Name = (string)a[1], Short = (string)a[1], Source = "RECOMP LAUNCHER", Series = "apps", Rank = rank++, Exe = "", CheckFile = "",
                Accent = ((string)a[3]).ToLowerInvariant(), Accent2 = ((string)a[4]).ToLowerInvariant(), App = (Action)a[5], SettingsLabel = (string)a[1], Needs = "" };
            g.Icon = AppFace((string)a[2], (string)a[3], (string)a[4]); g.IconIsArt = true;
            Catalog.Games.Add(g);
        }
    }
}
}
