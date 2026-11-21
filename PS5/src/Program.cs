using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace PS5Plugin;

internal sealed class Ctx
{
    public required string Id { get; init; }
    public JsonObject Settings { get; set; } = new();
    public string? LastImage;
    public bool LayoutSet;
    public CancellationTokenSource? Hold;
    public bool HoldFired, RotatedWhileDown;
    public string? Overlay;
    public string SdTitle = ""; // the title typed in Stream Deck itself
    public DateTime OverlayUntil;
}

internal static class Program
{
    // An own icon in this spot is used by default; otherwise a drawn controller.
    private static readonly string DefaultIcon = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OBS Stream", "icons", "ps5.png");
    private static readonly ClientWebSocket Ws = new();
    private static readonly SemaphoreSlim Gate = new(1, 1), SendLock = new(1, 1);
    private static readonly Dictionary<string, Ctx> Contexts = new();
    private static readonly Audio Sound = new();
    private static readonly Obs Obs = new();
    private static readonly Detector Ps5 = new();
    private static DateTime _startedObs = DateTime.MinValue;
    private static string? _piContext;

    private static async Task<int> Main(string[] args)
    {
        string? Arg(string n) { int i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        if (Arg("--assets") is { } assets) { Art.WriteAssets(assets); return 0; }
        if (Arg("--mark") is { } mark) { Art.MarkPreview(mark); return 0; }
        if (Arg("--preview") is { } preview) { Art.Preview(preview, File.Exists(DefaultIcon) ? DefaultIcon : null); return 0; }
        Log.Reset();
        if (Arg("--probe") is { } probe) { await Probe(probe); return 0; }

        var port = Arg("-port"); var uuid = Arg("-pluginUUID"); var reg = Arg("-registerEvent");
        if (port == null || uuid == null || reg == null) return 1;

        await Ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), CancellationToken.None);
        await Send(new JsonObject { ["event"] = reg, ["uuid"] = uuid });
        _ = Task.Run(SlowLoop);
        _ = Task.Run(FastLoop);

