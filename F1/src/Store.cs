using System.Text.Json;

namespace F1;

public sealed class F1Settings
{
    public string? BridgeIp { get; set; }
    public string? AppKey { get; set; }
    /// <summary>Hue room or zone whose colour lights show the flags.</summary>
    public string Group { get; set; } = "Alle lampen";
    public int Brightness { get; set; } = 254;
    public double GreenSeconds { get; set; } = 3;
    public bool FlagsEnabled { get; set; } = true;
    /// <summary>PowerToys Workspaces project that F1 mode launches.</summary>
    public string Workspace { get; set; } = "F1";
    /// <summary>Virtual desktop (1-based) that F1 mode switches to.</summary>
    public int Desktop { get; set; } = 2;
    /// <summary>Saved MultiViewer player layouts, by name ("Race", "Kwali").</summary>
    public Dictionary<string, List<SavedPlayer>> Layouts { get; set; } = new();
}

public static class Store
{
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StanF1");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");
    private static readonly string LogPath = Path.Combine(Dir, "plugin.log");
    private static readonly object LogLock = new();

    public static F1Settings Current { get; private set; } = new();

    public static void Load()
    {
        Directory.CreateDirectory(Dir);
        try { File.WriteAllText(LogPath, ""); } catch { }
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<F1Settings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) { Log("settings load: " + e.Message); }
    }

    public static void Save()
    {
        try
        {
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception e) { Log("settings save: " + e.Message); }
    }

    public static void Log(string msg)
    {
        lock (LogLock)
        {
            try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} {msg}\n"); } catch { }
        }
    }
}
