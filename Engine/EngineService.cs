using System.Diagnostics;
using System.Windows.Threading;

namespace FocusFlow.Engine;

public sealed class EngineService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly HashSet<string> _whitelist;
    private readonly ForegroundWatcher _watcher = new();
    private readonly ProcessThrottler _throttler = new();
    private readonly NetworkController _net = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private int _fgPid;
    private int _tick;

    public bool IsRunning { get; private set; }
    public string ForegroundName { get; private set; } = "-";
    public int ThrottledCount => _throttler.ThrottledCount;
    public IReadOnlyList<string> ThrottledNames => _throttler.ThrottledNames;
    public Profile Profile => _settings.Profiles.FirstOrDefault(p => p.Name == _settings.ActiveProfile)
                              ?? _settings.Profiles[0];

    public event Action? StateChanged;

    public EngineService(AppSettings settings)
    {
        _settings = settings;
        _whitelist = new HashSet<string>(settings.Whitelist, StringComparer.OrdinalIgnoreCase);
        _watcher.Changed += pid => { _fgPid = pid; Apply(); };
        _timer.Tick += (_, _) => Apply();
    }

    public void Recover()
    {
        ProcessThrottler.Recover();
        _net.ClearAll(false);
    }

    public void SetProfile(string name)
    {
        _settings.ActiveProfile = name;
        _settings.Save();
        if (!IsRunning) return;
        _throttler.RestoreAll();
        _net.ClearAll(false);
        Apply();
    }

    public void RefreshWhitelist()
    {
        _whitelist.Clear();
        foreach (var w in _settings.Whitelist)
            _whitelist.Add(w);
        if (IsRunning) Apply();
    }

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        _fgPid = ForegroundWatcher.CurrentPid();
        _watcher.Start();
        _timer.Start();
        Apply();
    }

    public void Stop() => Stop(false);

    private void Stop(bool wait)
    {
        if (!IsRunning) return;
        IsRunning = false;
        _timer.Stop();
        _watcher.Dispose();
        _throttler.RestoreAll();
        _net.ClearAll(wait);
        StateChanged?.Invoke();
    }

    private void Apply()
    {
        if (!IsRunning) return;

        string fgName;
        try
        {
            using var fg = Process.GetProcessById(_fgPid);
            fgName = fg.ProcessName;
        }
        catch { return; }

        if (ProtectedList.Contains(fgName)) return;

        ForegroundName = fgName;
        var prof = Profile;
        _throttler.Prune();

        var socketPids = prof.NetKbps > 0 ? SocketScanner.PidsWithSockets() : null;
        var netTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == Environment.ProcessId) continue;
                    string name = p.ProcessName;

                    if (name.Equals(fgName, StringComparison.OrdinalIgnoreCase))
                    {
                        _throttler.Boost(p);
                        continue;
                    }

                    if (ProtectedList.Contains(name) || _whitelist.Contains(name) || p.SessionId == 0)
                    {
                        _throttler.Release(p);
                        continue;
                    }

                    _throttler.Throttle(p, prof);
                    if (socketPids != null && socketPids.Contains(p.Id)) netTargets.Add(name + ".exe");
                }
                catch { }
            }
        }

        _net.Sync(netTargets, prof.NetKbps);
        if (++_tick % 10 == 0 && prof.TrimRam) _throttler.TrimThrottled();
        _throttler.Save();
        StateChanged?.Invoke();
    }

    public void Dispose() => Stop(true);
}
