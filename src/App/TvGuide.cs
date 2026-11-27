// TV & Movies: the TV Guide. On top: the picked channel playing small (its own mpv), what's on it, and a few facts;
// under it every channel of the profile, with what's on in a timeline that starts half an hour ago. The free profile's
// channels use open-epg.com's Dutch listings; an IPTV profile uses its provider's own guide (TvFree.Guide).
// Controller: up / down channels, left / right programmes, A watches the channel full screen, B back to the bar.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RecompLauncher {

class TvGuideRow { public TvItem Ch; public List<TvFree.Prog> Progs; public Grid Root; public Canvas Cells; public Border Head; public readonly List<Border> Boxes = new List<Border>(); }

partial class Shell {
    Grid tvGuide, tvGuideTop; StackPanel tvGuideList; ScrollViewer tvGuideScroll; Canvas tvGuideHead, tvGuideNow; TextBlock tvGuideToday;
    TextBlock tvGuideChan, tvGuideProg, tvGuideDesc, tvGuideWhen, tvGuideStatus; Border tvGuideBar, tvGuideBarFill; StackPanel tvGuideCards;
    System.Windows.Forms.Integration.WindowsFormsHost tvGuideHost; System.Windows.Forms.Panel tvGuideVideo; IntPtr tvGuideMpv;
    readonly List<TvGuideRow> tvGuideRows = new List<TvGuideRow>(); List<TvItem> tvGuideChans = new List<TvItem>(); Dictionary<string, List<TvFree.Prog>> tvGuideData;
    readonly TranslateTransform tvGuideShift = new TranslateTransform();
    int tvGuideRow, tvGuideCol, tvGuideGen; DateTime tvGuideT0, tvGuideAt; DispatcherTimer tvGuidePreviewTimer; TvItem tvGuidePlaying;
    const double GuidePx = 5.2, GuideRowH = 62, GuideChanW = 210; const int GuideHours = 14;
    bool TvGuideOpen { get { return tvGuide != null && tvGuide.Visibility == Visibility.Visible; } }

