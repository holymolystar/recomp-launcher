// Windows calls: program icons, taskbar identity, Windows 11 corners and XInput controllers.
using System;
using System.Runtime.InteropServices;

namespace RecompLauncher {

static class IconX {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[] ids, uint count, uint flags);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
    // own taskbar identity (the Start Menu shortcut has the same id, which gives the taskbar button its icon)
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    // Windows 11 window corners (DWMWA_WINDOW_CORNER_PREFERENCE): 1 square, 2 rounded. Does nothing on Windows 10.
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    public static void WindowCorners(IntPtr h, bool round) { int v = round ? 2 : 1; try { DwmSetWindowAttribute(h, 33, ref v, 4); } catch { } }
}

// Per-window taskbar identity: the app id plus what pinning should start and show (docks such as Seelen UI read these)
static class WindowAppId {
    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid Fmtid; public uint Pid; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] struct PropVariant { [FieldOffset(0)] public ushort Vt; [FieldOffset(8)] public IntPtr Ptr; }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [DllImport("shell32.dll")]
    static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    static readonly Guid Fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    static void Put(IPropertyStore store, uint pid, string text) {
        var key = new PropertyKey { Fmtid = Fmtid, Pid = pid };
        var value = new PropVariant { Vt = 31, Ptr = Marshal.StringToCoTaskMemUni(text) };
        try { Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value)); } finally { Marshal.FreeCoTaskMem(value.Ptr); }
    }
    public static void Set(IntPtr hwnd, string appId, string relaunch, string name, string icon) {
        var iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
        IPropertyStore store;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd, ref iid, out store));
        try { Put(store, 2, relaunch); Put(store, 4, name); Put(store, 3, icon); Put(store, 5, appId); store.Commit(); }
        finally { Marshal.ReleaseComObject(store); }
    }
}

static class Pad {
    [StructLayout(LayoutKind.Sequential)]
    public struct State { public uint Packet; public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern int Get14(int user, out State s);
    // the same, with the Xbox button (0x400) too: Windows has it, unnamed, as function number 100
    [DllImport("xinput1_4.dll", EntryPoint = "#100")] static extern int GetEx14(int user, out State s);
    static bool noEx;
    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")] static extern int Get910(int user, out State s);
    [StructLayout(LayoutKind.Sequential)] struct Vibration { public ushort Left, Right; }
    [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")] static extern int Set14(int user, ref Vibration v);
    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputSetState")] static extern int Set910(int user, ref Vibration v);
    // the battery of the Xbox controller that answered last: false when it has none to tell (wired, or not connected)
    [StructLayout(LayoutKind.Sequential)] struct BatteryInfo { public byte Type, Level; }
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation")] static extern int Bat14(int user, byte devType, out BatteryInfo b);
    public static bool Battery(out int percent) {
        percent = -1;
        if (last < 0 || none || legacy) return false;
        try {
            BatteryInfo b;
            if (Bat14(last, 0, out b) != 0 || b.Type == 0 || b.Type == 1 || b.Type == 0xFF) return false;   // disconnected, wired, unknown
            percent = b.Level == 0 ? 5 : b.Level == 1 ? 25 : b.Level == 2 ? 60 : 100;
            return true;
        } catch { return false; }
    }
    static System.Threading.Timer rumbleOff;
    // a short buzz on the controller that answered last (Xbox controllers, and others through Steam Input)
    public static void Rumble(double strength, int ms) {
        if (last < 0 || none) return;
        int user = last;
        try {
            var v = new Vibration { Left = (ushort)(65535 * strength), Right = (ushort)(65535 * strength * 0.6) };
            if (legacy) Set910(user, ref v); else Set14(user, ref v);
            if (rumbleOff != null) rumbleOff.Dispose();
            rumbleOff = new System.Threading.Timer(_ => { try { var z = new Vibration(); if (legacy) Set910(user, ref z); else Set14(user, ref z); } catch { } }, null, ms, System.Threading.Timeout.Infinite);
        } catch { }
    }
    static bool legacy, none;
    static int last = -1;
    public const int Up = 0x1, Down = 0x2, Left = 0x4, Right = 0x8, Start = 0x10, Back = 0x20, LS = 0x40, RS = 0x80,
                     LB = 0x100, RB = 0x200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000, Guide = 0x400,
                     LT = 0x10000, RT = 0x20000;   // the triggers, as buttons (made from how far they're pulled)
    // the first connected controller (the one that answered last is asked first)
    // what's in slot i (0-3): connected or not, and its battery (percent, -1 wired or unknown)
    public static bool Slot(int i, out int battery) {
        battery = -1;
        if (none) return false;
        try {
            State s; int r = legacy ? Get910(i, out s) : Get14(i, out s);
            if (r != 0) return false;
            if (!legacy) {
                BatteryInfo b;
                if (Bat14(i, 0, out b) == 0 && b.Type != 0 && b.Type != 1 && b.Type != 0xFF) battery = b.Level == 0 ? 5 : b.Level == 1 ? 25 : b.Level == 2 ? 60 : 100;
            }
            return true;
        } catch { return false; }
    }
    public static bool TryGet(out State s) {
        s = new State();
        if (none) return false;
        for (int k = 0; k < 4; k++) {
            int i = last >= 0 ? (last + k) % 4 : k;
            int r;
            try {
                if (!legacy) {
                    try {
                        if (!noEx) { try { r = GetEx14(i, out s); } catch (EntryPointNotFoundException) { noEx = true; r = Get14(i, out s); } }
                        else r = Get14(i, out s);
                    } catch (DllNotFoundException) { legacy = true; r = Get910(i, out s); }
                }
                else r = Get910(i, out s);
            } catch (DllNotFoundException) { none = true; return false; }
            if (r == 0) { last = i; return true; }
            if (last >= 0) break;   // the one we had is gone: look again next scan
        }
        last = -1;
        return false;
    }
}
}
