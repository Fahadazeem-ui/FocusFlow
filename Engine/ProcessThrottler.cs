using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FocusFlow.Engine;

internal sealed class ProcessThrottler
{
    private sealed record Entry(ProcessPriorityClass Original, long StartTicks, string Name);
    public sealed record StateItem(int Pid, long StartTicks, ProcessPriorityClass Original);

    private readonly Dictionary<int, Entry> _throttled = new();
    private readonly Dictionary<int, Entry> _boosted = new();
    private bool _dirty;

    private static string StatePath => Path.Combine(AppSettings.Dir, "state.json");

    public int ThrottledCount => _throttled.Count;

    public IReadOnlyList<string> ThrottledNames =>
        _throttled.Values.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    public void Throttle(Process p, Profile profile)
    {
        if (_throttled.ContainsKey(p.Id)) return;
        try
        {
            if (_boosted.Remove(p.Id, out var b)) p.PriorityClass = b.Original;
            var original = p.PriorityClass;
            if (original is ProcessPriorityClass.High or ProcessPriorityClass.RealTime) return;
            long start = p.StartTime.Ticks;
            p.PriorityClass = profile.BackgroundPriority;
            if (profile.EcoQos) SetEcoQos(p.Id, true);
            _throttled[p.Id] = new Entry(original, start, p.ProcessName);
            _dirty = true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { }
    }

    public void Boost(Process p)
    {
        try
        {
            if (_throttled.Remove(p.Id, out var t))
            {
                SetEcoQos(p.Id, false);
                p.PriorityClass = t.Original;
                _dirty = true;
            }
            if (_boosted.ContainsKey(p.Id)) return;
            var cur = p.PriorityClass;
            if (cur is ProcessPriorityClass.Normal or ProcessPriorityClass.BelowNormal)
            {
                _boosted[p.Id] = new Entry(cur, p.StartTime.Ticks, p.ProcessName);
                p.PriorityClass = ProcessPriorityClass.AboveNormal;
                _dirty = true;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { }
    }

    public void Release(Process p)
    {
        if (!_boosted.Remove(p.Id, out var b)) return;
        try { p.PriorityClass = b.Original; _dirty = true; } catch { }
    }

    public void Prune()
    {
        foreach (var pid in _throttled.Keys.Concat(_boosted.Keys).ToArray())
        {
            try { using var p = Process.GetProcessById(pid); }
            catch { _throttled.Remove(pid); _boosted.Remove(pid); _dirty = true; }
        }
    }

    public void RestoreAll()
    {
        foreach (var (pid, e) in _throttled.Concat(_boosted).ToArray()) Restore(pid, e);
        _throttled.Clear();
        _boosted.Clear();
        _dirty = false;
        DeleteState();
    }

    public void TrimThrottled()
    {
        foreach (var pid in _throttled.Keys)
        {
            IntPtr h = NativeMethods.OpenProcess(
                NativeMethods.PROCESS_SET_QUOTA | NativeMethods.PROCESS_QUERY_INFORMATION, false, pid);
            if (h == IntPtr.Zero) continue;
            try { NativeMethods.EmptyWorkingSet(h); }
            finally { NativeMethods.CloseHandle(h); }
        }
    }

    public void Save()
    {
        if (!_dirty) return;
        _dirty = false;
        try
        {
            Directory.CreateDirectory(AppSettings.Dir);
            var items = _throttled.Concat(_boosted)
                .Select(kv => new StateItem(kv.Key, kv.Value.StartTicks, kv.Value.Original)).ToList();
            File.WriteAllText(StatePath, JsonSerializer.Serialize(items));
        }
        catch { }
    }

    public static void Recover()
    {
        try
        {
            if (!File.Exists(StatePath)) return;
            var items = JsonSerializer.Deserialize<List<StateItem>>(File.ReadAllText(StatePath));
            if (items != null)
                foreach (var i in items) Restore(i.Pid, new Entry(i.Original, i.StartTicks, ""));
        }
        catch { }
        DeleteState();
    }

    private static void DeleteState()
    {
        try { File.Delete(StatePath); } catch { }
    }

    private static void Restore(int pid, Entry e)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.StartTime.Ticks != e.StartTicks) return;
            SetEcoQos(pid, false);
            p.PriorityClass = e.Original;
        }
        catch { }
    }

    private static void SetEcoQos(int pid, bool enable)
    {
        IntPtr h = NativeMethods.OpenProcess(NativeMethods.PROCESS_SET_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return;
        try
        {
            var state = new NativeMethods.PROCESS_POWER_THROTTLING_STATE
            {
                Version = 1,
                ControlMask = NativeMethods.THROTTLING_EXECUTION_SPEED,
                StateMask = enable ? NativeMethods.THROTTLING_EXECUTION_SPEED : 0
            };
            NativeMethods.SetProcessInformation(h, NativeMethods.ProcessPowerThrottling, ref state,
                Marshal.SizeOf<NativeMethods.PROCESS_POWER_THROTTLING_STATE>());
        }
        finally { NativeMethods.CloseHandle(h); }
    }
}
