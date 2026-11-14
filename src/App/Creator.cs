// The character creator: full screen, with the tabs down the left (Face, Hair, Eyes ... Backdrop), the character in
// the middle (it blinks and breathes, and the view moves in on the face for the face tabs), and the choices for the
// tab on the right: pictures of each part on your own character, colour swatches, and sliders to move, size, space
// and tilt. The jingle (Jingle.cs) plays while it's open. Save keeps it for the current profile; Undo goes back a
// step at a time; Random makes a new character.
// Controller: LB / RB change tab, the stick and d-pad move (left from the first column goes to the tabs), A picks,
// left / right move a slider, X random, Y undo, Menu saves, B leaves (twice when there are changes).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RecompLauncher {

// the character on the stage, in 3D and moving (Avatar3D.cs): it blinks and breathes, hops when something changes,
// turns when dragged (or LT / RT), and the camera glides between the whole body and the face
class AvatarView : Grid {
    public AvatarDef Def;
    public readonly AvatarState State = new AvatarState();
    public readonly Avatar3DView View3D = new Avatar3DView();
    string shown; int shownMood = -99; DateTime lastBuild = DateTime.MinValue;
    double yawTarget; bool dragging; Point dragFrom; DateTime turnedAt = DateTime.MinValue;
    public AvatarView() {
        Background = Brushes.Transparent;
        Children.Add(View3D);
        CompositionTarget.Rendering += Check;
        MouseLeftButtonDown += (s, e) => { dragging = true; dragFrom = e.GetPosition(this); CaptureMouse(); };
        MouseMove += (s, e) => { if (!dragging) return; var p = e.GetPosition(this); yawTarget += (p.X - dragFrom.X) * 0.7; dragFrom = p; turnedAt = DateTime.Now; };
        MouseLeftButtonUp += (s, e) => { dragging = false; ReleaseMouseCapture(); };
    }
    // the character changes in place: built again (at most about nine times a second, while a slider moves)
    void Check(object s, EventArgs e) {
        if (!IsVisible || Def == null) return;
        string k = Def.ToString();
        if ((k != shown || State.Mood != shownMood) && (DateTime.Now - lastBuild).TotalMilliseconds > 110) {
            shown = k; shownMood = State.Mood; lastBuild = DateTime.Now;
            View3D.Mood = State.Mood; View3D.Rebuild(Def);
        }
        // a turn eases in, and after a while it turns back to face you
        if (!dragging && (DateTime.Now - turnedAt).TotalSeconds > 4) yawTarget *= 0.97;
        View3D.Yaw += (yawTarget - View3D.Yaw) * 0.15;
    }
    public void Turn(double deg) { yawTarget += deg; turnedAt = DateTime.Now; }
    public void Hop() { View3D.Play(Anim3D.Jump, 1.05); }
    public void Aim(Rect target, bool now) { bool face = target.Height < 540; View3D.Aim(face ? AvatarArt.Crop.Head : AvatarArt.Crop.Full, face ? 0.78 : 0.92, now); }
}

partial class Shell {
    Grid crLayer; AvatarView crView; Ellipse crPicture; FrameworkElement crPictureBox; StackPanel crTabs, crPanel; ScrollViewer crScroll; TextBlock crName, crMsg, crTabTitle;
    Button crSave, crCancel, crRandom, crUndo;
    AvatarDef crDef; string crStart;
    readonly List<string> crUndoStack = new List<string>();
    DateTime crLastPush; string crLastKey;
    int crTab; bool crInTabs = true; int crIdx; bool crConfirm, crWasFull;
    readonly List<FrameworkElement> crStops = new List<FrameworkElement>();
    readonly Dictionary<FrameworkElement, object[]> crInfo = new Dictionary<FrameworkElement, object[]>();   // (item, part index or colour) per stop
    readonly List<Border> crTabButtons = new List<Border>();
    readonly List<object[]> crThumbs = new List<object[]>();   // (image, item, part index)
    DispatcherTimer crBlink, crThumbTimer;
    readonly Random crRandomGen = new Random();
    bool CreatorOpen { get { return crLayer != null && crLayer.Visibility == Visibility.Visible; } }

