// The characters in 3D: built from the same AvatarDef as before (every part, colour and slider of the creator), as
// lit, rounded 3D shapes: a head (its face shape bends the ball), real 3D hair and hats, ears, a body with sleeves,
// trousers or a skirt, shoes, scarves, capes and wings. The face itself (eyes, brows, nose, mouth, beard, glasses,
// freckles) is drawn flat by AvatarArt and printed on the front of the head, so all the face choices work as before.
//
// A skeleton moves it: the body (bob, lean, squash and stretch), the head (nod, turn, tilt), each arm at the shoulder
// and elbow, each leg at the hip and knee. Animations: idle (breathing, a little sway, blinking), walk, wave, cheer,
// jump, held (picked up: kicking), dazed, and the character's own pose. Avatar3DView plays them live (the creator);
// Avatar3D.Render makes still pictures (profile pictures, the plaza's walking frames, thumbnails).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace RecompLauncher {

enum Anim3D { Idle, Walk, Wave, Cheer, Jump, Held, Dazed, Pose }

class Pose3D {
    public double PUp, PFore, NUp, NFore;      // arms in the front plane (degrees out from hanging down): P the arm on +X (screen right), N on -X
    public double PSwing, NSwing;              // arms forward (+) or back
    public double PLeg, NLeg, PKnee, NKnee;    // legs forward (+) or back; knees bent (+)
    public double HeadX, HeadY, HeadZ;         // nod (+ down), turn, tilt
    public double Bob, Lean, Squash = 1, Turn; // body up, lean forward, squash (<1) or stretch (>1), turn round (degrees)
    public bool Blink;
}

// ---------- shapes ----------
static class Mesh3 {
    // a surface of revolution round the Y axis: profile points (radius, height) from top to bottom; u goes round
    // (0.5 is the front, +Z), v down the profile
    public static MeshGeometry3D Lathe(IList<Point> profile, int slices, double zScale = 1, double fromU = 0, double toU = 1) {
        var m = new MeshGeometry3D();
        int rows = profile.Count;
        double total = 0; var len = new double[rows];
        for (int i = 1; i < rows; i++) { total += (profile[i] - profile[i - 1]).Length; len[i] = total; }
        for (int i = 0; i < rows; i++)
            for (int j = 0; j <= slices; j++) {
                double u = fromU + (toU - fromU) * j / slices, phi = 2 * Math.PI * u;
                double r = profile[i].X;
                m.Positions.Add(new Point3D(-r * Math.Sin(phi), profile[i].Y, -r * Math.Cos(phi) * zScale));
                m.TextureCoordinates.Add(new Point(u, total > 0 ? len[i] / total : 0));
            }
        for (int i = 0; i < rows - 1; i++)
            for (int j = 0; j < slices; j++) {
                int a = i * (slices + 1) + j, b = a + 1, c = a + slices + 1, d = c + 1;
                m.TriangleIndices.Add(a); m.TriangleIndices.Add(c); m.TriangleIndices.Add(b);
                m.TriangleIndices.Add(b); m.TriangleIndices.Add(c); m.TriangleIndices.Add(d);
            }
        Finish(m, p => new Point3D(0, p.Y, 0));
        return m;
    }
    // a ball (or an egg), bent by an optional function, keeping only the triangles a test lets through (on the unit
    // ball: x right, y up, z front); planar texture: u, v from x, y across +-uvSpan
    public static MeshGeometry3D Ball(Point3D c, double rx, double ry, double rz, int slices = 32, int stacks = 24,
            Func<Vector3D, Vector3D> bend = null, Func<Vector3D, bool> keep = null, double uvSpan = 0, Func<Vector3D, double> sink = null) {
        var m = new MeshGeometry3D();
        var unit = new List<Vector3D>();
        for (int i = 0; i <= stacks; i++) {
            double th = Math.PI * i / stacks;
            for (int j = 0; j <= slices; j++) {
                double ph = 2 * Math.PI * j / slices;
                var n = new Vector3D(-Math.Sin(th) * Math.Sin(ph), Math.Cos(th), -Math.Sin(th) * Math.Cos(ph));
                unit.Add(n);
                var v = bend != null ? bend(n) : n;
                // sink: pulled in (into the head under it), so the edge is where the two surfaces meet, a smooth line
                if (sink != null) v *= sink(n);
                var p = new Point3D(c.X + v.X * rx, c.Y + v.Y * ry, c.Z + v.Z * rz);
                m.Positions.Add(p);
                m.TextureCoordinates.Add(uvSpan > 0 ? new Point(0.5 + (p.X - c.X) / (2 * uvSpan), 0.5 - (p.Y - c.Y) / (2 * uvSpan)) : new Point((double)j / slices, (double)i / stacks));
            }
        }
        for (int i = 0; i < stacks; i++)
            for (int j = 0; j < slices; j++) {
                int a = i * (slices + 1) + j, b = a + 1, cc = a + slices + 1, d = cc + 1;
                foreach (var t in new[] { new[] { a, cc, b }, new[] { b, cc, d } }) {
                    if (keep != null) { var mid = unit[t[0]] + unit[t[1]] + unit[t[2]]; mid.Normalize(); if (!keep(mid)) continue; }
                    m.TriangleIndices.Add(t[0]); m.TriangleIndices.Add(t[1]); m.TriangleIndices.Add(t[2]);
                }
            }
        Finish(m, p => c);
        return m;
    }
    public static MeshGeometry3D Torus(double ring, double tube, int segs = 28, int sides = 12, double arc = 1) {
        var m = new MeshGeometry3D();
        for (int i = 0; i <= segs; i++) {
            double a = 2 * Math.PI * arc * i / segs;
            for (int j = 0; j <= sides; j++) {
                double b = 2 * Math.PI * j / sides;
                double r = ring + tube * Math.Cos(b);
                m.Positions.Add(new Point3D(r * Math.Cos(a), tube * Math.Sin(b), r * Math.Sin(a)));
            }
        }
        for (int i = 0; i < segs; i++)
            for (int j = 0; j < sides; j++) {
                int a = i * (sides + 1) + j, b = a + 1, c = a + sides + 1, d = c + 1;
                m.TriangleIndices.Add(a); m.TriangleIndices.Add(c); m.TriangleIndices.Add(b);
                m.TriangleIndices.Add(b); m.TriangleIndices.Add(c); m.TriangleIndices.Add(d);
            }
        Finish(m, p => { var v = new Vector3D(p.X, 0, p.Z); if (v.Length < 1e-9) return new Point3D(0, 0, 0); v.Normalize(); return new Point3D(v.X * ring, 0, v.Z * ring); });
        return m;
    }
    // a capsule hanging down from the origin: radius r1 at the top, r2 at the bottom, len between the two centres
    public static MeshGeometry3D Capsule(double r1, double r2, double len, int slices = 18) {
        var pr = new List<Point>();
        for (int k = 0; k <= 6; k++) { double a = Math.PI / 2 * (1 - k / 6.0); pr.Add(new Point(Math.Max(0.0005, r1 * Math.Cos(a)), r1 * Math.Sin(a))); }
        for (int k = 0; k <= 6; k++) { double a = Math.PI / 2 * k / 6.0; pr.Add(new Point(Math.Max(0.0005, r2 * Math.Cos(a)), -len - r2 * Math.Sin(a))); }
        return Lathe(pr, slices);
    }
    // the triangles all face out (from the inside point the function gives for each), then smooth normals, the same
    // where the surface meets itself at a seam
    static void Finish(MeshGeometry3D m, Func<Point3D, Point3D> inside) {
        var P = m.Positions; var T = m.TriangleIndices;
        for (int i = 0; i + 2 < T.Count; i += 3) {
            Point3D a = P[T[i]], b = P[T[i + 1]], c = P[T[i + 2]];
            var n = Vector3D.CrossProduct(b - a, c - a);
            var mid = new Point3D((a.X + b.X + c.X) / 3, (a.Y + b.Y + c.Y) / 3, (a.Z + b.Z + c.Z) / 3);
            if (Vector3D.DotProduct(n, mid - inside(mid)) < 0) { int t = T[i + 1]; T[i + 1] = T[i + 2]; T[i + 2] = t; }
        }
        var acc = new Vector3D[P.Count];
        for (int i = 0; i + 2 < T.Count; i += 3) {
            Point3D a = P[T[i]], b = P[T[i + 1]], c = P[T[i + 2]];
            var n = Vector3D.CrossProduct(b - a, c - a);
            acc[T[i]] += n; acc[T[i + 1]] += n; acc[T[i + 2]] += n;
        }
        var weld = new Dictionary<Tuple<long, long, long>, Vector3D>();
        Func<Point3D, Tuple<long, long, long>> key = p => Tuple.Create((long)Math.Round(p.X * 20000), (long)Math.Round(p.Y * 20000), (long)Math.Round(p.Z * 20000));
        for (int i = 0; i < P.Count; i++) { var k = key(P[i]); Vector3D v; weld[k] = weld.TryGetValue(k, out v) ? v + acc[i] : acc[i]; }
        for (int i = 0; i < P.Count; i++) {
            var n = weld[key(P[i])];
            if (n.Length < 1e-12) n = P[i] - inside(P[i]);
            if (n.Length > 1e-12) n.Normalize();
            m.Normals.Add(n);
        }
        m.Freeze();
    }
}

