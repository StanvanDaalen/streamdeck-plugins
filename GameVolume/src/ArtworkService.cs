using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace GameVolume;

public sealed class Art
{
    public required string Id { get; init; }
    public required string IconUrl { get; init; }
    public required string BackgroundUrl { get; init; }
    public required string Accent { get; init; }
    public required Bitmap KeyIcon { get; init; }
}

public sealed class ArtworkService
{
    private readonly Dictionary<string, Art> _cache = new();
    private readonly GameLibrary _lib;
    public Art NoGame { get; }

    public ArtworkService(GameLibrary lib)
    {
        _lib = lib;
        using var glyph = Glyphs.Controller(96, Color.FromArgb(0x70, 0x70, 0x70));
        NoGame = new Art
        {
            Id = "none", IconUrl = DataUrl(glyph), BackgroundUrl = DataUrl(Solid(200, 100, Color.Black)),
            Accent = "#FFFFFF", KeyIcon = new Bitmap(glyph),
        };
    }

    public void Invalidate(string id) => _cache.Remove(id);

    public Art ForGame(RunningGame g)
    {
        if (_cache.TryGetValue(g.Entry.Id, out var a)) return a;
        Bitmap? custom = null;
        var img = SettingsStore.Current.Overrides.GetValueOrDefault(g.Entry.Id)?.Image;
        if (!string.IsNullOrEmpty(img) && File.Exists(img))
            try { custom = new Bitmap(img); } catch (Exception e) { SettingsStore.Log("custom image: " + e.Message); }

        using var icon = custom != null ? Cover(custom, 96, 96) : LoadIcon(g.Entry.IconExe ?? g.ExePath);
        Bitmap? wide = custom;
        if (wide == null && g.Entry.SteamAppId != null && _lib.SteamRoot != null)
        {
            var header = Path.Combine(_lib.SteamRoot, "appcache", "librarycache", g.Entry.SteamAppId, "header.jpg");
            var old = Path.Combine(_lib.SteamRoot, "appcache", "librarycache", g.Entry.SteamAppId + "_header.jpg");
            var file = File.Exists(header) ? header : File.Exists(old) ? old : null;
            if (file != null) try { wide = new Bitmap(file); } catch { }
        }

        a = Build(g.Entry.Id, icon, wide);
        wide?.Dispose();
        _cache[g.Entry.Id] = a;
        return a;
    }

    public Art? ForFile(string path)
    {
        try
        {
            var key = $"file:{path}:{File.GetLastWriteTimeUtc(path).Ticks}";
            if (_cache.TryGetValue(key, out var a)) return a;
            using var src = LoadImageFile(path);
            using var icon = TrimAndFit(src, 96);
            a = Build(key, icon, null);
            _cache[key] = a;
            return a;
        }
        catch (Exception e) { SettingsStore.Log("icon file: " + e.Message); return null; }
    }

