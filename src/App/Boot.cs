// The start-up animation: for a moment as the launcher opens, the logo grows in on the theme's colour with a ring of
// light going out from it and a soft chime (Sfx.Boot), the name fades in under it, then it all fades away to Home.
// About 1.6 seconds; a click or any key skips it. Settings > General > Start-up animation.
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
    void PlayBootAnim() {
        Func<string, Color> col = s => { try { return (Color)ColorConverter.ConvertFromString(s); } catch { return Colors.Black; } };
        Color bg = col(Theme.Current.Bg), accent = col(Theme.Current.Accent), ink = col(Theme.Current.Light ? "#1a1f24" : "#f2f5f7");
        var layer = new Grid { Background = new SolidColorBrush(bg) };
        var center = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        // the ring of light
        var ring = new Ellipse { Width = 170, Height = 170, Stroke = new SolidColorBrush(accent), StrokeThickness = 3, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(0.6, 0.6) };
        center.Children.Add(ring);
        // the logo (logo.png), else the launcher's own icon
        ImageSource logo = null;
        try { string f = Path.Combine(Paths.Root, "logo.png"); if (File.Exists(f)) { var b = new BitmapImage(); b.BeginInit(); b.UriSource = new Uri(f); b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit(); logo = b; } } catch { }
        var img = new Image { Source = logo ?? W.Icon, Width = 132, Height = 132, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(0.7, 0.7) };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        center.Children.Add(img);
        var name = new TextBlock { Text = "R E C O M P   L A U N C H E R", FontSize = 15, FontFamily = Ui.Semi, Foreground = new SolidColorBrush(ink), Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 250, 0, 0) };
        layer.Children.Add(center); layer.Children.Add(name);
        layer.LayoutTransform = N<Grid>("Main").LayoutTransform;
        N<Grid>("Clipped").Children.Add(layer);

        Func<double, double, int, int, IEasingFunction, DoubleAnimation> a = (from, to, at, ms, ease) =>
            new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(at), EasingFunction = ease };
        var back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 };
        var outE = new CubicEase { EasingMode = EasingMode.EaseOut };
        img.BeginAnimation(UIElement.OpacityProperty, a(0, 1, 60, 380, outE));
        ((ScaleTransform)img.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, a(0.7, 1, 60, 560, back));
        ((ScaleTransform)img.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, a(0.7, 1, 60, 560, back));
        var ringFade = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(220) };
        ringFade.KeyFrames.Add(new LinearDoubleKeyFrame(0.8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(60))));
        ringFade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900)), outE));
        ring.BeginAnimation(UIElement.OpacityProperty, ringFade);
        ((ScaleTransform)ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, a(0.6, 2.4, 220, 900, outE));
        ((ScaleTransform)ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, a(0.6, 2.4, 220, 900, outE));
        name.BeginAnimation(UIElement.OpacityProperty, a(0, 0.9, 420, 420, outE));
        Sound.Play(Sfx.Boot);

        bool gone = false;
        Action finish = () => {
            if (gone) return; gone = true;
            var fade = a(1, 0, 0, 340, new CubicEase { EasingMode = EasingMode.EaseIn });
            fade.Completed += (s, e) => N<Grid>("Clipped").Children.Remove(layer);
            layer.BeginAnimation(UIElement.OpacityProperty, fade);
        };
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
        t.Tick += (s, e) => { t.Stop(); finish(); };
        t.Start();
        layer.MouseLeftButtonDown += (s, e) => finish();
        KeyEventHandler skip = null;
        skip = (s, e) => { W.PreviewKeyDown -= skip; finish(); };
        W.PreviewKeyDown += skip;
    }
}
}
