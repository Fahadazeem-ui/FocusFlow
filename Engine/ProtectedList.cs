namespace FocusFlow.Engine;

internal static class ProtectedList
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "memory compression", "smss", "csrss", "wininit", "winlogon",
        "services", "lsass", "lsaiso", "svchost", "dwm", "explorer", "fontdrvhost", "audiodg",
        "sihost", "ctfmon", "taskhostw", "conhost", "dllhost", "runtimebroker", "searchhost",
        "searchindexer", "startmenuexperiencehost", "shellexperiencehost", "textinputhost",
        "applicationframehost", "systemsettings", "securityhealthservice", "securityhealthsystray",
        "msmpeng", "nissrv", "spoolsv", "wudfhost", "taskmgr", "focusflow", "logonui", "lockapp"
    };

    public static bool Contains(string processName) => Names.Contains(processName);
}
