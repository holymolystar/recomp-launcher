// The launcher styles' shared parts (Styles.cs): every style has the same screens and the same things on them (the
// friends and music pills on Home, the same buttons, the game page, Settings, the creator, Pixel art, Flip book);
// only how they look differs. These say how, per style:
//   Glow      modern: frosted glass on a soft violet and blue glow, a gloss on things, the theme's two colours together
//   Console   a handheld console's home screen: flat, plain surfaces in light or dark grey, one accent colour, round buttons
//   Channels  a living-room console's channels: white and silver, soft blue rims, gentle stripes behind
//   Cinema    dark glass over the picked game's picture
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RecompLauncher {

partial class Shell {
    static bool KitGloss { get { return StyleId == "glow" || StyleId == "cinema"; } }   // a shine on icons and cards
    static bool KitBlur { get { return StyleId == "glow" || StyleId == "cinema"; } }    // the game's colours, blurred, behind its page
    // how round things are, from Glow's own roundness
    static double KitRadius(double r) { return StyleId == "console" ? Math.Round(r * 0.45) : StyleId == "channels" ? Math.Round(r * 0.9) : StyleId == "cinema" ? Math.Round(r * 0.75) : r; }
    // a surface: glass (Glow, Cinema), or solid (Console: plain grey; Channels: white / slate)
    static Brush GlassFill(byte a) {
        bool light = Theme.Current.Light; Brush b;
        if (StyleId == "console") b = Ui.Brush(light ? "#ffffff" : "#1C252C");
        else if (StyleId == "channels") b = Ui.Brush(light ? "#fbfbfb" : "#2a3038");
        else b = new SolidColorBrush(light ? Color.FromArgb((byte)Math.Min(255, a * 6), 255, 255, 255) : Color.FromArgb(a, 255, 255, 255));
        if (b.CanFreeze && !b.IsFrozen) b.Freeze(); return b;
    }
    // its edge: a light rim on glass, none on Console's flat surfaces, a soft blue one on Channels
    static Brush GlassRim() {
        if (StyleId == "console") return Theme.Current.Light ? Ui.Brush("#e2e2e2") : Ui.Brush("#26333C");
        if (StyleId == "channels") return new SolidColorBrush(Ui.Alpha(Ui.Teal, 0x90));
        var b = new SolidColorBrush(Theme.Current.Light ? Colors.White : Color.FromArgb(0x34, 255, 255, 255)); b.Freeze(); return b;
    }
    // what's picked or most important: the theme's two colours (Glow), its one accent (Console), a light blue sheen (Channels)
    static Brush AccentPill(byte a) {
        var c1 = Ui.Col(Ui.Teal); var c2 = StyleId == "glow" || StyleId == "cinema" ? Ui.Col(Ui.Gold) : StyleId == "channels" ? Theme.Mix(c1, Colors.White, 0.45) : c1;
        var g = StyleId == "channels" ? new LinearGradientBrush(Color.FromArgb(a, c2.R, c2.G, c2.B), Color.FromArgb(a, c1.R, c1.G, c1.B), 90)
                                      : new LinearGradientBrush(Color.FromArgb(a, c1.R, c1.G, c1.B), Color.FromArgb(a, c2.R, c2.G, c2.B), 0);
        g.Freeze(); return g;
    }
    // the background of a full screen of its own (Settings, the creator, Pixel art, Flip book)
    Grid StyleBackdrop(double strength) {
        if (StyleId == "glow") return GlowBlobs(strength);
        if (StyleId == "channels") { var g = (Grid)ChanBackLayer(); return g; }
        return new Grid { IsHitTestVisible = false, Background = Ui.Brush("#090C0F") };
    }
}
}
