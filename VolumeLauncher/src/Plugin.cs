using System.Drawing;
using System.Drawing.Imaging;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GameVolume;

internal sealed class Ctx
{
    public required string Id { get; init; }
    public required string Action { get; init; }
    public JsonObject Settings { get; set; } = new();
    public string? Layout, ArtId, LastJson, KeyHash;
    public bool RotatedWhilePressed, Encoder, HoldFired;
    public CancellationTokenSource? HoldTimer, ClickTimer;
    public string? Overlay;
    public DateTime OverlayUntil, VolumeUntil, SeekUntil;
    public readonly Dictionary<string, string> MediaSent = new();
    public int Clicks;
    public TimeSpan SeekTarget;
    public bool SeekSending, SeekPending;
}

internal sealed class Plugin
{
    private const string GameAction = "com.stan.gamevolume.game";
    private const string AppAction = "com.stan.gamevolume.app";
    private const string Standard = "#FFFFFF", Dim = "#777777", MutedBar = "#555555";

    private readonly int _port;
    private readonly string _uuid, _register;
    private readonly ClientWebSocket _ws = new();
    private readonly SemaphoreSlim _gate = new(1, 1), _sendLock = new(1, 1);
    private readonly Dictionary<string, Ctx> _contexts = new();

    private readonly GameLibrary _library = new();
    private readonly GameDetector _detector;
    private readonly ArtworkService _art;
    private readonly AudioSessions _audio = new();
    private readonly LauncherKeys _launcher;

    private RunningGame? _game;
    private int _missedScans;
    private readonly HashSet<string> _appliedSessions = new();
    private readonly HashSet<string> _appSessionsSeen = new();
    private bool _appSessionsSeeded;
    private DateTime _libraryLoaded = DateTime.MinValue;
    private readonly Dictionary<string, string> _knownAppExe = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MediaInfo?> _media = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan VolumeShown = TimeSpan.FromMilliseconds(1500);

    private string? _piContext;

    private static GlobalSettings G => SettingsStore.Current;

