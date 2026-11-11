using System.Net;

namespace F1;

/// <summary>Key and action icons as SVG, so they can be recoloured at runtime.</summary>
public static class Art
{
    public const string Red = "#E10600", Dark = "#15151E", Grey = "#38383F";

    // Base64 rather than raw text: the '#' in colours would otherwise end the data URL.
    public static string DataUrl(string svg) =>
        "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));

    /// <summary>A 144×144 key: coloured tile, glyph in the middle, label at the bottom.</summary>
    public static string Key(string glyph, string label, string bg = Dark, string fg = "#FFFFFF", string? badge = null, string accent = Red)
    {
        var b = badge == null ? "" :
            $"<rect x='86' y='10' width='48' height='24' rx='12' fill='{fg}' fill-opacity='0.92'/>" +
            $"<text x='110' y='27' font-family='Segoe UI' font-size='14' font-weight='700' text-anchor='middle' fill='{bg}'>{E(badge)}</text>";
        return "<svg xmlns='http://www.w3.org/2000/svg' width='144' height='144' viewBox='0 0 144 144'>" +
               "<clipPath id='tile'><rect width='144' height='144' rx='18'/></clipPath>" +
               $"<g clip-path='url(#tile)'><rect width='144' height='144' fill='{bg}'/>" +
               $"<rect x='0' y='0' width='144' height='7' fill='{accent}'/></g>" +
               $"<g fill='{fg}' stroke='{fg}' transform='translate(72 62)'>{glyph}</g>" +
               $"<text x='72' y='128' font-family='Segoe UI' font-size='19' font-weight='700' text-anchor='middle' fill='{fg}'>{E(label)}</text>" +
               b + "</svg>";
    }

    /// <summary>20×20-ish action-list icon (scaled by Stream Deck).</summary>
    public static string Icon(string glyph) =>
        "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40' viewBox='-36 -36 72 72'>" +
        $"<g fill='#FFFFFF' stroke='#FFFFFF'>{glyph}</g></svg>";

    private static string E(string s) => WebUtility.HtmlEncode(s);

    // Glyphs are drawn around (0,0), roughly 64 px wide.
    public const string Flag =
        "<path d='M-20 -30 V34' stroke-width='6' stroke-linecap='round' fill='none'/>" +
        "<path d='M-17 -28 C0 -36 10 -20 30 -26 V6 C10 12 0 -4 -17 4 Z' stroke='none'/>";

    public const string Chequered =
        "<path d='M-20 -30 V34' stroke-width='6' stroke-linecap='round' fill='none'/>" +
        "<rect x='-17' y='-28' width='47' height='32' fill='none' stroke-width='3'/>" +
        "<path d='M-17 -28h12v8h-12zM7 -28h12v8h-12zM-5 -20h12v8h-12zM19 -20h11v8h-11zM-17 -12h12v8h-12zM7 -12h12v8h-12zM-5 -4h12v8h-12zM19 -4h11v8h-11z' stroke='none'/>";

    public const string Live =
        "<circle cx='-36' cy='0' r='8' stroke='none'/>" +
        "<text x='8' y='10' font-family='Segoe UI' font-size='28' font-weight='800' text-anchor='middle' stroke='none'>LIVE</text>";

    public const string Sync =
        "<path d='M-24 -4 A24 24 0 0 1 18 -18' fill='none' stroke-width='6' stroke-linecap='round'/>" +
        "<path d='M24 4 A24 24 0 0 1 -18 18' fill='none' stroke-width='6' stroke-linecap='round'/>" +
        "<path d='M10 -28 L26 -20 L14 -6 Z' stroke='none'/><path d='M-10 28 L-26 20 L-14 6 Z' stroke='none'/>";

    public const string Play = "<path d='M-14 -24 L24 0 L-14 24 Z' stroke='none'/>";
    public const string Pause = "<rect x='-20' y='-24' width='14' height='48' rx='3' stroke='none'/><rect x='6' y='-24' width='14' height='48' rx='3' stroke='none'/>";
    public const string PlayPause =
        "<path d='M-30 -20 L-2 0 L-30 20 Z' stroke='none'/>" +
        "<rect x='6' y='-20' width='9' height='40' rx='2' stroke='none'/><rect x='21' y='-20' width='9' height='40' rx='2' stroke='none'/>";

