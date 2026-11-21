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
    /// <summary>Volume (0-1) per App Volume app, also set while the app plays nothing; new audio of the app starts at it.</summary>
    public Dictionary<string, float> AppVolumes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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
    private static DateTime _knownTime;
    /// <summary>Game overrides this process changed since its last save; other overrides follow the file on disk.</summary>
    private static readonly HashSet<string> TouchedOverrides = new();

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
            {
                _knownTime = FileTime;
                Current = JsonSerializer.Deserialize<GlobalSettings>(File.ReadAllText(FilePath)) ?? new();
            }
        }
        catch (Exception e) { Log("settings load: " + e.Message); }
    }

    public static void MarkDirty() => _dirty = true;

    /// <summary>Marks a game's name or image as changed here, so a save keeps this version over the one on disk.</summary>
    public static void TouchOverride(string id) { TouchedOverrides.Add(id); _dirty = true; }

    /// <summary>
    /// Saves pending changes. Audio and Launcher share this file, so names and images set in the other plugin
    /// are taken over first instead of being overwritten. Returns true when it loaded changes from the other plugin.
    /// </summary>
    public static bool SaveIfDirty()
    {
        bool changedOnDisk = File.Exists(FilePath) && FileTime != _knownTime;
        if (!_dirty)
        {
            if (changedOnDisk) Reload();
            return changedOnDisk;
        }
        _dirty = false;
        try
        {
            if (changedOnDisk && JsonSerializer.Deserialize<GlobalSettings>(File.ReadAllText(FilePath)) is { } disk)
            {
                foreach (var id in Current.Overrides.Keys.Where(k => !TouchedOverrides.Contains(k) && !disk.Overrides.ContainsKey(k)).ToList())
                    Current.Overrides.Remove(id);
                foreach (var (id, o) in disk.Overrides)
                    if (!TouchedOverrides.Contains(id)) Current.Overrides[id] = o;
            }
            TouchedOverrides.Clear();
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, true);
            _knownTime = FileTime;
        }
        catch (Exception e) { Log("settings save: " + e.Message); }
        return changedOnDisk;
    }

    /// <summary>Copies a chosen game logo next to the settings, so it stays when the original file is moved or deleted.</summary>
    public static string KeepLogo(string gameId, string source)
    {
        try
        {
            var dir = Path.Combine(Dir, "logos");
            Directory.CreateDirectory(dir);
            if (Path.GetDirectoryName(Path.GetFullPath(source))!.Equals(dir, StringComparison.OrdinalIgnoreCase)) return source;
            var safe = string.Concat(gameId.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
            foreach (var old in Directory.EnumerateFiles(dir, safe + ".*")) File.Delete(old);
            var target = Path.Combine(dir, safe + Path.GetExtension(source).ToLowerInvariant());
            File.Copy(source, target, true);
            return target;
        }
        catch (Exception e) { Log("logo copy: " + e.Message); return source; }
    }

    public static void Log(string msg)
    {
        lock (LogLock)
        {
            try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} {msg}\n"); } catch { }
        }
    }
}
