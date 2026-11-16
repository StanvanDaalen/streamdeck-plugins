using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MonitorPlugin;

public static class Art
{
    private const string Dim = "#777777";
    private static readonly Color TileBase = Color.FromArgb(24, 24, 34);

    private static Graphics Quality(Image b)
    {
        var g = Graphics.FromImage(b);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        return g;
    }

    public static Bitmap Glyph(int size, Color c)
    {
        var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Quality(b);
        g.ScaleTransform(size / 100f, size / 100f);
        using var pen = new Pen(c, 8) { LineJoin = LineJoin.Round };
        g.DrawRectangle(pen, 12, 18, 76, 50);
        using var br = new SolidBrush(c);
        g.FillRectangle(br, 44, 68, 12, 12);
        g.FillRectangle(br, 30, 78, 40, 7);
        return b;
    }

    public static Bitmap DeviceGlyph(string device, int size, Color c)
    {
        if (device is not ("pc" or "ps5")) return Glyph(size, c);
        var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Quality(b);
        g.ScaleTransform(size / 100f, size / 100f);
        using var pen = new Pen(c, device == "pc" ? 4.2f : 4f)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        if (device == "pc") DrawPc(g, pen); else DrawPs5(g, pen);
        return b;
    }

    private static void DrawPc(Graphics g, Pen pen)
    {
        // Wide dual-chamber case, glass window with three visible fans and a narrow cable chamber.
        using var body = RoundedRect(new RectangleF(7, 10, 86, 76), 5);
        g.DrawPath(pen, body);
        g.DrawLine(pen, 74, 11, 74, 85);
        g.DrawLine(pen, 82, 18, 85, 18);
        g.DrawLine(pen, 82, 25, 85, 25);
        foreach (float y in new[] { 27f, 48f, 69f })
        {
            g.DrawEllipse(pen, 18, y - 9, 18, 18);
            g.DrawEllipse(pen, 25.5f, y - 1.5f, 3, 3);
        }
        g.DrawLine(pen, 15, 87, 15, 92);
        g.DrawLine(pen, 85, 87, 85, 92);
        g.DrawLine(pen, 11, 92, 21, 92);
        g.DrawLine(pen, 79, 92, 89, 92);
    }

