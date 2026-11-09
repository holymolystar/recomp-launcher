// PlayStation and Switch controllers read straight from Windows (HID), for when nothing turns them into an Xbox
// controller (Steam Input, DS4Windows, BetterJoy do; then XInput is used and these readers stay idle).
//   DualShock 4 and DualSense, by USB or Bluetooth;  Switch Pro Controller, by Bluetooth or USB (USB needs a short
//   handshake, sent here).
// Each report becomes the same State XInput gives (buttons in XInput's bits, sticks -32768..32767, up positive), so the
// rest of the launcher doesn't know the difference. Buttons are matched by where they sit: the bottom face button is A
// (Cross on PlayStation, B on Switch), as on an Xbox controller.
// Also tells what kind of controller is plugged in (PlayStation, Switch or other), even when Steam Input is in between,
// for the button prompts and the controller icon.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace RecompLauncher {

enum PadKind { None, Xbox, PlayStation, Switch }

static class HidPads {
    // ---------- Windows ----------
    [StructLayout(LayoutKind.Sequential)] struct DevIfData { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct Attributes { public int Size; public ushort VendorID, ProductID, VersionNumber; }
    [StructLayout(LayoutKind.Sequential)] struct Caps {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort a, b, c, d, e, f, g, h, i, j;
    }
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref Attributes a);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, ref Caps c);
    [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, int flags);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid g, int i, ref DevIfData d);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref DevIfData d, IntPtr detail, int size, out int need, IntPtr info);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

    const ushort Sony = 0x054C, Nintendo = 0x057E;
    static readonly ushort[] SonyPads = { 0x05C4, 0x09CC, 0x0BA0, 0x0CE6, 0x0DF2 };   // DS4 v1, DS4 v2, DS4 dongle, DualSense, DualSense Edge
    static readonly ushort[] NintendoPads = { 0x2009 };                              // Switch Pro Controller

    class Dev { public string Path; public ushort Vid, Pid; public int InLen; }

    // every game controller from Sony or Nintendo plugged in now
    static List<Dev> Find() {
        var list = new List<Dev>();
        Guid g; HidD_GetHidGuid(out g);
        IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);   // present, device interfaces
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return list;
        try {
            for (int i = 0; ; i++) {
                var d = new DevIfData { cbSize = Marshal.SizeOf(typeof(DevIfData)) };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d)) break;
                int need;
                SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out need, IntPtr.Zero);
                IntPtr buf = Marshal.AllocHGlobal(need);
                try {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref d, buf, need, out need, IntPtr.Zero)) continue;
                    string path = Marshal.PtrToStringAuto(new IntPtr(buf.ToInt64() + 4));
                    // vendor and product are in the path: \\?\hid#vid_054c&pid_0ce6...
                    var m = System.Text.RegularExpressions.Regex.Match(path ?? "", @"vid_([0-9a-f]{4}).*?pid_([0-9a-f]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!m.Success) continue;
                    ushort vid = Convert.ToUInt16(m.Groups[1].Value, 16), pid = Convert.ToUInt16(m.Groups[2].Value, 16);
                    if (!(vid == Sony && SonyPads.Contains(pid)) && !(vid == Nintendo && NintendoPads.Contains(pid))) continue;
                    list.Add(new Dev { Path = path, Vid = vid, Pid = pid });
                } finally { Marshal.FreeHGlobal(buf); }
            }
        } finally { SetupDiDestroyDeviceInfoList(set); }
        return list;
    }

    // ---------- what's plugged in ----------
    static PadKind seen = PadKind.None; static DateTime seenAt = DateTime.MinValue;
    // the kind of the first Sony or Nintendo controller plugged in (looked up at most every 3 seconds), else None
    public static PadKind Plugged() {
        if ((DateTime.Now - seenAt).TotalSeconds < 3) return seen;
        seenAt = DateTime.Now;
        try {
            var d = Find().FirstOrDefault();
            seen = d == null ? PadKind.None : d.Vid == Sony ? PadKind.PlayStation : PadKind.Switch;
        } catch { seen = PadKind.None; }
        return seen;
    }
    public static string Model { get; private set; }

    // ---------- reading ----------
    static Thread reader; static volatile bool readerOk;
    static readonly object gate = new object();
    static Pad.State latest; static DateTime latestAt = DateTime.MinValue; static PadKind latestKind;
    static DateTime nextOpen = DateTime.MinValue;

    // the newest state from a PlayStation or Switch controller read directly; false when there's none
    public static bool TryGet(out Pad.State s, out PadKind kind) {
        s = new Pad.State(); kind = PadKind.None;
        if (!readerOk && DateTime.Now >= nextOpen) { nextOpen = DateTime.Now.AddSeconds(2); Open(); }
        if (!readerOk) return false;
        lock (gate) {
            if ((DateTime.Now - latestAt).TotalSeconds > 1.5) return false;   // nothing new for a while: gone or asleep
            s = latest; kind = latestKind;
        }
        return true;
    }
    static void Open() {
        Dev dev;
        try { dev = Find().FirstOrDefault(); } catch { return; }
        if (dev == null) return;
        var h = CreateFile(dev.Path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);   // read and write, shared, existing
        if (h.IsInvalid) { h = CreateFile(dev.Path, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero); if (h.IsInvalid) return; }
        IntPtr pre; var caps = new Caps();
        if (HidD_GetPreparsedData(h, out pre)) { HidP_GetCaps(pre, ref caps); HidD_FreePreparsedData(pre); }
        int inLen = Math.Max((int)caps.InputReportByteLength, 64), outLen = caps.OutputReportByteLength;
        var kind = dev.Vid == Sony ? PadKind.PlayStation : PadKind.Switch;
        Model = dev.Vid == Sony ? (dev.Pid == 0x0CE6 || dev.Pid == 0x0DF2 ? "DualSense" : "DualShock 4") : "Switch Pro Controller";
        readerOk = true;
        reader = new Thread(() => Read(h, inLen, outLen, kind, dev.Pid)) { IsBackground = true, Name = "HID controller" };
        reader.Start();
    }
    static void Read(SafeFileHandle h, int inLen, int outLen, PadKind kind, ushort pid) {
        try {
            using (var fs = new FileStream(h, FileAccess.ReadWrite, Math.Max(inLen, 64), false)) {
                if (kind == PadKind.Switch) SwitchHello(fs, outLen);
                var buf = new byte[inLen];
                while (true) {
                    int n = fs.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    Pad.State s;
                    if (kind == PadKind.PlayStation ? ParsePlayStation(buf, n, pid, out s) : ParseSwitch(buf, n, out s)) {
                        lock (gate) { latest = s; latestAt = DateTime.Now; latestKind = kind; }
                    }
                }
            }
        } catch { }
        finally { readerOk = false; try { h.Dispose(); } catch { } }
    }

    // ---------- PlayStation ----------
    // hat 0..7 (up, up-right, right ... up-left), 8 = none
    static int Hat(int v) {
        switch (v) {
            case 0: return Pad.Up; case 1: return Pad.Up | Pad.Right; case 2: return Pad.Right; case 3: return Pad.Down | Pad.Right;
            case 4: return Pad.Down; case 5: return Pad.Down | Pad.Left; case 6: return Pad.Left; case 7: return Pad.Up | Pad.Left;
            default: return 0;
        }
    }
    static short Axis8(byte v, bool flip) { int x = (v - 128) * 256; if (flip) x = -x; return (short)Math.Max(-32768, Math.Min(32767, x)); }
    static bool ParsePlayStation(byte[] b, int n, ushort pid, out Pad.State s) {
        s = new Pad.State();
        bool dualSense = pid == 0x0CE6 || pid == 0x0DF2;
        int o;   // where the sticks start
        if (b[0] == 0x01 && n >= 10) {
            // USB, or Bluetooth's short report: DualSense over USB has its triggers before the buttons
            o = 1;
            int bt = dualSense && n >= 64 ? 8 : 5;
            Fill(ref s, b, o, bt);
            return true;
        }
        if ((b[0] == 0x11 && !dualSense && n >= 12) ) { Fill(ref s, b, 3, 7); return true; }       // DualShock 4 by Bluetooth
        if ((b[0] == 0x31 && dualSense && n >= 12)) { Fill(ref s, b, 2, 9); return true; }         // DualSense by Bluetooth
        return false;
    }
    // sticks at o..o+3, then the face buttons and hat at bt and the shoulders at bt + 1
    static void Fill(ref Pad.State s, byte[] b, int o, int bt) {
        s.LX = Axis8(b[o], false); s.LY = Axis8(b[o + 1], true); s.RX = Axis8(b[o + 2], false); s.RY = Axis8(b[o + 3], true);
        int face = b[bt], sh = b[bt + 1], btn = Hat(face & 0x0F);
        if ((face & 0x10) != 0) btn |= Pad.X;       // square
        if ((face & 0x20) != 0) btn |= Pad.A;       // cross
        if ((face & 0x40) != 0) btn |= Pad.B;       // circle
        if ((face & 0x80) != 0) btn |= Pad.Y;       // triangle
        if ((sh & 0x01) != 0) btn |= Pad.LB;
        if ((sh & 0x02) != 0) btn |= Pad.RB;
        if ((sh & 0x04) != 0) s.LT = 255; if ((sh & 0x08) != 0) s.RT = 255;   // L2, R2
        if ((sh & 0x10) != 0) btn |= Pad.Back;      // Share / Create
        if ((sh & 0x20) != 0) btn |= Pad.Start;     // Options
        if ((sh & 0x40) != 0) btn |= Pad.LS;
        if ((sh & 0x80) != 0) btn |= Pad.RS;
        if ((b[bt + 2] & 0x01) != 0) btn |= Pad.Guide;   // the PS button
        s.Buttons = (ushort)btn;
    }

    // ---------- Switch Pro Controller ----------
    // by USB it stays quiet until told to talk over USB only; then asked for its full reports
    static void SwitchHello(FileStream fs, int outLen) {
        try {
            int len = Math.Max(outLen, 64);
            Action<byte[]> send = data => { var r = new byte[len]; Array.Copy(data, r, data.Length); fs.Write(r, 0, r.Length); fs.Flush(); };
            send(new byte[] { 0x80, 0x02 });   // handshake
            send(new byte[] { 0x80, 0x04 });   // USB only, no Bluetooth timeout
            send(new byte[] { 0x01, 0x00, 0x00, 0x01, 0x40, 0x40, 0x00, 0x01, 0x40, 0x40, 0x03, 0x30 });   // reports at full rate (0x30)
        } catch { }   // by Bluetooth these writes aren't needed
    }
    static short Axis12(int v, bool flip) { int x = (v - 2048) * 20; if (flip) x = -x; return (short)Math.Max(-32768, Math.Min(32767, x)); }
    static bool ParseSwitch(byte[] b, int n, out Pad.State s) {
        s = new Pad.State();
        int btn = 0;
        if (b[0] == 0x30 && n >= 12) {
            // full report: right buttons, shared, left buttons, then two 12-bit sticks
            int r = b[3], m = b[4], l = b[5];
            if ((r & 0x04) != 0) btn |= Pad.A;   // B, at the bottom
            if ((r & 0x08) != 0) btn |= Pad.B;   // A, on the right
            if ((r & 0x01) != 0) btn |= Pad.X;   // Y, on the left
            if ((r & 0x02) != 0) btn |= Pad.Y;   // X, at the top
            if ((r & 0x40) != 0) btn |= Pad.RB;
            if ((l & 0x40) != 0) btn |= Pad.LB;
            if ((l & 0x80) != 0) s.LT = 255; if ((r & 0x80) != 0) s.RT = 255;   // ZL, ZR
            if ((l & 0x02) != 0) btn |= Pad.Up; if ((l & 0x01) != 0) btn |= Pad.Down; if ((l & 0x08) != 0) btn |= Pad.Left; if ((l & 0x04) != 0) btn |= Pad.Right;
            if ((m & 0x01) != 0) btn |= Pad.Back; if ((m & 0x02) != 0) btn |= Pad.Start; if ((m & 0x08) != 0) btn |= Pad.LS; if ((m & 0x04) != 0) btn |= Pad.RS;
            if ((m & 0x10) != 0) btn |= Pad.Guide;   // Home
            s.LX = Axis12(b[6] | ((b[7] & 0x0F) << 8), false); s.LY = Axis12((b[7] >> 4) | (b[8] << 4), false);
            s.RX = Axis12(b[9] | ((b[10] & 0x0F) << 8), false); s.RY = Axis12((b[10] >> 4) | (b[11] << 4), false);
        } else if (b[0] == 0x3F && n >= 12) {
            // simple Bluetooth report: two button bytes, a hat, then 16-bit sticks
            int b1 = b[1], b2 = b[2];
            if ((b1 & 0x01) != 0) btn |= Pad.A;  // B
            if ((b1 & 0x02) != 0) btn |= Pad.B;  // A
            if ((b1 & 0x04) != 0) btn |= Pad.X;  // Y
            if ((b1 & 0x08) != 0) btn |= Pad.Y;  // X
            if ((b1 & 0x10) != 0) btn |= Pad.LB; if ((b1 & 0x20) != 0) btn |= Pad.RB;
            if ((b1 & 0x40) != 0) s.LT = 255; if ((b1 & 0x80) != 0) s.RT = 255;   // ZL, ZR
            if ((b2 & 0x01) != 0) btn |= Pad.Back; if ((b2 & 0x02) != 0) btn |= Pad.Start; if ((b2 & 0x04) != 0) btn |= Pad.LS; if ((b2 & 0x08) != 0) btn |= Pad.RS;
            if ((b2 & 0x10) != 0) btn |= Pad.Guide;   // Home
            btn |= Hat(b[3]);
            Func<int, short> ax = i => (short)Math.Max(-32768, Math.Min(32767, (b[i] | (b[i + 1] << 8)) - 32768));
            s.LX = ax(4); s.LY = (short)-Math.Max(-32767, (int)ax(6)); s.RX = ax(8); s.RY = (short)-Math.Max(-32767, (int)ax(10));
        } else return false;
        s.Buttons = (ushort)btn;
        return true;
    }
}
}
