using System.Drawing.Imaging;
using System.Globalization;
using System.Net;
using Svg;

namespace F1;

/// <summary>
/// All artwork in one style: dark tile with a thin edge, white glyph with F1-red accents,
/// white label at the bottom and an optional status pill top right. Glyphs are drawn around
/// (0,0) in a box of roughly 90×70.
/// </summary>
public static class Art
{
    // formula1.com palette: F1 red, the near-black navy of the site, carbon greys.
    public const string Red = "#E10600", Tile = "#15151E", Edge = "#38383F", White = "#FFFFFF", Grey = "#949498";
    public const string Green = "#21C55D", Amber = "#FFB800";
    private const string Font = "Segoe UI Black, Segoe UI, Arial Black, sans-serif";

    // ------------------------------------------------------------ keys

    /// <summary>
    /// A key in the style of formula1.com cards: dark navy tile with the red border that runs along
    /// the top and round the top-right corner, glyph in the middle, heavy capitals at the bottom,
    /// optional status tag top left.
    /// </summary>
    /// <param name="upper">Labels are capitals; times like "-40s" keep their small s.</param>
    public static string Key(string glyph, string label, string? pill = null, string pillColor = Green, string labelColor = White, bool upper = true) =>
        Frame(pill, pillColor) +
        $"<g transform='translate(72 62)'>{glyph}</g>" +
        $"<text x='72' y='124' font-family='{Font}' font-size='17' font-weight='900' letter-spacing='0.4' text-anchor='middle' fill='{labelColor}'>{E(upper ? label.ToUpperInvariant() : label)}</text>" +
        "</svg>";

    private static string Frame(string? pill, string pillColor)
    {
        var p = pill == null ? "" :
            $"<rect x='8' y='10' width='32' height='18' rx='4' fill='{pillColor}'/>" +
            $"<text x='24' y='23.5' font-family='{Font}' font-size='11' font-weight='900' text-anchor='middle' fill='{Tile}'>{E(pill)}</text>";
        return "<svg xmlns='http://www.w3.org/2000/svg' width='144' height='144' viewBox='0 0 144 144'>" +
               $"<rect x='1' y='1' width='142' height='142' rx='18' fill='{Tile}' stroke='{Edge}' stroke-width='1.5'/>" +
               // the F1 site's red corner: along the top edge, round the corner and down the right side
               $"<path d='M62 1.75 H124 A18.25 18.25 0 0 1 142.25 20 V82' fill='none' stroke='{Red}' stroke-width='3.5' stroke-linecap='round'/>" +
               p;
    }

    /// <summary>
    /// An information key: a small title on top, one big value (a time, laps, a temperature) and
    /// a caption under it, like the timing graphics.
    /// </summary>
    public static string InfoKey(string title, string big, string caption, string bigColor, string? captionLead = null) =>
        Frame(null, Green) +
        $"<text x='72' y='38' font-family='{Font}' font-size='15' font-weight='900' letter-spacing='0.4' text-anchor='middle' fill='{Red}'>{E(title.ToUpperInvariant())}</text>" +
        $"<text x='72' y='84' font-family='{Font}' font-size='{(big.Length > 6 ? 28 : 36)}' font-weight='900' text-anchor='middle' fill='{bigColor}'>{E(big)}</text>" +
        $"<text x='72' y='112' font-family='{Font}' font-size='13' font-weight='700' text-anchor='middle' fill='{Grey}'>{E((captionLead != null ? captionLead + " · " : "") + caption)}</text>" +
        "</svg>";

    private static string? LogoFile => new[] { "f1-logo.svg", "f1-logo.png" }.Select(f => Path.Combine(Store.Dir, f)).FirstOrDefault(File.Exists);
    public static bool HasLogo => LogoFile != null;