    void BuildTvGuide() {
        tvGuide = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 96, 0, 0), Background = TvTheme.Brush(TvBg) };
        Grid.SetColumnSpan(tvGuide, 2);
        tvGuide.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        tvGuide.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        tvGuide.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        // the top: the picture, what's on, the facts
        tvGuideTop = new Grid { Height = 246, Margin = new Thickness(8, 0, 8, 10) };
        tvGuideTop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(437) });
        tvGuideTop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tvGuideTop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        tvGuideVideo = TvNewGuidePanel();
        tvGuideHost = new System.Windows.Forms.Integration.WindowsFormsHost { Child = tvGuideVideo, Background = Brushes.Black };
        tvGuideTop.Children.Add(new Border { Background = Brushes.Black, CornerRadius = new CornerRadius(8), Child = tvGuideHost });
        var info = new Border { Background = TvTheme.Brush("#141416"), BorderBrush = TvTheme.Brush("#232326"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Margin = new Thickness(10, 0, 10, 0), Padding = new Thickness(20, 16, 20, 18) };
        Grid.SetColumn(info, 1);
        var ig = new Grid(); ig.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); ig.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); ig.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var it = new StackPanel();
        var live = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        live.Children.Add(new Border { Background = TvTheme.Brush(TvAccent), CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 2, 9, 3), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "●  LIVE", FontSize = 12, FontFamily = Ui.Semi, Foreground = Brushes.White } });
        tvGuideChan = new TextBlock { FontSize = 25, FontFamily = TvFont, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        live.Children.Add(tvGuideChan);
        it.Children.Add(live);
        tvGuideProg = new TextBlock { FontSize = 20, FontFamily = Ui.Semi, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) };
        it.Children.Add(tvGuideProg);
        ig.Children.Add(it);
        tvGuideDesc = new TextBlock { FontSize = 14.5, Foreground = TvTheme.Brush("#C7FFFFFF"), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.WordEllipsis, LineHeight = 21, Margin = new Thickness(0, 8, 0, 8) };
        Grid.SetRow(tvGuideDesc, 1); ig.Children.Add(tvGuideDesc);
        var bottom = new StackPanel(); Grid.SetRow(bottom, 2);
        tvGuideWhen = new TextBlock { FontSize = 17, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#E6FFFFFF"), Margin = new Thickness(0, 0, 0, 8) };
        bottom.Children.Add(tvGuideWhen);
        tvGuideBarFill = new Border { Background = TvTheme.Brush(TvAccent), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        tvGuideBar = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = TvTheme.Brush("#33FFFFFF"), Child = tvGuideBarFill };
        bottom.Children.Add(tvGuideBar);
        ig.Children.Add(bottom);
        info.Child = ig;
        tvGuideTop.Children.Add(info);
        tvGuideCards = new StackPanel(); Grid.SetColumn(tvGuideCards, 2);
        tvGuideTop.Children.Add(tvGuideCards);
        tvGuide.Children.Add(tvGuideTop);
        // the times along the top
        var head = new Grid { Height = 34 }; Grid.SetRow(head, 1);
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GuideChanW) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.Children.Add(tvGuideToday = new TextBlock { Text = "TODAY", TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 15, FontFamily = Ui.Semi, Foreground = TvTheme.Brush(TvAccent), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(56, 0, 0, 0) });
        tvGuideHead = new Canvas { RenderTransform = tvGuideShift };
        var hc = new Border { Child = tvGuideHead, ClipToBounds = true }; Grid.SetColumn(hc, 1); head.Children.Add(hc);
        tvGuide.Children.Add(head);
        // the channels
        var gridArea = new Grid(); Grid.SetRow(gridArea, 2);
        tvGuideList = new StackPanel { Margin = new Thickness(8, 0, 0, 30) };
        tvGuideScroll = new ScrollViewer { Content = tvGuideList, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false };
        tvGuideScroll.ScrollChanged += (s, e) => { if (tvGuideScroll.VerticalOffset + tvGuideScroll.ViewportHeight > tvGuideScroll.ExtentHeight - 600) TvGuideBuildMore(30); };
        gridArea.Children.Add(tvGuideScroll);
        // the line at "now"
        tvGuideNow = new Canvas { IsHitTestVisible = false, RenderTransform = tvGuideShift };
        var nowBox = new Border { Child = tvGuideNow, ClipToBounds = true, Margin = new Thickness(GuideChanW, 0, 0, 0), IsHitTestVisible = false };
        gridArea.Children.Add(nowBox);
        tvGuideStatus = new TextBlock { FontSize = 16, Foreground = TvTheme.Brush("#99FFFFFF"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 40, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 700, TextAlignment = TextAlignment.Center };
        gridArea.Children.Add(tvGuideStatus);
        tvGuide.Children.Add(gridArea);
        tvLayer.Children.Insert(tvLayer.Children.IndexOf(tvBar), tvGuide);
        tvGuidePreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        tvGuidePreviewTimer.Tick += (s, e) => { tvGuidePreviewTimer.Stop(); TvGuidePreview(); };
    }

    // ---------- opening ----------
    void TvOpenGuide() {
        if (tvGuide == null) BuildTvGuide();
        tvContent.Visibility = Visibility.Collapsed; tvGuide.Visibility = Visibility.Visible; tvGuideHost.Visibility = Visibility.Visible;
        tvZone = "guide"; int gen = ++tvGuideGen;
        tvGuideList.Children.Clear(); tvGuideRows.Clear(); tvGuideHead.Children.Clear(); tvGuideNow.Children.Clear();
        tvGuideStatus.Text = "Loading the channels and what's on..."; tvGuideStatus.Visibility = Visibility.Visible;
        tvGuideChan.Text = ""; tvGuideProg.Text = ""; tvGuideDesc.Text = ""; tvGuideWhen.Text = ""; tvGuideBarFill.Width = 0; tvGuideCards.Children.Clear();
        var acc = tvAcc;
        // only the category open in TV Live, when there was one (for this profile)
        var fromList = tvGuideFromAcc == tvAcc ? tvGuideFrom : null;
        tvGuideToday.Text = fromList != null ? tvGuideFromName : "TODAY";
        // the small picture needs the player; it's fetched once (Mpv.cs)
        if (!Mpv.Ready) new Thread(() => { try { Mpv.Download(t => { }); W.Dispatcher.BeginInvoke(new Action(TvGuidePreview)); } catch { } }) { IsBackground = true }.Start();
        new Thread(() => {
            List<TvItem> chans = null; Dictionary<string, List<TvFree.Prog>> data = null; string err = null;
            try {
                if (acc != null) { if (!acc.Loaded) TvSource.LoadAccount(acc); chans = acc.Live.ToList(); }
                else chans = TvSources.Values.SelectMany(x => x).GroupBy(x => x.Url).Select(g => g.First()).ToList();
            } catch (Exception e) { err = e.Message; }
            if (fromList != null && chans != null) chans = fromList.ToList();
            try { data = TvFree.Guide(acc); } catch { data = new Dictionary<string, List<TvFree.Prog>>(); }
            W.Dispatcher.BeginInvoke(new Action(() => {
                if (gen != tvGuideGen || !TvGuideOpen) return;
                if (chans == null || chans.Count == 0) { tvGuideStatus.Text = err != null ? "Couldn't load the channels: " + err : "No channels here."; return; }
                tvGuideStatus.Visibility = Visibility.Collapsed;
                tvGuideData = data; tvGuideChans = chans;
                // channels with listings first, in their own order
                tvGuideChans = TvFavsFirst(TvGuideOrder(chans, data));   // My list's channels first (TvExtras.cs)
                TvGuideFill();
            }));
        }) { IsBackground = true }.Start();
        SetHints();
    }
    void TvCloseGuide() {
        if (tvGuide == null) return;
        TvGuideStopPreview();
        tvGuide.Visibility = Visibility.Collapsed; tvContent.Visibility = Visibility.Visible;
        tvGuideGen++;
    }
    void TvGuideFill() {
        var now = DateTime.Now;
        tvGuideT0 = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute >= 30 ? 30 : 0, 0).AddMinutes(-30);
        tvGuideShift.X = 0; tvGuideAt = now;
        // the times
        for (int i = 0; i <= GuideHours * 2; i++) {
            var t = tvGuideT0.AddMinutes(30 * i);
            var tb = new TextBlock { Text = t.ToString("HH:mm"), FontSize = 15, FontFamily = Ui.Semi, Foreground = TvTheme.Brush(t.Minute == 0 ? "#FFFFFF" : "#8E8E93") };
            Canvas.SetLeft(tb, i * 30 * GuidePx + 6); Canvas.SetTop(tb, 7); tvGuideHead.Children.Add(tb);
        }
        double nx = (now - tvGuideT0).TotalMinutes * GuidePx;
        var mark = new Rectangle { Width = 2, Height = 4000, Fill = TvTheme.Brush(TvAccent), Opacity = 0.9 }; Canvas.SetLeft(mark, nx); tvGuideNow.Children.Add(mark);
        var dot = new Ellipse { Width = 10, Height = 10, Fill = TvTheme.Brush(TvAccent) }; Canvas.SetLeft(dot, nx - 4); Canvas.SetTop(dot, 24); tvGuideHead.Children.Add(dot);
        tvGuideRow = 0; tvGuideCol = 0;
        TvGuideBuildMore(40);
        TvGuideFocus(0, null, false);
    }
    void TvGuideBuildMore(int n) {
        if (tvGuideChans == null) return;
        int upto = Math.Min(tvGuideChans.Count, tvGuideRows.Count + n);
        for (int i = tvGuideRows.Count; i < upto; i++) { var r = TvGuideMakeRow(tvGuideChans[i], i); tvGuideRows.Add(r); tvGuideList.Children.Add(r.Root); }
    }
    TvGuideRow TvGuideMakeRow(TvItem ch, int index) {
        var r = new TvGuideRow { Ch = ch };
        var end = tvGuideT0.AddHours(GuideHours);
        var list = tvGuideData != null ? TvFree.For(tvGuideData, ch) : null;
        r.Progs = list != null ? list.Where(p => p.Stop > tvGuideT0 && p.Start < end).ToList() : new List<TvFree.Prog>();
        if (r.Progs.Count == 0) r.Progs.Add(new TvFree.Prog { Start = tvGuideT0, Stop = end, Title = null });
        r.Root = new Grid { Height = GuideRowH, Margin = new Thickness(0, 0, 0, 4) };
        r.Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GuideChanW - 8) });
        r.Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // the channel
        var cg = new Grid();
        cg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        cg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var logo = new Image { Width = 40, Height = 34, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        if (!string.IsNullOrEmpty(ch.Image)) TvLoadImage(logo, ch.Image, 90, null);
        cg.Children.Add(logo);
        var nm = new TextBlock { Text = ch.Title, FontSize = 14, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#D9FFFFFF"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, MaxHeight = 40 };
        Grid.SetColumn(nm, 1); cg.Children.Add(nm);
        r.Head = new Border { Child = cg, Background = TvTheme.Brush("#141416"), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 0, 10, 0), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand };
        int ri = index;
        r.Head.MouseLeftButtonUp += (s, e) => { if (tvGuideRow == ri) TvGuideWatch(); else TvGuideFocus(ri, DateTime.Now, true); };
        r.Root.Children.Add(r.Head);
        // what's on
        r.Cells = new Canvas { RenderTransform = tvGuideShift, Height = GuideRowH };
        var now = DateTime.Now;
        for (int i = 0; i < r.Progs.Count; i++) {
            var p = r.Progs[i]; int pi = i;
            DateTime s0 = p.Start < tvGuideT0 ? tvGuideT0 : p.Start, s1 = p.Stop > end ? end : p.Stop;
            double x = (s0 - tvGuideT0).TotalMinutes * GuidePx, w = Math.Max(10, (s1 - s0).TotalMinutes * GuidePx - 4);
            bool onNow = p.Start <= now && p.Stop > now && p.Title != null;
            var g = new Grid { ClipToBounds = true };
            if (onNow) {
                double frac = Math.Max(0, Math.Min(1, (now - s0).TotalMinutes / Math.Max(1, (s1 - s0).TotalMinutes)));
                g.Children.Add(new Border { Background = TvTheme.Brush("#5A1710"), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(0, (w - 4) * frac) });
            }
            var txt = new StackPanel { Margin = new Thickness(10, 7, 8, 0) };
            if (p.Title != null) txt.Children.Add(new TextBlock { Text = p.Start.ToString("HH:mm"), FontSize = 12.5, Foreground = TvTheme.Brush(onNow ? "#D9FFFFFF" : "#8E8E93") });
            txt.Children.Add(new TextBlock { Text = p.Title ?? "No guide information for this channel", FontSize = 15, FontFamily = Ui.Semi, Foreground = TvTheme.Brush(p.Title == null ? "#8E8E93" : "#FFFFFF"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
            g.Children.Add(txt);
            // replayable (TV Archive), and a reminder set
            if (TvCanReplay(ch, p)) g.Children.Add(new TextBlock { Text = "\uE81C", FontFamily = Ui.Mdl, FontSize = 12, Foreground = TvTheme.Brush("#B3FFFFFF"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 9, 0), ToolTip = "Can be replayed" });
            if (p.Title != null && TvMine.HasReminder(ch, p.Start)) g.Children.Add(new TextBlock { Text = "\uEA8F", FontFamily = Ui.Mdl, FontSize = 12, Foreground = TvTheme.Brush(TvAccent), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 9, 0), ToolTip = "Reminder set" });
            if (p.Title != null && TvScheduled(ch, p.Start)) g.Children.Add(new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = TvTheme.Brush("#E5383B"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 10, 10), ToolTip = "Will be recorded" });
            var b = new Border { Width = w, Height = GuideRowH, CornerRadius = new CornerRadius(6), Background = TvTheme.Brush(onNow ? "#3A0E0A" : "#1A1A1C"), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Child = g, Cursor = Cursors.Hand };
            Canvas.SetLeft(b, x);
            b.MouseLeftButtonUp += (s, e) => { if (tvGuideRow == ri && tvGuideCol == pi) TvGuideAct(ri, pi); else { tvGuideRow = ri; tvGuideCol = pi; TvGuideFocus(ri, p.Start < tvGuideT0 ? tvGuideT0 : p.Start, true, pi); } };
            r.Boxes.Add(b); r.Cells.Children.Add(b);
        }
        var clip = new Border { Child = r.Cells, ClipToBounds = true, Margin = new Thickness(4, 0, 0, 0) }; Grid.SetColumn(clip, 1);
        r.Root.Children.Add(clip);
        return r;
    }

    // ---------- moving ----------
    // a channel (and in it, the programme on at "at", or a given one)
    void TvGuideFocus(int row, DateTime? at, bool sound, int col = -1) {
        if (tvGuideRows.Count == 0) return;
        row = Math.Max(0, Math.Min(tvGuideChans.Count - 1, row));
        if (row >= tvGuideRows.Count - 10) TvGuideBuildMore(30);
        row = Math.Min(row, tvGuideRows.Count - 1);
        bool chanChanged = row != tvGuideRow || tvGuidePlaying == null;
        var r = tvGuideRows[row];
        if (col < 0) { var t = at ?? tvGuideAt; col = r.Progs.FindIndex(p => p.Start <= t && p.Stop > t); if (col < 0) col = t < r.Progs[0].Start ? 0 : r.Progs.Count - 1; }
        tvGuideRow = row; tvGuideCol = Math.Max(0, Math.Min(r.Progs.Count - 1, col));
        if (sound) Sound.Play(Sfx.Move);
        TvGuideMark();
        if (chanChanged) { tvGuidePreviewTimer.Stop(); if (TvMine.PreviewMs > 0) { tvGuidePreviewTimer.Interval = TimeSpan.FromMilliseconds(TvMine.PreviewMs); tvGuidePreviewTimer.Start(); } }
    }
    void TvGuideMark() {
        for (int i = 0; i < tvGuideRows.Count; i++) {
            var r = tvGuideRows[i]; bool cur = i == tvGuideRow;
            r.Head.BorderBrush = cur && tvZone == "guide" ? Brushes.White : Brushes.Transparent;
            ((TextBlock)((Grid)r.Head.Child).Children[1]).Foreground = cur ? Brushes.White : TvTheme.Brush("#D9FFFFFF");
            for (int k = 0; k < r.Boxes.Count; k++) r.Boxes[k].BorderBrush = cur && k == tvGuideCol && tvZone == "guide" ? Brushes.White : Brushes.Transparent;
        }
        if (tvGuideRow >= tvGuideRows.Count) return;
        var row = tvGuideRows[tvGuideRow]; var p = row.Progs[tvGuideCol];
        // in view: the channel up and down, the programme left and right
        W.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
            try {
                double y = row.Root.TransformToAncestor(tvGuideList).Transform(new Point(0, 0)).Y, vh = tvGuideScroll.ViewportHeight;
                if (y < tvGuideScroll.VerticalOffset + GuideRowH) tvGuideScroll.ScrollToVerticalOffset(Math.Max(0, y - GuideRowH));
                else if (y + GuideRowH * 2 > tvGuideScroll.VerticalOffset + vh) tvGuideScroll.ScrollToVerticalOffset(y + GuideRowH * 2 - vh);
                double vw = ((FrameworkElement)row.Cells.Parent).ActualWidth;
                double x = Canvas.GetLeft(row.Boxes[tvGuideCol]), w = row.Boxes[tvGuideCol].Width, sx = -tvGuideShift.X;
                if (x < sx) sx = Math.Max(0, x - 30);
                else if (x + Math.Min(w, vw * 0.6) > sx + vw) sx = x + Math.Min(w, vw * 0.6) - vw + 30;
                tvGuideShift.X = -Math.Max(0, sx);
            } catch { }
        }));
        // what's on
        var now = DateTime.Now;
        tvGuideChan.Text = row.Ch.Title;
        tvGuideProg.Text = p.Title ?? "No guide information";
        tvGuideDesc.Text = p.Desc ?? (p.Title == null ? "This channel has no listings in the guide. Press A to watch it." : "");
        if (p.Title != null) {
            tvGuideWhen.Text = p.Start.ToString("HH:mm") + " – " + p.Stop.ToString("HH:mm") + "   (" + (int)(p.Stop - p.Start).TotalMinutes + " min)";
            double frac = Math.Max(0, Math.Min(1, (now - p.Start).TotalMinutes / Math.Max(1, (p.Stop - p.Start).TotalMinutes)));
            tvGuideBar.Visibility = Visibility.Visible;
            tvGuideBar.UpdateLayout(); tvGuideBarFill.Width = Math.Max(0, tvGuideBar.ActualWidth * frac);
        } else { tvGuideWhen.Text = ""; tvGuideBar.Visibility = Visibility.Collapsed; }
        // the facts
        tvGuideCards.Children.Clear();
        var next = tvGuideCol + 1 < row.Progs.Count ? row.Progs[tvGuideCol + 1] : null;
        TvGuideCard("", "Time", p.Title != null ? p.Start.ToString("HH:mm") + " – " + p.Stop.ToString("HH:mm") : "–");
        TvGuideCard("", "Duration", p.Title != null ? (int)(p.Stop - p.Start).TotalMinutes + " min" : "–");
        TvGuideCard("", "Up next", next != null && next.Title != null ? next.Start.ToString("HH:mm") + "  " + next.Title : "–");
        TvGuideCard("", "Channel", (tvGuideRow + 1) + " of " + tvGuideChans.Count);
        SetHints();
    }
    void TvGuideCard(string glyph, string label, string value) {
        var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.Mdl, FontSize = 17, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(t, 1);
        t.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = TvTheme.Brush("#B3FFFFFF") });
        t.Children.Add(new TextBlock { Text = value, FontSize = 12.5, FontFamily = Ui.Semi, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
        g.Children.Add(t);
        tvGuideCards.Children.Add(new Border { Height = 56, Margin = new Thickness(0, 0, 0, 7), Background = TvTheme.Brush("#141416"), BorderBrush = TvTheme.Brush("#232326"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 0, 8, 0), Child = g });
    }
    void TvGuidePad(int bit) {
        if (tvGuideRows.Count == 0) { if (bit == Pad.B || bit == Pad.Up) TvToRail(); return; }
        var r = tvGuideRows[tvGuideRow];
        if (bit == Pad.Up) { if (tvGuideRow > 0) TvGuideFocus(tvGuideRow - 1, null, true); else TvToRail(); }
        else if (bit == Pad.Down) { if (tvGuideRow < tvGuideChans.Count - 1) TvGuideFocus(tvGuideRow + 1, null, true); else Sound.Play(Sfx.Bump); }
        else if (bit == Pad.Left || bit == Pad.Right) {
            int c = tvGuideCol + (bit == Pad.Left ? -1 : 1);
            if (c < 0 || c >= r.Progs.Count) { Sound.Play(Sfx.Bump); return; }
            var p = r.Progs[c]; tvGuideAt = p.Start < tvGuideT0 ? tvGuideT0 : p.Start; if (p.Start <= DateTime.Now && p.Stop > DateTime.Now) tvGuideAt = DateTime.Now;
            TvGuideFocus(tvGuideRow, null, true, c);
        }
        else if (bit == Pad.A) TvGuideAct(tvGuideRow, tvGuideCol);
        else if (bit == Pad.X) TvItemActions(r.Ch);
        else if (bit == Pad.Y) { var ch = r.Ch; TvStore.ToggleFav(ch); Sound.Play(Sfx.Select); SetStep(TvStore.IsFav(ch) ? ch.Title + " is in My list." : ch.Title + " is out of My list.", Ui.Teal); }
        else if (bit == Pad.B) TvToRail();
    }

    // ---------- the picture ----------
    void TvGuidePreview() {
        if (!TvGuideOpen || TvPlaying || tvGuideRow >= tvGuideRows.Count) return;
        var ch = tvGuideRows[tvGuideRow].Ch; if (ch == tvGuidePlaying) return;
        if (!Mpv.Ready) return;   // the player comes with the first thing watched full screen
        try {
            if (tvGuideMpv != IntPtr.Zero && Mpv.Stuck(tvGuideMpv, 3)) {
                // stuck (Mpv.Stuck): left behind, hidden, for a fresh one (as TvFreshPlayer)
                var old = tvGuideMpv; tvGuideMpv = IntPtr.Zero; Mpv.Destroy(old);
                tvGuideVideo.Visible = false; tvStuckPanels.Add(tvGuideVideo);
                tvGuideVideo = TvNewGuidePanel(); tvGuideHost.Child = tvGuideVideo;
                Mpv.Note("the guide preview got stuck and was replaced");
            }
            if (tvGuideMpv == IntPtr.Zero) tvGuideMpv = Mpv.Create(tvGuideVideo.Handle);
            Mpv.Set(tvGuideMpv, "start", "none");
            Mpv.Command(tvGuideMpv, "loadfile", ch.Url, "replace");
            Mpv.Set(tvGuideMpv, "pause", "no");
            tvGuidePlaying = ch;
        } catch { }
    }
    System.Windows.Forms.Panel TvNewGuidePanel() {
        var v = new System.Windows.Forms.Panel { BackColor = System.Drawing.Color.Black, Dock = System.Windows.Forms.DockStyle.Fill };
        v.MouseDoubleClick += (s, e) => TvGuideWatch();
        return v;
    }
    void TvGuideStopPreview() {
        tvGuidePreviewTimer.Stop();
        if (tvGuideMpv != IntPtr.Zero) Mpv.Command(tvGuideMpv, "stop");
        tvGuidePlaying = null;
    }
    void TvGuideWatch() {
        if (tvGuideRow >= tvGuideRows.Count) return;
        var ch = tvGuideRows[tvGuideRow].Ch;
        TvGuideStopPreview();
        tvGuideHost.Visibility = Visibility.Collapsed;
        TvPlay(ch, tvGuideChans);
    }
    // the small picture is its own window, drawn over everything in the launcher's: it steps aside while a menu or the
    // keyboard is open over the guide (checked with the hints, which every menu updates when it opens or closes)
    void TvAirspace() {
        if (tvGuideHost == null) return;
        bool covered = ListPageOpen || OskOpen;
        var want = TvGuideOpen && !TvPlaying && !covered ? Visibility.Visible : Visibility.Collapsed;
        if (tvGuideHost.Visibility != want) tvGuideHost.Visibility = want;
    }
    // back from watching: the picture again
    void TvGuideBack() {
        if (!TvGuideOpen) return;
        tvGuideHost.Visibility = Visibility.Visible; tvZone = "guide";
        TvGuideMark(); tvGuidePreviewTimer.Stop(); tvGuidePreviewTimer.Start();
    }
}
}
