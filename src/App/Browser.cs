// The controller browser: Browser on Home opens a card of four shortcuts (YouTube's TV view, made for controllers,
// Google, Twitch, Spotify; X changes one to a site of your own) that open in your browser (Settings > General >
// Browser: Edge, Chrome, Firefox or Brave, whichever are installed), maximised. While a browser window is in front,
// the controller works it like a mouse and keyboard:
//   left stick: the pointer (push further to go faster)   right stick: scroll   d-pad: arrow keys
//   A: click (hold to drag)   X: right-click   B: back   Y: the on-screen keyboard
//   LB / RB: previous / next tab   Menu: the address bar (with the keyboard)   View: full screen
//   Xbox / PS / Home button, or View + Menu: back to the launcher
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace RecompLauncher {

static class Input {
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(int flags, int dx, int dy, int data, IntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, int flags, IntPtr extra);
    public const int LeftDown = 0x2, LeftUp = 0x4, RightDown = 0x8, RightUp = 0x10, Wheel = 0x800, HWheel = 0x1000;
    public static void Mouse(int flags, int data = 0) { mouse_event(flags, 0, 0, data, IntPtr.Zero); }
    static readonly HashSet<byte> extended = new HashSet<byte> { 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x24, 0x23, 0x21, 0x22 };
    public static void Key(byte vk, bool up) { keybd_event(vk, 0, (extended.Contains(vk) ? 1 : 0) | (up ? 2 : 0), IntPtr.Zero); }
    // a shortcut: the modifiers down, the key, then all up again
    public static void Chord(params byte[] keys) {
        foreach (var k in keys) Key(k, false);
        for (int i = keys.Length - 1; i >= 0; i--) Key(keys[i], true);
    }
    public const byte Ctrl = 0x11, Shift = 0x10, Alt = 0x12, Tab = 0x09, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28, L = 0x4C, F11 = 0x7A;

    // the on-screen (touch) keyboard: shown through Windows' own switch, else the classic On-Screen Keyboard
    [ComImport, Guid("37c994e7-432b-4834-a2f7-dce1f13b834b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ITipInvocation { void Toggle(IntPtr hwnd); }
    [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
    public static void Keyboard() {
        try {
            if (Process.GetProcessesByName("TabTip").Length == 0) {
                string tip = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), @"microsoft shared\ink\TabTip.exe");
                if (File.Exists(tip)) { Process.Start(tip); System.Threading.Thread.Sleep(400); }
            }
            var t = (ITipInvocation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("4ce576fa-83dc-4f88-951c-9d0782b4e376")));
            t.Toggle(GetDesktopWindow());
            Marshal.ReleaseComObject(t);
        } catch { try { Process.Start("osk.exe"); } catch { } }
    }
}

partial class Shell {
    bool browserMode; int browserPrev; bool leftHeld; DateTime fgCheckedAt; bool fgIsEdge;
    double scrollRest, hscrollRest;
    // the four shortcuts: yours (Settings "BrowserPicks": name|address lines) where set, else these
    static readonly string[][] DefaultPicks = {
        new[] { "YouTube", "https://www.youtube.com/tv" },
        new[] { "Google", "https://www.google.com" },
        new[] { "Twitch", "https://www.twitch.tv" },
        new[] { "Spotify", "https://open.spotify.com" },
    };
    static string[][] BrowserPicks() {
        var mine = Settings.Get("BrowserPicks").Split(new[] { '\n' }, StringSplitOptions.None);
        var r = new string[DefaultPicks.Length][];
        for (int i = 0; i < r.Length; i++) {
            var parts = i < mine.Length ? mine[i].Split('|') : new string[0];
            r[i] = parts.Length == 2 && parts[0].Trim().Length > 0 && parts[1].Trim().Length > 0 ? new[] { parts[0].Trim(), parts[1].Trim() } : DefaultPicks[i];
        }
        return r;
    }
    static void SavePick(int i, string name, string url) {
        var all = BrowserPicks();
        all[i] = string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url) ? DefaultPicks[i] : new[] { name.Trim().Replace("|", " "), url.Trim() };
        Settings.Set("BrowserPicks", string.Join("\n", all.Select((p, k) => p[0] == DefaultPicks[k][0] && p[1] == DefaultPicks[k][1] ? "" : p[0] + "|" + p[1])));
        Settings.Save();
    }
    Grid brLayer; readonly List<Button> brButtons = new List<Button>(); int brIdx; System.Windows.Controls.Primitives.UniformGrid brGrid;
    bool BrowserCardOpen { get { return brLayer != null && brLayer.Visibility == Visibility.Visible; } }

    // the browsers the controller can work (their program names), and where each is installed
    static readonly string[] BrowserApps = { "msedge", "chrome", "firefox", "brave", "opera", "vivaldi" };
    public static List<string[]> InstalledBrowsers() {
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var known = new[] {
            new[] { "edge", "Edge", @"Microsoft\Edge\Application\msedge.exe" }, new[] { "chrome", "Chrome", @"Google\Chrome\Application\chrome.exe" },
            new[] { "firefox", "Firefox", @"Mozilla Firefox\firefox.exe" }, new[] { "brave", "Brave", @"BraveSoftware\Brave-Browser\Application\brave.exe" } };
        var r = new List<string[]>();
        foreach (var k in known)
            foreach (var root in new[] { pf, pf86, local }) {
                string f = Path.Combine(root, k[2]);
                if (File.Exists(f)) { r.Add(new[] { k[0], k[1], f }); break; }
            }
        return r;
    }
    // the browser picked in Settings > General (Browser), else the first one installed
    static string[] PickedBrowser() {
        var all = InstalledBrowsers();
        return all.FirstOrDefault(b => b[0] == Settings.Get("Browser")) ?? all.FirstOrDefault();
    }
    void OpenBrowserCard() {
        if (brLayer == null) {
            Border card; StackPanel body;
            brLayer = Card("Br", 640, VerticalAlignment.Center, out card, out body);
            body.Children.Add(new TextBlock { Text = "BROWSER", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal) });
            body.Children.Add(new TextBlock { Text = "Where to?", FontSize = 22, FontFamily = Ui.Semi, Margin = new Thickness(0, 2, 0, 12) });
            brGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
            body.Children.Add(brGrid);
            body.Children.Add(new TextBlock { FontSize = 11.5, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0),
                Text = "X (or a right-click) changes a shortcut to a site of your own. In the browser: left stick moves the pointer, A clicks, X right-clicks, right stick scrolls, B goes back, Y opens the keyboard, LB / RB change tabs, Menu goes to the address bar, View is full screen. The Xbox / PS / Home button (or View + Menu) comes back here." });
            var close = MakeButton("Close", 40); close.HorizontalAlignment = HorizontalAlignment.Right; close.Margin = new Thickness(0, 14, 0, 0);
            close.Click += (s, e) => CloseBrowserCard();
            body.Children.Add(close);
            ((Rectangle)brLayer.Children[0]).MouseLeftButtonUp += (s, e) => CloseBrowserCard();
        }
        brIdx = 0; FillBrowserCard();
        Sound.Play(Sfx.Open);
        ShowCard("BrLayer", "BrCard");
    }
    void FillBrowserCard() {
        brGrid.Children.Clear(); brButtons.Clear();
        var picks = BrowserPicks();
        for (int i = 0; i < picks.Length; i++) {
            var p = picks[i]; int idx = i;
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = p[0].Substring(0, 1).ToUpperInvariant(), FontSize = 30, FontFamily = Ui.Semi, Foreground = Ui.Brush(Ui.Teal), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) });
            sp.Children.Add(new TextBlock { Text = p[0], FontSize = 14, FontFamily = Ui.Semi, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 120 });
            var b = new Button { Style = (Style)W.FindResource("Btn"), Content = sp, Height = 104, Margin = new Thickness(0, 0, 10, 0), ToolTip = p[1] };
            string url = p[1];
            b.Click += (s, e) => StartBrowser(url);
            b.MouseRightButtonUp += (s, e) => EditPick(idx);
            brButtons.Add(b); brGrid.Children.Add(b);
        }
        MarkBrowserCard();
    }
    // change a shortcut: its name, then its address (an empty name puts the launcher's own back)
    void EditPick(int i) {
        var cur = BrowserPicks()[i];
        OpenOsk("Shortcut " + (i + 1) + ": its name (empty: back to " + DefaultPicks[i][0] + ")", cur[0], null, name => {
            name = (name ?? "").Trim();
            if (name.Length == 0) { SavePick(i, null, null); FillBrowserCard(); return; }
            OpenOsk(name + ": its address", cur[1], null, url => {
                url = (url ?? "").Trim();
                if (url.Length > 0 && !url.Contains("://")) url = "https://" + url;
                SavePick(i, name, url); FillBrowserCard();
            }, null);
        }, null);
    }
    void MarkBrowserCard() { for (int i = 0; i < brButtons.Count; i++) brButtons[i].Effect = i == brIdx && padUsed ? SetGlow : null; }
    void CloseBrowserCard() { if (!BrowserCardOpen) return; Sound.Play(Sfx.Back); HideCard("BrLayer", "BrCard"); }
    void BrowserCardPad(int bit) {
        if (bit == Pad.B) { CloseBrowserCard(); return; }
        if (bit == Pad.A) { StartBrowser(BrowserPicks()[brIdx][1]); return; }
        if (bit == Pad.X) { EditPick(brIdx); return; }
        int was = brIdx;
        if (bit == Pad.Left) brIdx = Math.Max(0, brIdx - 1);
        if (bit == Pad.Right) brIdx = Math.Min(brButtons.Count - 1, brIdx + 1);
        if (brIdx != was) { Sound.Play(Sfx.Move); MarkBrowserCard(); }
    }
    void StartBrowser(string url) {
        if (WebViewAvailable()) { HideCard("BrLayer", "BrCard"); OpenBrowserView(url); return; }   // inside the launcher (BrowserView.cs)
        var br = PickedBrowser();
        HideCard("BrLayer", "BrCard");
        Sound.Play(Sfx.Launch);
        try {
            if (br == null) Process.Start(url);   // none of the known ones: Windows' default browser
            else if (br[0] == "firefox") Process.Start(br[2], "-new-window \"" + url + "\"");
            else Process.Start(br[2], "--new-window --start-maximized \"" + url + "\"");
        } catch (Exception e) { SetStep("The browser didn't open: " + e.Message, Ui.Red); return; }
        browserMode = true; browserPrev = 0;
        SetStep("The controller works the browser now: left stick points, A clicks, B goes back. The Xbox / PS / Home button comes back here.", Ui.Teal);
    }

    // is a browser the window in front? (asked at most 4 times a second)
    bool EdgeInFront() {
        if ((DateTime.Now - fgCheckedAt).TotalMilliseconds < 250) return fgIsEdge;
        fgCheckedAt = DateTime.Now; fgIsEdge = false;
        try {
            uint pid; GetWindowThreadProcessId2(GetForegroundWindow(), out pid);
            if (pid != 0) { string name = Process.GetProcessById((int)pid).ProcessName; fgIsEdge = BrowserApps.Any(b => b.Equals(name, StringComparison.OrdinalIgnoreCase)); }
        } catch { }
        return fgIsEdge;
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] static extern uint GetWindowThreadProcessId2(IntPtr h, out uint pid);

    // one controller reading while Edge is in front (from PadTick, 60 times a second)
    void BrowserPad(Pad.State st, int b) {
        int pressed = b & ~browserPrev, released = browserPrev & ~b;
        browserPrev = b;
        // the pointer: speed grows with how far the stick is pushed
        double lx = st.LX / 32767.0, ly = st.LY / 32767.0;
        double mag = Math.Sqrt(lx * lx + ly * ly);
        if (mag > 0.22) {
            double k = Math.Pow((mag - 0.22) / 0.78, 1.8) * 26 / mag;
            Input.POINT p; Input.GetCursorPos(out p);
            Input.SetCursorPos(p.X + (int)Math.Round(lx * k), p.Y - (int)Math.Round(ly * k));
        }
        // scrolling, smooth: the wheel in small steps
        double ry = st.RY / 32767.0, rx = st.RX / 32767.0;
        if (Math.Abs(ry) > 0.25) { scrollRest += Math.Sign(ry) * Math.Pow((Math.Abs(ry) - 0.25) / 0.75, 1.5) * 40; int n = (int)scrollRest; if (n != 0) { Input.Mouse(Input.Wheel, n); scrollRest -= n; } } else scrollRest = 0;
        if (Math.Abs(rx) > 0.35) { hscrollRest += Math.Sign(rx) * Math.Pow((Math.Abs(rx) - 0.35) / 0.65, 1.5) * 40; int n = (int)hscrollRest; if (n != 0) { Input.Mouse(Input.HWheel, n); hscrollRest -= n; } } else hscrollRest = 0;
        // A: the left button, held as long as A is (dragging works)
        if ((pressed & Pad.A) != 0) { Input.Mouse(Input.LeftDown); leftHeld = true; }
        if ((released & Pad.A) != 0 && leftHeld) { Input.Mouse(Input.LeftUp); leftHeld = false; }
        if ((pressed & Pad.X) != 0) { Input.Mouse(Input.RightDown); Input.Mouse(Input.RightUp); }
        if ((pressed & Pad.B) != 0) { if (BrowserViewOpen) BrowserBack(); else Input.Chord(Input.Alt, Input.Left); }
        if ((pressed & Pad.Y) != 0) Input.Keyboard();
        bool inside = BrowserViewOpen;   // the browser inside the launcher: no tabs or address bar of its own
        if ((pressed & Pad.LB) != 0) { if (inside) BrowserBack(); else Input.Chord(Input.Ctrl, Input.Shift, Input.Tab); }
        if ((pressed & Pad.RB) != 0) { if (inside) BrowserForward(); else Input.Chord(Input.Ctrl, Input.Tab); }
        if ((pressed & Pad.Start) != 0 && (b & Pad.Back) == 0) { if (inside) AskAddress(); else { Input.Chord(Input.Ctrl, Input.L); Input.Keyboard(); } }
        if ((released & Pad.Back) != 0 && (browserPrev & Pad.Start) == 0 && (pressed & Pad.Start) == 0) { if (inside) ToggleFullscreen(); else Input.Chord(Input.F11); }
        // the d-pad: arrow keys, held like keys
        foreach (var m in new[] { new[] { Pad.Up, Input.Up }, new[] { Pad.Down, Input.Down }, new[] { Pad.Left, Input.Left }, new[] { Pad.Right, Input.Right } }) {
            if ((pressed & m[0]) != 0) Input.Key((byte)m[1], false);
            if ((released & m[0]) != 0) Input.Key((byte)m[1], true);
        }
    }
    // the Xbox / PS / Home button from the browser: back to the launcher
    void LeaveBrowser() {
        if (leftHeld) { Input.Mouse(Input.LeftUp); leftHeld = false; }
        if (inTray) ComeBack(null);
        else { W.Show(); if (W.WindowState == WindowState.Minimized) W.WindowState = WindowState.Normal; BringToFront(new WindowInteropHelper(W).Handle); W.Activate(); }
        Sound.Play(Sfx.Back);
    }
}
}
