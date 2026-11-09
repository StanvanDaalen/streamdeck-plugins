using System.Drawing;
using System.Drawing.Imaging;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ScarlettSwitch.Core;

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
    public DateTime OverlayUntil;
}

internal sealed class Plugin
{
    private const string GameAction = "com.stan.gamevolume.game";
    private const string AppAction = "com.stan.gamevolume.app";
    private const string OutputAction = "com.stan.gamevolume.output";
    private const string SpeakersAction = "com.stan.gamevolume.speakers";
    private const string HeadphonesAction = "com.stan.gamevolume.headphones";
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

    private RunningGame? _game;
    private int _missedScans;
    private readonly HashSet<string> _appliedSessions = new();
    private DateTime _libraryLoaded = DateTime.MinValue;
    private readonly Dictionary<string, string> _knownAppExe = new(StringComparer.OrdinalIgnoreCase);

    // Scarlett is only started once one of its actions is placed.
    private Settings? _scarlett;
    private OutputController? _outputs;
    private EndpointVolume? _masterVolume;
    private string? _piContext;
    private LinkStatus? _loggedStatus;

    private static GlobalSettings G => SettingsStore.Current;

    private static bool IsOutput(string? action) => action is OutputAction or SpeakersAction or HeadphonesAction;

    private void EnsureScarlett()
    {
        if (_outputs != null) return;
        _scarlett = Settings.Load();
        _masterVolume = new EndpointVolume();
        _masterVolume.Changed += () => _ = WithGate(RenderOutputs);
        _outputs = new OutputController(_scarlett);
        _outputs.Changed += () => _ = WithGate(OnOutputChanged);
    }

    private async Task OnOutputChanged()
    {
        if (_outputs!.Status != _loggedStatus)
        {
            _loggedStatus = _outputs.Status;
            SettingsStore.Log("scarlett: " + _outputs.Status);
        }
        await RenderOutputs();
        if (_piContext != null && _contexts.TryGetValue(_piContext, out var c) && IsOutput(c.Action)) await SendPi(c);
    }

    private async Task RenderOutputs()
    {
        foreach (var c in _contexts.Values.Where(x => IsOutput(x.Action)).ToList()) await RenderAsync(c);
    }

    private async Task PressOutput(string action, string context)
    {
        switch (action)
        {
            case SpeakersAction: _outputs!.Set(Output.Speakers); break;
            case HeadphonesAction: _outputs!.Set(Output.Headphones); break;
            default: _outputs!.Toggle(); break;
        }
        if (_outputs.Status != LinkStatus.Ready)
            await SendAsync(new JsonObject { ["event"] = "showAlert", ["context"] = context });
    }

    private void ApplyScarlettSettings(JsonObject p)
    {
        var s = _scarlett!;
        bool any = p["reapply"] != null;
        if (p["speakerMix"] is { } a) { s.SpeakerMix = (int)a; any = true; }
        if (p["headphoneMix"] is { } b) { s.HeadphoneMix = (int)b; any = true; }
        if (p["volumeStep"] is { } v) { s.VolumeStep = Math.Clamp((int)v, 1, 10); any = true; }
        if (p["focusriteMode"] is { } f) { s.FocusriteMode = (FocusriteMode)Math.Clamp((int)f, 0, 2); any = true; }
        if (p["midiPort"] is { } m && !string.IsNullOrWhiteSpace((string?)m)) { s.MidiPort = ((string)m!).Trim(); any = true; }
        if (!any) return;
        s.Save();
        _outputs!.Reapply();
    }

    private static string StatusText(LinkStatus s) => s switch
    {
        LinkStatus.Ready => "Verbonden",
        LinkStatus.NoDevice => "Geen Scarlett gevonden",
        LinkStatus.UnsupportedDevice => "Deze Scarlett-generatie wordt niet door Focusrite Control 2 ondersteund",
        LinkStatus.NoFocusriteControl => FocusriteLauncher.Installed
            ? "Focusrite Control 2 draait niet (automatisch starten staat uit)"
            : "Focusrite Control 2 is niet geïnstalleerd",
        LinkStatus.NoMidiPort => "MIDI-loopbackpoort niet gevonden",
        _ => "MIDI-fout",
    };

    private Art OutputArt(bool headphones, bool on)
    {
        var id = $"output:{(headphones ? "hp" : "sp")}:{(on ? "on" : "off")}";
        var color = on ? Color.White : ColorTranslator.FromHtml(Dim);
        return _art.ForGlyph(id, () => headphones ? Glyphs.Headphones(96, color) : Glyphs.Speakers(96, color));
    }

