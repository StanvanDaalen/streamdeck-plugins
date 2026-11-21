namespace GameVolume;

/// <summary>
/// Player icons. When the official Elgato Spotify plugin is installed, its own icons are read from
/// that folder at runtime so the media dials look exactly like the Spotify dial. Otherwise our own drawings are used.
/// </summary>
internal static class Icons
{
    private static readonly string Official = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Elgato", "StreamDeck", "Plugins", "com.elgato.spotify.sdPlugin", "imgs");

    // name -> (file in the official plugin, our fallback)
    private static readonly Dictionary<string, (string Official, string Own)> Map = new()
    {
        ["play"] = ("playback/play@24.svg", "images/media/play.svg"),
        ["pause"] = ("playback/pause@24.svg", "images/media/pause.svg"),
        ["previous"] = ("playback/previous@24.svg", "images/media/previous.svg"),
        ["next"] = ("playback/next@24.svg", "images/media/next.svg"),
        ["volume0"] = ("volume/volume0@16.svg", "images/media/volume-muted.svg"),
        ["volume33"] = ("volume/volume33@16.svg", "images/media/volume.svg"),
        ["volume66"] = ("volume/volume66@16.svg", "images/media/volume.svg"),
        ["volume100"] = ("volume/volume100@16.svg", "images/media/volume.svg"),
        // No official counterpart: always our own drawing, in the same 24px style.
        ["speakers"] = ("-", "images/media/speakers.svg"),
        ["headphones"] = ("-", "images/media/headphones.svg"),
    };

    private static readonly Dictionary<string, string> Cache = new();
    private static DateTime _loaded = DateTime.MinValue;

    /// <summary>The icon as an image; with a colour, the exact same shape painted in that colour instead of white.</summary>
    public static string Get(string name, string? color = null)
    {
        // Re-read now and then so an update of the official plugin is picked up.
        if (DateTime.UtcNow - _loaded > TimeSpan.FromMinutes(10)) { Cache.Clear(); _loaded = DateTime.UtcNow; }
        var cacheKey = color == null ? name : name + "|" + color;
        if (Cache.TryGetValue(cacheKey, out var v)) return v;
        var (official, own) = Map[name];
        v = own;
        try
        {
            var path = Path.Combine(Official, official);
            if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, "..", own); // our own drawing
            if (File.Exists(path))
            {
                var svg = File.ReadAllText(path);
                if (color != null)
                    foreach (var white in new[] { "#fff\"", "#FFF\"", "#ffffff\"", "#FFFFFF\"" })
                        svg = svg.Replace("fill=\"" + white, "fill=\"" + color + "\"");
                v = "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));
            }
        }
        catch (Exception e) { SettingsStore.Log("icon " + name + ": " + e.Message); }
        return Cache[cacheKey] = v;
    }

    public static string Volume(int? level, bool muted) =>
        Get(muted || level is null or 0 ? "volume0" : level <= 33 ? "volume33" : level <= 66 ? "volume66" : "volume100");
}
