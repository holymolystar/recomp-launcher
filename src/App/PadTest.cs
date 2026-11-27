// The controller test in Settings > Controller, under the drawing of the controller: what the controller sends,
// live. Both sticks as circles with a dot where the stick is (the dead zone drawn in the middle, the numbers beside,
// a warning when a stick at rest isn't in the middle: drift), how far round each stick has reached (turn it round
// the edge: a worn stick doesn't reach the whole circle), both triggers as bars, every button as a light that's on
// while it's held and keeps a tick once it has been pressed (so you see which ones you haven't tried), and buttons
// to make it buzz (lightly, strongly, long).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RecompLauncher {

partial class Shell {
    class StickUi { public Canvas Area; public Ellipse Dot; public Path Reach; public TextBlock Text; public bool[] Hit = new bool[36]; public double RestX, RestY; public int Rest; }
    StickUi ptLeft, ptRight; Border ptLT, ptRT; TextBlock ptLTText, ptRTText, ptInfo, ptDrift;
    readonly Dictionary<int, Border> ptLights = new Dictionary<int, Border>(); readonly HashSet<int> ptTried = new HashSet<int>();
    DateTime ptLastTick = DateTime.MinValue; int ptTicks; double ptRate;
    const double PtR = 62;   // a stick circle's radius
    static readonly int[] PtButtons = { Pad.A, Pad.B, Pad.X, Pad.Y, Pad.LB, Pad.RB, Pad.LT, Pad.RT, Pad.LS, Pad.RS, Pad.Back, Pad.Start, Pad.Up, Pad.Down, Pad.Left, Pad.Right, Pad.Guide };
    static readonly string[] PtNames = { "A", "B", "X", "Y", "LB", "RB", "LT", "RT", "LS", "RS", "View", "Menu", "▲", "▼", "◀", "▶", "Home" };