    private void ToggleApp(Ctx c)
    {
        var app = Str(c, "app", "");
        if (app.Length == 0) return;
        if (WindowControl.FindApp(app) is { } window) { WindowControl.ToggleApp(window); return; }
        var exe = _knownAppExe.GetValueOrDefault(app) ?? G.AppExes.GetValueOrDefault(app) ?? Str(c, "appExe", "");
        if (exe.Length == 0 || !File.Exists(exe)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true }); }
        catch (Exception e) { SettingsStore.Log("start app: " + e.Message); }
    }

    public Plugin(int port, string uuid, string register)
    {
        _port = port; _uuid = uuid; _register = register;
        _detector = new GameDetector(_library);
        _art = new ArtworkService(_library);
        _launcher = new LauncherKeys(_library, _detector, _art, SendAsync, WithGate, OnLauncherLogo);
    }

    /// <summary>A launcher key set a game's logo: every dial and key of that game shows it.</summary>
    private async Task OnLauncherLogo(string gameId)
    {
        _art.Invalidate(gameId);
        foreach (var x in _contexts.Values) { x.ArtId = null; x.KeyHash = null; x.LastJson = null; }
        await RenderAllAsync();
        await _launcher.Redraw();
    }

    public async Task RunAsync()
    {
        await _ws.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}"), CancellationToken.None);
        await SendAsync(new JsonObject { ["event"] = _register, ["uuid"] = _uuid });
        _ = Task.Run(TickLoop);
        _ = Task.Run(MediaLoop);

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
        await _gate.WaitAsync();
        try { await f(); }
        catch (Exception e) { SettingsStore.Log("error: " + e); }
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
            await WithGate(TickAsync);
            await Task.Delay(2000);
        }
    }

    /// <summary>Once a second: what each App Volume dial's app is playing, for its media screen.</summary>
    private async Task MediaLoop()
    {
        while (_ws.State == WebSocketState.Open)
        {
            await Task.Delay(1000);
            List<string> apps = new();
            await WithGate(() =>
            {
                apps = _contexts.Values.Where(c => c.Action == AppAction && MediaStyle(c) != "off")
                    .Select(c => Str(c, "app", "")).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                return Task.CompletedTask;
            });
            await WithGate(() =>
            {
                // An app that had no audio yet: pick up its new audio quickly, so the chosen level applies at once.
                if (_contexts.Values.Any(c => c.Action == AppAction && Str(c, "app", "") is { Length: > 0 } a
                                              && G.AppVolumes.ContainsKey(a) && !_audio.ForProcess(a).Any()))
                {
                    _audio.Refresh();
                    ApplyAppPresets();
                }
                return Task.CompletedTask;
            });
            foreach (var app in apps)
            {
                var info = await MediaControl.GetAsync(app); // outside the gate: never holds up dial presses
                await WithGate(async () =>
                {
                    _media[app] = info;
                    foreach (var c in _contexts.Values.Where(c => c.Action == AppAction && Str(c, "app", "").Equals(app, StringComparison.OrdinalIgnoreCase)).ToList())
                        await RenderDial(c);
                });
            }
        }
    }

    private const int SeekStep = 5; // seconds per tick while the dial is held and turned

    private async Task Seek(Ctx c, MediaInfo m, int ticks)
    {
        var from = DateTime.UtcNow < c.SeekUntil ? c.SeekTarget : m.Position;
        var target = from + TimeSpan.FromSeconds(ticks * SeekStep);
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        if (target > m.Duration) target = m.Duration - TimeSpan.FromSeconds(1);
        c.SeekTarget = target;
        c.SeekUntil = DateTime.UtcNow + TimeSpan.FromSeconds(1.5);
        // One request at a time per dial, always to the newest target: fast turning never lands on an older spot.
        bool start;
        lock (c) { c.SeekPending = true; start = !c.SeekSending; c.SeekSending = true; }
        if (start)
        {
            var app = Str(c, "app", "");
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    TimeSpan next;
                    lock (c)
                    {
                        if (!c.SeekPending) { c.SeekSending = false; return; }
                        c.SeekPending = false;
                        next = c.SeekTarget;
                    }
                    await MediaControl.SeekAsync(app, next); // outside the gate; never throws
                }
            });
        }
        await RenderDial(c);
    }

    /// <summary>How a media dial shows which app it is: "play" (coloured play button), "timeline", "logo" or "off".</summary>
    /// <summary>
    /// How the dial shows which app it is; any combination can be on. Older settings stored one choice in
    /// "accent" (play / timeline / logo / off): that choice is the starting point until a box is ticked.
    /// </summary>
    private static (bool Play, bool Timeline, bool Logo) Indicators(Ctx c)
    {
        var old = c.Settings["accent"] is JsonValue a && a.TryGetValue<string>(out var s) ? s : "play";
        bool Flag(string key, bool fallback) => c.Settings[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;
        return (Flag("accentPlay", old == "play"), Flag("accentTimeline", old == "timeline"), Flag("accentLogo", old == "logo"));
    }

    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase) { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi" };

    /// <summary>The app's own colour and black-and-white logo. Browsers mostly play YouTube, so they get its red.</summary>
    private static (string? Color, string? Badge) AppIdentity(string app) =>
        Browsers.Contains(app) ? ("#FF0033", "images/media/badge-youtube.svg")
        : app.Equals("spotify", StringComparison.OrdinalIgnoreCase) ? ("#1ED760", "images/media/badge-spotify.svg")
        : (null, null);

    /// <summary>
    /// App Volume while the app plays nothing, built like the media dials and the PS5 dial: logo, name and
    /// status on top, then "Volume" with the level and a volume bar. The Classic style keeps the old look.
    /// </summary>
    private async Task RenderIdle(Ctx c)
    {
        const string layout = "layouts/app-player.json";
        if (c.Layout != layout)
        {
            c.Layout = layout; c.ArtId = null; c.LastJson = null; c.MediaSent.Clear();
            await SendAsync(new JsonObject { ["event"] = "setFeedbackLayout", ["context"] = c.Id, ["payload"] = new JsonObject { ["layout"] = layout } });
        }
        var app = Str(c, "app", "");
        var exe = _knownAppExe.GetValueOrDefault(app) ?? G.AppExes.GetValueOrDefault(app);
        var art = _art.ForExe("app:" + app.ToLowerInvariant(), exe) ?? _art.NoGame;
        var sessions = SessionsFor(c);
        bool audio = sessions.Count > 0;
        // Without audio, show the level the app will start at (chosen on this dial), so it can be set in advance.
        float? level = audio ? AudioSessions.Volume(sessions) : G.AppVolumes.TryGetValue(app, out var preset) ? preset : null;
        int volume = (int)Math.Round((level ?? 0f) * 100f);
        bool muted = audio && AudioSessions.Muted(sessions);

        var name = AppName(app, exe);
        if (Str(c, "titleMode", "app") == "custom" && Str(c, "customTitle", "").Trim() is { Length: > 0 } custom) name = custom;
        string status = audio ? "Nothing playing" : "No audio";
        if (c.Overlay != null)
        {
            if (DateTime.UtcNow < c.OverlayUntil) status = c.Overlay; else c.Overlay = null;
        }

        var items = new Dictionary<string, JsonObject>
        {
            ["icon"] = new() { ["value"] = art.IconUrl },
            ["name"] = new() { ["value"] = name },
            ["status"] = new() { ["value"] = status },
            ["volumeIcon"] = new() { ["value"] = Icons.Volume(volume, muted), ["opacity"] = level == null ? 0.4 : 1 },
            ["percent"] = new() { ["value"] = level == null ? "—" : muted ? "Mute" : $"{volume}%", ["color"] = level != null && !muted ? Standard : "#8C8C8C" },
            ["progress"] = new() { ["value"] = level == null ? 0 : volume, ["bar_fill_c"] = Standard, ["opacity"] = muted ? 0.3 : level == null ? 0.4 : 0.8 },
        };
        var payload = new JsonObject();
        foreach (var (key, value) in items)
        {
            var json = value.ToJsonString();
            if (c.MediaSent.GetValueOrDefault(key) == json) continue;
            c.MediaSent[key] = json;
            payload[key] = value;
        }
        if (payload.Count > 0) await SendAsync(new JsonObject { ["event"] = "setFeedback", ["context"] = c.Id, ["payload"] = payload });
    }

    /// <summary>
    /// New audio of an App Volume app starts at the level chosen on its dial, also when that level was set while
    /// nothing played. Audio that already ran when the plugin started is left alone.
    /// </summary>
    private void ApplyAppPresets()
    {
        var apps = _contexts.Values.Where(c => c.Action == AppAction).Select(c => Str(c, "app", ""))
            .Where(a => a.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var s in _audio.All)
        {
            if (!apps.Contains(s.Process) || !_appSessionsSeen.Add(s.Id) || !_appSessionsSeeded) continue;
            if (G.AppVolumes.TryGetValue(s.Process, out var level)) AudioSessions.SetVolume(new[] { s }, level);
        }
        _appSessionsSeeded = true;
    }

    /// <summary>An App Volume dial whose app is not running at all.</summary>
    private static bool AppClosed(Ctx c)
    {
        if (c.Action != AppAction) return false;
        var app = Str(c, "app", "");
        if (app.Length == 0) return false;
        var procs = System.Diagnostics.Process.GetProcessesByName(app);
        foreach (var p in procs) p.Dispose();
        return procs.Length == 0;
    }

    private async Task RenderClosed(Ctx c)
    {
        const string layout = "layouts/app-closed.json";
        if (c.Layout != layout)
        {
            c.Layout = layout; c.ArtId = null; c.LastJson = null; c.MediaSent.Clear();
            await SendAsync(new JsonObject { ["event"] = "setFeedbackLayout", ["context"] = c.Id, ["payload"] = new JsonObject { ["layout"] = layout } });
        }
        var app = Str(c, "app", "");
        var exe = _knownAppExe.GetValueOrDefault(app) ?? G.AppExes.GetValueOrDefault(app);
        var art = _art.ForExe("app:" + app.ToLowerInvariant(), exe) ?? _art.NoGame;
        var cover = _art.ClosedCover(art, AppName(app, exe));
        if (c.LastJson == cover) return;
        c.LastJson = cover;
        await SendAsync(new JsonObject { ["event"] = "setFeedback", ["context"] = c.Id, ["payload"] = new JsonObject { ["cover"] = cover } });
    }

    /// <summary>Starts the closed app, like tapping "Open Spotify" on the Spotify dial.</summary>
    private async Task OpenApp(Ctx c)
    {
        var app = Str(c, "app", "");
        var exe = _knownAppExe.GetValueOrDefault(app) ?? G.AppExes.GetValueOrDefault(app) ?? Str(c, "appExe", "");
        if (exe.Length == 0 || !File.Exists(exe)) { await SendAsync(new JsonObject { ["event"] = "showAlert", ["context"] = c.Id }); return; }
        ToggleApp(c);
        _ = Task.Delay(1500).ContinueWith(_ => WithGate(() => _contexts.ContainsKey(c.Id) ? RenderDial(c) : Task.CompletedTask));
    }

    private static string MediaStyle(Ctx c) => Str(c, "mediaStyle", "player") switch
    {
        "background" => "background",
        "compact" => "compact",
        "off" => "off",
        _ => "player",
    };

    private MediaInfo? MediaFor(Ctx c) =>
        c.Action == AppAction && MediaStyle(c) != "off" && Str(c, "app", "") is { Length: > 0 } app ? _media.GetValueOrDefault(app) : null;

    private void ShowVolumeBriefly(Ctx c)
    {
        if (MediaFor(c) == null) return;
        c.VolumeUntil = DateTime.UtcNow + VolumeShown;
        _ = Task.Delay(VolumeShown + TimeSpan.FromMilliseconds(60)).ContinueWith(_ => WithGate(() =>
            _contexts.ContainsKey(c.Id) && DateTime.UtcNow >= c.VolumeUntil ? RenderDial(c) : Task.CompletedTask));
    }

    private static string Clock(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    private async Task RenderMedia(Ctx c, MediaInfo m)
    {
        var style = MediaStyle(c);
        var show = Indicators(c);
        var (appColor, appBadge) = AppIdentity(Str(c, "app", ""));
        var layout = style == "background" && show.Logo && appBadge != null ? "layouts/media-background-logo.json" : $"layouts/media-{style}.json";
        if (c.Layout != layout)
        {
            c.Layout = layout; c.ArtId = null; c.LastJson = null; c.MediaSent.Clear();
            await SendAsync(new JsonObject { ["event"] = "setFeedbackLayout", ["context"] = c.Id, ["payload"] = new JsonObject { ["layout"] = layout } });
        }

        var app = Str(c, "app", "");
        var exe = _knownAppExe.GetValueOrDefault(app) ?? G.AppExes.GetValueOrDefault(app);
        var art = _art.ForExe("app:" + app.ToLowerInvariant(), exe) ?? _art.NoGame;
        var sessions = SessionsFor(c);
        int volume = (int)Math.Round((AudioSessions.Volume(sessions) ?? 0f) * 100f);
        bool muted = sessions.Count > 0 && AudioSessions.Muted(sessions);
        bool seeking = DateTime.UtcNow < c.SeekUntil;
        bool showVolume = !seeking && (muted || DateTime.UtcNow < c.VolumeUntil);
        bool timeline = m.HasTimeline && !showVolume;
        var position = seeking ? c.SeekTarget : m.Position;
        int progress = m.HasTimeline ? (int)Math.Round(100 * position.TotalSeconds / m.Duration.TotalSeconds) : 0;
        double timeOpacity = timeline ? (style == "compact" ? 0.7 : 1) : 0;

        string line = m.Title;
        if (c.Overlay != null)
        {
            if (DateTime.UtcNow < c.OverlayUntil) line = c.Overlay; else c.Overlay = null;
        }
        var lineText = new JsonObject { ["value"] = line };

        var items = new Dictionary<string, JsonObject>
        {
            ["playIcon"] = new() { ["value"] = Icons.Get(m.Playing ? "pause" : "play", show.Play ? appColor : null) },
            ["time"] = new() { ["value"] = m.HasTimeline ? Clock(position) : "", ["opacity"] = timeOpacity },
            ["duration"] = new() { ["value"] = m.HasTimeline ? Clock(m.Duration) : "", ["opacity"] = timeOpacity },
            ["progress"] = new()
            {
                ["value"] = Math.Clamp(progress, 0, 100),
                ["bar_fill_c"] = show.Timeline && appColor != null ? appColor : Standard,
                ["opacity"] = timeline ? (style == "background" || (show.Timeline && appColor != null) ? 1 : 0.8) : 0,
            },
            ["volumeIcon"] = new() { ["value"] = Icons.Volume(volume, muted), ["opacity"] = showVolume ? 1 : 0 },
            ["volumeLevel"] = new() { ["value"] = muted ? "Mute" : $"{volume}%", ["opacity"] = showVolume ? 1 : 0 },
            ["volumeBar"] = new() { ["value"] = volume, ["bar_fill_c"] = muted ? MutedBar : Standard, ["opacity"] = showVolume ? 1 : 0 },
        };
        if (style is "player" or "background")
            items["badge"] = new() { ["value"] = appBadge ?? "images/media/panel.svg", ["opacity"] = show.Logo && appBadge != null ? 1 : 0 };
        if (style is "player" or "background")
        {
            items["prevIcon"] = new() { ["value"] = Icons.Get("previous"), ["opacity"] = m.CanPrevious ? 1 : 0.35 };
            items["nextIcon"] = new() { ["value"] = Icons.Get("next"), ["opacity"] = m.CanNext ? 1 : 0.35 };
        }
        switch (style)
        {
            case "player":
                items["title"] = lineText;
                items["artist"] = new() { ["value"] = m.Artist };
                items["cover"] = new() { ["value"] = m.Cover ?? art.IconUrl };
                break;
            case "background":
                items["title"] = lineText;
                items["artist"] = new() { ["value"] = m.Artist };
                items["backdrop"] = new() { ["value"] = (showVolume ? m.BackdropDim : m.Backdrop) ?? art.BackgroundUrl };
                items["volumePanel"] = new() { ["opacity"] = showVolume ? 1 : 0 }; // calm, dark base under the volume
                break;
            default:
                items["title"] = new() { ["value"] = AppName(app, exe) };
                items["track"] = lineText;
                items["icon"] = new() { ["value"] = art.IconUrl };
                break;
        }

        var payload = new JsonObject();
        foreach (var (key, value) in items)
        {
            var json = value.ToJsonString();
            if (c.MediaSent.GetValueOrDefault(key) == json) continue;
            c.MediaSent[key] = json;
            payload[key] = value;
        }
        if (payload.Count > 0) await SendAsync(new JsonObject { ["event"] = "setFeedback", ["context"] = c.Id, ["payload"] = payload });
    }

    private async Task TickAsync()
    {
        if (DateTime.UtcNow - _libraryLoaded > TimeSpan.FromMinutes(5)) ReloadLibrary();
        _audio.Refresh();
        foreach (var s in _audio.All)
        {
            if (s.Path == null || s.Process.Length == 0) continue;
            _knownAppExe[s.Process] = s.Path;
            if (G.AppExes.GetValueOrDefault(s.Process) != s.Path)
            {
                G.AppExes[s.Process] = s.Path;
                SettingsStore.MarkDirty();
            }
        }

        ApplyAppPresets();

        var found = _detector.FindCurrent(_game);
        if (found == null && _game != null && ++_missedScans < 2) found = _game; // ride out a brief window swap
        else if (found != null) _missedScans = 0;

        bool gameChanged = found?.Entry.Id != _game?.Entry.Id;
        if (gameChanged)
        {
            _appliedSessions.Clear();
            SettingsStore.Log("game: " + (found?.Entry.Name ?? "(none)"));
        }
        _game = found;
        // An open settings panel jumps to the game that just started, so its logo can be set right away.
        if (gameChanged) foreach (var x in _contexts.Values.ToList()) await SendPi(x);

        if (_game != null)
        {
            var sessions = _audio.ForGame(_game).ToList();
            var initial = InitialVolume(_game.Entry.Id);
            foreach (var s in sessions)
                if (_appliedSessions.Add(s.Id) && initial != null)
                    AudioSessions.SetVolume(new[] { s }, initial.Value);

            if (G.LastGameId != _game.Entry.Id || G.LastGameExe != _game.ExePath)
            {
                G.LastGameId = _game.Entry.Id;
                G.LastGameExe = _game.ExePath;
                SettingsStore.MarkDirty();
            }
        }

        UpdateDuck();
        if (SettingsStore.SaveIfDirty())
        {
            // Names or images changed in a settings panel
            _art.Clear();
            foreach (var x in _contexts.Values) { x.ArtId = null; x.KeyHash = null; x.LastJson = null; }
            await _launcher.Redraw();
        }
        await RenderAllAsync();
        await _launcher.Tick(_game, gameChanged);
    }

    private static float? InitialVolume(string gameId)
    {
        if (G.StartOnFavorite && G.Favorites.TryGetValue(gameId, out var favorite)) return favorite;
        return G.RememberVolume && G.GameVolumes.TryGetValue(gameId, out var saved) ? saved : null;
    }

    // ---------- gestures: the physical press and the touch strip behave identically ----------

    private const string Closing = "Closing…";

    private void Schedule(Ctx c, CancellationTokenSource cts, int ms, Func<Task> action) =>
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(ms, cts.Token); } catch (OperationCanceledException) { return; }
            await WithGate(() => cts.IsCancellationRequested || !_contexts.ContainsKey(c.Id) ? Task.CompletedTask : action());
        });

    private void OnDialDown(Ctx c)
    {
        c.RotatedWhilePressed = false;
        c.HoldFired = false;
        c.HoldTimer?.Cancel();
        var hold = Str(c, "holdAction", "close");
        if (hold == "none") return;

        var cts = c.HoldTimer = new CancellationTokenSource();
        if (hold == "close")
            Schedule(c, cts, 300, () =>
            {
                c.Overlay = Closing;
                c.OverlayUntil = DateTime.UtcNow.AddSeconds(2);
                return RenderDial(c);
            });
        Schedule(c, cts, 1000, () =>
        {
            c.HoldTimer = null;
            c.HoldFired = true;
            c.Overlay = null;
            return Perform(c, hold);
        });
    }

    private async Task OnDialUp(Ctx c)
    {
        c.HoldTimer?.Cancel();
        c.HoldTimer = null;
        if (c.Overlay == Closing) { c.Overlay = null; await RenderDial(c); }
        if (!c.RotatedWhilePressed && !c.HoldFired) await OnClick(c);
    }

    private async Task OnClick(Ctx c)
    {
        if (AppClosed(c))
        {
            c.ClickTimer?.Cancel();
            c.ClickTimer = null;
            c.Clicks = 0;
            await OpenApp(c);
            return;
        }
        var actions = new[] { Str(c, "singleAction", "mute"), Str(c, "doubleAction", "window"), Str(c, "tripleAction", TripleDefault(c)) };
        int most = actions[2] != "none" ? 3 : actions[1] != "none" ? 2 : 1;
        c.ClickTimer?.Cancel();
        c.ClickTimer = null;
        c.Clicks = Math.Min(c.Clicks + 1, most);
        if (c.Clicks >= most)
        {
            // Nothing more can follow: act now instead of waiting.
            var n = c.Clicks;
            c.Clicks = 0;
            await Perform(c, actions[n - 1]);
            return;
        }
        var cts = c.ClickTimer = new CancellationTokenSource();
        Schedule(c, cts, 300, () =>
        {
            c.ClickTimer = null;
            var n = c.Clicks;
            c.Clicks = 0;
            return Perform(c, actions[n - 1]);
        });
    }

    private static string TripleDefault(Ctx c) => c.Action == AppAction ? "previous" : "none";

    private Task Perform(Ctx c, string action) => AppClosed(c) ? OpenApp(c) : action switch
    {
        "mute" => ToggleMute(c),
        "window" => ToggleWindow(c),
        "close" => CloseTarget(c),
        "favorite" => GoToFavorite(c),
        "playpause" or "next" or "previous" => RunMedia(c, action),
        _ => Task.CompletedTask,
    };

    // ---------- the game a Game Volume dial controls: its chosen game, or the game in front ----------

    private string FixedId(Ctx c) => c.Action == GameAction ? Str(c, "gameId", "") : "";

    private GameEntry? FixedEntry(Ctx c) => FixedId(c) is { Length: > 0 } id ? _library.Entries.FirstOrDefault(e => e.Id == id) : null;

    private RunningGame? DialGame(Ctx c)
    {
        var id = FixedId(c);
        if (id.Length == 0) return _game;
        return _game?.Entry.Id == id ? _game : _detector.FindById(id);
    }

    private void RememberVolume(Ctx c, float volume)
    {
        if (c.Action != GameAction || !G.RememberVolume || DialGame(c) is not { } g) return;
        G.GameVolumes[g.Entry.Id] = volume;
        SettingsStore.MarkDirty();
    }

    private async Task RunMedia(Ctx c, string action)
    {
        var process = c.Action == AppAction ? Str(c, "app", "") : DialGame(c)?.ExePath is { } exe ? Path.GetFileNameWithoutExtension(exe) : "";
        if (process.Length == 0) return;
        var status = await MediaControl.RunAsync(process, action);
        if (status != null && c.Action == AppAction && MediaStyle(c) != "off")
        {
            // The media screen shows the result itself (play/pause icon, new title): refresh it right away.
            await Task.Delay(250);
            _media[process] = await MediaControl.GetAsync(process);
            if (_media[process] != null) { await RenderDial(c); return; }
        }
        await ShowOverlay(c, status ?? "No media");
    }

    private static void Launch(string target)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception e) { SettingsStore.Log("launch: " + e.Message); }
    }

    private static string? LastGameTarget() => G.LastGameId == null ? null : LaunchTarget(G.LastGameId, G.LastGameExe);

    private static string? LaunchTarget(string id, string? exe)
    {
        if (id.StartsWith("steam:")) return "steam://rungameid/" + id[6..];
        if (id.StartsWith("epic:")) return $"com.epicgames.launcher://apps/{id[5..]}?action=launch&silent=true";
        if (id == GameLibrary.Minecraft.Id) return GameLibrary.Minecraft.IconExe;
        return exe;
    }

    private async Task ToggleWindow(Ctx c)
    {
        if (c.Action == AppAction) { ToggleApp(c); return; }
        if (DialGame(c) is { } game)
        {
            if (WindowControl.IsForeground(game)) WindowControl.Minimize(game.Window);
            else WindowControl.Activate(game.Window);
            return;
        }
        var target = FixedEntry(c) is { } fixedGame ? LaunchTarget(fixedGame.Id, fixedGame.ExactExe ?? GameLibrary.GuessExe(fixedGame)) : LastGameTarget();
        if (target != null) Launch(target);
        else await ShowOverlay(c, "No game yet");
    }

    private Task CloseTarget(Ctx c)
    {
        if (c.Action == GameAction)
        {
            if (DialGame(c) is { } game) WindowControl.CloseWindows(game.Pids);
            return Task.CompletedTask;
        }
        var app = Str(c, "app", "");
        if (app.Length == 0) return Task.CompletedTask;
        if (WindowControl.FindApp(app) is { } window) WindowControl.CloseWindows(window.Pids);

        // Apps such as Discord keep running in the tray once their window is gone.
        _ = Task.Run(async () =>
        {
            await Task.Delay(3000);
            if (WindowControl.FindApp(app) != null) return; // a window remains, for example a save prompt
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(app))
                try { p.Kill(); } catch (Exception e) { SettingsStore.Log("close: " + e.Message); } finally { p.Dispose(); }
        });
        return Task.CompletedTask;
    }

    private string? FavoriteKey(Ctx c)
    {
        if (c.Action == GameAction) return FixedId(c) is { Length: > 0 } id ? id : _game?.Entry.Id;
        var app = Str(c, "app", "");
        return app.Length > 0 ? "app:" + app.ToLowerInvariant() : null;
    }

    private async Task GoToFavorite(Ctx c)
    {
        var key = FavoriteKey(c);
        if (key == null || !G.Favorites.TryGetValue(key, out var favorite)) { await ShowOverlay(c, "No favorite"); return; }
        var sessions = SessionsFor(c);
        if (sessions.Count == 0) return;
        AudioSessions.SetVolume(sessions, favorite);
        RememberVolume(c, favorite);
        await RenderDial(c);
    }

    private void ReloadLibrary()
    {
        _library.Reload();
        _libraryLoaded = DateTime.UtcNow;
    }

    private void UpdateDuck()
    {
        var spotify = _audio.ForProcess("spotify").ToList();
        float factor = Math.Clamp(G.DuckPercent, 0, 100) / 100f;

        if (G.Duck && _game != null)
        {
            if (spotify.Count == 0) return;
            var cur = AudioSessions.Volume(spotify) ?? 1f;
            if (G.DuckRestore == null)
            {
                G.DuckRestore = cur;
                AudioSessions.SetVolume(spotify, cur * factor);
                SettingsStore.MarkDirty();
            }
            else if (Math.Abs(cur - G.DuckRestore.Value * factor) > 0.02f && factor > 0)
            {
                // The user moved Spotify by hand while ducked: restore relative to their choice.
                G.DuckRestore = Math.Clamp(cur / factor, 0f, 1f);
                SettingsStore.MarkDirty();
            }
        }
        else if (G.DuckRestore != null && spotify.Count > 0)
        {
            AudioSessions.SetVolume(spotify, G.DuckRestore.Value);
            G.DuckRestore = null;
            SettingsStore.MarkDirty();
        }
    }

    private async Task HandleAsync(JsonObject m)
    {
        var ev = m["event"]?.GetValue<string>();
        var id = m["context"]?.GetValue<string>();
        var action = m["action"]?.GetValue<string>();
        var payload = m["payload"] as JsonObject;

        if (ev != null && id != null && LauncherKeys.Handles(action))
        {
            await _launcher.Handle(ev, id, action!, payload);
            return;
        }

        if (ev == "willAppear" && id != null && action != null)
        {
            var c = new Ctx
            {
                Id = id, Action = action, Settings = payload?["settings"]?.DeepClone() as JsonObject ?? new(),
                Encoder = payload?["controller"]?.GetValue<string>() == "Encoder",
            };
            _contexts[id] = c;
            await RenderAsync(c);
            return;
        }
        if (ev == "willDisappear" && id != null) { _contexts.Remove(id); return; }

        if (id == null || !_contexts.TryGetValue(id, out var ctx)) return;

        switch (ev)
        {
            case "didReceiveSettings":
                ctx.Settings = payload?["settings"]?.DeepClone() as JsonObject ?? new();
                await RenderAsync(ctx);
                break;
            case "dialRotate":
                await OnRotate(ctx, payload!["ticks"]!.GetValue<int>(), payload["pressed"]?.GetValue<bool>() ?? false);
                break;
            case "dialDown":
                OnDialDown(ctx);
                break;
            case "dialUp":
                await OnDialUp(ctx);
                break;
            case "touchTap":
                if (payload?["hold"]?.GetValue<bool>() == true) await Perform(ctx, Str(ctx, "holdAction", "close"));
                else await OnClick(ctx);
                break;
            case "propertyInspectorDidAppear":
                _piContext = ctx.Id;
                await SendPi(ctx);
                break;
            case "propertyInspectorDidDisappear":
                _piContext = null;
                break;
            case "sendToPlugin":
                await OnPiMessage(ctx, payload ?? new());
                break;
        }
    }

    private static string Str(Ctx c, string key, string fallback) =>
        c.Settings[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : fallback;

    private static int Step(Ctx c) => int.TryParse(Str(c, "step", "2"), out var s) ? s : 2;

    private List<Session> SessionsFor(Ctx c)
    {
        if (c.Action == GameAction) return DialGame(c) is { } game ? _audio.ForGame(game).ToList() : new();
        var app = Str(c, "app", "");
        return app.Length > 0 ? _audio.ForProcess(app).ToList() : new();
    }

    private async Task OnRotate(Ctx c, int ticks, bool pressed)
    {
        if (pressed)
        {
            c.RotatedWhilePressed = true;
            c.HoldTimer?.Cancel();
            c.HoldTimer = null;
            if (c.Overlay == Closing) c.Overlay = null;
        }

        if (pressed && MediaFor(c) is { CanSeek: true, HasTimeline: true } playing)
        {
            await Seek(c, playing, ticks);
            return;
        }

        if (pressed && c.Action == GameAction && G.Balance)
        {
            await OnBalance(c, ticks);
            return;
        }

        var sessions = SessionsFor(c);
        if (sessions.Count == 0)
        {
            // Nothing playing yet: choose the level the app's audio will start at.
            if (c.Action != AppAction || Str(c, "app", "") is not { Length: > 0 } quietApp) return;
            var preset = Math.Clamp(G.AppVolumes.GetValueOrDefault(quietApp, 1f) + ticks * Step(c) / 100f, 0f, 1f);
            G.AppVolumes[quietApp] = MathF.Round(preset * 100f) / 100f;
            SettingsStore.MarkDirty();
            await RenderDial(c);
            return;
        }
        var v = Math.Clamp((AudioSessions.Volume(sessions) ?? 1f) + ticks * Step(c) / 100f, 0f, 1f);
        v = MathF.Round(v * 100f) / 100f;
        AudioSessions.SetVolume(sessions, v);
        if (AudioSessions.Muted(sessions) && ticks > 0) AudioSessions.SetMute(sessions, false);
        RememberVolume(c, v);
        if (c.Action == AppAction && Str(c, "app", "") is { Length: > 0 } turnedApp) { G.AppVolumes[turnedApp] = v; SettingsStore.MarkDirty(); }
        ShowVolumeBriefly(c);
        await RenderDial(c);
    }

    private async Task OnBalance(Ctx c, int ticks)
    {
        var game = SessionsFor(c);
        var discord = _audio.ForProcess("discord").ToList();
        if (game.Count == 0) return;
        if (discord.Count == 0) { await ShowOverlay(c, "No Discord"); return; }

        float delta = ticks * Step(c) / 100f;
        var gv = Math.Clamp((AudioSessions.Volume(game) ?? 1f) + delta, 0f, 1f);
        var dv = Math.Clamp((AudioSessions.Volume(discord) ?? 1f) - delta, 0f, 1f);
        AudioSessions.SetVolume(game, gv);
        AudioSessions.SetVolume(discord, dv);
        RememberVolume(c, gv);
        await ShowOverlay(c, $"G{Math.Round(gv * 100)} · D{Math.Round(dv * 100)}");
    }

    private async Task ShowOverlay(Ctx c, string text)
    {
        c.Overlay = text;
        c.OverlayUntil = DateTime.UtcNow.AddMilliseconds(1500);
        await RenderDial(c);
        _ = Task.Delay(1600).ContinueWith(_ => WithGate(() =>
            _contexts.ContainsKey(c.Id) && DateTime.UtcNow >= c.OverlayUntil ? RenderDial(c) : Task.CompletedTask));
    }

    private async Task ToggleMute(Ctx c)
    {
        var sessions = SessionsFor(c);
        if (sessions.Count == 0) return;
        AudioSessions.SetMute(sessions, !AudioSessions.Muted(sessions));
        ShowVolumeBriefly(c);
        await RenderDial(c);
    }

    private async Task RenderAllAsync()
    {
        foreach (var c in _contexts.Values.ToList()) await RenderAsync(c);
    }

    private Task RenderAsync(Ctx c) => RenderDial(c);

    private string DisplayName(RunningGame g) => DisplayName(g.Entry);

    private static string DisplayName(GameEntry e)
    {
        var custom = G.Overrides.GetValueOrDefault(e.Id)?.Name;
        return string.IsNullOrWhiteSpace(custom) ? e.Name : custom;
    }

    private async Task RenderDial(Ctx c)
    {
        if (!_contexts.ContainsKey(c.Id)) return;
        if (AppClosed(c)) { await RenderClosed(c); return; }
        if (MediaFor(c) is { } media) { await RenderMedia(c, media); return; }
        if (c.Action == AppAction && Str(c, "idleStyle", "player") == "player" && Str(c, "app", "").Length > 0) { await RenderIdle(c); return; }
        string title, value;
        int bar;
        string titleColor = Standard, barColor = Standard;
        Art art;
        bool themed = Str(c, "theme", "standard") == "game";

        if (c.Action == GameAction)
        {
            var game = DialGame(c);
            var fixedGame = FixedEntry(c);
            if (game != null)
            {
                art = _art.ForGame(game);
                title = DisplayName(game);
                (value, bar, barColor) = VolumeText(SessionsFor(c), themed ? art.Accent : Standard);
            }
            else if (fixedGame != null)
            {
                // The chosen game is closed: show it dimmed, so the dial still says which game it belongs to.
                art = _art.ForGame(new RunningGame
                {
                    Entry = fixedGame, Window = IntPtr.Zero, ExePath = fixedGame.ExactExe ?? GameLibrary.GuessExe(fixedGame), Pids = new(), Started = DateTime.Now,
                });
                (title, value, bar, titleColor) = (DisplayName(fixedGame), "", 0, Dim);
            }
            else
            {
                (title, value, bar, titleColor, art) = ("No game", "", 0, Dim, _art.NoGame);
            }
        }
        else
        {
            var app = Str(c, "app", "");
            var sessions = SessionsFor(c);
            var exe = _knownAppExe.GetValueOrDefault(app) ?? G.AppExes.GetValueOrDefault(app);
            art = (app.Length > 0 ? _art.ForExe("app:" + app.ToLowerInvariant(), exe) : null) ?? _art.NoGame;
            if (app.Length == 0) (title, value, bar, titleColor) = ("Choose an app", "", 0, Dim);
            else
            {
                title = AppName(app, exe);
                if (sessions.Count == 0) { (value, bar) = ("", 0); titleColor = Dim; }
                else (value, bar, barColor) = VolumeText(sessions, themed ? art.Accent : Standard);
            }
        }

        // "No game" and "Choose an app" are status messages, so a custom title never hides them.
        bool hasTarget = c.Action == GameAction ? DialGame(c) != null || FixedEntry(c) != null : Str(c, "app", "").Length > 0;
        if (hasTarget && Str(c, "titleMode", "app") == "custom")
        {
            var custom = Str(c, "customTitle", "").Trim();
            if (custom.Length > 0) title = custom;
        }

        if (c.Overlay != null)
        {
            if (DateTime.UtcNow < c.OverlayUntil) value = c.Overlay; else c.Overlay = null;
        }

        var background = Str(c, "display", "icon") == "background";
        var layout = background ? "layouts/background.json" : "layouts/icon.json";
        if (c.Layout != layout)
        {
            c.Layout = layout; c.ArtId = null; c.LastJson = null;
            await SendAsync(new JsonObject { ["event"] = "setFeedbackLayout", ["context"] = c.Id, ["payload"] = new JsonObject { ["layout"] = layout } });
        }

        var payload = new JsonObject
        {
            ["title"] = new JsonObject { ["value"] = title, ["color"] = titleColor },
            ["value"] = new JsonObject { ["value"] = value, ["color"] = Standard },
            ["indicator"] = new JsonObject { ["value"] = bar, ["bar_fill_c"] = barColor },
        };
        var state = payload.ToJsonString() + art.Id;
        if (state == c.LastJson) return;
        c.LastJson = state;
        if (c.ArtId != art.Id)
        {
            c.ArtId = art.Id;
            if (background) payload["background"] = art.BackgroundUrl;
            else payload["icon"] = art.IconUrl;
        }
        await SendAsync(new JsonObject { ["event"] = "setFeedback", ["context"] = c.Id, ["payload"] = payload });
    }

    private static (string, int, string) VolumeText(List<Session> sessions, string color)
    {
        if (sessions.Count == 0) return ("—", 0, color);
        int pct = (int)Math.Round((AudioSessions.Volume(sessions) ?? 0f) * 100f);
        return AudioSessions.Muted(sessions) ? ("Mute", pct, MutedBar) : ($"{pct}%", pct, color);
    }

    private static string AppName(string process, string? exe)
    {
        try
        {
            if (exe != null)
            {
                var d = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileDescription;
                if (!string.IsNullOrWhiteSpace(d) && d.Length <= 18)
                {
                    d = d.Trim();
                    return d.StartsWith("Google ", StringComparison.OrdinalIgnoreCase) ? d[7..] : d;
                }
            }
        }
        catch { }
        return process.Length > 0 ? char.ToUpperInvariant(process[0]) + process[1..] : process;
    }

    private async Task OnPiMessage(Ctx c, JsonObject p)
    {
        if (p["iconLib"] != null)
        {
            await SendAsync(new JsonObject
            {
                ["event"] = "sendToPropertyInspector", ["context"] = c.Id, ["action"] = c.Action,
                ["payload"] = new JsonObject { ["iconLibrary"] = ArtworkService.IconLibrary() },
            });
            return;
        }
        bool perAction = false, library = false, artChanged = false;
        var editId = p["editId"]?.GetValue<string>() ?? _game?.Entry.Id;
        foreach (var (key, node) in p.ToList())
        {
            if (node is not JsonValue v) continue;
            switch (key)
            {
                case "display" or "theme" or "step" or "app" or "gameId" or "mediaStyle" or "idleStyle" or "accentPlay" or "accentTimeline" or "accentLogo" or "titleMode" or "customTitle" or "singleAction" or "doubleAction" or "tripleAction" or "holdAction":
                    c.Settings[key] = node.DeepClone();
                    perAction = true;
                    break;
                case "piLog": SettingsStore.Log("pi file: " + v); break;
                case "startFavorite": G.StartOnFavorite = v.GetValue<bool>(); break;
                case "favorite":
                    if (FavoriteKey(c) is { } favKey)
                    {
                        if (int.TryParse(v.ToString(), out int percent)) G.Favorites[favKey] = Math.Clamp(percent, 0, 100) / 100f;
                        else G.Favorites.Remove(favKey);
                    }
                    break;
                case "favoriteCurrent":
                    if (FavoriteKey(c) is { } curKey && AudioSessions.Volume(SessionsFor(c)) is { } now) G.Favorites[curKey] = now;
                    break;
                case "rememberVolume": G.RememberVolume = v.GetValue<bool>(); break;
                case "balance": G.Balance = v.GetValue<bool>(); break;
                case "duck": G.Duck = v.GetValue<bool>(); break;
                case "duckPercent": G.DuckPercent = int.Parse(v.ToString()); break;
                case "customName" when editId != null:
                    Override(editId).Name = v.GetValue<string>();
                    break;
                case "customImage" when editId != null:
                    var image = v.GetValue<string>();
                    Override(editId).Image = image.Length > 0 ? SettingsStore.KeepLogo(editId, image) : null;
                    _art.Invalidate(editId);
                    artChanged = true;
                    break;
                case "hidden" when editId != null:
                    if (v.GetValue<bool>()) G.HiddenGames.Add(editId); else G.HiddenGames.Remove(editId);
                    break;
                case "removeGame":
                    G.CustomGames.RemoveAll(g => g.Exe.Equals(v.GetValue<string>(), StringComparison.OrdinalIgnoreCase));
                    library = true;
                    break;
            }
        }
        if (p["addExe"] is JsonValue exe && p["addName"] is JsonValue name)
        {
            var e = exe.GetValue<string>();
            var n = name.GetValue<string>().Trim();
            if (e.Length > 0 && n.Length > 0)
            {
                G.CustomGames.RemoveAll(g => g.Exe.Equals(e, StringComparison.OrdinalIgnoreCase));
                G.CustomGames.Add(new CustomGame { Exe = e, Name = n });
                library = true;
            }
        }

        if (library) ReloadLibrary();
        if (p.Count > 0) SettingsStore.MarkDirty();
        if (perAction)
            await SendAsync(new JsonObject { ["event"] = "setSettings", ["context"] = c.Id, ["payload"] = c.Settings.DeepClone() });

        if (artChanged) foreach (var x in _contexts.Values) { x.ArtId = null; x.KeyHash = null; x.LastJson = null; }
        c.LastJson = null; c.KeyHash = null;
        UpdateDuck();
        await RenderAllAsync();
        await SendPi(c);
    }

    private GameOverride Override(string id)
    {
        if (!G.Overrides.TryGetValue(id, out var o)) G.Overrides[id] = o = new GameOverride();
        SettingsStore.TouchOverride(id);
        return o;
    }

    private async Task SendPi(Ctx c)
    {
        var apps = new JsonArray();
        var appNames = new SortedSet<string>(_knownAppExe.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var a in appNames) apps.Add(a);
        var library = new JsonArray();
        foreach (var e in _library.Entries.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var o = G.Overrides.GetValueOrDefault(e.Id);
            library.Add(new JsonObject
            {
                ["id"] = e.Id, ["name"] = e.Name,
                ["customName"] = o?.Name ?? "", ["customImage"] = o?.Image ?? "",
                ["hidden"] = G.HiddenGames.Contains(e.Id),
                ["exe"] = e.ExactExe,
            });
        }

        await SendAsync(new JsonObject
        {
            ["event"] = "sendToPropertyInspector", ["context"] = c.Id, ["action"] = c.Action,
            ["payload"] = new JsonObject
            {
                ["display"] = Str(c, "display", "icon"),
                ["mediaStyle"] = MediaStyle(c),
                ["idleStyle"] = Str(c, "idleStyle", "player"),
                ["accentPlay"] = Indicators(c).Play,
                ["accentTimeline"] = Indicators(c).Timeline,
                ["accentLogo"] = Indicators(c).Logo,
                ["theme"] = Str(c, "theme", "standard"),
                ["step"] = Str(c, "step", "2"),
                ["singleAction"] = Str(c, "singleAction", "mute"),
                ["doubleAction"] = Str(c, "doubleAction", "window"),
                ["tripleAction"] = Str(c, "tripleAction", TripleDefault(c)),
                ["holdAction"] = Str(c, "holdAction", "close"),
                ["startFavorite"] = G.StartOnFavorite,
                ["favorite"] = FavoriteKey(c) is { } fav && G.Favorites.TryGetValue(fav, out var favValue) ? (int)Math.Round(favValue * 100) : null,
                ["gameId"] = Str(c, "gameId", ""),
                ["favoriteFor"] = c.Action == GameAction ? (FixedEntry(c) is { } fixedGame ? DisplayName(fixedGame) : _game != null ? DisplayName(_game) : null) : (Str(c, "app", "") is { Length: > 0 } dialApp ? AppName(dialApp, _knownAppExe.GetValueOrDefault(dialApp) ?? G.AppExes.GetValueOrDefault(dialApp)) : null),
                ["titleMode"] = Str(c, "titleMode", "app"),
                ["customTitle"] = Str(c, "customTitle", ""),
                ["app"] = Str(c, "app", ""),
                ["rememberVolume"] = G.RememberVolume,
                ["balance"] = G.Balance,
                ["duck"] = G.Duck,
                ["duckPercent"] = G.DuckPercent,
                ["currentId"] = _game?.Entry.Id,
                ["apps"] = apps,
                ["library"] = library,
            },
        });
    }
}
