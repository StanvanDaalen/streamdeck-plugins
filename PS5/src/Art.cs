using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace PS5Plugin;


public static class Art
{
    private static readonly Color Green = ColorTranslator.FromHtml("#1ED760");

    private static Graphics Quality(Image b)
    {
        var g = Graphics.FromImage(b);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        return g;
    }

    private static readonly Dictionary<string, (DateTime, Bitmap?)> IconCache = new();

    private static Bitmap? LoadIcon(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        var stamp = File.GetLastWriteTimeUtc(path);
        if (IconCache.TryGetValue(path, out var hit) && hit.Item1 == stamp) return hit.Item2;
        Bitmap? fitted = null;
        try { using var src = new Bitmap(path); fitted = TrimAndFit(src, 128); }
        catch (Exception e) { Log.Write("icon: " + e.Message); }
        IconCache[path] = (stamp, fitted);
        return fitted;
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

    /// <summary>A simple game controller, used when no own icon is set and for the action icons.</summary>
    public static void Controller(Graphics g, RectangleF r, Color c)
    {
        var s = Math.Min(r.Width, r.Height) / 100f;
        var st = g.Save();
        g.TranslateTransform(r.X + (r.Width - 100 * s) / 2, r.Y + (r.Height - 100 * s) / 2);
        g.ScaleTransform(s, s);
        using var body = new GraphicsPath();
        body.AddArc(4, 30, 30, 30, 180, 90);
        body.AddLine(19, 30, 81, 30);
        body.AddArc(66, 30, 30, 30, 270, 90);
        body.AddBezier(96, 45, 100, 70, 98, 84, 86, 84);
        body.AddBezier(86, 84, 76, 84, 72, 70, 64, 66);
        body.AddLine(64, 66, 36, 66);
        body.AddBezier(36, 66, 28, 70, 24, 84, 14, 84);
        body.AddBezier(14, 84, 2, 84, 0, 70, 4, 45);
        body.CloseFigure();
        using var br = new SolidBrush(c);
        g.FillPath(br, body);
        using var cut = new SolidBrush(Color.FromArgb(255, 0, 0, 0));
        g.CompositingMode = CompositingMode.SourceCopy;
        using var clear = new SolidBrush(Color.Transparent);
        g.FillRectangle(clear, 20, 42, 14, 4); g.FillRectangle(clear, 25, 37, 4, 14);
        foreach (var (x, y) in new[] { (72f, 36f), (80f, 43f), (64f, 43f), (72f, 50f) }) g.FillEllipse(clear, x - 3.5f, y - 3.5f, 7, 7);
        g.CompositingMode = CompositingMode.SourceOver;
        g.Restore(st);
    }

    private static void DrawIcon(Graphics g, string? path, RectangleF r, bool active)
    {
        var icon = LoadIcon(path);
        float alpha = active ? 1f : 0.32f;
        if (icon == null)
        {
            using var tmp = new Bitmap(128, 128, PixelFormat.Format32bppArgb);
            using (var tg = Quality(tmp)) Controller(tg, new RectangleF(0, 0, 128, 128), Color.White);
            DrawFaded(g, tmp, r, alpha, active);
            return;
        }
        DrawFaded(g, icon, r, alpha, active);
    }

    private static void DrawFaded(Graphics g, Bitmap b, RectangleF r, float alpha, bool colour)
    {
        // Inactive: grey and faded, so the state reads from across the desk.
        float lr = 0.299f, lg = 0.587f, lb = 0.114f;
        var m = colour
            ? new ColorMatrix { Matrix33 = alpha }
            : new ColorMatrix(new[]
            {
                new[] { lr, lr, lr, 0, 0 }, new[] { lg, lg, lg, 0, 0 }, new[] { lb, lb, lb, 0, 0 },
                new[] { 0f, 0, 0, alpha, 0 }, new[] { 0f, 0, 0, 0, 1 },
            });
        using var ia = new ImageAttributes();
        ia.SetColorMatrix(m);
        g.DrawImage(b, Rectangle.Round(r), 0, 0, b.Width, b.Height, GraphicsUnit.Pixel, ia);
    }

    private static void Text(Graphics g, string s, float size, FontStyle style, Color c, RectangleF r, StringAlignment align = StringAlignment.Near)
    {
        using var f = new Font("Segoe UI", size, style, GraphicsUnit.Pixel);
        using var br = new SolidBrush(c);
        using var fmt = new StringFormat { Alignment = align, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(s, f, br, r, fmt);
    }

    private static void Pill(Graphics g, RectangleF r, Color fill)
    {
        using var p = new GraphicsPath();
        float d = r.Height;
        p.AddArc(r.X, r.Y, d, d, 90, 180);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        p.CloseFigure();
        using var br = new SolidBrush(fill);
        g.FillPath(br, p);
    }

    public static string DataUrl(Bitmap b)
    {
        using var ms = new MemoryStream();
        b.Save(ms, ImageFormat.Png);
        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>Writes the static PNGs the manifest refers to.</summary>
    public static void WriteAssets(string dir)
    {
        Directory.CreateDirectory(dir);
        void Save(string name, int size, Color c, bool padded)
        {
            using var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Quality(b))
            {
                float pad = padded ? size * 0.12f : 0;
                Controller(g, new RectangleF(pad, pad, size - 2 * pad, size - 2 * pad), c);
            }
            b.Save(Path.Combine(dir, name), ImageFormat.Png);
        }
        foreach (var (name, size) in new[] { ("plugin", 256), ("category", 28), ("action", 20) })
        {
            Save($"{name}.png", size, Color.White, name == "plugin");
            Save($"{name}@2x.png", size * 2, Color.White, name == "plugin");
        }
        Save("key.png", 72, Color.White, true);
        Save("key@2x.png", 144, Color.White, true);
    }

    private static readonly Dictionary<string, string> Rendered = new();

    private static Bitmap Canvas(int w, int h, Action<Graphics> draw)
    {
        var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Quality(b);
        draw(g);
        return b;
    }

    private static string Cached(string key, Func<Bitmap> make)
    {
        if (Rendered.TryGetValue(key, out var hit)) return hit;
        using var b = make();
        return Rendered[key] = DataUrl(b);
    }

    /// <summary>
    /// The "artwork" square, like an album cover on the Spotify dial: PlayStation blue with a white icon
    /// when on; the empty Spotify cover (grey with a thin white edge) and a faded icon when off.
    /// </summary>
    public static string Tile(string? path, bool on)
    {
        var stamp = path != null && File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0;
        return Cached($"tile|{path}|{stamp}|{on}", () => Canvas(144, 144, g =>
        {
            if (on)
            {
                using var bg = new LinearGradientBrush(new Rectangle(0, 0, 144, 144), ColorTranslator.FromHtml("#0A6CD6"), ColorTranslator.FromHtml("#00439C"), LinearGradientMode.ForwardDiagonal);
                g.FillRectangle(bg, 0, 0, 144, 144);
            }
            else
            {
                using var bg = new SolidBrush(Color.FromArgb(38, 128, 128, 128));
                g.FillRectangle(bg, 0, 0, 144, 144);
                using var pen = new Pen(Color.FromArgb(204, 255, 255, 255), 1.5f);
                g.DrawRectangle(pen, 0.75f, 0.75f, 142.5f, 142.5f);
            }
            DrawIcon(g, path, new RectangleF(30, 30, 84, 84), on);
        }));
    }

    private static readonly string ElgatoIcons = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Elgato", "StreamDeck", "Plugins", "com.elgato.keycreator.sdPlugin", "static", "com.elgato.defaulticonswhite.sdIconPack", "icons");

    /// <summary>
    /// Elgato's own loudspeaker icon (with a cross when muted), cropped to its content so it fills the
    /// 20 px spot. Without Elgato's icon pack a drawn loudspeaker takes its place.
    /// </summary>
    public static string Loudspeaker(bool muted)
    {
        var key = "loud|" + muted;
        if (Rendered.TryGetValue(key, out var hit)) return hit;
        try
        {
            var file = Path.Combine(ElgatoIcons, muted ? "IconVolumeMuted-Filled-White.svg" : "IconVolume-Filled-White.svg");
            if (File.Exists(file))
            {
                var svg = File.ReadAllText(file).Replace("viewBox=\"0 0 144 144\"", "viewBox=\"24 24 96 96\"");
                return Rendered[key] = "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));
            }
        }
        catch (Exception e) { Log.Write("speaker icon: " + e.Message); }
        return Cached(key + "|own", () => Canvas(40, 40, g => DrawLoudspeaker(g, muted, Color.White)));
    }

    private static void DrawLoudspeaker(Graphics g, bool muted, Color c)
    {
        using var br = new SolidBrush(c);
        g.FillPolygon(br, new PointF[] { new(3, 14), new(11, 14), new(21, 5), new(21, 35), new(11, 26), new(3, 26) });
        using var pen = new Pen(c, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (muted) { g.DrawLine(pen, 26, 14, 36, 26); g.DrawLine(pen, 36, 14, 26, 26); }
        else { g.DrawArc(pen, 18, 12, 10, 16, -60, 120); g.DrawArc(pen, 18, 6, 18, 28, -60, 120); }
    }

    /// <summary>
    /// The small round logo on the cover corner, built like the YouTube badge of the media dials (18 px,
    /// black circle, white mark): here the PlayStation console itself.
    /// </summary>
    public static string Badge(string? path)
    {
        var stamp = path != null && File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0;
        return Cached($"badge|{path}|{stamp}", () => Canvas(36, 36, g =>
        {
            using var ring = new SolidBrush(Color.Black);
            g.FillEllipse(ring, 0, 0, 36, 36);
            var icon = LoadIcon(path);
            var r = new RectangleF(8, 8, 20, 20);
            if (icon != null) g.DrawImage(icon, r);
            else Controller(g, r, Color.White);
        }));
    }

    private static void FillRound(Graphics g, Brush br, RectangleF r, float rad)
    {
        using var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        g.FillPath(br, p);
    }

    /// <summary>Full-dial cover while OBS is closed, built like Spotify's "Open Spotify / Not running" cover.</summary>
    public static string ObsClosed(string? path) => Cached($"closed|{path}", () => Canvas(400, 200, g =>
    {
        g.Clear(Color.Black);
        DrawIcon(g, path, new RectangleF(168, 22, 64, 64), true);
        Text(g, "Open OBS", 32, FontStyle.Bold, Color.White, new RectangleF(0, 104, 400, 40), StringAlignment.Center);
        Text(g, "OBS not running", 32, FontStyle.Regular, Color.FromArgb(178, 255, 255, 255), new RectangleF(0, 144, 400, 40), StringAlignment.Center);
    }));

    private static Bitmap FromUrl(string url)
    {
        using var ms = new MemoryStream(Convert.FromBase64String(url[(url.IndexOf(',') + 1)..]));
        return new Bitmap(ms);
    }

    /// <summary>Approximation of the dial in every state, for checking the look without a Stream Deck.</summary>
    public static void Preview(string file, string? icon)
    {
        var rows = new (bool on, bool muted, string label, bool closed)[]
        {
            (true, false, "Volume", false), (true, true, "Muted", false), (false, false, "Volume", false), (false, false, "", true),
        };
        var grey = ColorTranslator.FromHtml("#8C8C8C");
        using var sheet = new Bitmap(420, 220);
        using var g = Quality(sheet);
        g.Clear(Color.FromArgb(60, 60, 60));
        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            var st = g.Save();
            g.TranslateTransform(5 + (i % 2) * 210, 5 + (i / 2) * 110);
            g.SetClip(new Rectangle(0, 0, 200, 100));
            g.Clear(Color.Black);
            void Img(string url, float x, float y, float w, float h) { using var b = FromUrl(url); g.DrawImage(b, x, y, w, h); }
            if (r.closed) { Img(ObsClosed(icon), 0, 0, 200, 100); g.Restore(st); continue; }
            Img(Tile(icon, r.on), 12, 12, 40, 40);
            Img(Badge(icon), 38, 38, 18, 18);
            Text(g, "PlayStation 5", 16, FontStyle.Bold, r.on ? Color.White : grey, new RectangleF(60, 12, 128, 20));
            Text(g, r.on ? "On" : "Off", 16, FontStyle.Regular, Color.FromArgb(178, 255, 255, 255), new RectangleF(60, 32, 128, 20));
            using (var tmp = new Bitmap(40, 40, PixelFormat.Format32bppArgb)) { using (var tg = Quality(tmp)) DrawLoudspeaker(tg, r.muted, Color.White); DrawFaded(g, tmp, new RectangleF(12, 57, 20, 20), r.on ? 1f : 0.5f, true); }
            Text(g, "72%", 16, FontStyle.Regular, r.on && !r.muted ? Color.White : grey, new RectangleF(138, 57, 50, 20), StringAlignment.Far);
            using (var bg = new SolidBrush(Color.FromArgb(204, 0x4D, 0x4D, 0x4D))) FillRound(g, bg, new RectangleF(12, 83, 176, 8), 4);
            using (var fg = new SolidBrush(r.on && !r.muted ? ColorTranslator.FromHtml("#0070D1") : Color.FromArgb(r.muted ? 77 : 102, 255, 255, 255))) FillRound(g, fg, new RectangleF(12, 83, 176 * 0.72f, 8), 4);
            g.Restore(st);
        }
        sheet.Save(file, ImageFormat.Png);
    }
}
