// Controllers drawn in the launcher's flat style: an Xbox, a PlayStation and a Switch Pro controller, on a 400 x 260
// canvas. ControllerView shows one: small as an icon (the bottom left of Home), or big in Settings with a line from
// each button to what it does, the buttons lighting up as they're pressed. Button names and colours follow the prompt
// style (PadStyle): Xbox A B X Y, PlayStation Cross Circle Square Triangle, Nintendo B A Y X.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace RecompLauncher {

static class PadStyle {
    public static PadKind Kind = PadKind.Xbox;   // whose names and colours the prompts use
    public static bool ConfirmRight;              // A (pick) is the right face button and B (back) the bottom one
    // where a launcher button sits: the face buttons by position (A bottom, B right, X left, Y top)
    static string Pos(string key) {
        if (!ConfirmRight) return key;
        return key == "A" ? "B" : key == "B" ? "A" : key;
    }
    public static string Label(string key) {
        string p = Pos(key);
        switch (Kind) {
            case PadKind.PlayStation:
                switch (p) { case "A": return "✕"; case "B": return "○"; case "X": return "□"; case "Y": return "△";
                    case "LB": return "L1"; case "RB": return "R1"; case "LS": return "L3"; case "RS": return "R3"; case "LT": return "L2"; case "RT": return "R2"; case "≡": return "OPT"; case "⧉": return "CRT"; }
                break;
            case PadKind.Switch:
                switch (p) { case "A": return "B"; case "B": return "A"; case "X": return "Y"; case "Y": return "X";
                    case "LB": return "L"; case "RB": return "R"; case "LT": return "ZL"; case "RT": return "ZR"; case "≡": return "+"; case "⧉": return "−"; }
                break;
        }
        return p;
    }
    public static string Colour(string key) {
        string p = Pos(key);
        switch (Kind) {
            case PadKind.PlayStation: return p == "A" ? "#7FA6E8" : p == "B" ? "#E0696B" : p == "X" ? "#D58BCF" : p == "Y" ? "#4FC3A1" : "#8A9AA5";
            case PadKind.Switch: return "#8A9AA5";
            default: return p == "A" ? "#6CC04A" : p == "B" ? "#E0524B" : p == "X" ? "#3E8FE0" : p == "Y" ? "#E8B730" : "#8A9AA5";
        }
    }
    public static string Name(PadKind k) { return k == PadKind.PlayStation ? "PlayStation" : k == PadKind.Switch ? "Switch" : "Xbox"; }
}

class ControllerView : FrameworkElement {
    public PadKind Kind = PadKind.Xbox;
    public bool Connected = true;
    public int Pressed;                 // XInput bits, lit up
    public bool Callouts;               // the big one in Settings: a line from each button to what it does
    public string[][] Actions = new string[0][];   // (key, what it does)

    public void Show(int pressed) { if (pressed != Pressed) { Pressed = pressed; InvalidateVisual(); } }

    // the spots of the buttons on the 400 x 260 drawing
    // where the button that does a launcher key sits (A and B change places when the right button picks)
    Point SpotOf(string key) { return Spot(PadStyle.ConfirmRight && (key == "A" || key == "B") ? (key == "A" ? "B" : "A") : key); }
    Point Spot(string key) {
        bool ps = Kind == PadKind.PlayStation;
        switch (key) {
            case "A": return new Point(300, 135); case "B": return new Point(326, 109); case "X": return new Point(274, 109); case "Y": return new Point(300, 83);
            case "LB": return new Point(100, 34); case "RB": return new Point(300, 34);
            case "Move": return ps ? new Point(100, 109) : new Point(150, 160);
            case "LS": return ps ? new Point(150, 170) : new Point(100, 109);
            case "RS": return ps ? new Point(250, 170) : new Point(250, 160);
            case "⧉": return ps ? new Point(150, 70) : new Point(172, 104);
            case "≡": return ps ? new Point(250, 70) : new Point(228, 104);
        }
        return new Point(200, 120);
    }

    protected override void OnRender(DrawingContext dc) {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        // the controller sits in the middle third when there are callouts
        double cw = Callouts ? ActualWidth * 0.46 : ActualWidth, k = Math.Min(cw / 400, ActualHeight / 260 * (Callouts ? 0.82 : 1));
        double ox = (ActualWidth - 400 * k) / 2, oy = (ActualHeight - 260 * k) / 2;
        Func<Point, Point> at = p => new Point(ox + p.X * k, oy + p.Y * k);
        if (Callouts) Lines(dc, at);
        dc.PushTransform(new TranslateTransform(ox, oy));
        dc.PushTransform(new ScaleTransform(k, k));
        Draw(dc);
        dc.Pop(); dc.Pop();
    }