    private static Bitmap LoadImageFile(string path) =>
        Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase)
            ? Svg.SvgDocument.Open(path).Draw(288, 288)
            : new Bitmap(path);

    /// <summary>Icon packs installed in Stream Deck, as shown in its own icon library.</summary>
    public static System.Text.Json.Nodes.JsonArray IconLibrary()
    {
        var result = new System.Text.Json.Nodes.JsonArray();
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Elgato", "StreamDeck");
        var program = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Elgato", "StreamDeck");

        // Installed packs first, then the ones Stream Deck ships with.
        foreach (var root in new[] { Path.Combine(data, "IconPacks"), Path.Combine(data, "Plugins", "com.elgato.keycreator.sdPlugin", "static") })
            if (Directory.Exists(root))
                foreach (var dir in Directory.EnumerateDirectories(root, "*.sdIconPack"))
                    AddIconPack(result, dir);

        var pageIcons = Path.Combine(program, "PageIcons");
        if (Directory.Exists(pageIcons))
            foreach (var dir in Directory.EnumerateDirectories(pageIcons, "*.sdIcons"))
                AddPageIcons(result, dir);
        return result;
    }

    private static readonly string[] ImageExtensions = { ".svg", ".png", ".jpg", ".jpeg" };

    private static string? ReadPackName(string dir, string file)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, file)));
            return doc.RootElement.TryGetProperty("Name", out var n) ? n.GetString() : null;
        }
        catch { return null; }
    }

    private static System.Text.Json.Nodes.JsonObject Pack(string name, string dir, System.Text.Json.Nodes.JsonArray icons) =>
        new() { ["name"] = name, ["dir"] = dir, ["icons"] = icons };

    private static System.Text.Json.Nodes.JsonObject Icon(string name, string file, string tags = "") =>
        new() { ["n"] = name, ["f"] = file, ["t"] = tags };

    private static void AddIconPack(System.Text.Json.Nodes.JsonArray result, string dir)
    {
        try
        {
            var iconDir = Path.Combine(dir, "icons");
            var icons = new System.Text.Json.Nodes.JsonArray();
            var index = Path.Combine(dir, "icons.json");
            if (File.Exists(index))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(index));
                foreach (var i in doc.RootElement.EnumerateArray())
                {
                    var file = i.TryGetProperty("path", out var p) ? p.GetString() : null;
                    if (string.IsNullOrEmpty(file) || !File.Exists(Path.Combine(iconDir, file))) continue;
                    var tags = i.TryGetProperty("tags", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.Array
                        ? string.Join(' ', t.EnumerateArray().Select(x => x.ToString())) : "";
                    icons.Add(Icon(i.TryGetProperty("name", out var n) ? n.GetString() ?? file : file, file, tags));
                }
            }
            else if (Directory.Exists(iconDir))
            {
                foreach (var f in Directory.EnumerateFiles(iconDir).Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())))
                    icons.Add(Icon(Path.GetFileNameWithoutExtension(f), Path.GetFileName(f)));
            }
            if (icons.Count > 0)
                result.Add(Pack(ReadPackName(dir, "manifest.json") ?? Path.GetFileNameWithoutExtension(dir), iconDir, icons));
        }
        catch (Exception e) { SettingsStore.Log("icon pack: " + e.Message); }
    }

    private static void AddPageIcons(System.Text.Json.Nodes.JsonArray result, string dir)
    {
        try
        {
            var images = Path.Combine(dir, "Images");
            if (!Directory.Exists(images)) return;
            var icons = new System.Text.Json.Nodes.JsonArray();
            foreach (var f in Directory.EnumerateFiles(images).Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())))
            {
                var name = System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(f), @"^\d*(Icon)?", "");
                name = System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");
                icons.Add(Icon(name, Path.GetFileName(f)));
            }
            if (icons.Count > 0) result.Add(Pack("Stream Deck: " + (ReadPackName(dir, "en.json") ?? Path.GetFileName(dir)), images, icons));
        }
        catch (Exception e) { SettingsStore.Log("icon pack: " + e.Message); }
    }

    public static string? FileImage(string path)
    {
        try
        {
            using var src = LoadImageFile(path);
            using var fit = TrimAndFit(src, 144);
            return DataUrl(fit);
        }
        catch (Exception e) { SettingsStore.Log("icon file: " + e.Message); return null; }
    }

    public Art ForGlyph(string key, Func<Bitmap> make)
    {
        if (_cache.TryGetValue(key, out var a)) return a;
        using var glyph = make();
        a = Build(key, glyph, null);
        _cache[key] = a;
        return a;
    }

    public Art? ForExe(string key, string? exe)
    {
        if (_cache.TryGetValue(key, out var a)) return a;
        if (exe == null) return null;
        using var icon = LoadIcon(exe);
        a = Build(key, icon, null);
        _cache[key] = a;
        return a;
    }

    private static Art Build(string id, Bitmap icon, Bitmap? wide)
    {
        using var bg = new Bitmap(200, 100, PixelFormat.Format32bppArgb);
        using (var g = HighQuality(bg))
        {
            g.Clear(Color.Black);
            if (wide != null)
            {
                using var cover = Cover(wide, 200, 100);
                g.DrawImage(cover, 0, 0, 200, 100);
            }
            else
            {
                // No wide art: a heavily enlarged icon reads as a soft colour wash.
                using var small = new Bitmap(10, 5, PixelFormat.Format32bppArgb);
                using (var s = HighQuality(small)) s.DrawImage(icon, 0, -2, 10, 10);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(small, 0, 0, 200, 100);
            }
            using var scrim = new LinearGradientBrush(new Rectangle(0, 0, 200, 100), Color.FromArgb(130, 0, 0, 0), Color.FromArgb(205, 0, 0, 0), 90f);
            g.FillRectangle(scrim, 0, 0, 200, 100);
        }
        return new Art
        {
            Id = id, IconUrl = DataUrl(icon), BackgroundUrl = DataUrl(bg), Accent = AccentOf(icon), KeyIcon = new Bitmap(icon),
        };
    }

    public static Bitmap Solid(int w, int h, Color c)
    {
        var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(b);
        g.Clear(c);
        return b;
    }

    public static Graphics HighQuality(Image b)
    {
        var g = Graphics.FromImage(b);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        return g;
    }

    public static Bitmap Cover(Bitmap src, int w, int h)
    {
        var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = HighQuality(dst);
        float scale = Math.Max((float)w / src.Width, (float)h / src.Height);
        float sw = w / scale, sh = h / scale;
        g.DrawImage(src, new RectangleF(0, 0, w, h), new RectangleF((src.Width - sw) / 2, (src.Height - sh) / 2, sw, sh), GraphicsUnit.Pixel);
        return dst;
    }

    public static string DataUrl(Bitmap b)
    {
        using var ms = new MemoryStream();
        b.Save(ms, ImageFormat.Png);
        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    }

    public static string AccentOf(Bitmap icon)
    {
        // Weigh hues by saturation so the logo colour wins over grey or dark pixels.
        var buckets = new double[36];
        var r = new double[36]; var gr = new double[36]; var bl = new double[36];
        for (int y = 0; y < icon.Height; y += 2)
        for (int x = 0; x < icon.Width; x += 2)
        {
            var p = icon.GetPixel(x, y);
            if (p.A < 128) continue;
            var s = p.GetSaturation();
            var br = p.GetBrightness();
            if (s < 0.3f || br < 0.2f || br > 0.9f) continue;
            int i = (int)(p.GetHue() / 10f) % 36;
            double w = s * s;
            buckets[i] += w; r[i] += p.R * w; gr[i] += p.G * w; bl[i] += p.B * w;
        }
        int top = Array.IndexOf(buckets, buckets.Max());
        if (buckets[top] < 20) return "#FFFFFF";
        double R = r[top] / buckets[top], G = gr[top] / buckets[top], B = bl[top] / buckets[top];
        // Keep the slider visible on black.
        double max = Math.Max(R, Math.Max(G, B));
        if (max < 220) { double f = 220 / Math.Max(max, 1); R *= f; G *= f; B *= f; }
        return $"#{(int)Math.Min(R, 255):X2}{(int)Math.Min(G, 255):X2}{(int)Math.Min(B, 255):X2}";
    }

    private static Bitmap LoadIcon(string? path)
    {
        try
        {
            if (path != null && File.Exists(path))
            {
                using var raw = ShellImage(path, 256);
                if (raw != null) return TrimAndFit(raw, 96);
            }
        }
        catch (Exception e) { SettingsStore.Log("icon: " + e.Message); }
        return Glyphs.Controller(96, Color.White);
    }

    /// <summary>Crops the transparent padding Windows adds around small icons in a jumbo slot.</summary>
    private static Bitmap TrimAndFit(Bitmap src, int size)
    {
        int minX = src.Width, minY = src.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < src.Height; y++)
        for (int x = 0; x < src.Width; x++)
            if (src.GetPixel(x, y).A > 16)
            {
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
            }
        var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        if (maxX < 0) return dst;
        int w = maxX - minX + 1, h = maxY - minY + 1;
        float scale = Math.Min((float)size / w, (float)size / h);
        float dw = w * scale, dh = h * scale;
        using var g = HighQuality(dst);
        g.DrawImage(src, new RectangleF((size - dw) / 2, (size - dh) / 2, dw, dh), new RectangleF(minX, minY, w, h), GraphicsUnit.Pixel);
        return dst;
    }

    private static Bitmap? ShellImage(string path, int size)
    {
        var iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
        SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out IShellItemImageFactory factory);
        try
        {
            factory.GetImage(new SIZE { cx = size, cy = size }, 0x1 | 0x4, out IntPtr hbmp); // BIGGERSIZEOK | ICONONLY
            try
            {
                GetObject(hbmp, Marshal.SizeOf<BITMAP>(), out BITMAP info);
                int w = info.bmWidth, h = Math.Abs(info.bmHeight);
                var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                var hdr = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                var dc = GetDC(IntPtr.Zero);
                GetDIBits(dc, hbmp, 0, (uint)h, data.Scan0, ref hdr, 0);
                ReleaseDC(IntPtr.Zero, dc);
                bmp.UnlockBits(data);
                return bmp;
            }
            finally { DeleteObject(hbmp); }
        }
        finally { Marshal.ReleaseComObject(factory); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        public int pad0, pad1, pad2;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { void GetImage(SIZE size, int flags, out IntPtr phbm); }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr ctx, ref Guid riid, out IShellItemImageFactory item);
    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int size, out BITMAP b);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, IntPtr bits, ref BITMAPINFOHEADER info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
}