        var buffer = new byte[1 << 16];
        using var ms = new MemoryStream();
        while (Ws.State == WebSocketState.Open)
        {
            ms.SetLength(0);
            WebSocketReceiveResult r;
            do
            {
                r = await Ws.ReceiveAsync(buffer, CancellationToken.None);
                if (r.MessageType == WebSocketMessageType.Close) return 0;
                ms.Write(buffer, 0, r.Count);
            } while (!r.EndOfMessage);
            var message = JsonNode.Parse(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length))!.AsObject();
            await Locked(() => Handle(message));
        }
        return 0;
    }

    /// <summary>Command-line check without Stream Deck: writes what the plugin sees for 20 seconds.</summary>
    private static async Task Probe(string file)
    {
        var sb = new StringBuilder();
        for (int t = 0; t < 10 && !Obs.Connected; t++) { await Obs.EnsureConnected(); await Task.Delay(1000); }
        var big = await Obs.Request("GetSourceScreenshot", new JsonObject { ["sourceName"] = "PS5 beeld", ["imageFormat"] = "png", ["imageWidth"] = 320, ["imageHeight"] = 180 });
        if (big?["imageData"]?.GetValue<string>() is { } img)
            File.WriteAllBytes(file + ".png", Convert.FromBase64String(img[(img.IndexOf(',') + 1)..]));
        for (int i = 0; i < 8; i++)
        {
            await Obs.EnsureConnected();
            Sound.Refresh();
            for (int k = 0; k < 10; k++) { Ps5.Sample(Sound.InputPeak); await Task.Delay(200); }
            await Ps5.Update(Obs, Sound, "PS5 beeld", "PS5 geluid");
            sb.AppendLine($"{DateTime.Now:HH:mm:ss} obs={Obs.Connected} err={Obs.LastError} state={Ps5.State} detail={Ps5.Detail} video={Ps5.VideoFound} audio={Ps5.AudioFound} vol={Sound.Volume} mute={Sound.Muted} peak={Sound.InputPeak}");
            File.WriteAllText(file, sb.ToString());
        }
    }

    private static async Task Locked(Func<Task> f)
    {
        await Gate.WaitAsync();
        try { await f(); }
        catch (Exception e) { Log.Write("error: " + e); }
        finally { Gate.Release(); }
    }

    private static async Task Send(JsonObject o)
    {
        var bytes = Encoding.UTF8.GetBytes(o.ToJsonString());
        await SendLock.WaitAsync();
        try { if (Ws.State == WebSocketState.Open) await Ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
        finally { SendLock.Release(); }
    }

    private static string Str(Ctx c, string key, string fallback) =>
        c.Settings[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Trim().Length > 0 ? s.Trim() : fallback;

    private static bool Bool(Ctx c, string key, bool fallback) =>
        c.Settings[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;

    private static int Int(Ctx c, string key, int fallback) =>
        c.Settings[key] is JsonValue v && (v.TryGetValue<int>(out var i) || (v.TryGetValue<string>(out var s) && int.TryParse(s, out i))) ? i : fallback;

    private static string VideoSource => Contexts.Values.Select(c => Str(c, "videoSource", "")).FirstOrDefault(s => s.Length > 0) ?? "PS5 beeld";
    private static string AudioSource => Contexts.Values.Select(c => Str(c, "audioSource", "")).FirstOrDefault(s => s.Length > 0) ?? "PS5 geluid";

    /// <summary>Every 2 s: OBS connection, audio sessions and whether the PS5 is on.</summary>
    private static async Task SlowLoop()
    {
        while (Ws.State == WebSocketState.Open)
        {
            try
            {
                if (Contexts.Count > 0)
                {
                    await Obs.EnsureConnected();
                    var before = Ps5.State;
                    await Locked(async () =>
                    {
                        Sound.Refresh();
                        await Ps5.Update(Obs, Sound, VideoSource, AudioSource);
                        if (Ps5.State != before) Log.Write($"ps5: {before} -> {Ps5.State} ({Ps5.Detail})");
                        await RenderAll();
                        if (_piContext != null && Contexts.TryGetValue(_piContext, out var pi)) await SendPi(pi);
                    });
                }
            }
            catch (Exception e) { Log.Write("slow loop: " + e.Message); }
            await Task.Delay(2000);
        }
    }

    /// <summary>Several times a second: the sound level, for the on/off check.</summary>
    private static async Task FastLoop()
    {
        while (Ws.State == WebSocketState.Open)
        {
            try
            {
                if (Contexts.Count > 0 && Obs.Running)
                {
                    var peak = Sound.InputPeak;
                    Ps5.Sample(peak);
                }
            }
            catch (Exception e) { Log.Write("fast loop: " + e.Message); }
            await Task.Delay(150);
        }
    }

    private static async Task RenderAll()
    {
        foreach (var c in Contexts.Values.ToList()) await Render(c);
    }

    private static async Task Handle(JsonObject m)
    {
        var ev = m["event"]?.GetValue<string>();
        var id = m["context"]?.GetValue<string>();
        var payload = m["payload"] as JsonObject;

        if (ev == "willAppear" && id != null)
        {
            var added = new Ctx { Id = id, Settings = payload?["settings"]?.DeepClone() as JsonObject ?? new() };
            Contexts[id] = added;
            Sound.Refresh();
            await Render(added);
            return;
        }
        if (ev == "willDisappear" && id != null) { Contexts.Remove(id); return; }
        if (id == null || !Contexts.TryGetValue(id, out var c)) return;

        switch (ev)
        {
            case "titleParametersDidChange":
                c.SdTitle = payload?["title"]?.GetValue<string>()?.Trim() ?? "";
                Log.Write($"stream deck title: \"{c.SdTitle}\"");
                c.LastImage = null;
                await Render(c);
                break;
            case "didReceiveSettings":
                c.Settings = payload?["settings"]?.DeepClone() as JsonObject ?? new();
                Ps5.Reset();
                c.LastImage = null;
                await Render(c);
                break;
            case "dialRotate":
                await Rotate(c, payload!["ticks"]!.GetValue<int>(), payload["pressed"]?.GetValue<bool>() ?? false);
                break;
            case "dialDown":
                c.HoldFired = false; c.RotatedWhileDown = false;
                StartHold(c);
                break;
            case "dialUp":
                c.Hold?.Cancel(); c.Hold = null;
                if (!c.HoldFired && !c.RotatedWhileDown) await Press(c);
                break;
            case "touchTap":
                if (payload?["hold"]?.GetValue<bool>() == true) await ResetVolume(c);
                else await Press(c);
                break;
            case "propertyInspectorDidAppear":
                _piContext = c.Id;
                await SendPi(c);
                break;
            case "propertyInspectorDidDisappear":
                _piContext = null;
                break;
            case "sendToPlugin":
                await OnPi(c, payload ?? new());
                break;
        }
    }

    private static void StartHold(Ctx c)
    {
        var cts = new CancellationTokenSource();
        c.Hold = cts;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(600, cts.Token); } catch (OperationCanceledException) { return; }
            await Locked(async () =>
            {
                if (cts.IsCancellationRequested || c.RotatedWhileDown) return;
                c.HoldFired = true;
                await ResetVolume(c);
            });
        });
    }

    private static async Task Rotate(Ctx c, int ticks, bool pressed)
    {
        if (pressed) c.RotatedWhileDown = true;
        if (!Sound.HasSessions) Sound.Refresh();
        if (!Sound.HasSessions) { await Flash(c, Obs.Running ? "No audio" : "OBS not running"); return; }
        int step = pressed ? 1 : Math.Clamp(Int(c, "step", 5), 1, 25);
        int now = (int)Math.Round((Sound.Volume ?? 0f) * 100f);
        // snap to the step grid so the numbers stay round
        int next = ticks > 0 ? (now / step + ticks) * step : ((now + step - 1) / step + ticks) * step;
        next = Math.Clamp(next, 0, 100);
        Sound.SetVolume(next / 100f);
        if (Sound.Muted && ticks > 0) Sound.SetMute(false);
        await RenderAll();
    }

    private static async Task Press(Ctx c)
    {
        if (!Obs.Running)
        {
            if (DateTime.UtcNow - _startedObs < TimeSpan.FromSeconds(15)) { await Flash(c, "Starting OBS…"); return; }
            _startedObs = DateTime.UtcNow;
            await Flash(c, Obs.Start() ? "Starting OBS…" : "OBS not found", 6);
            return;
        }
        if (!Sound.HasSessions) Sound.Refresh();
        if (!Sound.HasSessions) { await Flash(c, "No audio"); return; }
        Sound.SetMute(!Sound.Muted);
        await RenderAll();
    }

    private static async Task ResetVolume(Ctx c)
    {
        if (!Sound.HasSessions) Sound.Refresh();
        if (!Sound.HasSessions) { await Flash(c, Obs.Running ? "No audio" : "OBS not running"); return; }
        int target = Math.Clamp(Int(c, "defaultVolume", 70), 0, 100);
        Sound.SetVolume(target / 100f);
        Sound.SetMute(false);
        await Flash(c, $"Default {target}%");
    }

    private static async Task Flash(Ctx c, string text, int seconds = 2)
    {
        c.Overlay = text;
        c.OverlayUntil = DateTime.UtcNow.AddSeconds(seconds);
        await Render(c);
    }

    /// <summary>
    /// Built like the Spotify player dial: cover top left with the name beside it, a label bottom left,
    /// the percentage bottom right and the volume bar where Spotify has its timeline.
    /// On or off shows through colour only: blue cover and bar when on, grey when off.
    /// </summary>
    private static async Task Render(Ctx c)
    {
        if (c.Overlay != null && DateTime.UtcNow >= c.OverlayUntil) c.Overlay = null;
        if (!c.LayoutSet)
        {
            c.LayoutSet = true;
            c.LastImage = null;
            await Send(new JsonObject { ["event"] = "setFeedbackLayout", ["context"] = c.Id, ["payload"] = new JsonObject { ["layout"] = "layouts/ps5.json" } });
        }
        var state = Ps5.State;
        bool on = state == Ps5State.On;
        bool closed = state == Ps5State.ObsClosed;
        int? volume = Sound.HasSessions && Sound.Volume is { } v ? (int)Math.Round(v * 100f) : null;
        bool muted = Sound.HasSessions && Sound.Muted;
        // The title typed in Stream Deck wins, then the one in our own settings.
        var name = c.SdTitle.Length > 0 ? c.SdTitle : Str(c, "title", "PlayStation 5");
        var iconPath = Str(c, "icon", File.Exists(DefaultIcon) ? DefaultIcon : "");
        string? icon = iconPath.Length > 0 ? iconPath : null;

        // Bottom left says what the bar shows; a short message (such as "Default 70%") replaces it briefly.
        // Under the name: whether the capture card gets a signal from the PlayStation.
        string status = state switch
        {
            Ps5State.On => "On",
            Ps5State.Off => "Off",
            _ => "Checking…",
        };
        if (c.Overlay != null) status = c.Overlay; // short messages such as "Default 70%"

        var payload = new JsonObject
        {
            ["artwork"] = Art.Tile(icon, on),
            ["name"] = new JsonObject { ["value"] = name, ["color"] = on ? "#FFFFFF" : "#8C8C8C" },
            ["status"] = new JsonObject { ["value"] = status },
            // Loudspeaker, with a cross when muted.
            // Small PlayStation badge on the cover corner, like the YouTube and Spotify logos on the media dials.
            ["badge"] = new JsonObject { ["value"] = Art.Badge(), ["opacity"] = Bool(c, "accentLogo", true) ? (on ? 1 : 0.5) : 0 },
            ["speaker"] = new JsonObject { ["value"] = Art.Loudspeaker(muted), ["opacity"] = on ? 1 : 0.5 },
            ["percent"] = new JsonObject { ["value"] = volume == null ? "—" : $"{volume}%", ["color"] = on && !muted ? "#FFFFFF" : "#8C8C8C" },
            // PlayStation blue, like Spotify green / YouTube red on the media dials; grey when off or muted.
            ["progress"] = new JsonObject { ["value"] = volume ?? 0, ["bar_fill_c"] = on && !muted && Bool(c, "accentBar", true) ? "#0070D1" : "#FFFFFF", ["opacity"] = muted ? 0.3 : on ? 1 : 0.4 },
            ["overlay"] = new JsonObject { ["value"] = Art.ObsClosed(icon), ["opacity"] = closed && c.Overlay == null ? 1 : 0 },
        };
        var json = payload.ToJsonString();
        if (json == c.LastImage) return;
        c.LastImage = json;
        await Send(new JsonObject { ["event"] = "setFeedback", ["context"] = c.Id, ["payload"] = payload });
    }

    private static async Task OnPi(Ctx c, JsonObject p)
    {
        bool changed = false;
        foreach (var field in new[] { "title", "icon", "step", "defaultVolume", "videoSource", "audioSource", "accentBar", "accentLogo" })
            if (p[field] is { } value) { c.Settings[field] = value.DeepClone(); changed = true; }
        if (changed)
        {
            await Send(new JsonObject { ["event"] = "setSettings", ["context"] = c.Id, ["payload"] = c.Settings.DeepClone() });
            if (p["videoSource"] != null || p["audioSource"] != null) Ps5.Reset();
            c.LastImage = null;
            await Render(c);
        }
        await SendPi(c);
    }

    private static async Task SendPi(Ctx c)
    {
        string obs = !Obs.Running ? "Not running" : Obs.Connected ? "Connected" : $"Not connected{(Obs.LastError.Length > 0 ? ": " + Obs.LastError : "")}";
        string ps5 = Ps5.State switch
        {
            Ps5State.On => "On",
            Ps5State.Off => "Off",
            Ps5State.ObsClosed => "Unknown (OBS not running)",
            _ => "Checking…",
        };
        if (Ps5.Detail.Length > 0 && Ps5.State != Ps5State.ObsClosed) ps5 += $" ({Ps5.Detail})";

        var sources = new JsonArray();
        if (Obs.Connected && await Obs.Request("GetInputList") is { } list && list["inputs"] is JsonArray inputs)
            foreach (var i in inputs) if (i?["inputName"]?.GetValue<string>() is { } n) sources.Add(n);

        await Send(new JsonObject
        {
            ["event"] = "sendToPropertyInspector", ["context"] = c.Id,
            ["payload"] = new JsonObject
            {
                ["title"] = Str(c, "title", "PlayStation 5"),
                ["icon"] = Str(c, "icon", File.Exists(DefaultIcon) ? DefaultIcon : ""),
                ["step"] = Int(c, "step", 5).ToString(),
                ["defaultVolume"] = Int(c, "defaultVolume", 70),
                ["accentBar"] = Bool(c, "accentBar", true),
                ["accentLogo"] = Bool(c, "accentLogo", true),
                ["videoSource"] = Str(c, "videoSource", "PS5 beeld"),
                ["audioSource"] = Str(c, "audioSource", "PS5 geluid"),
                ["sources"] = sources,
                ["obsStatus"] = obs, ["obsOk"] = Obs.Connected,
                ["ps5Status"] = ps5, ["ps5On"] = Ps5.State == Ps5State.On,
            },
        });
    }
}
