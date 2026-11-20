namespace StanSpotify;

/// <summary>
/// Player icons. When the official Elgato Spotify plugin is installed, its own icons are read from
/// that folder at runtime so the dial looks exactly like the original. Otherwise our own drawings are used.
/// </summary>
internal static class Icons
{
    private static readonly string Official = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Elgato", "StreamDeck", "Plugins", "com.elgato.spotify.sdPlugin", "imgs");

    // name -> (file in the official plugin, our fallback)
    private static readonly Dictionary<string, (string Official, string Own)> Map = new()
    {
        ["play"] = ("playback/play@24.svg", "imgs/play.svg"),
        ["pause"] = ("playback/pause@24.svg", "imgs/pause.svg"),
        ["shuffleOff"] = ("shuffle/player/off@24.svg", "imgs/shuffle-off.svg"),
        ["shuffleOn"] = ("shuffle/player/on@24.svg", "imgs/shuffle-on.svg"),
        ["repeatOff"] = ("repeat/player/off@24.svg", "imgs/repeat-off.svg"),
        ["repeatList"] = ("repeat/player/playlist@24.svg", "imgs/repeat-on.svg"),
        ["repeatOne"] = ("repeat/player/single@24.svg", "imgs/repeat-one.svg"),
        ["volume0"] = ("volume/volume0@16.svg", "imgs/volume-muted.svg"),
        ["volume33"] = ("volume/volume33@16.svg", "imgs/volume.svg"),
        ["volume66"] = ("volume/volume66@16.svg", "imgs/volume.svg"),
        ["volume100"] = ("volume/volume100@16.svg", "imgs/volume.svg"),
        ["noArtwork"] = ("playback/noartwork.svg", "imgs/noartwork.svg"),
        ["notRunning"] = ("playback/notrunningdial.svg", "imgs/notrunning.svg"),
    };

    private static readonly Dictionary<string, string> Cache = new();
    private static DateTime _loaded = DateTime.MinValue;

    public static string Get(string name)
    {
        // Re-read now and then so an update of the official plugin is picked up.
        if (DateTime.UtcNow - _loaded > TimeSpan.FromMinutes(10)) { Cache.Clear(); _loaded = DateTime.UtcNow; }
        if (Cache.TryGetValue(name, out var v)) return v;
        var (official, own) = Map[name];
        v = own;
        try
        {
            var path = Path.Combine(Official, official);
            if (File.Exists(path))
                v = "data:image/svg+xml;base64," + Convert.ToBase64String(File.ReadAllBytes(path));
        }
        catch (Exception e) { Log.Write("icon " + name + ": " + e.Message); }
        return Cache[name] = v;
    }

    public static string Volume(int? level, bool muted) =>
        Get(muted || level is null or 0 ? "volume0" : level <= 33 ? "volume33" : level <= 66 ? "volume66" : "volume100");
}
