// Card faces: the player's own picture (icons\<id>.png/.jpg), else the game program's icon, else a drawn tile in the
// game's colours with a white symbol (or its short name). Pictures are decoded at the size they are shown at.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RecompLauncher {

static class Ui {
    static readonly Dictionary<string, SolidColorBrush> brushes = new Dictionary<string, SolidColorBrush>();
    public static Color Col(string hex) { return (Color)ColorConverter.ConvertFromString(Theme.Map(hex)); }
    // frozen, shared brushes: one per colour for the whole app
    public static SolidColorBrush Brush(string hex) {
        SolidColorBrush b;
        if (!brushes.TryGetValue(hex, out b)) { b = new SolidColorBrush(Col(hex)); b.Freeze(); brushes[hex] = b; }
        return b;
    }
    public static Color Alpha(string hex, byte a) { var c = Col(hex); return Color.FromArgb(a, c.R, c.G, c.B); }
    public static Brush Gradient(string a, string b) { var g = new LinearGradientBrush(Col(a), Col(b), 45); g.Freeze(); return g; }
    public const string Muted = "#8A9AA5", Gold = "#D9B45A", Teal = "#3FB8A0", Red = "#E06C5B", Text = "#E8E6DF", Dim = "#5F717C";
    public static readonly FontFamily Semi = new FontFamily("Segoe UI Variable Display Semibold, Segoe UI Semibold");
    public static readonly FontFamily Mdl = new FontFamily("Segoe MDL2 Assets");
}

