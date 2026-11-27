// TV & Movies: how the page moves, made cheap enough for a TV PC.
//   Focus frame   one white frame (and one soft shadow under it) glides from tile to tile, as on a streaming service.
//                 The tiles themselves only grow a little; nothing is redrawn but the two tiles that change. The
//                 frame's and the shadow's looks are drawn once (BitmapCache), so moving them costs next to nothing.
//   Scrolling     the rows slide (down and up through the page, left and right in a row) instead of jumping.
//   Soft pictures a blurred background (a poster or a channel logo used as the backdrop) is blurred once, at a small
//                 size, into a picture of its own, instead of being blurred again on every frame by the graphics card.
// Settings > Motion > Reduced: no sliding, the frame jumps.
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace RecompLauncher {

partial class Shell {
    Grid tvPageGrid; Border tvFocusRing, tvFocusShadow; TranslateTransform tvRingAt, tvShadowAt;
    Border tvMarked;   // the tile drawn as picked
    int tvVAnim, tvHAnim;   // a slide is going on (its number, 0: none)
    // reduced motion: chosen in Settings, or a graphics card that draws in software (RenderCapability tier below 2)
    static bool TvCalm { get { return Settings.Get("Motion") == "reduced" || (RenderCapability.Tier >> 16) < 2; } }
    const double TvGlideMs = 230;

    // the page with the frame's two layers: the shadow under the rows, the frame over them
    Grid TvMotionWrap(UIElement page) {
        tvPageGrid = new Grid();
        var under = new Canvas { IsHitTestVisible = false }; var over = new Canvas { IsHitTestVisible = false };
        tvShadowAt = new TranslateTransform(); tvRingAt = new TranslateTransform();
        tvFocusShadow = new Border { Background = Brushes.Black, CornerRadius = new CornerRadius(14), Opacity = 0, RenderTransform = tvShadowAt,
            Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 6, Direction = 270, Opacity = 0.75, Color = Colors.Black, RenderingBias = RenderingBias.Performance },
            CacheMode = new BitmapCache() };
        tvFocusRing = new Border { BorderBrush = Brushes.White, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(14), Opacity = 0, RenderTransform = tvRingAt,
            CacheMode = new BitmapCache() };
        under.Children.Add(tvFocusShadow); over.Children.Add(tvFocusRing);
        tvPageGrid.Children.Add(under); tvPageGrid.Children.Add(page); tvPageGrid.Children.Add(over);
        // the rows' sizes change (more tiles, a row filled in): the frame stays on its tile
        if (page is FrameworkElement) ((FrameworkElement)page).SizeChanged += (s, e) => TvFocusFollow(null);
        return tvPageGrid;
    }

    // ---------- the picked tile ----------
    void TvMarkTile(Border t, bool on) {
        var st = t.RenderTransform as ScaleTransform; double k = on ? 1.08 : 1;
        if (st != null && Math.Abs(st.ScaleX - k) > 0.001) {
            if (TvCalm) { st.BeginAnimation(ScaleTransform.ScaleXProperty, null); st.BeginAnimation(ScaleTransform.ScaleYProperty, null); st.ScaleX = st.ScaleY = k; }
            else { st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(k, 180)); st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(k, 180)); }
        }
        t.BorderBrush = Brushes.Transparent; t.Effect = null;
        Panel.SetZIndex(t, on ? 10 : 0);
        var top10 = t.Parent as UIElement; if (top10 != null && !(top10 is StackPanel)) Panel.SetZIndex(top10, on ? 10 : 0);
    }
    // only the tile that was picked and the one that is now change
    void TvMarkTiles() {
        Border want = null;
        if (tvZone == "rows" && tvRowIdx < tvRowList.Count) { var r = tvRowList[tvRowIdx]; if (r.Col < r.Tiles.Count) want = r.Tiles[r.Col]; }
        if (want == tvMarked) return;
        if (tvMarked != null) TvMarkTile(tvMarked, false);
        if (want != null) TvMarkTile(want, true);
        tvMarked = want;
        if (want == null) TvFocusHide();
    }

    // ---------- the frame ----------
    // puts the frame on the picked tile; hShift: how far its row is still going to slide (the frame goes straight to
    // where the tile will be)
    void TvPlaceFocus(bool glide, double hShift = 0) {
        if (tvFocusRing == null || tvZone != "rows" || tvRowIdx >= tvRowList.Count) { TvFocusHide(); return; }
        var r = tvRowList[tvRowIdx]; if (r.Col >= r.Tiles.Count) { TvFocusHide(); return; }
        var t = r.Tiles[r.Col];
        if (t.ActualWidth <= 0 || !tvPageGrid.IsAncestorOf(t)) return;
        Point c;
        try { c = t.TransformToAncestor(tvPageGrid).Transform(new Point(t.ActualWidth / 2, t.ActualHeight / 2)); } catch { return; }
        double w = t.ActualWidth * 1.08, h = t.ActualHeight * 1.08, x = c.X - w / 2 - hShift, y = c.Y - h / 2;
        bool shown = tvFocusRing.Opacity > 0.5 && tvFocusRing.Width > 0;
        glide = glide && shown && !TvCalm;
        foreach (var b in new[] { tvFocusRing, tvFocusShadow }) {
            if (glide && (Math.Abs(b.Width - w) > 0.5 || Math.Abs(b.Height - h) > 0.5)) {
                b.BeginAnimation(FrameworkElement.WidthProperty, Anim(w, TvGlideMs, b.Width)); b.BeginAnimation(FrameworkElement.HeightProperty, Anim(h, TvGlideMs, b.Height));
            } else { b.BeginAnimation(FrameworkElement.WidthProperty, null); b.BeginAnimation(FrameworkElement.HeightProperty, null); b.Width = w; b.Height = h; }
        }
        foreach (var tt in new[] { tvRingAt, tvShadowAt }) {
            if (glide) { tt.BeginAnimation(TranslateTransform.XProperty, Anim(x, TvGlideMs, tt.X)); tt.BeginAnimation(TranslateTransform.YProperty, Anim(y, TvGlideMs, tt.Y)); }
            else { tt.BeginAnimation(TranslateTransform.XProperty, null); tt.BeginAnimation(TranslateTransform.YProperty, null); tt.X = x; tt.Y = y; }
        }
        if (!shown) {
            tvFocusRing.BeginAnimation(UIElement.OpacityProperty, TvCalm ? null : Anim(1, 160, 0)); tvFocusRing.Opacity = 1;
            tvFocusShadow.BeginAnimation(UIElement.OpacityProperty, TvCalm ? null : Anim(1, 160, 0)); tvFocusShadow.Opacity = 1;
        }
    }
    void TvFocusHide() {
        if (tvFocusRing == null || tvFocusRing.Opacity == 0) return;
        foreach (var b in new[] { tvFocusRing, tvFocusShadow }) { b.BeginAnimation(UIElement.OpacityProperty, null); b.Opacity = 0; }
    }
    // a row was scrolled by hand (the mouse), or the page's size changed: the frame stays on its tile
    void TvFocusFollow(TvRow row) {
        if (tvHAnim != 0 || tvZone != "rows" || tvRowIdx >= tvRowList.Count) return;
        if (row != null && tvRowList[tvRowIdx] != row) return;
        TvPlaceFocus(false);
    }

    // ---------- sliding ----------
    static readonly DependencyProperty TvVOffsetProperty = DependencyProperty.RegisterAttached("TvVOffset", typeof(double), typeof(Shell),
        new PropertyMetadata(0.0, (d, e) => { var sv = d as ScrollViewer; if (sv != null) sv.ScrollToVerticalOffset((double)e.NewValue); }));
    static readonly DependencyProperty TvHOffsetProperty = DependencyProperty.RegisterAttached("TvHOffset", typeof(double), typeof(Shell),
        new PropertyMetadata(0.0, (d, e) => { var sv = d as ScrollViewer; if (sv != null) sv.ScrollToHorizontalOffset((double)e.NewValue); }));

    void TvSlideV(double to) {
        to = Math.Max(0, Math.Min(to, tvScroll.ScrollableHeight));
        double from = tvScroll.VerticalOffset;
        if (Math.Abs(to - from) < 1) return;
        if (TvCalm || Math.Abs(to - from) > 2500) { tvScroll.BeginAnimation(TvVOffsetProperty, null); tvScroll.ScrollToVerticalOffset(to); return; }
        int n = ++tvVAnim == 0 ? ++tvVAnim : tvVAnim;
        var a = Anim(to, 320, from); a.Completed += (s, e) => { if (tvVAnim == n) tvVAnim = 0; };
        tvScroll.BeginAnimation(TvVOffsetProperty, a);
    }
    // returns how far the row still slides (for the frame)
    double TvSlideH(ScrollViewer sv, double to) {
        to = Math.Max(0, Math.Min(to, sv.ScrollableWidth));
        double from = sv.HorizontalOffset;
        if (Math.Abs(to - from) < 1) return 0;
        if (TvCalm) { sv.BeginAnimation(TvHOffsetProperty, null); sv.ScrollToHorizontalOffset(to); return 0; }
        int n = ++tvHAnim == 0 ? ++tvHAnim : tvHAnim;
        var a = Anim(to, TvGlideMs, from); a.Completed += (s, e) => { if (tvHAnim == n) tvHAnim = 0; };
        sv.BeginAnimation(TvHOffsetProperty, a);
        return to - from;
    }

    // ---------- soft pictures ----------
    // the picture blurred once, at 480 pixels wide (it's soft anyway, so the size doesn't show)
    static ImageSource TvBlurred(ImageSource src, double radius) {
        try {
            double sw = src.Width, sh = src.Height; if (sw <= 0 || sh <= 0) return src;
            int w = 480, h = Math.Max(1, (int)Math.Round(w * sh / sw));
            double r = Math.Max(2, radius * w / 1920.0 * 1.6), pad = r * 1.2;
            var img = new Image { Source = src, Stretch = Stretch.Fill, Width = w + pad * 2, Height = h + pad * 2, Margin = new Thickness(-pad),
                Effect = new BlurEffect { Radius = r, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality } };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var box = new Grid { Width = w, Height = h, ClipToBounds = true };
            box.Children.Add(img);
            box.Measure(new Size(w, h)); box.Arrange(new Rect(0, 0, w, h)); box.UpdateLayout();
            var rt = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rt.Render(box); rt.Freeze();
            return rt;
        } catch { return src; }
    }
    static void TvBlurInPlace(Image img, double radius) {
        if (img.Source == null) return;
        img.Source = TvBlurred(img.Source, radius);
    }

    // ---------- the picture's colour: a glow behind the words, in the main colour of what's shown ----------
    Border tvGlow; RadialGradientBrush tvGlowBrush; int tvGlowGen;
    UIElement TvGlowLayer() {
        tvGlowBrush = new RadialGradientBrush { Center = new Point(0.12, 0.92), GradientOrigin = new Point(0.12, 0.92), RadiusX = 0.75, RadiusY = 0.95 };
        tvGlowBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        tvGlowBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        tvGlow = new Border { Background = tvGlowBrush, IsHitTestVisible = false };
        return tvGlow;
    }
    // worked out off the window's thread from a 24 x 14 copy of the picture; black-and-white pictures give almost none
    void TvGlowFrom(ImageSource src) {
        var bs = src as BitmapSource; if (tvGlowBrush == null) return;
        int gen = ++tvGlowGen;
        if (bs == null) { TvGlowTo(Colors.Transparent); return; }
        Action work = () => {
            Color c = Colors.Transparent;
            try {
                var small = new FormatConvertedBitmap(new TransformedBitmap(bs, new ScaleTransform(24.0 / bs.PixelWidth, 14.0 / bs.PixelHeight)), PixelFormats.Bgra32, null, 0);
                int w = small.PixelWidth, h = small.PixelHeight; var px = new byte[w * h * 4]; small.CopyPixels(px, w * 4, 0);
                double r = 0, g = 0, b = 0, wt = 0, sat = 0; int n = 0;
                for (int i = 0; i + 3 < px.Length; i += 4) {
                    double B = px[i], G = px[i + 1], R = px[i + 2], mx = Math.Max(R, Math.Max(G, B)), mn = Math.Min(R, Math.Min(G, B));
                    double s = mx <= 0 ? 0 : (mx - mn) / mx, k = s * s * (mx / 255) + 0.001;
                    r += R * k; g += G * k; b += B * k; wt += k; sat += s; n++;
                }
                if (wt > 0 && n > 0) {
                    r /= wt; g /= wt; b /= wt; double top = Math.Max(r, Math.Max(g, b)); if (top < 1) top = 1;
                    double lift = 215 / top, a = Math.Min(1, sat / n * 2.6) * 0x6A;
                    c = Color.FromArgb((byte)a, (byte)Math.Min(255, r * lift), (byte)Math.Min(255, g * lift), (byte)Math.Min(255, b * lift));
                }
            } catch { }
            W.Dispatcher.BeginInvoke(new Action(() => { if (gen == tvGlowGen) TvGlowTo(c); }));
        };
        if (bs.IsFrozen) System.Threading.ThreadPool.QueueUserWorkItem(_ => work()); else work();
    }
    void TvGlowTo(Color c) {
        var stop = tvGlowBrush.GradientStops[0];
        if (TvCalm) { stop.BeginAnimation(GradientStop.ColorProperty, null); stop.Color = c; return; }
        stop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation { To = c, Duration = Dur(700), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
    }

    // ---------- the preview's picture drifts slowly closer (once, 24 seconds, at 30 frames a second) ----------
    void TvSlowZoom(bool on) {
        var st = tvHeroImg.RenderTransform as ScaleTransform;
        if (st == null) { st = new ScaleTransform(1, 1); tvHeroImg.RenderTransform = st; tvHeroImg.RenderTransformOrigin = new Point(0.68, 0.32); }
        st.BeginAnimation(ScaleTransform.ScaleXProperty, null); st.BeginAnimation(ScaleTransform.ScaleYProperty, null); st.ScaleX = st.ScaleY = 1;
        if (!on || TvCalm) return;
        var a = new DoubleAnimation(1, 1.07, Dur(24000)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        Timeline.SetDesiredFrameRate(a, 30);
        st.BeginAnimation(ScaleTransform.ScaleXProperty, a); st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    // ---------- a page that opens grows out of the picked tile ----------
    void TvZoomFromTile(FrameworkElement page) {
        var st = page.RenderTransform as ScaleTransform; if (st == null) { st = new ScaleTransform(1, 1); page.RenderTransform = st; }
        st.BeginAnimation(ScaleTransform.ScaleXProperty, null); st.BeginAnimation(ScaleTransform.ScaleYProperty, null); st.ScaleX = st.ScaleY = 1;
        if (TvCalm) return;
        var origin = new Point(0.5, 0.5);
        try {
            if (tvMarked != null && tvMarked.IsVisible && tvLayer.ActualWidth > 0) {
                var c = tvMarked.TransformToAncestor(tvLayer).Transform(new Point(tvMarked.ActualWidth / 2, tvMarked.ActualHeight / 2));
                origin = new Point(Math.Max(0, Math.Min(1, c.X / tvLayer.ActualWidth)), Math.Max(0, Math.Min(1, c.Y / tvLayer.ActualHeight)));
            }
        } catch { }
        page.RenderTransformOrigin = origin;
        st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1, 300, 0.9)); st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1, 300, 0.9));
    }

    // ---------- posters far from view are let go (a provider's page can have thousands), and come back near ----------
    DispatcherTimer tvPosterTimer;
    // a tile's picture, remembered so it can be let go and fetched again
    class TvPic { public string Url; public int Px; public Action Ok; }
    static void TvRememberPicture(Image img, string url, int px, Action ok) { img.Tag = new TvPic { Url = url, Px = px, Ok = ok }; }
    // is this row near enough to the picked one to get its pictures now (the rest come when scrolled near)
    bool TvRowSoon(TvRow r) { int i = tvRowList.IndexOf(r); return i < 0 || i <= 4 || Math.Abs(i - tvRowIdx) <= 3; }
    void TvPostersSoon() {
        if (tvPosterTimer == null) { tvPosterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) }; tvPosterTimer.Tick += (s, e) => { tvPosterTimer.Stop(); TvPostersNow(); }; }
        tvPosterTimer.Stop(); tvPosterTimer.Start();
    }
    void TvPostersNow() {
        if (tvScroll == null || tvPageGrid == null || !TvOpen) return;
        double off = tvScroll.VerticalOffset, vh = Math.Max(400, tvScroll.ViewportHeight);
        foreach (var r in tvRowList) {
            if (r.Root == null || r.Root.ActualHeight <= 0) continue;
            double y; try { y = r.Root.TransformToAncestor(tvPageGrid).Transform(new Point(0, 0)).Y; } catch { continue; }
            double top = y, bottom = y + r.Root.ActualHeight;
            bool near = bottom > off - vh * 1.5 && top < off + vh * 2.5, far = bottom < off - vh * 4 || top > off + vh * 5;
            if (!near && !far) continue;
            foreach (var t in r.Tiles) {
                var g = t.Child as Grid; if (g == null) continue;
                foreach (var img in g.Children.OfType<Image>()) {
                    var mem = img.Tag as TvPic; if (mem == null) continue;
                    if (far && img.Source != null) { img.BeginAnimation(UIElement.OpacityProperty, null); img.Source = null; }
                    else if (near && img.Source == null) TvLoadImage(img, mem.Url, mem.Px, mem.Ok);
                }
            }
        }
    }

    // ---------- another section: its page fades in and lifts a little ----------
    void TvPageIn() {
        if (tvScroll == null) return;
        if (TvCalm) { tvScroll.BeginAnimation(UIElement.OpacityProperty, null); tvScroll.Opacity = 1; return; }
        var tt = tvScroll.RenderTransform as TranslateTransform; if (tt == null) { tt = new TranslateTransform(); tvScroll.RenderTransform = tt; }
        tvScroll.BeginAnimation(UIElement.OpacityProperty, Anim(1, 260, 0));
        tt.BeginAnimation(TranslateTransform.YProperty, Anim(0, 320, 18));
    }
}
}