    private static void DrawPs5(Graphics g, Pen pen)
    {
        // PS5 seen at an angle (after the user's reference drawing, 1200x1200 grid): narrow back panel on the
        // left, the dark centre fin, and the large front panel with its gently curved outer side.
        var state = g.Save();
        g.ScaleTransform(1 / 12f, 1 / 12f);
        using var stroke = new Pen(pen.Color, 75) { LineJoin = LineJoin.Round, StartCap = LineCap.Flat, EndCap = LineCap.Flat };
        using var fill = new SolidBrush(pen.Color);

        // One continuous outline: back panel top -> back panel side -> base -> front panel side -> top -> front edge.
        using var outline = new GraphicsPath();
        outline.AddLine(430, 150, 272, 150);
        outline.AddBezier(272, 150, 272, 470, 302, 760, 305, 1086);
        outline.AddLine(305, 1086, 973, 1086);
        outline.AddBezier(973, 1086, 976, 760, 916, 520, 918, 112);
        outline.AddLine(918, 112, 542, 112);
        outline.AddLine(542, 112, 472, 1086);
        g.DrawPath(stroke, outline);

        // Slanted end of the back panel's top edge.
        g.FillPolygon(fill, new PointF[] { new(429, 112.5f), new(450, 187.5f), new(429, 187.5f) });

        // The dark centre fin between the panels (drawn solid, like the reference).
        using var fin = new GraphicsPath();
        fin.AddLine(400, 1086, 381, 380);
        fin.AddBezier(381, 380, 379, 270, 405, 225, 465, 225);
        fin.AddLine(465, 225, 512, 225);
        fin.AddLine(512, 225, 480, 1086);
        fin.CloseFigure();
        g.FillPath(fill, fin);
        g.Restore(state);
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static Color Mix(Color a, Color b, float amount, int alpha = 255) => Color.FromArgb(alpha,
        (int)MathF.Round(a.R + (b.R - a.R) * amount),
        (int)MathF.Round(a.G + (b.G - a.G) * amount),
        (int)MathF.Round(a.B + (b.B - a.B) * amount));

    private static Color Accent(string device) => device switch
    {
        "pc" => Color.FromArgb(8, 145, 178),
        "ps5" => Color.FromArgb(0, 112, 209),
        _ => Color.FromArgb(71, 85, 105),
    };

    private static Bitmap? LoadIcon(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            using var src = Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase)
                ? Svg.SvgDocument.Open(path).Draw(288, 288) : new Bitmap(path);
            return TrimAndFit(src, 144);
        }
        catch (Exception e) { Log.Write("icon: " + e.Message); return null; }
    }

    private static Bitmap TrimAndFit(Bitmap src, int size)
    {
        int minX = src.Width, minY = src.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < src.Height; y++)
        for (int x = 0; x < src.Width; x++)
            if (src.GetPixel(x, y).A > 16)
            {
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
        var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        if (maxX < 0) return dst;
        int w = maxX - minX + 1, h = maxY - minY + 1;
        float scale = Math.Min((float)size / w, (float)size / h);
        float dw = w * scale, dh = h * scale;
        using var g = Quality(dst);
        g.DrawImage(src, new RectangleF((size - dw) / 2, (size - dh) / 2, dw, dh), new RectangleF(minX, minY, w, h), GraphicsUnit.Pixel);
        return dst;
    }

    public static string DataUrl(Bitmap b)
    {
        using var ms = new MemoryStream();
        b.Save(ms, ImageFormat.Png);
        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    }

    private static void DrawIcon(Graphics g, Bitmap icon, RectangleF target, bool active, bool custom)
    {
        if (active || !custom) { g.DrawImage(icon, target); return; }
        using var attributes = new ImageAttributes();
        float d = 0x77 / 255f;
        attributes.SetColorMatrix(new ColorMatrix(new[]
        {
            new[] { d, 0f, 0f, 0f, 0f }, new[] { 0f, d, 0f, 0f, 0f },
            new[] { 0f, 0f, d, 0f, 0f }, new[] { 0f, 0f, 0f, .72f, 0f },
            new[] { 0f, 0f, 0f, 0f, 1f },
        }));
        g.DrawImage(icon, Rectangle.Round(target), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, attributes);
    }

    private static void DrawFittedText(Graphics g, string text, RectangleF area, Color color, float startSize)
    {
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        float size = startSize;
        Font? font = null;
        while (size >= 12)
        {
            font?.Dispose();
            font = new Font("Segoe UI Semibold", size, FontStyle.Bold, GraphicsUnit.Pixel);
            if (g.MeasureString(text, font, int.MaxValue, fmt).Width <= area.Width) break;
            size -= 1;
        }
        using (font)
        using (var brush = new SolidBrush(color)) g.DrawString(text, font!, brush, area, fmt);
    }

    /// <summary>Key image in the selected button style. Custom art overrides the built-in device glyph.</summary>
    public static string Key(string? iconPath, string device, string style, string line1, string line2, string inputName, bool active)
    {
        using var custom = LoadIcon(iconPath);
        Color foreground = active ? Color.White : ColorTranslator.FromHtml(Dim);
        using var fallback = custom == null ? DeviceGlyph(device, 144, foreground) : null;
        var icon = custom ?? fallback!;

        using var bmp = new Bitmap(144, 144, PixelFormat.Format32bppArgb);
        using (var g = Quality(bmp))
        {
            g.Clear(Color.Black);
            if (style == "tile")
            {
                Color accent = active ? Accent(device) : Color.FromArgb(71, 85, 105);
                using var tile = RoundedRect(new RectangleF(0, 0, 144, 144), 26);
                using var gradient = new LinearGradientBrush(new PointF(72, 0), new PointF(72, 144), Mix(TileBase, accent, .38f), Mix(TileBase, accent, .12f));
                g.FillPath(gradient, tile);
                using var borderPath = RoundedRect(new RectangleF(2.5f, 2.5f, 139, 139), 24);
                using var border = new Pen(Mix(accent, Color.White, .35f, 153), 3f);
                g.DrawPath(border, borderPath);
                DrawIcon(g, icon, new RectangleF(40, 13, 64, 64), active, custom != null);
                DrawFittedText(g, inputName, new RectangleF(6, 99, 132, 35), foreground, 21);
            }
            else if (style == "icon")
            {
                DrawIcon(g, icon, new RectangleF(16, 16, 112, 112), active, custom != null);
            }
            else
            {
                bool text = line1.Length + line2.Length > 0;
                int size = text ? 72 : 112;
                DrawIcon(g, icon, new RectangleF((144 - size) / 2, text ? 8 : 16, size, size), active, custom != null);
                if (text)
                {
                    using var fmt = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                    using var bold = new Font("Segoe UI Semibold", 17f, GraphicsUnit.Pixel);
                    using var regular = new Font("Segoe UI", 16f, GraphicsUnit.Pixel);
                    using var main = new SolidBrush(foreground);
                    using var grey = new SolidBrush(active ? Color.FromArgb(0xB0, 0xB0, 0xB0) : foreground);
                    g.DrawString(line1, bold, main, new RectangleF(5, 88, 134, 24), fmt);
                    g.DrawString(line2, regular, grey, new RectangleF(5, 114, 134, 24), fmt);
                }
            }
        }
        return DataUrl(bmp);
    }

    public static void WritePreviews(string dir)
    {
        Directory.CreateDirectory(dir);
        var files = new List<string>();
        foreach (string style in new[] { "basic", "tile", "icon" })
        foreach (string device in new[] { "pc", "ps5", "none" })
        foreach (bool online in new[] { true, false })
        {
            string name = device switch { "pc" => "PC", "ps5" => "PS5 Pro", _ => "HDMI 1" };
            string url = Key(null, device, style, "Monitor", online ? name : "Niet verbonden", name, online);
            byte[] png = Convert.FromBase64String(url[(url.IndexOf(',') + 1)..]);
            string file = Path.Combine(dir, $"{style}-{device}-{(online ? "online" : "offline")}.png");
            File.WriteAllBytes(file, png);
            files.Add(file);
        }

        const int labelHeight = 25, cellWidth = 168, cellHeight = 144 + labelHeight;
        using var sheet = new Bitmap(cellWidth * 6, cellHeight * 3, PixelFormat.Format32bppArgb);
        using (var g = Quality(sheet))
        {
            g.Clear(Color.FromArgb(38, 38, 42));
            using var font = new Font("Segoe UI", 13, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.White);
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            for (int i = 0; i < files.Count; i++)
            {
                int row = i / 6, col = i % 6;
                using var image = new Bitmap(files[i]);
                g.DrawImage(image, col * cellWidth + 12, row * cellHeight, 144, 144);
                g.DrawString(Path.GetFileNameWithoutExtension(files[i]), font, brush,
                    new RectangleF(col * cellWidth, row * cellHeight + 144, cellWidth, labelHeight), fmt);
            }
        }
        sheet.Save(Path.Combine(dir, "_sheet.png"), ImageFormat.Png);
    }

    public static void WriteAssets(string dir)
    {
        Directory.CreateDirectory(dir);
        void Save(string name, int s, bool black, Color c)
        {
            foreach (var (suffix, px) in new[] { ("", s), ("@2x", s * 2) })
            {
                using var glyph = Glyph(px, c);
                using var output = new Bitmap(px, px, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(output)) { if (black) g.Clear(Color.Black); g.DrawImage(glyph, 0, 0, px, px); }
                output.Save(Path.Combine(dir, name + suffix + ".png"), ImageFormat.Png);
            }
        }
        Save("plugin", 128, false, Color.White);
        Save("category", 28, false, Color.White);
        Save("action", 20, false, Color.White);
        Save("key", 72, true, Color.FromArgb(0x70, 0x70, 0x70));
    }

    // ---------- icon library: every pack Stream Deck knows about ----------

    private static readonly string[] ImageExtensions = { ".svg", ".png", ".jpg", ".jpeg" };

    public static JsonArray IconLibrary()
    {
        var result = new JsonArray();
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Elgato", "StreamDeck");
        var program = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Elgato", "StreamDeck");
        foreach (var root in new[] { Path.Combine(data, "IconPacks"), Path.Combine(data, "Plugins", "com.elgato.keycreator.sdPlugin", "static") })
            if (Directory.Exists(root))
                foreach (var dir in Directory.EnumerateDirectories(root, "*.sdIconPack")) AddPack(result, dir);
        var pages = Path.Combine(program, "PageIcons");
        if (Directory.Exists(pages))
            foreach (var dir in Directory.EnumerateDirectories(pages, "*.sdIcons")) AddPageIcons(result, dir);
        return result;
    }

    private static string? PackName(string file)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            return doc.RootElement.TryGetProperty("Name", out var n) ? n.GetString() : null;
        }
        catch { return null; }
    }

    private static JsonObject Icon(string name, string file, string tags = "") => new() { ["n"] = name, ["f"] = file, ["t"] = tags };

    private static IEnumerable<string> Images(string dir) =>
        Directory.EnumerateFiles(dir).Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

    private static void AddPack(JsonArray result, string dir)
    {
        try
        {
            var iconDir = Path.Combine(dir, "icons");
            var icons = new JsonArray();
            var index = Path.Combine(dir, "icons.json");
            if (File.Exists(index))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(index));
                foreach (var i in doc.RootElement.EnumerateArray())
                {
                    var file = i.TryGetProperty("path", out var p) ? p.GetString() : null;
                    if (string.IsNullOrEmpty(file) || !File.Exists(Path.Combine(iconDir, file))) continue;
                    var tags = i.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
                        ? string.Join(' ', t.EnumerateArray().Select(x => x.ToString())) : "";
                    icons.Add(Icon(i.TryGetProperty("name", out var n) ? n.GetString() ?? file : file, file, tags));
                }
            }
            else if (Directory.Exists(iconDir))
                foreach (var f in Images(iconDir)) icons.Add(Icon(Path.GetFileNameWithoutExtension(f), Path.GetFileName(f)));
            if (icons.Count > 0)
                result.Add(new JsonObject { ["name"] = PackName(Path.Combine(dir, "manifest.json")) ?? Path.GetFileName(dir), ["dir"] = iconDir, ["icons"] = icons });
        }
        catch (Exception e) { Log.Write("icon pack: " + e.Message); }
    }

    private static void AddPageIcons(JsonArray result, string dir)
    {
        try
        {
            var images = Path.Combine(dir, "Images");
            if (!Directory.Exists(images)) return;
            var icons = new JsonArray();
            foreach (var f in Images(images))
            {
                var name = Regex.Replace(Path.GetFileNameWithoutExtension(f), @"^\d*(Icon)?", "");
                icons.Add(Icon(Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " "), Path.GetFileName(f)));
            }
            if (icons.Count > 0)
                result.Add(new JsonObject { ["name"] = "Stream Deck: " + (PackName(Path.Combine(dir, "en.json")) ?? Path.GetFileName(dir)), ["dir"] = images, ["icons"] = icons });
        }
        catch (Exception e) { Log.Write("icon pack: " + e.Message); }
    }
}
