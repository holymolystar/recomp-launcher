// Glow (the launcher style, Styles.cs) for Settings and the character creator: the same soft violet and blue glow
// behind them as on Home, frosted glass for their panels, a coloured icon for each of Settings' sections, and the
// picked section, tab and Save button in the theme's two colours with a glow. What they do stays their own
// (Panels.cs, Creator.cs); this only changes how they look.
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
    void GlowSaveButton(Button b) {
        b.Background = AccentPill(255); b.Foreground = Brushes.White; b.BorderThickness = new Thickness(0);
        b.Effect = StyleId == "console" ? null : new DropShadowEffect { BlurRadius = 22, ShadowDepth = 0, Opacity = 0.55, Color = Ui.Col(Ui.Teal) };
    }

    // ---------- Settings ----------
    bool glowSettingsDone;
    // each section's colour (its icon sits on a rounded square of it)
    static readonly Dictionary<string, string> SetColors = new Dictionary<string, string> {
        { "Display", "#2f8fe0" }, { "Sound", "#ff4d8d" }, { "Controller", "#7b4ce0" }, { "General", "#6b7380" }, { "GitHub token", "#3b3b4f" },
        { "Friends", "#1fa6a0" }, { "Steam achievements", "#2a475e" }, { "Phone remote", "#3fa34d" }, { "Discord", "#5865f2" }, { "Library", "#ff8a1c" },
        { "Game folders", "#e8a51c" }, { "Theme", "#9b6bff" }, { "Weather", "#29a8e0" }, { "Online play", "#1fa6a0" }, { "Profiles", "#ff6ec7" } };
    void GlowSettings() {
        if (glowSettingsDone) return;
        glowSettingsDone = true;
        bool light = Theme.Current.Light;
        var layer = N<Grid>("SettingsLayer");
        layer.Children.Insert(0, StyleBackdrop(1));
        N<Rectangle>("SettingsDim").Fill = new SolidColorBrush(light ? Color.FromArgb(0x50, 255, 255, 255) : Color.FromArgb(0x60, 0, 0, 0));
        var card = N<Border>("SettingsCard");
        card.CornerRadius = new CornerRadius(KitRadius(32)); card.BorderBrush = GlassRim(); card.BorderThickness = new Thickness(1.2);
        card.Background = StyleId == "glow" || StyleId == "cinema" ? new SolidColorBrush(light ? Color.FromArgb(0xC8, 0xF4, 0xF4, 0xFA) : Color.FromArgb(0xB8, 0x10, 0x10, 0x1E)) : Ui.Brush(light ? "#f2f3f5" : "#11181D");
        if (StyleId == "channels") card.BorderThickness = new Thickness(3);
        card.Effect = new DropShadowEffect { BlurRadius = 60, ShadowDepth = 10, Opacity = light ? 0.2 : 0.55, Color = Colors.Black };
        // the sections' column: a lighter pane of glass
        var side = ((Grid)card.Child).Children.OfType<Border>().FirstOrDefault();
        if (side != null) { side.Background = GlassFill(0x10); side.CornerRadius = new CornerRadius(KitRadius(32), 0, 0, KitRadius(32)); }
        // each section: its icon on a rounded square of its colour
        foreach (var sec in setSections) {
            var row = (StackPanel)sec.Tab.Child;
            var glyph = row.Children.OfType<TextBlock>().First();
            if (glyph.Parent != row) continue;
            row.Children.Remove(glyph);
            string c; if (!SetColors.TryGetValue(sec.Name, out c)) c = "#5b6670";
            var col = (Color)ColorConverter.ConvertFromString(c);
            glyph.Width = double.NaN; glyph.FontSize = 15; glyph.Foreground = Brushes.White; glyph.HorizontalAlignment = HorizontalAlignment.Center; glyph.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Insert(0, new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(10), Child = glyph, VerticalAlignment = VerticalAlignment.Center,
                Background = new LinearGradientBrush(Theme.Mix(col, Colors.White, 0.25), col, 90) });
            sec.Tab.Height = 50; sec.Tab.CornerRadius = new CornerRadius(KitRadius(25)); sec.Tab.Padding = new Thickness(9, 0, 12, 0);
        }
        GlowSaveButton(N<Button>("BtnSetSave"));
        var cancel = N<Button>("BtnSetCancel"); cancel.Background = GlassFill(0x18); cancel.BorderBrush = GlassRim();
        N<TextBlock>("SetSectionTitle").FontFamily = new FontFamily("Segoe UI Black"); N<TextBlock>("SetSectionTitle").FontSize = 30;
    }
    // the picked section: a glowing pill in the theme's two colours (after ShowSetSection has coloured it its way)
    void GlowSetTabs() {
        for (int k = 0; k < setSections.Count; k++) {
            var sec = setSections[k]; bool on = k == setSection;
            sec.Tab.Background = on ? AccentPill(0xC8) : Brushes.Transparent;
            sec.Tab.Effect = on && StyleId != "console" ? new DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.45, Color = Ui.Col(Ui.Teal) } : null;
            foreach (var t in ((StackPanel)sec.Tab.Child).Children.OfType<TextBlock>()) t.Foreground = on ? Brushes.White : Ui.Brush(Ui.Text);
        }
    }

    // ---------- the character creator ----------
    void GlowCreator(Border stage, FrameworkElement tabScroll, Border right) {
        bool light = Theme.Current.Light;
        crLayer.Background = Ui.Brush("#090C0F");
        var blobs = StyleBackdrop(1); Grid.SetRowSpan(blobs, 3); Grid.SetColumnSpan(blobs, 3);
        crLayer.Children.Insert(0, blobs);
        // a soft light behind the character, and a round glass floor for it to stand on
        var a = Ui.Col(Ui.Teal);
        var halo = new RadialGradientBrush { RadiusX = 0.5, RadiusY = 0.5 };
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(light ? (byte)0x50 : (byte)0x70, a.R, a.G, a.B), 0)); halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, a.R, a.G, a.B), 1)); halo.Freeze();
        stage.Background = halo;
        var floor = new RadialGradientBrush { RadiusX = 0.5, RadiusY = 0.5 };
        floor.GradientStops.Add(new GradientStop(Color.FromArgb(light ? (byte)0x90 : (byte)0x40, 255, 255, 255), 0)); floor.GradientStops.Add(new GradientStop(Color.FromArgb(light ? (byte)0x30 : (byte)0x14, 255, 255, 255), 0.7));
        floor.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)); floor.Freeze();
        var disc = new Ellipse { Width = 380, Height = 78, Fill = floor, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2), IsHitTestVisible = false };
        Grid.SetRow(disc, 1); Grid.SetColumn(disc, 1);
        crLayer.Children.Insert(crLayer.Children.IndexOf(crView), disc);
        // the tabs on a pane of glass
        int at = crLayer.Children.IndexOf(tabScroll);
        crLayer.Children.Remove(tabScroll);
        var pane = new Border { Child = tabScroll, CornerRadius = new CornerRadius(KitRadius(30)), Background = GlassFill(0x18), BorderBrush = GlassRim(), BorderThickness = new Thickness(1.2), Margin = new Thickness(14, 4, 6, 10), Padding = new Thickness(0, 10, 0, 10) };
        Grid.SetRow(pane, 1);
        crLayer.Children.Insert(at, pane);
        crTabs.Margin = new Thickness(8, 0, 8, 0);
        foreach (var b in crTabButtons) { b.CornerRadius = new CornerRadius(KitRadius(25)); b.Width = 88; }
        // the choices on glass
        right.Background = GlassFill(0x16); right.BorderBrush = GlassRim(); right.BorderThickness = new Thickness(StyleId == "channels" ? 3 : 1.2); right.CornerRadius = new CornerRadius(KitRadius(30));
        crTabTitle.FontFamily = new FontFamily("Segoe UI Black"); crTabTitle.FontSize = 22;
        GlowSaveButton(crSave);
        foreach (var b in new[] { crUndo, crRandom, crCancel }) { b.Background = GlassFill(0x18); b.BorderBrush = GlassRim(); }
        crPicture.Stroke = Brushes.White;
    }
    void GlowCrTabs() {
        for (int k = 0; k < crTabButtons.Count; k++) {
            bool on = k == crTab;
            crTabButtons[k].Background = on ? AccentPill(0xD0) : Brushes.Transparent;
            foreach (TextBlock tb in ((StackPanel)crTabButtons[k].Child).Children) tb.Foreground = on ? Brushes.White : Ui.Brush(Ui.Text);
        }
    }
}
}