// ---------- the character, built and posed ----------
class Rig3D {
    public readonly Model3DGroup Root = new Model3DGroup();
    public AvatarDef Def;
    public double R, FootY, HipY, ShoulderY, NeckY, HeadY, Top, ShoulderX, HipX, Upper, Fore, Thigh, Shin, BodyW;
    readonly AxisAngleRotation3D turn = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0), lean = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
    readonly AxisAngleRotation3D hx = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0), hy = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0), hz = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
    readonly AxisAngleRotation3D[] armZ = new AxisAngleRotation3D[2], armX = new AxisAngleRotation3D[2], foreZ = new AxisAngleRotation3D[2], legX = new AxisAngleRotation3D[2], kneeX = new AxisAngleRotation3D[2];
    readonly TranslateTransform3D bob = new TranslateTransform3D();
    readonly ScaleTransform3D squash = new ScaleTransform3D(1, 1, 1);
    DiffuseMaterial face; Brush faceOpen, faceShut;
    public Model3DGroup Body = new Model3DGroup(), Head = new Model3DGroup();
    public readonly Model3DGroup[] Arm = { new Model3DGroup(), new Model3DGroup() }, Forearm = { new Model3DGroup(), new Model3DGroup() },
        Leg = { new Model3DGroup(), new Model3DGroup() }, Shin3 = { new Model3DGroup(), new Model3DGroup() };

    public void Apply(Pose3D p) {
        turn.Angle = p.Turn; lean.Angle = p.Lean;
        bob.OffsetY = p.Bob;
        squash.ScaleY = p.Squash; squash.ScaleX = squash.ScaleZ = 1 / Math.Sqrt(Math.Max(0.5, p.Squash));
        hx.Angle = p.HeadX; hy.Angle = p.HeadY; hz.Angle = p.HeadZ;
        // [0] the arm on +X (screen right), [1] the one on -X
        armZ[0].Angle = p.PUp; foreZ[0].Angle = p.PFore - p.PUp; armX[0].Angle = -p.PSwing;
        armZ[1].Angle = -p.NUp; foreZ[1].Angle = -(p.NFore - p.NUp); armX[1].Angle = -p.NSwing;
        legX[0].Angle = -p.PLeg; legX[1].Angle = -p.NLeg; kneeX[0].Angle = p.PKnee; kneeX[1].Angle = p.NKnee;
        if (face != null) face.Brush = p.Blink && faceShut != null ? faceShut : faceOpen;
    }

    // ---------- building ----------
    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    static Color C(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
    static Material Mat(string hex, double gloss) {
        string k = hex + "@" + gloss;
        Material m;
        if (mats.TryGetValue(k, out m)) return m;
        var g = new MaterialGroup();
        var b = new SolidColorBrush(C(hex)); b.Freeze();
        g.Children.Add(new DiffuseMaterial(b));
        if (gloss > 0) { var s = new SolidColorBrush(Color.FromArgb((byte)(255 * gloss), 255, 255, 255)); s.Freeze(); g.Children.Add(new SpecularMaterial(s, 26)); }
        g.Freeze(); mats[k] = g;
        return g;
    }
    static GeometryModel3D Geo(MeshGeometry3D m, Material mat, bool twoSided = false) {
        var g = new GeometryModel3D(m, mat); if (twoSided) g.BackMaterial = mat; return g;
    }
    static Transform3D Joint(Point3D pivot, params AxisAngleRotation3D[] rots) {
        var t = new Transform3DGroup();
        foreach (var r in rots) t.Children.Add(new RotateTransform3D(r));
        t.Children.Add(new TranslateTransform3D(pivot.X, pivot.Y, pivot.Z));
        return t;
    }
    static Transform3D Place(double x, double y, double z, double rx = 0, double ry = 0, double rz = 0) {
        var t = new Transform3DGroup();
        if (rz != 0) t.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), rz)));
        if (rx != 0) t.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), rx)));
        if (ry != 0) t.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), ry)));
        t.Children.Add(new TranslateTransform3D(x, y, z));
        return t;
    }
    static GeometryModel3D At(GeometryModel3D g, Transform3D t) { g.Transform = t; return g; }

    public static Rig3D Build(AvatarDef d, int mood, bool shadow, int facePx = 512) {
        var r = new Rig3D { Def = d };
        double hgt = d.D("height"), bld = d.D("build"), hs = d.D("headSize");
        double gloss = d.I("style") == 1 ? 0.35 : 0.18;
        r.R = 1.0 * hs;
        r.FootY = 0.14; r.Thigh = 0.56 * hgt; r.Shin = 0.52 * hgt; r.HipY = r.FootY + r.Thigh + r.Shin;
        r.ShoulderY = r.HipY + 1.12 * hgt; r.NeckY = r.ShoulderY + 0.16; r.HeadY = r.NeckY + 0.86 * r.R;
        r.BodyW = 0.6 * bld; r.ShoulderX = r.BodyW - 0.02; r.HipX = 0.26 * bld;
        r.Upper = 0.52 * hgt; r.Fore = 0.5 * hgt;
        string skin = d.C("skin"), top = d.C("topCol"), top2 = d.C("topCol2"), bot = d.C("bottomCol"), shoe = d.C("shoeCol");
        int topK = d.I("top"), botK = d.I("bottom"), shoeK = d.I("shoes");

        // the skeleton
        var body = r.Body; body.Transform = Joint(new Point3D(0, 0, 0)); // placed by root
        var root = new Model3DGroup();
        var rootT = new Transform3DGroup();
        rootT.Children.Add(r.squash);
        rootT.Children.Add(new RotateTransform3D(r.lean, new Point3D(0, r.HipY, 0)));
        rootT.Children.Add(new RotateTransform3D(r.turn));
        rootT.Children.Add(r.bob);
        root.Transform = rootT;
        r.Root.Children.Add(root);
        root.Children.Add(body);

        // ---------- the body ----------
        double w = r.BodyW, sy = r.ShoulderY, hy = r.HipY, depth = 0.74;
        bool dress = topK == 8, overalls = topK == 9, tank = topK == 5;
        var torsoProfile = new List<Point> {
            new Point(0.001, sy + 0.12), new Point(w * 0.5, sy + 0.1), new Point(w * 0.86, sy + 0.02), new Point(w, sy - 0.14),
            new Point(w * 0.97, sy - 0.45), new Point(w * 0.88, sy - 0.75), new Point(w * 0.9, hy + 0.2), new Point(w * 0.86, hy - 0.02), new Point(w * 0.6, hy - 0.12), new Point(0.001, hy - 0.16) };
        var torsoTex = new ImageBrush(TorsoTexture(d)) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) }; torsoTex.Freeze();
        var tm = new MaterialGroup(); tm.Children.Add(new DiffuseMaterial(torsoTex));
        if (gloss > 0) { var sb = new SolidColorBrush(Color.FromArgb((byte)(255 * gloss * 0.6), 255, 255, 255)); sb.Freeze(); tm.Children.Add(new SpecularMaterial(sb, 20)); }
        body.Children.Add(Geo(Mesh3.Lathe(torsoProfile, 36, depth), tm));
        // the neck
        body.Children.Add(At(Geo(Mesh3.Capsule(0.17, 0.17, 0.22), Mat(skin, 0.1)), Place(0, r.NeckY + 0.04, 0)));
        // a hoodie's hood, lying behind the neck
        if (topK == 2) body.Children.Add(At(Geo(Mesh3.Torus(0.36, 0.13), Mat(top, gloss)), Place(0, sy + 0.08, -0.12, rx: -18)));
        // skirt or dress
        if (dress || botK == 2 || botK == 6) {
            double len = dress ? 0.62 : botK == 6 ? 0.36 : 0.52;
            string col = dress ? top : bot;
            var sk = new List<Point> { new Point(w * 0.9, hy + 0.22), new Point(w * 1.0, hy), new Point(w * 1.25 + len * 0.35, hy - len) };
            body.Children.Add(Geo(Mesh3.Lathe(sk, 32, 0.8), Mat(col, gloss), true));
        }
        // neck things
        string nc = d.C("neckCol");
        switch (d.I("neck")) {
            case 1: body.Children.Add(At(Geo(Mesh3.Torus(0.27, 0.11), Mat(nc, 0.1)), Place(0, sy + 0.1, 0))); body.Children.Add(At(Geo(Mesh3.Capsule(0.09, 0.08, 0.35), Mat(nc, 0.1)), Place(0.18, sy + 0.02, w * depth * 0.95, rz: 8))); break;
            case 2: for (int s = -1; s <= 1; s += 2) body.Children.Add(At(Geo(Mesh3.Ball(new Point3D(0, 0, 0), 0.12, 0.08, 0.05), Mat(nc, 0.3)), Place(s * 0.1, sy + 0.04, w * depth + 0.03, rz: s * 12))); break;
            case 3: body.Children.Add(At(Geo(Mesh3.Ball(new Point3D(0, 0, 0), 0.09, 0.36, 0.03), Mat(nc, 0.3)), Place(0, sy - 0.32, w * depth * 0.97 + 0.03, rx: -8))); break;
            case 4: body.Children.Add(At(Geo(Mesh3.Torus(0.3, 0.025), Mat(nc, 0.7)), Place(0, sy + 0.02, 0.06, rx: 22))); break;
            case 5: body.Children.Add(At(Geo(Mesh3.Torus(0.26, 0.08), Mat(nc, 0.1)), Place(0, sy + 0.12, 0, rx: 14))); break;
        }
        // on the back
        string bc = d.C("backCol");
        switch (d.I("back")) {
            case 1: body.Children.Add(At(Geo(Mesh3.Ball(new Point3D(0, 0, 0), w * 0.66, 0.48, 0.24), Mat(bc, 0.2)), Place(0, sy - 0.5, -w * depth - 0.12))); break;
            case 2: {
                var cape = new List<Point> { new Point(w * 0.8, sy + 0.05), new Point(w * 1.05, sy - 0.4), new Point(w * 1.45, hy - 0.75) };
                body.Children.Add(At(Geo(Mesh3.Lathe(cape, 20, 0.85, 0.82, 1.18), Mat(bc, 0.15), true), Place(0, 0, -0.05)));
                break;
            }
            case 3: for (int s = -1; s <= 1; s += 2) body.Children.Add(At(Geo(Mesh3.Ball(new Point3D(0, 0, 0), 0.62, 0.36, 0.05), Mat(bc, 0.4)), Place(s * 0.55, sy - 0.15, -w * depth - 0.1, ry: s * -20, rz: s * 28))); break;
        }

        // ---------- arms: sleeves as the top says, round hands ----------
        bool longSleeve = topK == 1 || topK == 2 || topK == 3 || topK == 4;
        bool shortSleeve = !longSleeve && !tank;
        for (int i = 0; i < 2; i++) {
            double s = i == 0 ? 1 : -1;
            r.armZ[i] = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0); r.armX[i] = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
            r.foreZ[i] = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
            var arm = r.Arm[i]; arm.Transform = Joint(new Point3D(s * r.ShoulderX, sy - 0.12, 0), r.armZ[i], r.armX[i]);
            double t = 0.15 * bld;
            arm.Children.Add(Geo(Mesh3.Capsule(t + 0.01, t, r.Upper), Mat(longSleeve ? top : skin, longSleeve ? gloss : 0.12)));
            if (shortSleeve) arm.Children.Add(Geo(Mesh3.Capsule(t + 0.05, t + 0.04, r.Upper * 0.45), Mat(dress ? top : top, gloss)));
            var fore = r.Forearm[i]; fore.Transform = Joint(new Point3D(0, -r.Upper, 0), r.foreZ[i]);
            fore.Children.Add(Geo(Mesh3.Capsule(t, t * 0.92, r.Fore), Mat(longSleeve ? top : skin, longSleeve ? gloss : 0.12)));
            if (longSleeve) fore.Children.Add(At(Geo(Mesh3.Torus(t * 0.95, 0.035), Mat(topK == 2 || topK == 4 ? top2 : top, gloss)), Place(0, -r.Fore + 0.02, 0)));
            fore.Children.Add(Geo(Mesh3.Ball(new Point3D(0, -r.Fore - 0.12, 0), 0.19, 0.19, 0.18), Mat(skin, 0.15)));
            arm.Children.Add(fore);
            body.Children.Add(arm);
        }

        // ---------- legs ----------
        bool skirtish = dress || botK == 2 || botK == 6, shorts = botK == 1;
        for (int i = 0; i < 2; i++) {
            double s = i == 0 ? 1 : -1;
            r.legX[i] = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0); r.kneeX[i] = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
            var leg = r.Leg[i]; leg.Transform = Joint(new Point3D(s * r.HipX, hy, 0), r.legX[i]);
            double t = 0.21 * bld;
            string thigh = skirtish ? skin : overalls ? bot : bot, shinCol = skirtish || shorts ? skin : bot;
            if (botK == 5) shinCol = bot;
            leg.Children.Add(Geo(Mesh3.Capsule(t + 0.02, t, r.Thigh), Mat(thigh, skirtish ? 0.12 : 0.1)));
            var shin = r.Shin3[i]; shin.Transform = Joint(new Point3D(0, -r.Thigh, 0), r.kneeX[i]);
            shin.Children.Add(Geo(Mesh3.Capsule(t, t * 0.85, r.Shin - 0.06), Mat(shinCol, 0.1)));
            if (shorts) leg.Children.Add(At(Geo(Mesh3.Torus(t + 0.01, 0.03), Mat(bot, 0.1)), Place(0, -r.Thigh * 0.85, 0)));
            // the shoe (or the foot)
            double fy = -r.Shin - 0.03;
            switch (shoeK) {
                case 6: shin.Children.Add(Geo(Mesh3.Ball(new Point3D(0, fy, 0.08), 0.15, 0.1, 0.24), Mat(skin, 0.12))); break;
                case 3:
                    shin.Children.Add(Geo(Mesh3.Ball(new Point3D(0, fy, 0.08), 0.15, 0.1, 0.24), Mat(skin, 0.12)));
                    shin.Children.Add(Geo(Mesh3.Ball(new Point3D(0, fy - 0.08, 0.08), 0.18, 0.04, 0.28), Mat(shoe, 0.2)));
                    break;
                case 2: case 5:
                    shin.Children.Add(Geo(Mesh3.Ball(new Point3D(0, fy, 0.1), 0.19, 0.13, 0.29), Mat(shoe, shoeK == 5 ? 0.6 : 0.3)));
                    shin.Children.Add(At(Geo(Mesh3.Capsule(t + 0.05, t + 0.05, 0.32), Mat(shoe, shoeK == 5 ? 0.6 : 0.3)), Place(0, fy + 0.42, 0)));
                    break;
                default:
                    shin.Children.Add(Geo(Mesh3.Ball(new Point3D(0, fy, 0.09), 0.18, 0.12, 0.28), Mat(shoe, 0.3)));
                    shin.Children.Add(Geo(Mesh3.Ball(new Point3D(0, fy - 0.07, 0.09), 0.19, 0.04, 0.29), Mat(shoeK == 4 ? shoe : "#f7f7f7", 0.1)));
                    if (shoeK == 1) shin.Children.Add(At(Geo(Mesh3.Capsule(t + 0.04, t + 0.04, 0.12), Mat(shoe, 0.3)), Place(0, fy + 0.2, 0)));
                    break;
            }
            leg.Children.Add(shin);
            body.Children.Add(leg);
        }

        // ---------- the head ----------
        var head = r.Head;
        var ht = new Transform3DGroup();
        ht.Children.Add(new RotateTransform3D(r.hx)); ht.Children.Add(new RotateTransform3D(r.hy)); ht.Children.Add(new RotateTransform3D(r.hz));
        ht.Children.Add(new TranslateTransform3D(0, r.NeckY, 0));
        head.Transform = ht;
        body.Children.Add(head);
        double R = r.R, hc = r.HeadY - r.NeckY;   // the head's centre, from the neck
        Func<Vector3D, Vector3D> bend = FaceBend(d.I("face"));
        double sideW = new[] { 1.0, 0.9, 1.02, 0.98, 0.86, 1.08, 0.93, 1.04 }[d.I("face")];
        double rx = R * sideW, ry = R * (new[] { 1.0, 1.05, 1.0, 1.0, 1.08, 0.94, 1.0, 1.0 }[d.I("face")]), rz = R * 0.94;
        head.Children.Add(Geo(Mesh3.Ball(new Point3D(0, hc, 0), rx, ry, rz, 40, 30, bend), Mat(skin, gloss * 0.8)));
        // the face, printed on the front
        r.faceOpen = new ImageBrush(AvatarArt.FaceDecal(d, mood, false, facePx)) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) };
        r.faceShut = new ImageBrush(AvatarArt.FaceDecal(d, mood, true, facePx)) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) };
        r.faceOpen.Freeze(); r.faceShut.Freeze();
        r.face = new DiffuseMaterial(r.faceOpen);
        // 110 of the drawing's units are a little more than the head's radius (100)
        head.Children.Add(Geo(Mesh3.Ball(new Point3D(0, hc, 0), rx * 1.006, ry * 1.006, rz * 1.006, 48, 36, bend, n => n.Z > 0.02, R * 1.1), r.face));
        // ears
        int ears = d.I("ears");
        for (int s = -1; s <= 1; s += 2) {
            Point3D ec = new Point3D(s * rx * 0.97, hc - 0.12 * R, -0.05);
            switch (ears) {
                case 1: head.Children.Add(Geo(Mesh3.Ball(ec, 0.1, 0.14, 0.08), Mat(skin, 0.1))); break;
                case 2: head.Children.Add(At(Geo(Mesh3.Ball(new Point3D(0, 0, 0), 0.09, 0.32, 0.08), Mat(skin, 0.1)), Place(ec.X + s * 0.08, ec.Y + 0.2, ec.Z, rz: -s * 38))); break;
                case 3: head.Children.Add(Geo(Mesh3.Ball(ec, 0.16, 0.24, 0.1), Mat(skin, 0.1))); break;
                default: head.Children.Add(Geo(Mesh3.Ball(ec, 0.12, 0.18, 0.09), Mat(skin, 0.1))); break;
            }
            int er = d.I("earrings");
            if (er > 0) {
                string ecol = d.C("earCol");
                if (er == 2) head.Children.Add(At(Geo(Mesh3.Torus(0.07, 0.015), Mat(ecol, 0.8)), Place(ec.X + s * 0.02, ec.Y - 0.25, ec.Z, rz: 90)));
                else head.Children.Add(Geo(Mesh3.Ball(new Point3D(ec.X + s * 0.02, ec.Y - 0.2, ec.Z), 0.045, 0.045, 0.045), Mat(ecol, 0.8)));
            }
        }
        Hair(r, d, head, new Point3D(0, hc, 0), rx, ry, rz, bend, gloss);
        Hat(r, d, head, new Point3D(0, hc, 0), rx, ry, rz, gloss);

        r.Top = r.HeadY + ry + 0.2;
        if (shadow) {
            // a soft shadow on the ground (last: it's see-through)
            var sh = new RadialGradientBrush(Color.FromArgb(110, 0, 0, 0), Color.FromArgb(0, 0, 0, 0)); sh.Freeze();
            var disc = new MeshGeometry3D();
            disc.Positions = new Point3DCollection { new Point3D(-1.1, 0.005, -0.9), new Point3D(1.1, 0.005, -0.9), new Point3D(1.1, 0.005, 0.9), new Point3D(-1.1, 0.005, 0.9) };
            disc.TextureCoordinates = new PointCollection { new Point(0, 0), new Point(1, 0), new Point(1, 1), new Point(0, 1) };
            disc.TriangleIndices = new Int32Collection { 0, 2, 1, 0, 3, 2 };
            disc.Normals = new Vector3DCollection { new Vector3D(0, 1, 0), new Vector3D(0, 1, 0), new Vector3D(0, 1, 0), new Vector3D(0, 1, 0) };
            var shm = new GeometryModel3D(disc, new EmissiveMaterial(sh)) { BackMaterial = new DiffuseMaterial(Brushes.Transparent) };
            shm.Material = new DiffuseMaterial(sh);
            r.Root.Children.Add(shm);
        }
        return r;
    }

    // the face shapes, as a bend of the ball (x right, y up, z front, on the unit ball)
    static Func<Vector3D, Vector3D> FaceBend(int f) {
        switch (f) {
            case 2: return n => new Vector3D(n.X * (n.Y < 0 ? 1 + 0.32 * n.Y : 1), n.Y, n.Z * (n.Y < 0 ? 1 + 0.12 * n.Y : 1));                           // heart: narrow chin
            case 3: return n => { double s = Math.Pow(Math.Pow(Math.Abs(n.X), 3) + Math.Pow(Math.Abs(n.Y), 3) + Math.Pow(Math.Abs(n.Z), 3), 1 / 3.0); double k = 0.55 + 0.45 / s; return new Vector3D(n.X * k, n.Y * k, n.Z * (0.6 + 0.4 / s)); };   // square
            case 6: return n => new Vector3D(n.X * (1 + 0.12 * -n.Y), n.Y, n.Z);                                                                          // pear: wider below
            case 7: return n => new Vector3D(n.X * (1 - 0.16 * Math.Abs(n.Y)), n.Y, n.Z);                                                               // diamond
            default: return null;
        }
    }

    // ---------- hair: a cap round the head, cut where the face is, and the style's own pieces ----------
    static void Hair(Rig3D r, AvatarDef d, Model3DGroup head, Point3D c, double rx, double ry, double rz, Func<Vector3D, Vector3D> bend, double gloss) {
        int st = d.I("hair"); if (st == 0) return;
        var mat = Mat(d.C("hairCol"), Math.Max(0.25, gloss));
        double flip = d.I("hairFlip") == 1 ? -1 : 1;
        // hairline over the forehead, how low it goes at the back and at the sides
        double line = 0.42, nape = -0.2, side = 0.05, thick = 1.07;
        bool fringe = false, longBack = false;
        switch (st) {
            case 1: thick = 1.025; line = 0.48; nape = -0.1; break;                   // buzz
            case 6: line = 0.26; fringe = true; break;                                // curtains
            case 10: line = 0.3; nape = -0.3; fringe = true; break;                   // pixie
            case 11: line = 0.28; nape = -0.55; side = -0.5; fringe = true; break;    // bob
            case 12: case 13: case 19: line = 0.36; nape = -0.6; side = -0.5; longBack = true; break;   // long, wavy, braids
            case 20: line = 0.3; nape = -0.3; fringe = true; break;                   // messy
            case 8: line = 0.3; break;                                                // afro (under the big ball)
        }
        double curtainGap = st == 6 ? 0.1 : 0;
        Func<Vector3D, bool> keep = n => {
            bool front = n.Z > 0.25, faceZone = Math.Abs(n.X) < 0.78;
            if (curtainGap > 0 && n.Z > 0 && Math.Abs(n.X) < curtainGap && n.Y < 0.7) return false;
            if (front && faceZone) return n.Y > line;
            if (n.Z < -0.15) return n.Y > nape;
            return n.Y > side;
        };
        // where there's no hair the cap sinks into the head (a soft blend over the hairline, not a cut)
        Func<Vector3D, double> sinkIn = n => {
            bool front = n.Z > 0.25, faceZone = Math.Abs(n.X) < 0.78;
            double edge = front && faceZone ? n.Y - line : n.Z < -0.15 ? n.Y - nape : n.Y - side;
            if (curtainGap > 0 && n.Z > 0 && Math.Abs(n.X) < curtainGap && n.Y < 0.7) edge = Math.Min(edge, -0.05);
            double k = Math.Max(0, Math.Min(1, (edge + 0.06) / 0.12));
            return 0.84 + 0.16 * k * k * (3 - 2 * k);
        };
        head.Children.Add(Geo(Mesh3.Ball(c, rx * thick, ry * thick, rz * thick, 64, 48, bend, n => sinkIn(n) > 0.845 || n.Y > -0.2, 0, sinkIn), mat, true));
        Func<double, double, double, double, double, double, GeometryModel3D> blob = (x, y, z, a, b, cc) => Geo(Mesh3.Ball(new Point3D(c.X + x, c.Y + y, c.Z + z), a, b, cc, 20, 14), mat);
        double R = r.R;
        if (longBack) head.Children.Add(blob(0, -0.75 * R, -0.42 * R, rx * 0.98, 1.0 * R, 0.5 * R));
        switch (st) {
            case 3:   // spiky
                for (int k = 0; k < 9; k++) {
                    double a = (k - 4) * 0.32;
                    head.Children.Add(At(Geo(Mesh3.Capsule(0.17, 0.02, 0.36, 10), mat), Place(c.X + Math.Sin(a) * 0.55 * R, c.Y + 0.95 * R, c.Z - 0.1 + (k % 2) * 0.2, rx: 180 + (k % 2 == 0 ? -20 : 15), rz: 180 - a * 50)));
                }
                break;
            case 4: case 5: case 21:   // swept, side part, quiff: a swoop over the forehead
                head.Children.Add(At(Geo(Mesh3.Ball(new Point3D(0, 0, 0), rx * 0.7, st == 21 ? 0.38 * R : 0.26 * R, 0.5 * R, 22, 16), mat), Place(c.X + flip * 0.22 * R, c.Y + (st == 21 ? 0.85 : 0.72) * R, c.Z + 0.42 * R, rx: st == 21 ? -25 : -12, rz: flip * -10)));
                break;
            case 7: case 20:   // curly, messy: little curls all over
                var rnd = new Random(st * 31 + d.ToString().Length);
                for (int k = 0; k < (st == 7 ? 26 : 14); k++) {
                    double th = rnd.NextDouble() * Math.PI * 0.62, ph = rnd.NextDouble() * Math.PI * 2;
                    var n = new Vector3D(Math.Sin(th) * Math.Sin(ph), Math.Cos(th), Math.Sin(th) * Math.Cos(ph));
                    if (n.Z > 0.3 && n.Y < 0.5 && Math.Abs(n.X) < 0.7) continue;
                    double s = st == 7 ? 0.24 : 0.18;
                    head.Children.Add(blob(n.X * rx * 1.05, n.Y * ry * 1.05, n.Z * rz * 1.05, s * R, s * R, s * R));
                }
                break;
            case 8: {  // afro
                Func<Vector3D, bool> open = n => !(n.Z > 0.45 && n.Y < 0.35 && Math.Abs(n.X) < 0.62);
                head.Children.Add(Geo(Mesh3.Ball(new Point3D(c.X, c.Y + 0.28 * R, c.Z - 0.18 * R), rx * 1.45, ry * 1.32, rz * 1.4, 32, 24, null, open), mat, true));
                break;
            }
            case 9:   // mohawk
                for (int k = 0; k < 7; k++) {
                    double a = -1.1 + k * 0.37;
                    head.Children.Add(At(Geo(Mesh3.Ball(new Point3D(0, 0, 0), 0.09 * R, 0.3 * R, 0.2 * R, 14, 10), mat), Place(c.X, c.Y + Math.Cos(a) * ry * 1.05, c.Z + Math.Sin(a) * rz * 1.05, rx: -a * 57.3)));
                }
                break;
            case 14: case 15: {   // ponytail, high ponytail
                bool high = st == 15;
                head.Children.Add(At(Geo(Mesh3.Capsule(0.2 * R, 0.1 * R, 0.85 * R, 14), mat), Place(c.X, c.Y + (high ? 0.7 : 0.05) * R, c.Z - rz * 1.02, rx: high ? 35 : 12)));
                head.Children.Add(Geo(Mesh3.Ball(new Point3D(c.X, c.Y + (high ? 0.72 : 0.06) * R, c.Z - rz * 1.0), 0.13 * R, 0.13 * R, 0.13 * R), Mat(d.C("hatCol"), 0.3)));
                break;
            }
            case 16:   // twin tails
                for (int s = -1; s <= 1; s += 2)
                    head.Children.Add(At(Geo(Mesh3.Capsule(0.2 * R, 0.1 * R, 0.95 * R, 14), mat), Place(c.X + s * rx * 0.95, c.Y + 0.35 * R, c.Z - 0.15 * R, rz: s * 22)));
                break;
            case 17: head.Children.Add(blob(0, 0.92 * R, -0.3 * R, 0.4 * R, 0.36 * R, 0.4 * R)); break;   // bun
            case 18: for (int s = -1; s <= 1; s += 2) head.Children.Add(blob(s * 0.62 * R, 0.78 * R, -0.08 * R, 0.33 * R, 0.33 * R, 0.33 * R)); break;   // space buns
            case 19:   // braids, in front of the shoulders
                for (int s = -1; s <= 1; s += 2)
                    for (int k = 0; k < 6; k++) head.Children.Add(blob(s * rx * 0.88, -0.35 * R - k * 0.24 * R, 0.08 * R, 0.15 * R, 0.15 * R, 0.15 * R));
                break;
            case 13:   // wavy: a few waves at the ends
                for (int s = -1; s <= 1; s += 2) for (int k = 0; k < 3; k++) head.Children.Add(blob(s * rx * 0.92, -0.45 * R - k * 0.3 * R, -0.2 * R, 0.2 * R, 0.2 * R, 0.2 * R));
                break;
        }
        if (fringe && st != 6) head.Children.Add(At(Geo(Mesh3.Ball(new Point3D(0, 0, 0), rx * 0.82, 0.2 * R, 0.4 * R, 22, 14), mat), Place(c.X + flip * 0.06 * R, c.Y + 0.48 * R, c.Z + 0.58 * R, rx: -6)));
    }

    // ---------- hats ----------
    static void Hat(Rig3D r, AvatarDef d, Model3DGroup head, Point3D c, double rx, double ry, double rz, double gloss) {
        int h = d.I("hat"); if (h == 0) return;
        var mat = Mat(d.C("hatCol"), 0.25); double R = r.R; double top = c.Y + ry;
        Func<Vector3D, bool> dome = n => n.Y > 0.3;
        switch (h) {
            case 1: case 2: {   // cap, backwards cap
                head.Children.Add(Geo(Mesh3.Ball(c, rx * 1.1, ry * 1.1, rz * 1.1, 32, 24, null, dome), mat, true));
                double z = h == 1 ? 1 : -1;
                head.Children.Add(At(Geo(Mesh3.Ball(new Point3D(0, 0, 0), 0.62 * R, 0.05 * R, 0.5 * R, 24, 10), mat), Place(c.X, c.Y + 0.38 * R, c.Z + z * 0.82 * R, rx: z * 8)));
                head.Children.Add(Geo(Mesh3.Ball(new Point3D(c.X, top + 0.1 * R, c.Z), 0.07 * R, 0.05 * R, 0.07 * R), mat));
                break;
            }
            case 3:   // beanie
                head.Children.Add(Geo(Mesh3.Ball(new Point3D(c.X, c.Y + 0.08 * R, c.Z), rx * 1.12, ry * 1.12, rz * 1.12, 32, 24, null, n => n.Y > 0.25), mat, true));
                head.Children.Add(At(Geo(Mesh3.Torus(rx * 1.0, 0.1 * R), mat), Place(c.X, c.Y + 0.4 * R, c.Z)));
                head.Children.Add(Geo(Mesh3.Ball(new Point3D(c.X, top + 0.2 * R, c.Z), 0.16 * R, 0.16 * R, 0.16 * R), Mat(d.C("topCol2"), 0.1)));
                break;
            case 4: {   // crown
                var gold = Mat("#f2c230", 0.85);
                head.Children.Add(Geo(Mesh3.Lathe(new List<Point> { new Point(0.55 * R, top + 0.25 * R), new Point(0.55 * R, top - 0.1 * R) }, 28), gold, true));
                for (int k = 0; k < 6; k++) { double a = k * Math.PI / 3; head.Children.Add(At(Geo(Mesh3.Capsule(0.09 * R, 0.02, 0.18 * R, 8), gold), Place(c.X + Math.Sin(a) * 0.55 * R, top + 0.42 * R, c.Z + Math.Cos(a) * 0.55 * R, rx: 180))); }
                break;
            }
            case 5: head.Children.Add(At(Geo(Mesh3.Torus(rx * 1.02, 0.07 * R), mat), Place(c.X, c.Y + 0.5 * R, c.Z, rx: -10))); break;   // headband
            case 6: for (int s = -1; s <= 1; s += 2) head.Children.Add(At(Geo(Mesh3.Ball(new Point3D(0, 0, 0), 0.26 * R, 0.16 * R, 0.09 * R), mat), Place(c.X + 0.4 * R + s * 0.22 * R, top - 0.05 * R, c.Z + 0.1, rz: s * 20))); break;   // bow
            case 7: head.Children.Add(At(Geo(Mesh3.Ball(new Point3D(0, 0, 0), rx * 1.15, 0.28 * R, rz * 1.1), mat), Place(c.X + 0.1 * R, top - 0.12 * R, c.Z - 0.05, rz: -10))); break;   // beret
            case 8:   // cowboy hat
                head.Children.Add(Geo(Mesh3.Ball(new Point3D(c.X, top - 0.2 * R, c.Z), rx * 0.82, 0.55 * R, rz * 0.82, 28, 20, null, n => n.Y > -0.05), mat, true));
                head.Children.Add(At(Geo(Mesh3.Ball(new Point3D(0, 0, 0), rx * 1.65, 0.05 * R, rz * 1.5, 32, 10), mat), Place(c.X, top - 0.22 * R, c.Z)));
                break;
            case 9:   // top hat
                head.Children.Add(Geo(Mesh3.Lathe(new List<Point> { new Point(0.001, top + 0.85 * R), new Point(0.55 * R, top + 0.85 * R), new Point(0.6 * R, top - 0.12 * R), new Point(0.001, top - 0.12 * R) }, 28), mat));
                head.Children.Add(At(Geo(Mesh3.Ball(new Point3D(0, 0, 0), 1.0 * R, 0.04 * R, 0.95 * R, 32, 8), mat), Place(c.X, top - 0.12 * R, c.Z)));
                break;
            case 10: head.Children.Add(Geo(Mesh3.Lathe(new List<Point> { new Point(0.001, top + 1.4 * R), new Point(0.75 * R, top - 0.18 * R), new Point(0.001, top - 0.18 * R) }, 28), mat)); break;   // wizard
            case 11: for (int s = -1; s <= 1; s += 2) head.Children.Add(At(Geo(Mesh3.Ball(new Point3D(0, 0, 0), 0.16 * R, 0.62 * R, 0.08 * R), Mat("#f7f7f7", 0.1)), Place(c.X + s * 0.32 * R, top + 0.45 * R, c.Z - 0.05, rz: s * -12))); break;   // bunny ears
            case 12: for (int s = -1; s <= 1; s += 2) head.Children.Add(At(Geo(Mesh3.Capsule(0.22 * R, 0.01, 0.32 * R, 4), mat), Place(c.X + s * 0.55 * R, top + 0.12 * R, c.Z, rx: 180, rz: s * -20))); break;   // cat ears
            case 13:   // headphones
                head.Children.Add(At(Geo(Mesh3.Torus(rx * 1.1, 0.06 * R, 28, 10, 0.5), mat), Place(c.X, c.Y + 0.05 * R, c.Z, rx: -90)));
                for (int s = -1; s <= 1; s += 2) head.Children.Add(At(Geo(Mesh3.Ball(new Point3D(0, 0, 0), 0.16 * R, 0.3 * R, 0.3 * R), mat), Place(c.X + s * rx * 1.08, c.Y - 0.1 * R, c.Z)));
                break;
            case 14:   // a flower
                for (int k = 0; k < 5; k++) { double a = k * Math.PI * 2 / 5; head.Children.Add(Geo(Mesh3.Ball(new Point3D(c.X + 0.62 * R + Math.Cos(a) * 0.14 * R, top - 0.18 * R + Math.Sin(a) * 0.14 * R, c.Z + 0.25 * R), 0.11 * R, 0.11 * R, 0.06 * R), mat)); }
                head.Children.Add(Geo(Mesh3.Ball(new Point3D(c.X + 0.62 * R, top - 0.18 * R, c.Z + 0.28 * R), 0.08 * R, 0.08 * R, 0.06 * R), Mat("#f2d24b", 0.2)));
                break;
        }
    }

    // the top's picture, wrapped round the body: u 0.5 is the front, v from the shoulders down
    static ImageSource TorsoTexture(AvatarDef d) {
        int topK = d.I("top"), pat = d.I("pattern");
        string top = d.C("topCol"), top2 = d.C("topCol2"), bot = d.C("bottomCol");
        var dv = new DrawingVisual();
        Func<string, Brush> B = h => { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)); b.Freeze(); return b; };
        using (var dc = dv.RenderOpen()) {
            const double W = 512, H = 256;
            dc.DrawRectangle(B(top), null, new Rect(0, 0, W, H));
            // the lower part: trousers start at the hips (skirt and dress cover their own)
            bool dress = topK == 8;
            if (!dress) dc.DrawRectangle(B(bot), null, new Rect(0, H * 0.84, W, H * 0.16));
            switch (topK) {
                case 2: dc.DrawRectangle(B(top2), null, new Rect(W * 0.46, H * 0.08, W * 0.08, H * 0.55)); dc.DrawRectangle(B(top2), null, new Rect(W * 0.38, H * 0.55, W * 0.24, H * 0.18)); break;   // hoodie: strings, pocket
                case 3: dc.DrawRectangle(B(top2), null, new Rect(W * 0.46, 0, W * 0.08, H * 0.84)); dc.DrawRectangle(B("#2d2d2d"), null, new Rect(W * 0.497, 0, W * 0.006, H * 0.84)); break;   // jacket
                case 4: for (int k = 0; k < 4; k++) dc.DrawRectangle(B(top2), null, new Rect(0, H * (0.72 + k * 0.025), W, H * 0.01)); break;   // sweater ribs
                case 6: dc.DrawGeometry(B(top2), null, Geometry.Parse("M 236,0 L 256,30 L 276,0 Z")); for (int k = 0; k < 4; k++) dc.DrawEllipse(B(top2), null, new Point(W / 2, 40 + k * 40), 4, 4); break;   // shirt
                case 7: dc.DrawRectangle(B(top2), null, new Rect(0, H * 0.04, W, H * 0.06)); dc.DrawRectangle(B(top2), null, new Rect(W * 0.05, H * 0.25, W * 0.04, H * 0.5)); break;   // jersey
                case 9: dc.DrawRectangle(B(bot), null, new Rect(W * 0.36, H * 0.3, W * 0.28, H * 0.6)); dc.DrawRectangle(B(bot), null, new Rect(W * 0.36, 0, W * 0.04, H * 0.3)); dc.DrawRectangle(B(bot), null, new Rect(W * 0.60, 0, W * 0.04, H * 0.3)); dc.DrawRectangle(B(bot), null, new Rect(0, H * 0.6, W, H * 0.3)); break;   // overalls
                case 5: break;
            }
            // the pattern on the chest
            var p2 = B(top2);
            var ctr = new Point(W / 2, H * 0.42);
            switch (pat) {
                case 1: for (int k = 0; k < 6; k++) dc.DrawRectangle(p2, null, new Rect(0, H * (0.12 + k * 0.12), W, H * 0.05)); break;
                case 2: for (int y = 20; y < H * 0.8; y += 28) for (int x = 10; x < W; x += 28) dc.DrawEllipse(p2, null, new Point(x + (y / 28 % 2) * 14, y), 5, 5); break;
                case 3: dc.DrawGeometry(p2, null, StarAt(ctr, 34, 0.45)); break;
                case 4: dc.DrawGeometry(p2, null, Geometry.Parse("M 256,140 C 210,110 218,72 240,72 C 250,72 256,80 256,88 C 256,80 262,72 272,72 C 294,72 302,110 256,140 Z")); break;
                case 5: dc.DrawGeometry(p2, null, Geometry.Parse("M 262,60 L 236,112 L 254,112 L 244,150 L 280,96 L 262,96 L 274,60 Z")); break;
            }
        }
        var bmp = new RenderTargetBitmap(512, 256, 96, 96, PixelFormats.Pbgra32); bmp.Render(dv); bmp.Freeze();
        return bmp;
    }
    static Geometry StarAt(Point c, double r, double inner) {
        var sg = new StreamGeometry();
        using (var g = sg.Open()) for (int i = 0; i < 10; i++) {
            double a = -Math.PI / 2 + i * Math.PI / 5, rr = i % 2 == 0 ? r : r * inner;
            var p = new Point(c.X + Math.Cos(a) * rr, c.Y + Math.Sin(a) * rr);
            if (i == 0) g.BeginFigure(p, true, true); else g.LineTo(p, true, true);
        }
        sg.Freeze(); return sg;
    }
}

