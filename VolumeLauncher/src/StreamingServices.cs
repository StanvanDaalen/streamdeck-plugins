using System.Text.RegularExpressions;

namespace GameVolume;

/// <summary>A video service an App Volume dial can recognise, with its colour and small black-and-white logo.</summary>
public sealed record StreamingService(string Id, string Name, string? Color, string[] TitleNames, string[] Processes)
{
    /// <summary>F1 TV, MotoGP and WEC: race and session instead of episode and series, with the country's flag.</summary>
    public bool Motorsport => Id is "f1tv" or "motogp" or "wec";

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
        // 1. The window showing what plays. 2. A title that signs with the service ("… - Paramount+", "Netflix: …"),
        // even when another service's tab is in front. 3. Any window's tab. 4. The service named anywhere in the text,
        // last so an episode called "Max" is not HBO Max.
        return (playing != null ? InText(playing) : null)
            ?? Signed(m.Title) ?? Signed(m.Artist)
            ?? titles.Select(InText).FirstOrDefault(s => s != null && s != YouTube)
            ?? InText(m.Artist) ?? InText(m.Title)
            ?? YouTube;
    }

    // " - Google Chrome", " - Microsoft Edge", " — Mozilla Firefox" at the end of a browser window's title.
    private static readonly Regex BrowserName = new(@"\s+[-–—]\s+(?:Google Chrome|Microsoft​?\s?Edge|Mozilla Firefox|Brave|Opera|Vivaldi)\s*$", RegexOptions.IgnoreCase);

    // Per browser: the reported title and the fuller tab title found for it, so it stays while another tab is in front.
    private static readonly Dictionary<string, (string Reported, string Tab)> Tabs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Some sites (Paramount+) tell Windows only the series ("FBI"), while their tab says it all:
    /// "Watch FBI Season 5 Episode 1: Hero's Journey - Paramount+". Then the tab's title is used instead,
    /// but only when it names a season or episode, so a film or a site's own good report is left alone.
    /// </summary>
    public static MediaInfo FromTab(string app, MediaInfo m, StreamingService s)
    {
        if (!IsBrowser(app) || s == YouTube || s.Motorsport || m.Artist.Length > 0) return m;
        var reported = m.Title.Trim();
        if (reported.Length < 2 || Regex.IsMatch(reported, Code, RegexOptions.IgnoreCase)) return m;
        var tab = WindowControl.WindowTitles(app)
            .Select(t => BrowserName.Replace(t, "").Trim())
            .FirstOrDefault(t => t.Length > reported.Length && t.Contains(reported, StringComparison.OrdinalIgnoreCase)
                                 && Regex.IsMatch(t, Code, RegexOptions.IgnoreCase));
        lock (Tabs)
        {
            if (tab != null) Tabs[app] = (reported, tab);
            else if (Tabs.TryGetValue(app, out var known) && known.Reported == reported) tab = known.Tab;
        }
        return tab != null ? m with { Title = tab } : m;
    }

    // The service's name as the title's first or last part: "… - Paramount+", "… | Netflix", "Disney+ | …".
    private static StreamingService? Signed(string text) =>
        text.Length == 0 ? null : All.FirstOrDefault(s => s.TitleNames.Any(n =>
            Regex.IsMatch(text, $@"(?:[|\-–—:·•]|\bon\b)\s*{Regex.Escape(n)}(?:\.\w+)?\s*$|^{Regex.Escape(n)}\s*[|\-–—:·•]", RegexOptions.IgnoreCase)));

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
        if (s.Motorsport) return Race(m, s);
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

    // Sessions, most specific first: "Sprint Qualifying" before "Sprint" and "Qualifying", "Race" last.
    private static readonly (Regex Pattern, string Name)[] Sessions =
    {
        (new(@"sprint\s*(qualifying|shootout)|\bSQ\b", RegexOptions.IgnoreCase), "Sprint Qualifying"),
        (new(@"\bsprint\b", RegexOptions.IgnoreCase), "Sprint"),
        (new(@"\bhyperpole\b", RegexOptions.IgnoreCase), "Hyperpole"),
        (new(@"\b(pre[\s-]?(race|qualifying|sprint)(\s*show)?|grid\s*walk|build[\s-]?up)\b", RegexOptions.IgnoreCase), "Pre-race"),
        (new(@"\b(post[\s-]?race(\s*show)?|press\s*conference)\b", RegexOptions.IgnoreCase), "Post-race"),
        (new(@"\bhighlights\b", RegexOptions.IgnoreCase), "Highlights"),
        (new(@"\bwarm[\s-]?up\b", RegexOptions.IgnoreCase), "Warm Up"),
        (new(@"\b(qualifying|quali|Q[12])\b", RegexOptions.IgnoreCase), "Qualifying"),
        (new(@"\b(?:free\s*)?practice\s*(?<n>\d)|\bFP\s*(?<n>\d)\b|\bpractice\b", RegexOptions.IgnoreCase), "Practice"),
        (new(@"\b(race|feature\s*race)\b", RegexOptions.IgnoreCase), "Race"),
    };

    // "Spanish Grand Prix", "6 Hours of Spa-Francorchamps", "24 Hours of Le Mans", "Qatar 1812 km", "Lone Star Le Mans".
    private static readonly Regex Event = new(
        @"(?<name>(?:[\p{Lu}][\p{L}'’.&-]*\s+){1,3}?)(?:Grand\s*Prix|GP)\b|(?<full>\d+\s*Hours\s+of\s+[\p{L}][\p{L}'’.-]*(?:\s+[\p{Lu}][\p{L}'’.-]*)*)|(?<full>[\p{Lu}][\p{L}]*\s*\d{3,4}\s*km)\b|(?<full>Lone Star Le Mans)",
        RegexOptions.IgnoreCase);

    // Words in the event name -> the weekend's country (ISO code), for its flag. F1, MotoGP and WEC venues.
    private static readonly (string Word, string Code)[] Countries =
    {
        ("Emilia", "it"), ("Romagna", "it"), ("Imola", "it"), ("Italian", "it"), ("Italy", "it"), ("Monza", "it"), ("Mugello", "it"), ("San Marino", "sm"),
        ("Abu Dhabi", "ae"), ("Saudi", "sa"), ("Bahrain", "bh"), ("Qatar", "qa"), ("Lusail", "qa"),
        ("Australian", "au"), ("Australia", "au"), ("Japanese", "jp"), ("Japan", "jp"), ("Fuji", "jp"), ("Motegi", "jp"), ("Chinese", "cn"), ("China", "cn"),
        ("Miami", "us"), ("Las Vegas", "us"), ("United States", "us"), ("Americas", "us"), ("Lone Star", "us"), ("Austin", "us"), ("COTA", "us"), ("Sebring", "us"),
        ("Monaco", "mc"), ("Canadian", "ca"), ("Canada", "ca"),
        ("Spanish", "es"), ("Spain", "es"), ("España", "es"), ("Espana", "es"), ("Aragón", "es"), ("Valenciana", "es"),
        ("Italia", "it"), ("Deutschland", "de"), ("Österreich", "at"), ("Barcelona", "es"), ("Catalan", "es"), ("Catalunya", "es"), ("Madrid", "es"), ("Aragon", "es"), ("Valencia", "es"), ("Jerez", "es"),
        ("Austrian", "at"), ("Austria", "at"), ("British", "gb"), ("Silverstone", "gb"), ("Great Britain", "gb"),
        ("Hungarian", "hu"), ("Hungary", "hu"), ("Balaton", "hu"), ("Belgian", "be"), ("Belgium", "be"), ("Spa", "be"),
        ("Dutch", "nl"), ("Netherlands", "nl"), ("Assen", "nl"), ("Zandvoort", "nl"),
        ("Azerbaijan", "az"), ("Baku", "az"), ("Singapore", "sg"), ("Mexico", "mx"), ("Mexican", "mx"),
        ("São Paulo", "br"), ("Sao Paulo", "br"), ("Brazilian", "br"), ("Brazil", "br"), ("Interlagos", "br"),
        ("Le Mans", "fr"), ("French", "fr"), ("France", "fr"), ("Portuguese", "pt"), ("Portugal", "pt"), ("Portimão", "pt"), ("Portimao", "pt"),
        ("German", "de"), ("Germany", "de"), ("Sachsenring", "de"), ("Czech", "cz"), ("Brno", "cz"),
        ("Thai", "th"), ("Thailand", "th"), ("Argentine", "ar"), ("Argentina", "ar"), ("Indonesian", "id"), ("Indonesia", "id"), ("Mandalika", "id"),
        ("Malaysian", "my"), ("Malaysia", "my"), ("Sepang", "my"), ("Kazakhstan", "kz"), ("Indian", "in"), ("India", "in"), ("Turkish", "tr"), ("Turkey", "tr"),
    };

    private static readonly Dictionary<string, string> Demonyms = new()
    {
        ["es"] = "Spanish", ["it"] = "Italian", ["sm"] = "San Marino", ["pt"] = "Portuguese", ["fr"] = "French", ["de"] = "German",
        ["at"] = "Austrian", ["cz"] = "Czech", ["nl"] = "Dutch", ["gb"] = "British", ["jp"] = "Japanese", ["au"] = "Australian",
        ["my"] = "Malaysian", ["th"] = "Thai", ["id"] = "Indonesian", ["ar"] = "Argentine", ["qa"] = "Qatar", ["hu"] = "Hungarian",
        ["mx"] = "Mexico City", ["br"] = "Brazilian", ["be"] = "Belgian", ["ca"] = "Canadian", ["mc"] = "Monaco", ["cn"] = "Chinese",
    };

    // Regions with their own weekend in a country that has several (MotoGP in Spain and Italy).
    private static readonly (string Word, string Name)[] Regions =
    {
        ("Valencian", "Valencia"), ("Valenciana", "Valencia"), ("Catalunya", "Catalan"), ("Aragón", "Aragon"), ("Aragon", "Aragon"),
        ("Jerez", "Spanish"), ("Emilia", "Emilia Romagna"), ("San Marino", "San Marino"), ("Americas", "Americas"), ("Mugello", "Italian"),
    };

    private static string? CountryOf(string text) =>
        Countries.FirstOrDefault(c => Regex.IsMatch(text, $@"(?<!\p{{L}}){Regex.Escape(c.Word)}(?!\p{{L}})", RegexOptions.IgnoreCase)).Code;

    /// <summary>Motorsport: the event on top (e.g. "Spanish GP"), the session below (Practice 1, Qualifying, Race, …).</summary>
    private static MediaInfo Race(MediaInfo m, StreamingService s)
    {
        var text = $"{Strip(m.Title, s)} - {Strip(m.Artist, s)}".Trim(' ', '-');
        // Sponsor and series names in official event names only make it longer ("FORMULA 1 ARAMCO GRAN PREMIO …").
        text = Regex.Replace(text, @"\b(FORMULA\s*1|F1|MotoGP™?|FIA\s*WEC|WEC|FIA)\b", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\s{2,}", " ").Trim();

        string session = "";
        foreach (var (pattern, name) in Sessions)
            if (pattern.Match(text) is { Success: true } sm)
            {
                session = name == "Practice" && sm.Groups["n"].Success ? $"Practice {sm.Groups["n"].Value}" : name;
                break;
            }

        string evt = "";
        if (Event.Match(text) is { Success: true } em)
            evt = em.Groups["full"].Success ? em.Groups["full"].Value.Trim() : $"{em.Groups["name"].Value.Trim()} GP";
        if (evt.Length == 0)
        {
            // No known event name: the first part of the title that is not only the session.
            evt = text.Split(new[] { " - ", " | ", " – ", ": " }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(p => p.Trim()).FirstOrDefault(p => !Sessions.Any(x => x.Pattern.IsMatch(p)) || p.Length > 25) ?? "";
        }
        evt = Regex.Replace(evt, @"\s+\d{4}$", "").Trim();
        var country = CountryOf(text);
        // Official names ("Gran Premio Red Bull de España", "Grand Prix of the Americas") become the short English one.
        if (Regex.Match(evt, @"\b(Gran\s+Premio|Grand\s+Prix|Großer\s+Preis|Grande\s+Prémio)\b(?<rest>.*)$", RegexOptions.IgnoreCase) is { Success: true } official)
        {
            var region = Regions.FirstOrDefault(r => official.Value.Contains(r.Word, StringComparison.OrdinalIgnoreCase)).Name;
            if (region != null) evt = $"{region} GP";
            else if (Regex.Match(official.Groups["rest"].Value, @"^\s*of\s+(the\s+)?(?<place>[\p{L}\s]+)$", RegexOptions.IgnoreCase) is { Success: true } of)
                evt = $"{of.Groups["place"].Value.Trim()} GP";
            else if (country != null && Demonyms.TryGetValue(country, out var demonym))
                evt = $"{demonym} GP";
        }
        if (evt.Length > 0 && evt == evt.ToUpperInvariant())
            evt = Regex.Replace(System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(evt.ToLowerInvariant()), @"\bGp\b", "GP");
        if (evt.Length == 0) evt = session.Length > 0 ? session : s.Name;
        if (evt == session) session = "";
        return m with { Title = evt, Artist = session, Country = country };
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
