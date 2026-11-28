using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using NAudio.CoreAudioApi;
using Windows.Media;
using Windows.Media.Control;

namespace StanSpotify;

internal sealed record MediaState(
    bool HasSession, bool Playing, string Title, string Artist, string? Artwork,
    TimeSpan Position, TimeSpan Duration, bool? Shuffle, MediaPlaybackAutoRepeatMode? Repeat, bool CanSeek,
    string? Backdrop = null, string? BackdropDim = null)
{
    public static readonly MediaState Empty = new(false, false, "", "", null, TimeSpan.Zero, TimeSpan.Zero, null, null, false);
}

/// <summary>Spotify's now-playing info and transport controls through the Windows media controls.</summary>
internal sealed class SpotifyMedia
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string _trackKey = "";
    private string? _artwork, _backdrop, _backdropDim;
    private string _title = "", _artist = "";

    public MediaState State { get; private set; } = MediaState.Empty;

    /// <summary>Windows media calls can hang (e.g. while Spotify restarts); never let one block the plugin.</summary>
    private static Task<T> Wait<T>(Windows.Foundation.IAsyncOperation<T> op) =>
        op.AsTask().WaitAsync(TimeSpan.FromSeconds(3));

    public static bool IsRunning => Process.GetProcessesByName("Spotify").Length > 0;

    public static void Launch()
    {
        try { Process.Start(new ProcessStartInfo("spotify:") { UseShellExecute = true }); }
        catch (Exception e) { Log.Write("launch: " + e.Message); }
    }

    private static bool IsSpotify(GlobalSystemMediaTransportControlsSession s) =>
        s.SourceAppUserModelId.Contains("spotify", StringComparison.OrdinalIgnoreCase);

    public async Task RefreshAsync()
    {
        try
        {
            _manager ??= await Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
            _session = _manager.GetSessions().FirstOrDefault(IsSpotify);
            if (_session == null || !IsRunning) { State = MediaState.Empty; return; }

            var playback = _session.GetPlaybackInfo();
            var timeline = _session.GetTimelineProperties();
            var props = await Wait(_session.TryGetMediaPropertiesAsync());
            var key = $"{props.Title}\u0001{props.Artist}";
            if (key != _trackKey)
            {
                _trackKey = key;
                _title = props.Title ?? "";
                _artist = props.Artist ?? "";
                (_artwork, _backdrop, _backdropDim) = await LoadArtwork(props.Thumbnail);
            }

            bool playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var duration = timeline.EndTime - timeline.StartTime;
            var position = timeline.Position;
            if (playing && timeline.LastUpdatedTime.Year > 2000)
                position += DateTimeOffset.Now - timeline.LastUpdatedTime;
            if (duration > TimeSpan.Zero) position = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, duration.Ticks));

            State = new MediaState(true, playing, _title, _artist, _artwork, position, duration,
                playback.IsShuffleActive, playback.AutoRepeatMode, playback.Controls.IsPlaybackPositionEnabled,
                _backdrop, _backdropDim);
        }
        catch (Exception e)
        {
            Log.Write("media: " + e.Message);
            if (e is TimeoutException) _manager = null; // start over with a fresh connection
            _session = null;
            State = MediaState.Empty;
        }
    }

    private static async Task<(string?, string?, string?)> LoadArtwork(Windows.Storage.Streams.IRandomAccessStreamReference? thumb)
    {
        if (thumb == null) return (null, null, null);
        try
        {
            using var stream = await Wait(thumb.OpenReadAsync());
            using var net = stream.AsStreamForRead();
            using var src = new Bitmap(net);
            // Spotify's Windows thumbnail carries a branded strip; crop to the square cover in the middle.
            int side = Math.Min(src.Width, src.Height);
            var crop = new Rectangle((src.Width - side) / 2, (src.Height - side) / 2, side, side);
            using var bmp = new Bitmap(80, 80, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                using var path = Rounded(new Rectangle(0, 0, 80, 80), 8);
                g.SetClip(path);
                g.DrawImage(src, new Rectangle(0, 0, 80, 80), crop, GraphicsUnit.Pixel);
            }
            // The Picture style: the cover over the whole screen, darkened so white text stays readable,
            // and an even darker version while the volume shows.
            return (DataUrl(bmp), Backdrop(src, crop, 150), Backdrop(src, crop, 215));
        }
        catch (Exception e) { Log.Write("artwork: " + e.Message); return (null, null, null); }
    }

    private static string DataUrl(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    }

    private static string Backdrop(Bitmap src, Rectangle square, int shadeAlpha)
    {
        // A square cover on a wide screen: use its middle band, scaled to fill 200x100.
        var band = new Rectangle(square.X, square.Y + square.Height / 4, square.Width, square.Height / 2);
        using var bmp = new Bitmap(200, 100, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, new Rectangle(0, 0, 200, 100), band, GraphicsUnit.Pixel);
            using var shade = new SolidBrush(Color.FromArgb(shadeAlpha, 0, 0, 0));
            g.FillRectangle(shade, 0, 0, 200, 100);
        }
        return DataUrl(bmp);
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

    public async Task<bool> WaitForSessionAsync(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            await RefreshAsync();
            if (State.HasSession) return true;
            await Task.Delay(500);
        }
        return false;
    }

    public async Task PlayPause() { if (_session != null) await Wait(_session.TryTogglePlayPauseAsync()); }
    public async Task Play() { if (_session != null) await Wait(_session.TryPlayAsync()); }
    public async Task Next() { if (_session != null) await Wait(_session.TrySkipNextAsync()); }
    public async Task Previous() { if (_session != null) await Wait(_session.TrySkipPreviousAsync()); }

    /// <summary>Jumps to a position in the current track (the timeline starts at zero).</summary>
    public async Task Seek(TimeSpan position)
    {
        if (_session == null) return;
        var start = _session.GetTimelineProperties().StartTime;
        await Wait(_session.TryChangePlaybackPositionAsync((start + position).Ticks));
    }

    public async Task ToggleShuffle()
    {
        if (_session == null) return;
        var on = _session.GetPlaybackInfo().IsShuffleActive ?? false;
        await Wait(_session.TryChangeShuffleActiveAsync(!on));
    }

    public async Task CycleRepeat()
    {
        if (_session == null) return;
        var next = _session.GetPlaybackInfo().AutoRepeatMode switch
        {
            MediaPlaybackAutoRepeatMode.None => MediaPlaybackAutoRepeatMode.List,
            MediaPlaybackAutoRepeatMode.List => MediaPlaybackAutoRepeatMode.Track,
            _ => MediaPlaybackAutoRepeatMode.None,
        };
        await Wait(_session.TryChangeAutoRepeatModeAsync(next));
    }
}