// ---------- the animations ----------
static class Moves3D {
    static double S(double x) { return Math.Sin(x); }
    static double Ease(double x) { x = Math.Max(0, Math.Min(1, x)); return x * x * (3 - 2 * x); }
    // the pose at time t (seconds); pose: the character's own pose (for Idle and Pose)
    public static Pose3D At(Anim3D a, double t, int pose) {
        var p = new Pose3D();
        var arms = AvatarArt.PoseArms(a == Anim3D.Pose ? pose : 0);
        // screen-left arm (-X) is arms[0..1], screen-right (+X) arms[2..3]
        p.NUp = arms[0]; p.NFore = arms[1]; p.PUp = arms[2]; p.PFore = arms[3];
        // breathing and a little life, always
        double br = S(t * 2.1);
        p.Bob = 0.012 * br; p.HeadZ = 2.5 * S(t * 0.7); p.HeadX = 1.5 * S(t * 0.53 + 1);
        p.PUp += 1.5 * br; p.NUp += 1.5 * br;
        // blinking: briefly, every three to five seconds
        double bp = (t + 0.7) % 3.9; p.Blink = bp < 0.13 || (bp > 0.35 && bp < 0.45 && ((int)((t + 0.7) / 3.9)) % 3 == 1);
        switch (a) {
            case Anim3D.Idle:
                p.HeadY = 6 * S(t * 0.31);
                break;
            case Anim3D.Walk: {
                double ph = t * 2 * Math.PI * 1.7;
                p.PLeg = 26 * S(ph); p.NLeg = -26 * S(ph);
                p.PKnee = 34 * Math.Max(0, S(ph - 1.4)); p.NKnee = 34 * Math.Max(0, -S(ph - 1.4));
                p.PSwing = -24 * S(ph); p.NSwing = 24 * S(ph);
                p.PUp = 9; p.NUp = 9; p.PFore = 9 + 12 + 10 * Math.Max(0, -S(ph)); p.NFore = 9 + 12 + 10 * Math.Max(0, S(ph));
                p.Bob = 0.055 * Math.Abs(S(ph)) - 0.02; p.Lean = 4; p.HeadZ = 3 * S(ph); p.HeadX = -2;
                p.Turn = 2.5 * S(ph);
                break;
            }
            case Anim3D.Wave: {
                double w = S(t * 9);
                p.PUp = 148; p.PFore = 168 + 22 * w; p.HeadZ = 7 + 2 * w; p.HeadX = -3;
                p.NUp = 10; p.NFore = 14;
                break;
            }
            case Anim3D.Cheer: {
                double j = Math.Max(0, S(t * 7));
                p.PUp = p.NUp = 150 + 8 * S(t * 14); p.PFore = p.NFore = 168;
                p.Bob = 0.28 * j; p.Squash = 1 + 0.06 * j; p.HeadX = -6; p.PKnee = p.NKnee = 20 * (1 - j);
                break;
            }
            case Anim3D.Jump: {
                double c = t % 1.4, up = 0;
                if (c < 0.25) { double k = Ease(c / 0.25); p.Squash = 1 - 0.14 * k; p.Bob = -0.12 * k; p.PKnee = p.NKnee = 40 * k; p.PUp = p.NUp = 20 - 10 * k; }
                else if (c < 0.85) { double k = (c - 0.25) / 0.6; up = 4 * k * (1 - k); p.Bob = 0.9 * up; p.Squash = 1 + 0.12 * (1 - Math.Abs(2 * k - 1)); p.PUp = p.NUp = 40 + 110 * (1 - Math.Abs(2 * k - 1)); p.PFore = p.NFore = p.PUp + 15; p.PKnee = p.NKnee = 25 * up; }
                else if (c < 1.05) { double k = Ease((c - 0.85) / 0.2); p.Squash = 1 - 0.12 * (1 - k); p.Bob = -0.1 * (1 - k); p.PKnee = p.NKnee = 30 * (1 - k); }
                break;
            }
            case Anim3D.Held: {
                double k = S(t * 11);
                p.PUp = p.NUp = 140 + 10 * S(t * 6); p.PFore = p.NFore = 160;
                p.PLeg = 30 * k; p.NLeg = -30 * k; p.PKnee = 25 + 20 * k; p.NKnee = 25 - 20 * k;
                p.HeadZ = 8 * S(t * 5); p.Squash = 1.04;
                break;
            }
            case Anim3D.Dazed: {
                p.HeadZ = 14 * S(t * 4); p.HeadX = 10 * S(t * 4 + 1.5); p.HeadY = 10 * S(t * 2);
                p.PUp = p.NUp = 35 + 8 * S(t * 3); p.PFore = p.NFore = 50; p.Lean = 6 * S(t * 2.3); p.Turn = 12 * S(t * 1.7);
                p.Blink = true;
                break;
            }
        }
        return p;
    }
}

