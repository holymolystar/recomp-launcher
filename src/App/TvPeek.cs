// TV & Movies: a film or series you stay on in a row opens up, as on a streaming service. After a moment the poster
// grows into a card over the row: the wide picture with the title, Play, My list and More info, then what it is (year,
// length, rating) and its genres. Moving on (the controller, the mouse, scrolling) puts it away again.
// Controller: A on a tile whose card is open steps into the card (Play first); left / right go between Play, My list
// and More info, A presses it, B (or up / down) goes back to the tile. A before the card opens still opens the page.
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace RecompLauncher {

partial class Shell {
    Canvas tvPeekLayer; Border tvPeek; TvItem tvPeekItem; DispatcherTimer tvPeekTimer;
    readonly System.Collections.Generic.List<Border> tvPeekBtns = new System.Collections.Generic.List<Border>();
    readonly System.Collections.Generic.List<Action> tvPeekActs = new System.Collections.Generic.List<Action>();
    int tvPeekIdx = -1;   // the card's button the controller is on (-1: on the tile, not in the card)
    const double TvPeekW = 380, TvPeekPicH = 214;

    static bool TvPeekable(TvItem it) { return it != null && (it.Kind == "movie" || it.Kind == "series" || it.Kind == "episode" || (it.Kind == "file")); }

    // a moment after a tile is picked, its card (again)
    void TvPeekSoon() {
        TvPeekHide();
        if (tvPeekTimer == null) {
            tvPeekTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            tvPeekTimer.Tick += (s, e) => {
                tvPeekTimer.Stop();
                if (!TvOpen || TvPlaying || TvDetailOpen || tvZone != "rows" || tvRowIdx < 0 || tvRowIdx >= tvRowList.Count) return;
                var r = tvRowList[tvRowIdx]; if (r.Col < 0 || r.Col >= r.Tiles.Count) return;
                TvPeekShow(r, r.Col);
            };
            // scrolling puts it away; it comes back over the picked tile once the page stands still
            tvScroll.ScrollChanged += (s, e) => { if ((e.VerticalChange != 0 || e.HorizontalChange != 0) && (tvPeek != null || tvPeekTimer.IsEnabled)) TvPeekSoon(); };
        }
        tvPeekTimer.Start();
    }
    void TvPeekHide() {
        if (tvPeekTimer != null) tvPeekTimer.Stop();
        if (tvPeek != null && tvPeekLayer != null) tvPeekLayer.Children.Remove(tvPeek);
        tvPeek = null; tvPeekItem = null; tvPeekIdx = -1;
        TvTrailerStop();   // its trailer stops with it (TvTrailer.cs)
    }

    void TvPeekShow(TvRow r, int col) {
        var it = r.Items[col]; var tile = r.Tiles[col];
        if (!TvPeekable(it) || !tile.IsLoaded) return;
        if (tvPeekLayer == null) { tvPeekLayer = new Canvas(); tvContent.Children.Add(tvPeekLayer); }
        if (tvPeek != null) tvPeekLayer.Children.Remove(tvPeek);
        tvPeekItem = it;
        if (tvTrailerMouse != null) tvTrailerMouse.Stop();
        var card = tvPeek = TvPeekCard(it, r);
        // over the tile, kept on the screen
        Point at; try { at = tile.TransformToAncestor(tvContent).Transform(new Point(0, 0)); } catch { return; }
        card.Measure(new Size(TvPeekW, double.PositiveInfinity));
        double h = card.DesiredSize.Height, cw = tvContent.ActualWidth, ch = tvContent.ActualHeight;
        if (at.Y + tile.ActualHeight < 80 || at.Y > ch - 40) { tvPeek = null; tvPeekItem = null; return; }   // the tile isn't on the screen
        double left = at.X + tile.ActualWidth / 2 - TvPeekW / 2, top = at.Y + tile.ActualHeight / 2 - h / 2;
        left = Math.Max(24, Math.Min(cw - TvPeekW - 24, left)); top = Math.Max(90, Math.Min(ch - h - 16, top));
        Canvas.SetLeft(card, left); Canvas.SetTop(card, top);
        tvPeekLayer.Children.Insert(0, card);
        TvTrailerStart(it, r, left, top);   // its trailer over the picture, once it plays (TvTrailer.cs)
        // it grows out of the tile
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var st = (ScaleTransform)card.RenderTransform;
        st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        // the details (the wide picture, the genres) come from Cinemeta the first time
        TvEnrichAsync(it, () => { if (tvPeekItem == it && tvPeek != null && tvZone == "rows") { var keep = tvPeek; var c = TvPeekCard(it, r); Canvas.SetLeft(c, Canvas.GetLeft(keep)); Canvas.SetTop(c, Canvas.GetTop(keep)); tvPeekLayer.Children.Remove(keep); tvPeekLayer.Children.Insert(0, c); tvPeek = c; TvPeekMark(); } });
    }

    Border TvPeekCard(TvItem it, TvRow r) {
        var stack = new StackPanel();
        tvPeekBtns.Clear(); tvPeekActs.Clear();
        // the picture, with the title (or its logo) on it
        var pic = new Grid { Height = TvPeekPicH, ClipToBounds = true };
        int hsh = Math.Abs((it.Title ?? "").GetHashCode()); string[] pal = { "#5b2a86", "#1b4f72", "#7b241c", "#145a32", "#784212", "#4a235a" };
        pic.Children.Add(new Border { Background = TvTheme.Gradient(pal[hsh % pal.Length], "#0b1114") });
        string art = it.Backdrop ?? it.Image;
        if (!string.IsNullOrEmpty(art)) {
            var img = new Image { Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top, Opacity = 0 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            TvLoadImage(img, art, 600, null); pic.Children.Add(img);
        }
        pic.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), TvTheme.Col("#1A1A1C"), 90) { StartPoint = new Point(0, 0.45), EndPoint = new Point(0, 1) } });
        var name = new TextBlock { Text = it.Title, FontSize = 22, FontFamily = TvFont, FontWeight = FontWeights.Black, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxHeight = 60, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 16, 10), Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 0, Opacity = 0.7, Color = Colors.Black } };
        pic.Children.Add(name);
        if (!string.IsNullOrEmpty(it.Logo)) {
            var logo = new Image { MaxHeight = 64, MaxWidth = 230, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 16, 12), Visibility = Visibility.Collapsed };
            TvLoadImage(logo, it.Logo, 460, () => { logo.Visibility = Visibility.Visible; name.Visibility = Visibility.Collapsed; });
            pic.Children.Add(logo);
        }
        stack.Children.Add(pic);
        // Play, My list, More info
        var info = new StackPanel { Margin = new Thickness(16, 12, 16, 16) };
        var buttons = new Grid();
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        bool series = it.Kind == "series";
        left.Children.Add(TvPeekButton("", series ? "Episodes" : TvStore.ResumeAt(it) > 0 ? "Resume" : "Play", true, () => { TvPeekHide(); if (series) TvPick(it, r); else TvPlay(it, null); }));
        Border fav = null;
        fav = TvPeekButton(TvStore.IsFav(it) ? "" : "", TvStore.IsFav(it) ? "In My list" : "My list", false, () => {
            TvStore.ToggleFav(it); Sound.Play(Sfx.Select);
            ((TextBlock)fav.Child).Text = TvStore.IsFav(it) ? "" : ""; fav.ToolTip = TvStore.IsFav(it) ? "In My list" : "My list";
        });
        left.Children.Add(fav);
        buttons.Children.Add(left);
        var more = TvPeekButton("", "More info", false, () => { TvPeekHide(); TvPick(it, r); }); more.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Children.Add(more);
        info.Children.Add(buttons);
        // what it is
        var meta = new TextBlock { Margin = new Thickness(0, 12, 0, 0) }; TvMeta(meta, it, null); meta.FontSize = 12.5;
        info.Children.Add(meta);
        if (!string.IsNullOrEmpty(it.Plot))
            info.Children.Add(new TextBlock { Text = it.Plot, FontSize = 13.5, Foreground = TvTheme.Brush("#C8FFFFFF"), TextWrapping = TextWrapping.Wrap, MaxHeight = 58, TextTrimming = TextTrimming.WordEllipsis, Margin = new Thickness(0, 8, 0, 0), LineHeight = 19 });
        stack.Children.Add(info);
        var card = new Border { Child = stack, Width = TvPeekW, CornerRadius = new CornerRadius(12), Background = TvTheme.Brush("#1A1A1C"), Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1),
            Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 6, Opacity = 0.75, Color = Colors.Black } };
        stack.Clip = null; pic.Clip = new RectangleGeometry(new Rect(0, 0, TvPeekW, TvPeekPicH + 20), 12, 12);
        card.MouseLeftButtonUp += (s, e) => { if (e.Handled) return; TvPeekHide(); TvPick(it, r); };
        card.MouseLeave += (s, e) => TvPeekMouseLeft();
        card.MouseRightButtonUp += (s, e) => { e.Handled = true; TvPeekHide(); TvItemActions(it); };
        return card;
    }
    Border TvPeekButton(string glyph, string tip, bool main, Action act) {
        var b = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(21), Margin = new Thickness(0, 0, 10, 0), ToolTip = tip, Cursor = Cursors.Hand,
            Background = main ? Brushes.White : TvTheme.Brush("#331C1C1E"), BorderBrush = main ? Brushes.White : TvTheme.Brush("#99FFFFFF"), BorderThickness = new Thickness(2),
            Child = new TextBlock { Text = glyph, FontFamily = Ui.Mdl, FontSize = 16, Foreground = main ? Brushes.Black : Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        b.MouseEnter += (s, e) => { if (!main) b.BorderBrush = Brushes.White; };
        b.MouseLeave += (s, e) => { if (!main) b.BorderBrush = TvTheme.Brush("#99FFFFFF"); };
        b.MouseLeftButtonUp += (s, e) => { e.Handled = true; act(); };
        b.Tag = main; b.RenderTransformOrigin = new Point(0.5, 0.5); b.RenderTransform = new ScaleTransform(1, 1);
        tvPeekBtns.Add(b); tvPeekActs.Add(act);
        return b;
    }

    // ---------- the card with the controller ----------
    // true when the press was the card's
    bool TvPeekPad(int bit) {
        if (tvPeek == null || tvZone != "rows" || tvPeekBtns.Count == 0) { tvPeekIdx = -1; return false; }
        if (tvPeekIdx < 0) {
            if (bit != Pad.A || tvPeekItem != tvFocus) return false;
            tvPeekIdx = 0; Sound.Play(Sfx.Move); TvPeekMark(); return true;   // into the card, on Play
        }
        if (bit == Pad.Left || bit == Pad.Right) {
            int j = tvPeekIdx + (bit == Pad.Left ? -1 : 1);
            if (j >= 0 && j < tvPeekBtns.Count) { tvPeekIdx = j; Sound.Play(Sfx.Move); TvPeekMark(); } else Sound.Play(Sfx.Bump);
        }
        else if (bit == Pad.A) { Sound.Play(Sfx.Select); var act = tvPeekActs[tvPeekIdx]; act(); if (tvPeek != null) TvPeekMark(); }
        else if (bit == Pad.B || bit == Pad.Up || bit == Pad.Down) { tvPeekIdx = -1; Sound.Play(Sfx.Back); TvPeekMark(); }
        else if (bit == Pad.Y) { int f = tvPeekBtns.Count > 1 ? 1 : 0; tvPeekActs[f](); TvPeekMark(); }   // Y: My list, as on the tile
        else return true;
        SetHints();
        return true;
    }
    // the button the controller is on: bigger, with a white ring and glow
    void TvPeekMark() {
        for (int i = 0; i < tvPeekBtns.Count; i++) {
            var b = tvPeekBtns[i]; bool on = i == tvPeekIdx, main = b.Tag is bool && (bool)b.Tag;
            var st = b.RenderTransform as ScaleTransform; double k = on ? 1.16 : 1;
            if (st != null && Math.Abs(st.ScaleX - k) > 0.001) { st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(k, 140)); st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(k, 140)); }
            b.BorderBrush = on ? (main ? TvTheme.Brush(TvAccent) : Brushes.White) : main ? Brushes.White : TvTheme.Brush("#99FFFFFF");
            b.Effect = on ? new DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9, Color = Colors.White } : null;
        }
    }
}
}
