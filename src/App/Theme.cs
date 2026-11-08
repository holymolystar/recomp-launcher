// Themes. The launcher is drawn in one base palette (Midnight); a theme recolours it as the window is built:
// every neutral shade (backgrounds, lines, text) is placed at the same spot between the theme's background and its
// ink, and the two accent families (teal, gold) become the theme's accents. Colours in the window layouts (.xaml)
// and in code go through the same map. Game colours are kept in lower case in the catalog, so they never match it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace RecompLauncher {

class ThemeDef {
    public string Id, Name, Desc, Bg, Ink, Accent, Accent2;
    public ThemeDef(string id, string name, string desc, string bg, string ink, string accent, string accent2) {
        Id = id; Name = name; Desc = desc; Bg = bg; Ink = ink; Accent = accent; Accent2 = accent2;
    }
}

static class Theme {
    public static readonly ThemeDef[] All = {
        new ThemeDef("midnight", "Midnight", "The original: deep blue-grey, teal and gold", "#090C0F", "#F2EFE6", "#3FB8A0", "#D9B45A"),
        new ThemeDef("neon", "Neon", "Synthwave purple with cyan and magenta", "#0A0614", "#F4EEFF", "#00E5FF", "#FF3DCB"),
        new ThemeDef("aurora", "Aurora", "Northern lights green and violet", "#03110E", "#EAFFF6", "#4DF5B0", "#B08CFF"),
        new ThemeDef("ember", "Ember", "Warm charcoal, orange and amber", "#120806", "#FFF1E6", "#FF7A3D", "#FFC94D"),
        new ThemeDef("sakura", "Sakura", "Plum night with pink and sky blue", "#140A11", "#FFEFF5", "#FF8FB8", "#8FD3FF"),
        new ThemeDef("abyss", "Abyss", "Pure black for OLED screens, ice blue", "#000000", "#F5F5F5", "#6EC1FF", "#E6E6E6"),
    };
    public static ThemeDef Current = All[0];

    // the base palette's neutral shades and accent families
    static readonly string[] Neutrals = {
        "050709", "090C0F", "0A0E11", "0B1114", "0D1215", "0E1418", "11181D", "1A242B", "1C252C", "1F2A32", "203039", "22303A",
        "26333C", "2A3640", "33424D", "5F717C", "6E808B", "7F8F99", "8A9AA5", "A9B6BE", "B5C0C7", "C9CCC8", "E8E6DF", "F2EFE6"
    };
    static readonly Dictionary<string, string> map = new Dictionary<string, string>();

    public static void Use(string id) {
        Current = All.FirstOrDefault(t => t.Id == id) ?? All[0];
        map.Clear();
        if (Current.Id == "midnight") return;
        Color b0 = Parse("090C0F"), w0 = Parse("F2EFE6"), bg = Parse(Current.Bg.TrimStart('#')), ink = Parse(Current.Ink.TrimStart('#'));
        double l0 = Lum(b0), l1 = Lum(w0);
        foreach (var n in Neutrals) {
            double t = (Lum(Parse(n)) - l0) / (l1 - l0);
            // darker than the base background (dim overlays): darker than the theme's background too
            Color c = t < 0 ? Mix(bg, Colors.Black, Math.Min(1, -t * 4)) : Mix(bg, ink, t);
            map[n] = Hex(c);
        }
        Color a = Parse(Current.Accent.TrimStart('#')), a2 = Parse(Current.Accent2.TrimStart('#'));
        map["3FB8A0"] = Hex(a); map["27D6C7"] = Hex(a); map["7FD6C9"] = Hex(Mix(a, Colors.White, 0.35));
        map["D9B45A"] = Hex(a2); map["F2D27A"] = Hex(Mix(a2, Colors.White, 0.3));
    }
    // "#AARRGGBB" or "#RRGGBB" in the theme's colours (the alpha is kept)
    public static string Map(string hex) {
        if (map.Count == 0 || hex == null || hex.Length < 7 || hex[0] != '#') return hex;
        string rgb = hex.Substring(hex.Length - 6), alpha = hex.Substring(1, hex.Length - 7);
        string m;
        return map.TryGetValue(rgb, out m) ? "#" + alpha + m : hex;
    }
    public static string MapXaml(string xaml) {
        if (map.Count == 0) return xaml;
        return Regex.Replace(xaml, @"#(?:[0-9A-F]{8}|[0-9A-F]{6})\b", m => Map(m.Value));
    }
    public static Color Parse(string rgb) {
        return Color.FromRgb(byte.Parse(rgb.Substring(0, 2), NumberStyles.HexNumber), byte.Parse(rgb.Substring(2, 2), NumberStyles.HexNumber), byte.Parse(rgb.Substring(4, 2), NumberStyles.HexNumber));
    }
    static string Hex(Color c) { return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
    static double Lum(Color c) { return 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B; }
    public static Color Mix(Color a, Color b, double t) {
        t = Math.Max(0, Math.Min(1, t));
        return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }
}
}
