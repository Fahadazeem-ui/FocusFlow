namespace FocusFlow.Engine;

internal sealed class ForegroundWatcher : IDisposable
{
    private IntPtr _hook;
    private readonly NativeMethods.WinEventDelegate _proc;

    public event Action<int>? Changed;

    public ForegroundWatcher() => _proc = OnEvent;

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
    }

    private void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid != 0) Changed?.Invoke((int)pid);
    }

    public static int CurrentPid()
    {
        NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out uint pid);
        return (int)pid;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
