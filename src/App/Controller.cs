// Any XInput controller (Xbox, or others through Steam Input) while the window is in front.
// Main screen: three rows to move between: the library, the game's buttons, the top bar.
//   A plays or installs (or presses the focused button), X updates, Y What's new, B goes back (buttons -> library,
//   then clears search and filter), LB / RB filter, LS sort, RS next view, Menu Settings, View full screen,
//   right stick scrolls the release notes or the list.
// What's new: B / Y close, LB / RB tabs. Settings: up / down move, A switches or presses, Menu saves, B cancels.
// Choose your game: A installs, X opens the file browser, Y skips, B closes. Uninstall: A uninstalls, B cancels.
// The controller is read 60 times a second only while one is connected and the window is active; otherwise it is
// looked for every 2 seconds.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RecompLauncher {

partial class Shell {
    DispatcherTimer padTimer;
    bool padActive = true, padOn;
    int padPrev, stickDir;
    readonly Dictionary<int, DateTime> padHeld = new Dictionary<int, DateTime>();
    string zone = "cards";
    int focusIdx;
    DateTime nextScan = DateTime.MinValue;

    void BuildController() {
        padTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        padTimer.Tick += (s, e) => PadTick();
    }
    void StartController() { padTimer.Start(); }

    void PadTick() {
        var now = DateTime.Now;
        if (!padOn) {
            if (now < nextScan) return;
            nextScan = now.AddSeconds(2);
        }
        Pad.State st;
        bool ok = Pad.TryGet(out st);
        if (ok != padOn) {
            padOn = ok;
            padTimer.Interval = TimeSpan.FromMilliseconds(ok ? 16 : 250);
            if (!ok) { padPrev = 0; padHeld.Clear(); }
        }
        if (!ok || !padActive) { padPrev = 0; return; }
        int b = st.Buttons;
        // the left stick works like the d-pad
        // a worn or resting stick doesn't move the selection: it counts once pushed past 24000 (of 32767), and
        // keeps counting until it's back under 12000; only its stronger direction counts
        const int press = 24000, release = 12000;
        int ax = Math.Abs((int)st.LX), ay = Math.Abs((int)st.LY);
        int was = stickDir, now2 = 0;
        if (ay >= ax) { if (st.LY > ((was & Pad.Up) != 0 ? release : press)) now2 = Pad.Up; else if (st.LY < -((was & Pad.Down) != 0 ? release : press)) now2 = Pad.Down; }
        else { if (st.LX < -((was & Pad.Left) != 0 ? release : press)) now2 = Pad.Left; else if (st.LX > ((was & Pad.Right) != 0 ? release : press)) now2 = Pad.Right; }
        stickDir = now2;
        b |= now2;
        int pressed = b & ~padPrev;
        foreach (int bit in new[] { Pad.Up, Pad.Down, Pad.Left, Pad.Right }) {
            if ((pressed & bit) != 0) padHeld[bit] = now.AddMilliseconds(400);
            else if ((b & bit) != 0) { DateTime t; if (padHeld.TryGetValue(bit, out t) && now >= t) { pressed |= bit; padHeld[bit] = now.AddMilliseconds(110); } }
        }
        padPrev = b;
        // right stick: scroll
        if (Math.Abs((int)st.RY) > press) ScrollBy(-st.RY / 32768.0 * 24);
        if (pressed == 0) return;
        if (!padUsed) { padUsed = true; SetHints(); }
        foreach (int bit in new[] { Pad.Up, Pad.Down, Pad.Left, Pad.Right, Pad.A, Pad.B, Pad.X, Pad.Y, Pad.LB, Pad.RB, Pad.LS, Pad.RS, Pad.Start, Pad.Back })
            if ((pressed & bit) != 0) PadPress(bit);
    }
    void ScrollBy(double dy) {
        ScrollViewer sv = null;
        if (N<Grid>("NotesLayer").Visibility == Visibility.Visible) sv = FindChild<ScrollViewer>(N<FlowDocumentScrollViewer>("Viewer"));
        else if (N<Grid>("SettingsLayer").Visibility == Visibility.Visible) return;
        else if (view == View.List) sv = N<ScrollViewer>("ListScroll");
        else if (view == View.Grid) sv = N<ScrollViewer>("GridScroll");
        else if (view == View.Compact) sv = N<ScrollViewer>("CompactScroll");
        if (sv != null) sv.ScrollToVerticalOffset(sv.VerticalOffset + dy);
    }
    static T FindChild<T>(DependencyObject el) where T : DependencyObject {
        if (el == null) return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(el); i++) {
            var c = VisualTreeHelper.GetChild(el, i);
            if (c is T) return (T)c;
            var r = FindChild<T>(c); if (r != null) return r;
        }
        return null;
    }
    // test hook: -PadScript "Right,Right,A"
    void PlayPadScript(string script) {
        var q = new Queue<string>(script.Split(',').Select(x => x.Trim()));
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        t.Tick += (s, e) => {
            if (q.Count == 0) { t.Stop(); return; }
            var name = q.Dequeue();
            var f = typeof(Pad).GetField(name);
            if (f != null) { padUsed = true; PadPress((int)f.GetValue(null)); }
        };
        t.Start();
    }

    List<Button> ZoneButtons(string z) {
        string pre = view == View.Row ? "" : "G";
        var names = z == "top" ? new[] { "BtnCheck", "BtnAll", "BtnSettings", "BtnFull" }
                               : new[] { pre + "BtnPlay", pre + "BtnUpdate", pre + "BtnNotes", pre + "BtnFolder", pre + "BtnBackups", pre + "BtnUninstall" };
        return names.Select(n => N<Button>(n)).Where(b => b.Visibility == Visibility.Visible && b.IsEnabled).ToList();
    }
    void SetZone(string z) {
        zone = z;
        if (z == "cards") HideRing();
        else { focusIdx = 0; PlaceRing(); }
        SetHints();
    }
    void MoveFocus(int dir) {
        var l = ZoneButtons(zone); if (l.Count == 0) return;
        focusIdx = Math.Max(0, Math.Min(l.Count - 1, focusIdx + dir));
        PlaceRing();
    }
    void FixFocus() {
        if (zone == "cards") return;
        var l = ZoneButtons(zone);
        if (l.Count == 0) { zone = "cards"; HideRing(); return; }
        if (focusIdx >= l.Count) focusIdx = l.Count - 1;
        QueueRing();
    }
    void PlaceRing() {
        var ring = N<Border>("FocusRing");
        if (!padUsed || zone == "cards" || view == View.Home || N<Grid>("NotesLayer").Visibility == Visibility.Visible || AnyDialog()) { HideRing(); return; }
        var l = ZoneButtons(zone); if (l.Count == 0) { HideRing(); return; }
        var b = l[Math.Min(focusIdx, l.Count - 1)];
        var main = N<Grid>("Main");
        if (!b.IsVisible) return;
        var p = b.TransformToAncestor(main).Transform(new Point(0, 0));
        ring.Width = b.ActualWidth + 12; ring.Height = b.ActualHeight + 12;
        var tt = (TranslateTransform)ring.RenderTransform;
        bool shown = ring.Opacity > 0.5;
        tt.BeginAnimation(TranslateTransform.XProperty, Anim(p.X - 6, shown ? 160 : 0));
        tt.BeginAnimation(TranslateTransform.YProperty, Anim(p.Y - 6, shown ? 160 : 0));
        ring.BeginAnimation(UIElement.OpacityProperty, Anim(1, 160));
    }
    void HideRing() { N<Border>("FocusRing").BeginAnimation(UIElement.OpacityProperty, Anim(0, 200)); }
    void QueueRing() { W.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { if (padUsed && zone != "cards") PlaceRing(); })); }
    void Press(Button b) {
        if (b == null || !b.IsEnabled || b.Visibility != Visibility.Visible) return;
        b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    void PadPress(int bit) {
        if (CreatorOpen) { CreatorPad(bit); return; }
        // overlays first
        if (N<Grid>("UninstLayer").Visibility == Visibility.Visible) {
            if (bit == Pad.A) Press(N<Button>("BtnUninstGo"));
            else if (bit == Pad.B) CloseUninstall();
            else if (bit == Pad.Up || bit == Pad.Down) SetToggle(keepSaves, !keepSaves.On, false);
            return;
        }
        if (FbVisible()) {
            if (bit == Pad.Up) SelectFb(fbIdx - 1); else if (bit == Pad.Down) SelectFb(fbIdx + 1);
            else if (bit == Pad.LB) SelectFb(fbIdx - 8); else if (bit == Pad.RB) SelectFb(fbIdx + 8);
            else if (bit == Pad.A || bit == Pad.Right) EnterFb();
            else if (bit == Pad.B || bit == Pad.Left) BackFb();
            else if (bit == Pad.X) UseFbFolder();
            else if (bit == Pad.Y) CloseFb();
            SetHints();
            return;
        }
        if (N<Grid>("RomLayer").Visibility == Visibility.Visible) {
            if (bit == Pad.A) Press(N<Button>("BtnRomInstall"));
            else if (bit == Pad.B) CloseRomPicker();
            else if (bit == Pad.X && romGame.Rom.Kind != "none") OpenFb();
            else if (bit == Pad.Y && romGame.Rom.Kind != "none") Press(N<Button>("BtnRomSkip"));
            else if (bit == Pad.Up) N<ScrollViewer>("RomScroll").ScrollToVerticalOffset(N<ScrollViewer>("RomScroll").VerticalOffset - 60);
            else if (bit == Pad.Down) N<ScrollViewer>("RomScroll").ScrollToVerticalOffset(N<ScrollViewer>("RomScroll").VerticalOffset + 60);
            return;
        }
        if (N<Grid>("SettingsLayer").Visibility == Visibility.Visible) {
            if (setIdx < 0) { SelectSetStop(0); return; }
            var el = setStops[setIdx];
            bool inGrid = el.Parent == showGrid;
            if (bit == Pad.Up) SelectSetStop(setIdx - (inGrid && setIdx - 2 >= 0 && setStops[setIdx - 2].Parent == showGrid ? 2 : 1));
            else if (bit == Pad.Down) SelectSetStop(setIdx + (inGrid && setIdx + 2 < setStops.Count && setStops[setIdx + 2].Parent == showGrid ? 2 : 1));
            else if (bit == Pad.Left) SelectSetStop(setIdx - 1);
            else if (bit == Pad.Right) SelectSetStop(setIdx + 1);
            else if (bit == Pad.A) { var t = el.Tag as Toggle; if (t != null) SetToggle(t, !t.On, false); else Press(el as Button); }
            else if (bit == Pad.Start) SaveSettingsPanel();
            else if (bit == Pad.B) CloseSettings();
            else if (bit == Pad.X) Press(N<Button>("BtnSetDefaults"));
            return;
        }
        if (N<Grid>("NotesLayer").Visibility == Visibility.Visible) {
            if (bit == Pad.B || bit == Pad.Y) CloseNotes();
            else if (bit == Pad.LB || bit == Pad.RB) SetTab(tab == "notes" ? "activity" : "notes");
            else if (bit == Pad.Up) ScrollBy(-80); else if (bit == Pad.Down) ScrollBy(80);
            return;
        }
        if (PlayersOpen) { PlayersPad(bit); SetHints(); return; }
        if (ProfileOpen) { ProfilePad(bit); SetHints(); return; }
        // main screen
        if (view == View.Home) {
            if (bit == Pad.LB && homePage == "home") { OpenPlayers(); SetHints(); return; }
            string d = bit == Pad.Left ? "left" : bit == Pad.Right ? "right" : bit == Pad.Up ? "up" : bit == Pad.Down ? "down" : bit == Pad.A ? "ok" : bit == Pad.B ? "back" : bit == Pad.Y ? "info" : null;
            if (d != null && HomeMove(d)) { SetHints(); return; }
        }
        switch (bit) {
            case Pad.Left: if (zone == "cards") StepGame(-1); else MoveFocus(-1); break;
            case Pad.Right: if (zone == "cards") StepGame(1); else MoveFocus(1); break;
            case Pad.Up:
                if (zone == "top") break;
                if (zone == "actions") { SetZone("top"); break; }
                if (view != View.Row) {
                    var before = sel; MoveVertical(-1);
                    if (before != sel) break;   // moved to the row above: still in the library
                }
                SetZone("actions");
                break;
            case Pad.Down:
                if (zone == "top") { SetZone("actions"); break; }
                if (zone == "actions") { SetZone("cards"); break; }
                if (view != View.Row) MoveVertical(1);
                break;
            case Pad.A:
                if (zone == "cards") { if (sel != null) PrimaryAction(sel); }
                else { var l = ZoneButtons(zone); if (l.Count > 0) Press(l[Math.Min(focusIdx, l.Count - 1)]); }
                break;
            case Pad.B:
                if (zone != "cards") SetZone("cards");
                else if (N<TextBox>("SearchBox").Text.Length > 0) N<TextBox>("SearchBox").Text = "";
                else if (filter != "all") SetFilter("all");
                break;
            case Pad.X: if (sel != null && sel.State == State.Update && !running) TryAction("Updating " + sel.Name, () => StartQueue(new List<Game> { sel })); break;
            case Pad.Y: OpenNotes("notes"); break;
            case Pad.LB: CycleFilter(-1); break;
            case Pad.RB: CycleFilter(1); break;
            case Pad.LS: CycleSort(); break;
            case Pad.RS: CycleView(1); break;
            case Pad.Start: if (!running) OpenSettings(); break;
            case Pad.Back: ToggleFullscreen(); break;
        }
        SetHints();
    }

    // ---------- button hints at the bottom, while a controller is used ----------
    string hintsShown = "";
    void SetHints() {
        var spec = new List<string[]>();
        if (!CreatorOpen && (padUsed || view == View.Home)) {
            if (N<Grid>("UninstLayer").Visibility == Visibility.Visible) { spec.Add(new[] { "A", "Uninstall" }); spec.Add(new[] { "B", "Cancel" }); }
            else if (FbVisible()) { spec.Add(new[] { "A", "Open" }); spec.Add(new[] { "B", "Back" }); if (fbDir != null && FbFolders()) spec.Add(new[] { "X", "Use this folder" }); spec.Add(new[] { "Y", "Close" }); }
            else if (N<Grid>("RomLayer").Visibility == Visibility.Visible) {
                spec.Add(new[] { "A", "Install" });
                if (romGame != null && romGame.Rom.Kind != "none") { spec.Add(new[] { "X", "Browse" }); spec.Add(new[] { "Y", "Skip" }); }
                spec.Add(new[] { "B", "Close" });
            } else if (N<Grid>("SettingsLayer").Visibility == Visibility.Visible) { spec.Add(new[] { "A", "Select" }); spec.Add(new[] { "≡", "Save" }); spec.Add(new[] { "X", "Defaults" }); spec.Add(new[] { "B", "Cancel" }); }
            else if (N<Grid>("NotesLayer").Visibility == Visibility.Visible) { spec.Add(new[] { "LB", "Tabs" }); spec.Add(new[] { "B", "Close" }); }
            else if (PlayersOpen) { spec.Add(new[] { "A", plIdx < Profiles.All.Count ? "Play as" : plIdx == Profiles.All.Count ? "New profile" : "Select" }); spec.Add(new[] { "X", "Edit" }); spec.Add(new[] { "B", "Close" }); }
            else if (ProfileOpen) { spec.Add(new[] { "A", "Select" }); spec.Add(new[] { "B", "Close" }); }
            else if (view == View.Home) {
                // every button that does something here, as the other views list theirs
                bool install = sel != null && sel.State == State.Missing;
                if (homePage != "game") {
                    spec.Add(new[] { "RS", homeAll ? "Layout" : "All games" });
                    spec.Add(new[] { "LB", homeAll ? "Filter" : "Profiles" });
                    spec.Add(new[] { "≡", "Settings" });
                }
                if (sel != null && sel.State == State.Update && homeZone == "tiles" && homePage != "game") spec.Add(new[] { "X", "Update" });
                if (homePage == "game") spec.Add(new[] { "A", "OK" });
                else {
                    spec.Add(new[] { "Y", "Game page" });
                    spec.Add(new[] { "A", homeZone == "buttons" && !homeAll ? "OK" : install ? "Install" : "Start" });
                }
                if (homePage != "home" || homeZone == "buttons") spec.Add(new[] { "B", "Back" });
            }
            else {
                bool install = sel != null && sel.State == State.Missing;
                spec.Add(new[] { "A", view == View.Home && homeZone == "buttons" ? "OK" : zone == "cards" ? (install ? "Install" : "Play") : "Select" });
                if (sel != null && sel.State == State.Update) spec.Add(new[] { "X", "Update" });
                spec.Add(new[] { "Y", "What's new" });
                spec.Add(new[] { "RS", "View" });
                spec.Add(new[] { "LB", "Filter" });
                spec.Add(new[] { "≡", "Settings" });
                if (zone != "cards") spec.Add(new[] { "B", "Back" });
            }
        }
        string key = string.Join("|", spec.Select(s => s[0] + s[1]));
        if (key == hintsShown) return;
        hintsShown = key;
        var host = N<StackPanel>("PadHints"); host.Children.Clear();
        foreach (var s in spec) host.Children.Add(Hint(s[0], s[1]));
    }
    static UIElement Hint(string key, string label) {
        string color = key == "A" ? "#6CC04A" : key == "B" ? "#E0524B" : key == "X" ? "#3E8FE0" : key == "Y" ? "#E8B730" : "#8A9AA5";
        bool round = key.Length == 1;
        var badge = new Border { Width = round ? 24 : 32, Height = 24, CornerRadius = new CornerRadius(12), Background = Ui.Brush("#1C252C"),
            BorderBrush = Ui.Brush(color), BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = key, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0) };
        sp.Children.Add(badge);
        sp.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Ui.Brush("#A9B6BE"), Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }
}
}
