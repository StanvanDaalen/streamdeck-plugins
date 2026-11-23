using System.Text.RegularExpressions;

namespace GameVolume;

/// <summary>A video service an App Volume dial can recognise, with its colour and small black-and-white logo.</summary>
public sealed record StreamingService(string Id, string Name, string? Color, string[] TitleNames, string[] Processes)
{
    public string Badge => $"images/media/badge-{Id}.svg";
}

/// <summary>
/// Works out which video service an app is playing, and turns what the service reports into
/// "episode on top, series below", like YouTube's video and channel.
/// </summary>
public static class StreamingServices
{
    // TitleNames: how the service names itself in the tab title or the media info (longest first where they overlap).
    // Processes: the service's own Windows app, for when the dial is set to that app instead of a browser.
    // Apple TV has no colour of its own (it is black and white), so it keeps the standard white.
    public static readonly StreamingService[] All =
    {
        new("youtube", "YouTube", "#FF0033", new[] { "YouTube" }, Array.Empty<string>()),
        new("netflix", "Netflix", "#E50914", new[] { "Netflix" }, new[] { "netflix" }),
        new("hbomax", "HBO Max", "#2F5BFF", new[] { "HBO Max", "Max" }, new[] { "hbomax", "max" }),
        new("paramount", "Paramount+", "#0064FF", new[] { "Paramount+", "Paramount Plus" }, new[] { "paramountplus", "paramount" }),
        new("viaplay", "Viaplay", "#FF2A63", new[] { "Viaplay" }, new[] { "viaplay" }),
        new("videoland", "Videoland", "#E4002B", new[] { "Videoland" }, new[] { "videoland" }),
        new("skyshowtime", "SkyShowtime", "#8A4DFF", new[] { "SkyShowtime" }, new[] { "skyshowtime" }),
        new("f1tv", "F1 TV", "#E10600", new[] { "F1 TV", "F1TV" }, new[] { "f1tv" }),
        new("motogp", "MotoGP", "#D7192D", new[] { "MotoGP VideoPass", "MotoGP™ VideoPass", "MotoGP™", "MotoGP" }, new[] { "motogp" }),
        new("wec", "WEC", "#00A3E0", new[] { "FIA WEC TV", "WEC TV", "FIA WEC", "WEC" }, new[] { "wec" }),
        new("disney", "Disney+", "#1F80E0", new[] { "Disney+", "Disney Plus" }, new[] { "disneyplus", "disney" }),
        new("appletv", "Apple TV", null, new[] { "Apple TV+", "Apple TV" }, new[] { "appletv" }),
        new("prime", "Prime Video", "#1A98FF", new[] { "Prime Video", "Amazon Prime Video" }, new[] { "primevideo", "amazonprimevideo" }),
    };

