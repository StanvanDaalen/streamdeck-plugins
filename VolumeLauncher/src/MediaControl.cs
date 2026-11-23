using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Windows.Media.Control;

namespace GameVolume;

/// <summary>What an app is playing right now, as the Windows media controls report it.</summary>
public sealed record MediaInfo(
    string Title, string Artist, bool Playing, TimeSpan Position, TimeSpan Duration,
    string? Cover, string? Backdrop, string? BackdropDim, bool CanPrevious, bool CanNext, bool CanSeek)
{
    public bool HasTimeline => Duration > TimeSpan.FromSeconds(1);

    /// <summary>The video service this is from (YouTube, Netflix, …), when known.</summary>
    public StreamingService? Service { get; init; }

    /// <summary>Motorsport: the race weekend's country (ISO code, e.g. "es"), for its flag.</summary>
    public string? Country { get; init; }

    /// <summary>Season and episode number, when the service's title names them (for looking up the episode picture).</summary>
    public int? Season { get; init; }
    public int? Episode { get; init; }
}

/// <summary>
/// Play/pause and track skipping for one specific app, through the Windows media sessions
/// (the same ones the volume flyout shows). Unlike a media key, this never hits another player.
/// </summary>
public static class MediaControl
{
    private static GlobalSystemMediaTransportControlsSessionManager? _manager;
    private static readonly Dictionary<string, (string Key, string? Cover, string? Backdrop, string? BackdropDim)> Art = new(StringComparer.OrdinalIgnoreCase);

    // Windows media calls can hang (for instance while an app restarts); never let one block the plugin.
    private static Task<T> Wait<T>(Windows.Foundation.IAsyncOperation<T> op) => op.AsTask().WaitAsync(TimeSpan.FromSeconds(3));

