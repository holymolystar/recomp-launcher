// TV & Movies' own themes, picked per TV profile (on the profile's page, "Theme"). The TV screens are drawn in the
// Classic colours: black, dark greys (#141416 ... #3A3A3C) and a red-orange accent (TvAccent). Those colours are
// handed out as shared brushes (TvTheme.Brush), so picking another theme recolours everything on screen at once:
// each grey is placed at the same spot between the theme's background and its lightest grey, and the accent becomes
// the theme's accent. Everything else (white text, channel and poster colours, the video) stays as it is.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace RecompLauncher {

class TvThemeDef {
    public string Id, Name, Desc, Bg, Hi, Accent, Mark1, Mark2;
    public TvThemeDef(string id, string name, string desc, string bg, string hi, string accent, string mark1, string mark2) {
        Id = id; Name = name; Desc = desc; Bg = bg; Hi = hi; Accent = accent; Mark1 = mark1; Mark2 = mark2;
    }
}

static class TvTheme {
    public static readonly TvThemeDef[] All = {
        new TvThemeDef("classic", "Classic", "Black with red-orange", "000000", "3A3A3C", "FF3D1F", "FF6A2B", "D7141E"),
        new TvThemeDef("ocean", "Ocean", "Deep navy with bright blue", "01060D", "2A3A4E", "2F9BFF", "4FB3FF", "1652D6"),
        new TvThemeDef("royal", "Royal", "Midnight purple with violet", "06030D", "3A2E52", "A66BFF", "BE8CFF", "6A2BD9"),
        new TvThemeDef("forest", "Forest", "Dark green with emerald", "010805", "26402F", "21C46B", "4BE38F", "0E8A47"),
        new TvThemeDef("gold", "Gold", "Warm black with amber", "0A0603", "433626", "F5A623", "FFC94D", "D9760F"),
        new TvThemeDef("rose", "Rose", "Plum black with pink", "0B0307", "45293A", "FF4F8B", "FF85AE", "D0145A"),
        new TvThemeDef("lagoon", "Lagoon", "Blue-grey with teal", "05090B", "2E3D45", "21BFA5", "3FD9BE", "107E8C"),
    };
    public static TvThemeDef Current = All[0];

    // the Classic colours that follow the theme
    const string BaseAccent = "FF3D1F", BaseHi = "3A3A3C";
    static readonly HashSet<string> Greys = new HashSet<string> {
        "000000", "0A0A0A", "101012", "141416", "151517", "161618", "1A1A1C", "1C1C1E", "232326", "2A2A2E", "2C2C30", "2E2E31", "3A3A3C" };
    static readonly Dictionary<string, SolidColorBrush> brushes = new Dictionary<string, SolidColorBrush>();
    static readonly List<Action> redraws = new List<Action>();

    // the theme a TV profile picked ("free" for Free TV)
    public static TvThemeDef Of(string profile) {
        string id; return TvMine.Themes.TryGetValue(profile ?? "free", out id) ? All.FirstOrDefault(t => t.Id == id) ?? All[0] : All[0];
    }
    public static void Use(TvThemeDef t) {
        if (t == null || t == Current) return;
        Current = t;
        foreach (var kv in brushes) kv.Value.Color = Map(kv.Key);
        foreach (var r in redraws) r();
    }

    // "#AARRGGBB" or "#RRGGBB" in the theme's colours (the alpha is kept)
    public static Color Map(string hex) {
        string rgb = hex.Substring(hex.Length - 6).ToUpperInvariant();
        byte a = hex.Length == 9 ? byte.Parse(hex.Substring(1, 2), NumberStyles.HexNumber) : (byte)255;
        Color c = Parse(rgb);
        if (Current != All[0]) {
            if (rgb == BaseAccent) c = Parse(Current.Accent);
            else if (Greys.Contains(rgb)) c = Theme.Mix(Parse(Current.Bg), Parse(Current.Hi), Lum(c) / Lum(Parse(BaseHi)));
        }
        return Color.FromArgb(a, c.R, c.G, c.B);
    }
    static bool Follows(string hex) {
        if (hex == null || hex.Length < 7 || hex[0] != '#') return false;
        string rgb = hex.Substring(hex.Length - 6).ToUpperInvariant();
        return rgb == BaseAccent || Greys.Contains(rgb);
    }
    // a brush: shared and kept in step with the theme for the Classic colours, the usual one (Ui.Brush) for the rest
    public static Brush Brush(string hex) {
        if (!Follows(hex)) return Ui.Brush(hex);
        string key = hex.ToUpperInvariant();
        SolidColorBrush b;
        if (!brushes.TryGetValue(key, out b)) brushes[key] = b = new SolidColorBrush(Map(key));
        return b;
    }
    public static Color Col(string hex) { return Follows(hex) ? Map(hex) : Ui.Col(hex); }
    // a brush drawn from the theme's colours, drawn again when the theme changes (made once, when a screen is built)
    public static T Live<T>(T brush, Action<T> draw) where T : Brush {
        draw(brush); redraws.Add(() => draw(brush)); return brush;
    }
    // a diagonal gradient (as Ui.Gradient), shared per pair of colours
    static readonly Dictionary<string, Brush> gradients = new Dictionary<string, Brush>();
    public static Brush Gradient(string a, string b) {
        if (!Follows(a) && !Follows(b)) return Ui.Gradient(a, b);
        Brush got; if (gradients.TryGetValue(a + b, out got)) return got;
        return gradients[a + b] = Live(new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) }, g => {
            g.GradientStops.Clear(); g.GradientStops.Add(new GradientStop(Col(a), 0)); g.GradientStops.Add(new GradientStop(Col(b), 1));
        });
    }
    // the app's mark (a tile with a screen on it), in the theme's two mark colours
    static Brush mark;
    public static Brush Mark { get {
        if (mark == null) mark = Live(new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) }, g => {
            g.GradientStops.Clear(); g.GradientStops.Add(new GradientStop(Parse(Current.Mark1), 0)); g.GradientStops.Add(new GradientStop(Parse(Current.Mark2), 1));
        });
        return mark;
    } }

    static Color Parse(string rgb) { return Theme.Parse(rgb.TrimStart('#')); }
    static double Lum(Color c) { return 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B; }
}
}