/// <summary>Spotify's own volume in the Windows mixer, on every active output device.</summary>
internal static class SpotifyAudio
{
    private static readonly MMDeviceEnumerator Enumerator = new();
    private static List<(MMDevice Device, AudioSessionControl Control)> _sessions = new();
    private static readonly List<MMDevice> Devices = new();
    private static DateTime _loaded = DateTime.MinValue;

    private static List<(MMDevice Device, AudioSessionControl Control)> Sessions()
    {
        if (DateTime.UtcNow - _loaded < TimeSpan.FromSeconds(3) && _sessions.Count > 0) return _sessions;
        foreach (var (_, c) in _sessions) try { c.Dispose(); } catch { }
        foreach (var d in Devices) try { d.Dispose(); } catch { }
        Devices.Clear();
        var list = new List<(MMDevice, AudioSessionControl)>();
        var pids = Process.GetProcessesByName("Spotify").Select(p => { using (p) return (uint)p.Id; }).ToHashSet();
        if (pids.Count > 0)
        {
            foreach (var device in Enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                Devices.Add(device); // every enumerated device is disposed on the next refresh
                try
                {
                    var mgr = device.AudioSessionManager;
                    mgr.RefreshSessions();
                    for (int i = 0; i < mgr.Sessions.Count; i++)
                    {
                        var c = mgr.Sessions[i];
                        if (pids.Contains(c.GetProcessID)) list.Add((device, c));
                        else c.Dispose();
                    }
                }
                catch (Exception e) { Log.Write("audio: " + e.Message); }
            }
        }
        _sessions = list;
        _loaded = DateTime.UtcNow;
        return list;
    }

