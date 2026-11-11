using System.Text.Json.Nodes;

namespace F1;

public enum Flag { Green, Yellow, SafetyCar, Vsc, Red, Chequered }

/// <summary>
/// Shows track flags on the Hue lights and puts the lights back the way they were afterwards.
/// All public methods are called under the plugin's gate, so state needs no extra locking.
/// </summary>
public sealed class FlagEngine
{
    private static readonly Dictionary<string, double[]> Xy = new()
    {
        ["yellow"] = new[] { 0.48, 0.47 },
        ["orange"] = new[] { 0.59, 0.39 },
        ["red"] = new[] { 0.675, 0.322 },
        ["green"] = new[] { 0.17, 0.70 },
        ["white"] = new[] { 0.3227, 0.329 },
    };

    private readonly Func<Func<Task>, CancellationToken, Task> _gate;
    private Task? _fxTask;
    private List<string> _lights = new();
    private string? _lightsGroup;
    private Dictionary<string, JsonObject>? _snapshot;
    private CancellationTokenSource? _fx;
    private int _seenChequered = -1;

    public Flag Current { get; private set; } = Flag.Green;
    /// <summary>True while the lights show something other than the user's own light setting.</summary>
    public bool Active => _snapshot != null;
    public event Action? Changed;

    private static F1Settings S => Store.Current;

    /// <param name="gate">Runs a callback under the plugin's lock (the wait can be cancelled); timed effects end through it.</param>
    public FlagEngine(Func<Func<Task>, CancellationToken, Task> gate) => _gate = gate;

    public static Flag FromTrackStatus(string status) => status switch
    {
        "2" => Flag.Yellow,
        "4" => Flag.SafetyCar,
        "5" => Flag.Red,
        "6" or "7" => Flag.Vsc, // 7 = VSC ending; stays VSC until green
        _ => Flag.Green,
    };

    /// <summary>Flags switched off in the settings count as green (the lights stay as they are).</summary>
    private static Flag Wanted(Flag f) => f switch
    {
        Flag.Yellow when !S.ShowYellow => Flag.Green,
        Flag.SafetyCar or Flag.Vsc when !S.ShowSafetyCar => Flag.Green,
        Flag.Red when !S.ShowRed => Flag.Green,
        _ => f,
    };

    public static string Label(Flag f) => f switch
    {
        Flag.Yellow => "Yellow",
        Flag.SafetyCar => "Safety Car",
        Flag.Vsc => "VSC",
        Flag.Red => "Red Flag",
        Flag.Chequered => "Chequered",
        _ => "Green",
    };

    public static string Color(Flag f) => f switch
    {
        Flag.Yellow => "#F5C400",
        Flag.SafetyCar or Flag.Vsc => "#FF8A00",
        Flag.Red => "#E10600",
        Flag.Chequered => "#FFFFFF",
        _ => "#1DB954",
    };

    private async Task<List<string>> LightsAsync()
    {
        if (_lightsGroup != S.Group || _lights.Count == 0)
        {
            var zone = S.ZoneList.FirstOrDefault(z => z.Name == S.Group) ?? new ZoneDef { Name = S.Group, Group = S.Group };
            _lights = await Hue.LightsAsync(zone);
            _lightsGroup = S.Group;
        }
        return _lights;
    }

    /// <summary>Called on every MultiViewer poll with the live timing state.</summary>
    public async Task UpdateAsync(MvState state)
    {
        if (!S.FlagsEnabled || !Hue.Paired) return;
        if (state.Timing == null)
        {
            _seenChequered = -1;
            if (Active) await BackToNormalAsync();
            return;
        }

        int cheq = state.Chequered;
        if (_seenChequered < 0 || cheq < _seenChequered) _seenChequered = cheq; // start, or rewound in a replay
        if (cheq > _seenChequered)
        {
            _seenChequered = cheq;
            if (S.ShowChequered) await ShowAsync(Flag.Chequered);
        }
        else if (Current != Flag.Chequered)
        {
            await ShowAsync(Wanted(FromTrackStatus(state.TrackStatus)));
        }
    }

