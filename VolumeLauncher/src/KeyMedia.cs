using System.Drawing;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace GameVolume;

/// <summary>
/// The App Key while its app plays something, built like the dial's Picture style: the picture across the key,
/// the title with the series or artist below it in grey, a timeline, and the small logo of the service.
/// Drawn as SVG, so the small logos (SVG files) and the text stay sharp.
/// </summary>
public static class KeyMedia
{
    private static readonly string Images = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "images", "media"));
    private static readonly Dictionary<string, string> Badges = new();

    /// <param name="picture">Key-size picture of what plays, or null.</param>
    /// <param name="icon">The app's own icon (data URL), used when there is no picture.</param>
    public static string Render(MediaInfo m, string? picture, string icon, string? color, string? badge, bool muted)
    {
        bool timeline = m.HasTimeline;
        int titleY = timeline ? 103 : 111, artistY = titleY + 19;
        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"144\" height=\"144\" viewBox=\"0 0 144 144\">");
        sb.Append("<rect width=\"144\" height=\"144\" fill=\"#000\"/>");
        if (picture != null)
        {
            sb.Append($"<image x=\"0\" y=\"0\" width=\"144\" height=\"144\" preserveAspectRatio=\"xMidYMid slice\" xlink:href=\"{picture}\"/>");
            // Dark toward the bottom, so the text stays readable over any picture.
            sb.Append("<defs><linearGradient id=\"shade\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\">")
              .Append("<stop offset=\"0.3\" stop-color=\"#000\" stop-opacity=\"0\"/><stop offset=\"0.62\" stop-color=\"#000\" stop-opacity=\"0.8\"/>")
              .Append("<stop offset=\"1\" stop-color=\"#000\" stop-opacity=\"0.95\"/></linearGradient></defs>")
              .Append("<rect width=\"144\" height=\"144\" fill=\"url(#shade)\"/>");
        }
        else sb.Append($"<image x=\"40\" y=\"14\" width=\"64\" height=\"64\" xlink:href=\"{icon}\"/>");

        if (badge != null && BadgeInner(badge) is { } inner)
            sb.Append($"<g transform=\"translate(7,7) scale(1.25)\">{inner}</g>");
        if (muted || !m.Playing)
        {
            // Top right: muted speaker, or the pause sign while paused.
            sb.Append("<circle cx=\"125\" cy=\"18\" r=\"12\" fill=\"#000\" fill-opacity=\"0.65\"/>");
            if (muted)
                sb.Append("<path d=\"M117 15h3.5l4.5-4v14l-4.5-4H117z\" fill=\"#fff\"/><path d=\"M128 15l5 6m0-6l-5 6\" stroke=\"#fff\" stroke-width=\"1.8\" stroke-linecap=\"round\"/>");
            else
                sb.Append("<rect x=\"120\" y=\"12\" width=\"3.6\" height=\"12\" rx=\"1\" fill=\"#fff\"/><rect x=\"126.4\" y=\"12\" width=\"3.6\" height=\"12\" rx=\"1\" fill=\"#fff\"/>");
        }

        sb.Append(Text(Fit(m.Title, 17, true), 8, titleY, 17, "#FFFFFF", 600));
        if (m.Artist.Length > 0) sb.Append(Text(Fit(m.Artist, 14, false), 8, artistY, 14, "#B3B3B3", 400));

        if (timeline)
        {
            double part = Math.Clamp(m.Position.TotalSeconds / m.Duration.TotalSeconds, 0, 1);
            sb.Append("<rect x=\"8\" y=\"131\" width=\"128\" height=\"5\" rx=\"2.5\" fill=\"#4D4D4D\"/>");
            // Invariant: a Dutch Windows would write "46,5", which SVG does not read.
            var width = Math.Max(5, 128 * part).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            if (part > 0) sb.Append($"<rect x=\"8\" y=\"131\" width=\"{width}\" height=\"5\" rx=\"2.5\" fill=\"{color ?? "#FFFFFF"}\"/>");
        }
        sb.Append("</svg>");
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static string Text(string value, int x, int y, int size, string fill, int weight) =>
        $"<text x=\"{x}\" y=\"{y}\" font-family=\"Segoe UI\" font-size=\"{size}\" font-weight=\"{weight}\" fill=\"{fill}\">{WebUtility.HtmlEncode(value)}</text>";

    /// <summary>Shortens text with "…" so it fits the key (128 px wide), measured with the same font.</summary>
    private static string Fit(string text, int size, bool bold)
    {
        text = text.Trim();
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        using var font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var fmt = StringFormat.GenericTypographic;
        bool Fits(string s) => g.MeasureString(s, font, int.MaxValue, fmt).Width <= 126;
        if (Fits(text)) return text;
        for (int n = text.Length - 1; n > 0; n--)
            if (Fits(text[..n].TrimEnd() + "…")) return text[..n].TrimEnd() + "…";
        return "…";
    }

    // The inside of a small logo's SVG file, to place in the key's SVG.
    private static string? BadgeInner(string badge)
    {
        lock (Badges)
        {
            if (Badges.TryGetValue(badge, out var cached)) return cached;
            string? inner = null;
            try
            {
                var file = Path.Combine(Images, Path.GetFileName(badge));
                var svg = File.ReadAllText(file);
                var match = Regex.Match(svg, @"<svg[^>]*>(?<inner>.*)</svg>", RegexOptions.Singleline);
                if (match.Success) inner = match.Groups["inner"].Value;
            }
            catch (Exception e) { SettingsStore.Log("badge: " + e.Message); }
            Badges[badge] = inner!;
            return inner;
        }
    }
}
