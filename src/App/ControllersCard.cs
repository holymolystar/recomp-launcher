// Controllers (on Home): which controllers are connected (Xbox ones by their slot, a PlayStation or Switch Pro
// one read directly), each with its battery; a drawing of the controller that lights up the buttons you press, to
// test them; and Pair a controller (Bluetooth.cs) and Controller settings. B closes it.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RecompLauncher {

partial class Shell {
    Grid ctLayer; StackPanel ctList; ControllerView ctView; TextBlock ctPressed; DispatcherTimer ctTimer; readonly List<Button> ctButtons = new List<Button>(); int ctIdx;
    bool ControllersOpen { get { return ctLayer != null && ctLayer.Visibility == Visibility.Visible; } }
    void OpenControllers() {
        if (ctLayer == null) {
            Border card; StackPanel body;
            ctLayer = Card("Ct", 700, VerticalAlignment.Center, out card, out body);
            body.Children.Add(new TextBlock { Text = "CONTROLLERS", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal) });
            body.Children.Add(new TextBlock { Text = "Connected", FontSize = 22, FontFamily = Ui.Semi, Margin = new Thickness(0, 2, 0, 10) });
            ctList = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            body.Children.Add(ctList);
            body.Children.Add(new TextBlock { Text = "Test: press the buttons and move the sticks; they light up here (B closes this page).", FontSize = 12, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap });
            ctView = new ControllerView { Height = 230, Margin = new Thickness(0, 8, 0, 4) };
            body.Children.Add(new Border { Background = Ui.Brush("#0D1215"), CornerRadius = new CornerRadius(14), Padding = new Thickness(8), Child = ctView });
            ctPressed = new TextBlock { FontSize = 12.5, Foreground = Ui.Brush(Ui.Text), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            body.Children.Add(ctPressed);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            foreach (var spec in new[] { "Pair a controller", "Controller settings", "Close" }) {
                var b = MakeButton(spec, 40); b.Margin = new Thickness(8, 0, 0, 0); string what = spec;
                b.Click += (s, e) => {
                    if (what == "Close") { CloseControllers(); return; }
                    CloseControllers();
                    if (what == "Pair a controller") OpenBluetooth(); else OpenSettings("Controller");
                };
                ctButtons.Add(b); bar.Children.Add(b);
            }
            body.Children.Add(bar);
            ((Rectangle)ctLayer.Children[0]).MouseLeftButtonUp += (s, e) => CloseControllers();
            ctTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            ctTimer.Tick += (s, e) => FillControllers();
        }
        FillControllers();
        ctIdx = 0; MarkControllers();
        ShowCard("CtLayer", "CtCard");
        ctTimer.Start();
    }
    void CloseControllers() { if (!ControllersOpen) return; ctTimer.Stop(); Sound.Play(Sfx.Back); HideCard("CtLayer", "CtCard"); }
    void FillControllers() {
        ctList.Children.Clear();
        int n = 0;
        for (int i = 0; i < 4; i++) {
            int bat;
            if (!Pad.Slot(i, out bat)) continue;
            n++;
            ctList.Children.Add(ControllerRow("Xbox controller", "Slot " + (i + 1), bat, false));
        }
        int hp; bool chg;
        var kind = HidPads.Plugged();
        if (kind == PadKind.PlayStation || kind == PadKind.Switch) {
            n++;
            bool hasBat = HidPads.Battery(out hp, out chg);
            ctList.Children.Add(ControllerRow(kind == PadKind.PlayStation ? "PlayStation controller" : "Switch Pro controller", "Read directly", hasBat ? hp : -1, chg));
        }
        if (n == 0) ctList.Children.Add(new TextBlock { Text = "No controller is connected. Plug one in, or pair one by Bluetooth.", FontSize = 13.5, Foreground = Ui.Brush(Ui.Muted) });
        ctView.Kind = padKind != PadKind.None ? padKind : PadKind.Xbox; ctView.Connected = n > 0; ctView.InvalidateVisual();
    }
    FrameworkElement ControllerRow(string name, string where, int battery, bool charging) {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 22, VerticalAlignment = VerticalAlignment.Center });
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(new TextBlock { Text = name, FontSize = 14.5, FontFamily = Ui.Semi });
        t.Children.Add(new TextBlock { Text = where, FontSize = 12, Foreground = Ui.Brush(Ui.Muted) });
        Grid.SetColumn(t, 1); g.Children.Add(t);
        string bt = battery < 0 ? "Wired" : charging ? "Charging" : battery + "%";
        var bat = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 13.5, Foreground = Ui.Brush(battery >= 0 && battery <= 25 && !charging ? "#e8453c" : Ui.Text) };
        if (battery >= 0) bat.Inlines.Add(new System.Windows.Documents.Run(((char)((charging ? 0xE85A : 0xE850) + Math.Min(9, battery / 10))) + "  ") { FontFamily = Ui.Mdl });
        bat.Inlines.Add(new System.Windows.Documents.Run(bt));
        Grid.SetColumn(bat, 2); g.Children.Add(bat);
        return g;
    }
    // every reading while the page is open: the drawing lights up what's pressed
    void ControllersShow(int b) {
        if (!ControllersOpen) return;
        ctView.Show(b);
        var names = new List<string>();
        foreach (var kv in new[] { Tuple.Create(Pad.A, "A"), Tuple.Create(Pad.B, "B"), Tuple.Create(Pad.X, "X"), Tuple.Create(Pad.Y, "Y"), Tuple.Create(Pad.LB, "LB"), Tuple.Create(Pad.RB, "RB"),
            Tuple.Create(Pad.LT, "LT"), Tuple.Create(Pad.RT, "RT"), Tuple.Create(Pad.Back, "View"), Tuple.Create(Pad.Start, "Menu"), Tuple.Create(Pad.LS, "LS"), Tuple.Create(Pad.RS, "RS"),
            Tuple.Create(Pad.Up, "Up"), Tuple.Create(Pad.Down, "Down"), Tuple.Create(Pad.Left, "Left"), Tuple.Create(Pad.Right, "Right"), Tuple.Create(Pad.Guide, "Home") })
            if ((b & kv.Item1) != 0) names.Add(kv.Item2);
        ctPressed.Text = names.Count > 0 ? "Pressed: " + string.Join("  ", names) : " ";
    }
    void MarkControllers() { for (int i = 0; i < ctButtons.Count; i++) ctButtons[i].Effect = padUsed && i == ctIdx ? SetGlow : null; }
    // only B (close) and, on the buttons, left / right / A act: everything else is a test press
    void ControllersPad(int bit) {
        if (bit == Pad.B) { CloseControllers(); return; }
        if (bit == Pad.A) { Press(ctButtons[ctIdx]); return; }
        int was = ctIdx;
        if (bit == Pad.Left) ctIdx = Math.Max(0, ctIdx - 1);
        if (bit == Pad.Right) ctIdx = Math.Min(ctButtons.Count - 1, ctIdx + 1);
        if (ctIdx != was) MarkControllers();
    }
}
}
