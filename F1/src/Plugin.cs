using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace F1;

internal sealed class Ctx
{
    public required string Id { get; init; }
    public required string Action { get; init; }
    public bool Encoder;
    public string? Hash;
    public CancellationTokenSource? Hold;
    public bool HoldFired;
}

internal sealed class Plugin
{
    private const string P = "com.stan.f1.";
    private const string Mode = P + "mode", Stop = P + "stop", Flags = P + "flags", Zone = P + "zone",
        Volume = P + "volume", Latency = P + "latency", Live = P + "live", Sync = P + "sync",
        Pause = P + "pause", Race = P + "race", Quali = P + "quali", Fullscreen = P + "fullscreen";

    private readonly int _port;
    private readonly string _uuid, _register;
    private readonly ClientWebSocket _ws = new();
    private readonly SemaphoreSlim _gate = new(1, 1), _sendLock = new(1, 1);
    private readonly Dictionary<string, Ctx> _contexts = new();
    private readonly FlagEngine _flags;
    private readonly Channel<JsonObject> _inbox = Channel.CreateUnbounded<JsonObject>();
    private bool _pairing, _busy;
    /// <summary>True while Close F1 runs: no new flags on the lights it just restored.</summary>
    private bool _closing;
    private string? _latencyPlayer;

    private MvState _mv = new();
    private List<string> _zones = new();
    /// <summary>Seconds of delay this plugin added on top of live (dial + pauses).</summary>
    private double _latency;
    private DateTime? _pausedAt;
    private string? _pairStatus;

    private static F1Settings S => Store.Current;

    public Plugin(int port, string uuid, string register)
    {
        _port = port; _uuid = uuid; _register = register;
        _flags = new FlagEngine(WithGate);
    }

    // ------------------------------------------------------------ plumbing

