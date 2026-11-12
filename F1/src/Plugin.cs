using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace F1;

internal sealed class Ctx
{
    public required string Id { get; init; }
    public required string Action { get; init; }
    public bool Encoder;
    public string? Hash;
    public CancellationTokenSource? Hold, Click;
    public bool HoldFired;
}

internal sealed class Plugin
{
    private const string P = "com.stan.f1.";
    private const string Mode = P + "mode", Flags = P + "flags", Zone = P + "zone",
        Volume = P + "volume", Latency = P + "latency", Live = P + "live", Sync = P + "sync",
        Pause = P + "pause", Race = P + "race", Quali = P + "quali", Fullscreen = P + "fullscreen", Session = P + "session";

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
    /// <summary>F1 Mode state: MultiViewer or the viewer is open; hidden = minimised and muted by a double click.</summary>
    private bool _f1Running, _hidden;
    /// <summary>F1 Mode's steps have run (desktop, apps, flags) since the last Close F1.</summary>
    private bool _f1ModeOn;
    /// <summary>Meeting (race weekend) of the session Race/Quali opened, for the expected order.</summary>
    private int? _sessionMeeting;
    private List<IntPtr> _hiddenWindows = new();
    private Dictionary<string, bool> _mutedBefore = new();
    /// <summary>Session key: 0 = clock or laps, 1 = weather.</summary>
    private int _sessionView;

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
        _ = Task.Run(RestLoop);

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
    /// <remarks>
    /// One job runs at a time. A press while one runs is remembered (only the latest) and runs right
    /// after it, instead of being refused; the keys involved show "…" meanwhile.
    /// </remarks>
    private void Background(Ctx c, string what, Func<Task<bool>> job)
    {
        lock (_jobLock)
        {
            if (_busy)
            {
                _pending = (c, what, job);
                Store.Log($"{what}: wacht tot {_runningWhat} klaar is");
                return;
            }
            _busy = true;
            _running = c.Action; _runningWhat = what;
        }
        _ = Task.Run(() => RunJobsAsync(c, what, job));
    }

    private async Task RunJobsAsync(Ctx c, string what, Func<Task<bool>> job)
    {
        while (true)
        {
            bool ok = false;
            try { Store.Log($"{what}: start"); ok = await job(); Store.Log($"{what}: {(ok ? "klaar" : "mislukt")}"); }
            catch (Exception e) { Store.Log($"{what}: {e}"); }
            await (ok ? Ok(c) : Alert(c));
            lock (_jobLock)
            {
                if (_pending is not { } next) { _busy = false; _running = null; _runningWhat = null; return; }
                _pending = null;
                (c, what, job) = next;
                _running = c.Action; _runningWhat = what;
            }
        }
    }

    private readonly object _jobLock = new();
    private (Ctx Ctx, string What, Func<Task<bool>> Job)? _pending;
    private string? _running, _runningWhat;