    private static string? OutputIconFile(Ctx c, bool headphones)
    {
        var path = Str(c, headphones ? "iconHeadphones" : "iconSpeakers", "");
        return path.Length > 0 && File.Exists(path) ? path : null;
    }

    private async Task RenderOutputKey(Ctx c)
    {
        bool ready = _outputs!.Status == LinkStatus.Ready;
        bool hp = _outputs.Output == Output.Headphones;
        int state = c.Action == SpeakersAction ? (hp ? 0 : 1) : (hp ? 1 : 0);
        var custom = c.Action == OutputAction ? OutputIconFile(c, hp) : null;
        var hash = $"{ready}|{state}|{custom}";
        if (hash == c.KeyHash) return;
        c.KeyHash = hash;

        await SendAsync(new JsonObject { ["event"] = "setState", ["context"] = c.Id, ["payload"] = new JsonObject { ["state"] = state } });

        // Without an image Stream Deck shows the state's own icon, including one the user picked in its editor.
        var image = !ready ? OfflineKey(c.Action switch { SpeakersAction => false, HeadphonesAction => true, _ => hp })
            : custom != null ? ArtworkService.FileImage(custom) : null;
        var payload = new JsonObject { ["target"] = 0 };
        if (image != null) payload["image"] = image;
        await SendAsync(new JsonObject { ["event"] = "setImage", ["context"] = c.Id, ["payload"] = payload });
    }

