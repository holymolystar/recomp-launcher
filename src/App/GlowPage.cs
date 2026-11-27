// Glow's game page (Styles.cs: the Glow launcher style): the game's colours, blurred, behind the whole page; on the
// left its icon big, as a glossy rounded square with a glow in its own colours, and under it Start (wide) with
// Update and What's new beside each other; on the right its name, its details as glass pills, and everything else
// it can do as a grid of square tiles with an icon and a short name, as on a handheld's menu.
// The buttons are the game page's own (HomePages.cs): only where they sit and how they look is Glow's.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace RecompLauncher {

partial class Shell {
    Image gpBlur; Grid gpFaceInner; StackPanel gpLeftButtons; DropShadowEffect gpGlow;
    const double GpIcon = 270, GpRadius = 62;
    // the short names on the tiles (the page's own texts are longer)
    static readonly Dictionary<int, string> GpShort = new Dictionary<int, string> {
        { 3, "Folder" }, { 4, "Backups" }, { 5, "Uninstall" }, { 6, "Favourite" }, { 7, "Collections" }, { 8, "Language" }, { 9, "Hide" },
        { 10, "Achievements" }, { 11, "Game menu" }, { 12, "Test versions" }, { 13, "Online play" }, { 14, "Pixel art" } };

    void GlowGamePage(StackPanel right) {
        bool light = Theme.Current.Light;
        // the game's colours behind the page
        gpBlur = new Image { Stretch = Stretch.UniformToFill, Margin = new Thickness(-80, -160, -80, -120), Opacity = light ? 0.3 : 0.4, IsHitTestVisible = false,
            Effect = new BlurEffect { Radius = 90, RenderingBias = RenderingBias.Performance } };
        Grid.SetColumnSpan(gpBlur, 3);
        gamePage.Children.Insert(0, gpBlur);
        if (!KitBlur) gpBlur.Visibility = Visibility.Collapsed;
        // the icon: a glossy rounded square with a glow
        gamePage.ColumnDefinitions[0].Width = new GridLength(GpIcon + 20);
        gamePage.Children.Remove(gpFace);
        gpFace.Child = null;
        gpFaceInner = new Grid { Width = GpIcon, Height = GpIcon };
        double rad = KitRadius(GpRadius);
        gpFaceInner.Clip = new RectangleGeometry(new Rect(0, 0, GpIcon, GpIcon), rad, rad);
        gpFaceInner.Children.Add(gpIcon);
        var shine = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        shine.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, 255, 255, 255), 0)); shine.GradientStops.Add(new GradientStop(Color.FromArgb(0x14, 255, 255, 255), 0.42));
        shine.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.5)); shine.Freeze();
        if (KitGloss) gpFaceInner.Children.Add(new Border { Background = shine, IsHitTestVisible = false });
        gpFaceInner.Children.Add(new Border { CornerRadius = new CornerRadius(rad), BorderThickness = new Thickness(2), BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 255, 255, 255)), IsHitTestVisible = false });
        gpFace.Child = gpFaceInner; gpFace.Width = GpIcon; gpFace.Height = GpIcon; gpFace.CornerRadius = new CornerRadius(rad); gpFace.ClipToBounds = false;
        gpFace.Margin = new Thickness(0); gpFace.HorizontalAlignment = HorizontalAlignment.Left;
        gpGlow = StyleId == "console" ? new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.3, Color = Colors.Black }
            : new DropShadowEffect { BlurRadius = 60, ShadowDepth = 0, Opacity = light ? 0.45 : 0.7, Color = Ui.Col(Ui.Teal) };
        gpFace.Effect = gpGlow;
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 26, 0, 0) };
        left.Children.Add(gpFace);
        // Start wide under it; Update and What's new beside each other
        gpLeftButtons = new StackPanel { Margin = new Thickness(0, 26, 0, 0), Width = GpIcon, HorizontalAlignment = HorizontalAlignment.Left };
        var pair = new UniformGrid2();
        for (int i = 0; i < 3; i++) gpButtonRow.Children.Remove(gpButtons[i].Root);
        gpLeftButtons.Children.Add(gpButtons[0].Root);
        pair.Add(gpButtons[1].Root); pair.Add(gpButtons[2].Root);
        gpLeftButtons.Children.Add(pair.Panel);
        left.Children.Add(gpLeftButtons);
        gamePage.Children.Add(left);
        gpButtons[0].Root.Height = 60; gpButtons[0].Root.CornerRadius = new CornerRadius(KitRadius(30)); gpButtons[0].Root.Margin = new Thickness(0, 0, 0, 12);
        gpButtons[0].Root.HorizontalAlignment = HorizontalAlignment.Stretch; ((StackPanel)gpButtons[0].Root.Child).HorizontalAlignment = HorizontalAlignment.Center;
        gpButtons[0].Text.FontSize = 19; gpButtons[0].Glyph.FontSize = 18;
        foreach (int i in new[] { 1, 2 }) {
            var b = gpButtons[i]; b.Root.Height = 46; b.Root.CornerRadius = new CornerRadius(KitRadius(23)); b.Root.Padding = new Thickness(10, 0, 10, 0); b.Root.Margin = new Thickness(i == 1 ? 0 : 6, 0, i == 1 ? 6 : 0, 0);
            ((StackPanel)b.Root.Child).HorizontalAlignment = HorizontalAlignment.Center; b.Text.FontSize = 13.5; b.Text.TextTrimming = TextTrimming.CharacterEllipsis;
        }
        // the name bigger and bolder
        gpName.FontSize = 44; gpName.FontFamily = new FontFamily("Segoe UI Black"); gpName.LineHeight = 50;
        gpName.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = light ? 0 : 0.55, Color = Colors.Black };
        right.Margin = new Thickness(0, 34, 0, 0);
        // the details: glass pills
        foreach (var chip in new[] { gpPlayedChip, gpSteamChip, gpSizeChip }.Concat(((WrapPanel)gpPlayedChip.Parent).Children.OfType<Border>()).Distinct()) {
            chip.Background = GlassFill(0x26); chip.BorderBrush = GlassRim(); chip.BorderThickness = new Thickness(StyleId == "channels" ? 2 : 1); chip.Effect = null;
            chip.CornerRadius = new CornerRadius(KitRadius(15));
        }
        // everything else: square tiles with an icon and a short name
        gpButtonRow.Margin = new Thickness(-6, 20, 0, 0); gpButtonRow.Width = 5 * 112; gpButtonRow.HorizontalAlignment = HorizontalAlignment.Left;   // five across
        for (int i = 3; i < gpButtons.Count; i++) {
            var b = gpButtons[i]; var sp = (StackPanel)b.Root.Child;
            sp.Orientation = Orientation.Vertical; sp.HorizontalAlignment = HorizontalAlignment.Center; sp.VerticalAlignment = VerticalAlignment.Center;
            b.Glyph.FontSize = 25; b.Glyph.Margin = new Thickness(0, 0, 0, 8); b.Glyph.HorizontalAlignment = HorizontalAlignment.Center;
            b.Text.FontSize = 11.5; b.Text.TextAlignment = TextAlignment.Center; b.Text.HorizontalAlignment = HorizontalAlignment.Center; b.Text.TextTrimming = TextTrimming.CharacterEllipsis; b.Text.MaxWidth = 92;
            b.Root.Width = 100; b.Root.Height = 90; b.Root.CornerRadius = new CornerRadius(KitRadius(24)); b.Root.Padding = new Thickness(4); b.Root.Margin = new Thickness(6, 0, 6, 12);
            b.Root.Effect = null; b.Root.BorderThickness = new Thickness(StyleId == "channels" ? 2.5 : 2);
        }
    }
    // a pair of buttons side by side, each half the width
    class UniformGrid2 {
        public Grid Panel = new Grid();
        public void Add(UIElement e) { Panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); Grid.SetColumn(e, Panel.ColumnDefinitions.Count - 1); Panel.Children.Add(e); }
    }
    // a button's look in Glow, after the page has set its text and whether it can be pressed
    void GlowPageButton(int i, PageButton b, bool enabled) {
        bool light = Theme.Current.Light;
        if (i == 0 && enabled) {
            b.Root.Background = AccentPill(255); b.Text.Foreground = Brushes.White; b.Glyph.Foreground = Brushes.White;
            b.Root.Effect = StyleId == "console" ? null : new DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = 0.6, Color = Ui.Col(Ui.Teal) };
            return;
        }
        if (i == 1 && enabled) { b.Root.Background = Ui.Brush(Ui.Gold); return; }   // an update waiting: gold, as everywhere
        b.Root.Background = GlassFill(0x22);
        b.Text.Foreground = Ui.Brush(Ui.Text);
        if (i == 0) b.Root.Effect = null;
        if (i < 3) return;
        string t = b.Text.Text, s;
        if (i == 6) t = sel != null && Profiles.IsFav(sel.Id) ? "Favourite" : "Add favourite";
        else if (i == 7) t = t.StartsWith("Collections") ? t.Replace("Collections  (", "Collections (") : "Collections";
        else if (i == 12) t = t.EndsWith("On") ? "Tests: On" : "Tests: Off";
        else if (GpShort.TryGetValue(i, out s)) t = s;
        b.Text.Text = t;
    }
    // the page's game: its colours behind it and round its icon
    void GlowGamePageUpdate(Game g) {
        if (gpBlur == null) return;
        gpFace.Background = Ui.Gradient(g.Accent, g.Accent2);
        ImageSource hero = null; try { hero = Stores.Hero(g); } catch { }
        gpBlur.Source = hero ?? (ImageSource)g.Icon;
        gpIcon.Margin = new Thickness(g.IconIsArt ? 0 : GpIcon * 0.22);
        if (StyleId != "console") gpGlow.Color = Theme.Mix(Ui.Col(g.Accent), Colors.White, 0.1);
    }
}
}
