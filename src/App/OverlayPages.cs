// The in-game menu's two extra pages:
//  - Quick settings: the volume (left / right, A mutes), which output plays the sound (A or left / right switches
//    between headphones, the TV, speakers...), and Bluetooth (opens Windows' Bluetooth page to pair a controller or
//    headset).
//  - Power: Sleep, Restart, Shut down. Each asks first (press again); Restart and Shut down close the game without
//    saving, so the menu says so.
// B goes back to the menu's first page.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RecompLauncher {

partial class Shell {
    StackPanel ovList, ovHints; string ovPage = "main";
    readonly List<Button> ovQuick = new List<Button>(), ovPower = new List<Button>();
    TextBlock ovMouseText;
    TextBlock ovVolText, ovOutText; Border ovVolBar; int ovPowerStep = -1;
    [DllImport("powrprof.dll")] static extern bool SetSuspendState(bool hibernate, bool force, bool wakeOff);

    List<Button> OvRows() { return ovPage == "quick" ? ovQuick : ovPage == "power" ? ovPower : ovButtons; }
    Button OvRow(string glyph, string text, List<Button> into, out TextBlock label) {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.Mdl, FontSize = 18, Width = 34, VerticalAlignment = VerticalAlignment.Center });
        label = new TextBlock { Text = text, FontSize = 16, FontFamily = Ui.Semi, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(label);
        var b = new Button { Style = (Style)W.FindResource("Btn"), Content = row, Height = 58, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(20, 0, 20, 0), Margin = new Thickness(0, 0, 0, 10) };
        int idx = into.Count;
        b.Click += (s, e) => { ovIdx = idx; OverlayChoose(); };
        into.Add(b);
        return b;
    }
    void BuildOverlayPages() {
        TextBlock t;
        // Quick settings
        var vol = OvRow("", "Volume", ovQuick, out ovVolText);
        var volRow = (StackPanel)vol.Content;
        var track = new Grid { Width = 120, Height = 6, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Ui.Brush("#26333C") });
        ovVolBar = new Border { CornerRadius = new CornerRadius(3), Background = Ui.Brush("#f2c230"), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        track.Children.Add(ovVolBar);
        volRow.Children.Add(track);
        OvRow("", "Sound", ovQuick, out ovOutText);
        OvRow("", "Bluetooth devices", ovQuick, out t);
        OvRow("\uE962", "Controller as mouse: Off", ovQuick, out ovMouseText);   // PadMouse.cs
        OvRow("", "Back", ovQuick, out t);
        // Power
        OvRow("", "Sleep", ovPower, out t);
        OvRow("", "Restart", ovPower, out t);
        OvRow("", "Shut down", ovPower, out t);
        OvRow("", "Back", ovPower, out t);
    }
    void ShowOvPage(string page) {
        if (ovQuick.Count == 0) BuildOverlayPages();
        ovPage = page; ovIdx = 0; ovPowerStep = -1; ovNote.Text = "";
        ovList.Children.Clear();
        foreach (var b in OvRows()) ovList.Children.Add(b);
        // the hints say what B does here: resume the game from the first page, back from the others
        ovHints.Children.Clear();
        if (page == "quick") ovHints.Children.Add(Hint("↔", "Change"));
        ovHints.Children.Add(Hint("A", "Select")); ovHints.Children.Add(Hint("B", page == "main" ? "Resume" : "Back"));
        if (page == "quick") UpdateQuick();
        if (page == "power") for (int i = 0; i < 3; i++) PowerLabel(i, new[] { "Sleep", "Restart", "Shut down" }[i]);
        MarkOverlay();
    }
    void PowerLabel(int i, string text) { ((TextBlock)((StackPanel)ovPower[i].Content).Children[1]).Text = text; }
    void UpdateQuick() {
        if (ovMouseText != null) ovMouseText.Text = "Controller as mouse: " + (mouseMode || HelperInFront() ? "On" : "Off");
        int v = Audio.Level; bool m = Audio.Muted;
        ovVolText.Text = v < 0 ? "Volume: no output" : m ? "Volume: muted" : "Volume  " + v + "%";
        ovVolBar.Width = v < 0 || m ? 0 : 1.2 * v;
        string cur; var outs = Audio.Outputs(out cur);
        var o = outs.FirstOrDefault(x => x.Id == cur);
        ovOutText.Text = o != null ? o.Name : "Sound: no output";
        ovOutText.TextTrimming = TextTrimming.CharacterEllipsis; ovOutText.MaxWidth = 300;
    }
    void CycleOutput(int dir) {
        string cur; var outs = Audio.Outputs(out cur);
        if (outs.Count < 2) { Sound.Play(Sfx.Bump); ovNote.Text = "Only one sound output is plugged in."; return; }
        int i = Math.Max(0, outs.FindIndex(x => x.Id == cur));
        var next = outs[(i + dir + outs.Count) % outs.Count];
        if (Audio.Use(next.Id)) { Sound.Play(Sfx.Select); ovNote.Text = "The sound plays on " + next.Name + " now."; }
        else { Sound.Play(Sfx.Bump); ovNote.Text = "Windows didn't switch the sound output."; }
        UpdateQuick();
    }
    // a button on Quick settings or Power; true when handled
    bool OverlayPagePad(int bit) {
        if (ovPage == "main") return false;
        if (bit == Pad.B) { Sound.Play(Sfx.Back); ShowOvPage("main"); return true; }
        if (ovPage == "quick" && (bit == Pad.Left || bit == Pad.Right)) {
            int d = bit == Pad.Right ? 1 : -1;
            if (ovIdx == 0) { int v = Audio.Level; if (v >= 0) { Audio.SetMuted(false); Audio.SetLevel((v / 5 + d) * 5); Sound.Play(Sfx.Move); UpdateQuick(); } }
            else if (ovIdx == 1) CycleOutput(d);
            return true;
        }
        if (bit == Pad.Up || bit == Pad.Down) {
            int was = ovIdx;
            ovIdx = Math.Max(0, Math.Min(OvRows().Count - 1, ovIdx + (bit == Pad.Down ? 1 : -1)));
            if (ovPage == "power" && ovIdx != was && ovPowerStep >= 0) { PowerLabel(ovPowerStep, new[] { "Sleep", "Restart", "Shut down" }[ovPowerStep]); ovPowerStep = -1; ovNote.Text = ""; }
            Sound.Play(ovIdx != was ? Sfx.Move : Sfx.Bump); MarkOverlay();
            return true;
        }
        if (bit == Pad.A) { OverlayPageChoose(); return true; }
        return true;
    }
    void OverlayPageChoose() {
        if (ovPage == "quick") {
            if (ovIdx == 0) { Audio.SetMuted(!Audio.Muted); Sound.Play(Sfx.Select); UpdateQuick(); }
            else if (ovIdx == 1) CycleOutput(1);
            else if (ovIdx == 2) {
                // pairing is a list in the launcher (Bluetooth.cs): it comes up over the game for it
                CloseOverlay(false);
                if (inTray) ComeBack(null); else { W.Show(); W.Activate(); BringToFront(new System.Windows.Interop.WindowInteropHelper(W).Handle); }
                OpenBluetooth();
            }
            else if (ovIdx == 3) ToggleMouseMode();
            else { Sound.Play(Sfx.Back); ShowOvPage("main"); }
            return;
        }
        if (ovIdx == 3) { Sound.Play(Sfx.Back); ShowOvPage("main"); return; }
        string[] names = { "Sleep", "Restart", "Shut down" };
        if (ovPowerStep != ovIdx) {
            if (ovPowerStep >= 0) PowerLabel(ovPowerStep, names[ovPowerStep]);
            ovPowerStep = ovIdx;
            PowerLabel(ovIdx, "Press again to " + names[ovIdx].ToLowerInvariant());
            ovNote.Text = ovIdx == 0 ? "The game waits while the PC sleeps." : "The game closes without saving: save first.";
            Sound.Play(Sfx.Bump);
            return;
        }
        Sound.Play(Sfx.Back);
        int what = ovIdx;
        CloseOverlay(false);
        try {
            if (what == 0) SetSuspendState(false, false, false);
            else Process.Start(new ProcessStartInfo("shutdown", what == 1 ? "/r /t 0" : "/s /t 0") { CreateNoWindow = true, UseShellExecute = false });
        } catch { }
    }
}
}
