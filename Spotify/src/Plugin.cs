using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Windows.Media;

namespace StanSpotify;

internal sealed class Ctx
{
    public required string Id { get; init; }
    public JsonObject Settings { get; set; } = new();
    public readonly Dictionary<string, string> Sent = new();

    // Press detection
    public bool Pressed, HoldFired, Turned;
    public int Count, Seq;
    public DateTime LastUp = DateTime.MinValue;

    public DateTime VolumeUntil = DateTime.MinValue;

    // Hold + turn = seek: the screen shows where the track will be right away.
    public TimeSpan SeekTarget;
    public DateTime SeekUntil = DateTime.MinValue;

    public string? Layout;
}

internal sealed class Plugin
{
    private const string PlayerAction = "com.stan.spotify.player";
    private static readonly TimeSpan ClickWindow = TimeSpan.FromMilliseconds(360);
    private static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan VolumeShown = TimeSpan.FromMilliseconds(1500);

    public static readonly Dictionary<string, string> Defaults = new()
    {
        ["press1"] = "playPause",
        ["press2"] = "next",
        ["press3"] = "previous",
        ["pressHold"] = "window",
        ["touch"] = "playPause",
        ["accent"] = "play",
    };

    private readonly int _port;
    private readonly string _uuid, _register;
    private readonly ClientWebSocket _ws = new();
    private readonly SemaphoreSlim _gate = new(1, 1), _sendLock = new(1, 1);
    private readonly Dictionary<string, Ctx> _contexts = new();
    private readonly SpotifyMedia _media = new();
    private DateTime _lastSkip = DateTime.MinValue;

    public Plugin(int port, string uuid, string register) { _port = port; _uuid = uuid; _register = register; }

    public async Task RunAsync()
    {
        await _ws.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}"), CancellationToken.None);
        await SendAsync(new JsonObject { ["event"] = _register, ["uuid"] = _uuid });
        _ = Task.Run(TickLoop);

