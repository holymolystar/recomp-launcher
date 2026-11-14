// Draws a character (AvatarDef) as a chibi figure on a 400 x 600 canvas: a big round head, a small rounded body,
// arms in two parts so they can pose, and layers from back to front (cape and wings, back hair, legs and bottoms,
// shoes, the top, neck things, arms, the head and everything on it, hair in front).
// Faces are drawn in "head units": the head is a circle of radius 100 around (0, 0), scaled to the head's size, so
// every part follows the face shape and the sliders. Colours are the character's own (lower case, never themed).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RecompLauncher {

class AvatarState {
    public int Mood = -1;      // -1: the character's own mood setting; 0: its plain face
    public int Pose = -1;      // -1: its own pose
    public bool Blink;
    public double Walk;        // 0: standing; else where it is in its step (radians): the feet lift in turn, the arms swing
    public bool Held;          // picked up: arms up, legs kicking (with Walk)
    public bool NoShadow;      // no shadow on the ground (the plaza draws its own, which stays on the floor)
    public bool Backdrop;      // draw its background colour and pattern
}

static class AvatarArt {
    public const double W = 400, H = 600, Ground = 580;

    // ---------- pictures ----------
    public enum Crop { Full, Portrait, Head, Features }
    public static Rect CropRect(AvatarDef d, Crop c) {
        var L = new Layout(d);
        switch (c) {
            case Crop.Portrait: return new Rect(200 - 150, L.HeadY - 140, 300, 300);
            case Crop.Head: { double s = 130 * L.Hs; return new Rect(200 - s, L.HeadY - s - 6, 2 * s, 2 * s); }
            case Crop.Features: { double s = 78 * L.Hs; return new Rect(200 - s, L.HeadY + 30 * L.Hs - s, 2 * s, 2 * s); }
            default: return new Rect(0, 40, W, H - 40);
        }
    }
    public static ImageSource Render(AvatarDef d, AvatarState st, Crop c, int pxW, int pxH) {
        if (c != Crop.Features) { try { return Avatar3D.Render(d, st, c, pxW, pxH); } catch { } }   // 3D (Avatar3D.cs); flat if that fails
        var dv = new DrawingVisual();
        var r = CropRect(d, c);
        using (var dc = dv.RenderOpen()) {
            double k = Math.Max(pxW / r.Width, pxH / r.Height);
            dc.PushTransform(new TranslateTransform(pxW / 2.0, pxH / 2.0));
            dc.PushTransform(new ScaleTransform(k, k));
            dc.PushTransform(new TranslateTransform(-(r.X + r.Width / 2), -(r.Y + r.Height / 2)));
            Draw(dc, d, st);
            dc.Pop(); dc.Pop(); dc.Pop();
        }
        var bmp = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv); bmp.Freeze();
        return bmp;
    }
    // ---------- for the 3D characters (Avatar3D.cs) ----------
    // the backdrop alone, framed like Render
    public static ImageSource BackdropImage(AvatarDef d, Crop c, int pxW, int pxH) {
        var dv = new DrawingVisual();
        var r = CropRect(d, c);
        using (var dc = dv.RenderOpen()) {
            double k = Math.Max(pxW / r.Width, pxH / r.Height);
            dc.PushTransform(new TranslateTransform(pxW / 2.0, pxH / 2.0)); dc.PushTransform(new ScaleTransform(k, k));
            dc.PushTransform(new TranslateTransform(-(r.X + r.Width / 2), -(r.Y + r.Height / 2)));
            Backdrop(dc, d);
            dc.Pop(); dc.Pop(); dc.Pop();
        }
        var bmp = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32); bmp.Render(dv); bmp.Freeze();
        return bmp;
    }
    // the face, drawn flat to be printed on the front of the 3D head: the head's 2D space, -110..110 both ways,
    // on a clear background (no skin, hair or hat: those are 3D)
    public static ImageSource FaceDecal(AvatarDef d, int mood, bool blink, int px) {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) {
            double k = px / 220.0;
            dc.PushTransform(new ScaleTransform(k, k)); dc.PushTransform(new TranslateTransform(110, 110));
            string skin = d.C("skin");
            if (mood < 0) mood = d.I("mood");
            FaceDetails(dc, d, skin); Beard(dc, d); Nose(dc, d, skin); Mouth(dc, d, mood); Eyes(dc, d, mood, blink); Brows(dc, d, mood); Glasses(dc, d);
            dc.Pop(); dc.Pop();
        }
        var bmp = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32); bmp.Render(dv); bmp.Freeze();
        return bmp;
    }
    // the arm angles of each pose (degrees from hanging down, outward): upper arm, forearm, for left then right
    public static double[] PoseArms(int pose) { return (double[])PoseAngles[Math.Max(0, Math.Min(PoseAngles.Length - 1, pose))].Clone(); }
    public static string[] TopNames { get { return AvatarSpec.ByKey["top"].Names; } }

    // the round profile picture: head and shoulders over the backdrop, in the character's mood
    public static ImageSource Portrait(AvatarDef d, int px) { return Render(d, new AvatarState { Backdrop = true }, Crop.Portrait, px, px); }

    // ---------- helpers ----------
    static readonly Dictionary<string, Brush> brushes = new Dictionary<string, Brush>();
    static Brush B(string hex) {
        Brush b;
        lock (brushes) if (!brushes.TryGetValue(hex, out b)) { b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); brushes[hex] = b; }
        return b;
    }
    static Brush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    static Color Col(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
    static Color Shade(Color c, double k) {
        Func<double, byte> f = v => (byte)Math.Max(0, Math.Min(255, k < 1 ? v * k : v + (255 - v) * (k - 1)));
        return Color.FromRgb(f(c.R), f(c.G), f(c.B));
    }
    static Brush Shade(string hex, double k) { return B(Shade(Col(hex), k)); }
    static Brush A(Color c, byte a) { return B(Color.FromArgb(a, c.R, c.G, c.B)); }
    static readonly Dictionary<string, Geometry> geoms = new Dictionary<string, Geometry>();
    static Geometry G(string path) {
        Geometry g;
        lock (geoms) if (!geoms.TryGetValue(path, out g)) { g = Geometry.Parse(path); g.Freeze(); geoms[path] = g; }
        return g;
    }
    static Pen P(Brush b, double w) { var p = new Pen(b, w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }; p.Freeze(); return p; }
    // numbers in path text always with a decimal point, whatever the Windows language
    static string F(string fmt, params object[] a) { return string.Format(System.Globalization.CultureInfo.InvariantCulture, fmt, a); }
    static Brush Ink { get { return B("#2a2230"); } }
    static void Push(DrawingContext dc, double x, double y) { dc.PushTransform(new TranslateTransform(x, y)); }
    static void Rot(DrawingContext dc, double deg) { dc.PushTransform(new RotateTransform(deg)); }
    static void Scale(DrawingContext dc, double sx, double sy) { dc.PushTransform(new ScaleTransform(sx, sy)); }
    static Geometry Star(double r, double inner, int points) {
        var sg = new StreamGeometry();
        using (var c = sg.Open()) {
            for (int i = 0; i < points * 2; i++) {
                double a = -Math.PI / 2 + i * Math.PI / points, rr = i % 2 == 0 ? r : r * inner;
                var p = new Point(Math.Cos(a) * rr, Math.Sin(a) * rr);
                if (i == 0) c.BeginFigure(p, true, true); else c.LineTo(p, true, true);
            }
        }
        sg.Freeze(); return sg;
    }
    static Geometry Heart(double s) {
        return Geometry.Parse(F(
            "M 0,{0} C {1},{2} {3},{4} 0,{5} C {6},{4} {7},{2} 0,{0} Z", 0.9 * s, -1.4 * s, 0.1 * s, -0.7 * s, -1.0 * s, -0.35 * s, 0.7 * s, 1.4 * s));
    }

    // ---------- where everything goes ----------
    class Layout {
        public double Hgt, Bld, Hs, HeadR, HeadY, ShoulderY, HipY, AnkleY, ShoulderW, HipW, ArmT, LegT, Upper, Fore, LegLen, SideW;
        public Layout(AvatarDef d) {
            Hgt = d.D("height"); Bld = d.D("build"); Hs = d.D("headSize");
            LegLen = 88 * Hgt; double torso = 92 * Hgt;
            AnkleY = Ground - 20; HipY = AnkleY - LegLen; ShoulderY = HipY - torso;
            HeadR = 104 * Hs;
            HeadY = ShoulderY - 10 - HeadR;
            ShoulderW = 44 * Bld; HipW = 38 * Bld; ArmT = 22 * Bld; LegT = 28 * Bld;
            Upper = 44 * Hgt; Fore = 40 * Hgt;
            SideW = new[] { 100, 90, 102, 98, 86, 108, 93, 104 }[d.I("face")];
        }
    }

    public static void Draw(DrawingContext dc, AvatarDef d, AvatarState st) {
        var L = new Layout(d);
        int pose = st.Pose >= 0 ? st.Pose : d.I("pose");
        int mood = st.Mood >= 0 ? st.Mood : d.I("mood");
        if (st.Backdrop) Backdrop(dc, d);
        // a soft shadow on the ground, so the character stands on something
        if (!st.NoShadow) {
            var ground = new RadialGradientBrush(Color.FromArgb(90, 0, 0, 0), Color.FromArgb(0, 0, 0, 0)); ground.Freeze();
            dc.DrawEllipse(ground, null, new Point(200, Ground - 2), 86 * L.Bld + 20, 15);
        }
        // walking: arms down and swinging against the legs; held: arms up
        double swing = 0;
        if (st.Held) pose = 2;
        else if (st.Walk != 0) { pose = 0; swing = Math.Sin(st.Walk) * 20; }
        // the character itself, drawn once into a picture of its own...
        bool simple = d.I("style") == 0;
        var fig = new DrawingGroup();
        using (var c = fig.Open()) {
            BackThings(c, d, L);
            HeadBack(c, d, L);
            if (simple) { SimpleLegs(c, d, L, st.Walk); SimpleTorso(c, d, L); Neck(c, d, L); SimpleArms(c, d, L, pose, swing); }
            else { LegsWalking(c, d, L, st.Walk); Torso(c, d, L); Neck(c, d, L); Arms(c, d, L, pose, swing); }
            Head(c, d, L, mood, st.Blink);
        }
        fig.Freeze();
        dc.DrawDrawing(fig);
        // ...then lit as one rounded shape: light from the top left, shade to the bottom right, only where the
        // character is (its own picture is the mask)
        var b = fig.Bounds;
        if (b.IsEmpty || b.Width < 1) return;
        var mask = new DrawingBrush(fig) { Stretch = Stretch.None, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = b, ViewportUnits = BrushMappingMode.Absolute, Viewport = b };
        mask.Freeze();
        dc.PushOpacityMask(mask);
        dc.DrawRectangle(simple ? SoftLight : BodyLight, null, b);
        dc.Pop();
    }
    // the simple style's light: gentler, like smooth plastic
    static readonly Brush SoftLight = MakeSoftLight();
    static Brush MakeSoftLight() {
        var g = new LinearGradientBrush { StartPoint = new Point(0.15, 0), EndPoint = new Point(0.85, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(30, 255, 255, 255), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.5));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(34, 20, 20, 40), 1));
        g.Freeze(); return g;
    }
    // a soft round shine, for the simple style's head and body
    static Brush Shine(byte a) {
        var g = new RadialGradientBrush(Color.FromArgb(a, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)); g.Freeze(); return g;
    }

    // ---------- the simple style: one smooth shape each, no folds or prints ----------
    // the body: a rounded capsule in the top's colour, a little narrower at the waist
    static Geometry SimpleTorsoShape(Layout L) {
        double sy = L.ShoulderY, hy = L.HipY, sw = L.ShoulderW, hw = L.HipW;
        return G(F("M {0},{1} C {0},{2} {3},{2} 200,{2} C {4},{2} {5},{2} {5},{1} L {6},{7} C {6},{8} {9},{8} 200,{8} C {10},{8} {11},{8} {11},{7} Z",
            200 - sw, sy + 16, sy - 4, 200 - sw * 0.5, 200 + sw * 0.5, 200 + sw, 200 + hw + 2, hy - 4, hy + 14, 200 + hw * 0.5, 200 - hw * 0.5, 200 - hw - 2));
    }
    static void SimpleTorso(DrawingContext dc, AvatarDef d, Layout L) {
        string c = d.C("topCol");
        var shape = SimpleTorsoShape(L);
        dc.DrawRectangle(Shade(d.C("skin"), 0.9), null, new Rect(188, L.ShoulderY - 20, 24, 26));   // the neck
        dc.DrawGeometry(B(c), null, shape);
        dc.PushClip(shape);
        var b = shape.Bounds;
        // round, like a capsule: darker at the sides, a soft shine up and to the left
        var side = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        side.GradientStops.Add(new GradientStop(Color.FromArgb(40, 10, 10, 30), 0)); side.GradientStops.Add(new GradientStop(Color.FromArgb(0, 10, 10, 30), 0.3));
        side.GradientStops.Add(new GradientStop(Color.FromArgb(0, 10, 10, 30), 0.7)); side.GradientStops.Add(new GradientStop(Color.FromArgb(56, 10, 10, 30), 1));
        side.Freeze();
        dc.DrawRectangle(side, null, b);
        dc.DrawEllipse(Shine(70), null, new Point(b.Left + b.Width * 0.34, b.Top + b.Height * 0.28), b.Width * 0.26, b.Height * 0.3);
        dc.Pop();
    }
    // the arms: sleeves all the way, round hands (no fingers)
    static void SimpleArms(DrawingContext dc, AvatarDef d, Layout L, int pose, double swing) {
        var a = (double[])PoseAngles[Math.Max(0, Math.Min(PoseAngles.Length - 1, pose))].Clone();
        a[0] += swing; a[1] += swing * 0.6; a[2] -= swing; a[3] -= swing * 0.6;
        string c = d.C("topCol"); var skin = d.C("skin");
        double t = L.ArmT + 4;
        for (int i = 0; i < 2; i++) {
            int s = i == 0 ? -1 : 1;
            double a1 = a[i * 2] * Math.PI / 180, a2 = a[i * 2 + 1] * Math.PI / 180;
            var sh = new Point(200 + s * (L.ShoulderW - t / 2 + 2), L.ShoulderY + t / 2 + 2);
            var el = new Point(sh.X + s * Math.Sin(a1) * L.Upper, sh.Y + Math.Cos(a1) * L.Upper);
            var hd = new Point(el.X + s * Math.Sin(a2) * L.Fore, el.Y + Math.Cos(a2) * L.Fore);
            var pen = P(B(c), t);
            dc.DrawLine(pen, sh, el); dc.DrawLine(pen, el, hd);
            dc.DrawLine(P(A(Color.FromRgb(255, 255, 255), 40), t * 0.3), new Point(sh.X - s * t * 0.18, sh.Y), new Point(el.X - s * t * 0.18, el.Y));   // a soft shine along it
            double r = t * 0.72;
            dc.DrawEllipse(B(skin), null, hd, r, r);
            dc.DrawEllipse(Shine(90), null, new Point(hd.X - r * 0.3, hd.Y - r * 0.3), r * 0.55, r * 0.55);
        }
    }
    // the legs: two smooth rounded columns, rounded feet; walking lifts each foot in turn
    static void SimpleLegs(DrawingContext dc, AvatarDef d, Layout L, double walk) {
        string bc = d.C("bottomCol"), sc = d.C("shoeCol");
        double t = L.LegT + 2, gap = L.HipW - t / 2 - 2;
        for (int s = -1; s <= 1; s += 2) {
            double lift = walk == 0 ? 0 : Math.Max(0, s * Math.Sin(walk)) * 14;
            double x = 200 + s * gap, top = L.HipY - 6, foot = L.AnkleY - lift;
            dc.DrawRoundedRectangle(B(bc), null, new Rect(x - t / 2, top, t, foot - top + 4), t / 2, t / 2);
            dc.DrawRoundedRectangle(A(Color.FromRgb(255, 255, 255), 30), null, new Rect(x - t * 0.3, top + 8, t * 0.22, foot - top - 16), t * 0.11, t * 0.11);
            dc.DrawRoundedRectangle(Shade(bc, 0.78), null, new Rect(x + t * 0.18, top + 6, t * 0.26, foot - top - 6), t * 0.13, t * 0.13);
            // the foot
            dc.DrawEllipse(B(sc), null, new Point(x + s * 4, foot + 10), t * 0.66, t * 0.4);
            dc.DrawEllipse(Shine(70), null, new Point(x + s * 2 - 4, foot + 6), t * 0.3, t * 0.16);
        }
    }
    // light from the top left to shade at the bottom right, over the whole character
    static readonly Brush BodyLight = MakeBodyLight();
    static Brush MakeBodyLight() {
        var g = new LinearGradientBrush { StartPoint = new Point(0.1, 0), EndPoint = new Point(0.9, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(46, 255, 255, 255), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.42));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 20, 10, 40), 0.55));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(70, 20, 10, 40), 1));
        g.Freeze(); return g;
    }
    // a ball's shading for the head: a soft highlight up and to the left, a darker rim down and to the right
    static readonly Brush HeadLight = MakeHeadLight();
    static Brush MakeHeadLight() {
        var g = new RadialGradientBrush { GradientOrigin = new Point(0.32, 0.26), Center = new Point(0.46, 0.44), RadiusX = 0.66, RadiusY = 0.66 };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(80, 255, 255, 255), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.4));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 60, 20, 30), 0.62));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(78, 60, 20, 30), 1));
        g.Freeze(); return g;
    }
    // a cylinder's shading for the body: darker at both sides, a band of light left of the middle
    static readonly Brush TorsoLight = MakeTorsoLight();
    static Brush MakeTorsoLight() {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(54, 15, 10, 35), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 15, 10, 35), 0.24));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(40, 255, 255, 255), 0.38));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.56));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 15, 10, 35), 0.7));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(84, 15, 10, 35), 1));
        g.Freeze(); return g;
    }

    // ---------- backdrop ----------
    static void Backdrop(DrawingContext dc, AvatarDef d) {
        var c = Col(d.C("bgCol"));
        dc.DrawRectangle(B(c), null, new Rect(-200, -200, W + 400, H + 400));
        var mark = A(Shade(c, c.R + c.G + c.B > 380 ? 0.86 : 1.25), 255);
        switch (d.I("bgPattern")) {
            case 1: for (int y = -200; y < 800; y += 40) for (int x = -200; x < 600; x += 40) dc.DrawEllipse(mark, null, new Point(x + (y / 40 % 2) * 20, y), 6, 6); break;
            case 2: for (int x = -400; x < 800; x += 48) dc.DrawGeometry(mark, null, G(F("M {0},-200 L {1},-200 L {2},800 L {3},800 Z", x, x + 22, x + 22 - 500, x - 500))); break;
            case 3:
                for (int y = -200; y < 800; y += 70) for (int x = -200; x < 600; x += 70) {
                    Push(dc, x + (y / 70 % 2) * 35, y); dc.DrawGeometry(mark, null, Star(14, 0.45, 5)); dc.Pop();
                }
                break;
            case 4:
                for (int i = 0; i < 16; i++) {
                    Push(dc, 200, 220); Rot(dc, i * 22.5);
                    if (i % 2 == 0) dc.DrawGeometry(mark, null, G("M 0,0 L -90,-700 L 90,-700 Z"));
                    dc.Pop(); dc.Pop();
                }
                break;
            case 5: for (int y = -200; y < 800; y += 40) for (int x = -200; x < 600; x += 40) if (((x + y) / 40) % 2 == 0) dc.DrawRectangle(mark, null, new Rect(x, y, 40, 40)); break;
        }
    }

    // ---------- behind the body: cape, wings, backpack ----------
    static void BackThings(DrawingContext dc, AvatarDef d, Layout L) {
        string c = d.C("backCol");
        double sy = L.ShoulderY, sw = L.ShoulderW;
        switch (d.I("back")) {
            case 1:
                dc.DrawRoundedRectangle(Shade(c, 0.85), null, new Rect(200 - sw - 6, sy - 4, (sw + 6) * 2, 84 * L.Hgt), 22, 22);
                break;
            case 2:
                dc.DrawGeometry(B(c), null, G(F("M {0},{1} L {2},{1} L {3},{4} Q 200,{5} {6},{4} Z", 200 - sw + 4, sy, 200 + sw - 4, 200 + sw + 40, Ground - 30, Ground - 10, 200 - sw - 40)));
                dc.DrawGeometry(Shade(c, 0.8), null, G(F("M {0},{1} L {2},{1} L {3},{4} Q 200,{5} {6},{4} Z", 200 - sw + 14, sy + 10, 200 + sw - 14, 200 + sw + 20, Ground - 40, Ground - 26, 200 - sw - 20)));
                break;
            case 3:
                for (int s = -1; s <= 1; s += 2) {
                    Push(dc, 200 + s * 18, sy + 30); Scale(dc, s, 1);
                    dc.DrawGeometry(B("#ffffff"), P(B(c), 3), G("M 0,0 C 40,-70 120,-90 150,-60 C 130,-40 140,-20 120,-4 C 120,10 110,26 90,30 C 80,44 50,50 0,20 Z"));
                    dc.DrawGeometry(null, P(A(Col(c), 140), 3), G("M 30,0 C 60,-30 100,-50 130,-52 M 34,14 C 60,0 90,-10 112,-8 M 36,24 C 56,22 72,20 88,24"));
                    dc.Pop(); dc.Pop();
                }
                break;
        }
    }

    // ---------- legs, bottoms, shoes ----------
    // walking: each leg below the knee lifts in its turn (drawn a little higher, over the leg above it)
    static void LegsWalking(DrawingContext dc, AvatarDef d, Layout L, double walk) {
        if (walk == 0) { Legs(dc, d, L); return; }
        double knee = L.HipY + L.LegLen * 0.5;
        dc.PushClip(new RectangleGeometry(new Rect(-300, -600, 1000, knee + 600))); Legs(dc, d, L); dc.Pop();
        for (int s = -1; s <= 1; s += 2) {
            double lift = Math.Max(0, s * Math.Sin(walk)) * 14;
            dc.PushClip(new RectangleGeometry(new Rect(s < 0 ? -300 : 200, knee - lift, 500, 900)));
            Push(dc, 0, -lift); Legs(dc, d, L); dc.Pop();
            dc.Pop();
        }
    }
    static void Legs(DrawingContext dc, AvatarDef d, Layout L) {
        var skin = d.C("skin"); string bc = d.C("bottomCol"), sc = d.C("shoeCol");
        int bottom = d.I("bottom"), top = d.I("top"), shoes = d.I("shoes");
        if (top == 9) bc = d.C("topCol");   // overalls: the trousers are the overalls' colour
        double gap = L.HipW - L.LegT / 2 - 2;
        var skinPen = P(B(skin), L.LegT);
        for (int s = -1; s <= 1; s += 2) dc.DrawLine(skinPen, new Point(200 + s * gap, L.HipY), new Point(200 + s * gap, L.AnkleY));
        var bp = B(bc); var dark = Shade(bc, 0.82);
        double knee = L.HipY + L.LegLen * 0.5;
        // the hips
        dc.DrawGeometry(bp, null, G(F("M {0},{1} L {2},{1} L {3},{4} Q 200,{5} {6},{4} Z", 200 - L.HipW - 4, L.HipY - 18, 200 + L.HipW + 4, 200 + L.HipW + 6, L.HipY + 14, L.HipY + 22, 200 - L.HipW - 6)));
        switch (bottom) {
            case 0: case 3: case 4: case 5: {
                double bottomY = bottom == 5 ? L.AnkleY : L.AnkleY - 4, t = L.LegT + (bottom == 5 ? 2 : 8);
                for (int s = -1; s <= 1; s += 2) dc.DrawLine(P(bp, t), new Point(200 + s * gap, L.HipY), new Point(200 + s * gap, bottomY));
                if (bottom == 0) for (int s = -1; s <= 1; s += 2) dc.DrawLine(P(dark, 1.6), new Point(200 + s * (gap + t / 2 - 5), L.HipY + 8), new Point(200 + s * (gap + t / 2 - 5), bottomY - 4));
                if (bottom == 3) for (int s = -1; s <= 1; s += 2) dc.DrawLine(P(dark, t + 2), new Point(200 + s * gap, bottomY - 4), new Point(200 + s * gap, bottomY - 2));
                if (bottom == 4) for (int s = -1; s <= 1; s += 2) dc.DrawRoundedRectangle(dark, null, new Rect(200 + s * gap + (s < 0 ? -t / 2 - 3 : t / 2 - 13), knee - 6, 16, 18), 3, 3);
                break;
            }
            case 1: for (int s = -1; s <= 1; s += 2) dc.DrawLine(P(bp, L.LegT + 10), new Point(200 + s * gap, L.HipY), new Point(200 + s * gap, L.HipY + L.LegLen * 0.38)); break;
            case 2: case 6: {
                double len = bottom == 2 ? 0.62 : 0.36;
                dc.DrawGeometry(bp, null, G(F("M {0},{1} L {2},{1} L {3},{4} Q 200,{5} {6},{4} Z", 200 - L.HipW - 4, L.HipY - 16, 200 + L.HipW + 4, 200 + L.HipW + 26, L.HipY + L.LegLen * len, L.HipY + L.LegLen * len + 8, 200 - L.HipW - 26)));
                dc.DrawGeometry(null, P(dark, 1.6), G(F("M {0},{1} L {2},{3} M 200,{1} L 200,{4} M {5},{1} L {6},{3}", 200 - L.HipW + 8, L.HipY, 200 - L.HipW - 4, L.HipY + L.LegLen * len - 4, L.HipY + L.LegLen * len + 4, 200 + L.HipW - 8, 200 + L.HipW + 4)));
                break;
            }
        }
        // shoes
        for (int s = -1; s <= 1; s += 2) {
            double x = 200 + s * gap + s * 4, y = Ground;
            Push(dc, x, y); Scale(dc, s * L.Bld * 0.9 + s * 0.1, 1);
            var c = B(sc); var dk = Shade(sc, 0.75);
            switch (shoes) {
                case 0:
                    dc.DrawGeometry(c, null, G("M -16,-24 C -4,-30 14,-26 20,-14 C 30,-12 32,-2 26,0 L -18,0 C -22,-8 -22,-18 -16,-24 Z"));
                    dc.DrawRectangle(B("#ffffff"), null, new Rect(-19, -5, 46, 5));
                    dc.DrawEllipse(B("#ffffff"), null, new Point(0, -21), 2.2, 2.2); dc.DrawEllipse(B("#ffffff"), null, new Point(7, -19), 2.2, 2.2);
                    break;
                case 1:
                    dc.DrawGeometry(c, null, G("M -16,-44 L 14,-44 L 16,-16 C 28,-14 32,-2 26,0 L -18,0 C -22,-14 -20,-34 -16,-44 Z"));
                    dc.DrawRectangle(B("#ffffff"), null, new Rect(-19, -6, 46, 6));
                    dc.DrawEllipse(B("#ffffff"), null, new Point(-1, -30), 5, 5);
                    break;
                case 2:
                    dc.DrawGeometry(c, null, G("M -17,-52 L 15,-52 L 16,-18 C 30,-16 32,-2 26,0 L -18,0 Z"));
                    dc.DrawRectangle(dk, null, new Rect(-19, -6, 46, 6));
                    dc.DrawRectangle(dk, null, new Rect(-17, -52, 32, 6));
                    break;
                case 3:
                    dc.DrawGeometry(B(d.C("skin")), null, G("M -14,-20 C -2,-24 14,-20 20,-10 C 28,-8 28,-2 24,0 L -16,0 C -20,-6 -20,-16 -14,-20 Z"));
                    dc.DrawRectangle(c, null, new Rect(-18, -4, 44, 4));
                    dc.DrawLine(P(c, 4), new Point(-12, -16), new Point(18, -8));
                    dc.DrawLine(P(c, 4), new Point(-12, -8), new Point(2, -20));
                    break;
                case 4:
                    dc.DrawGeometry(c, null, G("M -16,-18 C -2,-22 14,-20 20,-10 C 28,-8 30,-2 26,0 L -18,0 C -22,-6 -21,-14 -16,-18 Z"));
                    dc.DrawGeometry(dk, null, G("M -14,-18 C -4,-20 6,-20 10,-16 L -14,-14 Z"));
                    break;
                case 5:
                    dc.DrawGeometry(c, null, G("M -18,-58 L 16,-58 L 18,-18 C 30,-16 32,-2 26,0 L -20,0 Z"));
                    dc.DrawRectangle(dk, null, new Rect(-20, -6, 48, 6));
                    dc.DrawLine(P(A(Colors.White, 120), 4), new Point(-10, -50), new Point(-10, -20));
                    break;
                case 6:
                    dc.DrawGeometry(B(d.C("skin")), null, G("M -14,-20 C -2,-24 14,-20 20,-10 C 28,-8 28,-2 24,0 L -16,0 C -20,-6 -20,-16 -14,-20 Z"));
                    foreach (var tx in new[] { 6, 12, 18, 23 }) dc.DrawEllipse(Shade(d.C("skin"), 0.9), null, new Point(tx, -3), 2.6, 2.6);
                    break;
            }
            dc.Pop(); dc.Pop();
        }
    }

    // ---------- the top ----------
    static Geometry TorsoShape(Layout L) {
        double sy = L.ShoulderY, hy = L.HipY, sw = L.ShoulderW, hw = L.HipW;
        return G(F(
            "M {0},{1} Q {0},{2} {3},{2} L {4},{2} Q {5},{2} {5},{1} L {6},{7} Q 200,{8} {9},{7} Z",
            200 - sw, sy + 14, sy, 200 - sw + 16, 200 + sw - 16, 200 + sw, 200 + hw + 6, hy + 6, hy + 16, 200 - hw - 6));
    }
    static void Torso(DrawingContext dc, AvatarDef d, Layout L) {
        int top = d.I("top"); string c1 = d.C("topCol"), c2 = d.C("topCol2");
        var shape = TorsoShape(L);
        double sy = L.ShoulderY, hy = L.HipY, sw = L.ShoulderW;
        // hoodie: the hood behind the neck
        if (top == 2) dc.DrawEllipse(Shade(c1, 0.8), null, new Point(200, sy + 2), sw * 0.75, 20);
        // the neck
        dc.DrawRectangle(Shade(d.C("skin"), 0.88), null, new Rect(186, sy - 22, 28, 30));
        if (top == 9) {
            // overalls: a shirt in the second colour, the bib in the first
            dc.DrawGeometry(B(c2), null, shape);
            dc.DrawRoundedRectangle(B(c1), null, new Rect(200 - L.HipW + 6, sy + 34, (L.HipW - 6) * 2, hy - sy - 20), 8, 8);
            for (int s = -1; s <= 1; s += 2) {
                dc.DrawLine(P(B(c1), 9), new Point(200 + s * (L.HipW - 12), sy + 38), new Point(200 + s * (sw - 14), sy + 2));
                dc.DrawEllipse(B("#f2d24b"), null, new Point(200 + s * (L.HipW - 14), sy + 42), 4, 4);
            }
            dc.DrawRoundedRectangle(Shade(c1, 0.85), null, new Rect(186, sy + 52, 28, 20), 4, 4);
        } else {
            dc.DrawGeometry(B(c1), null, shape);
            Pattern(dc, d, shape, L);
            switch (top) {
                case 2:
                    dc.DrawRoundedRectangle(Shade(c1, 0.85), null, new Rect(170, hy - 34, 60, 26), 8, 8);
                    dc.DrawLine(P(B(c2), 3), new Point(192, sy + 4), new Point(190, sy + 30));
                    dc.DrawLine(P(B(c2), 3), new Point(208, sy + 4), new Point(210, sy + 30));
                    break;
                case 3:
                    dc.DrawGeometry(B(c2), null, G(F("M 188,{0} L 212,{0} L 216,{1} L 184,{1} Z", sy, hy + 12)));
                    dc.DrawGeometry(Shade(c1, 0.82), null, G(F("M 186,{0} L 172,{1} L 184,{2} Z M 214,{0} L 228,{1} L 216,{2} Z", sy, sy + 30, sy + 56)));
                    break;
                case 4:
                    for (double y = hy - 4; y <= hy + 8; y += 6) dc.DrawLine(P(Shade(c1, 0.85), 2.5), new Point(200 - L.HipW, y), new Point(200 + L.HipW, y));
                    dc.DrawGeometry(Shade(c1, 0.85), null, G(F("M 182,{0} Q 200,{1} 218,{0} L 214,{2} Q 200,{3} 186,{2} Z", sy - 2, sy + 14, sy - 6, sy + 6)));
                    break;
                case 5:
                    dc.DrawGeometry(B(d.C("skin")), null, G(F("M {0},{1} L {2},{1} L {3},{4} Q 200,{5} {6},{4} Z", 200 - sw + 12, sy - 1, 200 + sw - 12, 214, sy + 4, sy + 22, 186)));
                    break;
                case 6:
                    dc.DrawGeometry(B(c2), P(Shade(c2, 0.8), 1.5), G(F("M 186,{0} L 200,{1} L 214,{0} L 218,{2} L 200,{3} L 182,{2} Z", sy - 4, sy + 8, sy + 10, sy + 16)));
                    for (double y = sy + 26; y < hy; y += 16) dc.DrawEllipse(B(c2), null, new Point(200, y), 3, 3);
                    break;
                case 7:
                    for (int s = -1; s <= 1; s += 2) dc.DrawLine(P(B(c2), 7), new Point(200 + s * (sw - 6), sy + 20), new Point(200 + s * (L.HipW + 2), hy));
                    break;
                case 8:
                    dc.DrawGeometry(B(c1), null, G(F("M {0},{1} L {2},{1} L {3},{4} Q 200,{5} {6},{4} Z", 200 - L.HipW - 6, hy - 6, 200 + L.HipW + 6, 200 + L.HipW + 34, hy + L.LegLen * 0.5, hy + L.LegLen * 0.5 + 10, 200 - L.HipW - 34)));
                    dc.DrawLine(P(Shade(c1, 0.8), 4), new Point(200 - L.HipW - 2, hy - 4), new Point(200 + L.HipW + 2, hy - 4));
                    break;
            }
            if (top == 7) {
                // the jersey's number
                var ft = new FormattedText("7", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Black"), 34, B(c2));
                dc.DrawText(ft, new Point(200 - ft.Width / 2, sy + 22));
            }
            // a soft line at the neck
            if (top != 5) dc.DrawGeometry(null, P(Shade(c1, 0.8), 3), G(F("M 184,{0} Q 200,{1} 216,{0}", sy + 2, sy + 12)));
        }
        // the body as a cylinder, and the shadow the head throws on the shoulders
        dc.PushClip(shape);
        dc.DrawRectangle(TorsoLight, null, shape.Bounds);
        var under = new RadialGradientBrush(Color.FromArgb(92, 15, 10, 35), Color.FromArgb(0, 15, 10, 35)); under.Freeze();
        dc.DrawEllipse(under, null, new Point(204, sy + 2), sw * 0.95, 26);
        dc.Pop();
    }
    static void Pattern(DrawingContext dc, AvatarDef d, Geometry shape, Layout L) {
        int p = d.I("pattern"); if (p == 0 || d.I("top") == 9) return;
        var c = B(d.C("topCol2"));
        dc.PushClip(shape);
        double sy = L.ShoulderY, hy = L.HipY;
        switch (p) {
            case 1: for (double y = sy + 14; y < hy + 20; y += 18) dc.DrawRectangle(c, null, new Rect(140, y, 120, 8)); break;
            case 2: for (double y = sy + 12; y < hy + 20; y += 16) for (double x = 150; x < 260; x += 16) dc.DrawEllipse(c, null, new Point(x + ((int)((y - sy) / 16) % 2) * 8, y), 3.2, 3.2); break;
            case 3: Push(dc, 200, (sy + hy) / 2 + 4); dc.DrawGeometry(c, null, Star(20, 0.45, 5)); dc.Pop(); break;
            case 4: Push(dc, 200, (sy + hy) / 2 + 2); dc.DrawGeometry(c, null, Heart(16)); dc.Pop(); break;
            case 5: Push(dc, 200, (sy + hy) / 2 + 4); dc.DrawGeometry(c, null, G("M 4,-24 L -12,4 L 0,4 L -6,24 L 12,-6 L 0,-6 L 6,-24 Z")); dc.Pop(); break;
        }
        dc.Pop();
    }

    // ---------- scarf, ties, necklace ----------
    static void Neck(DrawingContext dc, AvatarDef d, Layout L) {
        string c = d.C("neckCol"); double sy = L.ShoulderY;
        switch (d.I("neck")) {
            case 1:
                dc.DrawRoundedRectangle(B(c), null, new Rect(176, sy - 10, 48, 20), 10, 10);
                dc.DrawGeometry(Shade(c, 0.85), null, G(F("M 206,{0} L 222,{0} L 226,{1} L 210,{1} Z", sy + 4, sy + 54)));
                for (int i = 0; i < 3; i++) dc.DrawLine(P(Shade(c, 0.7), 2), new Point(212 + i * 4, sy + 54), new Point(212 + i * 4, sy + 60));
                break;
            case 2:
                dc.DrawGeometry(B(c), null, G(F("M 200,{0} L 182,{1} L 182,{2} Z M 200,{0} L 218,{1} L 218,{2} Z", sy + 6, sy - 4, sy + 16)));
                dc.DrawEllipse(Shade(c, 0.8), null, new Point(200, sy + 6), 5, 5);
                break;
            case 3:
                dc.DrawGeometry(B(c), null, G(F("M 194,{0} L 206,{0} L 203,{1} L 210,{2} L 200,{3} L 190,{2} L 197,{1} Z", sy, sy + 8, sy + 52, sy + 62)));
                break;
            case 4:
                dc.DrawGeometry(null, P(B(c), 2), G(F("M 182,{0} Q 200,{1} 218,{0}", sy - 2, sy + 30)));
                Push(dc, 200, sy + 30); dc.DrawGeometry(B(c), null, Heart(7)); dc.Pop();
                break;
            case 5:
                dc.DrawGeometry(B(c), null, G(F("M 178,{0} L 222,{0} L 200,{1} Z", sy - 4, sy + 30)));
                for (int i = 0; i < 4; i++) dc.DrawEllipse(B("#ffffff"), null, new Point(190 + i * 7, sy + 2 + (i % 2) * 6), 1.8, 1.8);
                break;
        }
        if (d.I("back") == 1)
            for (int s = -1; s <= 1; s += 2) dc.DrawLine(P(B(d.C("backCol")), 7), new Point(200 + s * (L.ShoulderW - 14), sy + 2), new Point(200 + s * (L.ShoulderW - 18), sy + 56));
    }

    // ---------- arms ----------
    // (upper arm, forearm) angles from straight down, outward positive, for the arm on the viewer's left then right
    static readonly double[][] PoseAngles = {
        new double[] { 14, 8, 14, 8 },         // stand
        new double[] { 14, 8, 104, 178 },      // wave
        new double[] { 150, 168, 150, 168 },   // cheer
        new double[] { 42, -48, 42, -48 },     // hands on hips
        new double[] { 14, 8, 88, 88 },        // point
        new double[] { 18, -150, 12, -96 },    // thinking
        new double[] { 30, 100, 30, 100 },     // shrug
    };
    static void Arms(DrawingContext dc, AvatarDef d, Layout L, int pose, double swing = 0) {
        var a = (double[])PoseAngles[Math.Max(0, Math.Min(PoseAngles.Length - 1, pose))].Clone();
        // the swing: one arm out while the other comes in, and back
        a[0] += swing; a[1] += swing * 0.6; a[2] -= swing; a[3] -= swing * 0.6;
        int top = d.I("top");
        bool sleeveless = top == 5 || top == 8;
        bool longSleeve = top == 1 || top == 2 || top == 3 || top == 4;
        string sleeveCol = top == 9 ? d.C("topCol2") : d.C("topCol");
        var skin = B(d.C("skin"));
        for (int i = 0; i < 2; i++) {
            int s = i == 0 ? -1 : 1;
            double a1 = a[i * 2] * Math.PI / 180, a2 = a[i * 2 + 1] * Math.PI / 180;
            var sh = new Point(200 + s * (L.ShoulderW - L.ArmT / 2 + 1), L.ShoulderY + L.ArmT / 2 + 2);
            var el = new Point(sh.X + s * Math.Sin(a1) * L.Upper, sh.Y + Math.Cos(a1) * L.Upper);
            var hd = new Point(el.X + s * Math.Sin(a2) * L.Fore, el.Y + Math.Cos(a2) * L.Fore);
            var pen = P(skin, L.ArmT);
            dc.DrawLine(pen, sh, el); dc.DrawLine(pen, el, hd);
            if (!sleeveless) {
                var sp = P(B(sleeveCol), L.ArmT + 6);
                if (longSleeve) {
                    dc.DrawLine(sp, sh, el);
                    var cuff = new Point(hd.X - (hd.X - el.X) * 0.25, hd.Y - (hd.Y - el.Y) * 0.25);
                    dc.DrawLine(sp, el, cuff);
                    dc.DrawEllipse(Shade(sleeveCol, 0.82), null, cuff, (L.ArmT + 6) / 2, (L.ArmT + 6) / 2);
                } else {
                    var mid = new Point(sh.X + (el.X - sh.X) * 0.55, sh.Y + (el.Y - sh.Y) * 0.55);
                    dc.DrawLine(sp, sh, mid);
                    if (top == 7) dc.DrawLine(P(B(d.C("topCol2")), L.ArmT + 6), new Point(mid.X - (mid.X - sh.X) * 0.12, mid.Y - (mid.Y - sh.Y) * 0.12), mid);
                }
            }
            dc.DrawEllipse(skin, null, hd, L.ArmT * 0.62, L.ArmT * 0.62);
            dc.DrawEllipse(Shade(d.C("skin"), 0.9), null, new Point(hd.X - s * 3, hd.Y + 3), L.ArmT * 0.25, L.ArmT * 0.2);
        }
    }

    // ---------- the head ----------
    // face shapes in head units (radius 100)
    static Geometry FaceShape(int f) {
        switch (f) {
            case 1: return new EllipseGeometry(new Point(0, -3), 90, 105);
            case 2: return G("M 0,-100 C 60,-100 104,-60 102,-10 C 100,40 50,85 0,100 C -50,85 -100,40 -102,-10 C -104,-60 -60,-100 0,-100 Z");
            case 3: return new RectangleGeometry(new Rect(-98, -100, 196, 200), 58, 58);
            case 4: return new EllipseGeometry(new Point(0, -6), 86, 108);
            case 5: return new EllipseGeometry(new Point(0, 4), 108, 94);
            case 6: return G("M 0,-100 C 52,-100 88,-70 92,-20 C 98,30 100,75 60,94 C 30,104 -30,104 -60,94 C -100,75 -98,30 -92,-20 C -88,-70 -52,-100 0,-100 Z");
            case 7: return G("M 0,-100 C 50,-96 102,-50 104,0 C 100,50 50,92 0,100 C -50,92 -100,50 -104,0 C -102,-50 -50,-96 0,-100 Z");
            default: return new EllipseGeometry(new Point(0, 0), 100, 100);
        }
    }
    static void EnterHead(DrawingContext dc, Layout L) { Push(dc, 200, L.HeadY); Scale(dc, L.HeadR / 100, L.HeadR / 100); }
    static void LeaveHead(DrawingContext dc) { dc.Pop(); dc.Pop(); }

    // hair behind the head
    static void HeadBack(DrawingContext dc, AvatarDef d, Layout L) {
        EnterHead(dc, L);
        var hair = B(d.C("hairCol"));
        bool flip = d.I("hairFlip") == 1;
        if (flip) Scale(dc, -1, 1);
        double sx = L.SideW / 100.0;
        Scale(dc, sx, 1);
        switch (d.I("hair")) {
            case 7: dc.DrawEllipse(hair, null, new Point(0, -30), 124, 112); break;
            case 8: dc.DrawEllipse(hair, null, new Point(0, -24), 152, 140); break;
            case 11: dc.DrawGeometry(hair, null, G("M -112,-20 C -122,-100 -60,-126 0,-126 C 60,-126 122,-100 112,-20 L 116,70 C 100,84 80,84 66,74 L -66,74 C -80,84 -100,84 -116,70 Z")); break;
            case 12: case 19: dc.DrawGeometry(hair, null, G("M -110,-20 C -120,-100 -60,-126 0,-126 C 60,-126 120,-100 110,-20 L 122,190 C 96,206 70,200 56,186 L -56,186 C -70,200 -96,206 -122,190 Z")); break;
            case 13: dc.DrawGeometry(hair, null, G("M -110,-20 C -120,-100 -60,-126 0,-126 C 60,-126 120,-100 110,-20 C 128,30 104,60 124,100 C 140,140 112,170 126,196 C 100,210 70,204 56,188 L -56,188 C -70,204 -100,210 -126,196 C -112,170 -140,140 -124,100 C -104,60 -128,30 -110,-20 Z")); break;
            case 14: dc.DrawGeometry(hair, null, G("M 60,-70 C 130,-50 150,40 128,130 C 120,160 100,172 92,150 C 104,90 94,10 50,-36 Z")); dc.DrawEllipse(Shade(d.C("hairCol"), 0.7), null, new Point(80, -40), 12, 10); break;
            case 15: dc.DrawGeometry(hair, null, G("M 20,-112 C 80,-170 160,-140 160,-70 C 160,-10 140,40 122,66 C 128,10 116,-56 66,-86 Z")); dc.DrawEllipse(Shade(d.C("hairCol"), 0.7), null, new Point(42, -112), 14, 10); break;
            case 17: dc.DrawEllipse(hair, null, new Point(0, -128), 36, 34); break;
            case 18: dc.DrawEllipse(hair, null, new Point(-66, -104), 32, 32); dc.DrawEllipse(hair, null, new Point(66, -104), 32, 32); break;
        }
        dc.Pop();
        if (flip) dc.Pop();
        // back of a hat brim or a cape hood would go here; the ears come next
        int ears = d.I("ears");
        var skin = d.C("skin");
        for (int s = -1; s <= 1; s += 2) {
            double x = s * (L.SideW - 4);
            Push(dc, x, 14); Scale(dc, s, 1);
            switch (ears) {
                case 1: dc.DrawEllipse(B(skin), null, new Point(4, 0), 12, 16); dc.DrawEllipse(Shade(skin, 0.86), null, new Point(5, 0), 5, 8); break;
                case 2: dc.DrawGeometry(B(skin), null, G("M -6,-14 C 10,-30 30,-50 40,-56 C 34,-30 26,-4 12,18 C 4,22 -6,16 -6,-14 Z")); dc.DrawGeometry(Shade(skin, 0.86), null, G("M 2,-8 C 12,-20 24,-36 30,-42 C 26,-24 20,-6 10,8 Z")); break;
                case 3: dc.DrawEllipse(B(skin), null, new Point(8, 0), 22, 28); dc.DrawEllipse(Shade(skin, 0.86), null, new Point(9, 0), 10, 15); break;
                default: dc.DrawEllipse(B(skin), null, new Point(6, 0), 16, 22); dc.DrawEllipse(Shade(skin, 0.86), null, new Point(7, 0), 7, 11); break;
            }
            dc.Pop(); dc.Pop();
        }
        LeaveHead(dc);
    }

    static void Head(DrawingContext dc, AvatarDef d, Layout L, int mood, bool blink) {
        EnterHead(dc, L);
        string skin = d.C("skin");
        var face = FaceShape(d.I("face"));
        dc.DrawGeometry(B(skin), null, face);
        // the head as a ball: its highlight and rim shade, and a soft shadow at the chin
        dc.PushClip(face);
        if (d.I("style") == 0) {
            // simple: smooth and evenly lit, with a soft shine up and to the left
            var rim = new RadialGradientBrush { GradientOrigin = new Point(0.4, 0.35), Center = new Point(0.5, 0.48), RadiusX = 0.62, RadiusY = 0.62 };
            rim.GradientStops.Add(new GradientStop(Color.FromArgb(0, 40, 20, 30), 0.6)); rim.GradientStops.Add(new GradientStop(Color.FromArgb(44, 40, 20, 30), 1)); rim.Freeze();
            dc.DrawRectangle(rim, null, new Rect(-112, -112, 224, 224));
            dc.DrawEllipse(Shine(70), null, new Point(-34, -50), 40, 30);
        } else dc.DrawRectangle(HeadLight, null, new Rect(-112, -112, 224, 224));
        dc.DrawEllipse(A(Shade(Col(skin), 0.8), 60), null, new Point(0, 104), 90, 26);
        dc.Pop();

        FaceDetails(dc, d, skin);
        Beard(dc, d);
        Nose(dc, d, skin);
        Mouth(dc, d, mood);
        Eyes(dc, d, mood, blink);
        Brows(dc, d, mood);
        Earrings(dc, d, L);
        FrontHair(dc, d, L);
        Glasses(dc, d);
        Hat(dc, d, L);
        LeaveHead(dc);
        FrontTails(dc, d, L);
    }

    static void FaceDetails(DrawingContext dc, AvatarDef d, string skin) {
        int ex = d.I("extra");
        byte blush = (byte)(ex == 2 ? 120 : d.I("style") == 0 ? 0 : 55);   // the simple style: clean cheeks unless Blush is chosen
        var bl = B(Color.FromArgb(blush, 255, 110, 120));
        double ey = 12 + d.D("eyeY");
        dc.DrawEllipse(bl, null, new Point(-58, ey + 34), 18, 11);
        dc.DrawEllipse(bl, null, new Point(58, ey + 34), 18, 11);
        var dk = Shade(skin, 0.72);
        switch (ex) {
            case 1:
                foreach (var p in new[] { new Point(-64, 44), new Point(-54, 50), new Point(-70, 54), new Point(-58, 60), new Point(64, 44), new Point(54, 50), new Point(70, 54), new Point(58, 60), new Point(-8, 40), new Point(8, 42) })
                    dc.DrawEllipse(dk, null, p, 2.4, 2.4);
                break;
            case 3: dc.DrawEllipse(B("#3b2a20"), null, new Point(42, 74), 3, 3); break;
            case 4:
                Push(dc, 60, 52); Rot(dc, -30);
                dc.DrawRoundedRectangle(B("#f2d3b0"), P(B("#d9b48c"), 1), new Rect(-18, -7, 36, 14), 6, 6);
                dc.DrawRectangle(B("#e8c49c"), null, new Rect(-6, -7, 12, 14));
                dc.Pop(); dc.Pop();
                break;
            case 5:
                for (int s = -1; s <= 1; s += 2) {
                    dc.DrawLine(P(B("#f0453a"), 5), new Point(s * 46, 46), new Point(s * 72, 50));
                    dc.DrawLine(P(B("#2f6fe0"), 5), new Point(s * 46, 56), new Point(s * 72, 60));
                }
                break;
            case 6: Push(dc, -60, 50); dc.DrawGeometry(B("#f2d24b"), P(B("#c98f12"), 1.5), Star(11, 0.45, 5)); dc.Pop(); break;
            case 7:
                for (int s = -1; s <= 1; s += 2) dc.DrawGeometry(null, P(dk, 2.2), G(F("M {0},48 Q {1},62 {2},76", s * 40, s * 34, s * 38)));
                break;
        }
    }

    static void Beard(DrawingContext dc, AvatarDef d) {
        int b = d.I("beard"); if (b == 0) return;
        var c = Col(d.C("beardCol")); var cb = B(c);
        double my = 60 + d.D("mouthY");
        switch (b) {
            case 1: dc.DrawGeometry(A(c, 70), null, G("M -96,10 C -96,80 -50,118 0,120 C 50,118 96,80 96,10 C 86,40 60,48 40,46 C 24,44 14,40 0,40 C -14,40 -24,44 -40,46 C -60,48 -86,40 -96,10 Z")); break;
            case 5: dc.DrawGeometry(cb, null, G("M -90,20 C -90,80 -46,108 0,110 C 46,108 90,80 90,20 C 80,60 50,74 0,76 C -50,74 -80,60 -90,20 Z")); break;
            case 6: dc.DrawGeometry(cb, null, G("M -96,10 C -96,80 -50,118 0,120 C 50,118 96,80 96,10 C 86,40 60,48 40,46 C 24,44 14,40 0,40 C -14,40 -24,44 -40,46 C -60,48 -86,40 -96,10 Z")); break;
            case 7: dc.DrawGeometry(cb, null, G("M -94,10 C -92,84 -48,110 0,112 C 48,110 92,84 94,10 L 86,10 C 84,72 46,98 0,100 C -46,98 -84,72 -86,10 Z")); break;
        }
        Push(dc, 0, my - 60);
        switch (b) {
            case 2: case 4: case 6: dc.DrawGeometry(cb, null, G("M 0,48 C -10,42 -26,44 -32,54 C -24,52 -12,54 0,56 C 12,54 24,52 32,54 C 26,44 10,42 0,48 Z")); break;
            case 3: dc.DrawGeometry(cb, null, G("M 0,48 C -12,40 -30,44 -36,52 C -42,58 -50,52 -46,44 C -50,58 -38,64 -30,58 C -20,54 -10,56 0,56 C 10,56 20,54 30,58 C 38,64 50,58 46,44 C 50,52 42,58 36,52 C 30,44 12,40 0,48 Z")); break;
        }
        if (b == 4) dc.DrawGeometry(cb, null, G("M -16,70 C -16,96 -8,104 0,106 C 8,104 16,96 16,70 C 8,76 -8,76 -16,70 Z"));
        if (b == 8) dc.DrawGeometry(cb, null, G("M -7,72 L 7,72 L 0,86 Z"));
        dc.Pop();
    }

    static void Nose(DrawingContext dc, AvatarDef d, string skin) {
        int n = d.I("nose"); if (n == 0) return;
        Push(dc, 0, 36 + d.D("noseY")); double s = d.D("noseSize"); Scale(dc, s, s);
        var dk = Shade(skin, 0.78); var mid = Shade(skin, 0.88);
        switch (n) {
            case 1: dc.DrawEllipse(dk, null, new Point(0, 0), 4.5, 3.5); break;
            case 2: dc.DrawEllipse(mid, null, new Point(0, 0), 10, 8); dc.DrawEllipse(A(Colors.White, 90), null, new Point(-3, -3), 3, 2.4); break;
            case 3: dc.DrawGeometry(null, P(dk, 3.5), G("M -7,0 Q 0,7 7,0")); break;
            case 4: dc.DrawGeometry(mid, null, G("M 0,-14 L 9,6 Q 0,9 -9,6 Z")); break;
            case 5: dc.DrawEllipse(mid, null, new Point(0, 0), 14, 12); dc.DrawEllipse(A(Colors.White, 80), null, new Point(-4, -4), 4, 3); break;
            case 6: dc.DrawGeometry(null, P(dk, 3.2), G("M 0,-24 L 5,4 Q 0,9 -7,4")); break;
            case 7: dc.DrawGeometry(null, P(dk, 3.2), G("M -2,-18 L 12,6 L -2,6")); break;
            case 8: dc.DrawEllipse(mid, null, new Point(0, 0), 16, 8); dc.DrawEllipse(dk, null, new Point(-6, 2), 3, 2); dc.DrawEllipse(dk, null, new Point(6, 2), 3, 2); break;
            case 9: dc.DrawGeometry(null, P(dk, 3.2), G("M -2,-22 Q 8,-14 4,-8 Q 10,0 6,6 Q 0,9 -6,5")); break;
        }
        dc.Pop(); dc.Pop();
    }

    static int MoodMouth(int mood, int own) {
        switch (mood) { case 1: return 2; case 2: return 3; case 3: return 5; case 4: return 9; case 5: return 7; case 6: return 4; case 7: return 10; case 8: return 10; default: return own; }
    }
    static void Mouth(DrawingContext dc, AvatarDef d, int mood) {
        int m = MoodMouth(mood, d.I("mouth"));
        string lip = d.C("lipCol");
        Push(dc, 0, 60 + d.D("mouthY")); double s = d.D("mouthSize"); Scale(dc, s, s);
        var ink = Ink; var inside = B(lip);
        switch (m) {
            case 1: dc.DrawGeometry(null, P(ink, 4.5), G("M -22,-4 Q 0,16 22,-4")); dc.DrawGeometry(null, P(ink, 3), G("M -25,-7 Q -22,-4 -24,0 M 25,-7 Q 22,-4 24,0")); break;
            case 2: dc.DrawGeometry(inside, null, G("M -18,-5 Q 0,22 18,-5 Z")); dc.DrawGeometry(B("#ffffff"), null, G("M -15,-4 L 15,-4 L 13,2 L -13,2 Z")); break;
            case 3: dc.DrawGeometry(inside, null, G("M -22,-8 Q 0,34 22,-8 Z")); dc.DrawGeometry(B("#ffffff"), null, G("M -19,-7 L 19,-7 L 17,-1 L -17,-1 Z")); dc.DrawGeometry(B("#ff7f8f"), null, G("M -10,12 Q 0,4 10,12 Q 0,20 -10,12 Z")); break;
            case 4: dc.DrawGeometry(null, P(ink, 3.5), G("M -7,-1 Q 0,5 7,-1")); break;
            case 5: dc.DrawEllipse(inside, null, new Point(0, 2), 8, 10); dc.DrawEllipse(B("#ff7f8f"), null, new Point(0, 7), 5, 3); break;
            case 6: dc.DrawLine(P(ink, 4), new Point(-11, 0), new Point(11, 0)); break;
            case 7: dc.DrawGeometry(null, P(ink, 4), G("M -14,2 Q 4,8 16,-6")); break;
            case 8: dc.DrawGeometry(null, P(ink, 3.5), G("M -13,-3 Q -6,6 0,-1 Q 6,6 13,-3")); break;
            case 9: dc.DrawGeometry(inside, null, G("M -16,-4 Q 0,16 16,-4 Z")); dc.DrawGeometry(B("#ff7f8f"), null, G("M -7,4 Q 0,18 8,4 Q 0,1 -7,4 Z")); break;
            case 10: dc.DrawGeometry(null, P(ink, 4), G("M -14,6 Q 0,-6 14,6")); break;
            case 11: dc.DrawGeometry(inside, null, G("M -9,0 Q -4,-6 0,-3 Q 4,-6 9,0 Q 0,9 -9,0 Z")); break;
            case 12: dc.DrawGeometry(null, P(ink, 4), G("M -16,-3 Q 0,12 16,-3")); dc.DrawGeometry(B("#ffffff"), P(ink, 1), G("M 6,2 L 10,1 L 8,9 Z")); break;
            case 13: dc.DrawGeometry(inside, null, G("M 0,-4 C -6,-10 -14,-4 -8,2 C -4,6 0,8 0,8 C 0,8 4,6 8,2 C 14,-4 6,-10 0,-4 Z")); break;
            default: dc.DrawGeometry(null, P(ink, 4), G("M -16,-3 Q 0,12 16,-3")); break;
        }
        dc.Pop(); dc.Pop();
        if (mood == 8) {
            // a tear for a sad face
            double ey = 12 + d.D("eyeY");
            dc.DrawGeometry(B("#7fc8f8"), null, G(F("M 46,{0} C 40,{1} 40,{2} 46,{2} C 52,{2} 52,{1} 46,{0} Z", ey + 14, ey + 26, ey + 32)));
        }
    }

    static int MoodEye(int mood, int own, int side) {
        switch (mood) {
            case 1: case 2: return 9;
            case 3: return 7;
            case 4: return side > 0 ? 9 : own;
            case 5: case 6: return 5;
            default: return own;
        }
    }
    static void Eyes(DrawingContext dc, AvatarDef d, int mood, bool blink) {
        string ec = d.C("eyeCol");
        double y = 12 + d.D("eyeY"), sp = 36 + d.D("eyeSpace"), size = d.D("eyeSize"), tilt = d.D("eyeTilt");
        for (int s = -1; s <= 1; s += 2) {
            int e = MoodEye(mood, d.I("eyes"), s);
            Push(dc, s * sp, y); Scale(dc, s * size, size); Rot(dc, tilt);
            if (blink && e != 9) dc.DrawGeometry(null, P(Ink, 3.5), G("M -12,1 Q 0,8 12,1"));
            else Eye(dc, e, ec);
            dc.Pop(); dc.Pop(); dc.Pop();
        }
    }
    // one eye, the outer corner towards +x
    static void Eye(DrawingContext dc, int e, string ec) {
        var ink = Ink; var iris = B(ec); var white = B("#ffffff");
        var almond = G("M -16,0 Q 0,-15 16,-2 Q 0,13 -16,0 Z");
        switch (e) {
            case 1: dc.DrawEllipse(white, null, new Point(0, 0), 12, 13); dc.DrawEllipse(iris, null, new Point(0, 1), 8, 9); dc.DrawEllipse(ink, null, new Point(0, 1), 4.5, 5); dc.DrawEllipse(white, null, new Point(-3, -3), 2.4, 2.4); break;
            case 2:
                dc.DrawGeometry(white, null, almond);
                dc.PushClip(almond); dc.DrawEllipse(iris, null, new Point(0, 0), 9, 9); dc.DrawEllipse(ink, null, new Point(0, 0), 4.5, 4.5); dc.Pop();
                dc.DrawEllipse(white, null, new Point(-3, -3), 2.2, 2.2);
                dc.DrawGeometry(null, P(ink, 3), G("M -17,0 Q 0,-16 17,-3"));
                break;
            case 3: dc.DrawEllipse(white, null, new Point(0, 0), 15, 18); dc.DrawEllipse(iris, null, new Point(0, 2), 11, 13); dc.DrawEllipse(ink, null, new Point(0, 3), 6, 7); dc.DrawEllipse(white, null, new Point(-4, -4), 3.5, 3.5); dc.DrawEllipse(white, null, new Point(4, 8), 1.8, 1.8); break;
            case 4:
                dc.DrawEllipse(iris, null, new Point(0, 0), 12, 15); dc.DrawEllipse(ink, null, new Point(0, 2), 6, 8);
                dc.DrawEllipse(white, null, new Point(-4, -5), 3.6, 3.6); dc.DrawEllipse(white, null, new Point(4, 6), 2, 2);
                Push(dc, 7, -7); dc.DrawGeometry(white, null, Star(4, 0.3, 4)); dc.Pop();
                break;
            case 5:
                dc.DrawGeometry(white, null, G("M -15,0 L 15,0 Q 0,12 -15,0 Z"));
                dc.PushClip(G("M -15,0 L 15,0 Q 0,12 -15,0 Z")); dc.DrawEllipse(iris, null, new Point(0, 2), 7, 7); dc.DrawEllipse(ink, null, new Point(0, 2), 3.5, 3.5); dc.Pop();
                dc.DrawLine(P(ink, 3.5), new Point(-16, 0), new Point(16, 0));
                break;
            case 6:
                dc.DrawGeometry(B("#fff6c8"), null, almond);
                dc.PushClip(almond); dc.DrawEllipse(iris, null, new Point(0, 0), 10, 10); dc.DrawEllipse(ink, null, new Point(0, 0), 2.4, 9); dc.Pop();
                dc.DrawGeometry(null, P(ink, 3), G("M -17,0 Q 0,-16 18,-6"));
                break;
            case 7: dc.DrawEllipse(white, P(ink, 1.5), new Point(0, 0), 15, 15); dc.DrawEllipse(iris, null, new Point(0, 0), 5.5, 5.5); dc.DrawEllipse(ink, null, new Point(0, 0), 3, 3); break;
            case 8: dc.DrawEllipse(ink, null, new Point(0, 0), 12, 3.5); break;
            case 9: dc.DrawGeometry(null, P(ink, 4), G("M -11,4 Q 0,-9 11,4")); break;
            case 10: dc.DrawGeometry(iris, null, Star(13, 0.45, 5)); dc.DrawEllipse(white, null, new Point(-3, -3), 2, 2); break;
            case 11:
                dc.DrawEllipse(white, null, new Point(0, 0), 11, 12); dc.DrawEllipse(iris, null, new Point(0, 1), 7.5, 8.5); dc.DrawEllipse(ink, null, new Point(0, 1), 4, 4.5); dc.DrawEllipse(white, null, new Point(-3, -3), 2.2, 2.2);
                dc.DrawGeometry(null, P(ink, 2.6), G("M 6,-10 L 12,-17 M 10,-6 L 18,-11 M 1,-12 L 4,-19"));
                break;
            case 12:
                var droop = G("M -16,-4 Q 0,-14 16,4 Q 0,12 -16,-4 Z");
                dc.DrawGeometry(white, null, droop);
                dc.PushClip(droop); dc.DrawEllipse(iris, null, new Point(0, 0), 8, 8); dc.DrawEllipse(ink, null, new Point(0, 0), 4, 4); dc.Pop();
                dc.DrawGeometry(null, P(ink, 3), G("M -17,-4 Q 0,-15 17,4"));
                break;
            case 13:
                var sharp = G("M -16,2 L -6,-9 L 16,-6 L 8,7 Z");
                dc.DrawGeometry(white, null, sharp);
                dc.PushClip(sharp); dc.DrawEllipse(iris, null, new Point(0, -1), 8, 8); dc.DrawEllipse(ink, null, new Point(0, -1), 4, 4); dc.Pop();
                dc.DrawGeometry(null, P(ink, 4), G("M -17,2 L -6,-10 L 18,-7"));
                break;
            case 14: dc.DrawEllipse(ink, null, new Point(0, 0), 7, 7); dc.DrawEllipse(white, null, new Point(-2, -2), 2, 2); break;
            case 15: Push(dc, 0, 0); dc.DrawGeometry(iris, null, Heart(11)); dc.Pop(); dc.DrawEllipse(white, null, new Point(-4, -4), 2, 2); break;
            default: dc.DrawEllipse(B(ec), null, new Point(0, 0), 8, 11); dc.DrawEllipse(white, null, new Point(-2, -4), 2, 2); break;
        }
    }

    static void Brows(DrawingContext dc, AvatarDef d, int mood) {
        int b = d.I("brows"); if (b == 0) return;
        var c = B(d.C("browCol"));
        double y = -22 + d.D("eyeY") * 0.6 + d.D("browY") - (mood == 3 ? 8 : 0);
        double sp = 36 + d.D("eyeSpace") + d.D("browSpace"), size = d.D("browSize");
        double tilt = d.D("browTilt") + (mood == 7 ? 18 : mood == 8 ? -14 : 0);
        if (b == 11) {
            Push(dc, 0, y); Scale(dc, size, size);
            dc.DrawGeometry(null, P(c, 7), G(F("M {0},2 Q {1},-6 0,0 Q {2},-6 {3},2", -sp - 14, -sp, sp, sp + 14)));
            dc.Pop(); dc.Pop();
            return;
        }
        for (int s = -1; s <= 1; s += 2) {
            // mirrored, so +x is towards the outside and a positive tilt drops the inner end
            Push(dc, s * sp, y); Scale(dc, s * size, size); Rot(dc, -tilt);
            switch (b) {
                case 1: dc.DrawGeometry(null, P(c, 3), G("M -13,2 Q 0,-5 13,1")); break;
                case 2: dc.DrawGeometry(null, P(c, 5), G("M -13,2 Q 0,-5 13,1")); break;
                case 3: dc.DrawRoundedRectangle(c, null, new Rect(-14, -5, 28, 9), 4.5, 4.5); break;
                case 4: dc.DrawGeometry(c, null, G("M -15,4 L -14,-4 L -9,-2 L -6,-7 L -1,-3 L 3,-8 L 7,-3 L 11,-7 L 15,-1 L 15,4 Z")); break;
                case 5: dc.DrawGeometry(null, P(c, 4.5), G("M -13,4 Q -2,-10 14,0")); break;
                case 6: dc.DrawLine(P(c, 5), new Point(-13, 0), new Point(13, 0)); break;
                case 7: dc.DrawLine(P(c, 5), new Point(-12, 4), new Point(13, -4)); break;
                case 8: dc.DrawLine(P(c, 5), new Point(-12, -4), new Point(13, 3)); break;
                case 9: dc.DrawRoundedRectangle(c, null, new Rect(-7, -4, 14, 8), 4, 4); break;
                case 10: dc.DrawEllipse(c, null, new Point(-4, 0), 5, 4); break;
            }
            dc.Pop(); dc.Pop(); dc.Pop();
        }
    }

    static void Earrings(DrawingContext dc, AvatarDef d, Layout L) {
        int e = d.I("earrings"); if (e == 0) return;
        var c = B(d.C("earCol"));
        for (int s = -1; s <= 1; s += 2) {
            var p = new Point(s * (L.SideW + 4), 34);
            switch (e) {
                case 1: dc.DrawEllipse(c, null, p, 4.5, 4.5); break;
                case 2: dc.DrawEllipse(null, P(c, 3), new Point(p.X, p.Y + 9), 8, 9); break;
                case 3: Push(dc, p.X, p.Y + 8); dc.DrawGeometry(c, null, Star(8, 0.45, 5)); dc.Pop(); break;
            }
        }
    }

    // ---------- hair over the forehead ----------
    // the outer edge, from the left side (-98, sy) over the top to the right side (98, sy)
    static string Outer(int kind, int sy) {
        switch (kind) {
            case 1: return "C -108,-70 -60,-112 0,-112 C 60,-112 108,-70 98," + sy;
            case 2: return "L -112,-40 L -128,-64 L -102,-76 L -110,-112 L -70,-104 L -60,-142 L -26,-118 L 0,-150 L 22,-118 L 58,-140 L 66,-104 L 108,-112 L 100,-76 L 126,-62 L 110,-40 L 98," + sy;
            case 3: return "Q -122,-20 -114,-50 Q -132,-80 -100,-96 Q -98,-128 -62,-124 Q -40,-146 -10,-130 Q 14,-148 40,-130 Q 74,-140 86,-112 Q 120,-108 112,-74 Q 130,-46 112,-24 Q 112,-4 98," + sy;
            case 4: return "C -118,-70 -80,-118 -20,-124 C 10,-162 74,-160 88,-128 C 112,-108 116,-70 98," + sy;
            case 5: return "Q -120,-30 -112,-60 Q -130,-90 -96,-100 Q -100,-134 -56,-124 Q -30,-150 0,-128 Q 30,-152 56,-124 Q 100,-134 96,-100 Q 130,-90 112,-60 Q 120,-30 98," + sy;
            default: return "C -118,-70 -66,-124 0,-124 C 66,-124 118,-70 98," + sy;
        }
    }
    // the hairline, from the right side (98, sy) across the forehead to the left side (-98, sy)
    static string Inner(int kind, int sy) {
        switch (kind) {
            case 1: return "C 98,-30 90,-38 80,-38 L -80,-38 C -90,-38 -98,-30 -98," + sy;
            case 2: return "C 96,-40 80,-56 40,-62 C 20,-50 -20,-30 -60,-34 C -90,-38 -98,-10 -98," + sy;
            case 3: return "C 96,-30 70,-58 20,-58 C -20,-50 -60,-20 -96,-14 L -98," + sy;
            case 4: return "C 98,-20 80,-50 40,-46 C 20,-44 6,-60 0,-70 C -6,-60 -20,-44 -40,-46 C -80,-50 -98,-20 -98," + sy;
            case 5: return "L 90,-30 L 72,-48 L 60,-30 L 40,-56 L 24,-34 L 6,-58 L -12,-36 L -30,-58 L -46,-34 L -64,-52 L -76,-30 L -92,-40 L -98," + sy;
            case 6: return "Q 104,-40 80,-44 Q 76,-66 50,-56 Q 34,-74 12,-58 Q -6,-76 -26,-58 Q -46,-72 -60,-52 Q -84,-60 -86,-38 Q -104,-30 -98," + sy;
            case 7: return "C 94,-50 60,-72 0,-74 C -60,-72 -94,-50 -98," + sy;
            default: return "C 96,-40 70,-62 30,-64 C 10,-58 -10,-58 -30,-64 C -70,-62 -96,-40 -98," + sy;
        }
    }
    // style: (outer, inner, side length); -1 for no hair over the forehead
    static readonly int[][] HairFront = {
        null,                      // bald
        new[] { 1, 7, -10 },       // buzz
        new[] { 0, 0, 10 },        // short
        new[] { 2, 5, 6 },         // spiky
        new[] { 0, 3, 10 },        // swept
        new[] { 0, 2, 10 },        // side part
        new[] { 0, 4, 22 },        // curtains
        new[] { 3, 6, 10 },        // curly
        new[] { 3, 6, 14 },        // afro
        null,                      // mohawk (drawn on its own)
        new[] { 0, 3, 30 },        // pixie
        new[] { 0, 1, 40 },        // bob
        new[] { 0, 2, 40 },        // long
        new[] { 0, 4, 40 },        // wavy
        new[] { 0, 3, 10 },        // ponytail
        new[] { 0, 0, 10 },        // high ponytail
        new[] { 0, 1, 14 },        // twin tails
        new[] { 0, 4, 10 },        // bun
        new[] { 0, 1, 10 },        // space buns
        new[] { 0, 2, 20 },        // braids
        new[] { 5, 5, 10 },        // messy
        new[] { 4, 0, 10 },        // quiff
    };
    static void FrontHair(DrawingContext dc, AvatarDef d, Layout L) {
        int h = d.I("hair");
        var hc = Col(d.C("hairCol")); var hair = B(hc);
        bool flip = d.I("hairFlip") == 1;
        if (flip) Scale(dc, -1, 1);
        Scale(dc, L.SideW / 100.0, 1);
        if (h == 9) {
            dc.DrawGeometry(A(hc, 90), null, G("M -98,-10 " + Outer(1, -10) + " " + Inner(7, -10) + " Z"));
            dc.DrawGeometry(hair, null, G("M -22,-60 C -26,-110 -16,-152 0,-172 C 16,-152 26,-110 22,-60 C 10,-66 -10,-66 -22,-60 Z"));
        } else if (HairFront[h] != null) {
            var f = HairFront[h];
            var path = "M -98," + f[2] + " " + Outer(f[0], f[2]) + " " + Inner(f[1], f[2]) + " Z";
            dc.DrawGeometry(h == 1 ? A(hc, 150) : hair, null, G(path));
            if (h != 1) dc.DrawGeometry(null, P(A(Shade(hc, 1.5), 110), 7), G("M -52,-100 Q -10,-118 34,-106"));
        }
        dc.Pop();
        if (flip) dc.Pop();
    }
    // twin tails and braids hang in front of the shoulders
    static void FrontTails(DrawingContext dc, AvatarDef d, Layout L) {
        int h = d.I("hair"); if (h != 16 && h != 19) return;
        EnterHead(dc, L);
        var hc = Col(d.C("hairCol")); var hair = B(hc);
        bool flip = d.I("hairFlip") == 1;
        if (flip) Scale(dc, -1, 1);
        for (int s = -1; s <= 1; s += 2) {
            Push(dc, s * (L.SideW - 2), 0); Scale(dc, s, 1);
            if (h == 16) {
                dc.DrawGeometry(hair, null, G("M 0,-50 C 50,-40 60,60 44,150 C 40,172 22,170 24,146 C 34,80 24,10 -6,-20 Z"));
                dc.DrawEllipse(B("#f0453a"), null, new Point(6, -40), 10, 8);
            } else {
                for (int i = 0; i < 6; i++) dc.DrawEllipse(hair, P(A(Shade(hc, 0.75), 255), 1.5), new Point(8, 10 + i * 30), 15, 18);
                dc.DrawEllipse(B("#f0453a"), null, new Point(8, 186), 7, 6);
            }
            dc.Pop(); dc.Pop();
        }
        if (flip) dc.Pop();
        LeaveHead(dc);
    }

    // ---------- glasses ----------
    static void Glasses(DrawingContext dc, AvatarDef d) {
        int g = d.I("glasses"); if (g == 0) return;
        var fc = B(d.C("glassCol"));
        double y = 12 + d.D("eyeY") + d.D("glassY"), sp = 36 + d.D("eyeSpace"), k = d.D("glassSize");
        var frame = P(fc, 4);
        if (g == 8) {
            dc.DrawLine(P(fc, 10), new Point(-100, y - 4), new Point(100, y - 4));
        }
        for (int s = -1; s <= 1; s += 2) {
            if (g == 9 && s < 0) continue;
            Push(dc, s * sp, y); Scale(dc, s * k, k);
            switch (g) {
                case 1: case 9: dc.DrawEllipse(B(Color.FromArgb(40, 255, 255, 255)), frame, new Point(0, 0), 22, 22); break;
                case 2: dc.DrawRoundedRectangle(B(Color.FromArgb(40, 255, 255, 255)), frame, new Rect(-24, -18, 48, 36), 7, 7); break;
                case 3: dc.DrawRoundedRectangle(null, P(fc, 2.4), new Rect(-22, -14, 44, 28), 10, 10); break;
                case 4: dc.DrawGeometry(B(Color.FromArgb(40, 255, 255, 255)), frame, G("M -22,-6 Q -20,18 0,18 Q 22,18 24,0 Q 30,-20 30,-24 Q 10,-14 -22,-6 Z")); break;
                case 5: dc.DrawRoundedRectangle(B("#1d1a24"), frame, new Rect(-24, -15, 48, 30), 11, 11); dc.DrawLine(P(B(Color.FromArgb(150, 255, 255, 255)), 3.5), new Point(-14, -6), new Point(-4, -6)); break;
                case 6: dc.DrawGeometry(B(Color.FromArgb(150, 90, 70, 40)), P(fc, 2.6), G("M -22,-14 L 22,-14 Q 26,6 8,18 Q -6,24 -18,12 Q -26,0 -22,-14 Z")); break;
                case 7: dc.DrawGeometry(B(Color.FromArgb(60, 255, 255, 255)), frame, Star(26, 0.55, 5)); break;
                case 8: dc.DrawEllipse(B(Color.FromArgb(110, 120, 200, 255)), P(fc, 7), new Point(0, -2), 24, 22); break;
            }
            dc.Pop(); dc.Pop();
        }
        if (g == 1 || g == 2 || g == 4 || g == 5 || g == 7) dc.DrawGeometry(null, P(fc, 3.5), G(F("M {0},{1} Q 0,{2} {3},{1}", -sp + 22 * k, y - 4, y - 10, sp - 22 * k)));
        if (g == 3 || g == 6) dc.DrawLine(P(fc, 2.4), new Point(-sp + 22 * k, y - 4), new Point(sp - 22 * k, y - 4));
        if (g == 9) dc.DrawGeometry(null, P(B("#c98f12"), 1.5), G(F("M {0},{1} Q {2},{3} {4},{5}", sp + 20 * k, y + 8, sp + 40, y + 60, sp + 10, y + 96)));
    }

    // ---------- hats ----------
    static void Hat(DrawingContext dc, AvatarDef d, Layout L) {
        int h = d.I("hat"); if (h == 0) return;
        string hc = d.C("hatCol"); var c = B(hc); var dk = Shade(hc, 0.78); var lt = Shade(hc, 1.3);
        Scale(dc, L.SideW / 100.0, 1);
        switch (h) {
            case 1:
                dc.DrawGeometry(c, null, G("M -104,-36 C -108,-130 108,-130 104,-36 Z"));
                dc.DrawGeometry(dk, null, G("M -108,-40 C -60,-56 60,-56 108,-40 C 112,-24 94,-18 0,-20 C -94,-18 -112,-24 -108,-40 Z"));
                dc.DrawEllipse(dk, null, new Point(0, -118), 8, 6);
                dc.DrawGeometry(B("#ffffff"), null, G("M -20,-96 L 20,-96 L 16,-72 L -16,-72 Z"));
                break;
            case 2:
                dc.DrawGeometry(c, null, G("M -104,-36 C -108,-130 108,-130 104,-36 Z"));
                dc.DrawRoundedRectangle(dk, null, new Rect(-104, -48, 208, 14), 7, 7);
                dc.DrawGeometry(B("#ffffff"), null, G("M -12,-48 Q 0,-66 12,-48 Z"));
                dc.DrawEllipse(dk, null, new Point(0, -118), 8, 6);
                break;
            case 3:
                dc.DrawGeometry(c, null, G("M -106,-30 C -112,-150 112,-150 106,-30 Z"));
                dc.DrawRoundedRectangle(dk, null, new Rect(-112, -54, 224, 30), 14, 14);
                for (int x = -96; x <= 96; x += 12) dc.DrawLine(P(Shade(hc, 0.68), 2), new Point(x, -50), new Point(x, -28));
                dc.DrawEllipse(lt, null, new Point(0, -142), 22, 22);
                break;
            case 4:
                dc.DrawGeometry(B("#f2c230"), P(B("#c98f12"), 3), G("M -60,-74 L -64,-150 L -30,-112 L 0,-162 L 30,-112 L 64,-150 L 60,-74 Z"));
                foreach (var p in new[] { new Point(-34, -90), new Point(0, -92), new Point(34, -90) }) dc.DrawEllipse(B(hc), null, p, 7, 7);
                break;
            case 5: dc.DrawRoundedRectangle(c, null, new Rect(-104, -70, 208, 22), 11, 11); dc.DrawLine(P(dk, 2), new Point(-100, -59), new Point(100, -59)); break;
            case 6:
                Push(dc, 60, -96); Rot(dc, 18);
                dc.DrawGeometry(c, null, G("M 0,0 C -20,-34 -60,-28 -52,4 C -46,30 -16,16 0,0 Z M 0,0 C 20,-34 60,-28 52,4 C 46,30 16,16 0,0 Z"));
                dc.DrawEllipse(dk, null, new Point(0, 0), 11, 11);
                dc.Pop(); dc.Pop();
                break;
            case 7:
                Rot(dc, -10);
                dc.DrawEllipse(c, null, new Point(10, -96), 112, 40);
                dc.DrawEllipse(dk, null, new Point(10, -70), 96, 12);
                dc.DrawLine(P(dk, 6), new Point(10, -136), new Point(14, -150));
                dc.Pop();
                break;
            case 8:
                dc.DrawEllipse(dk, null, new Point(0, -60), 170, 30);
                dc.DrawEllipse(c, null, new Point(0, -64), 160, 22);
                dc.DrawGeometry(c, null, G("M -76,-64 C -84,-150 -40,-160 0,-140 C 40,-160 84,-150 76,-64 Z"));
                dc.DrawRectangle(Shade(hc, 0.6), null, new Rect(-78, -88, 156, 14));
                break;
            case 9:
                dc.DrawEllipse(dk, null, new Point(0, -70), 130, 20);
                dc.DrawRectangle(c, null, new Rect(-66, -200, 132, 132));
                dc.DrawEllipse(lt, null, new Point(0, -200), 66, 12);
                dc.DrawRectangle(B("#f0453a"), null, new Rect(-66, -96, 132, 18));
                break;
            case 10:
                dc.DrawEllipse(dk, null, new Point(0, -64), 136, 24);
                dc.DrawGeometry(c, null, G("M -86,-66 C -40,-120 -10,-190 30,-236 C 34,-200 60,-120 86,-66 C 30,-50 -30,-50 -86,-66 Z"));
                foreach (var p in new[] { new Point(-24, -110), new Point(22, -150), new Point(30, -96), new Point(4, -196) }) { Push(dc, p.X, p.Y); dc.DrawGeometry(B("#f2d24b"), null, Star(9, 0.45, 5)); dc.Pop(); }
                break;
            case 11:
                for (int s = -1; s <= 1; s += 2) {
                    Push(dc, s * 36, -90); Rot(dc, s * 10);
                    dc.DrawEllipse(B("#ffffff"), P(B("#e6e6e6"), 2), new Point(0, -70), 20, 70);
                    dc.DrawEllipse(c, null, new Point(0, -64), 9, 52);
                    dc.Pop(); dc.Pop();
                }
                break;
            case 12:
                for (int s = -1; s <= 1; s += 2) {
                    Push(dc, s * 60, -84); Scale(dc, s, 1);
                    dc.DrawGeometry(B(d.C("hairCol")), null, G("M -30,6 L 6,-60 L 34,10 Z"));
                    dc.DrawGeometry(c, null, G("M -16,2 L 6,-38 L 22,6 Z"));
                    dc.Pop(); dc.Pop();
                }
                break;
            case 13:
                dc.DrawGeometry(null, P(dk, 14), G("M -104,0 C -110,-150 110,-150 104,0"));
                for (int s = -1; s <= 1; s += 2) { dc.DrawRoundedRectangle(c, null, new Rect(s * 104 - 20, -18, 40, 56), 16, 16); dc.DrawRoundedRectangle(lt, null, new Rect(s * 104 - 10, -6, 20, 32), 8, 8); }
                break;
            case 14:
                Push(dc, 70, -70);
                for (int i = 0; i < 5; i++) { Rot(dc, i * 72); dc.DrawEllipse(c, null, new Point(0, -14), 10, 15); dc.Pop(); }
                dc.DrawEllipse(B("#f2d24b"), null, new Point(0, 0), 9, 9);
                dc.Pop();
                break;
        }
        dc.Pop();
    }
}
}
