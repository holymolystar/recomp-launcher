// Flip book: draw little animations frame by frame, with the controller or the mouse, on a small sheet of paper.
// The frame before shows faintly under the one you draw on (onion skin), so each drawing can move on a little from
// the last; Play runs them as a cartoon. Saved to Pictures\Recomp Launcher\Flip book\ as an animated GIF (to share)
// and a sheet of its frames with a few details (to open again and go on drawing).
// Controller: the left stick moves the pen (the further, the faster; the d-pad a pixel at a time while A or X is
// not held), A draws (hold it), X erases (hold it), LB / RB pick the colour, Y the pen size; d-pad left / right go to
// the frame before or after, up adds a new frame after this one, down copies this one after it; LT undoes, RT plays
// or stops, View clears the frame (twice), Menu saves, B leaves (twice when there are changes).
// Mouse: left draws, right erases, the wheel picks the colour; the buttons on the right do the rest.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace RecompLauncher {

partial class Shell {
    const int FW = 256, FH = 192, FScale = 3;
    static readonly string[] FlipInks = { "#141414", "#e8453c", "#2f6ae0", "#3fa34d", "#ffb81c", "#9b4ce0", "#ff6ec7", "#ffffff" };
    static readonly string[] FlipInkNames = { "Black", "Red", "Blue", "Green", "Yellow", "Purple", "Pink", "White" };
    static readonly string[] FlipPapers = { "#fbfaf4", "#f4ead2", "#202326" };
    static readonly int[] FlipSpeeds = { 2, 4, 6, 8, 12, 15, 20 };
    static readonly int[] FlipSizes = { 1, 2, 4, 7 };
    Grid flLayer, flStage; Image flImage, flOnion; Rectangle flPaper; Ellipse flCursor; TextBlock flTitle, flFrameText, flMsg, flSpeedText, flInfo; StackPanel flStrip; WrapPanel flInkRow;
    WriteableBitmap flBmp, flOnionBmp;
    readonly List<uint[]> flFrames = new List<uint[]>(); readonly List<uint[]> flUndo = new List<uint[]>();
    int flIdx, flInk, flSize = 1, flSpeed = 3, flPaperIdx; double flX = FW / 2, flY = FH / 2, flLastX = -1, flLastY = -1;
    bool flChanged, flLeaveAsked, flClearAsked, flPlaying, flOnionOn = true, flMouse; int flPrevBits; string flFile;
    DispatcherTimer flPlayTimer; int flPlayAt;
    bool FlipOpen { get { return flLayer != null && flLayer.Visibility == Visibility.Visible; } }
    static string FlipDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Recomp Launcher", "Flip book"); } }

