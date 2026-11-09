using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace MonitorPlugin;

internal sealed class Ctx
{
    public required string Id { get; init; }
    public JsonObject Settings { get; set; } = new();
    public string? Hash;
    public CancellationTokenSource? Pending;
    public bool LongFired;
}

internal static class Program
{
    private static readonly ClientWebSocket Ws = new();
    private static readonly SemaphoreSlim Gate = new(1, 1), SendLock = new(1, 1);
    private static readonly Dictionary<string, Ctx> Contexts = new();
    private static readonly Dictionary<string, int> Current = new();
    private static readonly Dictionary<string, DateTime> HoldUntil = new();
    private static HashSet<string> _connected = new();

    private static async Task<int> Main(string[] args)
    {
        string? Arg(string n) { int i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        if (Arg("--assets") is { } assets) { Art.WriteAssets(assets); return 0; }
        Log.Reset();
        Ddc.LoadCache();
        if (Arg("--dump") is { } dump) { Dump(dump); return 0; }

        var port = Arg("-port"); var uuid = Arg("-pluginUUID"); var reg = Arg("-registerEvent");
        if (port == null || uuid == null || reg == null) return 1;

        await Ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), CancellationToken.None);
        await Send(new JsonObject { ["event"] = reg, ["uuid"] = uuid });
        _ = Task.Run(PollLoop);

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