    private string OfflineKey(bool headphones)
    {
        using var bmp = new Bitmap(144, 144, PixelFormat.Format32bppArgb);
        using (var g = ArtworkService.HighQuality(bmp))
        {
            g.Clear(Color.Black);
            g.DrawImage(OutputArt(headphones, false).KeyIcon, 18, 18, 108, 108);
        }
        return ArtworkService.DataUrl(bmp);
    }

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
    }

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

        var found = _detector.FindCurrent(_game);
        if (found == null && _game != null && ++_missedScans < 2) found = _game; // ride out a brief window swap
        else if (found != null) _missedScans = 0;

        if (found?.Entry.Id != _game?.Entry.Id)
        {
            _appliedSessions.Clear();
            SettingsStore.Log("game: " + (found?.Entry.Name ?? "(none)"));
        }
        _game = found;

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
        SettingsStore.SaveIfDirty();
        await RenderAllAsync();
    }

    private static float? InitialVolume(string gameId)
    {
        if (G.StartOnFavorite && G.Favorites.TryGetValue(gameId, out var favorite)) return favorite;
        return G.RememberVolume && G.GameVolumes.TryGetValue(gameId, out var saved) ? saved : null;
    }

    // ---------- gestures: the physical press and the touch strip behave identically ----------

    private const string Closing = "Afsluiten…";

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
        var single = Str(c, "singleAction", "mute");
        var twice = Str(c, "doubleAction", "window");
        if (twice == "none") { await Perform(c, single); return; }

        if (c.ClickTimer != null)
        {
            c.ClickTimer.Cancel();
            c.ClickTimer = null;
            await Perform(c, twice);
            return;
        }
        var cts = c.ClickTimer = new CancellationTokenSource();
        Schedule(c, cts, 300, () =>
        {
            c.ClickTimer = null;
            return Perform(c, single);
        });
    }

    private Task Perform(Ctx c, string action) => action switch
    {
        "mute" => ToggleMute(c),
        "window" => ToggleWindow(c),
        "close" => CloseTarget(c),
        "favorite" => GoToFavorite(c),
        _ => Task.CompletedTask,
    };

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
        if (_game != null)
        {
            if (WindowControl.IsForeground(_game)) WindowControl.Minimize(_game.Window);
            else WindowControl.Activate(_game.Window);
            return;
        }
        if (LastGameTarget() is { } target) Launch(target);
        else await ShowOverlay(c, "Nog geen game");
    }

    private Task CloseTarget(Ctx c)
    {
        if (c.Action == GameAction)
        {
            if (_game != null) WindowControl.CloseWindows(_game.Pids);
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
        if (c.Action == GameAction) return _game?.Entry.Id;
        var app = Str(c, "app", "");
        return app.Length > 0 ? "app:" + app.ToLowerInvariant() : null;
    }

    private async Task GoToFavorite(Ctx c)
    {
        var key = FavoriteKey(c);
        if (key == null || !G.Favorites.TryGetValue(key, out var favorite)) { await ShowOverlay(c, "Geen favoriet"); return; }
        var sessions = SessionsFor(c);
        if (sessions.Count == 0) return;
        AudioSessions.SetVolume(sessions, favorite);
        if (c.Action == GameAction && _game != null && G.RememberVolume)
        {
            G.GameVolumes[_game.Entry.Id] = favorite;
            SettingsStore.MarkDirty();
        }
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

        if (ev == "willAppear" && id != null && action != null)
        {
            var c = new Ctx
            {
                Id = id, Action = action, Settings = payload?["settings"]?.DeepClone() as JsonObject ?? new(),
                Encoder = payload?["controller"]?.GetValue<string>() == "Encoder",
            };
            _contexts[id] = c;
            if (IsOutput(action))
            {
                EnsureScarlett();
                if (c.Encoder) _masterVolume!.Bind();
            }
            await RenderAsync(c);
            return;
        }
        if (ev == "willDisappear" && id != null) { _contexts.Remove(id); return; }

        // Keys inside a Multi Action fire keyDown without ever sending willAppear.
        if (ev == "keyDown" && id != null && IsOutput(action))
        {
            EnsureScarlett();
            await PressOutput(action!, id);
            return;
        }
        if (id == null || !_contexts.TryGetValue(id, out var ctx)) return;

        if (IsOutput(ctx.Action) && ev is "dialDown" or "touchTap" or "dialRotate")
        {
            if (ev == "dialRotate")
            {
                _masterVolume!.Adjust(payload!["ticks"]!.GetValue<int>() * _scarlett!.VolumeStep);
                await RenderDial(ctx);
            }
            else if (ev == "touchTap" && payload?["hold"]?.GetValue<bool>() == true) _masterVolume!.ToggleMute();
            else await PressOutput(ctx.Action, id);
            return;
        }

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
                if (IsOutput(ctx.Action)) ApplyScarlettSettings(payload ?? new());
                await OnPiMessage(ctx, payload ?? new());
                break;
        }
    }

    private static string Str(Ctx c, string key, string fallback) =>
        c.Settings[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : fallback;

    private static int Step(Ctx c) => int.TryParse(Str(c, "step", "2"), out var s) ? s : 2;

    private List<Session> SessionsFor(Ctx c)
    {
        if (c.Action == GameAction) return _game != null ? _audio.ForGame(_game).ToList() : new();
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

        if (pressed && c.Action == GameAction && G.Balance)
        {
            await OnBalance(c, ticks);
            return;
        }

        var sessions = SessionsFor(c);
        if (sessions.Count == 0) return;
        var v = Math.Clamp((AudioSessions.Volume(sessions) ?? 1f) + ticks * Step(c) / 100f, 0f, 1f);
        v = MathF.Round(v * 100f) / 100f;
        AudioSessions.SetVolume(sessions, v);
        if (AudioSessions.Muted(sessions) && ticks > 0) AudioSessions.SetMute(sessions, false);
        if (c.Action == GameAction && _game != null && G.RememberVolume)
        {
            G.GameVolumes[_game.Entry.Id] = v;
            SettingsStore.MarkDirty();
        }
        await RenderDial(c);
    }

    private async Task OnBalance(Ctx c, int ticks)
    {
        var game = SessionsFor(c);
        var discord = _audio.ForProcess("discord").ToList();
        if (game.Count == 0) return;
        if (discord.Count == 0) { await ShowOverlay(c, "Geen Discord"); return; }

        float delta = ticks * Step(c) / 100f;
        var gv = Math.Clamp((AudioSessions.Volume(game) ?? 1f) + delta, 0f, 1f);
        var dv = Math.Clamp((AudioSessions.Volume(discord) ?? 1f) - delta, 0f, 1f);
        AudioSessions.SetVolume(game, gv);
        AudioSessions.SetVolume(discord, dv);
        if (_game != null && G.RememberVolume) { G.GameVolumes[_game.Entry.Id] = gv; SettingsStore.MarkDirty(); }
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
        await RenderDial(c);
    }

    private async Task RenderAllAsync()
    {
        foreach (var c in _contexts.Values.ToList()) await RenderAsync(c);
    }

    private Task RenderAsync(Ctx c) => IsOutput(c.Action) && !c.Encoder ? RenderOutputKey(c) : RenderDial(c);

    private string DisplayName(RunningGame g) => DisplayName(g.Entry);

    private static string DisplayName(GameEntry e)
    {
        var custom = G.Overrides.GetValueOrDefault(e.Id)?.Name;
        return string.IsNullOrWhiteSpace(custom) ? e.Name : custom;
    }

    private async Task RenderDial(Ctx c)
    {
        string title, value;
        int bar;
        string titleColor = Standard, barColor = Standard;
        Art art;
        bool themed = Str(c, "theme", "standard") == "game";

        bool output = IsOutput(c.Action);
        if (output)
        {
            bool ready = _outputs!.Status == LinkStatus.Ready;
            bool hp = _outputs.Output == Output.Headphones;
            int vol = _masterVolume!.Percent;
            bool muted = _masterVolume.Muted;
            title = ready ? (hp ? "Headphones" : "Speakers") : "Offline";
            titleColor = ready ? Standard : Dim;
            value = vol < 0 ? "" : muted ? "Mute" : $"{vol}%";
            bar = Math.Max(vol, 0);
            barColor = muted ? MutedBar : Standard;
            var iconFile = ready ? OutputIconFile(c, hp) : null;
            art = (iconFile != null ? _art.ForFile(iconFile) : null) ?? OutputArt(hp, ready);
        }
        else if (c.Action == GameAction)
        {
            if (_game == null)
            {
                (title, value, bar, titleColor, art) = ("Geen game", "", 0, Dim, _art.NoGame);
            }
            else
            {
                art = _art.ForGame(_game);
                title = DisplayName(_game);
                (value, bar, barColor) = VolumeText(SessionsFor(c), themed ? art.Accent : Standard);
            }
        }
        else
        {
            var app = Str(c, "app", "");
            var sessions = SessionsFor(c);
            var exe = _knownAppExe.GetValueOrDefault(app) ?? G.AppExes.GetValueOrDefault(app);
            art = (app.Length > 0 ? _art.ForExe("app:" + app.ToLowerInvariant(), exe) : null) ?? _art.NoGame;
            if (app.Length == 0) (title, value, bar, titleColor) = ("Kies een app", "", 0, Dim);
            else
            {
                title = AppName(app, exe);
                if (sessions.Count == 0) { (value, bar) = ("", 0); titleColor = Dim; }
                else (value, bar, barColor) = VolumeText(sessions, themed ? art.Accent : Standard);
            }
        }

        // "Geen game" and "Kies een app" are status messages, so a custom title never hides them.
        bool hasTarget = output ? _outputs!.Status == LinkStatus.Ready
            : c.Action == GameAction ? _game != null : Str(c, "app", "").Length > 0;
        if (hasTarget && Str(c, "titleMode", "app") == "custom")
        {
            var custom = Str(c, "customTitle", "").Trim();
            if (custom.Length > 0) title = custom;
        }

        if (c.Overlay != null)
        {
            if (DateTime.UtcNow < c.OverlayUntil) value = c.Overlay; else c.Overlay = null;
        }

        var background = !output && Str(c, "display", "icon") == "background";
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
                case "display" or "theme" or "step" or "app" or "titleMode" or "customTitle" or "iconSpeakers" or "iconHeadphones" or "singleAction" or "doubleAction" or "holdAction":
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
                    Override(editId).Image = v.GetValue<string>();
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

    private JsonObject ScarlettPi()
    {
        var d = _outputs!.Device;
        var ports = new JsonArray();
        foreach (var n in MidiOut.GetPortNames()) ports.Add(n);
        return new JsonObject
        {
            ["status"] = StatusText(_outputs.Status),
            ["ready"] = _outputs.Status == LinkStatus.Ready,
            ["device"] = d == null ? "—" : $"{d.Name} (generatie {d.Generation})",
            ["port"] = _outputs.PortName ?? "—",
            ["ports"] = ports,
            ["speakerMix"] = _scarlett!.SpeakerMix,
            ["headphoneMix"] = _scarlett.HeadphoneMix,
            ["volumeStep"] = _scarlett.VolumeStep,
            ["focusriteMode"] = (int)_scarlett.FocusriteMode,
            ["midiPort"] = _scarlett.MidiPort,
        };
    }

    private GameOverride Override(string id)
    {
        if (!G.Overrides.TryGetValue(id, out var o)) G.Overrides[id] = o = new GameOverride();
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
                ["theme"] = Str(c, "theme", "standard"),
                ["step"] = Str(c, "step", "2"),
                ["singleAction"] = Str(c, "singleAction", "mute"),
                ["doubleAction"] = Str(c, "doubleAction", "window"),
                ["holdAction"] = Str(c, "holdAction", "close"),
                ["startFavorite"] = G.StartOnFavorite,
                ["favorite"] = FavoriteKey(c) is { } fav && G.Favorites.TryGetValue(fav, out var favValue) ? (int)Math.Round(favValue * 100) : null,
                ["favoriteFor"] = c.Action == GameAction ? (_game != null ? DisplayName(_game) : null) : (Str(c, "app", "") is { Length: > 0 } dialApp ? AppName(dialApp, _knownAppExe.GetValueOrDefault(dialApp) ?? G.AppExes.GetValueOrDefault(dialApp)) : null),
                ["iconSpeakers"] = Str(c, "iconSpeakers", ""),
                ["iconHeadphones"] = Str(c, "iconHeadphones", ""),
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
                ["scarlett"] = IsOutput(c.Action) && _outputs != null ? ScarlettPi() : null,
            },
        });
    }
}
