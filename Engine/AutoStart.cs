using System.Diagnostics;
using System.Text;

namespace FocusFlow.Engine;

/// <summary>Manages a shortcut in the current user's Startup folder via PowerShell.</summary>
internal static class AutoStart
{
    private static string ShortcutPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            "FocusFlow.lnk");

    public static bool IsEnabled() => File.Exists(ShortcutPath);

    public static void Set(bool enable)
    {
        try
        {
            if (enable)
            {
                string exe = Environment.ProcessPath
                    ?? Process.GetCurrentProcess().MainModule?.FileName
                    ?? "";
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return;

                string dir = Path.GetDirectoryName(exe) ?? "";
                string ps =
                    $"$s = (New-Object -ComObject WScript.Shell).CreateShortcut('{Esc(ShortcutPath)}');" +
                    $"$s.TargetPath = '{Esc(exe)}';" +
                    $"$s.WorkingDirectory = '{Esc(dir)}';" +
                    "$s.Description = 'FocusFlow';" +
                    "$s.Save()";

                RunPs(ps);
            }
            else
            {
                if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
            }
        }
        catch { /* non-fatal */ }
    }

    private static string Esc(string s) => s.Replace("'", "''");

    private static void RunPs(string script)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-EncodedCommand");
        psi.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var p = Process.Start(psi);
        p?.WaitForExit(10000);
    }
}
