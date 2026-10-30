using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusFlow.Engine;

public sealed class Profile
{
    public string Name { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ProcessPriorityClass BackgroundPriority { get; set; } = ProcessPriorityClass.BelowNormal;

    public bool EcoQos { get; set; } = true;

    /// <summary>Upload cap per background app in Kbps. 0 = no network control.</summary>
    public int NetKbps { get; set; }

    public bool TrimRam { get; set; }
}

public sealed class AppSettings
{
    public string ActiveProfile { get; set; } = "Gaming";

    /// <summary>Process names (no .exe) that are never throttled.</summary>
    public List<string> Whitelist { get; set; } = new() { "discord", "spotify", "obs64", "obs32" };

    public bool AutoStart { get; set; }

    public List<Profile> Profiles { get; set; } = new()
    {
        new Profile { Name = "Gaming",  BackgroundPriority = ProcessPriorityClass.Idle,        EcoQos = true, NetKbps = 256,  TrimRam = true  },
        new Profile { Name = "Study",   BackgroundPriority = ProcessPriorityClass.BelowNormal, EcoQos = true, NetKbps = 1024, TrimRam = false },
        new Profile { Name = "Editing", BackgroundPriority = ProcessPriorityClass.BelowNormal, EcoQos = true, NetKbps = 2048, TrimRam = false },
    };

    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FocusFlow");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s is { Profiles.Count: > 0 }) return s;
            }
        }
        catch { /* corrupt file: fall back to defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