    FrameworkElement BuildPadTest() {
        var box = new StackPanel();
        box.Children.Add(new TextBlock { Text = "TEST YOUR CONTROLLER", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal), Margin = new Thickness(0, 4, 0, 4) });
        ptInfo = new TextBlock { FontSize = 12, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            Text = "Press every button and turn both sticks round the edge. The lights keep a tick for each button that works." };
        box.Children.Add(ptInfo);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        ptLeft = MakeStick("Left stick"); ptRight = MakeStick("Right stick");
        row.Children.Add(StickCard(ptLeft, "Left stick"));
        // the triggers between the sticks
        var trig = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        trig.Children.Add(TriggerBar("LT", out ptLT, out ptLTText)); trig.Children.Add(TriggerBar("RT", out ptRT, out ptRTText));
        row.Children.Add(trig);
        row.Children.Add(StickCard(ptRight, "Right stick"));
        box.Children.Add(row);
        ptDrift = new TextBlock { FontSize = 12.5, FontFamily = Ui.Semi, Foreground = Ui.Brush(Ui.Gold), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        box.Children.Add(ptDrift);
        // every button
        var lights = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        for (int i = 0; i < PtButtons.Length; i++) {
            var t = new TextBlock { Text = PtNames[i], FontSize = 12.5, FontFamily = Ui.Semi, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { MinWidth = 50, Height = 34, CornerRadius = new CornerRadius(17), Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 7, 7), Child = t,
                Background = Ui.Brush("#1C252C"), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent };
            ptLights[PtButtons[i]] = b; lights.Children.Add(b);
        }
        box.Children.Add(lights);
        // buzz
        var buzz = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        Action<string, double, int> add = (text, s, ms) => { var bt = MakeButton(text, 34); bt.FontSize = 12.5; bt.Margin = new Thickness(0, 0, 8, 0); bt.Click += (o, e) => Pad.Rumble(s, ms); buzz.Children.Add(bt); extraStops.Add(bt); };
        add("Buzz lightly", 0.3, 250); add("Buzz strongly", 1.0, 350); add("Buzz long", 0.6, 1200);
        var reset = MakeButton("Start the test again", 34); reset.FontSize = 12.5; reset.Click += (o, e) => PadTestReset(); buzz.Children.Add(reset); extraStops.Add(reset);
        box.Children.Add(buzz);
        return new Border { Background = Ui.Brush("#0D1215"), CornerRadius = new CornerRadius(16), Padding = new Thickness(18, 14, 18, 16), Child = box, Margin = new Thickness(0, 0, 0, 14) };
    }
    StickUi MakeStick(string name) {
        var s = new StickUi();
        s.Area = new Canvas { Width = PtR * 2, Height = PtR * 2 };
        s.Area.Children.Add(new Ellipse { Width = PtR * 2, Height = PtR * 2, Fill = Ui.Brush("#11181D"), Stroke = Ui.Brush("#33424D"), StrokeThickness = 2 });
        // how far round it has reached: a ring of 36 pieces, lit as the stick reaches the edge there
        s.Reach = new Path { Stroke = Ui.Brush(Ui.Teal), StrokeThickness = 4, Opacity = 0.9 };
        s.Area.Children.Add(s.Reach);
        // the dead zone in the middle, and the cross
        double dz = PtR * 0.24;
        var dzEl = new Ellipse { Width = dz * 2, Height = dz * 2, Stroke = Ui.Brush("#5F717C"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 } };
        Canvas.SetLeft(dzEl, PtR - dz); Canvas.SetTop(dzEl, PtR - dz); s.Area.Children.Add(dzEl);
        s.Area.Children.Add(new Line { X1 = PtR, Y1 = 8, X2 = PtR, Y2 = PtR * 2 - 8, Stroke = Ui.Brush("#26333C"), StrokeThickness = 1 });
        s.Area.Children.Add(new Line { X1 = 8, Y1 = PtR, X2 = PtR * 2 - 8, Y2 = PtR, Stroke = Ui.Brush("#26333C"), StrokeThickness = 1 });
        s.Dot = new Ellipse { Width = 18, Height = 18, Fill = Ui.Brush(Ui.Teal), Stroke = Brushes.White, StrokeThickness = 2 };
        Canvas.SetLeft(s.Dot, PtR - 9); Canvas.SetTop(s.Dot, PtR - 9); s.Area.Children.Add(s.Dot);
        s.Text = new TextBlock { FontSize = 11.5, Foreground = Ui.Brush(Ui.Muted), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), FontFamily = new FontFamily("Consolas") };
        return s;
    }
    FrameworkElement StickCard(StickUi s, string name) {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = name, FontSize = 12, FontFamily = Ui.Semi, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });
        sp.Children.Add(s.Area); sp.Children.Add(s.Text);
        return sp;
    }
    FrameworkElement TriggerBar(string name, out Border fill, out TextBlock text) {
        var sp = new StackPanel { Margin = new Thickness(6, 0, 6, 0) };
        sp.Children.Add(new TextBlock { Text = name, FontSize = 12, FontFamily = Ui.Semi, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });
        fill = new Border { Height = 0, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(9), Background = Ui.Gradient(Ui.Teal, Ui.Gold) };
        sp.Children.Add(new Border { Width = 26, Height = PtR * 2 - 10, CornerRadius = new CornerRadius(10), Background = Ui.Brush("#11181D"), BorderBrush = Ui.Brush("#33424D"), BorderThickness = new Thickness(2), Child = fill, Padding = new Thickness(2) });
        text = new TextBlock { FontSize = 11.5, Foreground = Ui.Brush(Ui.Muted), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), FontFamily = new FontFamily("Consolas"), Text = "0%" };
        sp.Children.Add(text);
        return sp;
    }
    void PadTestReset() {
        ptTried.Clear();
        foreach (var s in new[] { ptLeft, ptRight }) { for (int i = 0; i < s.Hit.Length; i++) s.Hit[i] = false; s.Reach.Data = null; s.Rest = 0; }
        foreach (var kv in ptLights) kv.Value.BorderBrush = Brushes.Transparent;
        ptDrift.Text = ""; Sound.Play(Sfx.Select);
    }
    // every tick while Settings > Controller is on screen
    void PadTestShow(Pad.State st, int b) {
        if (ptLeft == null || !ptLeft.Area.IsVisible) return;
        var now = DateTime.Now; ptTicks++;
        if ((now - ptLastTick).TotalSeconds >= 1) { ptRate = ptTicks / Math.Max(0.5, (now - ptLastTick).TotalSeconds); ptTicks = 0; ptLastTick = now; }
        ShowStick(ptLeft, st.LX, st.LY); ShowStick(ptRight, st.RX, st.RY);
        ptLT.Height = Math.Max(0, (PtR * 2 - 18) * st.LT / 255.0); ptRT.Height = Math.Max(0, (PtR * 2 - 18) * st.RT / 255.0);
        ptLTText.Text = Math.Round(st.LT * 100 / 255.0) + "%"; ptRTText.Text = Math.Round(st.RT * 100 / 255.0) + "%";
        b = st.Buttons | (st.LT > 120 ? Pad.LT : 0) | (st.RT > 120 ? Pad.RT : 0);   // the buttons as pressed, not as the launcher reads them
        foreach (var kv in ptLights) {
            bool on = (b & kv.Key) != 0; if (on) ptTried.Add(kv.Key);
            kv.Value.Background = on ? Ui.Brush(Ui.Teal) : Ui.Brush("#1C252C");
            ((TextBlock)kv.Value.Child).Foreground = on ? Ui.Brush("#0A0E11") : Ui.Brush(Ui.Text);
            kv.Value.BorderBrush = ptTried.Contains(kv.Key) && !on ? Ui.Brush(Ui.Teal) : Brushes.Transparent;
        }
        int left = PtButtons.Length - ptTried.Count;
        var drift = new List<string>();
        foreach (var s in new[] { new { S = ptLeft, N = "left" }, new { S = ptRight, N = "right" } })
            if (s.S.Rest > 40 && Math.Sqrt(s.S.RestX * s.S.RestX + s.S.RestY * s.S.RestY) > 0.12) drift.Add("The " + s.N + " stick isn't in the middle at rest (" + Math.Round(Math.Sqrt(s.S.RestX * s.S.RestX + s.S.RestY * s.S.RestY) * 100) + "%): it drifts. Settings > Controller > Stick sensitivity: Low ignores it.");
        ptDrift.Text = string.Join("\n", drift);
        ptInfo.Text = (left == 0 ? "Every button works." : left + " button" + (left == 1 ? "" : "s") + " not tried yet.") + "   Left stick round: " + Coverage(ptLeft) + "%   Right stick round: " + Coverage(ptRight) + "%   (" + Math.Round(ptRate) + " readings a second)";
    }
    static int Coverage(StickUi s) { return (int)Math.Round(100.0 * s.Hit.Count(h => h) / s.Hit.Length); }
    void ShowStick(StickUi s, short rx, short ry) {
        double x = rx / 32768.0, y = -ry / 32768.0, m = Math.Sqrt(x * x + y * y);
        Canvas.SetLeft(s.Dot, PtR - 9 + x * (PtR - 9)); Canvas.SetTop(s.Dot, PtR - 9 + y * (PtR - 9));
        s.Dot.Fill = m < 0.24 ? Ui.Brush("#5F717C") : Ui.Brush(Ui.Teal);
        s.Text.Text = string.Format("{0,4:0} {1,4:0}", x * 100, -y * 100);
        // at rest (no other button, a small push only): its average place
        if (m < 0.3) { s.Rest++; double k = 1.0 / Math.Min(s.Rest, 60); s.RestX += (x - s.RestX) * k; s.RestY += (y - s.RestY) * k; }
        // near the edge: that piece of the ring is reached
        if (m > 0.9) {
            int piece = (int)((Math.Atan2(y, x) + Math.PI) / (2 * Math.PI) * s.Hit.Length) % s.Hit.Length;
            if (!s.Hit[piece]) {
                s.Hit[piece] = true;
                var g = new GeometryGroup();
                for (int i = 0; i < s.Hit.Length; i++) {
                    if (!s.Hit[i]) continue;
                    double a0 = i * 2 * Math.PI / s.Hit.Length - Math.PI, a1 = (i + 1) * 2 * Math.PI / s.Hit.Length - Math.PI, r = PtR - 4;
                    var fig = new PathFigure { StartPoint = new Point(PtR + Math.Cos(a0) * r, PtR + Math.Sin(a0) * r) };
                    fig.Segments.Add(new ArcSegment(new Point(PtR + Math.Cos(a1) * r, PtR + Math.Sin(a1) * r), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
                    var pg = new PathGeometry(); pg.Figures.Add(fig); g.Children.Add(pg);
                }
                s.Reach.Data = g;
            }
        }
    }
}
}
