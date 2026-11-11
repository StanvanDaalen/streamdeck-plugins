using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace F1;

internal sealed class Ctx
{
    public required string Id { get; init; }
    public required string Action { get; init; }
    public string? Device;
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
        Test = P + "test", Pause = P + "pause", Race = P + "race", Quali = P + "quali";

    private readonly int _port;
    private readonly string _uuid, _register;
    private readonly ClientWebSocket _ws = new();
    private readonly SemaphoreSlim _gate = new(1, 1), _sendLock = new(1, 1);
    private readonly Dictionary<string, Ctx> _contexts = new();
    private readonly FlagEngine _flags;
    private readonly System.Threading.Channels.Channel<JsonObject> _inbox =
        System.Threading.Channels.Channel.CreateUnbounded<JsonObject>();
    private bool _pairing;

    private MvState _mv = new();
    private List<string> _groups = new();
    /// <summary>Seconds of delay this plugin added on top of live (dial + pauses).</summary>
    private double _latency;
    private DateTime? _pausedAt;
    private string? _piContext;
    private string _pairStatus = "";

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

            var text = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            _inbox.Writer.TryWrite(JsonNode.Parse(text)!.AsObject());
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
                    if (wasOnline) { _latency = 0; _pausedAt = null; }
                    if (_flags.Active) await _flags.BackToNormalAsync();
                }
                else await _flags.UpdateAsync(state);
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
            _contexts[ctxId] = new Ctx
            {
                Id = ctxId, Action = action, Device = (string?)m["device"],
                Encoder = (string?)payload?["controller"] == "Encoder",
            };
            if (action == Zone && _groups.Count == 0) _groups = await Hue.GroupNamesAsync();
            await RenderAsync(_contexts[ctxId]);
            return;
        }
        if (ev == "willDisappear" && ctxId != null) { _contexts.Remove(ctxId); return; }
        if (ctxId == null || !_contexts.TryGetValue(ctxId, out var c)) return;
        c.Device ??= (string?)m["device"];

        switch (ev)
        {
            case "keyDown": await KeyDown(c); break;
            case "keyUp": await KeyUp(c); break;
            case "dialRotate": await Rotate(c, (int?)payload?["ticks"] ?? 0); break;
            case "dialDown": await Push(c); break;
            case "touchTap": await Touch(c); break;
            case "propertyInspectorDidAppear": _piContext = c.Id; await SendPi(c); break;
            case "propertyInspectorDidDisappear": _piContext = null; break;
            case "sendToPlugin": await FromPi(c, payload ?? new()); break;
        }
        await RenderAllAsync();
    }

    private async Task KeyDown(Ctx c)
    {
        if (c.Action is Race or Quali)
        {
            // Hold 2 s = save the current layout; a short press restores it (on key up).
            c.HoldFired = false;
            c.Hold?.Cancel();
            var cts = c.Hold = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(2000, cts.Token); }
                catch (OperationCanceledException) { return; }
                // Key-up cancels under the gate before restoring, so a release right at 2 s
                // never both restores and saves.
                await WithGate(async () => { if (!cts.IsCancellationRequested) await SaveLayout(c); });
            });
            return;
        }

        switch (c.Action)
        {
            case Mode: await StartF1(c); break;
            case Stop: await StopF1(c); break;
            case Flags: await ToggleFlags(); break;
            case Live: await GoLive(c); break;
            case Sync:
                if (_mv.Main is { } main) { await MultiViewer.SyncAsync(main.Id); await Ok(c); } else await Alert(c);
                break;
            case Test: await RunTest(c); break;
            case Pause: await TogglePause(c); break;
        }
    }

    private async Task KeyUp(Ctx c)
    {
        if (c.Action is not (Race or Quali)) return;
        c.Hold?.Cancel();
        c.Hold = null;
        if (c.HoldFired) return;
        var name = LayoutName(c);
        if (!S.Layouts.TryGetValue(name, out var layout) || layout.Count == 0) { await Alert(c); return; }
        if (await MultiViewer.RestoreAsync(layout)) await Ok(c); else await Alert(c);
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
                if (_groups.Count == 0) _groups = await Hue.GroupNamesAsync();
                if (_groups.Count == 0) { await Alert(c); return; }
                if (_flags.Active) await _flags.BackToNormalAsync(); // old zone back first
                int i = Math.Max(0, _groups.IndexOf(S.Group));
                S.Group = _groups[((i + ticks) % _groups.Count + _groups.Count) % _groups.Count];
                Store.Save();
                _flags.InvalidateLights();
                break;
            case Volume:
                if (_mv.Main is not { } p) return;
                double scale = _mv.Players.Any(x => x.Volume > 1) ? 100 : 1;
                double v = Math.Clamp(p.Volume + ticks * 0.02 * scale, 0, scale);
                if (p.Muted && ticks > 0) await MultiViewer.SetMutedAsync(p.Id, false);
                await MultiViewer.SetVolumeAsync(p.Id, v);
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
            case Zone: await RunTest(c); break;
            case Volume:
                if (_mv.Main is { } p)
                {
                    await MultiViewer.SetMutedAsync(p.Id, !p.Muted);
                    _mv.Players[_mv.Players.IndexOf(p)] = p with { Muted = !p.Muted };
                }
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
            default: await Push(c); break;
        }
    }

    // ------------------------------------------------------------ actions

    private async Task StartF1(Ctx c)
    {
        await Desktops.GoToAsync(Math.Max(0, S.Desktop - 1));
        await Task.Delay(300);
        bool launched = Desktops.LaunchWorkspace(S.Workspace);
        if (!S.FlagsEnabled) { S.FlagsEnabled = true; Store.Save(); }
        if (c.Device != null)
            await SendAsync(new JsonObject
            {
                ["event"] = "switchToProfile", ["context"] = _uuid, ["device"] = c.Device,
                ["payload"] = new JsonObject { ["profile"] = "F1" },
            });
        if (!launched) await Alert(c);
    }

    private async Task StopF1(Ctx c)
    {
        await _flags.BackToNormalAsync();
        await Desktops.GoToAsync(0);
        // Without a profile name Stream Deck goes back to the profile that was active before.
        if (c.Device != null)
            await SendAsync(new JsonObject
            {
                ["event"] = "switchToProfile", ["context"] = _uuid, ["device"] = c.Device, ["payload"] = new JsonObject(),
            });
    }

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

    private async Task ShiftLatency(Ctx c, int ticks)
    {
        if (_mv.Main is not { } main) { await Alert(c); return; }
        // Clockwise = more delay (seek back); you cannot get ahead of live.
        double shift = Math.Max(ticks, -_latency);
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
        if (_latency > 0)
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

    private static string LayoutName(Ctx c) => c.Action == Race ? "Race" : "Kwali";

    private async Task SaveLayout(Ctx c)
    {
        c.HoldFired = true;
        var players = await MultiViewer.CaptureAsync();
        if (players is not { Count: > 0 }) { await Alert(c); return; }
        S.Layouts[LayoutName(c)] = players;
        Store.Save();
        Store.Log($"layout {LayoutName(c)} opgeslagen: {players.Count} spelers");
        await Ok(c);
        await RenderAsync(c);
    }

    // ------------------------------------------------------------ property inspector (Hue pairing)

    private async Task SendPi(Ctx c)
    {
        if (_groups.Count == 0 && Hue.Paired) _groups = await Hue.GroupNamesAsync();
        await SendAsync(new JsonObject
        {
            ["event"] = "sendToPropertyInspector", ["context"] = c.Id,
            ["payload"] = new JsonObject
            {
                ["paired"] = Hue.Paired, ["bridge"] = S.BridgeIp, ["group"] = S.Group,
                ["groups"] = new JsonArray(_groups.Select(g => (JsonNode)g).ToArray()),
                ["greenSeconds"] = S.GreenSeconds, ["workspace"] = S.Workspace, ["desktop"] = S.Desktop,
                ["status"] = _pairStatus,
            },
        });
    }

    private async Task FromPi(Ctx c, JsonObject p)
    {
        if (p["group"] is { } g) { if (_flags.Active) await _flags.BackToNormalAsync(); S.Group = (string)g!; _flags.InvalidateLights(); }
        if (p["greenSeconds"] is { } gs && double.TryParse(gs.ToString(), out var sec)) S.GreenSeconds = Math.Clamp(sec, 0, 30);
        if (p["workspace"] is { } w) S.Workspace = ((string?)w ?? "F1").Trim();
        if (p["desktop"] is { } d && int.TryParse(d.ToString(), out var dk)) S.Desktop = Math.Clamp(dk, 1, 9);
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
                _groups = await Hue.GroupNamesAsync();
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
            Pause => _mv.Main?.Paused == true ? Art.Key(Art.Play, "Verder") : Art.Key(Art.Pause, "Pauze"),
            Live => Art.Key(Art.Live, _latency >= 1 ? $"+{_latency:0} s" : "Naar live",
                _latency >= 1 ? Art.Dark : Art.Red, accent: Art.Red),
            Race or Quali => Art.Key(Art.Layout, LayoutName(c), badge: c.Action == Race ? "R" : "Q",
                accent: S.Layouts.ContainsKey(LayoutName(c)) ? Art.Red : Art.Grey),
            _ => null, // static image from the manifest
        };
        if (svg == null || svg == c.Hash) return;
        c.Hash = svg;
        await SendAsync(new JsonObject
        {
            ["event"] = "setImage", ["context"] = c.Id,
            ["payload"] = new JsonObject { ["image"] = Art.DataUrl(svg), ["target"] = 0 },
        });
    }

    private string FlagsKey()
    {
        if (!Hue.Paired) return Art.Key(Art.Flag, "Hue koppelen", Art.Grey);
        if (!S.FlagsEnabled) return Art.Key(Art.Flag, "Vlaggen", Art.Grey, "#9A9AA0", "UIT", Art.Grey);
        var f = _flags.Current;
        var glyph = f == Flag.Chequered ? Art.Chequered : Art.Flag;
        if (f == Flag.Green) return Art.Key(glyph, "Vlaggen", Art.Dark, "#FFFFFF", "AAN", FlagEngine.Color(f));
        return Art.Key(glyph, FlagEngine.Label(f), FlagEngine.Color(f), "#15151E", "AAN", Art.Dark);
    }

    private async Task RenderDial(Ctx c)
    {
        string title, value, accent = Art.Red, valueColor = "#FFFFFF";
        int bar;
        bool online = _mv.Online;
        var main = _mv.Main;

        switch (c.Action)
        {
            case Flags:
                var f = _flags.Current;
                title = !Hue.Paired ? "Vlaggen · koppel Hue" : S.FlagsEnabled ? "Vlaggen · AAN" : "Vlaggen · UIT";
                value = !online ? "MultiViewer dicht" : _mv.Timing == null ? "Geen sessie"
                    : FlagEngine.Label(f) + (_mv.Lap is { } lap ? "  " + lap : "");
                accent = S.FlagsEnabled ? FlagEngine.Color(f) : Art.Grey;
                if (S.FlagsEnabled && f != Flag.Green) valueColor = FlagEngine.Color(f);
                bar = (int)Math.Round(S.Brightness / 254.0 * 100);
                break;
            case Zone:
                title = "Lampzone";
                value = S.Group;
                int idx = _groups.IndexOf(S.Group);
                bar = _groups.Count > 1 && idx >= 0 ? (int)Math.Round(idx * 100.0 / (_groups.Count - 1)) : 100;
                accent = "#7A5CFF";
                break;
            case Volume:
                double scale = _mv.Players.Any(x => x.Volume > 1) ? 100 : 1;
                int pct = main == null ? 0 : (int)Math.Round(main.Volume / scale * 100);
                title = "MultiViewer";
                value = main == null ? (online ? "Geen speler" : "Niet open") : main.Muted ? "Gedempt" : $"{pct}%";
                bar = main?.Muted == true ? 0 : pct;
                accent = "#2D9CDB";
                break;
            case Latency:
                title = "Latency";
                double shown = _latency + (_pausedAt is { } at ? (DateTime.UtcNow - at).TotalSeconds : 0);
                value = main == null ? (online ? "Geen speler" : "Niet open")
                    : _pausedAt != null || main.Paused ? $"Pauze  +{shown:0} s"
                    : shown < 1 ? "LIVE" : $"+{shown:0} s";
                if (value == "LIVE") valueColor = Art.Red;
                bar = (int)Math.Min(100, shown / 30 * 100);
                break;
            default:
                return;
        }

        var payload = new JsonObject
        {
            ["accent"] = new JsonObject { ["value"] = 100, ["bar_fill_c"] = accent },
            ["title"] = title,
            ["value"] = new JsonObject { ["value"] = value, ["color"] = valueColor },
            ["indicator"] = new JsonObject { ["value"] = bar },
        };
        var json = payload.ToJsonString();
        if (json == c.Hash) return;
        c.Hash = json;
        await SendAsync(new JsonObject { ["event"] = "setFeedback", ["context"] = c.Id, ["payload"] = payload });
    }
}
