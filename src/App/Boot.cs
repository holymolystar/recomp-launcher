// The start-up screen: the logo on the theme's colour, there from the window's very first frame (PrepareBoot, before
// the window shows), so the launcher never opens on a blank window while it sets itself up. Once it's ready
// (StartBoot), the logo gives a small pulse with a ring of light around it and a soft chime (Sfx.Boot), the name
// fades in under it, and it all fades away to Home: about a second. A click or any key skips it.
// Settings > General > Start-up animation.
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace RecompLauncher {

partial class Shell {
    Grid bootLayer; Image bootImg; Ellipse bootRing; TextBlock bootName;
    bool BootWanted { get { return args.Snapshot == null && args.PadScript == null && Settings.Get("StartAnim") != "off"; } }
    // before the window shows: the logo, still
    void PrepareBoot() {
        if (!BootWanted) return;
        Func<string, Color> col = s => { try { return (Color)ColorConverter.ConvertFromString(s); } catch { return Colors.Black; } };
        Color bg = col(Theme.Current.Bg), accent = col(Theme.Current.Accent), ink = col(Theme.Current.Light ? "#1a1f24" : "#f2f5f7");
        var layer = bootLayer = new Grid { Background = new SolidColorBrush(bg) };
        var center = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        // the ring of light
        var ring = bootRing = new Ellipse { Width = 170, Height = 170, Stroke = new SolidColorBrush(accent), StrokeThickness = 3, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(0.85, 0.85) };
        center.Children.Add(ring);
        // the logo (logo.png), else the launcher's own icon
        ImageSource logo = null;
        try { string f = Path.Combine(Paths.Root, "logo.png"); if (File.Exists(f)) { var b = new BitmapImage(); b.BeginInit(); b.UriSource = new Uri(f); b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit(); logo = b; } } catch { }
        var img = bootImg = new Image { Source = logo ?? W.Icon, Width = 132, Height = 132, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        center.Children.Add(img);
        var name = bootName = new TextBlock { Text = "R E C O M P   L A U N C H E R", FontSize = 15, FontFamily = Ui.Semi, Foreground = new SolidColorBrush(ink), Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 330, 0, 0) };   // under the ring at its widest
        layer.Children.Add(center); layer.Children.Add(name);
        layer.LayoutTransform = N<Grid>("Main").LayoutTransform;
        N<Grid>("Clipped").Children.Add(layer);
        // the window shows whole at once (its usual fade-in would fade the logo in from a blank window)
        var root = N<Border>("Root"); root.Opacity = 1;
    }
    // once the launcher is set up: the pulse, the ring, the name and the chime, then away
    void StartBoot() {
        if (bootLayer == null) return;
        var layer = bootLayer; var img = bootImg; var ring = bootRing; var name = bootName; bootLayer = null;
        Func<double, double, int, int, IEasingFunction, DoubleAnimation> a = (from, to, at, ms, ease) =>
            new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(at), EasingFunction = ease };
        var back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 };
        var outE = new CubicEase { EasingMode = EasingMode.EaseOut };
        ((ScaleTransform)img.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, a(0.9, 1, 0, 520, back));
        ((ScaleTransform)img.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, a(0.9, 1, 0, 520, back));
        var ringFade = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(120) };
        ringFade.KeyFrames.Add(new LinearDoubleKeyFrame(0.8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(60))));
        ringFade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900)), outE));
        ring.BeginAnimation(UIElement.OpacityProperty, ringFade);
        ((ScaleTransform)ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, a(0.85, 1.7, 120, 900, outE));
        ((ScaleTransform)ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, a(0.85, 1.7, 120, 900, outE));
        name.BeginAnimation(UIElement.OpacityProperty, a(0, 0.9, 150, 400, outE));
        Sound.Play(Sfx.Boot);

        bool gone = false;
        Action finish = () => {
            if (gone) return; gone = true;
            var fade = a(1, 0, 0, 340, new CubicEase { EasingMode = EasingMode.EaseIn });
            fade.Completed += (s, e) => N<Grid>("Clipped").Children.Remove(layer);
            layer.BeginAnimation(UIElement.OpacityProperty, fade);
        };
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1150) };
        t.Tick += (s, e) => { t.Stop(); finish(); };
        t.Start();
        layer.MouseLeftButtonDown += (s, e) => finish();
        KeyEventHandler skip = null;
        skip = (s, e) => { W.PreviewKeyDown -= skip; finish(); };
        W.PreviewKeyDown += skip;
    }
}
}
