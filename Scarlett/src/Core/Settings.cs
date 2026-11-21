using System.Text.Json;

namespace ScarlettSwitch.Core;

public enum Output { Speakers, Headphones }

/// <summary>What to do when Focusrite Control 2 is closed and a switch is requested.</summary>
public enum FocusriteMode
{
    /// <summary>Start it hidden, switch, close it again after a short idle period.</summary>
    AutoClose,
    /// <summary>Start it hidden and leave it running.</summary>
    KeepRunning,
    /// <summary>Never start it; switching only works while the user has it open.</summary>
    Manual,
}

public sealed class Settings
{
    public Output Output { get; set; } = Output.Speakers;
    /// <summary>Focusrite Control 2 mix (0 = Mix A, 1 = Mix B, 2 = Mix C) routed to Output 1-2 (speakers).</summary>
    public int SpeakerMix { get; set; } = 1;
    /// <summary>Focusrite Control 2 mix routed to the headphone output.</summary>
    public int HeadphoneMix { get; set; } = 0;
    /// <summary>Part of the MIDI output port name that Focusrite Control 2 listens to.</summary>
    public string MidiPort { get; set; } = "Scarlett Switch";
    /// <summary>Windows volume change per dial tick, in percent.</summary>
    public int VolumeStep { get; set; } = 2;
    public FocusriteMode FocusriteMode { get; set; } = FocusriteMode.AutoClose;
    /// <summary>Extra idle seconds (after the ~7 s start-up window) before an auto-started FC2 is closed.</summary>
    public int CloseAfterSeconds { get; set; } = 3;

    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScarlettSwitch");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { /* corrupt file: fall back to defaults */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { /* non-fatal */ }
    }
}
