// The controller as a mouse, for the windows that only work with one: the setup and launcher programs some ports
// come with (Hookshot's builder, WarioLauncher, KarPur's launcher...: games.json "Helpers"). While one of those is in
// front the controller drives the pointer by itself, and a small note in the corner says how; anything else can be
// driven the same way from the in-game menu (Quick settings > Controller as mouse).
//   left stick: the pointer   A: click (hold to drag)   X: right click   right stick: scroll
//   d-pad: arrow keys   LB / RB: previous / next field (Shift+Tab / Tab)   Menu: Enter   B: Escape
//   Y: the on-screen keyboard   View + Menu: the in-game menu
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace RecompLauncher {

partial class Shell {
    bool mouseMode;   // switched on from the in-game menu, for whatever is in front (off when the game closes)
    DateTime helperCheckedAt = DateTime.MinValue; bool helperFront; int mousePrev; bool mouseLeft; double mScroll, mHScroll;
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

    // is one of the ports' own setup or launcher windows in front? (looked at four times a second)
    bool HelperInFront() {
        if ((DateTime.Now - helperCheckedAt).TotalMilliseconds < 250) return helperFront;
        helperCheckedAt = DateTime.Now; helperFront = false;
        try {
            uint pid; GetWindowThreadProcessId2(GetForegroundWindow(), out pid);
            if (pid == 0 || pid == (uint)Process.GetCurrentProcess().Id) return false;
            string name = Process.GetProcessById((int)pid).ProcessName;
            foreach (var g in Games) {
                if (string.IsNullOrEmpty(g.Helpers)) continue;
                foreach (var h in g.Helpers.Split(';')) {
                    string p = h.Trim(); if (p.Length == 0) continue;
                    if (p.EndsWith("*") ? name.StartsWith(p.TrimEnd('*'), StringComparison.OrdinalIgnoreCase) : name.Equals(p, StringComparison.OrdinalIgnoreCase)) { helperFront = true; return true; }
                }
            }
        } catch { }
        return false;
    }
    // something other than the launcher is in front (for the in-game menu's switch)
    bool OtherInFront() {
        try { uint pid; GetWindowThreadProcessId2(GetForegroundWindow(), out pid); return pid != 0 && pid != (uint)Process.GetCurrentProcess().Id; } catch { return false; }
    }
    void ToggleMouseMode() {
        mouseMode = !mouseMode;
        Sound.Play(mouseMode ? Sfx.ToggleOn : Sfx.ToggleOff);
        ovNote.Text = mouseMode ? "The controller is a mouse now: the left stick moves the pointer, A clicks. View + Menu, then this again, turns it off." : "The controller is a controller again.";
        UpdateQuick();
    }

    // one controller reading while the controller is a mouse (from PadTick, 60 times a second)
    void MousePad(Pad.State st, int b) {
        int pressed = b & ~mousePrev, released = mousePrev & ~b;
        mousePrev = b;
        double lx = st.LX / 32767.0, ly = st.LY / 32767.0, mag = Math.Sqrt(lx * lx + ly * ly);
        if (mag > 0.2) {
            double k = Math.Pow((mag - 0.2) / 0.8, 1.8) * 24 / mag;
            Input.POINT p; Input.GetCursorPos(out p);
            Input.SetCursorPos(p.X + (int)Math.Round(lx * k), p.Y - (int)Math.Round(ly * k));
        }
        double ry = st.RY / 32767.0, rx = st.RX / 32767.0;
        if (Math.Abs(ry) > 0.25) { mScroll += Math.Sign(ry) * Math.Pow((Math.Abs(ry) - 0.25) / 0.75, 1.5) * 40; int n = (int)mScroll; if (n != 0) { Input.Mouse(Input.Wheel, n); mScroll -= n; } } else mScroll = 0;
        if (Math.Abs(rx) > 0.35) { mHScroll += Math.Sign(rx) * Math.Pow((Math.Abs(rx) - 0.35) / 0.65, 1.5) * 40; int n = (int)mHScroll; if (n != 0) { Input.Mouse(Input.HWheel, n); mHScroll -= n; } } else mHScroll = 0;
        if ((pressed & Pad.A) != 0) { Input.Mouse(Input.LeftDown); mouseLeft = true; }
        if ((released & Pad.A) != 0 && mouseLeft) { Input.Mouse(Input.LeftUp); mouseLeft = false; }
        if ((pressed & Pad.X) != 0) { Input.Mouse(Input.RightDown); Input.Mouse(Input.RightUp); }
        if ((pressed & Pad.Y) != 0) Input.Keyboard();
        if ((pressed & Pad.B) != 0) Input.Chord(0x1B);                       // Escape
        if ((pressed & Pad.Start) != 0 && (b & Pad.Back) == 0) Input.Chord(0x0D);   // Enter
        if ((pressed & Pad.LB) != 0) Input.Chord(Input.Shift, Input.Tab);
        if ((pressed & Pad.RB) != 0) Input.Chord(Input.Tab);
        foreach (var m in new[] { new[] { Pad.Up, Input.Up }, new[] { Pad.Down, Input.Down }, new[] { Pad.Left, Input.Left }, new[] { Pad.Right, Input.Right } }) {
            if ((pressed & m[0]) != 0) Input.Key((byte)m[1], false);
            if ((released & m[0]) != 0) Input.Key((byte)m[1], true);
        }
    }
    void MouseOff() {
        if (mouseLeft) { Input.Mouse(Input.LeftUp); mouseLeft = false; }
        mousePrev = 0;
    }

    // ---------- the note in the corner: how the controller works as a mouse ----------
    Window mouseHint;
    void MouseHint(bool on) {
        if (!on) { if (mouseHint != null && mouseHint.IsVisible) mouseHint.Hide(); return; }
        if (mouseHint == null) {
            var text = new TextBlock { FontSize = 14, Foreground = Brushes.White, LineHeight = 21 };
            text.Inlines.Add(new System.Windows.Documents.Run("Controller as mouse") { FontWeight = FontWeights.SemiBold });
            text.Inlines.Add(new System.Windows.Documents.LineBreak());
            text.Inlines.Add(new System.Windows.Documents.Run("Left stick: pointer   A: click   X: right click\nRight stick: scroll   Y: keyboard   B: Esc   Menu: Enter") { Foreground = Ui.Brush("#cfd7dd") });
            mouseHint = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false,
                ShowActivated = false, Focusable = false, ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight,
                Content = new Border { Background = Ui.Brush("#E0111820"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 10, 16, 12), Child = text } };
            // it never takes the focus or the clicks
            mouseHint.SourceInitialized += (s, e) => {
                var h = new WindowInteropHelper(mouseHint).Handle;
                SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x08000000 | 0x80 | 0x20);   // no activate, tool window, click-through
            };
        }
        if (!mouseHint.IsVisible) {
            mouseHint.Show();
            // the bottom right of the screen the window in front is on
            try {
                var scr = System.Windows.Forms.Screen.FromHandle(GetForegroundWindow()).WorkingArea;
                var src = PresentationSource.FromVisual(mouseHint);
                var m = src != null ? src.CompositionTarget.TransformFromDevice : Matrix.Identity;
                var br = m.Transform(new Point(scr.Right, scr.Bottom));
                mouseHint.Left = br.X - mouseHint.ActualWidth - 24; mouseHint.Top = br.Y - mouseHint.ActualHeight - 24;
            } catch { }
        }
    }
}
}
