// Pixel art: draw a game's icon yourself, pixel by pixel, with the controller (or the mouse). It opens on the picked
// game with its own icon turned into 32 x 32 pixels to start from (or an empty one), and saves to icons\<id>.png,
// the picture the launcher shows for that game everywhere (Icons.Custom). Every game drawn this way shares the same
// grid of pixels and the same palette, so a row of them looks like one set.
// Controller: the stick / d-pad moves; A paints (hold A while moving to draw a line), X erases (hold it too), Y fills
// the area, LB / RB pick the colour, RT takes the colour under the cursor, LT undoes, View clears, Menu saves,
// B leaves (twice when there are changes). Mouse: left paints, right erases, the wheel picks the colour.
// Keyboard: arrows, Space paint, Delete erase, F fill, Q / E colour, Ctrl+Z undo, Ctrl+S save, Escape leave.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace RecompLauncher {

partial class Shell {
    const int PxN = 32, PxCell = 16;
    // 32 colours: greys, then warm to cool, light and dark of each, so most icons can be drawn from them
    static readonly string[] PxPalette = {
        "#00000000", "#000000", "#3b3b4f", "#6b6b85", "#a3a3b8", "#ffffff", "#5a2d1e", "#9c5a3c",
        "#e8a57c", "#ffd9b8", "#7a1f2b", "#d8323c", "#ff7a7a", "#c24d00", "#ff8a1c", "#ffc94d",
        "#fff27a", "#2b5a1e", "#3fa34d", "#8ce06b", "#0f4a4a", "#1fa6a0", "#7fe3d6", "#1c2f7a",
        "#2f6ae0", "#6fb4ff", "#b8e4ff", "#3d1f6e", "#7b4ce0", "#c49cff", "#a1246e", "#ff6ec7" };
    Grid pxLayer; Image pxImage; Rectangle pxCursor; Border pxPreview; Image pxPreviewImg; WrapPanel pxSwatches; TextBlock pxTitle, pxMsg;
    WriteableBitmap pxBmp; uint[] pxData = new uint[PxN * PxN]; readonly List<uint[]> pxUndo = new List<uint[]>();
    int pxX = PxN / 2, pxY = PxN / 2, pxColor = 5; bool pxChanged, pxLeaveAsked, pxClearAsked, pxMouseDown; Game pxGame;
    bool PixelOpen { get { return pxLayer != null && pxLayer.Visibility == Visibility.Visible; } }

