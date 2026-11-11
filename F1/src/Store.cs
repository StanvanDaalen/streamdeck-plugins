using System.Text.Json;

namespace F1;

public sealed class F1Settings
{
    public string? BridgeIp { get; set; }
    public string? AppKey { get; set; }
    /// <summary>Hue room or zone whose colour lights show the flags.</summary>
    public string Group { get; set; } = "All lights";
    /// <summary>
    /// What the zone dial switches between, in dial order. A zone is a Hue room/zone, optionally
    /// without the lights of another one ("everything except the ceiling").
    /// </summary>
    public List<ZoneDef> ZoneList { get; set; } = new()
    {
        new() { Name = "Ceiling", Group = "Plafond" },
        new() { Name = "Ambient", Group = "Alle lampen", Except = "Plafond" }, // gradient strip, tube and Play lights
        new() { Name = "All lights", Group = "Alle lampen" },
    };
    /// <summary>Delay MultiViewer starts live streams with (its own setting); the dial starts there.</summary>
    public double BaseLatency { get; set; } = 40;
    public int Brightness { get; set; } = 254;
    public double GreenSeconds { get; set; } = 3;
    public bool FlagsEnabled { get; set; } = true;
    /// <summary>PowerToys Workspaces project that F1 mode launches.</summary>
    public string Workspace { get; set; } = "F1";
    /// <summary>Virtual desktop F1 mode set up last (registry GUID, hex), so Close F1 can remove it.</summary>
    public string? F1DesktopId { get; set; }
    /// <summary>Remote-debugging port Ultimate F1 Viewer is started with (set in the workspace).</summary>
    public int Uf1Port { get; set; } = 9333;
}

public sealed class ZoneDef
{
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";
    public string? Except { get; set; }
}

public static class Store
{
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StanF1");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");
    private static readonly string LogPath = Path.Combine(Dir, "plugin.log");
    private static readonly object LogLock = new();

    public static F1Settings Current { get; private set; } = new();

    /// <param name="freshLog">Only the plugin itself starts a new log; diagnostics keep it readable.</param>
    public static void Load(bool freshLog = true)
    {
        Directory.CreateDirectory(Dir);
        if (freshLog) try { File.WriteAllText(LogPath, ""); } catch { }
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<F1Settings>(File.ReadAllText(FilePath)) ?? new();
            // Zone names on the device are English now; older settings used Dutch display names.
            var renamed = new Dictionary<string, string>
            {
                ["Plafond"] = "Ceiling", ["Alles behalve plafond"] = "Ambient", ["Sfeerlicht"] = "Ambient", ["Alle lampen"] = "All lights",
            };
            foreach (var z in Current.ZoneList)
                if (renamed.TryGetValue(z.Name, out var n)) z.Name = n;
            if (renamed.TryGetValue(Current.Group, out var g)) Current.Group = g;
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
