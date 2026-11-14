// The plaza (My character on Home): every profile's character roams a round paved square, its name over its head.
// - They wander freely, each in its own way: tall ones take long slow strides, short ones quick little steps, and
//   they bob, sway and swing their arms as they go.
// - Grab one with the mouse to pick it up (it kicks its legs, arms up), and let go to throw it: it flies, spins,
//   bounces, sits dazed a moment and walks on. With a controller, LB tosses the picked one into the air.
// - Click one (or A) for its profile: the character, its play time in all and game by game (most played first, with
//   when last played), and Edit character / Play as.
// - Round buttons along the bottom: New (a new profile, straight into the editor), Who's playing, Back.
// Each character is drawn once as standing, walking (8 steps) and held (4 kicks) pictures. The floor is drawn once.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RecompLauncher {

partial class Shell {
    enum WalkerMode { Wait, Walk, Held, Flying, Dazed }
    class Walker {
        public PlayerProfile P; public Grid Root; public Avatar3DView View; public TextBlock Name; public Border Tag; public Ellipse Shadow;
        public ScaleTransform Flip; public RotateTransform Spin;
        public ImageSource Stand; public ImageSource[] Steps, Kicks;
        public WalkerMode Mode;
        public double X, Y, Z;              // on the floor (0..1 across, 0..1 front to back = far to near) and height (px)
        public double Tx, Ty, Wait, Phase;
        public double Speed, Cadence;       // its own way of walking
        public double Vx, Vz, Vy, Turn;     // while flying (floor units / s, px / s, depth / s, degrees / s)
        public bool Hop;                    // a little hop of its own (it lands on its feet, not dazed)
    }
    Grid plazaLayer; Canvas plazaField; Image plazaFloor; readonly List<Walker> walkers = new List<Walker>(); DispatcherTimer plazaTimer; DateTime plazaLast;
    int plazaIdx; readonly Random plazaRnd = new Random();
    Walker grabbed; Point grabAt; bool grabMoved; readonly List<Tuple<DateTime, Point>> grabTrail = new List<Tuple<DateTime, Point>>();
    const double WalkerW = 190, WalkerH = 285;
    bool PlazaOpen { get { return plazaLayer != null && plazaLayer.Visibility == Visibility.Visible; } }

    void OpenPlaza() {
        if (PlazaOpen) return;
        if (plazaLayer == null) BuildPlaza();
        FillPlaza();
        plazaLayer.Visibility = Visibility.Visible;
        plazaLayer.BeginAnimation(UIElement.OpacityProperty, Anim(1, 260, 0));
        Sound.Play(Sfx.Open);
        plazaLast = DateTime.Now; plazaTimer.Start();
        HideRing(); SetHints();
    }
    void ClosePlaza() {
        if (!PlazaOpen) return;
        if (PlazaCardOpen) ClosePlazaCard();
        plazaTimer.Stop();
        Sound.Play(Sfx.Back);
        var a = Anim(0, 180);
        a.Completed += (s, e) => { plazaLayer.Visibility = Visibility.Collapsed; plazaField.Children.Clear(); walkers.Clear(); };
        plazaLayer.BeginAnimation(UIElement.OpacityProperty, a);
        SetHints();
    }
    static Brush Flat(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    static Brush Grad(string top, string bottom) {
        var b = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(top), (Color)ColorConverter.ConvertFromString(bottom), 90); b.Freeze(); return b;
    }