    void BuildFlipBook() {
        if (flLayer != null) return;
        bool light = Theme.Current.Light;
        flLayer = new Grid { Visibility = Visibility.Collapsed, Background = Ui.Brush("#090C0F") };
        flLayer.Children.Add(StyleBackdrop(0.8));
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        // the paper: its colour, the frame before (faint), this frame, the pen
        flStage = new Grid { Width = FW * FScale, Height = FH * FScale, Cursor = Cursors.None, ClipToBounds = true };
        flPaper = new Rectangle { Fill = Ui.Brush(FlipPapers[0]) };
        flStage.Children.Add(flPaper);
        flOnionBmp = new WriteableBitmap(FW, FH, 96, 96, PixelFormats.Bgra32, null);
        flOnion = new Image { Source = flOnionBmp, Stretch = Stretch.Fill, Opacity = 0.22 };
        RenderOptions.SetBitmapScalingMode(flOnion, BitmapScalingMode.NearestNeighbor);
        flStage.Children.Add(flOnion);
        flBmp = new WriteableBitmap(FW, FH, 96, 96, PixelFormats.Bgra32, null);
        flImage = new Image { Source = flBmp, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(flImage, BitmapScalingMode.NearestNeighbor);
        flStage.Children.Add(flImage);
        flCursor = new Ellipse { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, StrokeThickness = 1.5, IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Opacity = 0.8, Color = Colors.Black } };
        flStage.Children.Add(flCursor);
        flStage.MouseLeftButtonDown += (s, e) => { flMouse = true; flStage.CaptureMouse(); FlStartStroke(); FlMouseAt(e.GetPosition(flStage), false); };
        flStage.MouseRightButtonDown += (s, e) => { flMouse = true; flStage.CaptureMouse(); FlStartStroke(); FlMouseAt(e.GetPosition(flStage), true); };
        flStage.MouseMove += (s, e) => {
            var p = e.GetPosition(flStage);
            if (flMouse && (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed)) FlMouseAt(p, e.RightButton == MouseButtonState.Pressed);
            else { flX = p.X / FScale; flY = p.Y / FScale; FlPlaceCursor(); }
        };
        MouseButtonEventHandler up = (s, e) => { flMouse = false; flLastX = -1; flStage.ReleaseMouseCapture(); };
        flStage.MouseLeftButtonUp += up; flStage.MouseRightButtonUp += up;
        flStage.MouseWheel += (s, e) => { FlInk(flInk + (e.Delta < 0 ? 1 : -1)); e.Handled = true; };
        var frame = new Border { CornerRadius = new CornerRadius(22), Padding = new Thickness(14), Child = new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = flStage },
            Background = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)), BorderThickness = new Thickness(1.2),
            Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 8, Opacity = 0.45, Color = Colors.Black } };
        var leftCol = new StackPanel();
        leftCol.Children.Add(frame);
        // the frames, as a strip under the paper
        flStrip = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0), Height = 62 };
        leftCol.Children.Add(flStrip);
        row.Children.Add(leftCol);
        // the side: title, frame, inks, buttons, help
        var side = new StackPanel { Width = 360, Margin = new Thickness(40, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        side.Children.Add(new TextBlock { Text = "FLIP BOOK", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal) });
        flTitle = new TextBlock { FontSize = 26, FontFamily = new FontFamily("Segoe UI Black"), Foreground = Ui.Brush(Ui.Text), Margin = new Thickness(0, 2, 0, 4) };
        side.Children.Add(flTitle);
        flFrameText = new TextBlock { FontSize = 15, FontFamily = Ui.Semi, Foreground = Ui.Brush(Ui.Text) };
        side.Children.Add(flFrameText);
        flInfo = new TextBlock { FontSize = 13, Foreground = Ui.Brush(Ui.Muted), Margin = new Thickness(0, 2, 0, 14) };
        side.Children.Add(flInfo);
        flInkRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        for (int i = 0; i < FlipInks.Length; i++) {
            int k = i;
            var sw = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Margin = new Thickness(0, 0, 9, 9), Background = Ui.Brush(FlipInks[i]), BorderThickness = new Thickness(3), Cursor = Cursors.Hand, ToolTip = FlipInkNames[i] };
            sw.MouseLeftButtonUp += (s, e) => FlInk(k);
            flInkRow.Children.Add(sw);
        }
        side.Children.Add(flInkRow);
        var buttons = new WrapPanel();
        Action<string, Action> btn = (text, act) => { var b = MakeButton(text, 38); b.Margin = new Thickness(0, 0, 8, 8); b.Focusable = false; b.Click += (s, e) => act(); buttons.Children.Add(b); };
        btn("Pen size", () => FlSize(1)); btn("New frame", FlNewFrame); btn("Copy frame", FlCopyFrame); btn("Delete frame", FlDeleteFrame);
        btn("Play", FlTogglePlay); btn("Slower", () => FlSpeed(-1)); btn("Faster", () => FlSpeed(1)); btn("Onion skin", FlToggleOnion);
        btn("Paper", FlNextPaper); btn("Undo", FlUndoStep); btn("Clear", FlClear); btn("Save", FlSave); btn("Leave", FlLeave);
        side.Children.Add(buttons);
        flSpeedText = new TextBlock { FontSize = 12.5, Foreground = Ui.Brush(Ui.Muted), Margin = new Thickness(0, 6, 0, 0) };
        side.Children.Add(flSpeedText);
        side.Children.Add(new TextBlock { FontSize = 12.5, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), LineHeight = 20,
            Text = "Stick move   A draw   X erase   Y pen size   LB / RB colour\n◀ ▶ frame before / after   ▲ new frame   ▼ copy frame\nLT undo   RT play   View clear   Menu save   B leave" });
        flMsg = new TextBlock { FontSize = 13, FontFamily = Ui.Semi, Foreground = Ui.Brush(Ui.Gold), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        side.Children.Add(flMsg);
        row.Children.Add(side);
        // the whole of it shrinks to fit a smaller window
        flLayer.Children.Add(new Viewbox { Child = row, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(36, 30, 36, 30) });
        N<Grid>("Clipped").Children.Add(flLayer);
        W.RegisterName("FlipLayer", flLayer);
        ApplyScale();
        flPlayTimer = new DispatcherTimer();
        flPlayTimer.Tick += (s, e) => { flPlayAt = (flPlayAt + 1) % flFrames.Count; FlShow(flPlayAt, false); };
    }

    // ---------- the list of flip books, to start one or open one ----------
    ListPage flPage;
    void OpenFlipBooks() {
        if (flPage == null) {
            flPage = MakeListPage("FlipPage", "FLIP BOOK", "Flip book", 560, null);
            flPage.Fill = () => {
                flPage.Sub.Text = "Draw a little animation, frame by frame. Saved in Pictures\\Recomp Launcher\\Flip book.";
                var rows = new List<ListRow> { new ListRow { Glyph = "", C1 = "#ff6ec7", C2 = "#ffb81c", Title = "New flip book", Text = "An empty sheet of paper", Do = () => { CloseListPage(); FlOpen(null); } } };
                try {
                    if (Directory.Exists(FlipDir))
                        foreach (var f in new DirectoryInfo(FlipDir).GetFiles("*.flip.png").OrderByDescending(x => x.LastWriteTime).Take(40)) {
                            var file = f.FullName; ImageSource pic = null;
                            try { var gif = file.Replace(".flip.png", ".gif"); var bi = new BitmapImage(); bi.BeginInit(); bi.UriSource = new Uri(File.Exists(gif) ? gif : file); bi.DecodePixelWidth = 92; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze(); pic = bi; } catch { }
                            rows.Add(new ListRow { Pic = pic, Title = Path.GetFileName(file).Replace(".flip.png", ""), Text = f.LastWriteTime.ToString("d MMM yyyy, HH:mm"), Do = () => { CloseListPage(); FlOpen(file); } });
                        }
                } catch { }
                FillRows(flPage, rows, "");
            };
        }
        OpenListPage(flPage);
    }

    // ---------- opening, leaving ----------
    void FlOpen(string file) {
        BuildFlipBook();
        flFrames.Clear(); flUndo.Clear(); flIdx = 0; flChanged = false; flLeaveAsked = false; flClearAsked = false; flMsg.Text = ""; flFile = file;
        flSpeed = 3; flPaperIdx = 0; flPrevBits = padPrev;   // the A that opened it doesn't draw
        if (file != null && !FlLoad(file)) { flMsg.Text = "Couldn't open that one; here's a new one."; flFile = null; }
        if (flFrames.Count == 0) flFrames.Add(new uint[FW * FH]);
        flTitle.Text = flFile != null ? Path.GetFileName(flFile).Replace(".flip.png", "") : "New flip book";
        flPaper.Fill = Ui.Brush(FlipPapers[flPaperIdx]);
        flX = FW / 2; flY = FH / 2; FlInk(flPaperIdx == 2 ? 7 : 0); FlShow(0, true);
        flLayer.Visibility = Visibility.Visible;
        flLayer.BeginAnimation(UIElement.OpacityProperty, Anim(1, 220, 0));
        Sound.Play(Sfx.Open);
        SetHints();
    }
    void FlClose() {
        if (!FlipOpen) return;
        FlStop();
        Sound.Play(Sfx.Back);
        var a = Anim(0, 180); a.Completed += (s, e) => { flLayer.Visibility = Visibility.Collapsed; SetHints(); };
        flLayer.BeginAnimation(UIElement.OpacityProperty, a);
    }
    void FlLeave() {
        if (flChanged && !flLeaveAsked) { flLeaveAsked = true; flMsg.Text = "Not saved yet. Press B again to leave without saving, or Menu to save."; Sound.Play(Sfx.Bump); return; }
        FlClose();
    }

    // ---------- showing a frame ----------
    void FlShow(int i, bool all) {
        flIdx = Math.Max(0, Math.Min(flFrames.Count - 1, i));
        flBmp.WritePixels(new Int32Rect(0, 0, FW, FH), flFrames[flIdx], FW * 4, 0);
        if (all) {
            bool onion = flOnionOn && !flPlaying && flIdx > 0;
            flOnion.Visibility = onion ? Visibility.Visible : Visibility.Collapsed;
            if (onion) flOnionBmp.WritePixels(new Int32Rect(0, 0, FW, FH), flFrames[flIdx - 1], FW * 4, 0);
            FlStripFill();
        }
        flFrameText.Text = "Frame " + (flIdx + 1) + " of " + flFrames.Count + (flPlaying ? "   ▶ playing" : "");
        flInfo.Text = FlipInkNames[flInk] + " pen, size " + FlipSizes[flSize] + (flOnionOn ? "   •   onion skin on" : "");
        flSpeedText.Text = "Speed: " + FlipSpeeds[flSpeed] + " frames a second";
    }
    // the frames round this one, small, this one lit
    void FlStripFill() {
        flStrip.Children.Clear();
        int from = Math.Max(0, Math.Min(flIdx - 4, flFrames.Count - 9));
        for (int i = from; i < Math.Min(flFrames.Count, from + 9); i++) {
            var wb = new WriteableBitmap(FW, FH, 96, 96, PixelFormats.Bgra32, null); wb.WritePixels(new Int32Rect(0, 0, FW, FH), flFrames[i], FW * 4, 0);
            var g = new Grid { Width = 72, Height = 54 };
            g.Children.Add(new Rectangle { Fill = Ui.Brush(FlipPapers[flPaperIdx]) });
            g.Children.Add(new Image { Source = wb, Stretch = Stretch.Fill });
            g.Children.Add(new TextBlock { Text = (i + 1).ToString(), FontSize = 10, Foreground = Brushes.Gray, Margin = new Thickness(4, 2, 0, 0) });
            int k = i;
            var b = new Border { Child = g, CornerRadius = new CornerRadius(6), ClipToBounds = true, Margin = new Thickness(3, 0, 3, 0), BorderThickness = new Thickness(3), Cursor = Cursors.Hand,
                BorderBrush = i == flIdx ? Ui.Brush(Ui.Teal) : new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)) };
            b.MouseLeftButtonUp += (s, e) => { FlStop(); FlShow(k, true); Sound.Play(Sfx.Move); };
            flStrip.Children.Add(b);
        }
    }
    void FlPlaceCursor() {
        double r = Math.Max(2, FlipSizes[flSize] * FScale / 2.0 + 1);
        flCursor.Width = flCursor.Height = r * 2;
        flCursor.Margin = new Thickness(flX * FScale - r, flY * FScale - r, 0, 0);
        flCursor.Stroke = flPaperIdx == 2 ? Brushes.White : Brushes.Black;
        flCursor.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(FlipInks[flInk])) { Opacity = 0.5 };
    }
    void FlInk(int i) {
        flInk = ((i % FlipInks.Length) + FlipInks.Length) % FlipInks.Length;
        for (int k = 0; k < flInkRow.Children.Count; k++) ((Border)flInkRow.Children[k]).BorderBrush = k == flInk ? Ui.Brush(Ui.Teal) : new SolidColorBrush(Color.FromArgb(0x50, 255, 255, 255));
        FlPlaceCursor(); if (flFrames.Count > 0) FlShow(flIdx, false);
    }
    void FlSize(int d) { flSize = (flSize + d + FlipSizes.Length) % FlipSizes.Length; FlPlaceCursor(); FlShow(flIdx, false); Sound.Play(Sfx.Move); }

    // ---------- drawing ----------
    static uint FlArgb(string hex) { var c = (Color)ColorConverter.ConvertFromString(hex); return 0xFF000000 | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B; }
    void FlStartStroke() { FlStop(); flUndo.Add((uint[])flFrames[flIdx].Clone()); if (flUndo.Count > 50) flUndo.RemoveAt(0); flLastX = -1; flClearAsked = false; }
    // a dot of the pen at (x, y), and the line to it from the last one
    void FlDraw(double x, double y, bool erase) {
        var px = flFrames[flIdx]; uint c = erase ? 0 : FlArgb(FlipInks[flInk]);
        double r = FlipSizes[flSize] / 2.0;
        int steps = flLastX < 0 ? 1 : (int)Math.Ceiling(Math.Max(Math.Abs(x - flLastX), Math.Abs(y - flLastY)) * 2) + 1;
        for (int s = 0; s < steps; s++) {
            double t = steps == 1 ? 1 : s / (double)(steps - 1);
            double cx = flLastX < 0 ? x : flLastX + (x - flLastX) * t, cy = flLastY < 0 ? y : flLastY + (y - flLastY) * t;
            double rr = erase ? Math.Max(r, 2.5) : r;
            for (int yy = (int)Math.Floor(cy - rr); yy <= (int)Math.Ceiling(cy + rr); yy++)
                for (int xx = (int)Math.Floor(cx - rr); xx <= (int)Math.Ceiling(cx + rr); xx++) {
                    if (xx < 0 || yy < 0 || xx >= FW || yy >= FH) continue;
                    double dx = xx + 0.5 - cx, dy = yy + 0.5 - cy;
                    if (rr <= 0.5 ? (Math.Abs(dx) <= 0.5 && Math.Abs(dy) <= 0.5) : dx * dx + dy * dy <= rr * rr) px[yy * FW + xx] = c;
                }
        }
        flLastX = x; flLastY = y; flChanged = true; flLeaveAsked = false;
        flBmp.WritePixels(new Int32Rect(0, 0, FW, FH), px, FW * 4, 0);
    }
    void FlMouseAt(Point p, bool erase) { flX = Math.Max(0, Math.Min(FW - 1, p.X / FScale)); flY = Math.Max(0, Math.Min(FH - 1, p.Y / FScale)); FlPlaceCursor(); FlDraw(flX, flY, erase); }
    void FlUndoStep() {
        if (flUndo.Count == 0) { Sound.Play(Sfx.Bump); return; }
        flFrames[flIdx] = flUndo[flUndo.Count - 1]; flUndo.RemoveAt(flUndo.Count - 1); flChanged = true; FlShow(flIdx, true); Sound.Play(Sfx.Back);
    }
    void FlClear() {
        if (!flClearAsked) { flClearAsked = true; flMsg.Text = "Clear this frame? Press it again."; Sound.Play(Sfx.Bump); return; }
        flClearAsked = false; FlStartStroke(); flFrames[flIdx] = new uint[FW * FH]; flChanged = true; FlShow(flIdx, true); flMsg.Text = "Cleared. Undo brings it back."; Sound.Play(Sfx.Select);
    }

    // ---------- frames ----------
    void FlNewFrame() { FlStop(); if (flFrames.Count >= 300) { Sound.Play(Sfx.Bump); return; } flFrames.Insert(flIdx + 1, new uint[FW * FH]); flUndo.Clear(); flChanged = true; FlShow(flIdx + 1, true); Sound.Play(Sfx.Select); }
    void FlCopyFrame() { FlStop(); if (flFrames.Count >= 300) { Sound.Play(Sfx.Bump); return; } flFrames.Insert(flIdx + 1, (uint[])flFrames[flIdx].Clone()); flUndo.Clear(); flChanged = true; FlShow(flIdx + 1, true); Sound.Play(Sfx.Select); }
    void FlDeleteFrame() {
        FlStop();
        if (flFrames.Count == 1) { flFrames[0] = new uint[FW * FH]; } else flFrames.RemoveAt(flIdx);
        flUndo.Clear(); flChanged = true; FlShow(Math.Min(flIdx, flFrames.Count - 1), true); Sound.Play(Sfx.Back);
    }
    void FlStep(int d) {
        FlStop();
        int j = flIdx + d;
        if (j < 0 || j >= flFrames.Count) { Sound.Play(Sfx.Bump); return; }
        flUndo.Clear(); FlShow(j, true); Sound.Play(Sfx.Move);
    }
    void FlToggleOnion() { flOnionOn = !flOnionOn; FlShow(flIdx, true); Sound.Play(Sfx.Select); }
    void FlNextPaper() { flPaperIdx = (flPaperIdx + 1) % FlipPapers.Length; flPaper.Fill = Ui.Brush(FlipPapers[flPaperIdx]); flChanged = true; FlPlaceCursor(); FlShow(flIdx, true); Sound.Play(Sfx.Select); }

    // ---------- playing ----------
    void FlTogglePlay() { if (flPlaying) FlStop(); else FlPlay(); }
    void FlPlay() {
        if (flFrames.Count < 2) { flMsg.Text = "Add a frame or two first (d-pad up, or New frame)."; Sound.Play(Sfx.Bump); return; }
        flPlaying = true; flPlayAt = flIdx; flOnion.Visibility = Visibility.Collapsed;
        flPlayTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / FlipSpeeds[flSpeed]); flPlayTimer.Start(); FlShow(flIdx, false);
    }
    void FlStop() { if (!flPlaying) return; flPlaying = false; flPlayTimer.Stop(); FlShow(flPlayAt, true); }
    void FlSpeed(int d) { flSpeed = Math.Max(0, Math.Min(FlipSpeeds.Length - 1, flSpeed + d)); if (flPlaying) flPlayTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / FlipSpeeds[flSpeed]); flChanged = true; FlShow(flPlaying ? flPlayAt : flIdx, false); Sound.Play(Sfx.Move); }

    // ---------- saving and opening ----------
    // <name>.flip.png: the frames side by side on one sheet, its paper and speed written in the picture's text
    // <name>.gif: the animation, to share
    void FlSave() {
        FlStop();
        try {
            Directory.CreateDirectory(FlipDir);
            if (flFile == null) flFile = Path.Combine(FlipDir, "Flip " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss") + ".flip.png");
            int n = flFrames.Count;
            var sheet = new uint[FW * n * FH];
            for (int i = 0; i < n; i++) for (int y = 0; y < FH; y++) Array.Copy(flFrames[i], y * FW, sheet, y * FW * n + i * FW, FW);
            var bmp = BitmapSource.Create(FW * n, FH, 96, 96, PixelFormats.Bgra32, null, sheet, FW * n * 4);
            var meta = new BitmapMetadata("png");
            meta.SetQuery("/tEXt/{str=Description}", "flipbook;frames=" + n + ";speed=" + flSpeed + ";paper=" + flPaperIdx);
            using (var fs = File.Create(flFile)) { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp, null, meta, null)); enc.Save(fs); }
            FlWriteGif(flFile.Replace(".flip.png", ".gif"));
            flChanged = false; flLeaveAsked = false;
            flTitle.Text = Path.GetFileName(flFile).Replace(".flip.png", "");
            flMsg.Text = "Saved, with a GIF beside it to share: Pictures\\Recomp Launcher\\Flip book.";
            Sound.Play(Sfx.Select);
        } catch (Exception e) { flMsg.Text = "Couldn't save: " + e.Message; Sound.Play(Sfx.Bump); }
    }
    bool FlLoad(string file) {
        try {
            var dec = new PngBitmapDecoder(new Uri(file), BitmapCreateOptions.IgnoreImageCache | BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var fr = dec.Frames[0]; string desc = null;
            try { var m = fr.Metadata as BitmapMetadata; if (m != null) desc = m.GetQuery("/tEXt/{str=Description}") as string; } catch { }
            int n = Math.Max(1, fr.PixelWidth / FW);
            if (desc != null) foreach (var kv in desc.Split(';').Select(x => x.Split('=')).Where(x => x.Length == 2)) {
                int v; if (!int.TryParse(kv[1], out v)) continue;
                if (kv[0] == "speed") flSpeed = Math.Max(0, Math.Min(FlipSpeeds.Length - 1, v));
                if (kv[0] == "paper") flPaperIdx = Math.Max(0, Math.Min(FlipPapers.Length - 1, v));
            }
            var conv = new FormatConvertedBitmap(fr, PixelFormats.Bgra32, null, 0);
            var all = new uint[conv.PixelWidth * conv.PixelHeight]; conv.CopyPixels(all, conv.PixelWidth * 4, 0);
            int w = conv.PixelWidth;
            for (int i = 0; i < n; i++) {
                var px = new uint[FW * FH];
                for (int y = 0; y < Math.Min(FH, conv.PixelHeight); y++) Array.Copy(all, y * w + i * FW, px, y * FW, FW);
                flFrames.Add(px);
            }
            return true;
        } catch { flFrames.Clear(); return false; }
    }
    // an animated GIF that loops, each frame on the paper, at the flip book's speed. WPF's own GIF writer makes each
    // frame (in the flip book's few colours, so they stay exact); the frames are put together here with the delay
    // and the loop it leaves out.
    void FlWriteGif(string file) {
        uint paper = FlArgb(FlipPapers[flPaperIdx]);
        var cols = new List<Color> { (Color)ColorConverter.ConvertFromString(FlipPapers[flPaperIdx]) };
        foreach (var s in FlipInks) { var c = (Color)ColorConverter.ConvertFromString(s); if (!cols.Contains(c)) cols.Add(c); }
        var pal = new BitmapPalette(cols);
        int delay = (int)Math.Round(100.0 / FlipSpeeds[flSpeed]);   // hundredths of a second
        using (var outp = new BinaryWriter(File.Create(file))) {
            outp.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));
            outp.Write((ushort)FW); outp.Write((ushort)FH); outp.Write((byte)0); outp.Write((byte)0); outp.Write((byte)0);   // no colour table of its own: each frame has one
            outp.Write(new byte[] { 0x21, 0xFF, 0x0B }); outp.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0")); outp.Write(new byte[] { 3, 1, 0, 0, 0 });   // loop forever
            foreach (var f in flFrames) {
                var px = new uint[FW * FH];
                for (int i = 0; i < px.Length; i++) px[i] = (f[i] >> 24) == 0 ? paper : f[i];
                var src = BitmapSource.Create(FW, FH, 96, 96, PixelFormats.Bgra32, null, px, FW * 4);
                var idx = new FormatConvertedBitmap(src, PixelFormats.Indexed8, pal, 0);
                byte[] one; using (var ms = new MemoryStream()) { var enc = new GifBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(idx)); enc.Save(ms); one = ms.ToArray(); }
                // the single frame's parts: its colour table (global there), its image
                int flags = one[10], p = 13; byte[] table = null;
                if ((flags & 0x80) != 0) { int size = 3 * (1 << ((flags & 7) + 1)); table = new byte[size]; Array.Copy(one, 13, table, 0, size); p += size; }
                while (p < one.Length && one[p] == 0x21) { p += 2; while (one[p] != 0) p += one[p] + 1; p++; }   // extensions it wrote: skipped
                if (p >= one.Length || one[p] != 0x2C) continue;
                outp.Write(new byte[] { 0x21, 0xF9, 4, 0 }); outp.Write((ushort)delay); outp.Write(new byte[] { 0, 0 });   // how long this frame shows
                byte packed = one[p + 9];
                if (table != null && (packed & 0x80) == 0) packed = (byte)(0x80 | (flags & 7));   // the colour table becomes this frame's own
                outp.Write(one, p, 9); outp.Write(packed);
                int q = p + 10;
                if ((one[p + 9] & 0x80) != 0) { int size = 3 * (1 << ((one[p + 9] & 7) + 1)); outp.Write(one, q, size); q += size; }
                else if (table != null) outp.Write(table);
                // the picture's data: the code size, then blocks up to the empty one
                int start = q; q++; while (one[q] != 0) q += one[q] + 1; q++;
                outp.Write(one, start, q - start);
            }
            outp.Write((byte)0x3B);
        }
    }

    // test: a ball rolling across six frames, with a line under it, saved (the -PadScript test hook)
    void FlDemo() {
        FlOpen(null); flSize = 2;
        for (int i = 0; i < 6; i++) {
            if (i > 0) FlNewFrame();
            FlStartStroke(); FlInk(0);
            for (double x = 10; x < 246; x += 1) FlDraw(x, 170, false);
            flLastX = -1; FlInk(1); double cx = 40 + i * 36, cy = 150 - Math.Abs(Math.Sin(i * 0.9)) * 60;
            for (double a = 0; a <= Math.PI * 2 + 0.1; a += 0.05) FlDraw(cx + Math.Cos(a) * 16, cy + Math.Sin(a) * 16, false);
            flLastX = -1;
        }
        FlSave();
        FlShow(2, true);
    }

    // ---------- controller (every tick: the stick moves the pen smoothly) and keyboard ----------
    void FlipTick(Pad.State st, int b) {
        int pressed = b & ~flPrevBits; flPrevBits = b;
        bool draw = (b & Pad.A) != 0, erase = (b & Pad.X) != 0;
        if ((pressed & (Pad.A | Pad.X)) != 0) { FlStartStroke(); FlDraw(flX, flY, erase && !draw); }
        if (!draw && !erase) flLastX = -1;
        // the stick: faster the further it's pushed (a small dead zone)
        double lx = st.LX / 32768.0, ly = -st.LY / 32768.0, mag = Math.Sqrt(lx * lx + ly * ly);
        if (mag > 0.18) {
            double k = (mag - 0.18) / 0.82; k = k * k * 3.2 + 0.25;
            flX = Math.Max(0, Math.Min(FW - 1, flX + lx / mag * k)); flY = Math.Max(0, Math.Min(FH - 1, flY + ly / mag * k));
            FlPlaceCursor();
            if (draw || erase) FlDraw(flX, flY, erase && !draw);
        }
        if (pressed == 0) return;
        padUsed = true;
        if ((pressed & Pad.B) != 0) { FlLeave(); return; }
        if ((pressed & Pad.Start) != 0) { FlSave(); return; }
        if ((pressed & Pad.Back) != 0) FlClear();
        if ((pressed & Pad.LB) != 0) { FlInk(flInk - 1); Sound.Play(Sfx.Move); }
        if ((pressed & Pad.RB) != 0) { FlInk(flInk + 1); Sound.Play(Sfx.Move); }
        if ((pressed & Pad.Y) != 0) FlSize(1);
        if ((pressed & Pad.LT) != 0) FlUndoStep();
        if ((pressed & Pad.RT) != 0) FlTogglePlay();
        if ((pressed & Pad.Left) != 0) FlStep(-1);
        if ((pressed & Pad.Right) != 0) FlStep(1);
        if ((pressed & Pad.Up) != 0) FlNewFrame();
        if ((pressed & Pad.Down) != 0) FlCopyFrame();
    }
    bool FlipKey(Key k) {
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl && k == Key.Z) { FlUndoStep(); return true; }
        if (ctrl && k == Key.S) { FlSave(); return true; }
        switch (k) {
            case Key.Escape: FlLeave(); return true;
            case Key.Left: FlStep(-1); return true;
            case Key.Right: FlStep(1); return true;
            case Key.Up: FlNewFrame(); return true;
            case Key.Down: FlCopyFrame(); return true;
            case Key.Space: FlTogglePlay(); return true;
            case Key.Q: FlInk(flInk - 1); return true;
            case Key.E: FlInk(flInk + 1); return true;
            case Key.P: FlSize(1); return true;
            case Key.O: FlToggleOnion(); return true;
            case Key.Delete: FlClear(); return true;
            default: return false;
        }
    }
}
}
