using System.Text.Json;

namespace F1;

public sealed class F1Settings
{
    public string? BridgeIp { get; set; }
    public string? AppKey { get; set; }
    /// <summary>Hue room or zone whose colour lights show the flags.</summary>
    public string Group { get; set; } = "";
    /// <summary>
    /// What the zone dial switches between, in dial order. A zone is a Hue room/zone, optionally
    /// without the lights of another one ("everything except the ceiling").
    /// </summary>
    /// <remarks>Empty (the default for a new install) = every room and zone on the user's own bridge.</remarks>
    public List<ZoneDef> ZoneList { get; set; } = new();
    /// <summary>Delay MultiViewer starts live streams with (its own setting); the dial starts there.</summary>
    public double BaseLatency { get; set; } = 0;
    public int Brightness { get; set; } = 254;
    public double GreenSeconds { get; set; } = 3;
    public bool FlagsEnabled { get; set; } = true;
    /// <summary>Where F1 Mode puts the main windows when it starts the apps (physical pixels).</summary>
    public AppPlace? PlaceMultiViewer { get; set; }
    public AppPlace? PlaceUf1 { get; set; }
    /// <summary>Virtual desktop F1 mode set up last (registry GUID, hex), so Close F1 can remove it.</summary>
    public string? F1DesktopId { get; set; }
    /// <summary>Remote-debugging port Ultimate F1 Viewer is started with (set in the workspace).</summary>
    public int Uf1Port { get; set; } = 9333;

    // ---- F1 Mode
    /// <summary>"f1" = the desktop named F1 (made when missing); "current" = stay on the current desktop.</summary>
    public string ModeDesktop { get; set; } = "f1";
    /// <summary>Name of F1 Mode's own desktop.</summary>
    public string DesktopName { get; set; } = "F1";
    /// <summary>Settings layout version, for one-off migrations.</summary>
    public int SettingsVersion { get; set; }
    /// <summary>"auto" = by the live session, "race", "quali" or "none".</summary>
    public string ModeLayout { get; set; } = "auto";
    public bool ModeFlagsOn { get; set; } = true;

    // ---- Close F1
    public bool CloseMultiViewer { get; set; } = true;
    public bool CloseUf1 { get; set; } = true;
    public bool CloseDesktop { get; set; } = false;

    // ---- Flags
    public bool ShowYellow { get; set; } = true;
    public bool ShowSafetyCar { get; set; } = true; // SC and VSC
    public bool ShowRed { get; set; } = true;
    public bool ShowChequered { get; set; } = true;
    public bool VscPulse { get; set; } = true;

    // ---- Volume, latency, pause, live
    public int VolumeStep { get; set; } = 2;
    public int LatencyStep { get; set; } = 1;
    /// <summary>What pushing the latency dial does: "live" (back to live / 0s) or "pause".</summary>
    public string LatencyPush { get; set; } = "live";
    public bool LiveSyncAll { get; set; } = true;
    public bool PauseAll { get; set; } = true;

    // ---- Race / Quali layouts (Ultimate F1 Viewer layout ids; null = by name)
    public string? RaceLayout { get; set; }
    public string? QualiLayout { get; set; }
    /// <summary>MultiViewer setups opened with them (names; null = by name).</summary>
    public string? RaceMvSetup { get; set; }
    public string? QualiMvSetup { get; set; }
    /// <summary>Remote-debugging port MultiViewer is started with, to open its setups.</summary>
    public int MvPort { get; set; } = 9334;
    // Where the apps are, when the plugin cannot find them itself (mainly Linux: an AppImage or zip anywhere).
    public string? MultiViewerPath { get; set; }
    public string? Uf1Path { get; set; }
    /// <summary>Onboards of drivers outside these teams are "best of the rest" slots.</summary>
    public List<string> TopTeams { get; set; } = new() { "McLaren", "Ferrari", "Mercedes", "Red Bull Racing" };
    public bool RestEnabled { get; set; } = true;
    /// <summary>Keep the best-of-the-rest slots up to date during the session.</summary>
    public bool RestLive { get; set; } = true;

    // ---- Battle mode (Ultimate F1 Viewer) in the race layout
    public bool BattleEnabled { get; set; } = true;
    /// <summary>Right-hand battle mode: "champ3" = top 3 of the championship, "p5to8" = positions 5-8.</summary>
    public string BattleRight { get; set; } = "champ3";
    public bool BattleLive { get; set; } = true;

    /// <summary>No race/quali on: open the latest race or qualifying replay.</summary>
    public bool OpenLatestReplay { get; set; } = true;
    public double HoldSeconds { get; set; } = 2;
    public bool LayoutCloseCurrent { get; set; } = true;
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
            if (Current.ZoneList.Count > 0) // only an own zone list had these display names
            {
                foreach (var z in Current.ZoneList)
                    if (renamed.TryGetValue(z.Name, out var n)) z.Name = n;
                if (renamed.TryGetValue(Current.Group, out var g)) Current.Group = g;
            }
            // v2: F1 gets its own named desktop that stays (instead of "empty desktop 2", removed on close).
            if (Current.SettingsVersion < 2)
            {
                Current.ModeDesktop = "f1";
                Current.CloseDesktop = false;
                Current.SettingsVersion = 2;
                Save();
            }
            // Window places come from the old PowerToys workspace once, so it can be deleted afterwards.
            if (Current.PlaceMultiViewer == null || Current.PlaceUf1 == null) { Apps.ImportFromWorkspace(Current); Save(); }
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