    /// <summary>
    /// A key with the user's own logo file (for example the official F1 logo) instead of a glyph,
    /// as PNG. The logo stays on this PC: it is read from the settings folder, never shipped.
    /// </summary>
    public static string? KeyWithLogo(string label, string? pill = null, string pillColor = Green)
    {
        var file = LogoFile;
        if (file == null) return null;
        try
        {
            var key = $"{file}|{File.GetLastWriteTimeUtc(file).Ticks}|{label}|{pill}|{pillColor}";
            if (PngCache.TryGetValue(key, out var hit)) return hit;
            var tile = SvgDocument.FromSvg<SvgDocument>(Frame(pill, pillColor) +
                $"<text x='72' y='124' font-family='{Font}' font-size='17' font-weight='900' letter-spacing='0.4' text-anchor='middle' fill='{White}'>{E(label.ToUpperInvariant())}</text></svg>");
            using var bmp = tile.Draw(144, 144);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var logo = file.EndsWith(".svg")
                    ? SvgDocument.Open(file).Draw(400, 0)
                    : new System.Drawing.Bitmap(file);
                // Fit inside 104×56 above the label, centred.
                double s = Math.Min(104.0 / logo.Width, 56.0 / logo.Height);
                int w = (int)(logo.Width * s), h = (int)(logo.Height * s);
                g.DrawImage(logo, 72 - w / 2, 62 - h / 2, w, h);
            }
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return PngCache[key] = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        }
        catch (Exception e) { Store.Log("logo: " + e.Message); return null; }
    }

    /// <summary>"F1" in heavy italic with speed lines, in the spirit of F1 graphics (not the trademarked logo).</summary>
    public static string F1Mark() =>
        $"<path d='M-46 -10 H-22 M-50 0 H-26 M-46 10 H-30' stroke='{White}' stroke-width='4' stroke-linecap='round'/>" +
        $"<text x='12' y='17' font-family='{Font}' font-size='50' font-weight='900' font-style='italic' text-anchor='middle' fill='{Red}' letter-spacing='-2'>F1</text>";

    /// <summary>Action-list / dial icon: only the glyph, on a transparent background.</summary>
    public static string Icon(string glyph, int size = 72) =>
        $"<svg xmlns='http://www.w3.org/2000/svg' width='{size}' height='{size}' viewBox='-50 -50 100 100'>{glyph}</svg>";

    public static string DataUrl(string svg) =>
        "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));

    private static readonly Dictionary<string, string> PngCache = new();

    /// <summary>SVG rendered to a PNG data URL (the touch strip shows PNG reliably).</summary>
    public static string Png(string svg, int size)
    {
        var key = size + svg;
        if (PngCache.TryGetValue(key, out var hit)) return hit;
        var doc = SvgDocument.FromSvg<SvgDocument>(svg);
        doc.Width = size; doc.Height = size;
        using var bmp = doc.Draw(size, size);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        var url = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        if (PngCache.Count > 64) PngCache.Clear();
        return PngCache[key] = url;
    }

    private static string E(string s) => WebUtility.HtmlEncode(s);
    private static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------ glyphs

    /// <summary>Waving flag on a pole.</summary>
    public static string Flag(string color = Red) =>
        $"<path d='M-26 -32 V34' stroke='{White}' stroke-width='6' stroke-linecap='round'/>" +
        $"<path d='M-23 -30 C-6 -38 6 -22 30 -28 V4 C6 10 -6 -6 -23 2 Z' fill='{color}'/>";

    /// <summary>Chequered flag (race).</summary>
    public static string Chequered()
    {
        var squares = "";
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 6; c++)
                if ((r + c) % 2 == 0) squares += $"<rect x='{-23 + c * 9}' y='{-30 + r * 8}' width='9' height='8' fill='{White}'/>";
        return $"<path d='M-26 -32 V34' stroke='{White}' stroke-width='6' stroke-linecap='round'/>" +
               $"<rect x='-23' y='-30' width='54' height='32' fill='#050507' stroke='{White}' stroke-width='2'/>" + squares;
    }

    /// <summary>Stopwatch with a red hand (qualifying).</summary>
    public static string Stopwatch(string hand = Red) =>
        $"<circle cx='0' cy='4' r='27' fill='none' stroke='{White}' stroke-width='6'/>" +
        $"<rect x='-7' y='-33' width='14' height='7' rx='2' fill='{White}'/>" +
        $"<path d='M19 -20 L24 -25' stroke='{White}' stroke-width='5' stroke-linecap='round'/>" +
        $"<path d='M0 4 L12 -10' stroke='{hand}' stroke-width='5' stroke-linecap='round'/>" +
        $"<circle cx='0' cy='4' r='4' fill='{hand}'/>";

    /// <summary>Red "on air" dot with rings plus LIVE.</summary>
    public static string Live(string dot = Red) =>
        $"<circle cx='-38' cy='-2' r='7' fill='{dot}'/>" +
        $"<circle cx='-38' cy='-2' r='12' fill='none' stroke='{dot}' stroke-width='3' opacity='0.55'/>" +
        $"<text x='14' y='9' font-family='{Font}' font-size='26' font-weight='900' text-anchor='middle' fill='{White}'>LIVE</text>";

    /// <summary>Two clockwise arrows around a circle.</summary>
    public static string Sync() => Arc(200, 320, 26, White) + Arc(20, 140, 26, Red);

    /// <summary>Timing tower: three rows with position bars (session info).</summary>
    public static string Tower() =>
        $"<rect x='-30' y='-28' width='12' height='12' rx='2' fill='{Red}'/><rect x='-12' y='-28' width='42' height='12' rx='2' fill='{White}'/>" +
        $"<rect x='-30' y='-6' width='12' height='12' rx='2' fill='{White}'/><rect x='-12' y='-6' width='34' height='12' rx='2' fill='{White}'/>" +
        $"<rect x='-30' y='16' width='12' height='12' rx='2' fill='{White}'/><rect x='-12' y='16' width='26' height='12' rx='2' fill='{White}'/>";

    /// <summary>Power symbol (close F1).</summary>
    public static string Power() =>
        $"<path d='{ArcPath(-60, 240, 27)}' fill='none' stroke='{Red}' stroke-width='7' stroke-linecap='round'/>" +
        $"<path d='M0 -34 V-4' stroke='{White}' stroke-width='7' stroke-linecap='round'/>";

    public static string Play() => $"<path d='M-16 -26 L26 0 L-16 26 Z' fill='{White}'/>";
    public static string Pause() =>
        $"<rect x='-22' y='-26' width='15' height='52' rx='4' fill='{White}'/><rect x='7' y='-26' width='15' height='52' rx='4' fill='{White}'/>";

    /// <summary>Four corner arrows pointing out (go fullscreen).</summary>
    public static string Expand(string color = White) => Corners(color, outward: true);

    /// <summary>Four corner arrows pointing in (leave fullscreen).</summary>
    public static string Collapse(string color = White) => Corners(color, outward: false);

    private static string Corners(string color, bool outward)
    {
        // Each corner: an L-shaped bracket; outward brackets sit at the corners, inward ones near the centre.
        double o = outward ? 30 : 10, l = 16;
        string Br(int sx, int sy) =>
            outward
                ? $"M{N(sx * o)} {N(sy * (o - l))} V{N(sy * o)} H{N(sx * (o - l))}"
                : $"M{N(sx * o)} {N(sy * (o + l))} V{N(sy * o)} H{N(sx * (o + l))}";
        return $"<path d='{Br(-1, -1)} {Br(1, -1)} {Br(-1, 1)} {Br(1, 1)}' fill='none' stroke='{color}' stroke-width='7' stroke-linecap='round' stroke-linejoin='round'/>" +
               (outward ? $"<rect x='-12' y='-9' width='24' height='18' rx='2' fill='{Red}'/>" : "");
    }

    public static string Bulb(string color = White) =>
        $"<path d='M0 -34 A22 22 0 0 1 13 6 V14 H-13 V6 A22 22 0 0 1 0 -34 Z' fill='{color}'/>" +
        $"<rect x='-11' y='18' width='22' height='5' rx='2' fill='{color}'/><rect x='-8' y='26' width='16' height='5' rx='2' fill='{color}'/>";

    public static string Speaker(string color = White, bool muted = false) =>
        $"<path d='M-30 -10 H-16 L4 -28 V28 L-16 10 H-30 Z' fill='{color}'/>" +
        (muted
            ? $"<path d='M14 -12 L34 8 M34 -12 L14 8' stroke='{Red}' stroke-width='6' stroke-linecap='round'/>"
            : $"<path d='M14 -12 A16 16 0 0 1 14 12 M22 -22 A30 30 0 0 1 22 22' fill='none' stroke='{color}' stroke-width='5' stroke-linecap='round'/>");

    public static string Clock(string color = White) =>
        $"<circle cx='0' cy='0' r='28' fill='none' stroke='{color}' stroke-width='6'/>" +
        $"<path d='M0 0 V-16 M0 0 L12 8' stroke='{color}' stroke-width='6' stroke-linecap='round'/>";

    // An arc from a to b degrees (clockwise on screen) with an arrowhead at the end.
    private static string Arc(double a, double b, double r, string color)
    {
        double rad = b * Math.PI / 180, ex = r * Math.Cos(rad), ey = r * Math.Sin(rad);
        double tx = -Math.Sin(rad), ty = Math.Cos(rad);    // direction of travel at the end
        double nx = Math.Cos(rad), ny = Math.Sin(rad);     // outward normal
        double tipX = ex + tx * 11, tipY = ey + ty * 11;
        return $"<path d='{ArcPath(a, b, r)}' fill='none' stroke='{color}' stroke-width='7' stroke-linecap='round'/>" +
               $"<path d='M{N(ex + nx * 10)} {N(ey + ny * 10)} L{N(tipX)} {N(tipY)} L{N(ex - nx * 10)} {N(ey - ny * 10)} Z' fill='{color}'/>";
    }

    private static string ArcPath(double a, double b, double r)
    {
        double ra = a * Math.PI / 180, rb = b * Math.PI / 180;
        int large = b - a > 180 ? 1 : 0;
        return $"M{N(r * Math.Cos(ra))} {N(r * Math.Sin(ra))} A{N(r)} {N(r)} 0 {large} 1 {N(r * Math.Cos(rb))} {N(r * Math.Sin(rb))}";
    }

    // ------------------------------------------------------------ static files

    /// <summary>Static images written into the plugin folder at build time (name → svg).</summary>
    public static IEnumerable<(string Name, string Svg)> Assets()
    {
        yield return ("plugin", Key(F1Mark(), "F1"));
        yield return ("category", Icon(F1Mark(), 28));
        foreach (var (name, glyph) in new[]
        {
            ("mode", F1Mark()), ("session", Tower()), ("flags", Flag(White)), ("zone", Bulb()), ("volume", Speaker()),
            ("latency", Clock()), ("live", Live(White)), ("sync", Sync()), ("pause", Pause()),
            ("race", Chequered()), ("quali", Stopwatch(White)), ("fullscreen", Expand()),
        })
            yield return ("action-" + name, Icon(glyph, 40));
        yield return ("key-mode", Key(F1Mark(), "F1 Mode"));
        yield return ("key-session", InfoKey("Session", "--:--", "Remaining", White));
        yield return ("key-flags", Key(Flag(), "Flags", "ON"));
        yield return ("key-live", Key(Live(), "Go live"));
        yield return ("key-sync", Key(Sync(), "Sync"));
        yield return ("key-pause", Key(Pause(), "Pause"));
        yield return ("key-race", Key(Chequered(), "Race"));
        yield return ("key-quali", Key(Stopwatch(), "Quali"));
        yield return ("key-fullscreen", Key(Expand(), "Fullscreen"));
    }
}
