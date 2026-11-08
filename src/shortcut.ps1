# Creates the Start Menu shortcut "Recomp Launcher" with the launcher's AppUserModelID (RecompLauncher.App, set by
# Recomp Launcher.exe). The id ties the launcher's window to this shortcut, so the taskbar shows the launcher's icon
# instead of Windows PowerShell's, and pinning it starts Recomp Launcher.exe.
$src  = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $src
$exe  = Join-Path $root 'Recomp Launcher.exe'
$lnk  = Join-Path ([Environment]::GetFolderPath('Programs')) 'Recomp Launcher.lnk'
$AppId = 'RecompLauncher.App'

$s = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk)
$s.TargetPath = $exe
$s.WorkingDirectory = $root
$s.IconLocation = "$exe,0"
$s.Description = 'Play and update Tears of the Kingdom and Super Mario Odyssey recomps'
$s.Save()

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class LinkAppId {
    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey { public Guid Fmtid; public uint Pid; }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PropVariant { [FieldOffset(0)] public ushort Vt; [FieldOffset(8)] public IntPtr Ptr; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPersistFile {
        void GetClassID(out Guid id);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string file, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string file);
        void GetCurFile(out IntPtr file);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

    // PKEY_AppUserModel_ID
    static readonly Guid AppIdFmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    public static void Set(string lnk, string appId) {
        object link = new ShellLink();
        try {
            var file = (IPersistFile)link;
            file.Load(lnk, 2);   // STGM_READWRITE
            var store = (IPropertyStore)link;
            var key = new PropertyKey { Fmtid = AppIdFmtid, Pid = 5 };
            var value = new PropVariant { Vt = 31, Ptr = Marshal.StringToCoTaskMemUni(appId) };   // VT_LPWSTR
            try {
                Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value));
                Marshal.ThrowExceptionForHR(store.Commit());
            } finally { Marshal.FreeCoTaskMem(value.Ptr); }
            file.Save(lnk, true);
        } finally { Marshal.ReleaseComObject(link); }
    }
}
'@
[LinkAppId]::Set($lnk, $AppId)
"Shortcut: $lnk ($AppId)"