    static Brush B(string hex) { return Ui.Brush(hex); }
    static Pen P(Brush b, double w) { var p = new Pen(b, w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; p.Freeze(); return p; }
    bool On(int bit) { return (Pressed & bit) != 0; }

    void Draw(DrawingContext dc) {
        var body = B(Connected ? "#1C252C" : "#11181D");
        var edge = P(B(Connected ? "#33424D" : "#26333C"), 3);
        var part = B("#0E1418"); var lit = B(Ui.Teal);
        // shoulders
        foreach (var s in new[] { -1, 1 }) {
            bool on = On(s < 0 ? Pad.LB : Pad.RB);
            dc.DrawRoundedRectangle(on ? lit : B("#26333C"), edge, new Rect(200 + s * 100 - 46, 22, 92, 26), 13, 13);
        }
        // the body
        Geometry shape;
        if (Kind == PadKind.PlayStation)
            shape = Geometry.Parse("M 70,46 L 330,46 C 362,46 380,70 386,104 L 396,186 C 402,228 376,250 346,236 C 322,224 300,196 280,196 L 120,196 C 100,196 78,224 54,236 C 24,250 -2,228 4,186 L 14,104 C 20,70 38,46 70,46 Z");
        else if (Kind == PadKind.Switch)
            shape = Geometry.Parse("M 80,44 L 320,44 C 360,44 384,74 388,116 L 394,190 C 398,232 368,250 340,234 C 316,220 296,196 270,196 L 130,196 C 104,196 84,220 60,234 C 32,250 2,232 6,190 L 12,116 C 16,74 40,44 80,44 Z");
        else
            shape = Geometry.Parse("M 86,44 C 120,40 150,52 200,52 C 250,52 280,40 314,44 C 356,48 378,80 388,124 C 398,170 402,214 380,234 C 356,254 326,236 306,212 C 292,196 276,190 260,190 L 140,190 C 124,190 108,196 94,212 C 74,236 44,254 20,234 C -2,214 2,170 12,124 C 22,80 44,48 86,44 Z");
        dc.DrawGeometry(body, edge, shape);

        // sticks
        foreach (var key in new[] { "LS", "RS" }) {
            var c = Spot(key); bool on = On(key == "LS" ? Pad.LS : Pad.RS);
            dc.DrawEllipse(part, null, c, 30, 30);
            dc.DrawEllipse(on ? lit : B("#26333C"), P(B("#33424D"), 2), c, 21, 21);
        }
        // d-pad
        var d = Spot("Move");
        dc.DrawEllipse(part, null, d, 32, 32);
        foreach (var arm in new[] { new { bit = Pad.Up, r = new Rect(d.X - 9, d.Y - 27, 18, 22) }, new { bit = Pad.Down, r = new Rect(d.X - 9, d.Y + 5, 18, 22) },
                                    new { bit = Pad.Left, r = new Rect(d.X - 27, d.Y - 9, 22, 18) }, new { bit = Pad.Right, r = new Rect(d.X + 5, d.Y - 9, 22, 18) } })
            dc.DrawRoundedRectangle(On(arm.bit) ? lit : B("#33424D"), null, arm.r, 4, 4);
        dc.DrawRectangle(B("#33424D"), null, new Rect(d.X - 9, d.Y - 9, 18, 18));
        // face buttons
        foreach (var key in new[] { "A", "B", "X", "Y" }) {
            int bit = key == "A" ? Pad.A : key == "B" ? Pad.B : key == "X" ? Pad.X : Pad.Y;
            var c = Spot(key);
            // the drawing is by position; the button at a position is named by where the launcher's key sits
            string named = PadStyle.ConfirmRight && (key == "A" || key == "B") ? (key == "A" ? "B" : "A") : key;
            bool on = On(named == "A" ? Pad.A : named == "B" ? Pad.B : bit);
            dc.DrawEllipse(on ? lit : B("#0E1418"), P(B(PadStyle.Colour(named)), 2.5), c, 13, 13);
            Text(dc, PadStyle.Label(named), c, 12, on ? "#0A0E11" : PadStyle.Colour(named), true);
        }
        // the small middle buttons
        foreach (var key in new[] { "⧉", "≡" }) {
            var c = Spot(key); bool on = On(key == "≡" ? Pad.Start : Pad.Back);
            if (Kind == PadKind.PlayStation) dc.DrawRoundedRectangle(on ? lit : B("#33424D"), null, new Rect(c.X - 5, c.Y - 10, 10, 20), 5, 5);
            else dc.DrawEllipse(on ? lit : B("#33424D"), null, c, 8, 8);
        }
        if (Kind == PadKind.PlayStation) dc.DrawRoundedRectangle(B("#26333C"), P(B("#33424D"), 2), new Rect(166, 54, 68, 46), 8, 8);
        if (Kind == PadKind.Xbox) dc.DrawEllipse(B("#33424D"), null, new Point(200, 76), 13, 13);
        if (Kind == PadKind.Switch) { dc.DrawEllipse(B("#33424D"), null, new Point(222, 140), 8, 8); dc.DrawRoundedRectangle(B("#33424D"), null, new Rect(170, 132, 15, 15), 3, 3); }
    }

    void Text(DrawingContext dc, string s, Point c, double size, string colour, bool centre) {
        var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), size, B(colour), 1.25);
        dc.DrawText(ft, centre ? new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2) : c);
    }

    // labels on both sides, each with a line to its button; the line and label light up while it's pressed
    void Lines(DrawingContext dc, Func<Point, Point> at) {
        var left = new List<string[]>(); var right = new List<string[]>();
        foreach (var a in Actions) (SpotOf(a[0]).X < 200 ? left : right).Add(a);
        Side(dc, at, left, true); Side(dc, at, right, false);
    }
    void Side(DrawingContext dc, Func<Point, Point> at, List<string[]> items, bool leftSide) {
        // top to bottom by where the buttons are
        items.Sort((a, b) => SpotOf(a[0]).Y.CompareTo(SpotOf(b[0]).Y));
        double rowH = Math.Min(54, (ActualHeight - 20) / Math.Max(1, items.Count));
        double top = (ActualHeight - rowH * items.Count) / 2;
        for (int i = 0; i < items.Count; i++) {
            string key = items[i][0], what = items[i][1];
            bool on = KeyOn(key);
            var target = at(SpotOf(key));
            double y = top + rowH * i + rowH / 2;
            double textX = leftSide ? 10 : ActualWidth * 0.77, lineX = leftSide ? ActualWidth * 0.22 : ActualWidth * 0.76;
            var pen = P(B(on ? Ui.Teal : "#33424D"), on ? 2.4 : 1.4);
            dc.DrawLine(pen, new Point(lineX, y), new Point((lineX + target.X) / 2, y));
            dc.DrawLine(pen, new Point((lineX + target.X) / 2, y), target);
            dc.DrawEllipse(B(on ? Ui.Teal : "#33424D"), null, target, 3, 3);
            // the badge, then what it does
            string label = key == "Move" ? "✛" : key == "RSc" ? "R" : PadStyle.Label(key);
            string col = key == "Move" ? "#8A9AA5" : PadStyle.Colour(key);
            var bx = new Point(leftSide ? lineX - 26 : lineX + 4, y);
            double bw = label.Length > 1 ? 34 : 24;
            if (leftSide) bx.X = lineX - bw - 4;
            dc.DrawRoundedRectangle(B("#1C252C"), P(B(on ? Ui.Teal : col), 1.5), new Rect(bx.X, y - 12, bw, 24), 12, 12);
            Text(dc, label, new Point(bx.X + bw / 2, y), 11, on ? Ui.Teal : col, true);
            var ft = new FormattedText(what, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI Variable Display Semibold, Segoe UI Semibold"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 13.5, B(on ? Ui.Teal : Ui.Text), 1.25);
            ft.MaxTextWidth = Math.Max(60, (leftSide ? bx.X - 14 : ActualWidth - (bx.X + bw + 10)));
            ft.TextAlignment = leftSide ? TextAlignment.Right : TextAlignment.Left;
            dc.DrawText(ft, leftSide ? new Point(4, y - ft.Height / 2) : new Point(bx.X + bw + 10, y - ft.Height / 2));
        }
    }
    bool KeyOn(string key) {
        switch (key) {
            case "A": return On(Pad.A); case "B": return On(Pad.B); case "X": return On(Pad.X); case "Y": return On(Pad.Y);
            case "LB": return On(Pad.LB); case "RB": return On(Pad.RB); case "LS": return On(Pad.LS); case "RS": return On(Pad.RS);
            case "≡": return On(Pad.Start); case "⧉": return On(Pad.Back);
            case "Move": return On(Pad.Up | Pad.Down | Pad.Left | Pad.Right);
        }
        return false;
    }
}
}