// ---------- pictures ----------
static class Avatar3D {
    // the camera for a crop: what it looks at and how much of the character's height fits
    static void Frame(Rig3D r, AvatarArt.Crop c, double aspect, out Point3D target, out double span, out double yaw) {
        switch (c) {
            case AvatarArt.Crop.Portrait: target = new Point3D(0, r.HeadY - 0.25 * r.R, 0); span = 3.3 * r.R; yaw = 0; break;
            case AvatarArt.Crop.Head: target = new Point3D(0, r.HeadY + 0.08 * r.R, 0); span = 2.7 * r.R; yaw = 0; break;
            case AvatarArt.Crop.Features: target = new Point3D(0, r.HeadY - 0.12 * r.R, 0); span = 1.6 * r.R; yaw = 0; break;
            default: target = new Point3D(0, r.Top / 2 - 0.05, 0); span = r.Top * 1.12; yaw = 14; break;
        }
    }
    public static PerspectiveCamera Camera(Rig3D r, AvatarArt.Crop c, double aspect, double zoom = 1) {
        Point3D target; double span, yaw; Frame(r, c, aspect, out target, out span, out yaw);
        span /= zoom;
        double vfov = 26 * Math.PI / 180;
        double dist = span / 2 / Math.Tan(vfov / 2);
        double hfov = 2 * Math.Atan(Math.Tan(vfov / 2) * aspect) * 180 / Math.PI;
        double up = c == AvatarArt.Crop.Full ? 0.08 : 0.04;
        var pos = new Point3D(target.X, target.Y + dist * up, target.Z + dist);
        return new PerspectiveCamera(pos, target - pos, new Vector3D(0, 1, 0), hfov) { NearPlaneDistance = 0.1, FarPlaneDistance = 200 };
    }
    public static Model3DGroup Lights() {
        var g = new Model3DGroup();
        g.Children.Add(new AmbientLight(Color.FromRgb(118, 116, 124)));
        g.Children.Add(new DirectionalLight(Color.FromRgb(214, 210, 200), new Vector3D(0.5, -0.55, -0.7)));    // the key light, from the top left, in front
        g.Children.Add(new DirectionalLight(Color.FromRgb(70, 78, 100), new Vector3D(-0.7, -0.15, -0.5)));    // a cool fill from the right
        g.Children.Add(new DirectionalLight(Color.FromRgb(96, 92, 88), new Vector3D(0, -0.35, 0.95)));        // a rim from behind
        return g;
    }
    static Anim3D AnimOf(AvatarState st, out double t) {
        t = 0;
        if (st.Held) { t = st.Walk / 11.0; return Anim3D.Held; }
        if (st.Walk != 0) { t = st.Walk / (2 * Math.PI * 1.7); return Anim3D.Walk; }
        return Anim3D.Pose;
    }
    // a still picture, like AvatarArt.Render (drawn at twice the size, then made smooth)
    static int FacePx(AvatarArt.Crop c, int pxH) { return Math.Max(160, Math.Min(512, (int)(pxH * (c == AvatarArt.Crop.Full ? 0.9 : 1.6)))); }
    // several pictures of one character (the walking frames): built once, posed for each
    public static ImageSource[] RenderMany(AvatarDef d, AvatarState[] states, AvatarArt.Crop c, int pxW, int pxH) {
        var rig = Rig3D.Build(d, d.I("mood"), !states[0].NoShadow && c == AvatarArt.Crop.Full, FacePx(c, pxH));
        var cam = Camera(rig, c, pxW / (double)pxH);
        var outp = new ImageSource[states.Length];
        for (int i = 0; i < states.Length; i++) {
            double t; var a = AnimOf(states[i], out t);
            var p = Moves3D.At(a, t, d.I("pose"));
            if (a == Anim3D.Pose) { p.Bob = 0; p.HeadZ = 0; p.HeadX = 0; p.HeadY = 0; p.Blink = false; }
            Point3D target; double span, yaw; Frame(rig, c, pxW / (double)pxH, out target, out span, out yaw);
            p.Turn += yaw; rig.Apply(p);
            outp[i] = Snapshot(rig, cam, pxW, pxH);
        }
        return outp;
    }
    public static ImageSource Render(AvatarDef d, AvatarState st, AvatarArt.Crop c, int pxW, int pxH, double yawExtra = 0) {
        int mood = st.Mood >= 0 ? st.Mood : d.I("mood");
        var rig = Rig3D.Build(d, mood, !st.NoShadow && c == AvatarArt.Crop.Full, FacePx(c, pxH));
        double t; var a = AnimOf(st, out t);
        int pose = st.Pose >= 0 ? st.Pose : d.I("pose");
        var p = Moves3D.At(a, t, pose);
        if (a == Anim3D.Pose) { p.Bob = 0; p.HeadZ = 0; p.HeadX = 0; p.HeadY = 0; p.Blink = st.Blink; }
        Point3D target; double span, yaw; Frame(rig, c, pxW / (double)pxH, out target, out span, out yaw);
        p.Turn += yaw + yawExtra;
        rig.Apply(p);
        var bmp = Snapshot(rig, Camera(rig, c, pxW / (double)pxH), pxW, pxH);
        if (!st.Backdrop) return bmp;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) {
            dc.DrawImage(AvatarArt.BackdropImage(d, c, pxW, pxH), new Rect(0, 0, pxW, pxH));
            dc.DrawImage(bmp, new Rect(0, 0, pxW, pxH));
        }
        var outb = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32); outb.Render(dv); outb.Freeze();
        return outb;
    }
    public static BitmapSource Snapshot(Rig3D rig, Camera cam, int pxW, int pxH) {
        const int ss = 2;
        var vp = new Viewport3D { Width = pxW, Height = pxH, Camera = cam, ClipToBounds = true };
        vp.Children.Add(new ModelVisual3D { Content = Lights() });
        vp.Children.Add(new ModelVisual3D { Content = rig.Root });
        vp.Measure(new Size(pxW, pxH)); vp.Arrange(new Rect(0, 0, pxW, pxH)); vp.UpdateLayout();
        var big = new RenderTargetBitmap(pxW * ss, pxH * ss, 96 * ss, 96 * ss, PixelFormats.Pbgra32);
        big.Render(vp);
        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
        using (var dc = dv.RenderOpen()) dc.DrawImage(big, new Rect(0, 0, pxW, pxH));
        var bmp = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32); bmp.Render(dv); bmp.Freeze();
        return bmp;
    }
}

