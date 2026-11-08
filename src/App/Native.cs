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
    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")] static extern int Get910(int user, out State s);
    static bool legacy, none;
    static int last = -1;
    public const int Up = 0x1, Down = 0x2, Left = 0x4, Right = 0x8, Start = 0x10, Back = 0x20, LS = 0x40, RS = 0x80,
                     LB = 0x100, RB = 0x200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;
    // the first connected controller (the one that answered last is asked first)
    public static bool TryGet(out State s) {
        s = new State();
        if (none) return false;
        for (int k = 0; k < 4; k++) {
            int i = last >= 0 ? (last + k) % 4 : k;
            int r;
            try {
                if (!legacy) { try { r = Get14(i, out s); } catch (DllNotFoundException) { legacy = true; r = Get910(i, out s); } }
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