    /// <summary>The key's job is running or waiting: show "…" on it.</summary>
    private bool IsWorking(string action)
    {
        lock (_jobLock) return _running == action || _pending?.Ctx.Action == action;
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
            _ = Schedule.RefreshAsync(); // at most every 30 minutes
            await WithGate(async () =>
            {
                bool wasOnline = _mv.Online;
                _mv = state;
                if (!state.Online)
                {
                    if (wasOnline) { _latency = 0; _pausedAt = null; _latencyPlayer = null; MvSetups.Active = false; }
                    if (_flags.Active) await _flags.BackToNormalAsync();
                }
                else if (!_closing) await _flags.UpdateAsync(state);
                TrackNewStream(state.Main);
                _f1Running = state.Online || Desktops.IsRunning(Uf1.Process);
                if (!_f1Running && _hidden) { _hidden = false; _hiddenWindows = new(); _mutedBefore = new(); }
                await RenderAllAsync();
            });
            if (++_ticks % 4 == 0 && _f1Running)
            {
                try { Apps.KeepBattleModeOnTop(); } catch (Exception e) { Store.Log("battle mode voor: " + e.Message); }
            }
            await Task.Delay(state.Online ? 500 : 2000);
        }
    }

    private int _ticks;

    /// <summary>Every 30 s: keep the best-of-the-rest onboards on the two best drivers outside the top teams.</summary>
    private async Task RestLoop()
    {
        while (_ws.State == WebSocketState.Open)
        {
            await Task.Delay(30_000);
            if (!_mv.Online || _mv.Timing == null || _closing) continue;
            try { await MvSetups.UpdateRestSlotsAsync(); }
            catch (Exception e) { Store.Log("best of the rest: " + e.Message); }
            // Battle mode follows the order during a race opened with the Race key.
            if (MvSetups.Active && S.BattleLive && _mv.Timing?["SessionInfo"]?["Type"]?.ToString() == "Race")
            {
                try { await UpdateBattleAsync(); }
                catch (Exception e) { Store.Log("battle mode: " + e.Message); }
            }
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
            if (_contexts.Remove(ctxId, out var gone)) { gone.Hold?.Cancel(); gone.Hold = null; gone.Click?.Cancel(); gone.Click = null; }
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
        if (c.Action is Race or Quali or Mode)
        {
            // Hold 1 s = close F1 (F1 Mode, Race and Quali alike); a short press is handled on key up.
            c.HoldFired = false;
            c.Hold?.Cancel();
            var cts = c.Hold = new CancellationTokenSource();
            const double hold = 1.0;
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(TimeSpan.FromSeconds(hold), cts.Token); }
                catch (OperationCanceledException) { return; }
                // Key-up cancels under the gate first, so a release right at the limit never does both.
                await WithGate(async () =>
                {
                    if (cts.IsCancellationRequested) return;
                    c.HoldFired = true;
                    c.Click?.Cancel();
                    Background(c, "Close F1", CloseF1Async);
                    await Task.CompletedTask;
                });
            });
            return;
        }

        switch (c.Action)
        {
            case Session: _sessionView = (_sessionView + 1) % 2; break;
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

    private async Task KeyUp(Ctx c)
    {
        if (c.Action is not (Race or Quali or Mode)) return;
        c.Hold?.Cancel();
        c.Hold = null;
        if (c.HoldFired) return;
        if (c.Action != Mode) { Background(c, "layout openen", () => OpenLayoutAsync(LayoutKind(c), closeCurrent: true)); return; }

        // F1 Mode: a second click within 350 ms makes a double click (hide/show); otherwise a single click.
        if (c.Click != null)
        {
            c.Click.Cancel();
            c.Click = null;
            await ToggleHiddenAsync(c);
            return;
        }
        var click = c.Click = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(350, click.Token); }
            catch (OperationCanceledException) { return; }
            await WithGate(async () =>
            {
                if (click.IsCancellationRequested) return;
                c.Click = null;
                if (_hidden) await ToggleHiddenAsync(c);           // hidden: bring it back
                else if (_f1ModeOn && _f1Running) await Ok(c);     // already running: nothing to do
                else Background(c, "F1 Mode", StartF1Async);       // start
            });
        });
    }

    /// <summary>Double click on F1 Mode: minimise F1's windows and mute everything, or bring it all back.</summary>
    private async Task ToggleHiddenAsync(Ctx c)
    {
        if (!_hidden)
        {
            if (!_f1Running) { await Alert(c); return; }
            _hiddenWindows = Desktops.AppWindows(MultiViewer.Process, Uf1.Process);
            Desktops.Minimize(_hiddenWindows);
            _mutedBefore = _mv.Players.ToDictionary(p => p.Id, p => p.Muted);
            foreach (var p in _mv.Players.Where(p => !p.Muted)) await MultiViewer.SetMutedAsync(p.Id, true);
            _hidden = true;
        }
        else
        {
            Desktops.Restore(_hiddenWindows);
            foreach (var p in _mv.Players)
                if (_mutedBefore.TryGetValue(p.Id, out var wasMuted) && !wasMuted) await MultiViewer.SetMutedAsync(p.Id, false);
            _hiddenWindows = new(); _mutedBefore = new();
            _hidden = false;
        }
        await Ok(c);
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
                double v = Math.Clamp(p.Volume + ticks * S.VolumeStep / 100.0 * scale, 0, scale);
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
            case Latency:
                if (S.LatencyPush == "pause") await TogglePause(c); else await GoLive(c);
                break;
        }
    }

    private async Task Touch(Ctx c)
    {
        switch (c.Action)
        {
            case Flags: await RunTest(c); break;
            case Latency: // touch does the other one of live / pause
                if (S.LatencyPush == "pause") await GoLive(c); else await TogglePause(c);
                break;
            case Zone: break;
            default: await Push(c); break;
        }
    }

    // ------------------------------------------------------------ F1 mode

    /// <summary>
    /// Empty desktop 2 (or a new desktop), the F1 workspace on it, flags on, and once the viewer is up
    /// the race or qualifying layout for the session that is live.
    /// </summary>
    private Task<bool> StartF1Async() => StartF1Async(openLayout: true);

    /// <summary>
    /// MultiViewer is required (without it: a message with its download page, and nothing starts).
    /// Ultimate F1 Viewer is optional (a message with its download page once, then it is skipped).
    /// </summary>
    private static bool CheckApps()
    {
        if (!Apps.MultiViewerInstalled)
        {
            Apps.OfferDownload("MultiViewer", "Deze plugin werkt met MultiViewer om F1 TV te kijken: vlaggen, latency, volume en de race- en quali-opstellingen.",
                Apps.MultiViewerDownload, oncePerRun: false);
            return false;
        }
        if (Apps.Uf1Exe == null && !Desktops.IsRunning(Uf1.Process))
            Apps.OfferDownload("Ultimate F1 Viewer", "Die is optioneel: hij geeft vlaggenbalken, battle mode en overlays. Zonder werkt de rest van de plugin gewoon.",
                Apps.Uf1Download, oncePerRun: true);
        return true;
    }

    private static bool Uf1Available => Apps.Uf1Exe != null || Desktops.IsRunning(Uf1.Process);

    /// <param name="openLayout">False when Race/Quali start F1 themselves and open their own layout afterwards.</param>
    private async Task<bool> StartF1Async(bool openLayout)
    {
        if (!CheckApps()) return false;
        Guid? desk = S.ModeDesktop == "current" ? null : await Desktops.GoToNamedAsync(S.DesktopName);
        await WithGate(async () =>
        {
            S.F1DesktopId = desk?.ToString();
            if (S.ModeFlagsOn && !S.FlagsEnabled) S.FlagsEnabled = true;
            Store.Save();
            await Task.CompletedTask;
        });
        await Task.Delay(300);
        // MultiViewer and the viewer are started here (the viewer with its debug port) and put in place.
        if (!await Apps.LaunchF1AppsAsync()) return false;
        _f1ModeOn = true;

        // Wait for Ultimate F1 Viewer; one that was already running without the debug port is restarted.
        if (Uf1Available)
            for (int i = 0; i < 30 && !await Uf1.ReadyAsync(); i++) await Task.Delay(1000);
        if (!openLayout || S.ModeLayout == "none") return true;

        string kind;
        if (S.ModeLayout is "race" or "quali") kind = S.ModeLayout;
        else
        {
            // Finding the live session uses Ultimate F1 Viewer; without it F1 Mode leaves the layout to the keys.
            if (!Uf1Available || !await Uf1.EnsureAsync()) { Store.Log("F1 Mode: geen Ultimate F1 Viewer, layout via Race/Quali"); return true; }
            // Automatic: the live session decides; without one no layout is opened.
            var (live, type) = await Uf1.WaitLiveSessionAsync();
            if (!live) { Store.Log("F1 Mode: geen live sessie, geen layout geopend"); return true; }
            kind = (type ?? "").Contains("qual", StringComparison.OrdinalIgnoreCase) ||
                   (type ?? "").Contains("shootout", StringComparison.OrdinalIgnoreCase) ? "quali" : "race";
            Store.Log($"F1 Mode: live {type} -> layout {kind}");
        }
        return await OpenLayoutAsync(kind, closeCurrent: false);
    }

    /// <summary>Lights back, MultiViewer and the viewer closed, F1's desktop removed when empty, back to desktop 1.</summary>
    private async Task<bool> CloseF1Async()
    {
        await WithGate(async () => { _closing = true; await _flags.BackToNormalAsync(); });
        try
        {
            var closing = new List<Task>();
            if (S.CloseUf1) closing.Add(Desktops.CloseAppAsync(Uf1.Process));
            if (S.CloseMultiViewer) closing.Add(Desktops.CloseAppAsync(MultiViewer.Process));
            await Task.WhenAll(closing);
            await Task.Delay(500);
            if (S.CloseDesktop && Guid.TryParse(S.F1DesktopId, out var desk)) await Desktops.CloseIfEmptyAsync(desk);
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
                _hidden = false; _hiddenWindows = new(); _mutedBefore = new();
                MvSetups.Active = false;
                _f1ModeOn = false;
                _closing = false;
            });
        }
        return true;
    }

    private static string LayoutKind(Ctx c) => c.Action == Race ? "race" : "quali";

    /// <summary>
    /// Race / Quali, from any starting point:
    /// 1. F1 not running yet → start it (desktop, apps, flags), MultiViewer with its debug port;
    /// 2. the right session in MultiViewer: the one already open if it is that kind, else the live
    ///    race/qualifying, else the latest race/qualifying replay on F1 TV;
    /// 3. the MultiViewer setup (best of the rest filled in), then the Ultimate F1 Viewer layout,
    ///    then the battle mode drivers (race).
    /// </summary>
    private async Task<bool> OpenLayoutAsync(string kind, bool closeCurrent)
    {
        bool any = false;
        if (!CheckApps()) return false;

        // Race/Quali include F1 Mode: run all its steps when it has not been started yet (even when an
        // app was already open by hand); otherwise just make sure we are on the F1 desktop.
        if (closeCurrent)
        {
            if (!_f1ModeOn)
            {
                if (!await StartF1Async(openLayout: false)) Store.Log($"layout {kind}: F1 Mode starten mislukt");
            }
            else if (S.ModeDesktop != "current") await Desktops.GoToNamedAsync(S.DesktopName);
        }
        if (!await MvSetups.EnsureAsync()) { Store.Log($"layout {kind}: MultiViewer niet bereikbaar"); return false; }
        if (Uf1Available) await Uf1.EnsureAsync();
        await OpenSessionAsync(kind);

        if (MvSetups.Find(kind) is { } setup)
        {
            await MvSetups.Lock.WaitAsync();
            try
            {
                MvSetups.Active = false; // slots of a previous setup no longer count
                var rest = S.RestEnabled ? await MvSetups.BestOfRestAsync() : new List<int>();
                // No timing yet (a session just opened): use the grid / last practice, so the slots
                // open with the right drivers instead of being swapped later.
                if (S.RestEnabled && rest.Count == 0 && _sessionMeeting is { } meeting)
                {
                    rest = await Replays.ExpectedRestAsync(kind, meeting);
                    Store.Log($"best of the rest vooraf ({(kind == "race" ? "startopstelling" : "laatste training")}): {string.Join(",", rest.Take(4))}");
                }
                var r = await MvSetups.RestoreAsync(setup, rest);
                Store.Log($"MultiViewer-setup {setup}: {r} (best of the rest: {string.Join(",", rest.Take(4))})");
                if (r == "ok") { any = true; MvSetups.Active = true; }
            }
            finally { MvSetups.Lock.Release(); }
        }
        else Store.Log($"layout {kind}: geen MultiViewer-setup gevonden");

        // The setup's players are starting; MultiViewer's timing for the session follows within seconds.
        // Wait for it before anything that needs positions or the driver list.
        bool timing = false;
        for (int i = 0; i < 25 && !timing; i++)
        {
            timing = MultiViewer.ByPosition(await MultiViewer.TimingAsync()).Count >= 8;
            if (!timing) await Task.Delay(1000);
        }
        Store.Log(timing ? "timing geladen" : "timing na 25 s nog niet geladen");

        // Best of the rest right away (the setup may have opened before positions were known).
        if (timing && MvSetups.Active) await MvSetups.UpdateRestSlotsAsync(force: true);

        // Higher windows in front of lower ones, so overlapping edges hide the title bars below.
        Store.Log($"vensters op hoogte gestapeld: {await MvSetups.StackAsync()}");

        // Only now the viewer's layout: battle mode and crash detection read the driver list once,
        // when their window opens, so they must open after the timing is there.
        if (!Uf1Available) return any; // optional app not installed: the MultiViewer part is all there is
        var id = Uf1.FindLayout(kind);
        if (id == null) { Store.Log($"layout {kind}: geen layout gevonden in Ultimate F1 Viewer"); return any; }
        var replayContent = await MultiViewer.ContentIdAsync();
        if (!await Uf1.EnsureAsync()) { Store.Log($"layout {kind}: Ultimate F1 Viewer niet bereikbaar"); return any; }
        var (live, type) = await Uf1.LiveSessionAsync();
        var contentId = live ? null : replayContent;
        Store.Log($"layout {kind} (id {id}): live={live} {type} replay={(contentId ?? "-")}");
        if (closeCurrent && S.LayoutCloseCurrent) await Uf1.CloseExtraWindowsAsync();
        var ok = await Uf1.RestoreLayoutAsync(id, contentId);
        if (!ok) Store.Log($"layout {kind}: openen in Ultimate F1 Viewer mislukt");

        // Battle mode and crash detection open in move mode; switch that off (as pressing Escape does).
        for (int i = 0; i < 8; i++)
        {
            await Task.Delay(1000);
            if (await Uf1.LockWindowsAsync() == 0) break;
        }

        // Battle mode: its windows need a moment to load their driver list.
        if (kind == "race")
        {
            bool battleOk = false;
            for (int i = 0; i < 10 && !battleOk; i++)
            {
                battleOk = await UpdateBattleAsync();
                if (battleOk) break;
                if (i == 3)
                {
                    // Still not ready after a few tries: the window gave up loading; reload it, lock it again.
                    await Uf1.ReloadBattleModeAsync();
                    await Task.Delay(3000);
                    await Uf1.LockWindowsAsync();
                }
                await Task.Delay(1500);
            }
            Store.Log(battleOk ? "battle mode: coureurs gezet" : "battle mode: lukte niet");
            Apps.KeepBattleModeOnTop(); // in front of the flag display behind it
        }
        return any | ok;
    }

    /// <summary>
    /// Makes MultiViewer show a session of the wanted kind: keeps one that is already open, else opens
    /// the live race/qualifying, else the latest replay of it (when that setting is on).
    /// </summary>
    private async Task OpenSessionAsync(string kind)
    {
        bool wanted(string? type) => kind == "quali"
            ? (type ?? "").Contains("Qualifying", StringComparison.OrdinalIgnoreCase)
            : string.Equals(type, "Race", StringComparison.OrdinalIgnoreCase);

        var openNow = await MvSetups.OpenContentAsync();
        _sessionMeeting = int.TryParse(_mv.Timing?["SessionInfo"]?["Meeting"]?["Key"]?.ToString(), out var mk) ? mk : null;
        if (openNow != null && wanted((string?)_mv.Timing?["SessionInfo"]?["Type"]))
        {
            Store.Log($"sessie: al open ({_mv.Session})");
            return;
        }

        string? contentId = null, what = null;
        var (live, type) = Uf1Available ? await Uf1.WaitLiveSessionAsync(5) : (false, (string?)null);
        if (live && wanted(type) && await Uf1.LiveContentIdAsync() is { } liveId) { contentId = liveId; what = $"live {type}"; }
        else if (S.OpenLatestReplay && await Replays.LatestAsync(kind) is { } latest)
        {
            contentId = latest.Id; what = latest.Title;
            _sessionMeeting = Replays.LastMeetingKey;
        }
        if (contentId == null || contentId == openNow) { Store.Log($"sessie: niets nieuws te openen voor {kind}"); return; }

        Store.Log($"sessie openen: {what} ({contentId})");
        Replays.Open(contentId);
        // Wait until MultiViewer shows that session page, so the setup can find its streams.
        bool opened = false;
        for (int i = 0; i < 30 && !opened; i++)
        {
            await Task.Delay(1000);
            opened = await MvSetups.OpenContentAsync() == contentId;
        }
        if (!opened) Store.Log("sessie: pagina niet op tijd geopend");
        // No waiting for timing here: MultiViewer only loads a replay's timing once a player runs,
        // and the players come with the setup that opens next.
    }

    /// <summary>Battle mode: left = top 4 of the session, right = top 3 of the championship (or P5-8).</summary>
    private static async Task<bool> UpdateBattleAsync()
    {
        if (!S.BattleEnabled) return true;
        var timing = await MultiViewer.TimingAsync();
        var pos = MultiViewer.ByPosition(timing);
        if (pos.Count < 8) { Store.Log($"battle mode: nog geen posities ({pos.Count})"); return false; }
        var champ = MultiViewer.ByChampionship(timing);
        var right = S.BattleRight == "p5to8" || champ.Count < 3 ? pos.Skip(4).Take(4).ToList() : champ.Take(3).ToList();
        return await Uf1.SetBattleDriversAsync(pos.Take(4).ToList(), right);
    }

    private static async Task<bool> SaveLayoutAsync(string kind)
    {
        var id = Uf1.FindLayout(kind);
        // No restart here: that would close the very windows that should be saved.
        if (id == null || !await Uf1.ReadyAsync()) { Store.Log($"layout {kind} opslaan: viewer niet bereikbaar of geen layout"); return false; }
        var ok = await Uf1.SaveLayoutAsync(id);
        Store.Log($"layout {kind} (id {id}) opgeslagen: {ok}");
        return ok;
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
        // Own zone list when set up (e.g. "Ambient" = all lights minus the ceiling), else simply every
        // room and zone on the bridge, so a new install works with anyone's lights.
        _zones = S.ZoneList.Count > 0
            ? S.ZoneList.Where(z => all.Contains(z.Group) && (z.Except == null || all.Contains(z.Except))).Select(z => z.Name).ToList()
            : all;
        if (_zones.Count > 0 && !_zones.Contains(S.Group))
        {
            S.Group = _zones[0];
            Store.Save();
            _flags.InvalidateLights();
        }
    }

    private double VolumeScale => _mv.Players.Any(x => x.Volume > 1) ? 100 : 1;

    private async Task ShiftLatency(Ctx c, int ticks)
    {
        if (_mv.Main is not { } main) { await Alert(c); return; }
        // Clockwise = more delay (seek back); you cannot get ahead of live.
        // Live streams cannot get ahead of live; in a replay you can shift both ways.
        double step = ticks * Math.Max(1, S.LatencyStep);
        double shift = main.Live ? Math.Max(step, -_latency) : step;
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
        if (Math.Abs(_latency) >= 0.5 && !await MultiViewer.SeekRelativeAsync(main.Id, _latency)) { await Alert(c); return; }
        _latency = 0;
        // Optionally pull every onboard and extra feed along to the main feed.
        if (S.LiveSyncAll) await MultiViewer.SyncAsync(main.Id);
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
        if (S.PauseAll)
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

    /// <summary>Everything the settings panel shows: the settings themselves plus live status and choices.</summary>
    private async Task SendPi(Ctx c)
    {
        if (_zones.Count == 0 && Hue.Paired) await LoadZonesAsync();
        var settings = JsonSerializer.SerializeToNode(S)!.AsObject();
        settings.Remove("AppKey"); // the bridge key never goes to the panel
        settings.Remove("ZoneList");
        var layouts = new JsonArray(Uf1.Layouts().Select(l => (JsonNode)new JsonObject
        {
            ["id"] = l.Id, ["name"] = l.Name, ["windows"] = l.Windows, ["streams"] = l.Streams,
        }).ToArray());
        await SendAsync(new JsonObject
        {
            ["event"] = "sendToPropertyInspector", ["context"] = c.Id,
            ["payload"] = new JsonObject
            {
                ["settings"] = settings,
                ["paired"] = Hue.Paired,
                ["zones"] = new JsonArray(_zones.Select(g => (JsonNode)g).ToArray()),
                ["layouts"] = layouts,
                ["raceFound"] = Uf1.FindLayout("race"), ["qualiFound"] = Uf1.FindLayout("quali"),
                ["mvSetups"] = new JsonArray(MvSetups.List().Select(s => (JsonNode)new JsonObject
                {
                    ["name"] = s.Name, ["windows"] = s.Windows, ["players"] = s.Players,
                }).ToArray()),
                ["raceSetupFound"] = MvSetups.Find("race"), ["qualiSetupFound"] = MvSetups.Find("quali"),
                ["mvReady"] = await MvSetups.ReadyAsync(),
                ["uf1Ready"] = await Uf1.ReadyAsync(),
                ["uf1Running"] = Desktops.IsRunning(Uf1.Process),
                ["placeMv"] = S.PlaceMultiViewer?.ToString() ?? "niet ingesteld",
                ["placeUf1"] = S.PlaceUf1?.ToString() ?? "niet ingesteld",
                ["mvOnline"] = _mv.Online,
                ["status"] = _pairStatus ?? "",
            },
        });
    }

    private async Task FromPi(Ctx c, JsonObject p)
    {
        static bool B(JsonNode n) => n.GetValueKind() == JsonValueKind.True || n.ToString() is "true" or "1";
        static double D(JsonNode n, double min, double max, double fallback) =>
            double.TryParse(n.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
                ? Math.Clamp(v, min, max) : fallback;
        static string? Id(JsonNode n) => n.ToString() is { Length: > 0 } s ? s : null;

        foreach (var (key, v) in p.ToList())
        {
            if (v == null) continue;
            switch (key)
            {
                case "Group":
                    if (_flags.Active) await _flags.BackToNormalAsync();
                    S.Group = v.ToString(); _flags.InvalidateLights(); break;
                case "GreenSeconds": S.GreenSeconds = D(v, 0, 30, S.GreenSeconds); break;
                case "SavePlaces":
                    var (mvPlace, uf1Place) = Apps.Capture(_mv.Players.Select(x => x.Title ?? "").Where(t => t.Length > 0).ToList());
                    if (mvPlace != null) S.PlaceMultiViewer = mvPlace;
                    if (uf1Place != null) S.PlaceUf1 = uf1Place;
                    _pairStatus = mvPlace == null && uf1Place == null ? "Geen F1-vensters gevonden om op te slaan." : "Posities opgeslagen.";
                    break;
                case "ModeDesktop": S.ModeDesktop = v.ToString(); break;
                case "ModeLayout": S.ModeLayout = v.ToString(); break;
                case "ModeFlagsOn": S.ModeFlagsOn = B(v); break;
                case "CloseMultiViewer": S.CloseMultiViewer = B(v); break;
                case "CloseUf1": S.CloseUf1 = B(v); break;
                case "CloseDesktop": S.CloseDesktop = B(v); break;
                case "ShowYellow": S.ShowYellow = B(v); break;
                case "ShowSafetyCar": S.ShowSafetyCar = B(v); break;
                case "ShowRed": S.ShowRed = B(v); break;
                case "ShowChequered": S.ShowChequered = B(v); break;
                case "VscPulse": S.VscPulse = B(v); await _flags.RefreshAsync(); break;
                case "VolumeStep": S.VolumeStep = (int)D(v, 1, 10, S.VolumeStep); break;
                case "LatencyStep": S.LatencyStep = (int)D(v, 1, 10, S.LatencyStep); break;
                case "BaseLatency": S.BaseLatency = D(v, 0, 300, S.BaseLatency); break;
                case "LatencyPush": S.LatencyPush = v.ToString(); break;
                case "LiveSyncAll": S.LiveSyncAll = B(v); break;
                case "PauseAll": S.PauseAll = B(v); break;
                case "RaceMvSetup": S.RaceMvSetup = Id(v); break;
                case "QualiMvSetup": S.QualiMvSetup = Id(v); break;
                case "RestEnabled": S.RestEnabled = B(v); break;
                case "RestLive": S.RestLive = B(v); break;
                case "BattleEnabled": S.BattleEnabled = B(v); break;
                case "BattleRight": S.BattleRight = v.ToString(); break;
                case "BattleLive": S.BattleLive = B(v); break;
                case "OpenLatestReplay": S.OpenLatestReplay = B(v); break;
                case "TopTeams":
                    var teams = v.ToString().Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
                    if (teams.Count > 0) S.TopTeams = teams;
                    break;
                case "RaceLayout": S.RaceLayout = Id(v); break;
                case "QualiLayout": S.QualiLayout = Id(v); break;
                case "HoldSeconds": S.HoldSeconds = D(v, 1, 5, S.HoldSeconds); break;
                case "LayoutCloseCurrent": S.LayoutCloseCurrent = B(v); break;
                case "FlagsEnabled":
                    S.FlagsEnabled = B(v);
                    if (!S.FlagsEnabled) await _flags.BackToNormalAsync(); else await _flags.UpdateAsync(_mv);
                    break;
            }
        }
        Store.Save();
        if (p["test"] != null && Hue.Paired) await _flags.TestAsync(RenderAllAsync);
        // Race/Quali settings: save the viewer's current windows as that layout (was: holding the key).
        if (p["saveLayout"] != null && c.Action is Race or Quali)
        {
            var kind = LayoutKind(c);
            _pairStatus = await SaveLayoutAsync(kind)
                ? $"Huidige vensters opgeslagen als layout {(kind == "race" ? "Race" : "Qualifying")}."
                : "Opslaan lukte niet: Ultimate F1 Viewer is niet bereikbaar.";
        }
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
            Session => SessionKey(),
            Race => Art.Key(Art.Chequered(), "Race", IsWorking(Race) ? "…" : null, Art.Amber, caption: Opens("race", out var rc), captionColor: rc),
            Quali => Art.Key(Art.Stopwatch(), "Quali", IsWorking(Quali) ? "…" : null, Art.Amber, caption: Opens("quali", out var qc), captionColor: qc),
            Mode when !Art.HasLogo => Art.Key(Art.F1Mark(), "F1 Mode", ModePill, ModePillColor),
            _ => null,
        };
        // F1 Mode shows the user's own logo file when there is one.
        string? image = svg != null ? Art.DataUrl(svg) : c.Action == Mode ? Art.KeyWithLogo("F1 Mode", ModePill, ModePillColor) : null;
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

    // Only "hidden" gets a tag: whether F1 runs you can see on screen, a hidden F1 you cannot.
    private string? ModePill => IsWorking(Mode) ? "…" : _hidden ? "HID" : null;
    private string ModePillColor => Art.Amber;

    /// <summary>
    /// Session key: session name on top, then the clock (qualifying, practice) or laps (race), or —
    /// after a press — the weather.
    /// </summary>
    /// <summary>
    /// Which session Race/Quali would open, for the small line on the key: the one open in MultiViewer,
    /// a live one ("LIVE"), or the latest replay (its location). Null when unknown.
    /// </summary>
    private string? Opens(string kind, out string color)
    {
        color = Art.Grey;
        var type = (string?)_mv.Timing?["SessionInfo"]?["Type"];
        bool open = kind == "quali" ? (type ?? "").Contains("Qualifying", StringComparison.OrdinalIgnoreCase) : type == "Race";
        if (open && (string?)_mv.Timing?["SessionInfo"]?["Meeting"]?["Location"] is { Length: > 0 } here) return here;
        if (Schedule.LiveNow(kind) is { } live) { color = Art.Red; return "LIVE · " + live.Location; }
        return S.OpenLatestReplay ? Schedule.LatestDone(kind)?.Location : null;
    }

    private string SessionKey()
    {
        var name = _mv.SessionShort ?? "Session";
        // No session open: the next one on the calendar ("NEXT", "Quali", "Sat 16:00").
        if (_mv.Timing == null && Schedule.Next() is { } next)
            return Art.InfoKey("Next", Schedule.Short(next.Name), Schedule.When(next.Start), Art.White);
        if (_mv.Timing == null) return Art.InfoKey("Session", "–", _mv.Online ? "No session" : "Offline", Art.Grey);
        if (_sessionView == 1)
        {
            var w = _mv.Weather;
            return Art.InfoKey(name, w.Track is { } t ? $"{t:0}°" : "–",
                (w.Air is { } a ? $"Air {a:0}°" : "") + (w.Rain ? " · Rain" : ""), w.Rain ? "#2D9CDB" : Art.White, "Track");
        }
        bool race = _mv.IsRace && _mv.Lap != null;
        return race
            ? Art.InfoKey(name, _mv.LapShort ?? "–", "Laps", Art.White)
            : Art.InfoKey(name, _mv.Remaining ?? "–", "Remaining", Art.White);
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
        // Short enough for the value line; the reason goes on the detail line below it.
        string noPlayer = online ? "No stream" : "Offline";
        string noPlayerWhy = online ? "No player in MultiViewer" : "MultiViewer is closed";

        switch (c.Action)
        {
            case Flags:
            {
                var f = _flags.Current;
                bool on = S.FlagsEnabled && Hue.Paired;
                string color = on ? (f == Flag.Green ? Art.Green : FlagEngine.Color(f)) : Art.Grey;
                icon = f == Flag.Chequered && on ? Art.Chequered() : Art.Flag(color);
                title = !Hue.Paired ? "Flags · pair Hue" : S.FlagsEnabled ? "Track status" : "Flags off";
                value = !online ? "Offline" : _mv.Timing == null ? "No session" : FlagEngine.Label(f);
                if (on && _mv.Timing != null) valueColor = color;
                detail = !online ? "MultiViewer is closed"
                    : _mv.Timing == null ? "No session in MultiViewer"
                    : $"{_mv.Lap ?? _mv.Session ?? "–"} · lights {Math.Round(S.Brightness / 254.0 * 100)}%";
                accent = color;
                bar = (int)Math.Round(S.Brightness / 254.0 * 100);
                break;
            }
            case Zone:
            {
                icon = Art.Bulb(Hue.Paired ? Art.White : Art.Grey);
                title = "Flag lights";
                value = Hue.Paired ? S.Group : "Not set up";
                detail = !Hue.Paired ? "Pair Hue in the settings" : _flags.LightCount > 0 ? $"{_flags.LightCount} lights" : "";
                if (!Hue.Paired) valueColor = Art.Grey;
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
                detail = main == null ? noPlayerWhy : main.Title ?? "Main feed";
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
                if (main == null) detail = noPlayerWhy;
                else if (detail.Length == 0) detail = replay ? "1s per click · push = 0s" : "1s per click · push = live";
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
