// Power on Home (the power button): Close the launcher, Sleep, Restart, Shut down, like a console's power menu.
// Sleep, Restart and Shut down ask first (press again). Restart and Shut down end any game that's still running.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace RecompLauncher {

partial class Shell {
    Grid pwLayer; TextBlock pwNote; readonly List<Button> pwButtons = new List<Button>(); int pwIdx, pwArmed = -1;
    static readonly string[][] PowerChoices = { new[] { "", "Close the launcher" }, new[] { "", "Sleep" }, new[] { "", "Restart" }, new[] { "", "Shut down" } };
    bool PowerOpen { get { return pwLayer != null && pwLayer.Visibility == Visibility.Visible; } }
    void OpenPower() {
        if (pwLayer == null) {
            Border card; StackPanel body;
            pwLayer = Card("Pw", 420, VerticalAlignment.Center, out card, out body);
            body.Children.Add(new TextBlock { Text = "POWER", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal), Margin = new Thickness(0, 0, 0, 14) });
            for (int i = 0; i < PowerChoices.Length; i++) {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock { Text = PowerChoices[i][0], FontFamily = Ui.Mdl, FontSize = 18, Width = 34, VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(new TextBlock { Text = PowerChoices[i][1], FontSize = 16, FontFamily = Ui.Semi, VerticalAlignment = VerticalAlignment.Center });
                var b = new Button { Style = (Style)W.FindResource("Btn"), Content = row, Height = 54, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(20, 0, 20, 0), Margin = new Thickness(0, 0, 0, 10) };
                int idx = i;
                b.Click += (s, e) => { pwIdx = idx; PowerChoose(); };
                pwButtons.Add(b); body.Children.Add(b);
            }
            pwNote = new TextBlock { FontSize = 12, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 0, 0) };
            body.Children.Add(pwNote);
            ((Rectangle)pwLayer.Children[0]).MouseLeftButtonUp += (s, e) => ClosePower();
        }
        pwIdx = 0; pwArmed = -1; pwNote.Text = "";
        for (int i = 0; i < pwButtons.Count; i++) PowerText(i, PowerChoices[i][1]);
        MarkPower();
        Sound.Play(Sfx.Open);
        ShowCard("PwLayer", "PwCard");
    }
    void PowerText(int i, string t) { ((TextBlock)((StackPanel)pwButtons[i].Content).Children[1]).Text = t; }
    void MarkPower() {
        for (int i = 0; i < pwButtons.Count; i++) {
            bool on = i == pwIdx;
            pwButtons[i].Background = on ? Ui.Brush(Ui.Teal) : Ui.Brush("#1C252C");
            pwButtons[i].Foreground = on ? Ui.Brush(Theme.Current.Light ? "#ffffff" : "#0A0E11") : Ui.Brush(Ui.Text);
        }
    }
    void ClosePower() { if (!PowerOpen) return; Sound.Play(Sfx.Back); HideCard("PwLayer", "PwCard"); }
    void PowerPad(int bit) {
        if (bit == Pad.B) { ClosePower(); return; }
        if (bit == Pad.A) { PowerChoose(); return; }
        int was = pwIdx;
        if (bit == Pad.Up) pwIdx = Math.Max(0, pwIdx - 1);
        if (bit == Pad.Down) pwIdx = Math.Min(pwButtons.Count - 1, pwIdx + 1);
        if (pwIdx != was) {
            if (pwArmed >= 0) { PowerText(pwArmed, PowerChoices[pwArmed][1]); pwArmed = -1; pwNote.Text = ""; }
            Sound.Play(Sfx.Move); MarkPower();
        } else if (bit == Pad.Up || bit == Pad.Down) Sound.Play(Sfx.Bump);
    }
    void PowerChoose() {
        if (pwIdx == 0) { ClosePower(); W.Close(); return; }
        if (pwArmed != pwIdx) {
            if (pwArmed >= 0) PowerText(pwArmed, PowerChoices[pwArmed][1]);
            pwArmed = pwIdx;
            PowerText(pwIdx, "Press again to " + PowerChoices[pwIdx][1].ToLowerInvariant());
            pwNote.Text = pwIdx == 1 ? "The PC goes to sleep; anything open stays as it is." : playing != null ? playing.Name + " is still running: it closes without saving." : "Everything open closes.";
            Sound.Play(Sfx.Bump); MarkPower();
            return;
        }
        int what = pwIdx;
        HideCard("PwLayer", "PwCard");
        Sound.Play(Sfx.Back);
        try {
            if (what == 1) SetSuspendState(false, false, false);
            else Process.Start(new ProcessStartInfo("shutdown", what == 2 ? "/r /t 0" : "/s /t 0") { CreateNoWindow = true, UseShellExecute = false });
        } catch { }
    }
}
}