    void BuildPixelArt() {
        if (pxLayer != null) return;
        pxLayer = new Grid { Visibility = Visibility.Collapsed, Background = Ui.Brush("#090C0F") };
        // the same soft glow as Home's Glow style behind it
        pxLayer.Children.Add(StyleBackdrop(0.7));
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        // the canvas: a checkerboard for the empty pixels, the picture, a faint grid, the cursor
        double side = PxN * PxCell;
        var canvas = new Grid { Width = side, Height = side, Cursor = Cursors.Pen, Background = Checker() };
        pxBmp = new WriteableBitmap(PxN, PxN, 96, 96, PixelFormats.Bgra32, null);
        pxImage = new Image { Source = pxBmp, Width = side, Height = side, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(pxImage, BitmapScalingMode.NearestNeighbor);
        canvas.Children.Add(pxImage);
        canvas.Children.Add(new Rectangle { Fill = GridLines(), IsHitTestVisible = false });
        pxCursor = new Rectangle { Width = PxCell + 4, Height = PxCell + 4, Stroke = Brushes.White, StrokeThickness = 2.5, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false, Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 1, Color = Colors.Black } };
        canvas.Children.Add(pxCursor);
        canvas.MouseLeftButtonDown += (s, e) => { pxMouseDown = true; canvas.CaptureMouse(); PxUndoPoint(); PxMouse(e.GetPosition(canvas), false); };
        canvas.MouseRightButtonDown += (s, e) => { pxMouseDown = true; canvas.CaptureMouse(); PxUndoPoint(); PxMouse(e.GetPosition(canvas), true); };
        canvas.MouseMove += (s, e) => {
            var p = e.GetPosition(canvas);
            if (pxMouseDown && (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed)) PxMouse(p, e.RightButton == MouseButtonState.Pressed);
            else { int x = (int)(p.X / PxCell), y = (int)(p.Y / PxCell); if (x >= 0 && y >= 0 && x < PxN && y < PxN) { pxX = x; pxY = y; PxPlaceCursor(); } }
        };
        MouseButtonEventHandler up = (s, e) => { pxMouseDown = false; canvas.ReleaseMouseCapture(); };
        canvas.MouseLeftButtonUp += up; canvas.MouseRightButtonUp += up;
        canvas.MouseWheel += (s, e) => { PxPickColor(pxColor + (e.Delta < 0 ? 1 : -1)); e.Handled = true; };
        var frame = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(14), Background = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)), BorderThickness = new Thickness(1.2), Child = canvas,
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.4, Color = Colors.Black } };
        row.Children.Add(frame);
        // the side: the game, how the icon looks on Home, the palette, the buttons
        var side2 = new StackPanel { Width = 400, Margin = new Thickness(46, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        side2.Children.Add(new TextBlock { Text = "PIXEL ART", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal) });
        pxTitle = new TextBlock { FontSize = 26, FontFamily = Ui.Semi, Foreground = Ui.Brush(Ui.Text), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 16) };
        side2.Children.Add(pxTitle);
        pxPreviewImg = new Image { Source = pxBmp, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(pxPreviewImg, BitmapScalingMode.NearestNeighbor);
        pxPreview = new Border { Width = 132, Height = 132, CornerRadius = new CornerRadius(31), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, Child = pxPreviewImg,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 5, Opacity = 0.45, Color = Colors.Black } };
        pxPreview.Clip = new RectangleGeometry(new Rect(0, 0, 132, 132), 31, 31);
        side2.Children.Add(pxPreview);
        pxSwatches = new WrapPanel { Margin = new Thickness(0, 22, 0, 0), Width = 8 * 44 };
        for (int i = 0; i < PxPalette.Length; i++) {
            int idx = i;
            var sw = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, BorderThickness = new Thickness(3),
                Background = i == 0 ? Checker() : new SolidColorBrush((Color)ColorConverter.ConvertFromString(PxPalette[i])) };
            if (i == 0) sw.Child = new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 15, Foreground = Ui.Brush("#e06c5b"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Eraser" };
            sw.MouseLeftButtonUp += (s, e) => PxPickColor(idx);
            pxSwatches.Children.Add(sw);
        }
        side2.Children.Add(pxSwatches);
        var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        Func<string, Action, Button> btn = (text, act) => { var b = MakeButton(text); b.Margin = new Thickness(0, 0, 10, 10); b.Click += (s, e) => act(); b.Focusable = false; buttons.Children.Add(b); return b; };
        btn("Save", PxSave); btn("Undo", PxUndo); btn("Fill", () => PxFill(pxX, pxY)); btn("Start again", PxClear); btn("Leave", PxLeave);
        side2.Children.Add(buttons);
        side2.Children.Add(new TextBlock { FontSize = 12.5, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), LineHeight = 20,
            Text = "A paint (hold it to draw)   X erase   Y fill\nLB / RB colour   RT take a colour   LT undo\nView start again   Menu save   B leave" });
        pxMsg = new TextBlock { FontSize = 13, FontFamily = Ui.Semi, Foreground = Ui.Brush(Ui.Gold), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        side2.Children.Add(pxMsg);
        row.Children.Add(side2);
        pxLayer.Children.Add(row);
        N<Grid>("Clipped").Children.Add(pxLayer);
        W.RegisterName("PixelLayer", pxLayer);
        ApplyScale();
    }
    // the checkerboard that shows where a pixel is empty
    static Brush Checker() {
        var dg = new DrawingGroup();
        using (var dc = dg.Open()) {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2a, 0x2c, 0x3a)), null, new Rect(0, 0, 16, 16));
            var b = new SolidColorBrush(Color.FromRgb(0x22, 0x24, 0x30));
            dc.DrawRectangle(b, null, new Rect(0, 0, 8, 8)); dc.DrawRectangle(b, null, new Rect(8, 8, 8, 8));
        }
        var br = new DrawingBrush(dg) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None }; br.Freeze();
        return br;
    }
    static Brush GridLines() {
        var dg = new DrawingGroup();
        using (var dc = dg.Open()) {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, PxCell, PxCell));
            var line = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255));
            dc.DrawRectangle(line, null, new Rect(0, 0, PxCell, 1)); dc.DrawRectangle(line, null, new Rect(0, 0, 1, PxCell));
        }
        var br = new DrawingBrush(dg) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, PxCell, PxCell), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None }; br.Freeze();
        return br;
    }

    // ---------- opening and leaving ----------
    void OpenPixelArt(Game g) {
        if (g == null || g.App != null || IsFolder(g) || IsFolderBack(g)) { SetStep("Pick a game first: pixel art draws the icon of the game that's picked.", Ui.Gold); Sound.Play(Sfx.Bump); return; }
        BuildPixelArt();
        pxGame = g; pxUndo.Clear(); pxChanged = false; pxLeaveAsked = false; pxClearAsked = false; pxMsg.Text = "";
        pxTitle.Text = g.Name;
        pxPreview.Background = Ui.Gradient(g.Accent, g.Accent2);
        PxFromIcon((ImageSource)g.Icon);
        pxX = PxN / 2; pxY = PxN / 2; PxPlaceCursor(); PxPickColor(pxColor);
        pxLayer.Visibility = Visibility.Visible;
        pxLayer.BeginAnimation(UIElement.OpacityProperty, Anim(1, 220, 0));
        Sound.Play(Sfx.Open);
        SetHints();
    }
    void ClosePixelArt() {
        if (!PixelOpen) return;
        Sound.Play(Sfx.Back);
        var a = Anim(0, 180); a.Completed += (s, e) => { pxLayer.Visibility = Visibility.Collapsed; SetHints(); };
        pxLayer.BeginAnimation(UIElement.OpacityProperty, a);
    }
    void PxLeave() {
        if (pxChanged && !pxLeaveAsked) { pxLeaveAsked = true; pxMsg.Text = "Not saved yet. Press B again to leave without saving, or Menu to save."; Sound.Play(Sfx.Bump); return; }
        ClosePixelArt();
    }

    // the game's icon as 32 x 32 pixels: its colours taken from the middle of each block (sharp, as pixel art is)
    void PxFromIcon(ImageSource src) {
        Array.Clear(pxData, 0, pxData.Length);
        var bs = src as BitmapSource;
        if (bs == null && src != null) {
            try {
                var dv = new DrawingVisual(); using (var dc = dv.RenderOpen()) dc.DrawImage(src, new Rect(0, 0, 256, 256));
                var rt = new RenderTargetBitmap(256, 256, 96, 96, PixelFormats.Pbgra32); rt.Render(dv); bs = rt;
            } catch { bs = null; }
        }
        if (bs != null) {
            try {
                var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight; var all = new uint[w * h];
                conv.CopyPixels(all, w * 4, 0);
                // square: the middle of a wide or tall picture, as the tiles crop it
                int sz = Math.Min(w, h), ox = (w - sz) / 2, oy = (h - sz) / 2;
                for (int y = 0; y < PxN; y++) for (int x = 0; x < PxN; x++) {
                    int sx = ox + (int)((x + 0.5) * sz / PxN), sy = oy + (int)((y + 0.5) * sz / PxN);
                    uint c = all[Math.Min(h - 1, sy) * w + Math.Min(w - 1, sx)];
                    pxData[y * PxN + x] = (c >> 24) < 0x60 ? 0 : (c | 0xFF000000);
                }
            } catch { Array.Clear(pxData, 0, pxData.Length); }
        }
        PxDraw();
    }
    void PxDraw() {
        pxBmp.WritePixels(new Int32Rect(0, 0, PxN, PxN), pxData, PxN * 4, 0);
    }
    void PxPlaceCursor() { if (pxCursor != null) pxCursor.Margin = new Thickness(pxX * PxCell - 2, pxY * PxCell - 2, 0, 0); }
    static uint PxArgb(string hex) { var c = (Color)ColorConverter.ConvertFromString(hex); return ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B; }
    void PxPickColor(int i) {
        pxColor = ((i % PxPalette.Length) + PxPalette.Length) % PxPalette.Length;
        for (int k = 0; k < pxSwatches.Children.Count; k++) ((Border)pxSwatches.Children[k]).BorderBrush = k == pxColor ? Brushes.White : Brushes.Transparent;
        pxCursor.Fill = pxColor == 0 ? null : new SolidColorBrush((Color)ColorConverter.ConvertFromString(PxPalette[pxColor])) { Opacity = 0.45 };
    }

    // ---------- drawing ----------
    void PxUndoPoint() { pxUndo.Add((uint[])pxData.Clone()); if (pxUndo.Count > 60) pxUndo.RemoveAt(0); pxClearAsked = false; }
    void PxSet(int x, int y, uint c) {
        if (x < 0 || y < 0 || x >= PxN || y >= PxN || pxData[y * PxN + x] == c) return;
        pxData[y * PxN + x] = c; pxChanged = true; pxLeaveAsked = false; PxDraw();
    }
    void PxPaint(bool erase) { PxSet(pxX, pxY, erase || pxColor == 0 ? 0 : PxArgb(PxPalette[pxColor])); }
    void PxMouse(Point p, bool erase) {
        int x = (int)(p.X / PxCell), y = (int)(p.Y / PxCell);
        if (x < 0 || y < 0 || x >= PxN || y >= PxN) return;
        pxX = x; pxY = y; PxPlaceCursor(); PxPaint(erase);
    }
    // the area of one colour under the cursor, filled with the picked colour
    void PxFill(int x0, int y0) {
        uint from = pxData[y0 * PxN + x0], to = pxColor == 0 ? 0 : PxArgb(PxPalette[pxColor]);
        if (from == to) { Sound.Play(Sfx.Bump); return; }
        PxUndoPoint();
        var todo = new Stack<int>(); todo.Push(y0 * PxN + x0);
        while (todo.Count > 0) {
            int i = todo.Pop(); if (pxData[i] != from) continue;
            pxData[i] = to; int x = i % PxN, y = i / PxN;
            if (x > 0) todo.Push(i - 1); if (x < PxN - 1) todo.Push(i + 1); if (y > 0) todo.Push(i - PxN); if (y < PxN - 1) todo.Push(i + PxN);
        }
        pxChanged = true; pxLeaveAsked = false; PxDraw(); Sound.Play(Sfx.Select);
    }
    void PxUndo() {
        if (pxUndo.Count == 0) { Sound.Play(Sfx.Bump); return; }
        pxData = pxUndo[pxUndo.Count - 1]; pxUndo.RemoveAt(pxUndo.Count - 1); pxChanged = true; PxDraw(); Sound.Play(Sfx.Back);
    }
    void PxEyedrop() {
        uint c = pxData[pxY * PxN + pxX];
        int best = 0; double bestD = double.MaxValue;
        if (c != 0)
            for (int i = 1; i < PxPalette.Length; i++) {
                uint p = PxArgb(PxPalette[i]);
                double d = Sq((int)((p >> 16) & 255) - (int)((c >> 16) & 255)) + Sq((int)((p >> 8) & 255) - (int)((c >> 8) & 255)) + Sq((int)(p & 255) - (int)(c & 255));
                if (d < bestD) { bestD = d; best = i; }
            }
        PxPickColor(best); Sound.Play(Sfx.Select);
    }
    static double Sq(int v) { return (double)v * v; }
    void PxClear() {
        if (!pxClearAsked) { pxClearAsked = true; pxMsg.Text = "Start again with an empty picture? Press it again to clear."; Sound.Play(Sfx.Bump); return; }
        pxClearAsked = false; PxUndoPoint(); Array.Clear(pxData, 0, pxData.Length); pxChanged = true; PxDraw(); pxMsg.Text = "Cleared. LT (or Undo) brings it back."; Sound.Play(Sfx.Select);
    }
    // saved as a sharp 256 x 256 picture: icons\<id>.png, which the launcher uses for the game from then on
    void PxSave() {
        try {
            const int S = 256;
            var big = new uint[S * S]; int k = S / PxN;
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) big[y * S + x] = pxData[(y / k) * PxN + x / k];
            var bmp = BitmapSource.Create(S, S, 96, 96, PixelFormats.Bgra32, null, big, S * 4);
            string dir = Path.Combine(Paths.Root, "icons"); Directory.CreateDirectory(dir);
            foreach (var ext in new[] { "jpg", "jpeg" }) { string old = Path.Combine(dir, pxGame.Id + "." + ext); if (File.Exists(old)) File.Move(old, old + ".before-pixel-art"); }
            string file = Path.Combine(dir, pxGame.Id + ".png");
            using (var fs = File.Create(file)) { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp)); enc.Save(fs); }
            File.WriteAllText(Path.Combine(dir, pxGame.Id + ".pixel"), "drawn in Pixel art: shown sharp");
            Icons.Load(pxGame); ApplyIcon(pxGame);
            pxChanged = false; pxLeaveAsked = false;
            Sound.Play(Sfx.Select);
            pxMsg.Text = "Saved: " + pxGame.Name + " shows your pixel art now.";
            SetStep(pxGame.Name + ": your pixel art is its icon now (icons\\" + pxGame.Id + ".png).", Ui.Teal);
        } catch (Exception e) { pxMsg.Text = "Couldn't save: " + e.Message; Sound.Play(Sfx.Bump); }
    }

    // ---------- controller and keyboard ----------
    void PixelPad(int bit) {
        if (bit == Pad.B) { PxLeave(); return; }
        if (bit == Pad.Start) { PxSave(); return; }
        if (bit == Pad.Back) { PxClear(); return; }
        if (bit == Pad.LB || bit == Pad.RB) { PxPickColor(pxColor + (bit == Pad.LB ? -1 : 1)); Sound.Play(Sfx.Move); return; }
        if (bit == Pad.LT) { PxUndo(); return; }
        if (bit == Pad.RT) { PxEyedrop(); return; }
        if (bit == Pad.Y) { PxFill(pxX, pxY); return; }
        if (bit == Pad.A || bit == Pad.X) { PxUndoPoint(); PxPaint(bit == Pad.X); return; }
        int dx = bit == Pad.Left ? -1 : bit == Pad.Right ? 1 : 0, dy = bit == Pad.Up ? -1 : bit == Pad.Down ? 1 : 0;
        if (dx == 0 && dy == 0) return;
        int nx = Math.Max(0, Math.Min(PxN - 1, pxX + dx)), ny = Math.Max(0, Math.Min(PxN - 1, pxY + dy));
        if (nx == pxX && ny == pxY) { Sound.Play(Sfx.Bump); return; }
        pxX = nx; pxY = ny; PxPlaceCursor();
        // A or X held while moving: a line
        if ((padPrev & Pad.A) != 0) PxPaint(false); else if ((padPrev & Pad.X) != 0) PxPaint(true); else Sound.Play(Sfx.Move);
    }
    bool PixelKey(Key k) {
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl && k == Key.Z) { PxUndo(); return true; }
        if (ctrl && k == Key.S) { PxSave(); return true; }
        int bit = k == Key.Left ? Pad.Left : k == Key.Right ? Pad.Right : k == Key.Up ? Pad.Up : k == Key.Down ? Pad.Down
            : k == Key.Space || k == Key.Enter ? Pad.A : k == Key.Delete ? Pad.X : k == Key.F ? Pad.Y : k == Key.Q ? Pad.LB : k == Key.E ? Pad.RB
            : k == Key.Escape ? Pad.B : 0;
        if (bit == 0) return false;
        PixelPad(bit);
        return true;
    }
}
}