    public async Task ShowAsync(Flag flag)
    {
        if (flag == Current) return;
        var prev = Current;
        Current = flag;
        Store.Log($"vlag {prev} -> {flag}");
        Changed?.Invoke();

        var lights = await LightsAsync();
        if (lights.Count == 0) return;
        if (flag != Flag.Green && _snapshot == null)
        {
            // Without a snapshot the user's lights could never be put back, so leave them alone.
            _snapshot = await SnapshotAsync(lights);
            if (_snapshot == null) { Store.Log("vlag niet getoond: lichtstand niet op te halen"); return; }
        }

        await StopFxAsync();
        switch (flag)
        {
            case Flag.Yellow: await Hue.SetAllAsync(lights, ColorState("yellow")); break;
            case Flag.SafetyCar: await Hue.SetAllAsync(lights, ColorState("orange")); break;
            case Flag.Red: await Hue.SetAllAsync(lights, ColorState("red")); break;
            case Flag.Vsc when S.VscPulse: StartFx(t => PulseAsync(lights, t)); break;
            case Flag.Vsc: await Hue.SetAllAsync(lights, ColorState("orange")); break;
            case Flag.Chequered: StartFx(t => ChequeredAsync(lights, t)); break;
            case Flag.Green when _snapshot != null: StartFx(t => GreenAsync(lights, t)); break;
        }
    }

    /// <summary>Re-applies the current flag, for example after a brightness change.</summary>
    public async Task RefreshAsync()
    {
        if (!Active || Current is Flag.Green or Flag.Chequered or Flag.Vsc) return;
        var f = Current;
        Current = Flag.Green;
        await ShowAsync(f);
    }

    public async Task BackToNormalAsync()
    {
        await StopFxAsync();
        Current = Flag.Green;
        if (_snapshot != null)
        {
            var snap = _snapshot;
            _snapshot = null;
            await RestoreAsync(snap);
        }
        Changed?.Invoke();
    }

    /// <summary>Runs through every flag so you can see how they look.</summary>
    public async Task TestAsync(Func<Task> render)
    {
        var lights = await LightsAsync();
        if (lights.Count == 0) return;
        var snap = _snapshot ?? await SnapshotAsync(lights);
        if (snap == null) return;
        await StopFxAsync();
        try
        {
            foreach (var (name, xy, ms) in new[] { ("Geel", "yellow", 1500), ("SC", "orange", 1500), ("Rood", "red", 1500), ("Groen", "green", 1200) })
            {
                Current = name switch { "Geel" => Flag.Yellow, "SC" => Flag.SafetyCar, "Rood" => Flag.Red, _ => Flag.Green };
                await render();
                await Hue.SetAllAsync(lights, ColorState(xy));
                await Task.Delay(ms);
            }
        }
        finally
        {
            Current = Flag.Green;
            _snapshot = null;
            await RestoreAsync(snap);
            await render();
        }
    }

    public void InvalidateLights() => _lights = new();

    /// <summary>Number of colour lights in the current zone (0 until they have been looked up).</summary>
    public int LightCount => _lightsGroup == S.Group ? _lights.Count : 0;

    // ------------------------------------------------------------ effects

    /// <summary>Starts an effect; callers stop the previous one first with <see cref="StopFxAsync"/>.</summary>
    private void StartFx(Func<CancellationToken, Task> fx)
    {
        var cts = new CancellationTokenSource();
        _fx = cts;
        _fxTask = Task.Run(async () =>
        {
            try { await fx(cts.Token); }
            catch (OperationCanceledException) { }
            catch (Exception e) { Store.Log("fx: " + e.Message); }
        });
    }