        var buffer = new byte[1 << 16];
        using var ms = new MemoryStream();
        while (_ws.State == WebSocketState.Open)
        {
            ms.SetLength(0);
            WebSocketReceiveResult r;
            do
            {
                r = await _ws.ReceiveAsync(buffer, CancellationToken.None);
                if (r.MessageType == WebSocketMessageType.Close) return;
                ms.Write(buffer, 0, r.Count);
            } while (!r.EndOfMessage);
            var text = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            await WithGate(() => HandleAsync(JsonNode.Parse(text)!.AsObject()));
        }
    }

    private async Task WithGate(Func<Task> f)
    {
        if (!await _gate.WaitAsync(TimeSpan.FromSeconds(10)))
        {
            Log.Write("gate busy for 10 s, skipped one event");
            return;
        }
        try { await f(); }
        catch (Exception e) { Log.Write("error: " + e); }
        finally { _gate.Release(); }
    }

    private async Task SendAsync(JsonObject msg)
    {
        var bytes = Encoding.UTF8.GetBytes(msg.ToJsonString());
        await _sendLock.WaitAsync();
        try { if (_ws.State == WebSocketState.Open) await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
        finally { _sendLock.Release(); }
    }

    private async Task TickLoop()
    {
        while (_ws.State == WebSocketState.Open)
        {
            await WithGate(async () =>
            {
                if (_contexts.Count == 0) return;
                await _media.RefreshAsync();
                foreach (var c in _contexts.Values.ToList()) await RenderAsync(c);
            });
            await Task.Delay(500);
        }
    }

    // ---------- settings ----------

    private static string Str(Ctx c, string key) =>
        c.Settings[key] is JsonValue v && v.TryGetValue(out string? s) && s != null ? s : Defaults.GetValueOrDefault(key, "");

    /// <summary>Hold + turn: "seek" (default) or "other" (whatever plain turning does not do).</summary>
    private static bool PressSeeks(Ctx c) =>
        c.Settings["pressRotate"] is not JsonValue v || !v.TryGetValue(out string? s) || s != "other";

    /// <summary>The screen while Spotify runs: player (like the official dial), picture or compact.</summary>
    private const string SpotifyGreen = "#1ED760";

    /// <summary>How the dial shows it is Spotify: "play" (green play button), "timeline", "logo" or "off".</summary>
    private static string Accent(Ctx c) => Str(c, "accent") switch { "timeline" => "timeline", "logo" => "logo", "off" => "off", _ => "play" };

    private static string Look(Ctx c) => Str(c, "look") switch { "picture" => "picture", "compact" => "compact", _ => "player" };

    private static bool PreferVolume(Ctx c) =>
        c.Settings["preferVolume"] is not JsonValue v || !v.TryGetValue(out bool b) || b; // default: rotate = volume

    private static int Step(Ctx c)
    {
        var n = c.Settings["volumeStep"];
        int step = n is JsonValue v && v.TryGetValue(out int i) ? i
                 : n is JsonValue s && s.TryGetValue(out string? t) && int.TryParse(t, out var p) ? p : 5;
        return Math.Clamp(step, 1, 10);
    }

    // ---------- events ----------

    private async Task HandleAsync(JsonObject msg)
    {
        var ev = (string?)msg["event"];
        var context = (string?)msg["context"];
        var payload = msg["payload"] as JsonObject;
        if (context == null) return;

        switch (ev)
        {
            case "willAppear":
                if ((string?)msg["action"] != PlayerAction) return;
                var c = new Ctx { Id = context, Settings = payload?["settings"] as JsonObject ?? new() };
                _contexts[context] = c;
                await _media.RefreshAsync();
                await RenderAsync(c);
                return;
            case "willDisappear":
                if (_contexts.Remove(context, out var gone)) { gone.Seq++; gone.Pressed = false; } // cancels pending clicks/holds
                return;
        }

        if (!_contexts.TryGetValue(context, out var ctx)) return;
        switch (ev)
        {
            case "didReceiveSettings":
                ctx.Settings = payload?["settings"] as JsonObject ?? new();
                ctx.Sent.Clear();
                await RenderAsync(ctx);
                break;
            case "dialDown": OnDown(ctx); break;
            case "dialUp": await OnUp(ctx); break;
            case "dialRotate": await OnRotate(ctx, (int?)payload?["ticks"] ?? 0); break;
            case "touchTap":
                bool hold = (bool?)payload?["hold"] ?? false;
                int x = (int?)payload?["tapPos"]?[0] ?? 0;
                if (hold) await RunAsync(ctx, x > 100 ? "next" : "previous");
                else await RunAsync(ctx, Str(ctx, "touch"));
                break;
        }
    }

    private void OnDown(Ctx c)
    {
        c.Pressed = true;
        c.HoldFired = false;
        c.Turned = false;
        if (DateTime.UtcNow - c.LastUp > ClickWindow) c.Count = 0;
        c.Count++;
        int seq = ++c.Seq;
        if (c.Count == 1)
            _ = Task.Delay(HoldTime).ContinueWith(_ => WithGate(async () =>
            {
                if (c.Seq != seq || !c.Pressed || c.Turned) return;
                c.HoldFired = true;
                if (Str(c, "pressHold") == "showVolume") { c.VolumeUntil = DateTime.MaxValue; await RenderAsync(c); }
            }));
    }

    private async Task OnUp(Ctx c)
    {
        c.Pressed = false;
        c.LastUp = DateTime.UtcNow;
        int seq = ++c.Seq;
        if (c.VolumeUntil == DateTime.MaxValue) { c.VolumeUntil = DateTime.UtcNow + VolumeShown; await RenderAsync(c); }

        if (c.Turned) { c.Count = 0; return; }           // press + turn is its own gesture
        if (c.HoldFired) { c.Count = 0; await RunAsync(c, Str(c, "pressHold")); return; }

        bool more2 = Str(c, "press2") != "none", more3 = Str(c, "press3") != "none";
        bool final = c.Count >= 3 || (c.Count == 2 && !more3) || (c.Count == 1 && !more2 && !more3);
        if (final) { await Fire(c); return; }

        _ = Task.Delay(ClickWindow).ContinueWith(_ => WithGate(async () =>
        {
            if (c.Seq == seq && !c.Pressed) await Fire(c);
        }));
    }

    private async Task Fire(Ctx c)
    {
        var key = c.Count switch { 1 => "press1", 2 => "press2", _ => "press3" };
        c.Count = 0;
        await RunAsync(c, Str(c, key));
    }

    private async Task OnRotate(Ctx c, int ticks)
    {
        if (ticks == 0) return;
        if (c.Pressed) c.Turned = true;
        if (c.Pressed && PressSeeks(c))
        {
            var s = _media.State;
            if (!SpotifyMedia.IsRunning || !s.HasSession || !s.CanSeek || s.Duration <= TimeSpan.Zero) return;
            var from = DateTime.UtcNow < c.SeekUntil ? c.SeekTarget : s.Position;
            var target = from + TimeSpan.FromSeconds(ticks * 5);
            if (target < TimeSpan.Zero) target = TimeSpan.Zero;
            if (target > s.Duration) target = s.Duration - TimeSpan.FromSeconds(1);
            c.SeekTarget = target;
            c.SeekUntil = DateTime.UtcNow + TimeSpan.FromSeconds(1.5);
            try { await _media.Seek(target); } catch (Exception e) { Log.Write("seek: " + e.Message); }
            await RenderAsync(c);
            return;
        }
        bool volume = PreferVolume(c) != c.Pressed;
        if (volume)
        {
            if (!SpotifyMedia.IsRunning) return;
            if (SpotifyAudio.Change(ticks * Step(c)) == null) return;
            if (c.VolumeUntil != DateTime.MaxValue) c.VolumeUntil = DateTime.UtcNow + VolumeShown;
            await RenderAsync(c);
            _ = Task.Delay(VolumeShown + TimeSpan.FromMilliseconds(50)).ContinueWith(_ => WithGate(() => RenderAsync(c)));
        }
        else
        {
            if (DateTime.UtcNow - _lastSkip < TimeSpan.FromMilliseconds(350)) return;
            _lastSkip = DateTime.UtcNow;
            await RunAsync(c, ticks > 0 ? "next" : "previous");
        }
    }

    // ---------- actions ----------

    public static readonly string[] NeedsSession = { "playPause", "play", "next", "previous", "shuffle", "repeat" };

    private async Task RunAsync(Ctx c, string action)
    {
        if (action is "" or "none" or "showVolume") return;
        Log.Write("action: " + action);

        if (!SpotifyMedia.IsRunning)
        {
            // Like the official plugin: anything wakes Spotify up; play/pause also starts the music.
            SpotifyMedia.Launch();
            if (action == "playPause" && await _media.WaitForSessionAsync(TimeSpan.FromSeconds(12)))
            {
                await Task.Delay(1500);
                await _media.Play();
            }
            return;
        }

        await _media.RefreshAsync();
        switch (action)
        {
            case "playPause": await _media.PlayPause(); break;
            case "next": await _media.Next(); break;
            case "previous": await _media.Previous(); break;
            case "shuffle": await _media.ToggleShuffle(); break;
            case "repeat": await _media.CycleRepeat(); break;
            case "volumeUp": SpotifyAudio.Change(Step(c)); c.VolumeUntil = DateTime.UtcNow + VolumeShown; break;
            case "volumeDown": SpotifyAudio.Change(-Step(c)); c.VolumeUntil = DateTime.UtcNow + VolumeShown; break;
            case "mute": SpotifyAudio.ToggleMute(); c.VolumeUntil = DateTime.UtcNow + VolumeShown; break;
            case "window": SpotifyWindow.Toggle(); break;
            case "playlist":
                var uri = Str(c, "playlistUri").Trim();
                if (uri.Length > 0) SpotifyWindow.Open(uri);
                else await SendAsync(new JsonObject { ["event"] = "showAlert", ["context"] = c.Id });
                break;
        }
        await Task.Delay(150);
        await _media.RefreshAsync();
        await RenderAsync(c);
        if (c.VolumeUntil > DateTime.UtcNow && c.VolumeUntil != DateTime.MaxValue)
            _ = Task.Delay(VolumeShown + TimeSpan.FromMilliseconds(50)).ContinueWith(_ => WithGate(() => RenderAsync(c)));
    }

    // ---------- display ----------

    private static string Clock(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    private async Task RenderAsync(Ctx c)
    {
        if (!_contexts.ContainsKey(c.Id)) return; // action was removed while a timer was pending
        var s = _media.State;
        bool running = SpotifyMedia.IsRunning;
        bool showVolume = running && DateTime.UtcNow < c.VolumeUntil;
        int? volume = showVolume ? SpotifyAudio.Volume() : null;
        bool muted = showVolume && SpotifyAudio.Muted();
        showVolume &= volume != null;
        bool hasTrack = running && s.HasSession && s.Title.Length > 0;
        bool seeking = DateTime.UtcNow < c.SeekUntil;
        if (seeking) showVolume = false;
        var position = seeking ? c.SeekTarget : s.Position;
        int progress = s.Duration.TotalSeconds > 0 ? (int)Math.Round(100 * position.TotalSeconds / s.Duration.TotalSeconds) : 0;
        string titleText = hasTrack ? s.Title : running ? "Nothing playing" : "";

        var look = Look(c);
        var accent = Accent(c);
        var layout = look == "picture" && accent == "logo" ? "layouts/picture-logo.json" : $"layouts/{look}.json";
        if (c.Layout != layout)
        {
            c.Layout = layout;
            c.Sent.Clear();
            await SendAsync(new JsonObject { ["event"] = "setFeedbackLayout", ["context"] = c.Id, ["payload"] = new JsonObject { ["layout"] = layout } });
        }

        var items = new Dictionary<string, JsonObject>
        {
            ["overlay"] = new() { ["value"] = Icons.Get("notRunning"), ["opacity"] = running ? 0 : 1 },
            ["time"] = new() { ["value"] = hasTrack ? Clock(position) : "", ["opacity"] = showVolume ? 0 : 1 },
            ["duration"] = new() { ["value"] = hasTrack ? Clock(s.Duration) : "", ["opacity"] = showVolume ? 0 : 1 },
            ["progress"] = new() { ["value"] = Math.Clamp(progress, 0, 100), ["bar_fill_c"] = accent == "timeline" ? SpotifyGreen : "#FFFFFF", ["opacity"] = showVolume ? 0 : (accent == "timeline" ? 1 : 0.8) },
            ["playIcon"] = new() { ["value"] = Icons.Get(s.Playing ? "pause" : "play", accent == "play" ? SpotifyGreen : null) },
            ["volumeIcon"] = new() { ["value"] = Icons.Volume(volume, muted), ["opacity"] = showVolume ? 1 : 0 },
            ["volumeLevel"] = new() { ["value"] = muted ? "Mute" : $"{volume ?? 0}%", ["opacity"] = showVolume ? 1 : 0 },
            ["volumeBar"] = new() { ["value"] = volume ?? 0, ["opacity"] = showVolume ? 1 : 0 },
        };
        if (look is "player" or "picture")
            items["badge"] = new() { ["value"] = "imgs/badge-spotify.svg", ["opacity"] = accent == "logo" && hasTrack ? 1 : 0 };
        if (look is "player" or "picture")
        {
            items["shuffleIcon"] = new() { ["value"] = Icons.Get(s.Shuffle == true ? "shuffleOn" : "shuffleOff") };
            items["autoRepeatIcon"] = new()
            {
                ["value"] = Icons.Get(s.Repeat switch
                {
                    MediaPlaybackAutoRepeatMode.Track => "repeatOne",
                    MediaPlaybackAutoRepeatMode.List => "repeatList",
                    _ => "repeatOff",
                })
            };
        }
        switch (look)
        {
            case "player":
                items["artwork"] = new() { ["value"] = hasTrack && s.Artwork != null ? s.Artwork : Icons.Get("noArtwork") };
                items["title"] = new() { ["value"] = titleText };
                items["artist"] = new() { ["value"] = hasTrack ? s.Artist : "" };
                break;
            case "picture":
                items["backdrop"] = new() { ["value"] = (hasTrack ? (showVolume ? s.BackdropDim : s.Backdrop) : null) ?? "imgs/black.svg" };
                items["volumePanel"] = new() { ["opacity"] = showVolume ? 1 : 0 }; // calm, dark base under the volume
                items["title"] = new() { ["value"] = titleText };
                items["artist"] = new() { ["value"] = hasTrack ? s.Artist : "" };
                break;
            default:
                items["icon"] = new() { ["value"] = "imgs/logo.svg" };
                items["title"] = new() { ["value"] = "Spotify" };
                items["track"] = new() { ["value"] = titleText };
                break;
        }

        var payload = new JsonObject();
        foreach (var (key, value) in items)
        {
            var json = value.ToJsonString();
            if (c.Sent.GetValueOrDefault(key) == json) continue;
            c.Sent[key] = json;
            payload[key] = value;
        }
        if (payload.Count > 0)
            await SendAsync(new JsonObject { ["event"] = "setFeedback", ["context"] = c.Id, ["payload"] = payload });
    }
}