    private static async Task<GlobalSystemMediaTransportControlsSession?> FindAsync(string process)
    {
        try { _manager ??= await Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync()); }
        catch { _manager = null; throw; }
        var name = process.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
        // Chrome reports "Chrome", Spotify "Spotify.exe", Edge "MSEdge": the process name is part of the id.
        return _manager.GetSessions().FirstOrDefault(s => s.SourceAppUserModelId.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Runs the action and returns a short status for the dial, or null when the app plays no media.</summary>
    public static async Task<string?> RunAsync(string process, string action)
    {
        try
        {
            var session = await FindAsync(process);
            if (session == null) return null;
            switch (action)
            {
                case "playpause":
                    bool playing = session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    await Wait(session.TryTogglePlayPauseAsync());
                    return playing ? "Paused" : "Playing";
                case "next":
                    await Wait(session.TrySkipNextAsync());
                    return "Next";
                case "previous":
                    await Wait(session.TrySkipPreviousAsync());
                    return "Previous";
            }
        }
        catch (Exception e) { SettingsStore.Log("media: " + e.Message); }
        return null;
    }

    /// <summary>The app's current media, or null when it has no media session or nothing to show.</summary>
    public static async Task<MediaInfo?> GetAsync(string process)
    {
        try
        {
            var session = await FindAsync(process);
            if (session == null) return null;
            var props = await Wait(session.TryGetMediaPropertiesAsync());
            var title = props.Title?.Trim() ?? "";
            if (title.Length == 0) return null;
            var artist = props.Artist?.Trim() ?? "";
            if (artist.Length == 0) artist = props.AlbumTitle?.Trim() ?? "";

            var key = title + "\u0001" + artist;
            if (!Art.TryGetValue(process, out var art) || art.Key != key)
            {
                var (cover, backdrop, dim) = await LoadArt(props.Thumbnail);
                art = Art[process] = (key, cover, backdrop, dim);
            }

            var playback = session.GetPlaybackInfo();
            bool playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var t = session.GetTimelineProperties();
            var duration = t.EndTime - t.StartTime;
            var position = t.Position - t.StartTime;
            if (playing && t.LastUpdatedTime.Year > 2000) position += DateTimeOffset.Now - t.LastUpdatedTime;
            if (duration > TimeSpan.Zero) position = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, duration.Ticks));
            else position = TimeSpan.Zero;

            return new MediaInfo(title, artist, playing, position, duration, art.Cover, art.Backdrop, art.BackdropDim,
                playback.Controls.IsPreviousEnabled, playback.Controls.IsNextEnabled, playback.Controls.IsPlaybackPositionEnabled);
        }
        catch (Exception e) { SettingsStore.Log("media info: " + e.Message); return null; }
    }

    /// <summary>Jumps to a position in what the app is playing (the timeline starts at zero).</summary>
    public static async Task SeekAsync(string process, TimeSpan position)
    {
        try
        {
            var session = await FindAsync(process);
            if (session == null) return;
            var start = session.GetTimelineProperties().StartTime;
            await Wait(session.TryChangePlaybackPositionAsync((start + position).Ticks));
        }
        catch (Exception e) { SettingsStore.Log("seek: " + e.Message); }
    }

    private static async Task<(string?, string?, string?)> LoadArt(Windows.Storage.Streams.IRandomAccessStreamReference? thumb)
    {
        if (thumb == null) return (null, null, null);
        try
        {
            using var stream = await Wait(thumb.OpenReadAsync());
            using var net = stream.AsStreamForRead();
            using var src = new Bitmap(net);
            // A site without artwork gets the browser's own icon (transparent corners): that is no picture of the video.
            if (src.GetPixel(0, 0).A < 20 && src.GetPixel(src.Width - 1, src.Height - 1).A < 20) return (null, null, null);
            return RenderArt(src);
        }
        catch (Exception e) { SettingsStore.Log("media art: " + e.Message); return (null, null, null); }
    }

    /// <summary>A picture as the dial's rounded cover, full-screen backdrop and darker backdrop (under the volume).</summary>
    public static (string?, string?, string?) RenderArt(Bitmap src)
    {
        try
        {

            // Cover: a square crop with rounded corners, like the Spotify dial's album art.
            using var square = ArtworkService.Cover(src, 80, 80);
            using var cover = new Bitmap(80, 80, PixelFormat.Format32bppArgb);
            using (var g = ArtworkService.HighQuality(cover))
            {
                using var path = Rounded(new Rectangle(0, 0, 80, 80), 8);
                g.SetClip(path);
                g.DrawImage(square, 0, 0, 80, 80);
            }

            // Backdrop: the full picture over the whole screen, darkened so white text stays readable.
            // While turning the volume, an even darker version keeps the volume bar calm over any picture.
            return (ArtworkService.DataUrl(cover), Backdrop(src, 150), Backdrop(src, 215));
        }
        catch (Exception e) { SettingsStore.Log("media art: " + e.Message); return (null, null, null); }
    }

    /// <summary>A flag: whole on a dark tile as the cover (never cropped), across the screen for the Picture style.</summary>
    public static (string?, string?, string?) RenderFlag(Bitmap src)
    {
        try
        {
            using var cover = new Bitmap(80, 80, PixelFormat.Format32bppArgb);
            using (var g = ArtworkService.HighQuality(cover))
            {
                using var tile = Rounded(new Rectangle(0, 0, 80, 80), 8);
                using var dark = new SolidBrush(Color.FromArgb(26, 26, 26));
                g.FillPath(dark, tile);
                double scale = Math.Min(68.0 / src.Width, 68.0 / src.Height);
                int w = (int)Math.Round(src.Width * scale), h = (int)Math.Round(src.Height * scale);
                var box = new Rectangle((80 - w) / 2, (80 - h) / 2, w, h);
                using var flag = Rounded(box, 4);
                g.SetClip(flag);
                g.DrawImage(src, box);
            }
            return (ArtworkService.DataUrl(cover), Backdrop(src, 150), Backdrop(src, 215));
        }
        catch (Exception e) { SettingsStore.Log("flag: " + e.Message); return (null, null, null); }
    }

    private static string Backdrop(Bitmap src, int shadeAlpha)
    {
        using var wide = ArtworkService.Cover(src, 200, 100);
        using (var g = ArtworkService.HighQuality(wide))
        {
            using var shade = new SolidBrush(Color.FromArgb(shadeAlpha, 0, 0, 0));
            g.FillRectangle(shade, 0, 0, 200, 100);
        }
        return ArtworkService.DataUrl(wide);
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