    /// <summary>
    /// Cancels the running effect and waits until its last Hue write is done, so nothing it sends
    /// can land after a restore. Safe under the gate: an effect only takes the gate with its own
    /// token, which this cancels first.
    /// </summary>
    private async Task StopFxAsync()
    {
        var cts = _fx; var task = _fxTask;
        _fx = null; _fxTask = null;
        if (cts == null) return;
        cts.Cancel();
        if (task != null) await task;
    }

    private static async Task PulseAsync(List<string> lights, CancellationToken t)
    {
        bool high = true;
        while (!t.IsCancellationRequested)
        {
            await Hue.SetAllAsync(lights, ColorState("orange", high ? S.Brightness : Math.Max(30, S.Brightness / 4), 5));
            high = !high;
            await Task.Delay(800, t);
        }
    }

    private async Task ChequeredAsync(List<string> lights, CancellationToken t)
    {
        for (int i = 0; i < 12; i++)
        {
            await Hue.SetAllAsync(lights, i % 2 == 0 ? ColorState("white") : new JsonObject { ["on"] = false, ["transitiontime"] = 0 });
            await Task.Delay(500, t);
        }
        await FinishFxAsync(t);
    }

    private async Task GreenAsync(List<string> lights, CancellationToken t)
    {
        await Hue.SetAllAsync(lights, ColorState("green"));
        await Task.Delay(TimeSpan.FromSeconds(S.GreenSeconds), t);
        await FinishFxAsync(t);
    }

    /// <summary>End of a timed effect: lights back to normal (unless a new flag took over).</summary>
    private Task FinishFxAsync(CancellationToken t) => _gate(async () =>
    {
        // Checked under the gate: a new flag cancels this token before it changes anything itself.
        if (t.IsCancellationRequested) return;
        var snap = _snapshot;
        _snapshot = null;
        Current = Flag.Green;
        if (snap != null) await RestoreAsync(snap);
        Changed?.Invoke();
    }, t);

    // ------------------------------------------------------------ hue state

    private static JsonObject ColorState(string color, int? bri = null, int tt = 0) => new()
    {
        ["on"] = true,
        ["xy"] = new JsonArray(Xy[color][0], Xy[color][1]),
        ["bri"] = bri ?? S.Brightness,
        ["transitiontime"] = tt,
    };

    /// <summary>The lights' current state, or null when it could not be read for every light.</summary>
    private static async Task<Dictionary<string, JsonObject>?> SnapshotAsync(List<string> lights)
    {
        var all = await Hue.GetAsync("/lights");
        if (all == null || lights.Any(id => all[id]?["state"] == null)) return null;
        var snap = new Dictionary<string, JsonObject>();
        foreach (var id in lights)
        {
            var s = all?[id]?["state"];
            if (s == null) continue;
            var st = new JsonObject { ["on"] = (bool?)s["on"] ?? false, ["bri"] = (int?)s["bri"] ?? 254, ["transitiontime"] = 4 };
            switch ((string?)s["colormode"])
            {
                case "ct": st["ct"] = (int?)s["ct"]; break;
                case "hs": st["hue"] = (int?)s["hue"]; st["sat"] = (int?)s["sat"]; break;
                default: if (s["xy"] != null) st["xy"] = s["xy"]!.DeepClone(); break;
            }
            snap[id] = st;
        }
        return snap;
    }

    private static Task RestoreAsync(Dictionary<string, JsonObject> snap) =>
        Task.WhenAll(snap.Select(async kv =>
        {
            // Colour can only be set while a light is on; switch it off afterwards if it was off.
            var on = (JsonObject)kv.Value.DeepClone();
            bool wasOn = (bool?)on["on"] ?? false;
            on["on"] = true;
            await Hue.PutStateAsync(kv.Key, on);
            if (!wasOn)
            {
                await Task.Delay(500);
                await Hue.PutStateAsync(kv.Key, new JsonObject { ["on"] = false, ["transitiontime"] = 4 });
            }
        }));
}