static class Icons {
    public static ImageSource ExeIcon(string path, int size) {
        if (!File.Exists(path)) return null;
        var h = new IntPtr[1]; var id = new int[1];
        uint n = IconX.PrivateExtractIcons(path, 0, size, size, h, id, 1, 0);
        if (n < 1 || h[0] == IntPtr.Zero) return null;
        try {
            var src = Imaging.CreateBitmapSourceFromHIcon(h[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze(); return src;
        } catch { return null; } finally { IconX.DestroyIcon(h[0]); }
    }
    public static ImageSource Custom(Game g) {
        foreach (var ext in new[] { "png", "jpg", "jpeg" }) {
            string p = Path.Combine(Paths.Root, "icons", g.Id + "." + ext);
            if (!File.Exists(p)) continue;
            try {
                var img = new BitmapImage();
                img.BeginInit(); img.UriSource = new Uri(p); img.CacheOption = BitmapCacheOption.OnLoad; img.DecodePixelWidth = 320; img.EndInit(); img.Freeze();
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen()) {
                    var rect = new Rect(0, 0, 256, 256);
                    dc.PushClip(new RectangleGeometry(rect, 56, 56));
                    dc.DrawImage(img, rect);
                    dc.Pop();
                }
                return Render(dv);
            } catch { }
        }
        return null;
    }
    static ImageSource Render(DrawingVisual dv) {
        var bmp = new RenderTargetBitmap(256, 256, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv); bmp.Freeze(); return bmp;
    }
    // icon for a game: (image, whether it is a whole card face)
    public static void Load(Game g) {
        var c = Custom(g);
        if (c != null) { g.Icon = c; g.IconIsArt = true; return; }
        var e = ExeIcon(Engine.ResolveExe(g), 256);
        if (e != null) { g.Icon = e; g.IconIsArt = false; return; }
        g.Icon = Drawn(g); g.IconIsArt = true;
    }

    // ---------- drawn tiles: symbols on a 256 x 256 grid, filled shapes and round-capped lines ----------
    struct Part { public Geometry Geo; public double W; }
    static Part F(Geometry g) { return new Part { Geo = g }; }
    static Part F(string d) { return new Part { Geo = Geometry.Parse(d) }; }
    static Part L(string d, double w) { return new Part { Geo = Geometry.Parse(d), W = w }; }
    static Part L(Geometry g, double w) { return new Part { Geo = g, W = w }; }
    static Geometry C(double x, double y, double r) { return new EllipseGeometry(new Point(x, y), r, r); }
    static Geometry Cut(Geometry a, Geometry b) { return new CombinedGeometry(GeometryCombineMode.Exclude, a, b); }
    static Geometry Rays(double cx, double cy, double r1, double r2, int n) {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < n; i++) {
            double a = 2 * Math.PI * i / n;
            sb.AppendFormat(CultureInfo.InvariantCulture, "M{0:0.#},{1:0.#} L{2:0.#},{3:0.#} ", cx + r1 * Math.Cos(a), cy + r1 * Math.Sin(a), cx + r2 * Math.Cos(a), cy + r2 * Math.Sin(a));
        }
        return Geometry.Parse(sb.ToString());
    }
    static Part[] Symbol(string id) {
        switch (id) {
            case "totk": return new[] { L("M128,58 A70,70 0 1 1 58,128", 18), L("M128,96 A32,32 0 1 0 160,128", 14), F(C(128, 128, 9)) };
            case "smo": return new[] { F(Cut(C(122, 136, 70), C(152, 110, 60))), F("M184,52 L192,74 L215,74 L196,88 L203,110 L184,96 L165,110 L172,88 L153,74 L176,74 Z") };
            case "wwhd": return new[] { F("M130,44 L130,160 L74,160 Z"), F("M140,70 L140,160 L188,160 Z"), F("M52,170 L204,170 L184,196 L72,196 Z"), L("M46,222 Q66,208 86,222 T126,222 T166,222 T206,222", 10) };
            case "tod": return new[] { L("M152,104 L82,174", 36), F(Cut(C(166, 90, 46), Geometry.Parse("M183.7,93.5 L162.5,72.3 L204.9,30.1 L226.1,51.3 Z"))) };
            case "acit": return new[] { L(C(128, 132, 70), 16), L("M128,132 L128,90", 12), L("M128,132 L160,152", 12), F(C(128, 132, 10)), L("M172,77 L158,98 L174,110 L162,128", 8) };
            case "mp6": return new[] { F(C(98, 100, 34)), L(Rays(98, 100, 46, 62, 8), 10), F(Cut(C(166, 162, 50), C(190, 140, 44))) };
            case "mkwii": return new[] { L(C(128, 128, 76), 20), F(C(128, 128, 24)), L("M52,128 L104,128 M152,128 L204,128 M128,152 L128,204", 18) };
            case "sms": return new[] { F(C(128, 100, 36)), L(Rays(128, 100, 48, 64, 10), 10), L("M44,174 Q64,160 86,174 T128,174 T170,174 T212,174", 11), L("M64,206 Q84,192 106,206 T148,206 T190,206", 11) };
            case "botw": return new[] { F("M30,212 L96,118 L132,166 L158,134 L226,212 Z"), L("M112,62 Q156,30 200,62", 14), L("M112,62 L156,100 L200,62", 4), F(C(156, 104, 9)) };
            case "nsmbu": case "nsmbw": return new[] { F("M28,214 Q128,150 228,214 Z"), L("M150,184 L150,48", 12), F("M156,52 L214,74 L156,98 Z"), F(C(150, 44, 11)) };
            case "pkla": case "pokopia": case "pkstadium": case "pkss": case "pkgen": case "pmd": case "pkblack": case "pksnap": case "pklgpe": case "pkswsh": {
                var parts = new List<Part> { F("M128,128 C164,128 196,166 190,190 C184,212 160,204 128,204 C96,204 72,212 66,190 C60,166 92,128 128,128 Z") };
                foreach (var t in new[] { new[] { 66, 112 }, new[] { 102, 76 }, new[] { 154, 76 }, new[] { 190, 112 } })
                    parts.Add(F(new EllipseGeometry(new Point(t[0], t[1]), 20, 26)));
                return parts.ToArray();
            }
            case "pikmin": return new[] { L("M128,214 C124,176 136,140 128,104", 14), F("M128,104 C92,96 72,62 84,34 C114,40 134,70 128,104 Z") };
            case "pikmin2": {
                var petals = new GeometryGroup();
                foreach (var a in new[] { 0, 72, 144, 216, 288 }) {
                    double r = a * Math.PI / 180;
                    petals.Children.Add(new EllipseGeometry(new Point(128 + 30 * Math.Sin(r), 84 - 30 * Math.Cos(r)), 22, 22));
                }
                return new[] { L("M128,214 C124,176 136,140 128,112", 14), F(Cut(petals, C(128, 84, 15))), F(C(128, 84, 9)) };
            }
            case "smg": return new[] { F("M128,36 L152,100 L220,102 L166,144 L186,210 L128,172 L70,210 L90,144 L36,102 L104,100 Z") };
            default: return null;
        }
    }
    public static ImageSource Drawn(Game g) {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) {
            var tile = new Rect(8, 8, 240, 240);
            dc.DrawRoundedRectangle(new LinearGradientBrush(Ui.Col(g.Accent), Ui.Col(g.Accent2), 45), null, tile, 56, 56);
            dc.DrawRoundedRectangle(new LinearGradientBrush(Ui.Alpha("#FFFFFF", 0x48), Ui.Alpha("#FFFFFF", 0), 90), null, tile, 56, 56);
            var parts = Symbol(g.Id);
            if (parts != null) {
                foreach (var pass in new[] { new { C = Ui.Alpha("#000000", 0x38), Dy = 6.0 }, new { C = Colors.White, Dy = 0.0 } }) {
                    var br = new SolidColorBrush(pass.C);
                    dc.PushTransform(new TranslateTransform(0, pass.Dy));
                    foreach (var p in parts) {
                        if (p.W > 0) {
                            var pen = new Pen(br, p.W) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                            dc.DrawGeometry(null, pen, p.Geo);
                        } else dc.DrawGeometry(br, null, p.Geo);
                    }
                    dc.Pop();
                }
            } else {
                var ft = new FormattedText(g.Short, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Black"),
                    g.Short.Length > 3 ? 58 : 92, Brushes.White, 1.0);
                dc.DrawText(ft, new Point(128 - ft.Width / 2, 128 - ft.Height / 2));
            }
        }
        return Render(dv);
    }
}
}