    public const string Car =
        "<path d='M-34 10 L-30 -2 L-14 -6 L-6 -16 L14 -16 L20 -6 L34 -2 L34 10 Z' stroke='none'/>" +
        "<circle cx='-18' cy='12' r='8' stroke='none'/><circle cx='20' cy='12' r='8' stroke='none'/>";

    public const string Stop =
        "<rect x='-30' y='-22' width='44' height='34' rx='4' fill='none' stroke-width='5'/>" +
        "<path d='M2 0 H34 M22 -12 L34 0 L22 12' fill='none' stroke-width='6' stroke-linecap='round' stroke-linejoin='round'/>";

    public const string Timer =
        "<circle cx='0' cy='4' r='26' fill='none' stroke-width='6'/>" +
        "<path d='M0 4 V-12 M-8 -30 H8' fill='none' stroke-width='6' stroke-linecap='round'/>";

    public const string Bulb =
        "<path d='M0 -32 A22 22 0 0 1 13 8 V16 H-13 V8 A22 22 0 0 1 0 -32 Z' stroke='none'/>" +
        "<rect x='-11' y='20' width='22' height='5' rx='2' stroke='none'/><rect x='-8' y='28' width='16' height='5' rx='2' stroke='none'/>";

    public const string Speaker =
        "<path d='M-28 -10 H-14 L4 -26 V26 L-14 10 H-28 Z' stroke='none'/>" +
        "<path d='M14 -12 A16 16 0 0 1 14 12 M22 -22 A28 28 0 0 1 22 22' fill='none' stroke-width='5' stroke-linecap='round'/>";

    public const string Layout =
        "<rect x='-32' y='-24' width='40' height='26' rx='3' stroke='none'/>" +
        "<rect x='12' y='-24' width='20' height='12' rx='2' stroke='none'/><rect x='12' y='-10' width='20' height='12' rx='2' stroke='none'/>" +
        "<rect x='-32' y='6' width='20' height='18' rx='2' stroke='none'/><rect x='-8' y='6' width='20' height='18' rx='2' stroke='none'/><rect x='16' y='6' width='16' height='18' rx='2' stroke='none'/>";

    public const string Test =
        "<path d='M-20 -30 V34' stroke-width='6' stroke-linecap='round' fill='none'/>" +
        "<path d='M-17 -28 H26 V4 H-17 Z' stroke='none' fill-opacity='0.35'/>" +
        "<path d='M-6 -14 L4 -4 L22 -24' fill='none' stroke-width='6' stroke-linecap='round' stroke-linejoin='round'/>";

    /// <summary>Static images written into the plugin folder at build time (name → svg).</summary>
    public static IEnumerable<(string Name, string Svg)> Assets()
    {
        yield return ("plugin", Key(Flag, "F1", Red));
        yield return ("category", Icon(Flag));
        foreach (var (name, glyph) in new[]
        {
            ("mode", Car), ("stop", Stop), ("flags", Flag), ("zone", Bulb), ("volume", Speaker),
            ("latency", Timer), ("live", Live), ("sync", Sync), ("test", Test), ("pause", PlayPause), ("layout", Layout),
        })
            yield return ("action-" + name, Icon(glyph));
        yield return ("key-mode", Key(Car, "F1-modus", Red));
        yield return ("key-stop", Key(Stop, "F1 stoppen", Grey));
        yield return ("key-flags", Key(Flag, "Vlaggen"));
        yield return ("key-live", Key(Live, "Naar live", accent: Red));
        yield return ("key-sync", Key(Sync, "Sync spelers"));
        yield return ("key-test", Key(Test, "Test vlaggen"));
        yield return ("key-pause", Key(PlayPause, "Pauze / play"));
        yield return ("key-race", Key(Layout, "Race", badge: "R"));
        yield return ("key-quali", Key(Layout, "Kwali", badge: "Q"));
    }
}