    public async Task RunAsync()
    {
        await _ws.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}"), CancellationToken.None);
        await SendAsync(new JsonObject { ["event"] = _register, ["uuid"] = _uuid });
        _ = Task.Run(PollLoop);
        _ = Task.Run(HandleLoop);

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
            _inbox.Writer.TryWrite(JsonNode.Parse(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length))!.AsObject());
        }
    }

    /// <summary>Handles events one by one and in order, off the receive loop.</summary>
    private async Task HandleLoop()
    {
        await foreach (var msg in _inbox.Reader.ReadAllAsync())
            await WithGate(() => HandleAsync(msg));
    }

    private Task WithGate(Func<Task> f) => WithGate(f, CancellationToken.None);

    /// <summary>Runs f under the gate; a cancelled token gives up while still waiting (throws OperationCanceledException).</summary>
    private async Task WithGate(Func<Task> f, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try { await f(); }
        catch (Exception e) { Store.Log("error: " + e); }
        finally { _gate.Release(); }
    }

    /// <summary>Long jobs (apps, desktops, layouts) run outside the gate so dials and flags keep working.</summary>
    private void Background(Ctx c, string what, Func<Task<bool>> job)
    {
        if (_busy) { _ = Alert(c); return; }
        _busy = true;
        _ = Task.Run(async () =>
        {
            bool ok = false;
            try { ok = await job(); }
            catch (Exception e) { Store.Log($"{what}: {e}"); }
            finally { _busy = false; }
            await (ok ? Ok(c) : Alert(c));
        });
    }

    private async Task SendAsync(JsonObject msg)
    {
        var bytes = Encoding.UTF8.GetBytes(msg.ToJsonString());
        await _sendLock.WaitAsync();
        try { if (_ws.State == WebSocketState.Open) await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
        finally { _sendLock.Release(); }
    }

    private Task Alert(Ctx c) => SendAsync(new JsonObject { ["event"] = "showAlert", ["context"] = c.Id });
    private Task Ok(Ctx c) => SendAsync(new JsonObject { ["event"] = "showOk", ["context"] = c.Id });

    private async Task PollLoop()
    {
        while (_ws.State == WebSocketState.Open)
        {
            var state = await MultiViewer.StateAsync();
            await WithGate(async () =>
            {
                bool wasOnline = _mv.Online;
                _mv = state;
                if (!state.Online)
                {
                    if (wasOnline) { _latency = 0; _pausedAt = null; _latencyPlayer = null; }
                    if (_flags.Active) await _flags.BackToNormalAsync();
                }
                else if (!_closing) await _flags.UpdateAsync(state);
                TrackNewStream(state.Main);
                await RenderAllAsync();
            });
            await Task.Delay(state.Online ? 500 : 2000);
        }
    }

    // ------------------------------------------------------------ events

    private async Task HandleAsync(JsonObject m)
    {
        var ev = (string?)m["event"];
        var ctxId = (string?)m["context"];
        var action = (string?)m["action"];
        var payload = m["payload"] as JsonObject;

        if (ev == "willAppear" && ctxId != null && action != null)
        {
            _contexts[ctxId] = new Ctx { Id = ctxId, Action = action, Encoder = (string?)payload?["controller"] == "Encoder" };
            if (action == Zone && _zones.Count == 0) await LoadZonesAsync();
            await RenderAsync(_contexts[ctxId]);
            return;
        }
        if (ev == "willDisappear" && ctxId != null)
        {
            // A Race/Quali key that leaves the screen while held must not save two seconds later.
            if (_contexts.Remove(ctxId, out var gone)) { gone.Hold?.Cancel(); gone.Hold = null; }
            return;
        }
        if (ctxId == null || !_contexts.TryGetValue(ctxId, out var c)) return;

        switch (ev)
        {
            case "keyDown": await KeyDown(c); break;
            case "keyUp": await KeyUp(c); break;
            case "dialRotate": await Rotate(c, (int?)payload?["ticks"] ?? 0); break;
            case "dialDown": await Push(c); break;
            case "touchTap": await Touch(c); break;
            case "propertyInspectorDidAppear": await SendPi(c); break;
            case "sendToPlugin": await FromPi(c, payload ?? new()); break;
        }
        await RenderAllAsync();
    }

    private async Task KeyDown(Ctx c)
    {
        if (c.Action is Race or Quali)
        {
            // Hold 2 s = save the layout; a short press opens it (on key up).
            c.HoldFired = false;
            c.Hold?.Cancel();
            var cts = c.Hold = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(2000, cts.Token); }
                catch (OperationCanceledException) { return; }
                // Key-up cancels under the gate before opening, so a release right at 2 s never does both.
                await WithGate(async () =>
                {
                    if (cts.IsCancellationRequested) return;
                    c.HoldFired = true;
                    Background(c, "layout opslaan", () => SaveLayoutAsync(LayoutKind(c)));
                    await Task.CompletedTask;
                });
            });
            return;
        }

        switch (c.Action)
        {
            case Mode: Background(c, "F1 Mode", StartF1Async); break;
            case Stop: Background(c, "Close F1", CloseF1Async); break;
            case Flags: await ToggleFlags(); break;
            case Live: await GoLive(c); break;
            case Sync:
                if (_mv.Main is { } main && await MultiViewer.SyncAsync(main.Id)) await Ok(c); else await Alert(c);
                break;
            case Pause: await TogglePause(c); break;
            case Fullscreen:
                if (_mv.Main is { } fs && await MultiViewer.SetFullscreenAsync(fs.Id, !fs.Fullscreen))
                    _mv.Players[_mv.Players.IndexOf(fs)] = fs with { Fullscreen = !fs.Fullscreen };
                else await Alert(c);
                break;
        }
    }

    private Task KeyUp(Ctx c)
    {
        if (c.Action is not (Race or Quali)) return Task.CompletedTask;
        c.Hold?.Cancel();
        c.Hold = null;
        if (!c.HoldFired) Background(c, "layout openen", () => OpenLayoutAsync(LayoutKind(c), closeCurrent: true));
        return Task.CompletedTask;
    }

    private async Task Rotate(Ctx c, int ticks)
    {
        switch (c.Action)
        {
            case Flags:
                S.Brightness = Math.Clamp(S.Brightness + ticks * 13, 25, 254);
                Store.Save();
                await _flags.RefreshAsync();
                break;
            case Zone:
                if (_zones.Count == 0) await LoadZonesAsync();
                if (_zones.Count == 0) { await Alert(c); return; }
                if (_flags.Active) await _flags.BackToNormalAsync(); // old zone back first
                int i = Math.Max(0, _zones.IndexOf(S.Group));
                S.Group = _zones[((i + ticks) % _zones.Count + _zones.Count) % _zones.Count];
                Store.Save();
                _flags.InvalidateLights();
                break;
            case Volume:
                if (_mv.Main is not { } p) return;
                double scale = VolumeScale;
                double v = Math.Clamp(p.Volume + ticks * 0.02 * scale, 0, scale);
                if (p.Muted && ticks > 0) await MultiViewer.SetMutedAsync(p.Id, false);
                if (await MultiViewer.SetVolumeAsync(p.Id, v))
                    _mv.Players[_mv.Players.IndexOf(p)] = p with { Volume = v, Muted = p.Muted && ticks <= 0 };
                break;
            case Latency:
                await ShiftLatency(c, ticks);
                break;
        }
    }

    private async Task Push(Ctx c)
    {
        switch (c.Action)
        {
            case Flags: await ToggleFlags(); break;
            case Volume:
                if (_mv.Main is { } p && await MultiViewer.SetMutedAsync(p.Id, !p.Muted))
                    _mv.Players[_mv.Players.IndexOf(p)] = p with { Muted = !p.Muted };
                break;
            case Latency: await GoLive(c); break;
        }
    }

    private async Task Touch(Ctx c)
    {
        switch (c.Action)
        {
            case Flags: await RunTest(c); break;
            case Latency: await TogglePause(c); break;
            case Zone: break;
            default: await Push(c); break;
        }
    }

    // ------------------------------------------------------------ F1 mode

    /// <summary>
    /// Empty desktop 2 (or a new desktop), the F1 workspace on it, flags on, and once the viewer is up
    /// the race or qualifying layout for the session that is live.
    /// </summary>
    private async Task<bool> StartF1Async()
    {
        var desk = await Desktops.PrepareF1DesktopAsync();
        await WithGate(async () =>
        {
            S.F1DesktopId = desk?.ToString();
            if (!S.FlagsEnabled) S.FlagsEnabled = true;
            Store.Save();
            await Task.CompletedTask;
        });
        await Task.Delay(300);
        if (!Desktops.LaunchWorkspace(S.Workspace)) return false;

        // Wait for Ultimate F1 Viewer and its live-session lookup, then open the matching layout.
        for (int i = 0; i < 60 && !await Uf1.ReadyAsync(); i++) await Task.Delay(1000);
        (bool live, string? type) = (false, null);
        for (int i = 0; i < 10 && !live; i++)
        {
            await Task.Delay(1500);
            (live, type) = await Uf1.LiveSessionAsync();
        }
        if (!live) { Store.Log("F1 Mode: geen live sessie, geen layout geopend"); return true; }
        var kind = (type ?? "").Contains("qual", StringComparison.OrdinalIgnoreCase) ||
                   (type ?? "").Contains("shootout", StringComparison.OrdinalIgnoreCase) ? "quali" : "race";
        Store.Log($"F1 Mode: live {type} -> layout {kind}");
        return await OpenLayoutAsync(kind, closeCurrent: false);
    }

    /// <summary>Lights back, MultiViewer and the viewer closed, F1's desktop removed when empty, back to desktop 1.</summary>
    private async Task<bool> CloseF1Async()
    {
        await WithGate(async () => { _closing = true; await _flags.BackToNormalAsync(); });
        try
        {
            await Task.WhenAll(Desktops.CloseAppAsync(Uf1.Process), Desktops.CloseAppAsync(MultiViewer.Process));
            await Task.Delay(500);
            if (Guid.TryParse(S.F1DesktopId, out var desk)) await Desktops.CloseIfEmptyAsync(desk);
            else await Desktops.GoToAsync(0);
        }
        finally
        {
            // Restore once more in case a flag slipped in, then allow flags again.
            await WithGate(async () =>
            {
                await _flags.BackToNormalAsync();
                S.F1DesktopId = null; Store.Save();
                _latency = 0; _pausedAt = null; _latencyPlayer = null;
                _closing = false;
            });
        }
        return true;
    }

    private static string LayoutKind(Ctx c) => c.Action == Race ? "race" : "quali";

    /// <summary>
    /// Opens the viewer's Race or Qualifying layout: its own windows plus the MultiViewer streams.
    /// Live sessions are found by the viewer; otherwise the replay that is open in MultiViewer is used.
    /// </summary>
    private static async Task<bool> OpenLayoutAsync(string kind, bool closeCurrent)
    {
        var id = Uf1.FindLayout(kind);
        if (id == null || !await Uf1.ReadyAsync()) return false;
        var (live, _) = await Uf1.LiveSessionAsync();
        var contentId = live ? null : await MultiViewer.ContentIdAsync();
        if (closeCurrent)
        {
            await Uf1.CloseExtraWindowsAsync();
            if (live || contentId != null) await MultiViewer.DeleteAllPlayersAsync();
        }
        return await Uf1.RestoreLayoutAsync(id, contentId);
    }

    private static async Task<bool> SaveLayoutAsync(string kind)
    {
        var id = Uf1.FindLayout(kind);
        return id != null && await Uf1.SaveLayoutAsync(id);
    }

    // ------------------------------------------------------------ flags, latency, pause

    private async Task ToggleFlags()
    {
        S.FlagsEnabled = !S.FlagsEnabled;
        Store.Save();
        if (!S.FlagsEnabled) await _flags.BackToNormalAsync();
        else await _flags.UpdateAsync(_mv);
    }

    private async Task RunTest(Ctx c)
    {
        if (!Hue.Paired) { await Alert(c); return; }
        await _flags.TestAsync(RenderAllAsync);
    }

    /// <summary>The dial's zones whose Hue groups exist on the bridge, in dial order.</summary>
    private async Task LoadZonesAsync()
    {
        var all = await Hue.GroupNamesAsync();
        _zones = S.ZoneList
            .Where(z => all.Contains(z.Group) && (z.Except == null || all.Contains(z.Except)))
            .Select(z => z.Name).ToList();
    }

    private double VolumeScale => _mv.Players.Any(x => x.Volume > 1) ? 100 : 1;

    private async Task ShiftLatency(Ctx c, int ticks)
    {
        if (_mv.Main is not { } main) { await Alert(c); return; }
        // Clockwise = more delay (seek back); you cannot get ahead of live.
        // Live streams cannot get ahead of live; in a replay you can shift both ways.
        double shift = main.Live ? Math.Max(ticks, -_latency) : ticks;
        if (shift == 0) return;
        // Only count what MultiViewer actually did, so the dial keeps matching the picture.
        if (!await MultiViewer.SeekRelativeAsync(main.Id, -shift)) { await Alert(c); return; }
        _latency += shift;
        await MultiViewer.SyncAsync(main.Id);
    }

    private async Task GoLive(Ctx c)
    {
        if (_mv.Main is not { } main) { await Alert(c); return; }
        if (_pausedAt != null || main.Paused) await SetPaused(main, false);
        // Live: back to live. Replay: undo the shift made with the dial.
        if (Math.Abs(_latency) >= 0.5)
        {
            if (!await MultiViewer.SeekRelativeAsync(main.Id, _latency)) { await Alert(c); return; }
            await MultiViewer.SyncAsync(main.Id);
        }
        _latency = 0;
        await Ok(c);
    }

    private async Task TogglePause(Ctx c)
    {
        if (_mv.Main is not { } main) { await Alert(c); return; }
        await SetPaused(main, !main.Paused);
    }

    private async Task SetPaused(PlayerInfo main, bool paused)
    {
        // The main player decides: if it did not change, the latency bookkeeping must not either.
        if (!await MultiViewer.SetPausedAsync(main.Id, paused)) return;
        foreach (var p in _mv.Players.Where(p => p.Id != main.Id)) await MultiViewer.SetPausedAsync(p.Id, paused);
        if (paused) _pausedAt = DateTime.UtcNow;
        else if (_pausedAt is { } at) { _latency += (DateTime.UtcNow - at).TotalSeconds; _pausedAt = null; }
        for (int i = 0; i < _mv.Players.Count; i++) _mv.Players[i] = _mv.Players[i] with { Paused = paused };
    }

    /// <summary>
    /// A new live main stream starts at MultiViewer's own start delay (its setting, e.g. 40 s), so the
    /// dial shows that and "naar live" removes it too. Replays have no distance to live.
    /// </summary>
    private void TrackNewStream(PlayerInfo? main)
    {
        if (main == null) { _latencyPlayer = null; return; }
        if (main.Id == _latencyPlayer) return;
        _latencyPlayer = main.Id;
        _latency = main.Live ? S.BaseLatency : 0;
        _pausedAt = null;
    }

    private double ShownLatency => _latency + (_pausedAt is { } at ? (DateTime.UtcNow - at).TotalSeconds : 0);

    // ------------------------------------------------------------ property inspector (Hue pairing)

    private async Task SendPi(Ctx c)
    {
        if (_zones.Count == 0 && Hue.Paired) await LoadZonesAsync();
        await SendAsync(new JsonObject
        {
            ["event"] = "sendToPropertyInspector", ["context"] = c.Id,
            ["payload"] = new JsonObject
            {
                ["paired"] = Hue.Paired, ["bridge"] = S.BridgeIp, ["group"] = S.Group,
                ["groups"] = new JsonArray(_zones.Select(g => (JsonNode)g).ToArray()),
                ["greenSeconds"] = S.GreenSeconds, ["workspace"] = S.Workspace,
                ["status"] = _pairStatus ?? "",
            },
        });
    }

    private async Task FromPi(Ctx c, JsonObject p)
    {
        if (p["group"] is { } g) { if (_flags.Active) await _flags.BackToNormalAsync(); S.Group = (string)g!; _flags.InvalidateLights(); }
        if (p["greenSeconds"] is { } gs && double.TryParse(gs.ToString(), out var sec)) S.GreenSeconds = Math.Clamp(sec, 0, 30);
        if (p["workspace"] is { } w) S.Workspace = ((string?)w ?? "F1").Trim();
        Store.Save();
        if (p["pair"] != null && !_pairing)
        {
            _pairing = true;
            _ = Task.Run(() => PairAsync(c)); // waits up to a minute; must not hold the gate
        }
        await SendPi(c);
    }

    private async Task PairAsync(Ctx c)
    {
        async Task Status(string s) => await WithGate(async () => { _pairStatus = s; await SendPi(c); });
        try
        {
            await Status("Bridges zoeken…");
            var bridges = await Hue.DiscoverAsync();
            if (bridges.Count == 0) { await Status("Geen bridge gevonden."); return; }
            await Status("Druk nu op de ronde knop van je Hue Bridge…");
            string? key = null, ip = null;
            for (int i = 0; i < 30 && key == null; i++)
            {
                foreach (var b in bridges)
                    if ((key = await Hue.TryPairAsync(b)) != null) { ip = b; break; }
                if (key == null) await Task.Delay(2000);
            }
            if (key == null) { await Status("Niet gekoppeld: knop niet op tijd ingedrukt."); return; }
            await WithGate(async () =>
            {
                if (_flags.Active) await _flags.BackToNormalAsync();
                S.BridgeIp = ip; S.AppKey = key; Store.Save();
                await LoadZonesAsync();
                _flags.InvalidateLights();
                _pairStatus = $"Gekoppeld met {ip}.";
                await SendPi(c);
                await RenderAllAsync();
            });
        }
        finally { _pairing = false; }
    }

    // ------------------------------------------------------------ rendering

    private async Task RenderAllAsync()
    {
        foreach (var c in _contexts.Values.ToList()) await RenderAsync(c);
    }

    private async Task RenderAsync(Ctx c)
    {
        if (c.Encoder) await RenderDial(c);
        else await RenderKey(c);
    }

    private async Task RenderKey(Ctx c)
    {
        string? svg = c.Action switch
        {
            Flags => FlagsKey(),
            Pause => _mv.Main?.Paused == true ? Art.Key(Art.Play(), "Play") : Art.Key(Art.Pause(), "Pause"),
            Live => LiveKey(),
            Fullscreen => FullscreenKey(),
            _ => null,
        };
        // F1 Mode shows the user's own logo file when there is one; otherwise the manifest image.
        string? image = svg != null ? Art.DataUrl(svg) : c.Action == Mode ? Art.KeyWithLogo("F1 Mode") : null;
        if (image == null || image == c.Hash) return;
        c.Hash = image;
        await SendAsync(new JsonObject
        {
            ["event"] = "setImage", ["context"] = c.Id,
            ["payload"] = new JsonObject { ["image"] = image, ["target"] = 0 },
        });
    }

    /// <summary>
    /// Time against the reference point, the same everywhere: "0s", "-40s" (behind), "+3s" (ahead).
    /// The plugin's own latency counts seconds behind, so it is shown negated.
    /// </summary>
    private static string Offset(double behind)
    {
        var s = Math.Round(-behind);
        return s == 0 ? "0s" : $"{(s > 0 ? "+" : "-")}{Math.Abs(s):0}s";
    }

    /// <summary>Live key: where you are, not what the key does (LIVE, -40s, replay offset).</summary>
    private string LiveKey()
    {
        var main = _mv.Main;
        double shown = ShownLatency;
        if (main == null) return Art.Key(Art.Live(Art.Grey), "No stream", labelColor: Art.Grey);
        if (!main.Live)
            return Math.Abs(shown) < 1
                ? Art.Key(Art.Live(Art.Grey), "Replay", labelColor: Art.Grey)
                : Art.Key(Art.Live(Art.Grey), "Replay " + Offset(shown), labelColor: Art.Amber, upper: false);
        return shown < 1
            ? Art.Key(Art.Live(), "You're live")
            : Art.Key(Art.Live(Art.Grey), Offset(shown), labelColor: Art.Amber, upper: false);
    }

    private string FlagsKey()
    {
        if (!Hue.Paired) return Art.Key(Art.Flag(Art.Grey), "Pair Hue", "OFF", Art.Grey);
        if (!S.FlagsEnabled) return Art.Key(Art.Flag(Art.Grey), "Flags", "OFF", Art.Grey);
        var f = _flags.Current;
        return f == Flag.Green
            ? Art.Key(Art.Flag(), "Flags", "ON")
            : Art.Key(f == Flag.Chequered ? Art.Chequered() : Art.Flag(FlagEngine.Color(f)), FlagEngine.Label(f), "ON", labelColor: FlagEngine.Color(f));
    }

    private string FullscreenKey()
    {
        var main = _mv.Main;
        if (main == null) return Art.Key(Art.Expand(Art.Grey), "Fullscreen", labelColor: Art.Grey);
        return main.Fullscreen
            ? Art.Key(Art.Collapse(), "Exit full", "ON")
            : Art.Key(Art.Expand(), "Fullscreen");
    }

    private async Task RenderDial(Ctx c)
    {
        string icon, title, value, detail, valueColor = Art.White, accent = Art.Red;
        int bar;
        bool online = _mv.Online;
        var main = _mv.Main;
        string noPlayer = online ? "No player open" : "MultiViewer closed";

        switch (c.Action)
        {
            case Flags:
            {
                var f = _flags.Current;
                bool on = S.FlagsEnabled && Hue.Paired;
                string color = on ? (f == Flag.Green ? Art.Green : FlagEngine.Color(f)) : Art.Grey;
                icon = f == Flag.Chequered && on ? Art.Chequered() : Art.Flag(color);
                title = !Hue.Paired ? "Flags · pair Hue" : S.FlagsEnabled ? "Track status" : "Flags off";
                value = !online ? "MultiViewer closed" : _mv.Timing == null ? "No session" : FlagEngine.Label(f);
                if (on && _mv.Timing != null) valueColor = color;
                detail = $"{_mv.Lap ?? _mv.Session ?? "–"} · lights {Math.Round(S.Brightness / 254.0 * 100)}%";
                accent = color;
                bar = (int)Math.Round(S.Brightness / 254.0 * 100);
                break;
            }
            case Zone:
            {
                icon = Art.Bulb();
                title = "Flag lights";
                value = S.Group;
                detail = _flags.LightCount > 0 ? $"{_flags.LightCount} lights" : "";
                // Fills by share of the lights: ceiling 1/3, all but ceiling 2/3, all lamps full.
                int idx = _zones.IndexOf(S.Group);
                bar = idx >= 0 && _zones.Count > 0 ? (int)Math.Round((idx + 1) * 100.0 / _zones.Count) : 100;
                accent = "#F5C400";
                break;
            }
            case Volume:
            {
                int pct = main == null ? 0 : (int)Math.Round(main.Volume / VolumeScale * 100);
                icon = Art.Speaker(Art.White, main?.Muted == true);
                title = "Volume";
                value = main == null ? noPlayer : main.Muted ? "Muted" : $"{pct}%";
                detail = main?.Title ?? "Main feed";
                bar = main?.Muted == true ? 0 : pct;
                accent = "#2D9CDB";
                break;
            }
            case Latency:
            {
                double shown = ShownLatency;
                bool paused = _pausedAt != null || main?.Paused == true;
                bool replay = main != null && !main.Live;
                icon = Art.Clock(!replay && shown < 1 && !paused ? Art.Red : Art.White);
                // Live: distance to live ("LIVE" or "-40s"). Replay: how far it was moved ("0s", "-3s", "+3s").
                title = replay ? "Replay offset" : "Live delay";
                value = main == null ? noPlayer
                    : (paused ? "Paused " : "") + (replay ? Offset(shown) : shown < 1 ? "LIVE" : Offset(shown));
                if (value == "LIVE") valueColor = Art.Red;
                else if (main != null && Math.Abs(shown) >= 1) valueColor = Art.Amber;
                detail = string.Join(" · ", new[] { _mv.Session, _mv.Lap }.Where(s => s != null));
                if (detail.Length == 0) detail = replay ? "1s per click · push = 0s" : "1s per click · push = live";
                bar = (int)Math.Min(100, Math.Abs(shown) / (replay ? 30 : 60) * 100);
                break;
            }
            default:
                return;
        }

        var payload = new JsonObject
        {
            ["accent"] = new JsonObject { ["value"] = 100, ["bar_fill_c"] = accent },
            ["icon"] = Art.Png(Art.Icon(icon), 72),
            ["title"] = title,
            ["value"] = new JsonObject { ["value"] = value, ["color"] = valueColor },
            ["detail"] = detail,
            ["indicator"] = new JsonObject { ["value"] = bar, ["bar_fill_c"] = accent },
        };
        var json = payload.ToJsonString();
        if (json == c.Hash) return;
        c.Hash = json;
        await SendAsync(new JsonObject { ["event"] = "setFeedback", ["context"] = c.Id, ["payload"] = payload });
    }
}