    private static void Dump(string file)
    {
        Ddc.RefreshCaps();
        var read = Ddc.ReadCurrent(Ddc.Known.Keys);
        var sb = new StringBuilder();
        foreach (var (key, c) in Ddc.Known)
            sb.AppendLine($"{Label(key)} | current={(read.TryGetValue(key, out var v) ? Ddc.DefaultInputName(v) : "offline")} | inputs={string.Join(", ", c.Inputs.Select(Ddc.DefaultInputName))} | {key}");
        File.WriteAllText(file, sb.ToString());
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

    private static async Task PollLoop()
    {
        await Locked(() => { Ddc.RefreshCaps(); return Task.CompletedTask; });
        while (Ws.State == WebSocketState.Open)
        {
            await Locked(Poll);
            await Task.Delay(5000);
        }
    }

    private static async Task Poll()
    {
        Ddc.RefreshCaps();
        // Only monitors with a placed button are read, to keep DDC traffic low.
        var used = Contexts.Values.Select(c => Str(c, "monitor", "")).Where(k => k.Length > 0).Distinct().ToList();
        var read = Ddc.ReadCurrent(used);
        _connected = read.Keys.ToHashSet();
        foreach (var (key, value) in read)
            if (value > 0 && DateTime.UtcNow >= HoldUntil.GetValueOrDefault(key)) Current[key] = value;
        foreach (var c in Contexts.Values.ToList()) await Render(c);
    }

    private static string Str(Ctx c, string key, string fallback) =>
        c.Settings[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : fallback;

    private static bool Bool(Ctx c, string key, bool fallback) =>
        c.Settings[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;

    private static string Label(string key)
    {
        var c = Ddc.Known.GetValueOrDefault(key);
        if (c == null) return key;
        if (c.Name.Length > 0) return $"{c.Name} ({c.Model})";
        bool duplicate = Ddc.Known.Values.Count(x => x.Model == c.Model) > 1;
        return duplicate ? $"{c.Model} (scherm {c.Display})" : c.Model;
    }

    // The per-button name is what earlier versions stored; the monitor's own name replaces it.
    private static string MonitorName(Ctx c, string key)
    {
        var caps = Ddc.Known.GetValueOrDefault(key);
        return caps?.Name is { Length: > 0 } name ? name : Str(c, "monitorName", "");
    }

    private static string? Mapped(Ctx c, string map, int code) =>
        (c.Settings[map] as JsonObject)?[code.ToString()]?.GetValue<string>() is { Length: > 0 } s ? s : null;

    private static string InputName(Ctx c, int code) => Mapped(c, "names", code) ?? Ddc.DefaultInputName(code);

    /// <summary>Inputs the button cycles through. Until the user ticks some, only the current one counts, so a press can never land on an empty input.</summary>
    private static List<int> Enabled(Ctx c, string key)
    {
        var caps = Ddc.Known.GetValueOrDefault(key);
        if (caps == null) return new();
        if (c.Settings["enabled"] is JsonArray a)
        {
            var chosen = a.Select(x => x!.GetValue<int>()).ToHashSet();
            return caps.Inputs.Where(chosen.Contains).ToList();
        }
        return Current.TryGetValue(key, out var cur) && caps.Inputs.Contains(cur) ? new() { cur } : new();
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
            await RefreshFor(added);
            await Render(added);
            return;
        }
        if (ev == "willDisappear" && id != null) { Contexts.Remove(id); return; }
        if (id == null || !Contexts.TryGetValue(id, out var c)) return;

        switch (ev)
        {
            case "didReceiveSettings":
                c.Settings = payload?["settings"]?.DeepClone() as JsonObject ?? new();
                await Render(c);
                break;
            case "keyDown": KeyDown(c); break;
            case "keyUp": await KeyUp(c); break;
            case "propertyInspectorDidAppear": await SendPi(c); break;
            case "sendToPlugin": await OnPi(c, payload ?? new()); break;
        }
    }

    private static async Task RefreshFor(Ctx c)
    {
        var key = Str(c, "monitor", "");
        if (key.Length == 0) return;
        var read = Ddc.ReadCurrent(new[] { key });
        _connected.Remove(key);
        if (read.TryGetValue(key, out var v))
        {
            _connected.Add(key);
            if (v > 0) Current[key] = v;
        }
    }

    private static void After(Ctx c, int ms, Func<Task> action)
    {
        var cts = new CancellationTokenSource();
        c.Pending = cts;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(ms, cts.Token); } catch (OperationCanceledException) { return; }
            await Locked(() => cts.IsCancellationRequested ? Task.CompletedTask : action());
        });
    }

    private static void CancelPending(Ctx c) { c.Pending?.Cancel(); c.Pending = null; }

    private static void KeyDown(Ctx c)
    {
        if (Str(c, "previous", "double") != "long") return;
        c.LongFired = false;
        After(c, 500, () => { c.Pending = null; c.LongFired = true; return Step(c, -1); });
    }

    private static async Task KeyUp(Ctx c)
    {
        switch (Str(c, "previous", "double"))
        {
            case "long":
                CancelPending(c);
                if (!c.LongFired) await Step(c, +1);
                break;
            case "double":
                if (c.Pending != null) { CancelPending(c); await Step(c, -1); }
                else After(c, 300, () => { c.Pending = null; return Step(c, +1); });
                break;
            default:
                await Step(c, +1);
                break;
        }
    }

    private static Task Alert(Ctx c) => Send(new JsonObject { ["event"] = "showAlert", ["context"] = c.Id });

    private static async Task Step(Ctx c, int direction)
    {
        var key = Str(c, "monitor", "");
        var list = Enabled(c, key);
        if (key.Length == 0 || list.Count < 2) { await Alert(c); return; }

        int index = list.IndexOf(Current.GetValueOrDefault(key, -1));
        int next = index < 0 ? 0 : (index + direction + list.Count) % list.Count;
        int target = list[next];

        if (!Ddc.SetInput(key, target))
        {
            Log.Write($"switch failed: {Label(key)} -> {Ddc.DefaultInputName(target)}");
            await Alert(c);
            return;
        }
        Log.Write($"switched: {Label(key)} -> {Ddc.DefaultInputName(target)}");
        Current[key] = target;
        HoldUntil[key] = DateTime.UtcNow.AddSeconds(4); // the monitor needs a moment before it reports the new input
        foreach (var x in Contexts.Values.Where(x => Str(x, "monitor", "") == key).ToList()) await Render(x);
    }

    private static async Task Render(Ctx c)
    {
        var key = Str(c, "monitor", "");
        bool chosen = key.Length > 0;
        bool online = chosen && _connected.Contains(key);
        int cur = Current.GetValueOrDefault(key, -1);
        bool showText = Bool(c, "showText", true);

        string? icon = online && cur > 0 ? Mapped(c, "icons", cur) : null;
        var ownName = MonitorName(c, key);
        string line1 = !chosen ? "Kies monitor" : ownName.Length > 0 ? ownName : Ddc.Known.GetValueOrDefault(key)?.Model ?? "Monitor";
        string line2 = !chosen ? "" : !online ? "Niet verbonden" : cur > 0 ? InputName(c, cur) : "";
        if (!showText && online) (line1, line2) = ("", "");

        var hash = $"{icon}|{line1}|{line2}|{online}";
        if (hash == c.Hash) return;
        c.Hash = hash;
        await Send(new JsonObject
        {
            ["event"] = "setImage", ["context"] = c.Id,
            ["payload"] = new JsonObject { ["image"] = Art.Key(icon, line1, line2, online), ["target"] = 0 },
        });
    }

    private static async Task OnPi(Ctx c, JsonObject p)
    {
        if (p["iconLib"] != null)
        {
            await Send(new JsonObject { ["event"] = "sendToPropertyInspector", ["context"] = c.Id, ["payload"] = new JsonObject { ["iconLibrary"] = Art.IconLibrary() } });
            return;
        }
        if (p["piLog"] != null) { Log.Write("pi file: " + p["piLog"]); return; }

        bool changed = false;
        foreach (var field in new[] { "monitor", "previous", "showText", "enabled" })
            if (p[field] is { } value) { c.Settings[field] = value.DeepClone(); changed = true; }
        if (p["monitor"] != null) c.Settings.Remove("enabled");

        if (p["monitorName"] is { } newName)
        {
            var named = Str(c, "monitor", "");
            Ddc.SetName(named, newName.GetValue<string>().Trim());
            c.Settings.Remove("monitorName");
            changed = true;
            foreach (var other in Contexts.Values.Where(x => Str(x, "monitor", "") == named).ToList())
            {
                other.Hash = null;
                await Render(other);
            }
        }

        void SetMapped(string map, string value)
        {
            if (c.Settings[map] is not JsonObject o) c.Settings[map] = o = new JsonObject();
            var code = p["code"]!.ToString();
            if (value.Length == 0) o.Remove(code); else o[code] = value;
            changed = true;
        }
        if (p["inputName"] is { } name) SetMapped("names", name.GetValue<string>().Trim());
        if (p["inputIcon"] is { } path) SetMapped("icons", path.GetValue<string>());

        if (changed)
            await Send(new JsonObject { ["event"] = "setSettings", ["context"] = c.Id, ["payload"] = c.Settings.DeepClone() });
        if (p["refresh"] != null) Ddc.RefreshCaps(force: true);
        await RefreshFor(c);
        c.Hash = null;
        await Render(c);
        await SendPi(c);
    }

    private static async Task SendPi(Ctx c)
    {
        Ddc.RefreshCaps();
        var key = Str(c, "monitor", "");
        var connected = Ddc.WithMonitors(l => l.Select(m => m.Key).ToHashSet());
        var monitors = new JsonArray();
        foreach (var (k, _) in Ddc.Known.OrderBy(x => x.Value.Display))
            monitors.Add(new JsonObject { ["key"] = k, ["label"] = Label(k) + (connected.Contains(k) ? "" : " (niet verbonden)") });

        var inputs = new JsonArray();
        if (Ddc.Known.TryGetValue(key, out var caps))
        {
            var enabled = Enabled(c, key);
            foreach (var code in caps.Inputs)
                inputs.Add(new JsonObject
                {
                    ["code"] = code, ["default"] = Ddc.DefaultInputName(code), ["name"] = Mapped(c, "names", code) ?? "",
                    ["icon"] = Mapped(c, "icons", code) ?? "", ["enabled"] = enabled.Contains(code),
                });
        }
        await Send(new JsonObject
        {
            ["event"] = "sendToPropertyInspector", ["context"] = c.Id,
            ["payload"] = new JsonObject
            {
                ["monitor"] = key, ["monitors"] = monitors, ["inputs"] = inputs,
                ["current"] = Current.GetValueOrDefault(key, -1),
                ["previous"] = Str(c, "previous", "double"),
                ["showText"] = Bool(c, "showText", true),
                ["monitorName"] = MonitorName(c, key),
                ["model"] = Ddc.Known.GetValueOrDefault(key)?.Model ?? "",
            },
        });
    }
}
