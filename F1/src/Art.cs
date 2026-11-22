using System.Globalization;
using System.Net;
using SkiaSharp;
using Svg.Skia;

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
    /// <param name="caption">Optional small line under the label (for example which race a key opens); without it the key is unchanged.</param>
    public static string Key(string glyph, string label, string? pill = null, string pillColor = Green, string labelColor = White, bool upper = true,
        string? caption = null, string captionColor = Grey) =>
        caption == null
            ? Frame(pill, pillColor) +
              $"<g transform='translate(72 62)'>{glyph}</g>" +
              $"<text x='72' y='124' font-family='{Font}' font-size='17' font-weight='900' letter-spacing='0.4' text-anchor='middle' fill='{labelColor}'>{E(upper ? label.ToUpperInvariant() : label)}</text>" +
              "</svg>"
            : Frame(pill, pillColor) +
              $"<g transform='translate(72 57) scale(0.92)'>{glyph}</g>" +
              $"<text x='72' y='114' font-family='{Font}' font-size='17' font-weight='900' letter-spacing='0.4' text-anchor='middle' fill='{labelColor}'>{E(upper ? label.ToUpperInvariant() : label)}</text>" +
              $"<text x='72' y='132' font-family='{Font}' font-size='{(caption.Length > 13 ? 10 : 12)}' font-weight='700' text-anchor='middle' fill='{captionColor}'>{E(caption)}</text>" +
              "</svg>";

    private static string Frame(string? pill, string pillColor)
    {
        // A number (how many watch the party) is a round badge in the corner, clear of the edges and the icon.
        bool count = pill is { Length: > 0 and <= 2 } && pill.All(char.IsDigit);
        var p = pill == null ? "" : count
            ? $"<circle cx='25' cy='25' r='13' fill='{pillColor}'/>" +
              $"<text x='25' y='30' font-family='{Font}' font-size='14' font-weight='900' text-anchor='middle' fill='{Tile}'>{E(pill)}</text>"
            : $"<rect x='8' y='10' width='32' height='18' rx='4' fill='{pillColor}'/>" +
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

    /// <summary>The next session: NEXT, where (the race weekend), what (FP1, Quali …) and when.</summary>
    /// <param name="countdown">Time left ("6d 21h", "3h 25m", "12m"); amber in the last hour.</param>
    public static string NextKey(string where, string what, string when, string countdown, bool soon) =>
        Frame(null, Green) +
        $"<text x='72' y='29' font-family='{Font}' font-size='12' font-weight='900' letter-spacing='0.6' text-anchor='middle' fill='{Red}'>NEXT</text>" +
        $"<text x='72' y='48' font-family='{Font}' font-size='{(where.Length > 10 ? 12 : 14)}' font-weight='900' letter-spacing='0.3' text-anchor='middle' fill='{White}'>{E(where.ToUpperInvariant())}</text>" +
        $"<text x='72' y='84' font-family='{Font}' font-size='{(what.Length > 6 ? 24 : 32)}' font-weight='900' text-anchor='middle' fill='{White}'>{E(what)}</text>" +
        $"<text x='72' y='106' font-family='{Font}' font-size='12' font-weight='700' text-anchor='middle' fill='{Grey}'>{E(when)}</text>" +
        $"<text x='72' y='127' font-family='{Font}' font-size='15' font-weight='900' text-anchor='middle' fill='{(soon ? Amber : White)}'>{E(countdown)}</text>" +
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
            var tile = Frame(pill, pillColor) +
                $"<text x='72' y='124' font-family='{Font}' font-size='17' font-weight='900' letter-spacing='0.4' text-anchor='middle' fill='{White}'>{E(label.ToUpperInvariant())}</text></svg>";
            using var bmp = new SKBitmap(144, 144);
            using (var canvas = new SKCanvas(bmp))
            {
                canvas.Clear(SKColors.Transparent);
                DrawSvg(canvas, tile, 144, 144);
                // Fit inside 104×56 above the label, centred; without a label the logo is larger and in the middle.
                if (label.Length > 0) DrawLogo(canvas, file, 72, 62, 104, 56);
                else DrawLogo(canvas, file, 72, 74, 112, 64);
            }
            var ms = EncodePng(bmp);
            return PngCache[key] = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        }
        catch (Exception e) { Store.Log("logo: " + e.Message); return null; }
    }

    /// <summary>Draws the logo file (SVG or bitmap) centred on (cx, cy), fitted inside maxW × maxH.</summary>
    private static void DrawLogo(SKCanvas canvas, string file, float cx, float cy, float maxW, float maxH)
    {
        if (file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            using var logo = new SKSvg();
            logo.Load(file);
            var r = logo.Picture?.CullRect ?? SKRect.Empty;
            if (r.Width <= 0 || r.Height <= 0) return;
            float s = Math.Min(maxW / r.Width, maxH / r.Height);
            canvas.Save();
            canvas.Translate(cx - r.Width * s / 2, cy - r.Height * s / 2);
            canvas.Scale(s);
            canvas.Translate(-r.Left, -r.Top);
            canvas.DrawPicture(logo.Picture);
            canvas.Restore();
        }
        else
        {
            using var logo = SKBitmap.Decode(file);
            if (logo == null) return;
            float s = Math.Min(maxW / logo.Width, maxH / logo.Height);
            float w = logo.Width * s, h = logo.Height * s;
            using var img = SKImage.FromBitmap(logo);
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawImage(img, SKRect.Create(cx - w / 2, cy - h / 2, w, h), new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        }
    }

    /// <summary>Only the logo on a transparent square of <paramref name="size"/> pixels, as PNG bytes, in plain white like the other plugins' icons in the action list.</summary>
    private static byte[] LogoIcon(string file, int size)
    {
        using var bmp = new SKBitmap(size, size);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            using var white = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(SKColors.White, SKBlendMode.SrcIn) };
            canvas.SaveLayer(white);
            DrawLogo(canvas, file, size / 2f, size / 2f, size * 0.94f, size * 0.94f);
            canvas.Restore();
        }
        return EncodePng(bmp).ToArray();
    }

    /// <summary>
    /// Local branding: when the user has their own logo in the settings folder, writes PNG versions of the
    /// plugin, category, F1 Mode action and F1 Mode key images into an installed images folder and removes the
    /// matching SVGs, so Stream Deck shows that logo. Only for the installed copy; the repo keeps its own icons.
    /// </summary>
    public static int WriteBrandAssets(string dir)
    {
        var file = LogoFile;
        if (file == null) return 0;
        void Png(string name, byte[] png) => File.WriteAllBytes(Path.Combine(dir, name), png);
        byte[] Url(string? dataUrl) => dataUrl == null ? throw new InvalidOperationException("logo could not be drawn") : Convert.FromBase64String(dataUrl[(dataUrl.IndexOf(',') + 1)..]);

        Png("category.png", LogoIcon(file, 28)); Png("category@2x.png", LogoIcon(file, 56));
        Png("action-mode.png", LogoIcon(file, 20)); Png("action-mode@2x.png", LogoIcon(file, 40));
        var plugin = Url(KeyWithLogo(""));   // the logo already says F1, so no label
        Png("plugin.png", plugin); Png("plugin@2x.png", plugin);
        var mode = Url(KeyWithLogo("F1 Mode"));
        Png("key-mode.png", mode); Png("key-mode@2x.png", mode);
        foreach (var n in new[] { "category", "action-mode", "plugin", "key-mode" })
            File.Delete(Path.Combine(dir, n + ".svg"));
        return 4;
    }

    /// <summary>"F1" in heavy italic with speed lines, in the spirit of F1 graphics (not the trademarked logo).</summary>
    public static string F1Mark(string color = Red) =>
        $"<path d='M-46 -10 H-22 M-50 0 H-26 M-46 10 H-30' stroke='{White}' stroke-width='4' stroke-linecap='round'/>" +
        $"<text x='12' y='17' font-family='{Font}' font-size='50' font-weight='900' font-style='italic' text-anchor='middle' fill='{color}' letter-spacing='-2'>F1</text>";

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
        using var bmp = new SKBitmap(size, size);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            DrawSvg(canvas, svg, size, size);
        }
        var ms = EncodePng(bmp);
        var url = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        if (PngCache.Count > 64) PngCache.Clear();
        return PngCache[key] = url;
    }

    /// <summary>Draws SVG markup scaled to fill <paramref name="width"/>×<paramref name="height"/>.</summary>
    private static void DrawSvg(SKCanvas canvas, string svg, int width, int height)
    {
        using var doc = new SKSvg();
        doc.FromSvg(svg);
        if (doc.Picture is not { } pic) return;
        var r = pic.CullRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        canvas.Save();
        canvas.Scale(width / r.Width, height / r.Height);
        canvas.Translate(-r.Left, -r.Top);
        canvas.DrawPicture(pic);
        canvas.Restore();
    }

    private static MemoryStream EncodePng(SKBitmap bmp)
    {
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new MemoryStream(data.ToArray());
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

    /// <summary>Power symbol (End session): a short stem in the ring's opening.</summary>
    public static string Power() =>
        $"<path d='{ArcPath(-50, 230, 27)}' fill='none' stroke='{Red}' stroke-width='7' stroke-linecap='round'/>" +
        $"<path d='M0 -32 V-12' stroke='{White}' stroke-width='7' stroke-linecap='round'/>";

    /// <summary>
    /// Four people in front of a screen (F1 Party). Off: grey with a stroke through it, like the other on/off
    /// keys.
    /// </summary>
    public static string Party(bool on = true)
    {
        string c = on ? White : Grey, play = on ? Red : Grey, people = "";
        foreach (var x in new[] { -27, -9, 9, 27 })
            people += $"<circle cx='{x}' cy='11' r='5' fill='{c}'/><path d='M{x - 7} 31 Q{x - 7} 19 {x} 19 Q{x + 7} 19 {x + 7} 31 Z' fill='{c}'/>";
        return $"<rect x='-34' y='-34' width='68' height='34' rx='5' fill='none' stroke='{c}' stroke-width='5'/>" +
               $"<path d='M-6 -24 L8 -17 L-6 -10 Z' fill='{play}'/>" + people +
               (on ? "" : $"<path d='M-38 34 L38 -38' stroke='{Tile}' stroke-width='12' stroke-linecap='round'/><path d='M-38 34 L38 -38' stroke='{c}' stroke-width='6' stroke-linecap='round'/>");
    }

    /// <summary>Arrow back to the left (Stream Deck's Go to Page, back to page 1).</summary>
    public static string Back() =>
        $"<path d='M-8 -24 L-30 0 L-8 24' fill='none' stroke='{White}' stroke-width='7' stroke-linecap='round' stroke-linejoin='round'/>" +
        $"<path d='M-28 0 H30' stroke='{Red}' stroke-width='7' stroke-linecap='round'/>";

    /// <summary>A circular arrow going back with the seconds inside (Replay).</summary>
    public static string Rewind(int seconds) =>
        $"<path d='{ArcPath(-150, 150, 28)}' fill='none' stroke='{White}' stroke-width='7' stroke-linecap='round'/>" +
        $"<path d='M-36 -28 L-23 -13 L-38 -6' fill='none' stroke='{White}' stroke-width='6' stroke-linecap='round' stroke-linejoin='round'/>" +
        $"<text x='3' y='9' font-family='{Font}' font-size='24' font-weight='900' text-anchor='middle' fill='{Red}'>{seconds}</text>";

    /// <summary>An eye: open (shown) or shut with a stroke (hidden).</summary>
    public static string Eye(bool open = true)
    {
        string c = open ? White : Grey;
        var s = $"<path d='M-36 0 Q0 -30 36 0 Q0 30 -36 0 Z' fill='none' stroke='{c}' stroke-width='6' stroke-linejoin='round'/>" +
                $"<circle cx='0' cy='0' r='10' fill='{(open ? Red : Grey)}'/>";
        return open ? s : s + $"<path d='M-34 30 L34 -30' stroke='{Tile}' stroke-width='12' stroke-linecap='round'/><path d='M-34 30 L34 -30' stroke='{c}' stroke-width='6' stroke-linecap='round'/>";
    }

    /// <summary>A paper plane (send the invite).</summary>
    public static string Plane() =>
        $"<path d='M-34 -2 L32 -28 L18 30 L2 10 Z' fill='none' stroke='{White}' stroke-width='6' stroke-linejoin='round'/>" +
        $"<path d='M2 10 L32 -28 M2 10 V28 L12 18' fill='none' stroke='{Red}' stroke-width='6' stroke-linecap='round' stroke-linejoin='round'/>";

    /// <summary>The five start lights in their bar; lit = how many are red (Start together's countdown).</summary>
    public static string Lights(int lit)
    {
        var s = $"<rect x='-52' y='-14' width='104' height='28' rx='14' fill='none' stroke='{White}' stroke-width='3'/>";
        for (int i = 0; i < 5; i++)
            s += $"<circle cx='{-40 + i * 20}' cy='0' r='8' fill='{(i < lit ? Red : "#3A3A44")}'/>";
        return s;
    }

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
        yield return ("category", Icon(F1Mark(White), 28));   // white, like the other plugins in the action list
        foreach (var (name, glyph) in new[]
        {
            ("mode", F1Mark(White)), ("session", Tower()), ("flags", Flag(White)), ("zone", Bulb()), ("volume", Speaker()),
            ("latency", Clock()), ("live", Live(White)), ("sync", Party()), ("pause", Pause()),
            ("race", Chequered()), ("quali", Stopwatch(White)), ("fullscreen", Expand()), ("close", Power()), ("start", Lights(5)),
            ("replay", Rewind(10)), ("hide", Eye()), ("invite", Plane()),
        })
            yield return ("action-" + name, Icon(glyph, 40));
        yield return ("key-mode", Key(F1Mark(), "F1 Mode"));
        yield return ("key-session", InfoKey("Session", "--:--", "Remaining", White));
        yield return ("key-flags", Key(Flag(), "Flags", "ON"));
        yield return ("key-live", Key(Live(), "Go live"));
        yield return ("key-sync", Key(Party(), "Party"));
        yield return ("key-close", Key(Power(), "End session"));
        yield return ("key-start", Key(Lights(0), "Start", caption: "together"));
        yield return ("key-replay", Key(Rewind(10), "Replay"));
        yield return ("key-hide", Key(Eye(), "Hide"));
        yield return ("key-invite", Key(Plane(), "Invite"));
        yield return ("key-page1", Key(Back(), "Page 1"));
        yield return ("key-pause", Key(Pause(), "Pause"));
        yield return ("key-race", Key(Chequered(), "Race"));
        yield return ("key-quali", Key(Stopwatch(), "Quali"));
        yield return ("key-fullscreen", Key(Expand(), "Fullscreen"));
    }
}
