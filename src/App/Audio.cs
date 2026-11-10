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