    // ---------- the square ----------
    // round rings of soft rounded tiles, each turned along its ring, seen a little from above
    static ImageSource PavingFloor() {
        var dg = new DrawingGroup();
        using (var dc = dg.Open()) {
            dc.DrawRectangle(Flat("#fbfcfc"), null, new Rect(-1100, -700, 2200, 1400));
            var rnd = new Random(3);
            string[] tints = { "#e7ece9", "#e9e7e2", "#e3e9ec", "#ebe8e4", "#e5ebe6" };
            double tile = 54, gap = 14;
            for (int ring = 0; ring < 22; ring++) {
                double r = ring == 0 ? 0 : ring * (tile + gap) - 10;
                int n = ring == 0 ? 1 : (int)Math.Round(2 * Math.PI * r / (tile + gap));
                for (int k = 0; k < n; k++) {
                    double ang = 2 * Math.PI * k / n + ring * 0.37;
                    double x = r * Math.Cos(ang), y = r * Math.Sin(ang);
                    dc.PushTransform(new TranslateTransform(x, y));
                    dc.PushTransform(new RotateTransform(ang * 180 / Math.PI + 45 * (ring % 2)));
                    double size = tile * (0.9 + rnd.NextDouble() * 0.08);
                    dc.DrawRoundedRectangle(Flat(tints[rnd.Next(tints.Length)]), null, new Rect(-size / 2, -size / 2, size, size), 7, 7);
                    dc.Pop(); dc.Pop();
                }
            }
        }
        dg.Freeze();
        var img = new DrawingImage(dg); img.Freeze();
        return img;
    }
    void BuildPlaza() {
        plazaLayer = new Grid { Visibility = Visibility.Collapsed, Opacity = 0, ClipToBounds = true, Background = Flat("#fbfcfc") };
        Panel.SetZIndex(plazaLayer, 40);
        // the paved square, squashed for a view from above at an angle, with white haze far away
        plazaFloor = new Image { Source = PavingFloor(), Stretch = Stretch.UniformToFill, RenderTransformOrigin = new Point(0.5, 0.62),
            RenderTransform = new ScaleTransform(1.3, 0.8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        plazaLayer.Children.Add(plazaFloor);
        var haze = new Border { Height = 200, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        var hz = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        hz.GradientStops.Add(new GradientStop(Color.FromArgb(255, 252, 253, 253), 0)); hz.GradientStops.Add(new GradientStop(Color.FromArgb(0, 252, 253, 253), 1));
        haze.Background = hz; plazaLayer.Children.Add(haze);

        plazaField = new Canvas { Background = Brushes.Transparent };
        plazaField.MouseMove += PlazaMouseMove;
        plazaField.MouseLeftButtonUp += PlazaMouseUp;
        plazaField.LostMouseCapture += (s, e) => { if (grabbed != null) Throw(grabbed, 0, 0); };
        plazaLayer.Children.Add(plazaField);

        var top = new StackPanel { Margin = new Thickness(60, 34, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        top.Children.Add(new TextBlock { Text = "Characters", FontSize = 30, FontFamily = Ui.Semi, Foreground = Flat("#5b6670") });
        top.Children.Add(new TextBlock { Text = "Click one for its profile. Grab one to pick it up, and throw it.", FontSize = 14, Foreground = Flat("#8a959e"), Margin = new Thickness(0, 2, 0, 0) });
        plazaLayer.Children.Add(top);

        // the round buttons along the bottom, on a white bar
        var barBack = new Border { Height = 132, VerticalAlignment = VerticalAlignment.Bottom, Background = Grad("#f7f9fa", "#e6ebef"), BorderBrush = Flat("#cfd7dd"), BorderThickness = new Thickness(0, 2, 0, 0) };
        plazaLayer.Children.Add(barBack);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 16) };
        foreach (var spec in new[] { new[] { "", "New" }, new[] { "", "Who's playing" }, new[] { "", "Back" } }) {
            var col = new StackPanel { Margin = new Thickness(26, 0, 26, 0), Cursor = Cursors.Hand };
            var circle = new Border { Width = 76, Height = 76, CornerRadius = new CornerRadius(38), Background = Grad("#ffffff", "#eef2f5"), BorderBrush = Flat("#c3ccd3"), BorderThickness = new Thickness(3),
                Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.18, Color = Colors.Black },
                Child = new TextBlock { Text = spec[0], FontFamily = Ui.Mdl, FontSize = 28, Foreground = Flat("#34beed"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            col.Children.Add(circle);
            col.Children.Add(new TextBlock { Text = spec[1], FontSize = 13.5, FontFamily = Ui.Semi, Foreground = Flat("#6a757e"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
            string what = spec[1];
            col.MouseLeftButtonUp += (s, e) => PlazaButton(what);
            col.MouseEnter += (s, e) => circle.BorderBrush = Flat("#34beed");
            col.MouseLeave += (s, e) => circle.BorderBrush = Flat("#c3ccd3");
            bar.Children.Add(col);
        }
        plazaLayer.Children.Add(bar);

        plazaLayer.LayoutTransform = N<Grid>("Main").LayoutTransform;
        N<Grid>("Clipped").Children.Add(plazaLayer);
        plazaTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        plazaTimer.Tick += (s, e) => PlazaTick();
        plazaLayer.SizeChanged += (s, e) => { foreach (var w in walkers) PlaceWalker(w); };
    }
    void PlazaButton(string what) {
        if (what == "New") { PlazaNew(); return; }
        if (what == "Who's playing") { ClosePlaza(); OpenPlayers(); return; }
        ClosePlaza();
    }
    // a new profile with a character of its own, straight into the editor
    void PlazaNew() {
        var p = Profiles.Add(plazaRnd);
        Profiles.Switch(p); ApplyAvatar(); RefreshForProfile();
        Sound.Play(Sfx.Select);
        plazaReturn = true;
        ClosePlaza();
        OpenCreator();
    }

    // ---------- the characters ----------
    void FillPlaza() {
        plazaField.Children.Clear(); walkers.Clear();
        int n = Profiles.All.Count;
        for (int i = 0; i < n; i++) {
            var p = Profiles.All[i];
            var def = AvatarDef.Parse(p.Character);
            // its own way of walking: tall ones stride long and slow, short ones take quick little steps
            double hgt = def.D("height"), bld = def.D("build");
            var w = new Walker { P = p, Speed = 0.07 + 0.05 * hgt + plazaRnd.NextDouble() * 0.02, Cadence = 10.5 - 3.2 * hgt + (bld - 1) * -1.5 + plazaRnd.NextDouble() };
            w.Flip = new ScaleTransform(1, 1); w.Spin = new RotateTransform(0);
            var tg = new TransformGroup(); tg.Children.Add(w.Flip); tg.Children.Add(w.Spin);
            // the character itself, in 3D and moving (Avatar3D.cs)
            w.View = new Avatar3DView { Width = WalkerW, Height = WalkerH, Shadow = false, Zoom = 0.84, RenderTransformOrigin = new Point(0.5, 0.62), RenderTransform = tg, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
            w.View.SetDef(def);
            w.Name = new TextBlock { Text = Profiles.DisplayName(p), FontSize = 15, FontFamily = Ui.Semi, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(12, 3, 12, 4) };
            w.Tag = new Border { CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Child = w.Name, Margin = new Thickness(0, 26, 0, 0), BorderThickness = new Thickness(2) };
            w.Root = new Grid { Width = WalkerW, Height = WalkerH, Cursor = Cursors.Hand, RenderTransformOrigin = new Point(0.5, 1), RenderTransform = new ScaleTransform(1, 1), Background = Brushes.Transparent };
            w.Root.Children.Add(w.View); w.Root.Children.Add(w.Tag);
            w.Shadow = new Ellipse { Width = 110, Height = 22, IsHitTestVisible = false, Fill = new RadialGradientBrush(Color.FromArgb(80, 30, 40, 50), Color.FromArgb(0, 30, 40, 50)) };
            var walker = w; int idx = i;
            w.Root.MouseLeftButtonDown += (s, e) => { plazaIdx = idx; MarkPlaza(); StartGrab(walker, e.GetPosition(plazaField)); e.Handled = true; };
            w.Root.MouseEnter += (s, e) => { if (grabbed == null && plazaIdx != idx) { plazaIdx = idx; MarkPlaza(); } };
            // spread out to begin with
            w.X = n == 1 ? 0.5 : 0.15 + 0.7 * i / (n - 1); w.Y = 0.3 + plazaRnd.NextDouble() * 0.55;
            w.Tx = w.X; w.Ty = w.Y; w.Wait = 0.3 + plazaRnd.NextDouble() * 1.5; w.Mode = WalkerMode.Wait;
            walkers.Add(w); plazaField.Children.Add(w.Shadow); plazaField.Children.Add(w.Root);
            PlaceWalker(w);
        }
        plazaIdx = Math.Max(0, Profiles.All.IndexOf(Profiles.Current));
        MarkPlaza();
    }
    double PlazaW { get { return plazaLayer.ActualWidth > 0 ? plazaLayer.ActualWidth : 1280; } }
    double PlazaH { get { return plazaLayer.ActualHeight > 0 ? plazaLayer.ActualHeight : 720; } }
    double ScaleAt(double y) { return 0.5 + 0.48 * y; }
    double FeetAt(double y) { return PlazaH * (0.42 + 0.36 * y); }
    double ScreenX(double x) { return PlazaW * (0.07 + 0.86 * x); }
    // where a walker is on the screen: further back (smaller Y) is higher up, smaller and behind; Z lifts it
    void PlaceWalker(Walker w) {
        double scale = ScaleAt(w.Y), sx = ScreenX(w.X), feet = FeetAt(w.Y);
        var st = (ScaleTransform)w.Root.RenderTransform; st.ScaleX = st.ScaleY = scale;
        Canvas.SetLeft(w.Root, sx - WalkerW / 2); Canvas.SetTop(w.Root, feet - WalkerH * 0.985 - w.Z);
        Panel.SetZIndex(w.Root, w.Mode == WalkerMode.Held || w.Mode == WalkerMode.Flying ? 5000 : (int)(w.Y * 1000) + 10);
        // its shadow stays on the floor, smaller and fainter the higher it is
        double fade = Math.Max(0.25, 1 - w.Z / 500);
        w.Shadow.Width = 110 * scale * fade; w.Shadow.Height = 22 * scale * fade; w.Shadow.Opacity = fade;
        Canvas.SetLeft(w.Shadow, sx - w.Shadow.Width / 2); Canvas.SetTop(w.Shadow, feet - w.Shadow.Height / 2 - 4 * scale);
        Panel.SetZIndex(w.Shadow, (int)(w.Y * 1000));
    }
    // turns a walker smoothly to face a direction (degrees: 0 the front, 90 to the right)
    static void TurnTo(Walker w, double yaw, double dt) {
        double d = yaw - w.View.Yaw;
        while (d > 180) d -= 360; while (d < -180) d += 360;
        w.View.Yaw += d * Math.Min(1, dt * 7);
    }
    void PlazaTick() {
        double dt = Math.Min(0.05, (DateTime.Now - plazaLast).TotalSeconds); plazaLast = DateTime.Now;
        foreach (var w in walkers) {
            switch (w.Mode) {
                case WalkerMode.Held:
                    w.Phase += dt * 14;
                    w.View.Anim = Anim3D.Held; TurnTo(w, 0, dt);
                    w.Spin.Angle = Math.Sin(w.Phase * 0.5) * 8;   // dangling
                    break;
                case WalkerMode.Flying: {
                    w.Phase += dt * 16;
                    if (!w.Hop) w.View.Anim = Anim3D.Held;
                    w.X += w.Vx * dt; w.Y = Math.Max(0, Math.Min(1, w.Y + w.Vy * dt));
                    w.Vz -= 2600 * dt; w.Z += w.Vz * dt;
                    w.Spin.Angle += w.Turn * dt;
                    // the sides of the square: it bounces back
                    if (w.X < 0) { w.X = 0; w.Vx = -w.Vx * 0.5; } else if (w.X > 1) { w.X = 1; w.Vx = -w.Vx * 0.5; }
                    if (w.Z <= 0) {
                        w.Z = 0;
                        if (w.Hop) { w.Hop = false; w.Mode = WalkerMode.Wait; w.Wait = 3; w.Vz = 0; break; }
                        if (Math.Abs(w.Vz) > 260) { w.Vz = -w.Vz * 0.38; w.Vx *= 0.6; w.Vy *= 0.6; w.Turn *= 0.5; Sound.Play(Sfx.Bump); }
                        else { w.Mode = WalkerMode.Dazed; w.Wait = 1.6; w.View.Anim = Anim3D.Dazed; w.Vx = w.Vy = 0; }
                    }
                    break;
                }
                case WalkerMode.Dazed:
                    // back on its feet: it wobbles a moment, then walks on
                    w.Wait -= dt;
                    w.Spin.Angle = w.Spin.Angle * 0.85 + Math.Sin(DateTime.Now.TimeOfDay.TotalSeconds * 9) * 6 * Math.Min(1, w.Wait);
                    if (w.Wait <= 0) { w.Spin.Angle = 0; w.Mode = WalkerMode.Wait; w.Wait = 0.3; }
                    break;
                case WalkerMode.Wait:
                    w.Wait -= dt;
                    if (w.View.Anim == Anim3D.Walk || w.View.Anim == Anim3D.Held || w.View.Anim == Anim3D.Dazed) w.View.Anim = Anim3D.Idle;
                    TurnTo(w, 0, dt);
                    w.Spin.Angle *= 0.8;
                    if (w.Wait <= 0) { w.Tx = 0.04 + plazaRnd.NextDouble() * 0.92; w.Ty = 0.05 + plazaRnd.NextDouble() * 0.95; w.Phase = 0; w.Mode = WalkerMode.Walk; }
                    break;
                case WalkerMode.Walk: {
                    double dx = w.Tx - w.X, dy = (w.Ty - w.Y) * 0.6, dist = Math.Sqrt(dx * dx + dy * dy), step = w.Speed * dt;
                    if (dist <= step) { w.X = w.Tx; w.Y = w.Ty; w.Mode = WalkerMode.Wait; w.Wait = 0.8 + plazaRnd.NextDouble() * 3; w.Spin.Angle = 0; break; }
                    w.X += dx / dist * step; w.Y += dy / dist * step / 0.6;
                    // it turns to face where it's going (the steps and the bob are the 3D walk's own)
                    TurnTo(w, Math.Atan2(dx * PlazaW * 0.86, (w.Ty - w.Y) * PlazaH * 0.36) * 180 / Math.PI, dt);
                    w.View.Anim = Anim3D.Walk;
                    w.Phase += dt * w.Cadence;
                    w.Z = 0; w.Spin.Angle = 0;
                    break;
                }
            }
            PlaceWalker(w);
        }
    }

    // ---------- picking up and throwing ----------
    void StartGrab(Walker w, Point at) {
        grabbed = w; grabAt = at; grabMoved = false; grabTrail.Clear();
        grabTrail.Add(Tuple.Create(DateTime.Now, at));
        plazaField.CaptureMouse();
    }
    void PlazaMouseMove(object s, MouseEventArgs e) {
        if (grabbed == null) return;
        var at = e.GetPosition(plazaField);
        if (!grabMoved && (at - grabAt).Length < 7) return;
        if (!grabMoved) { grabMoved = true; grabbed.Mode = WalkerMode.Held; grabbed.Flip.ScaleX = 1; Sound.Play(Sfx.Select); }
        grabTrail.Add(Tuple.Create(DateTime.Now, at));
        while (grabTrail.Count > 6) grabTrail.RemoveAt(0);
        // held by the head: the floor spot under it follows the mouse across, the height follows it up
        var w = grabbed;
        w.X = Math.Max(0, Math.Min(1, (at.X / PlazaW - 0.07) / 0.86));
        double scale = ScaleAt(w.Y);
        w.Z = Math.Max(0, FeetAt(w.Y) - at.Y - WalkerH * scale * 0.72);
        PlaceWalker(w);
    }
    void PlazaMouseUp(object s, MouseButtonEventArgs e) {
        if (grabbed == null) return;
        var w = grabbed;
        plazaField.ReleaseMouseCapture();
        if (!grabMoved) { grabbed = null; Sound.Play(Sfx.Select); OpenPlazaCard(w); return; }   // a click: its profile
        // the throw: how fast the mouse was going as it let go
        double vx = 0, vy = 0;
        if (grabTrail.Count >= 2) {
            var a = grabTrail[0]; var b = grabTrail[grabTrail.Count - 1];
            double t = Math.Max(0.016, (b.Item1 - a.Item1).TotalSeconds);
            vx = (b.Item2.X - a.Item2.X) / t; vy = (b.Item2.Y - a.Item2.Y) / t;
        }
        Throw(w, vx, vy);
    }
    // off it goes (screen px / s): across as it was thrown, up as high as it was thrown up
    void Throw(Walker w, double vx, double vy) {
        grabbed = null;
        w.Mode = WalkerMode.Flying; w.Hop = false;
        w.Vx = Math.Max(-2.5, Math.Min(2.5, vx / PlazaW / 0.86));
        w.Vz = Math.Max(-200, Math.Min(1800, -vy));
        w.Vy = (plazaRnd.NextDouble() - 0.5) * 0.3;
        w.Turn = Math.Max(-900, Math.Min(900, vx * 0.6));
        if (Math.Abs(vx) + Math.Abs(vy) > 600) Sound.Play(Sfx.Launch);
    }
    // the controller's way: LB throws the picked one up in the air
    void TossPicked() {
        if (plazaIdx < 0 || plazaIdx >= walkers.Count) return;
        var w = walkers[plazaIdx];
        if (w.Mode == WalkerMode.Flying) return;
        w.Mode = WalkerMode.Flying; w.Hop = false; w.Vz = 900 + plazaRnd.NextDouble() * 500; w.Vx = (plazaRnd.NextDouble() - 0.5) * 0.8; w.Vy = 0; w.Turn = (plazaRnd.NextDouble() - 0.5) * 900;
        Sound.Play(Sfx.Launch);
    }

    void MarkPlaza() {
        for (int i = 0; i < walkers.Count; i++) {
            bool on = i == plazaIdx; var w = walkers[i];
            w.Tag.Background = on ? Flat("#34beed") : Flat("#ffffff");
            w.Tag.BorderBrush = on ? Flat("#1a9ccc") : Flat("#cfd7dd");
            w.Name.Foreground = on ? Brushes.White : Flat("#5b6670");
            if (on && (w.Mode == WalkerMode.Walk || w.Mode == WalkerMode.Wait)) {
                // it stops, turns to you and gives a little hop
                w.Mode = WalkerMode.Wait; w.Wait = 3; w.Flip.ScaleX = 1; w.Spin.Angle = 0; w.View.Yaw = 0; w.View.Play(Anim3D.Wave, 2.4);
                w.Mode = WalkerMode.Flying; w.Hop = true; w.Vz = 380; w.Vx = 0; w.Vy = 0; w.Turn = 0;
            }
        }
    }
    void PlazaPad(int bit) {
        if (PlazaCardOpen) { PlazaCardPad(bit); return; }
        if (bit == Pad.B) { ClosePlaza(); return; }
        if (bit == Pad.A) { if (plazaIdx >= 0 && plazaIdx < walkers.Count) { Sound.Play(Sfx.Select); OpenPlazaCard(walkers[plazaIdx]); } return; }
        if (bit == Pad.Y) { ClosePlaza(); OpenPlayers(); return; }
        if (bit == Pad.X) { PlazaNew(); return; }
        if (bit == Pad.LB) { TossPicked(); return; }
        if (walkers.Count == 0) return;
        // left / right: the next character that way on the screen
        if (bit == Pad.Left || bit == Pad.Right) {
            var order = walkers.Select((w, i) => new { w.X, i }).OrderBy(a => a.X).Select(a => a.i).ToList();
            int at = order.IndexOf(plazaIdx);
            plazaIdx = order[(at + (bit == Pad.Right ? 1 : -1) + order.Count) % order.Count];
            Sound.Play(Sfx.Move); MarkPlaza();
        }
    }

    // ---------- a character's profile: its play time ----------
    Border plazaCard; StackPanel pcBody; Walker pcFor; Button pcEdit, pcPlay, pcClose; int pcIdx;
    bool PlazaCardOpen { get { return plazaCard != null && plazaCard.Visibility == Visibility.Visible; } }
    void OpenPlazaCard(Walker w) {
        pcFor = w;
        if (plazaCard == null) {
            pcBody = new StackPanel();
            plazaCard = new Border { Width = 560, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(22),
                Background = Flat("#ffffff"), BorderBrush = Flat("#cfd7dd"), BorderThickness = new Thickness(2), Padding = new Thickness(28, 24, 28, 22), Child = pcBody,
                Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 4, Opacity = 0.25, Color = Colors.Black } };
            Panel.SetZIndex(plazaCard, 6000);
            plazaLayer.Children.Add(plazaCard);
        }
        pcBody.Children.Clear();
        var p = w.P;
        // the top: the character's picture, its name and its play time in all
        var head = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) }); head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.Children.Add(new Border { Width = 84, Height = 84, CornerRadius = new CornerRadius(42), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left,
            Child = new Image { Source = AvatarArt.Portrait(AvatarDef.Parse(p.Character), 168) } });
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = Profiles.DisplayName(p), FontSize = 24, FontFamily = Ui.Semi, Foreground = Flat("#3c4750") });
        string top; double total = Playtime.TotalIn(Profiles.PlaytimeFile(p), out top);
        title.Children.Add(new TextBlock { Text = total > 60 ? Playtime.Format(total) + " played in all" : "Hasn't played anything yet", FontSize = 14, Foreground = Flat("#7a858e") });
        Grid.SetColumn(title, 1); head.Children.Add(title);
        pcBody.Children.Add(head);
        // this week, day by day (from the message board's diary)
        var week = Board.Week(p.Id); double wmax = Math.Max(600, week.Max());
        var chart = new Grid { Height = 74, Margin = new Thickness(0, 0, 0, 14) };
        for (int k = 0; k < 7; k++) chart.ColumnDefinitions.Add(new ColumnDefinition());
        for (int k = 0; k < 7; k++) {
            var col = new Grid();
            col.RowDefinitions.Add(new RowDefinition()); col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var bar2 = new Border { Width = 26, CornerRadius = new CornerRadius(6, 6, 2, 2), VerticalAlignment = VerticalAlignment.Bottom, Height = Math.Max(3, 50 * week[k] / wmax),
                Background = Flat(k == 6 ? "#34beed" : "#a9dcf0"), ToolTip = Playtime.Format(week[k]) };
            col.Children.Add(bar2);
            var lbl = new TextBlock { Text = DateTime.Today.AddDays(k - 6).ToString("ddd").Substring(0, 2), FontSize = 11, Foreground = Flat("#8a959e"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            Grid.SetRow(lbl, 1); col.Children.Add(lbl);
            Grid.SetColumn(col, k); chart.Children.Add(col);
        }
        pcBody.Children.Add(new TextBlock { Text = "This week: " + Playtime.Format(week.Sum()), FontSize = 13, FontFamily = Ui.Semi, Foreground = Flat("#5b6670"), Margin = new Thickness(0, 0, 0, 6) });
        pcBody.Children.Add(chart);
        // game by game, most played first, with a bar for how much
        var games = Playtime.AllIn(Profiles.PlaytimeFile(p));
        double most = games.Count > 0 ? games[0].Item2 : 1;
        var list = new StackPanel();
        foreach (var t in games.Take(10)) {
            var g = Catalog.Find(t.Item1);
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            left.Children.Add(new TextBlock { Text = g != null ? g.Name : t.Item1, FontSize = 14.5, FontFamily = Ui.Semi, Foreground = Flat("#3c4750"), TextTrimming = TextTrimming.CharacterEllipsis });
            var track = new Grid { Height = 7, Margin = new Thickness(0, 5, 18, 0) };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = Flat("#e8edf0") });
            track.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = Flat("#34beed"), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(8, 330 * t.Item2 / most) });
            left.Children.Add(track);
            row.Children.Add(left);
            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(new TextBlock { Text = Playtime.Format(t.Item2), FontSize = 14, FontFamily = Ui.Semi, Foreground = Flat("#3c4750"), HorizontalAlignment = HorizontalAlignment.Right });
            if (t.Item3 != DateTime.MinValue) right.Children.Add(new TextBlock { Text = Playtime.When(t.Item3), FontSize = 11.5, Foreground = Flat("#8a959e"), HorizontalAlignment = HorizontalAlignment.Right });
            Grid.SetColumn(right, 1); row.Children.Add(right);
            list.Children.Add(row);
        }
        if (games.Count == 0) list.Children.Add(new TextBlock { Text = "Play a game as " + Profiles.DisplayName(p) + " and it shows up here.", FontSize = 13.5, Foreground = Flat("#8a959e"), Margin = new Thickness(0, 0, 0, 10) });
        pcBody.Children.Add(new ScrollViewer { MaxHeight = 330, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list, Focusable = false });
        // what to do: edit its character, play as it, close
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        pcEdit = MakeButton("Edit character", 42); pcEdit.Click += (s, e) => PlazaEdit(pcFor);
        pcPlay = MakeButton(Profiles.Current == p ? "Playing now" : "Play as " + Profiles.DisplayName(p), 42); pcPlay.Margin = new Thickness(10, 0, 0, 0);
        pcPlay.IsEnabled = Profiles.Current != p;
        pcPlay.Click += (s, e) => { Profiles.Switch(pcFor.P); ApplyAvatar(); RefreshForProfile(); Sound.Play(Sfx.Select); SetStep("Playing as " + Profiles.DisplayName(pcFor.P) + ".", Ui.Teal); ClosePlazaCard(); };
        pcClose = MakeButton("Close", 42); pcClose.Margin = new Thickness(10, 0, 0, 0); pcClose.Click += (s, e) => ClosePlazaCard();
        bar.Children.Add(pcEdit); bar.Children.Add(pcPlay); bar.Children.Add(pcClose);
        pcBody.Children.Add(bar);
        pcIdx = 0; MarkPlazaCard();
        plazaCard.Visibility = Visibility.Visible;
        plazaCard.BeginAnimation(UIElement.OpacityProperty, Anim(1, 200, 0));
        SetHints();
    }
    void ClosePlazaCard() { if (!PlazaCardOpen) return; plazaCard.Visibility = Visibility.Collapsed; Sound.Play(Sfx.Back); SetHints(); }
    Button[] PlazaCardButtons() { return new[] { pcEdit, pcPlay, pcClose }.Where(b => b.IsEnabled).ToArray(); }
    void MarkPlazaCard() {
        var bs = PlazaCardButtons();
        foreach (var b in new[] { pcEdit, pcPlay, pcClose }) b.Effect = null;
        if (padUsed && bs.Length > 0) bs[Math.Min(pcIdx, bs.Length - 1)].Effect = SetGlow;
    }
    void PlazaCardPad(int bit) {
        var bs = PlazaCardButtons();
        if (bit == Pad.B) { ClosePlazaCard(); return; }
        if (bit == Pad.A) { if (bs.Length > 0) Press(bs[Math.Min(pcIdx, bs.Length - 1)]); return; }
        int was = pcIdx;
        if (bit == Pad.Left) pcIdx = Math.Max(0, pcIdx - 1);
        if (bit == Pad.Right) pcIdx = Math.Min(bs.Length - 1, pcIdx + 1);
        if (pcIdx != was) { Sound.Play(Sfx.Move); MarkPlazaCard(); }
    }
    // its character in the editor; saving or leaving comes back to the plaza
    bool plazaReturn;
    void PlazaEdit(Walker w) {
        if (w == null) return;
        if (Profiles.Current != w.P) { Profiles.Switch(w.P); ApplyAvatar(); RefreshForProfile(); }
        plazaReturn = true;
        ClosePlaza();
        OpenCreator();
    }
}
}