    /// <summary>0–100, or null when Spotify has no audio session yet.</summary>
    public static int? Volume()
    {
        float? max = null;
        foreach (var (_, c) in Sessions())
            try { var v = c.SimpleAudioVolume.Volume; if (max == null || v > max) max = v; } catch { }
        return max == null ? null : (int)Math.Round(max.Value * 100);
    }

    public static bool Muted()
    {
        var list = Sessions();
        if (list.Count == 0) return false;
        foreach (var (_, c) in list) try { if (!c.SimpleAudioVolume.Mute) return false; } catch { }
        return true;
    }

    public static int? Change(int delta)
    {
        var current = Volume();
        if (current == null) return null;
        var value = Math.Clamp(current.Value + delta, 0, 100);
        foreach (var (_, c) in Sessions())
            try { c.SimpleAudioVolume.Volume = value / 100f; if (delta > 0) c.SimpleAudioVolume.Mute = false; } catch { }
        return value;
    }

    public static void ToggleMute()
    {
        var mute = !Muted();
        foreach (var (_, c) in Sessions()) try { c.SimpleAudioVolume.Mute = mute; } catch { }
    }
}

/// <summary>Shows or hides the Spotify window, like clicking it in the taskbar.</summary>
internal static class SpotifyWindow
{
    private static IntPtr Find()
    {
        IntPtr best = IntPtr.Zero;
        long bestArea = -1;
        var pids = Process.GetProcessesByName("Spotify").Select(p => { using (p) return (uint)p.Id; }).ToHashSet();
        if (pids.Count == 0) return IntPtr.Zero;
        Native.EnumWindows((hwnd, _) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pids.Contains(pid) && Native.IsWindowVisible(hwnd) && Native.GetWindow(hwnd, 4) == IntPtr.Zero
                && !Native.IsCloaked(hwnd) && Native.WindowTitle(hwnd).Length > 0)
            {
                var area = Native.IsIconic(hwnd) ? 0 : Native.WindowArea(hwnd);
                if (area > bestArea) { bestArea = area; best = hwnd; }
            }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    public static void Toggle()
    {
        var hwnd = Find();
        if (hwnd == IntPtr.Zero) { SpotifyMedia.Launch(); return; } // closed to the tray: Spotify reopens its window
        if (Native.GetForegroundWindow() == hwnd && !Native.IsIconic(hwnd)) { Native.ShowWindowAsync(hwnd, 6); return; }
        if (Native.IsIconic(hwnd)) Native.ShowWindowAsync(hwnd, 9);
        var fg = Native.GetForegroundWindow();
        uint fgThread = Native.GetWindowThreadProcessId(fg, out _);
        uint me = Native.GetCurrentThreadId();
        var attached = fgThread != 0 && fgThread != me && Native.AttachThreadInput(me, fgThread, true);
        Native.BringWindowToTop(hwnd);
        Native.SetForegroundWindow(hwnd);
        if (attached) Native.AttachThreadInput(me, fgThread, false);
        if (Native.GetForegroundWindow() != hwnd)
        {
            Native.keybd_event(0x12, 0, 0, UIntPtr.Zero);
            Native.keybd_event(0x12, 0, 2, UIntPtr.Zero);
            Native.SetForegroundWindow(hwnd);
        }
    }

    public static void Open(string uri)
    {
        if (!uri.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
        {
            // Accept a share link like https://open.spotify.com/playlist/<id>?si=...
            var m = System.Text.RegularExpressions.Regex.Match(uri, @"open\.spotify\.com/(?:intl-[a-z-]+/)?([a-z]+)/([A-Za-z0-9]+)");
            if (!m.Success) return;
            uri = $"spotify:{m.Groups[1].Value}:{m.Groups[2].Value}";
        }
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception e) { Log.Write("open: " + e.Message); }
    }
}