    // ---------- building ----------
    void BuildCreator() {
        if (crLayer != null) return;
        crLayer = new Grid { Visibility = Visibility.Collapsed, Opacity = 0, Background = Ui.Brush("#090C0F") };
        crLayer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(82) });
        crLayer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        crLayer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
        crLayer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
        crLayer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        crLayer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(590) });

        // the stage behind the character: a soft light in the middle
        var stage = new Border { Background = new RadialGradientBrush(Ui.Col("#1C252C"), Ui.Col("#090C0F")) { RadiusX = 0.6, RadiusY = 0.6 } };
        Grid.SetColumn(stage, 1); Grid.SetRowSpan(stage, 3);
        crLayer.Children.Add(stage);

        // top bar
        var top = new Grid { Margin = new Thickness(30, 18, 30, 0) };
        Grid.SetColumnSpan(top, 3);
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = "CHARACTER CREATOR", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal) });
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        crName = new TextBlock { FontSize = 24, FontFamily = Ui.Semi, Cursor = Cursors.Hand, ToolTip = "Change the name" };
        crName.MouseLeftButtonUp += (s, e) => CrRename();
        crMsg = new TextBlock { FontSize = 13, Foreground = Ui.Brush(Ui.Gold), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(18, 0, 0, 4) };
        nameRow.Children.Add(crName); nameRow.Children.Add(crMsg);
        titles.Children.Add(nameRow);
        top.Children.Add(titles);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        crUndo = MakeButton("Undo", 40); crUndo.Click += (s, e) => CrUndo();
        crRandom = MakeButton("Random", 40); crRandom.Click += (s, e) => CrRandom();
        crCancel = MakeButton("Cancel", 40); crCancel.Click += (s, e) => CrLeave();
        crSave = MakeButton("Save", 40); crSave.Background = Ui.Brush(Ui.Teal); crSave.Foreground = Ui.Brush("#0A0E11"); crSave.Padding = new Thickness(26, 0, 26, 0);
        crSave.Click += (s, e) => CloseCreator(true);
        foreach (var b in new[] { crUndo, crRandom, crCancel, crSave }) buttons.Children.Add(b);
        top.Children.Add(buttons);
        crLayer.Children.Add(top);

        // tabs
        crTabs = new StackPanel { Margin = new Thickness(18, 4, 0, 10) };
        for (int i = 0; i < AvatarSpec.Tabs.Count; i++) {
            var t = AvatarSpec.Tabs[i]; int idx = i;
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = t.Glyph, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = t.Name, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 0) });
            var b = new Border { Width = 92, Height = 50, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 0, 3), Child = sp, Cursor = Cursors.Hand };
            b.MouseLeftButtonUp += (s, e) => { crInTabs = true; Sound.Play(Sfx.Move); SelectCrTab(idx); };
            crTabButtons.Add(b); crTabs.Children.Add(b);
        }
        var tabScroll = new ScrollViewer { Content = crTabs, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false };
        Grid.SetRow(tabScroll, 1);
        crLayer.Children.Add(tabScroll);

        // the character
        crView = new AvatarView { Margin = new Thickness(10, 0, 10, 10), RenderTransformOrigin = new Point(0.5, 0.92) };
        var tg = new TransformGroup(); tg.Children.Add(new ScaleTransform(1, 1)); tg.Children.Add(new ScaleTransform(1, 1)); tg.Children.Add(new RotateTransform(0));
        crView.RenderTransform = tg;
        Grid.SetRow(crView, 1); Grid.SetColumn(crView, 1);
        crLayer.Children.Add(crView);
        // on the Pose and Backdrop tabs: the round picture made from it, as Home shows it
        crPicture = new Ellipse { Width = 150, Height = 150, Stroke = Ui.Brush("#26333C"), StrokeThickness = 4, Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.3 } };
        var pic = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(26, 6, 0, 0), Visibility = Visibility.Collapsed };
        pic.Children.Add(crPicture);
        pic.Children.Add(new TextBlock { Text = "Your picture", FontSize = 12, Foreground = Ui.Brush(Ui.Muted), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
        crPictureBox = pic;
        Grid.SetRow(pic, 1); Grid.SetColumn(pic, 1);
        crLayer.Children.Add(pic);

        // the choices
        var right = new Border { Background = Ui.Brush("#11181D"), CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 4, 26, 10), Padding = new Thickness(24, 18, 12, 10),
            BorderBrush = Ui.Brush("#26333C"), BorderThickness = new Thickness(1) };
        var rightGrid = new Grid();
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        crTabTitle = new TextBlock { FontSize = 20, FontFamily = Ui.Semi, Margin = new Thickness(0, 0, 0, 6) };
        rightGrid.Children.Add(crTabTitle);
        crPanel = new StackPanel { Margin = new Thickness(0, 0, 12, 10) };
        crScroll = new ScrollViewer { Content = crPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        Grid.SetRow(crScroll, 1);
        rightGrid.Children.Add(crScroll);
        right.Child = rightGrid;
        Grid.SetRow(right, 1); Grid.SetColumn(right, 2);
        crLayer.Children.Add(right);

        // button hints
        var hints = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 30, 4) };
        foreach (var h in new[] { new[] { "LB", "Tab" }, new[] { "RB", "Tab" }, new[] { "A", "Pick" }, new[] { "X", "Random" }, new[] { "Y", "Undo" }, new[] { "⧉", "Name" }, new[] { "≡", "Save" }, new[] { "B", "Back" } }) hints.Children.Add(Hint(h[0], h[1]));
        Grid.SetRow(hints, 2); Grid.SetColumnSpan(hints, 3);
        crLayer.Children.Add(hints);

        crLayer.LayoutTransform = N<Grid>("Main").LayoutTransform;
        N<Grid>("Clipped").Children.Add(crLayer);
        W.RegisterName("CreatorLayer", crLayer);

        crBlink = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        crBlink.Tick += (s, e) => {
            if (crView.State.Blink) { crView.State.Blink = false; crBlink.Interval = TimeSpan.FromMilliseconds(1800 + crRandomGen.Next(3200)); }
            else { crView.State.Blink = true; crBlink.Interval = TimeSpan.FromMilliseconds(130); }
            crView.InvalidateVisual();
        };
        crThumbTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        crThumbTimer.Tick += (s, e) => { crThumbTimer.Stop(); RenderCrThumbs(); };
    }

    // ---------- open and close ----------
    void OpenCreator() {
        BuildCreator();
        string saved = Profiles.Get("Character");
        crDef = saved.Length > 0 ? AvatarDef.Parse(saved) : AvatarDef.Random(crRandomGen);
        crStart = saved.Length > 0 ? crDef.ToString() : null;
        crUndoStack.Clear(); crConfirm = false; crMsg.Text = "";
        crName.Text = Profiles.DisplayName(Profiles.Current);
        crView.Def = crDef;
        crWasFull = isFull;
        if (!isFull) SetFullscreen(true);
        crLayer.Visibility = Visibility.Visible;
        crLayer.BeginAnimation(UIElement.OpacityProperty, Anim(1, 260, 0));
        Sound.Play(Sfx.Open);
        Jingle.Start();
        crInTabs = true;
        SelectCrTab(0);
        crView.Aim(CrCamera(), true);
        StartCrIdle();
        crBlink.Start();
    }
    void CloseCreator(bool save) {
        if (!CreatorOpen) return;
        if (save) {
            Profiles.Set("Character", crDef.ToString());
            Profiles.Set("Avatar", "char");
            Profiles.Save();
            Sound.Play(Sfx.Select);
        } else Sound.Play(Sfx.Back);
        Jingle.Stop();
        crBlink.Stop();
        var a = Anim(0, 220, null, EasingMode.EaseIn);
        a.Completed += (s, e) => { crLayer.Visibility = Visibility.Collapsed; SetHints(); };
        crLayer.BeginAnimation(UIElement.OpacityProperty, a);
        if (!crWasFull) SetFullscreen(false);
        ApplyAvatar();
        if (ProfileOpen) { FillProfile(); MarkProfile(); }
        if (plazaReturn) { plazaReturn = false; OpenPlaza(); }
    }
    // leaving without saving asks first when something changed
    void CrLeave() {
        bool changed = crStart == null || crDef.ToString() != crStart;
        if (changed && !crConfirm) { crConfirm = true; crMsg.Text = "Your changes aren't saved. Press B (or Cancel) again to leave anyway."; Sound.Play(Sfx.Bump); return; }
        CloseCreator(false);
    }
    void StartCrIdle() {
        return;   // the 3D character breathes and sways by itself
        var tg = (TransformGroup)crView.RenderTransform;
        var breathe = new DoubleAnimation(1, 1.012, TimeSpan.FromSeconds(1.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        ((ScaleTransform)tg.Children[0]).BeginAnimation(ScaleTransform.ScaleYProperty, breathe);
        var sway = new DoubleAnimation(-0.8, 0.8, TimeSpan.FromSeconds(2.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        ((RotateTransform)tg.Children[2]).BeginAnimation(RotateTransform.AngleProperty, sway);
    }
    // a little hop when something changes
    void CrPop() {
        crView.Hop(); return;
        var st = (ScaleTransform)((TransformGroup)crView.RenderTransform).Children[1];
        var a = new DoubleAnimation(1.035, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, a); st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }
    Rect CrCamera() {
        var t = AvatarSpec.Tabs[crTab];
        if (!t.Zoom) return new Rect(-40, 40, 480, 560);
        var r = AvatarArt.CropRect(crDef, AvatarArt.Crop.Head);
        r.Inflate(r.Width * 0.38, r.Height * 0.38);
        return r;
    }

    // ---------- a tab's choices ----------
    void SelectCrTab(int i) {
        int n = AvatarSpec.Tabs.Count;
        crTab = ((i % n) + n) % n;
        var tab = AvatarSpec.Tabs[crTab];
        for (int k = 0; k < crTabButtons.Count; k++) {
            bool on = k == crTab;
            crTabButtons[k].Background = on ? Ui.Brush(Ui.Teal) : Ui.Brush("#00000000");
            foreach (TextBlock tb in ((StackPanel)crTabButtons[k].Child).Children) tb.Foreground = on ? Ui.Brush(Theme.Current.Light ? "#ffffff" : "#0A0E11") : Ui.Brush(Ui.Text);
        }
        crTabButtons[crTab].BringIntoView();
        crTabTitle.Text = tab.Name;
        // the plain face while editing, your mood and backdrop on their own tabs
        crView.State.Mood = tab.Name == "Pose" ? -1 : 0;
        crView.State.Backdrop = false;
        crPictureBox.Visibility = tab.Name == "Backdrop" || tab.Name == "Pose" ? Visibility.Visible : Visibility.Collapsed;
        ShowCrPicture();
        crView.Aim(CrCamera(), false);
        crView.InvalidateVisual();

        crPanel.Children.Clear(); crStops.Clear(); crInfo.Clear(); crThumbs.Clear();
        bool first = true;
        foreach (var it in tab.Items) {
            crPanel.Children.Add(new TextBlock { Text = it.Label.ToUpper(), FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal), Margin = new Thickness(2, first ? 6 : 18, 0, 8) });
            first = false;
            if (it.Kind == SpecKind.Part) crPanel.Children.Add(PartGrid(it));
            else if (it.Kind == SpecKind.Colour) crPanel.Children.Add(SwatchGrid(it));
            else crPanel.Children.Add(SliderRow(it));
        }
        crScroll.ScrollToTop();
        crIdx = Math.Max(0, crStops.FindIndex(IsCrPicked));
        MarkCr();
        RenderCrThumbs();
    }
    FrameworkElement PartGrid(AvatarItem it) {
        var wrap = new WrapPanel();
        bool tall = it.Thumb == 1;
        for (int i = 0; i < it.Names.Length; i++) {
            var img = new Image { Width = 84, Height = tall ? 112 : 84, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var stack = new StackPanel();
            stack.Children.Add(img);
            stack.Children.Add(new TextBlock { Text = it.Names[i], FontSize = 10.5, Foreground = Ui.Brush(Ui.Muted), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 2, 2, 0) });
            var tile = new Border { Width = 98, CornerRadius = new CornerRadius(14), Background = Ui.Brush("#1C252C"), BorderThickness = new Thickness(3), Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(4, 4, 4, 5), Child = stack, Cursor = Cursors.Hand, ToolTip = it.Names[i] };
            AddCrStop(tile, it, i);
            crThumbs.Add(new object[] { img, it, i });
            wrap.Children.Add(tile);
        }
        return wrap;
    }
    FrameworkElement SwatchGrid(AvatarItem it) {
        var wrap = new WrapPanel();
        foreach (var hex in it.Palette) {
            var g = new Grid { Width = 42, Height = 42, Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            g.Children.Add(new Ellipse { StrokeThickness = 3 });
            var fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); fill.Freeze();
            g.Children.Add(new Ellipse { Margin = new Thickness(6), Fill = fill, Stroke = Ui.Brush("#26333C"), StrokeThickness = 1 });
            AddCrStop(g, it, hex);
            wrap.Children.Add(g);
        }
        return wrap;
    }
    FrameworkElement SliderRow(AvatarItem it) {
        var row = new Grid { Height = 40, Margin = new Thickness(0, 0, 10, 4), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        var track = new Grid { VerticalAlignment = VerticalAlignment.Center, Height = 26, Margin = new Thickness(13, 0, 13, 0) };
        track.Children.Add(new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = Ui.Brush("#26333C"), VerticalAlignment = VerticalAlignment.Center });
        track.Children.Add(new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = Ui.Brush(Ui.Teal), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left });
        track.Children.Add(new Ellipse { Width = 26, Height = 26, Fill = Ui.Brush(Ui.Text), HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = new TranslateTransform(),
            Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.3 } });
        row.Children.Add(track);
        var val = new TextBlock { FontSize = 13, FontFamily = Ui.Semi, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(val, 1); row.Children.Add(val);
        Action<MouseEventArgs> setFrom = e => {
            double x = e.GetPosition(track).X / Math.Max(1, track.ActualWidth);
            CrSet(it, it.Min + Math.Max(0, Math.Min(1, x)) * (it.Max - it.Min), false);
        };
        row.MouseLeftButtonDown += (s, e) => { crIdx = crStops.IndexOf(row); crInTabs = false; row.CaptureMouse(); setFrom(e); MarkCr(); };
        row.MouseMove += (s, e) => { if (row.IsMouseCaptured) setFrom(e); };
        row.MouseLeftButtonUp += (s, e) => row.ReleaseMouseCapture();
        // double click: back to the middle
        row.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) CrSet(it, it.Def, true); };
        row.SizeChanged += (s, e) => MarkCr();
        crStops.Add(row); crInfo[row] = new object[] { it, null };
        return row;
    }
    void AddCrStop(FrameworkElement el, AvatarItem it, object value) {
        crStops.Add(el); crInfo[el] = new object[] { it, value };
        el.MouseLeftButtonUp += (s, e) => { crIdx = crStops.IndexOf(el); crInTabs = false; CrPick(el); };
    }
    bool IsCrPicked(FrameworkElement el) {
        var info = crInfo[el]; var it = (AvatarItem)info[0];
        if (it.Kind == SpecKind.Part) return crDef.I(it.Key) == (int)info[1];
        if (it.Kind == SpecKind.Colour) return string.Equals(crDef.C(it.Key), (string)info[1], StringComparison.OrdinalIgnoreCase);
        return false;
    }
    // rings on the picked choices, the sliders where they are, a glow where the controller is
    void MarkCr() {
        for (int i = 0; i < crStops.Count; i++) {
            var el = crStops[i]; var it = (AvatarItem)crInfo[el][0];
            bool focus = !crInTabs && i == crIdx;
            if (it.Kind == SpecKind.Part) ((Border)el).BorderBrush = IsCrPicked(el) ? Ui.Brush(Ui.Teal) : Brushes.Transparent;
            else if (it.Kind == SpecKind.Colour) ((Ellipse)((Grid)el).Children[0]).Stroke = IsCrPicked(el) ? Ui.Brush(Ui.Text) : Brushes.Transparent;
            else {
                var row = (Grid)el; var track = (Grid)row.Children[0];
                double f = (crDef.D(it.Key) - it.Min) / (it.Max - it.Min), w = Math.Max(0, track.ActualWidth);
                ((Border)track.Children[1]).Width = Math.Max(8, f * w);
                ((TranslateTransform)((Ellipse)track.Children[2]).RenderTransform).X = f * w - 13;
                double v = crDef.D(it.Key);
                ((TextBlock)row.Children[1]).Text = it.Percent ? Math.Round(v * 100) + "%" : (v > 0 ? "+" : "") + Math.Round(v);
                row.Background = Ui.Brush(focus ? "#203039" : "#00000000");
            }
            el.Effect = focus && it.Kind != SpecKind.Slider ? SetGlow : null;
        }
        for (int k = 0; k < crTabButtons.Count; k++) crTabButtons[k].Effect = crInTabs && k == crTab ? SetGlow : null;
        if (!crInTabs && crIdx < crStops.Count) crStops[crIdx].BringIntoView();
    }
    // each part's picture: your character wearing that choice
    // the part pictures are drawn on a thread of their own (a 3D picture takes a moment): the creator never waits,
    // and pictures for a character that has changed since are dropped
    static System.Windows.Threading.Dispatcher thumbWorker; int crThumbGen;
    static System.Windows.Threading.Dispatcher ThumbWorker() {
        if (thumbWorker != null) return thumbWorker;
        var ready = new System.Threading.ManualResetEvent(false);
        var t = new System.Threading.Thread(() => { thumbWorker = System.Windows.Threading.Dispatcher.CurrentDispatcher; ready.Set(); System.Windows.Threading.Dispatcher.Run(); });
        t.SetApartmentState(System.Threading.ApartmentState.STA); t.IsBackground = true; t.Name = "Character pictures"; t.Priority = System.Threading.ThreadPriority.BelowNormal;
        t.Start(); ready.WaitOne();
        return thumbWorker;
    }
    void RenderCrThumbs() {
        int gen = ++crThumbGen;
        string def = crDef.ToString();
        var ui = W.Dispatcher;
        foreach (var t in crThumbs) {
            var img = (Image)t[0]; var it = (AvatarItem)t[1]; int i = (int)t[2];
            ThumbWorker().BeginInvoke(DispatcherPriority.Background, new Action(() => {
                if (gen != crThumbGen) return;   // the character changed again: these are out of date
                ImageSource pic = null;
                try {
                    var d = AvatarDef.Parse(def); d.Set(it.Key, i);
                    var st = new AvatarState { Mood = it.Key == "mood" ? i : 0, Pose = it.Key == "pose" ? i : -1, Backdrop = it.Thumb == 2 || it.Key == "pose" };
                    pic = it.Thumb == 1 ? AvatarArt.Render(d, st, AvatarArt.Crop.Full, 126, 168)
                        : AvatarArt.Render(d, st, it.Thumb == 2 ? AvatarArt.Crop.Portrait : it.Thumb == 3 ? AvatarArt.Crop.Features : AvatarArt.Crop.Head, 126, 126);
                    if (pic != null && pic.CanFreeze && !pic.IsFrozen) pic.Freeze();
                } catch { }
                if (pic != null) ui.BeginInvoke(new Action(() => { if (gen == crThumbGen) img.Source = pic; }));
            }));
        }
    }

    // ---------- changes ----------
    void CrPushUndo(string key) {
        if (crLastKey == key && (DateTime.Now - crLastPush).TotalMilliseconds < 700) { crLastPush = DateTime.Now; return; }
        crUndoStack.Add(crDef.ToString());
        if (crUndoStack.Count > 100) crUndoStack.RemoveAt(0);
        crLastKey = key; crLastPush = DateTime.Now;
    }
    void CrChanged(bool pop) {
        crConfirm = false; crMsg.Text = "";
        crView.InvalidateVisual();
        if (pop) CrPop();
        if (AvatarSpec.Tabs[crTab].Zoom) crView.Aim(CrCamera(), false);
        MarkCr();
        crThumbTimer.Stop(); crThumbTimer.Start();
        ShowCrPicture();
    }
    void ShowCrPicture() {
        if (crPictureBox.Visibility != Visibility.Visible) return;
        var b = new ImageBrush(AvatarArt.Portrait(crDef, 220)); b.Freeze();
        crPicture.Fill = b;
    }
    void CrPick(FrameworkElement el) {
        var info = crInfo[el]; var it = (AvatarItem)info[0];
        if (it.Kind == SpecKind.Slider) return;
        if (IsCrPicked(el)) { Sound.Play(Sfx.Bump); MarkCr(); return; }
        CrPushUndo(it.Key + info[1]);
        if (it.Kind == SpecKind.Part) crDef.Set(it.Key, (int)info[1]);
        else {
            // hair colour carries to the eyebrows and beard when they matched it
            if (it.Key == "hairCol") foreach (var k in new[] { "browCol", "beardCol" }) if (crDef.C(k) == crDef.C("hairCol")) crDef.SetColour(k, (string)info[1]);
            crDef.SetColour(it.Key, (string)info[1]);
        }
        Sound.Play(Sfx.Select);
        CrChanged(true);
    }
    void CrSet(AvatarItem it, double v, bool pop) {
        CrPushUndo(it.Key);
        double before = crDef.D(it.Key);
        crDef.Set(it.Key, v);
        if (Math.Abs(before - crDef.D(it.Key)) < 1e-9) return;
        if (pop) Sound.Play(Sfx.Move);
        CrChanged(false);
    }
    // the character's (and its profile's) name, typed on the on-screen keyboard; a click on the name does the same
    void CrRename() {
        OpenOsk("Name", Profiles.Get("PlayerName"), null, t => {
            t = (t ?? "").Trim(); if (t.Length > 24) t = t.Substring(0, 24);
            Profiles.Set("PlayerName", t); Profiles.Save();
            crName.Text = Profiles.DisplayName(Profiles.Current); ApplyAvatar();
        }, null);
    }
    void CrUndo() {
        if (crUndoStack.Count == 0) { Sound.Play(Sfx.Bump); return; }
        crDef = AvatarDef.Parse(crUndoStack[crUndoStack.Count - 1]);
        crUndoStack.RemoveAt(crUndoStack.Count - 1);
        crLastKey = null;
        crView.Def = crDef;
        Sound.Play(Sfx.Back);
        CrChanged(true);
    }
    void CrRandom() {
        crUndoStack.Add(crDef.ToString()); crLastKey = null;
        crDef = AvatarDef.Random(crRandomGen);
        crView.Def = crDef;
        Sound.Play(Sfx.Select);
        CrChanged(true);
    }

    // ---------- controller and keyboard ----------
    void CreatorPad(int bit) {
        if (bit == Pad.LB || bit == Pad.RB) { Sound.Play(Sfx.Move); SelectCrTab(crTab + (bit == Pad.LB ? -1 : 1)); return; }
        if (bit == Pad.X) { CrRandom(); return; }
        if (bit == Pad.Back) { CrRename(); return; }   // the name, on the on-screen keyboard
        if (bit == Pad.Y) { CrUndo(); return; }
        if (bit == Pad.LT || bit == Pad.RT) { crView.Turn(bit == Pad.RT ? 45 : -45); return; }   // turn the character round
        if (bit == Pad.Start) { CloseCreator(true); return; }
        if (bit == Pad.B) { if (!crInTabs) { crInTabs = true; Sound.Play(Sfx.Back); MarkCr(); } else CrLeave(); return; }
        if (crInTabs) {
            if (bit == Pad.Up || bit == Pad.Down) { Sound.Play(Sfx.Move); SelectCrTab(crTab + (bit == Pad.Up ? -1 : 1)); }
            else if (bit == Pad.Right || bit == Pad.A) { if (crStops.Count > 0) { crInTabs = false; crIdx = Math.Max(0, crStops.FindIndex(IsCrPicked)); Sound.Play(Sfx.Move); MarkCr(); } }
            return;
        }
        if (crIdx >= crStops.Count) crIdx = 0;
        var cur = crStops[crIdx]; var it = (AvatarItem)crInfo[cur][0];
        if (bit == Pad.A) { CrPick(cur); return; }
        if (it.Kind == SpecKind.Slider && (bit == Pad.Left || bit == Pad.Right)) {
            CrSet(it, crDef.D(it.Key) + (bit == Pad.Left ? -1 : 1) * (it.Max - it.Min) / 24, true);
            return;
        }
        int dx = bit == Pad.Left ? -1 : bit == Pad.Right ? 1 : 0, dy = bit == Pad.Up ? -1 : bit == Pad.Down ? 1 : 0;
        if (dx == 0 && dy == 0) return;
        Func<FrameworkElement, Point> centre = el => el.TransformToAncestor(crPanel).Transform(new Point(el.ActualWidth / 2, el.ActualHeight / 2));
        var c0 = centre(cur);
        int best = -1; double bestScore = double.MaxValue;
        for (int i = 0; i < crStops.Count; i++) {
            if (i == crIdx) continue;
            var c = centre(crStops[i]);
            double along = (c.X - c0.X) * dx + (c.Y - c0.Y) * dy, across = Math.Abs((c.X - c0.X) * dy) + Math.Abs((c.Y - c0.Y) * dx);
            if (along < 8) continue;
            double score = along + across * (dy != 0 ? 0.5 : 3);
            if (score < bestScore) { bestScore = score; best = i; }
        }
        if (best < 0) {
            if (dx < 0) { crInTabs = true; Sound.Play(Sfx.Move); MarkCr(); }
            else Sound.Play(Sfx.Bump);
            return;
        }
        Sound.Play(Sfx.Move);
        crIdx = best; MarkCr();
    }
    bool CreatorKey(Key k) {
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl && k == Key.Z) { CrUndo(); return true; }
        if (ctrl && k == Key.S) { CloseCreator(true); return true; }
        int bit = k == Key.Left ? Pad.Left : k == Key.Right ? Pad.Right : k == Key.Up ? Pad.Up : k == Key.Down ? Pad.Down
            : k == Key.Enter || k == Key.Space ? Pad.A : k == Key.Escape ? Pad.B : k == Key.PageUp ? Pad.LB : k == Key.PageDown || k == Key.Tab ? Pad.RB : 0;
        if (bit == 0) return false;
        CreatorPad(bit);
        return true;
    }
}
}