    public static readonly StreamingService YouTube = All[0];

    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase) { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi" };

    public static bool IsBrowser(string app) => Browsers.Contains(app);

    /// <summary>The service whose own app this is, or null.</summary>
    public static StreamingService? ForProcess(string app) =>
        All.FirstOrDefault(s => s.Processes.Any(p => p.Equals(app, StringComparison.OrdinalIgnoreCase)));

    // A service name as a separate word, so "Max" never matches "Maximum".
    private static bool Names(string text, string name) =>
        Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase);

    private static StreamingService? InText(string text) =>
        text.Length == 0 ? null : All.FirstOrDefault(s => s.TitleNames.Any(n => Names(text, n)));

    /// <summary>
    /// Which service the app is playing. A browser is matched on its window titles (the active tab), preferring the
    /// window that shows what is playing; without a match it stays YouTube, which is what browsers mostly play.
    /// </summary>
    public static StreamingService? Detect(string app, MediaInfo m)
    {
        if (ForProcess(app) is { } own) return own;
        if (!IsBrowser(app)) return null;
        var titles = WindowControl.WindowTitles(app);
        var playing = titles.FirstOrDefault(t => m.Title.Length > 3 && t.Contains(m.Title, StringComparison.OrdinalIgnoreCase));
        // The tab before the episode's own name: an episode called "Max" is not HBO Max.
        return (playing != null ? InText(playing) : null)
            ?? titles.Select(InText).FirstOrDefault(s => s != null && s != YouTube)
            ?? InText(m.Artist) ?? InText(m.Title)
            ?? YouTube;
    }

    // "S1:E3", "S1 E3", "Season 1 Episode 8", "Seizoen 1 Aflevering 2", "Episode 3", "Ep. 3".
    private const string Code = @"\b(?:S(?:eason|eizoen)?\s*(?<s>\d+)\s*[:,.]?\s*(?:E|Ep\.?|Episode|Aflevering)\s*(?<e>\d+)|(?:Episode|Aflevering|Ep\.?)\s*(?<e>\d+))\b";

    // Series, then the code (with or without a separator), then the episode's own name.
    private static readonly Regex Episode = new($@"^(?<series>.+?)(?:\s*[:\-–|,]\s*|\s+){Code}\s*(?:[:\-–|.]\s*)?(?<episode>.*)$", RegexOptions.IgnoreCase);
    // Only a code in front of the episode's name, when the series comes separately.
    private static readonly Regex Leading = new($@"^{Code}\s*(?:[:\-–|.]\s*)?(?<episode>.*)$", RegexOptions.IgnoreCase);

    /// <summary>
    /// The episode on top and the series below. The service's own name and "Watch" are removed from both, and a title
    /// like "Series Season 1 Episode 8: Episode" is split in two; the season and episode numbers are kept for the
    /// episode picture. Returns the media unchanged for YouTube.
    /// </summary>
    public static MediaInfo Tidy(MediaInfo m, StreamingService s)
    {
        if (s == YouTube) return m;
        string title = Strip(m.Title, s), artist = Strip(m.Artist, s);
        if (artist.Equals(title, StringComparison.OrdinalIgnoreCase)) artist = "";
        int? season = null, number = null;
        var e = artist.Length == 0 ? Episode.Match(title) : Leading.Match(title);
        if (e.Success)
        {
            if (int.TryParse(e.Groups["s"].Value, out var sn)) season = sn;
            if (int.TryParse(e.Groups["e"].Value, out var en)) number = en;
            var episode = e.Groups["episode"].Value.Trim(' ', '"', '“', '”');
            var code = season != null ? $"S{season}:E{number}" : $"Episode {number}";
            title = episode.Length > 0 ? episode : code;
            if (artist.Length == 0) artist = e.Groups["series"].Value.Trim();
        }
        // Nothing left but the service's name (Netflix shows no episode in its tab): show the service itself.
        if (title.Length == 0) { title = artist.Length > 0 ? artist : s.Name; artist = ""; }
        return m with { Title = title, Artist = artist, Season = season, Episode = number };
    }

    private static string Strip(string text, StreamingService s)
    {
        text = text.Trim();
        foreach (var n in s.TitleNames)
        {
            var name = Regex.Escape(n);
            // "… | Netflix", "… - Disney+", "Netflix: …", "… on Prime Video", and a bare "netflix.com".
            text = Regex.Replace(text, $@"\s*(?:[|\-–—:·•]|\bon\b|\bop\b)\s*{name}(?:\.\w+)?\s*$", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, $@"^{name}(?:\.\w+)?\s*[|\-–—:·•]\s*", "", RegexOptions.IgnoreCase);
            if (Regex.IsMatch(text, $@"^(?:www\.)?{name.Replace(@"\ ", "")}(?:\.\w+)*$", RegexOptions.IgnoreCase)
                || text.Equals(n, StringComparison.OrdinalIgnoreCase)) text = "";
        }
        text = Regex.Replace(text, @"^(?:Watch|Kijk|Bekijk)\s+", "", RegexOptions.IgnoreCase);
        return text.Trim(' ', '|', '-', '–', ':', '·');
    }
}
