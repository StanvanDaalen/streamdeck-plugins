using System.Text.Json;

namespace GameVolume;

public sealed class GameOverride
{
    public string? Name { get; set; }
    public string? Image { get; set; }
}

public sealed class CustomGame
{
    public string Exe { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class GlobalSettings
{
    public bool RememberVolume { get; set; } = true;
    public bool Balance { get; set; } = true;
    public bool Duck { get; set; } = true;
    public int DuckPercent { get; set; } = 40;
    public Dictionary<string, float> GameVolumes { get; set; } = new();
    public Dictionary<string, GameOverride> Overrides { get; set; } = new();
    public List<CustomGame> CustomGames { get; set; } = new();
    public HashSet<string> HiddenGames { get; set; } = new();
    /// <summary>Favourite volume (0-1) per game id or "app:name".</summary>
    public Dictionary<string, float> Favorites { get; set; } = new();
    public bool StartOnFavorite { get; set; }
    /// <summary>Last known program path per process name, so closed apps can be started again.</summary>
    public Dictionary<string, string> AppExes { get; set; } = new();
    public string? LastGameId { get; set; }
    public string? LastGameExe { get; set; }
    /// <summary>Spotify's normal volume while ducked; persisted so a plugin restart can still restore it.</summary>
    public float? DuckRestore { get; set; }
}

public static class SettingsStore
{
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StanGameVolume");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");
    private static string LogPath => Path.Combine(Dir, LogFile);
    private static readonly object LogLock = new();
    private static bool _dirty;

    /// <summary>Each plugin process keeps its own log; set this before calling Load.</summary>
    public static string LogFile { get; set; } = "plugin.log";

    public static GlobalSettings Current { get; private set; } = new();

    public static DateTime FileTime => File.GetLastWriteTimeUtc(FilePath);

    public static void Load()
    {
        Directory.CreateDirectory(Dir);
        try { File.WriteAllText(LogPath, ""); } catch { }
        Reload();
    }

    public static void Reload()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<GlobalSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) { Log("settings load: " + e.Message); }
    }

    public static void MarkDirty() => _dirty = true;

    public static void SaveIfDirty()
    {
        if (!_dirty) return;
        _dirty = false;
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