public static class Glyphs
{
    private static GraphicsPath Round(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath();
        p.AddArc(x, y, r * 2, r * 2, 180, 90);
        p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static Bitmap Controller(int size, Color c)
    {
        var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = ArtworkService.HighQuality(b);
        g.ScaleTransform(size / 100f, size / 100f);
        using var br = new SolidBrush(c);
        using var body = Round(8, 28, 84, 44, 21);
        g.FillPath(br, body);
        g.CompositingMode = CompositingMode.SourceCopy;
        using var hole = new SolidBrush(Color.Transparent);
        g.FillRectangle(hole, 25, 47, 18, 6);
        g.FillRectangle(hole, 31, 41, 6, 18);
        g.FillEllipse(hole, 62, 40, 9, 9);
        g.FillEllipse(hole, 70, 51, 9, 9);
        return b;
    }

    public static Bitmap Speaker(int size, Color c)
    {
        var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = ArtworkService.HighQuality(b);
        g.ScaleTransform(size / 100f, size / 100f);
        using var br = new SolidBrush(c);
        g.FillPolygon(br, new[] { new PointF(14, 39), new PointF(32, 39), new PointF(54, 20), new PointF(54, 80), new PointF(32, 61), new PointF(14, 61) });
        using var pen = new Pen(c, 8) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, 48, 34, 26, 32, -55, 110);
        g.DrawArc(pen, 48, 18, 44, 64, -50, 100);
        return b;
    }

