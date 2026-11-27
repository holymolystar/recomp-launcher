// The PC's sound, for Quick settings in the in-game menu: the volume and mute of the speakers in use, and which
// output plays the sound (headphones, the TV, speakers). Windows' own audio interfaces (Core Audio); switching the
// output uses the same switch as the sound menu by the clock (IPolicyConfig).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RecompLauncher {

static class Audio {
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection { int GetCount(out int n); int Item(int i, out IMMDevice device); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice {
        int Activate(ref Guid iid, int clsCtx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        int OpenPropertyStore(int access, out IPropStore store);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }
    [StructLayout(LayoutKind.Sequential)] struct PropKey { public Guid fmtid; public int pid; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] struct PropVar { [FieldOffset(0)] public short vt; [FieldOffset(8)] public IntPtr p; }
    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropStore { int GetCount(out int n); int GetAt(int i, out PropKey k); int GetValue(ref PropKey k, out PropVar v); }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume {
        int RegisterControlChangeNotify(IntPtr n); int UnregisterControlChangeNotify(IntPtr n); int GetChannelCount(out int n);
        int SetMasterVolumeLevel(float db, ref Guid ctx); int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        int GetMasterVolumeLevel(out float db); int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(int ch, float db, ref Guid ctx); int SetChannelVolumeLevelScalar(int ch, float level, ref Guid ctx);
        int GetChannelVolumeLevel(int ch, out float db); int GetChannelVolumeLevelScalar(int ch, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx); int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] class PolicyConfigClient { }
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig {
        int GetMixFormat(); int GetDeviceFormat(); int ResetDeviceFormat(); int SetDeviceFormat(); int GetProcessingPeriod();
        int SetProcessingPeriod(); int GetShareMode(); int SetShareMode(); int GetPropertyValue(); int SetPropertyValue();
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    }
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVar v);

    static IAudioEndpointVolume Volume() {
        var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDevice dev; Marshal.ThrowExceptionForHR(en.GetDefaultAudioEndpoint(0, 1, out dev));   // render, multimedia
        var iid = typeof(IAudioEndpointVolume).GUID; object o;
        Marshal.ThrowExceptionForHR(dev.Activate(ref iid, 23, IntPtr.Zero, out o));
        return (IAudioEndpointVolume)o;
    }
    // the volume (0-100), or -1 when there's no output
    public static int Level { get { try { float v; Volume().GetMasterVolumeLevelScalar(out v); return (int)Math.Round(v * 100); } catch { return -1; } } }
    public static void SetLevel(int percent) { try { var g = Guid.Empty; Volume().SetMasterVolumeLevelScalar(Math.Max(0, Math.Min(100, percent)) / 100f, ref g); } catch { } }
    public static bool Muted { get { try { bool m; Volume().GetMute(out m); return m; } catch { return false; } } }
    public static void SetMuted(bool m) { try { var g = Guid.Empty; Volume().SetMute(m, ref g); } catch { } }

    // The launcher's own volume: its slider in Windows' volume mixer (everything it plays, the TV's picture and sound
    // included, as mpv plays inside it), leaving the PC's volume and other apps alone.
    [ComImport, Guid("BFA971F1-4D5E-40BB-935E-967039BFBEE4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager {
        int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
        int GetSimpleAudioVolume(IntPtr sessionGuid, int crossProcess, out ISimpleAudioVolume volume);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume {
        int SetMasterVolume(float level, ref Guid ctx); int GetMasterVolume(out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx); int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
    static ISimpleAudioVolume AppVolume() {
        var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDevice dev; Marshal.ThrowExceptionForHR(en.GetDefaultAudioEndpoint(0, 1, out dev));
        var iid = typeof(IAudioSessionManager).GUID; object o;
        Marshal.ThrowExceptionForHR(dev.Activate(ref iid, 23, IntPtr.Zero, out o));
        ISimpleAudioVolume v; Marshal.ThrowExceptionForHR(((IAudioSessionManager)o).GetSimpleAudioVolume(IntPtr.Zero, 0, out v));
        return v;
    }
    // the launcher's volume (0-100), or -1 when there's no output
    public static int AppLevel { get { try { float v; AppVolume().GetMasterVolume(out v); return (int)Math.Round(v * 100); } catch { return -1; } } }
    public static void SetAppLevel(int percent) {
        try { var g = Guid.Empty; var v = AppVolume(); v.SetMute(false, ref g); v.SetMasterVolume(Math.Max(0, Math.Min(100, percent)) / 100f, ref g); } catch { }
        MatchHelpers();
    }

    // The launcher's browser parts (WebView2: the trailers in TV & Movies, the browser) play their sound from Edge
    // processes of their own, which Windows' mixer lists apart. Those of this launcher (started by it, not anyone
    // else's Edge) are kept at the launcher's volume, so a trailer is exactly as loud as the rest.
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2 {
        int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
        int GetSimpleAudioVolume(IntPtr sessionGuid, int crossProcess, out IntPtr volume);
        int GetSessionEnumerator(out IAudioSessionEnumerator e);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator { int GetCount(out int n); int GetSession(int i, out IAudioSessionControl2 s); }
    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2 {
        int GetState(out int s); int GetDisplayName(out IntPtr n); int SetDisplayName(string n, ref Guid ctx); int GetIconPath(out IntPtr p); int SetIconPath(string p, ref Guid ctx);
        int GetGroupingParam(out Guid g); int SetGroupingParam(ref Guid g, ref Guid ctx); int RegisterAudioSessionNotification(IntPtr n); int UnregisterAudioSessionNotification(IntPtr n);
        int GetSessionIdentifier(out IntPtr id); int GetSessionInstanceIdentifier(out IntPtr id); int GetProcessId(out uint pid);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcEntry { public int dwSize, cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID; public int th32ModuleID, cntThreads, th32ParentProcessID, pcPriClassBase, dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile; }
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snap, ref ProcEntry e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snap, ref ProcEntry e);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    // the processes this launcher started, and those they started
    static HashSet<int> OwnHelpers() {
        var parent = new Dictionary<int, int>(); var own = new HashSet<int>();
        IntPtr snap = CreateToolhelp32Snapshot(2 /* processes */, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return own;
        try {
            var e = new ProcEntry { dwSize = Marshal.SizeOf(typeof(ProcEntry)) };
            for (bool ok = Process32FirstW(snap, ref e); ok; ok = Process32NextW(snap, ref e)) parent[e.th32ProcessID] = e.th32ParentProcessID;
        } finally { CloseHandle(snap); }
        int me = System.Diagnostics.Process.GetCurrentProcess().Id;
        foreach (var pid in parent.Keys) {
            int p = pid; for (int depth = 0; depth < 6 && parent.TryGetValue(p, out p) && p != 0; depth++) if (p == me) { own.Add(pid); break; }
        }
        return own;
    }
    public static void MatchHelpers() {
        try {
            float level; var g = Guid.Empty; var mine = AppVolume(); mine.GetMasterVolume(out level);
            var own = OwnHelpers(); if (own.Count == 0) return;
            var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice dev; Marshal.ThrowExceptionForHR(en.GetDefaultAudioEndpoint(0, 1, out dev));
            var iid = typeof(IAudioSessionManager2).GUID; object o;
            Marshal.ThrowExceptionForHR(dev.Activate(ref iid, 23, IntPtr.Zero, out o));
            IAudioSessionEnumerator list; Marshal.ThrowExceptionForHR(((IAudioSessionManager2)o).GetSessionEnumerator(out list));
            int n; list.GetCount(out n);
            for (int i = 0; i < n; i++) {
                IAudioSessionControl2 s; if (list.GetSession(i, out s) != 0 || s == null) continue;
                uint pid; if (s.GetProcessId(out pid) != 0 || !own.Contains((int)pid)) continue;
                var v = s as ISimpleAudioVolume; if (v == null) continue;
                v.SetMute(false, ref g); v.SetMasterVolume(level, ref g);
            }
        } catch { }
    }

    public class Output { public string Id, Name; }
    // the outputs that are plugged in and on, and which one plays now
    public static List<Output> Outputs(out string current) {
        var l = new List<Output>(); current = null;
        try {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice def; if (en.GetDefaultAudioEndpoint(0, 1, out def) == 0) def.GetId(out current);
            IMMDeviceCollection col; Marshal.ThrowExceptionForHR(en.EnumAudioEndpoints(0, 1, out col));   // render, active
            int n; col.GetCount(out n);
            var nameKey = new PropKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };   // friendly name
            for (int i = 0; i < n; i++) {
                IMMDevice d; if (col.Item(i, out d) != 0) continue;
                string id; d.GetId(out id);
                string name = id;
                IPropStore ps;
                if (d.OpenPropertyStore(0, out ps) == 0) {
                    PropVar v; if (ps.GetValue(ref nameKey, out v) == 0) { if (v.vt == 31) name = Marshal.PtrToStringUni(v.p); PropVariantClear(ref v); }
                }
                l.Add(new Output { Id = id, Name = name });
            }
        } catch { }
        return l;
    }
    public static bool Use(string id) {
        try {
            var pc = (IPolicyConfig)new PolicyConfigClient();
            for (int role = 0; role < 3; role++) pc.SetDefaultEndpoint(id, role);   // console, multimedia, communications
            return true;
        } catch { return false; }
    }
}
}
