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

    /// <summary>Key image: icon with up to two text lines, or the icon alone when there is no text.</summary>
    public static string Key(string? iconPath, string line1, string line2, bool active)
    {
        using var custom = LoadIcon(iconPath);
        using var fallback = custom == null ? Glyph(144, active ? Color.White : ColorTranslator.FromHtml(Dim)) : null;
        var icon = custom ?? fallback!;
        bool text = line1.Length + line2.Length > 0;

        using var bmp = new Bitmap(144, 144, PixelFormat.Format32bppArgb);
        using (var g = Quality(bmp))
        {
            g.Clear(Color.Black);
            int size = text ? 72 : 112;
            g.DrawImage(icon, (144 - size) / 2, text ? 8 : 16, size, size);
            if (text)
            {
                using var fmt = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                using var bold = new Font("Segoe UI Semibold", 17f, GraphicsUnit.Pixel);
                using var regular = new Font("Segoe UI", 16f, GraphicsUnit.Pixel);
                using var main = new SolidBrush(active ? Color.White : ColorTranslator.FromHtml(Dim));
                using var grey = new SolidBrush(Color.FromArgb(0xB0, 0xB0, 0xB0));
                g.DrawString(line1, bold, main, new RectangleF(5, 88, 134, 24), fmt);
                g.DrawString(line2, regular, grey, new RectangleF(5, 114, 134, 24), fmt);
            }
        }
        return DataUrl(bmp);
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
