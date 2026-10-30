using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FocusFlow.Engine;

/// <summary>Finds which PIDs currently own TCP/UDP sockets (IPv4 + IPv6).</summary>
internal static class SocketScanner
{
    public static HashSet<int> PidsWithSockets()
    {
        var set = new HashSet<int>();
        Read(set, true, 2, 5, 24, 20, 0);
        Read(set, true, 23, 5, 56, 52, 48);
        Read(set, false, 2, 1, 12, 8, -1);
        Read(set, false, 23, 1, 28, 24, -1);
        return set;
    }

    private static void Read(HashSet<int> set, bool tcp, int af, int cls, int rowSize, int pidOff, int stateOff)
    {
        uint Call(IntPtr buf, ref int size) => tcp
            ? NativeMethods.GetExtendedTcpTable(buf, ref size, false, af, cls, 0)
            : NativeMethods.GetExtendedUdpTable(buf, ref size, false, af, cls, 0);

        int size = 0;
        IntPtr mem = IntPtr.Zero;
        bool ok = false;
        try
        {
            for (int i = 0; i < 3; i++)
            {
                uint r = Call(mem, ref size);
                if (r == 0) { ok = mem != IntPtr.Zero; break; }
                if (r != 122) break;
                if (mem != IntPtr.Zero) Marshal.FreeHGlobal(mem);
                mem = Marshal.AllocHGlobal(size);
            }
            if (!ok) return;

            int count = Marshal.ReadInt32(mem);
            for (int i = 0; i < count; i++)
            {
                IntPtr row = mem + 4 + i * rowSize;
                if (stateOff >= 0 && Marshal.ReadInt32(row, stateOff) == 2) continue;
                set.Add(Marshal.ReadInt32(row, pidOff));
            }
        }
        finally
        {
            if (mem != IntPtr.Zero) Marshal.FreeHGlobal(mem);
        }
    }
}

/// <summary>
/// Per-app upload throttling through Windows QoS policies (New-NetQosPolicy, ActiveStore).
/// </summary>
internal sealed class NetworkController
{
    private const string Prefix = "FocusFlow_";
    private readonly HashSet<string> _applied = new(StringComparer.OrdinalIgnoreCase);
    private int _appliedKbps;
    private Task _chain = Task.CompletedTask;
    private readonly object _lock = new();

    public void Sync(HashSet<string> targets, int kbps)
    {
        if (kbps <= 0)
        {
            if (_applied.Count > 0) ClearAll(false);
            return;
        }

        var keep = _applied.Where(targets.Contains).ToList();
        var remove = _applied.Where(a => !targets.Contains(a)).ToList();
        var add = targets.Where(t => !_applied.Contains(t)).ToList();
        bool rateChanged = kbps != _appliedKbps;
        if (!rateChanged && remove.Count == 0 && add.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var n in remove) sb.AppendLine(Rm(n));
        foreach (var n in add) sb.AppendLine(Rm(n)).AppendLine(New(n, kbps));
        if (rateChanged) foreach (var n in keep) sb.AppendLine(Rm(n)).AppendLine(New(n, kbps));

        _applied.Clear();
        foreach (var t in targets) _applied.Add(t);
        _appliedKbps = kbps;
        Run(sb.ToString());
    }

    public void ClearAll(bool wait)
    {
        _applied.Clear();
        _appliedKbps = 0;
        var t = Run("Get-NetQosPolicy -PolicyStore ActiveStore -ErrorAction SilentlyContinue | " +
                    "Where-Object { $_.Name -like 'FocusFlow_*' } | " +
                    "Remove-NetQosPolicy -PolicyStore ActiveStore -Confirm:$false -ErrorAction SilentlyContinue");
        if (wait) t.Wait(8000);
    }

    private static string Esc(string s) => s.Replace("'", "''");

    private static string Rm(string exe) =>
        $"Remove-NetQosPolicy -Name '{Esc(Prefix + exe)}' -PolicyStore ActiveStore -Confirm:$false -ErrorAction SilentlyContinue";

    private static string New(string exe, int kbps) =>
        $"New-NetQosPolicy -Name '{Esc(Prefix + exe)}' -AppPathNameMatchCondition '{Esc(exe)}' " +
        $"-ThrottleRateActionBitsPerSecond {kbps * 1000L} -PolicyStore ActiveStore -ErrorAction SilentlyContinue | Out-Null";

    private Task Run(string script)
    {
        lock (_lock)
        {
            _chain = _chain.ContinueWith(_ => Exec(script), TaskScheduler.Default);
            return _chain;
        }
    }

    private static void Exec(string script)
    {
        try
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
            p?.WaitForExit(20000);
        }
        catch { }
    }
}
