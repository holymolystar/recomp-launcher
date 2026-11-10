// The plaza (My character on Home): every profile's character walks around a little park, its name over its head.
// They stroll to a spot, stand a moment, and go on; the ones further back are smaller and behind the others.
// Click a character (or left / right and A) to edit that profile: its picture, name and character. Y asks who's
// playing; B goes back. Each character is drawn once as nine pictures (standing and eight steps of its walk).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RecompLauncher {

partial class Shell {
    class Walker {
        public PlayerProfile P; public StackPanel Root; public Image Img; public TextBlock Name; public ScaleTransform Flip; public TranslateTransform Bob;
        public ImageSource Stand; public ImageSource[] Steps;
        public double X, Y, Tx, Ty, Wait, Phase, Speed;
    }
    Grid plazaLayer; Canvas plazaField; readonly List<Walker> walkers = new List<Walker>(); DispatcherTimer plazaTimer; DateTime plazaLast;
    int plazaIdx; Button plazaEdit; readonly Random plazaRnd = new Random();
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
    void BuildPlaza() {
        bool day = Theme.Current.Light;
        plazaLayer = new Grid { Visibility = Visibility.Collapsed, Opacity = 0, ClipToBounds = true };
        // the park: sky, a sun or moon, hills at the horizon, grass, a pale path
        var scene = new Grid();
        scene.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46, GridUnitType.Star) });
        scene.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54, GridUnitType.Star) });
        scene.Children.Add(new Border { Background = day ? Grad("#8fd0f0", "#e4f5fb") : Grad("#141b33", "#34457a") });
        var sun = new Ellipse { Width = 90, Height = 90, Fill = Flat(day ? "#fff3b8" : "#e8ecf8"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 60, 150, 0),
            Effect = new DropShadowEffect { BlurRadius = 60, ShadowDepth = 0, Opacity = 0.9, Color = (Color)ColorConverter.ConvertFromString(day ? "#fff0a0" : "#b8c4ff") } };
        scene.Children.Add(sun);
        var hills = new Canvas { VerticalAlignment = VerticalAlignment.Bottom, Height = 0 };
        scene.Children.Add(hills);
        foreach (var h in new[] { new[] { -120.0, 520, 150 }, new[] { 300.0, 700, 200 }, new[] { 860.0, 600, 160 }, new[] { 1300.0, 760, 210 }, new[] { 1900.0, 640, 170 } }) {
            var hill = new Ellipse { Width = h[1], Height = h[2] * 2, Fill = Flat(day ? "#8cc98a" : "#27473f") };
            Canvas.SetLeft(hill, h[0]); Canvas.SetTop(hill, -h[2]);
            hills.Children.Add(hill);
        }
        var grass = new Border { Background = day ? Grad("#9bd88a", "#6fb863") : Grad("#2f5d48", "#1f4234") };
        Grid.SetRow(grass, 1); scene.Children.Add(grass);
        var path = new Ellipse { Fill = Flat(day ? "#e9dfc2" : "#4a5a5c"), Opacity = 0.75, Margin = new Thickness(140, 60, 140, 50) };
        Grid.SetRow(path, 1); scene.Children.Add(path);
        plazaLayer.Children.Add(scene);

        plazaField = new Canvas();
        plazaLayer.Children.Add(plazaField);

        var top = new StackPanel { Margin = new Thickness(60, 34, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        top.Children.Add(new TextBlock { Text = "MY CHARACTER", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Flat(day ? "#1c5d7a" : "#9fd8ff") });
        top.Children.Add(new TextBlock { Text = "Everyone's here", FontSize = 30, FontFamily = Ui.Semi, Foreground = Flat(day ? "#17323f" : "#f2f6fb"), Margin = new Thickness(0, 2, 0, 0) });
        top.Children.Add(new TextBlock { Text = "Pick a character to change its look, name or picture.", FontSize = 13, Foreground = Flat(day ? "#2f5566" : "#c4d2e8"), Margin = new Thickness(0, 4, 0, 0) });
        plazaLayer.Children.Add(top);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 60, 34) };
        plazaEdit = MakeButton("Edit", 44); plazaEdit.Background = Ui.Brush(Ui.Teal); plazaEdit.Foreground = Ui.Brush("#0A0E11"); plazaEdit.Padding = new Thickness(22, 0, 22, 0);
        plazaEdit.Click += (s, e) => PlazaEditPicked();
        var who = MakeButton("Who's playing?", 44); who.Margin = new Thickness(10, 0, 0, 0);
        who.Click += (s, e) => { ClosePlaza(); OpenPlayers(); };
        var back = MakeButton("Back", 44); back.Margin = new Thickness(10, 0, 0, 0);
        back.Click += (s, e) => ClosePlaza();
        bar.Children.Add(plazaEdit); bar.Children.Add(who); bar.Children.Add(back);
        plazaLayer.Children.Add(bar);

        plazaLayer.LayoutTransform = N<Grid>("Main").LayoutTransform;
        N<Grid>("Clipped").Children.Add(plazaLayer);
        plazaTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        plazaTimer.Tick += (s, e) => PlazaTick();
        plazaLayer.SizeChanged += (s, e) => { foreach (var w in walkers) PlaceWalker(w); };
    }
    void FillPlaza() {
        plazaField.Children.Clear(); walkers.Clear();
        int n = Profiles.All.Count;
        for (int i = 0; i < n; i++) {
            var p = Profiles.All[i];
            var def = AvatarDef.Parse(p.Character);
            var w = new Walker { P = p, Speed = 0.085 + plazaRnd.NextDouble() * 0.04 };
            w.Stand = AvatarArt.Render(def, new AvatarState(), AvatarArt.Crop.Full, 240, 360);
            w.Steps = new ImageSource[8];
            for (int k = 0; k < 8; k++) w.Steps[k] = AvatarArt.Render(def, new AvatarState { Walk = (k + 0.5) * Math.PI / 4 }, AvatarArt.Crop.Full, 240, 360);
            w.Flip = new ScaleTransform(1, 1); w.Bob = new TranslateTransform();
            var tg = new TransformGroup(); tg.Children.Add(w.Flip); tg.Children.Add(w.Bob);
            w.Img = new Image { Source = w.Stand, Width = WalkerW, Height = WalkerH, RenderTransformOrigin = new Point(0.5, 1), RenderTransform = tg };
            RenderOptions.SetBitmapScalingMode(w.Img, BitmapScalingMode.HighQuality);
            w.Name = new TextBlock { Text = Profiles.DisplayName(p), FontSize = 15, FontFamily = Ui.Semi, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(12, 3, 12, 4) };
            var tag = new Border { CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Center, Child = w.Name, Margin = new Thickness(0, 0, 0, 2) };
            w.Root = new StackPanel { Width = WalkerW, Cursor = Cursors.Hand, RenderTransformOrigin = new Point(0.5, 1), RenderTransform = new ScaleTransform(1, 1) };
            w.Root.Children.Add(tag); w.Root.Children.Add(w.Img);
            int idx = i;
            w.Root.MouseLeftButtonUp += (s, e) => { plazaIdx = idx; MarkPlaza(); PlazaEditPicked(); };
            w.Root.MouseEnter += (s, e) => { plazaIdx = idx; MarkPlaza(); };
            // spread out along the path to begin with
            w.X = n == 1 ? 0.5 : 0.18 + 0.64 * i / (n - 1); w.Y = 0.25 + plazaRnd.NextDouble() * 0.6;
            w.Tx = w.X; w.Ty = w.Y; w.Wait = 0.4 + plazaRnd.NextDouble() * 1.6;
            walkers.Add(w); plazaField.Children.Add(w.Root);
            PlaceWalker(w);
        }
        plazaIdx = Math.Max(0, Profiles.All.IndexOf(Profiles.Current));
        MarkPlaza();
    }
    // where a walker is on the screen: further back (smaller Y) is higher up, smaller and behind
    void PlaceWalker(Walker w) {
        double W0 = plazaLayer.ActualWidth > 0 ? plazaLayer.ActualWidth : 1280, H0 = plazaLayer.ActualHeight > 0 ? plazaLayer.ActualHeight : 720;
        double scale = 0.62 + 0.5 * w.Y;
        double sx = W0 * (0.08 + 0.84 * w.X), feet = H0 * (0.56 + 0.36 * w.Y);
        var st = (ScaleTransform)w.Root.RenderTransform; st.ScaleX = st.ScaleY = scale;
        double h = w.Root.ActualHeight > 0 ? w.Root.ActualHeight : WalkerH + 30;
        Canvas.SetLeft(w.Root, sx - WalkerW / 2); Canvas.SetTop(w.Root, feet - h);
        Panel.SetZIndex(w.Root, (int)(w.Y * 1000));
    }
    void PlazaTick() {
        double dt = Math.Min(0.1, (DateTime.Now - plazaLast).TotalSeconds); plazaLast = DateTime.Now;
        foreach (var w in walkers) {
            if (w.Wait > 0) {
                w.Wait -= dt;
                if (w.Img.Source != w.Stand) { w.Img.Source = w.Stand; w.Bob.Y = 0; }
                if (w.Wait <= 0) { w.Tx = 0.04 + plazaRnd.NextDouble() * 0.92; w.Ty = 0.12 + plazaRnd.NextDouble() * 0.88; w.Phase = 0; }
                continue;
            }
            double dx = w.Tx - w.X, dy = (w.Ty - w.Y) * 0.6, dist = Math.Sqrt(dx * dx + dy * dy), step = w.Speed * dt;
            if (dist <= step) { w.X = w.Tx; w.Y = w.Ty; w.Wait = 1 + plazaRnd.NextDouble() * 3; PlaceWalker(w); continue; }
            w.X += dx / dist * step; w.Y += dy / dist * step / 0.6;
            if (Math.Abs(dx) > 0.002) w.Flip.ScaleX = dx < 0 ? -1 : 1;
            w.Phase += dt * 8.5;
            w.Img.Source = w.Steps[(int)(w.Phase / (Math.PI / 4)) % 8];
            w.Bob.Y = -Math.Abs(Math.Sin(w.Phase)) * 4;
            PlaceWalker(w);
        }
    }
    void MarkPlaza() {
        for (int i = 0; i < walkers.Count; i++) {
            bool on = i == plazaIdx; var w = walkers[i];
            ((Border)w.Name.Parent).Background = on ? Ui.Brush(Ui.Teal) : Flat("#b0101820");
            w.Name.Foreground = on ? Ui.Brush(Theme.Current.Light ? "#ffffff" : "#0A0E11") : Brushes.White;
            w.Img.Effect = on ? new DropShadowEffect { BlurRadius = 26, ShadowDepth = 0, Opacity = 0.9, Color = Colors.White } : null;
        }
        if (plazaIdx >= 0 && plazaIdx < walkers.Count) plazaEdit.Content = "Edit " + Profiles.DisplayName(walkers[plazaIdx].P);
    }
    // the picked character's profile becomes the one in use, and its editor opens
    void PlazaEditPicked() {
        if (plazaIdx < 0 || plazaIdx >= walkers.Count) return;
        var p = walkers[plazaIdx].P;
        if (Profiles.Current != p) { Profiles.Switch(p); ApplyAvatar(); RefreshForProfile(); }
        ClosePlaza();
        OpenProfile();
    }
    void PlazaPad(int bit) {
        if (bit == Pad.B) { ClosePlaza(); return; }
        if (bit == Pad.A) { Sound.Play(Sfx.Select); PlazaEditPicked(); return; }
        if (bit == Pad.Y) { ClosePlaza(); OpenPlayers(); return; }
        if (walkers.Count == 0) return;
        // left / right: the next character that way on the screen
        if (bit == Pad.Left || bit == Pad.Right) {
            var order = walkers.Select((w, i) => new { w.X, i }).OrderBy(a => a.X).Select(a => a.i).ToList();
            int at = order.IndexOf(plazaIdx);
            plazaIdx = order[(at + (bit == Pad.Right ? 1 : -1) + order.Count) % order.Count];
            Sound.Play(Sfx.Move); MarkPlaza();
        }
    }
}
}
