// Recomp Launcher.exe: opens Recomp-Launcher.ps1 (next to this exe) in a hidden PowerShell, with no console window.
// Build: build.ps1 in this folder.
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

static class Launcher {
    [STAThread]
    static void Main(string[] args) {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        string script = Path.Combine(dir, "Recomp-Launcher.ps1");
        if (!File.Exists(script)) {
            MessageBox.Show("Recomp-Launcher.ps1 was not found next to this program:\n" + dir,
                "Recomp Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        string extra = args.Length > 0 ? " " + string.Join(" ", Array.ConvertAll(args, a => "\"" + a + "\"")) : "";
        var psi = new ProcessStartInfo {
            FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\"" + extra,
            WorkingDirectory = dir,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        try { Process.Start(psi); }
        catch (Exception e) {
            MessageBox.Show("Could not start the launcher: " + e.Message, "Recomp Launcher",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