// ---------- live: the character moving, in its own little stage ----------
class Avatar3DView : Grid {
    readonly Viewport3D vp = new Viewport3D { ClipToBounds = false };
    readonly ModelVisual3D model = new ModelVisual3D();
    Rig3D rig; AvatarDef def; DateTime start = DateTime.Now, animStart = DateTime.Now;
    public Anim3D Anim = Anim3D.Idle;
    Anim3D after = Anim3D.Idle; double animFor;   // a one-off (wave, cheer, jump) for this long, then back
    public double Yaw, Zoom = 1; public AvatarArt.Crop Crop = AvatarArt.Crop.Full;
    public int Mood = -1;
    public bool Shadow = true;   // a soft shadow under the feet (the plaza draws its own)
    bool hooked;
    public Avatar3DView() {
        vp.Children.Add(new ModelVisual3D { Content = Avatar3D.Lights() });
        vp.Children.Add(model);
        Children.Add(vp);
        IsVisibleChanged += (s, e) => Hook(IsVisible);
        SizeChanged += (s, e) => UpdateCamera();
    }
    void Hook(bool on) {
        if (on && !hooked) { CompositionTarget.Rendering += Tick; hooked = true; }
        else if (!on && hooked) { CompositionTarget.Rendering -= Tick; hooked = false; }
    }
    public void SetDef(AvatarDef d) {
        if (d == null) return;
        if (def != null && def.Same(d) && rig != null) return;
        Rebuild(d);
    }
    public void Rebuild(AvatarDef d) {
        def = d.Clone();
        try {
            rig = Rig3D.Build(def, Mood >= 0 ? Mood : def.I("mood"), Shadow);
            model.Content = rig.Root;
        } catch { rig = null; model.Content = null; }
        UpdateCamera();
    }
    // a one-off animation, then back to what it was doing
    public void Play(Anim3D a, double seconds) { after = Anim == a ? Anim3D.Idle : Anim; Anim = a; animFor = seconds; animStart = DateTime.Now; }
    // the camera glides to a new framing (the creator: the whole body, or the face)
    PerspectiveCamera camFrom; DateTime camStart; bool gliding;
    public void Aim(AvatarArt.Crop c, double zoom, bool now) {
        var cur = vp.Camera as PerspectiveCamera;
        Crop = c; Zoom = zoom;
        if (now || cur == null || rig == null) { UpdateCamera(); return; }
        camFrom = cur.Clone(); camStart = DateTime.Now; gliding = true;
    }
    void UpdateCamera() {
        if (rig == null || ActualWidth < 1 || ActualHeight < 1) return;
        var to = Avatar3D.Camera(rig, Crop, ActualWidth / ActualHeight, Zoom);
        if (gliding && camFrom != null) {
            double t = Math.Min(1, (DateTime.Now - camStart).TotalMilliseconds / 450), k = 1 - Math.Pow(1 - t, 3);
            to.Position = camFrom.Position + (to.Position - camFrom.Position) * k;
            to.LookDirection = camFrom.LookDirection + (to.LookDirection - camFrom.LookDirection) * k;
            to.FieldOfView = camFrom.FieldOfView + (to.FieldOfView - camFrom.FieldOfView) * k;
            if (t >= 1) gliding = false;
        }
        vp.Camera = to;
    }
    void Tick(object s, EventArgs e) {
        if (rig == null) return;
        if (gliding) UpdateCamera();
        double t = (DateTime.Now - start).TotalSeconds, ta = (DateTime.Now - animStart).TotalSeconds;
        if (animFor > 0 && ta > animFor) { Anim = after; animFor = 0; }
        var p = Moves3D.At(Anim, Anim == Anim3D.Jump ? ta : t, def.I("pose"));
        p.Turn += Yaw + (Crop == AvatarArt.Crop.Full && Shadow ? 10 : 0);
        rig.Apply(p);
    }
}
}