    public static Bitmap Headphones(int size, Color c)
    {
        var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = ArtworkService.HighQuality(b);
        g.ScaleTransform(size / 100f, size / 100f);
        using var pen = new Pen(c, 8) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, 16, 16, 68, 68, 180, 180);
        g.DrawLine(pen, 16, 50, 16, 62);
        g.DrawLine(pen, 84, 50, 84, 62);
        using var br = new SolidBrush(c);
        using var left = Round(10, 56, 20, 30, 7);
        using var right = Round(70, 56, 20, 30, 7);
        g.FillPath(br, left);
        g.FillPath(br, right);
        return b;
    }

    public static Bitmap Speakers(int size, Color c)
    {
        var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = ArtworkService.HighQuality(b);
        g.ScaleTransform(size / 100f, size / 100f);
        using var br = new SolidBrush(c);
        using var cabinet = Round(24, 10, 52, 80, 8);
        g.FillPath(br, cabinet);
        g.CompositingMode = CompositingMode.SourceCopy;
        using var hole = new SolidBrush(Color.Transparent);
        g.FillEllipse(hole, 43, 18, 14, 14);
        g.FillEllipse(hole, 34, 42, 32, 32);
        g.CompositingMode = CompositingMode.SourceOver;
        g.FillEllipse(br, 44, 52, 12, 12);
        return b;
    }

    public static void WriteAssets(string dir)
    {
        Directory.CreateDirectory(dir);
        void Save(string name, Func<int, Bitmap> make, int s)
        {
            using var a = make(s); a.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
            using var b = make(s * 2); b.Save(Path.Combine(dir, name + "@2x.png"), ImageFormat.Png);
        }
        Func<int, Bitmap> game = s => Glyphs.Controller(s, Color.White);
        Func<int, Bitmap> app = s => Glyphs.Speaker(s, Color.White);
        Func<int, Bitmap> hp = s => Glyphs.Headphones(s, Color.White);
        Func<int, Bitmap> sp = s => Glyphs.Speakers(s, Color.White);
        Save("plugin", app, 128);
        Save("category", app, 28);
        Save("action-output", hp, 20);
        Save("action-speakers", sp, 20);
        Save("action-headphones", hp, 20);
        Func<int, Bitmap> OnBlack(Func<int, Color, Bitmap> glyph, Color c) => s =>
        {
            var k = ArtworkService.Solid(s, s, Color.Black);
            using var g = glyph(s, c);
            using var gr = ArtworkService.HighQuality(k);
            int m = s / 8;
            gr.DrawImage(g, m, m, s - m * 2, s - m * 2);
            return k;
        };
        var off = Color.FromArgb(0x70, 0x70, 0x70);
        Save("key-speakers-on", OnBlack(Glyphs.Speakers, Color.White), 72);
        Save("key-speakers-off", OnBlack(Glyphs.Speakers, off), 72);
        Save("key-headphones-on", OnBlack(Glyphs.Headphones, Color.White), 72);
        Save("key-headphones-off", OnBlack(Glyphs.Headphones, off), 72);
        Save("action-game", game, 20);
        Save("action-key", game, 20);
        Save("action-app", app, 20);
        Func<int, Bitmap> keyGame = s =>
        {
            var k = ArtworkService.Solid(s, s, Color.Black);
            using var g = Glyphs.Controller(s, Color.FromArgb(0x70, 0x70, 0x70));
            using var gr = Graphics.FromImage(k); gr.DrawImage(g, 0, 0, s, s);
            return k;
        };
        Func<int, Bitmap> keyApp = s =>
        {
            var k = ArtworkService.Solid(s, s, Color.Black);
            using var g = Glyphs.Speaker(s, Color.FromArgb(0x70, 0x70, 0x70));
            using var gr = Graphics.FromImage(k); gr.DrawImage(g, 0, 0, s, s);
            return k;
        };
        Save("key-game", keyGame, 72);
        Save("key-app", keyApp, 72);
    }
}
