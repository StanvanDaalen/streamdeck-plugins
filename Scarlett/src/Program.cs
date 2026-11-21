using System.Drawing;
using System.Drawing.Imaging;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using GameVolume;
using ScarlettSwitch.Core;

namespace ScarlettSwitch;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string? Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        SettingsStore.LogFile = "scarlett.log";
        if (Arg("--assets") is { } dir) { Plugin.WriteAssets(dir); return 0; }
        if (Arg("--dump") is { } file) { Dump(file); return 0; }

        var port = Arg("-port"); var uuid = Arg("-pluginUUID"); var register = Arg("-registerEvent");
        if (port == null || uuid == null || register == null) return 1;
        try { await new Plugin(int.Parse(port), uuid, register).RunAsync(); }
        catch (Exception e) { SettingsStore.Log("fatal: " + e); return 1; }
        return 0;
    }

    /// <summary>Diagnostic output: what the plugin sees of the Scarlett and the Windows volume.</summary>
    private static void Dump(string file)
    {
        var sb = new StringBuilder();
        var settings = Settings.Load();
        using (var outputs = new OutputController(settings))
        {
            Thread.Sleep(2500);
            sb.AppendLine($"status={outputs.Status} device={outputs.Device?.Name} port={outputs.PortName} output={outputs.Output} speakerMix={settings.SpeakerMix} headphoneMix={settings.HeadphoneMix}");
        }
        using (var master = new EndpointVolume())
        {
            master.Bind();
            sb.AppendLine($"masterVolume={master.Percent} muted={master.Muted}");
        }
        File.WriteAllText(file, sb.ToString());
    }
}

internal sealed class Ctx
{
    public required string Id { get; init; }
    public required string Action { get; init; }
    public JsonObject Settings { get; set; } = new();
    public bool Encoder;
    public string? Layout, ArtId, LastJson, KeyHash;
    public readonly Dictionary<string, string> Sent = new();
}

/// <summary>
/// Scarlett Switch: switches a Focusrite Scarlett between speakers and headphones through Focusrite Control 2
/// (over a MIDI loopback port), and turns the Windows volume on a dial.
/// </summary>
internal sealed class Plugin
{
    private const string SwitchAction = "com.stan.scarlett.switch";
    private const string SpeakersAction = "com.stan.scarlett.speakers";
    private const string HeadphonesAction = "com.stan.scarlett.headphones";
    private const string Standard = "#FFFFFF", Dim = "#777777", MutedBar = "#555555";

    private readonly int _port;
    private readonly string _uuid, _register;
    private readonly ClientWebSocket _ws = new();
    private readonly SemaphoreSlim _gate = new(1, 1), _sendLock = new(1, 1);
    private readonly Dictionary<string, Ctx> _contexts = new();
    private readonly ArtworkService _art = new(new GameLibrary());
    // Started once one of the actions is placed, so an unused plugin never starts Focusrite Control.
    private Settings? _settings;
    private OutputController? _controller;
    private EndpointVolume? _master;
    private Settings _scarlett => _settings!;
    private OutputController _outputs => _controller!;
    private EndpointVolume _masterVolume => _master!;
    private readonly Dictionary<string, string> _covers = new();
    private string? _piContext;
    private LinkStatus? _loggedStatus;
    private bool _masterBound;

    public Plugin(int port, string uuid, string register)
    {
        _port = port; _uuid = uuid; _register = register;
    }

    private void EnsureStarted()
    {
        if (_controller != null) return;
        _settings = Settings.Load();
        _master = new EndpointVolume();
        _master.Changed += () => _ = WithGate(RenderAll);
        _controller = new OutputController(_settings);
        _controller.Changed += () => _ = WithGate(OnOutputChanged);
    }

    public async Task RunAsync()
    {
        await _ws.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}"), CancellationToken.None);
        await SendAsync(new JsonObject { ["event"] = _register, ["uuid"] = _uuid });

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

    private static string Str(Ctx c, string key, string fallback) =>
        c.Settings[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : fallback;

    // ---------- events ----------

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
            EnsureStarted();
            if (c.Encoder && !_masterBound) { _masterVolume.Bind(); _masterBound = true; }
            await RenderAsync(c);
            return;
        }
        if (ev == "willDisappear" && id != null) { _contexts.Remove(id); return; }

        // Keys inside a Multi Action fire keyDown without ever sending willAppear.
        if (ev == "keyDown" && id != null && action != null) { EnsureStarted(); await Press(action, id); return; }
        if (id == null || !_contexts.TryGetValue(id, out var ctx)) return;

        switch (ev)
        {
            case "didReceiveSettings":
                ctx.Settings = payload?["settings"]?.DeepClone() as JsonObject ?? new();
                ctx.KeyHash = null; ctx.LastJson = null; ctx.Sent.Clear();
                await RenderAsync(ctx);
                break;
            case "dialRotate":
                _masterVolume.Adjust(payload!["ticks"]!.GetValue<int>() * _scarlett.VolumeStep);
                await RenderAsync(ctx);
                break;
            case "dialDown":
                await Press(ctx.Action, id);
                break;
            case "touchTap":
                if (payload?["hold"]?.GetValue<bool>() == true) _masterVolume.ToggleMute();
                else await Press(ctx.Action, id);
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

    private async Task Press(string action, string context)
    {
        switch (action)
        {
            case SpeakersAction: _outputs.Set(Output.Speakers); break;
            case HeadphonesAction: _outputs.Set(Output.Headphones); break;
            default: _outputs.Toggle(); break;
        }
        if (_outputs.Status != LinkStatus.Ready)
            await SendAsync(new JsonObject { ["event"] = "showAlert", ["context"] = context });
    }

    private async Task OnOutputChanged()
    {
        if (_outputs.Status != _loggedStatus)
        {
            _loggedStatus = _outputs.Status;
            SettingsStore.Log("scarlett: " + _outputs.Status);
        }
        await RenderAll();
        if (_piContext != null && _contexts.TryGetValue(_piContext, out var c)) await SendPi(c);
    }

    private static string StatusText(LinkStatus s) => s switch
    {
        LinkStatus.Ready => "Connected",
        LinkStatus.NoDevice => "No Scarlett found",
        LinkStatus.UnsupportedDevice => "This Scarlett generation is not supported by Focusrite Control 2",
        LinkStatus.NoFocusriteControl => FocusriteLauncher.Installed
            ? "Focusrite Control 2 is not running (auto start is off)"
            : "Focusrite Control 2 is not installed",
        LinkStatus.NoMidiPort => "MIDI loopback port not found",
        _ => "MIDI error",
    };

    // ---------- drawing ----------

    private async Task RenderAll()
    {
        foreach (var c in _contexts.Values.ToList()) await RenderAsync(c);
    }

    private Task RenderAsync(Ctx c) => !c.Encoder ? RenderKey(c)
        : Str(c, "look", "player") == "player" ? RenderPlayer(c) : RenderClassic(c);

    private Art DeviceArt(bool headphones, bool on)
    {
        var id = $"output:{(headphones ? "hp" : "sp")}:{(on ? "on" : "off")}";
        var color = on ? Color.White : ColorTranslator.FromHtml(Dim);
        return _art.ForGlyph(id, () => headphones ? Glyphs.Headphones(96, color) : Glyphs.Speakers(96, color));
    }

    private static string? IconFile(Ctx c, bool headphones)
    {
        var path = Str(c, headphones ? "iconHeadphones" : "iconSpeakers", "");
        return path.Length > 0 && File.Exists(path) ? path : null;
    }

    private async Task RenderKey(Ctx c)
    {
        bool ready = _outputs.Status == LinkStatus.Ready;
        bool hp = _outputs.Output == Output.Headphones;
        int state = c.Action == SpeakersAction ? (hp ? 0 : 1) : (hp ? 1 : 0);
        var custom = c.Action == SwitchAction ? IconFile(c, hp) : null;
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
            g.DrawImage(DeviceArt(headphones, false).KeyIcon, 18, 18, 108, 108);
        }
        return ArtworkService.DataUrl(bmp);
    }

    /// <summary>The "cover": the active device's icon on a dark rounded tile, like album art.</summary>
    private string Cover(bool headphones, bool ready, string? iconFile)
    {
        var key = $"{headphones}|{ready}|{iconFile}";
        if (_covers.TryGetValue(key, out var url)) return url;
        using var bmp = new Bitmap(96, 96, PixelFormat.Format32bppArgb);
        using (var g = ArtworkService.HighQuality(bmp))
        {
            using var tile = new System.Drawing.Drawing2D.GraphicsPath();
            tile.AddArc(0, 0, 24, 24, 180, 90); tile.AddArc(72, 0, 24, 24, 270, 90);
            tile.AddArc(72, 72, 24, 24, 0, 90); tile.AddArc(0, 72, 24, 24, 90, 90); tile.CloseFigure();
            using var fill = new SolidBrush(Color.FromArgb(0x2E, 0x2E, 0x2E));
            g.FillPath(fill, tile);
            var icon = (iconFile != null ? _art.ForFile(iconFile)?.KeyIcon : null) ?? DeviceArt(headphones, ready).KeyIcon;
            g.DrawImage(icon, 18, 18, 60, 60);
        }
        return _covers[key] = ArtworkService.DataUrl(bmp);
    }

    private async Task SetLayout(Ctx c, string layout)
    {
        if (c.Layout == layout) return;
        c.Layout = layout; c.ArtId = null; c.LastJson = null; c.Sent.Clear();
        await SendAsync(new JsonObject { ["event"] = "setFeedbackLayout", ["context"] = c.Id, ["payload"] = new JsonObject { ["layout"] = layout } });
    }

    /// <summary>Player look: the same design as the media and Spotify dials, with the device name as title.</summary>
    private async Task RenderPlayer(Ctx c)
    {
        await SetLayout(c, "layouts/output-player.json");
        bool ready = _outputs.Status == LinkStatus.Ready;
        bool hp = _outputs.Output == Output.Headphones;
        int vol = _masterVolume.Percent;
        bool muted = _masterVolume.Muted;
        string kind = hp ? "Headphones" : "Speakers";
        string name = Str(c, hp ? "headphonesName" : "speakersName", "");

        string title = !ready ? "Scarlett offline" : name.Length > 0 ? name : kind;
        // With a device name the second line says what it is; without one it names the interface.
        string artist = !ready ? StatusText(_outputs.Status) : name.Length > 0 ? kind : _outputs.Device?.Name ?? "Scarlett";

        var items = new Dictionary<string, JsonObject>
        {
            ["cover"] = new() { ["value"] = Cover(hp, ready, ready ? IconFile(c, hp) : null) },
            ["title"] = new() { ["value"] = title, ["color"] = ready ? Standard : Dim },
            ["artist"] = new() { ["value"] = artist },
            ["speakersIcon"] = new() { ["value"] = Icons.Get("speakers"), ["opacity"] = ready && !hp ? 1 : 0.35 },
            ["headphonesIcon"] = new() { ["value"] = Icons.Get("headphones"), ["opacity"] = ready && hp ? 1 : 0.35 },
            ["volumeIcon"] = new() { ["value"] = Icons.Volume(vol < 0 ? null : vol, muted), ["opacity"] = vol < 0 ? 0 : 1 },
            ["volumeLevel"] = new() { ["value"] = vol < 0 ? "" : muted ? "Mute" : $"{vol}%" },
            ["volumeBar"] = new() { ["value"] = Math.Max(vol, 0), ["bar_fill_c"] = muted ? MutedBar : Standard },
        };

        var payload = new JsonObject();
        foreach (var (key, value) in items)
        {
            var json = value.ToJsonString();
            if (c.Sent.GetValueOrDefault(key) == json) continue;
            c.Sent[key] = json;
            payload[key] = value;
        }
        if (payload.Count > 0) await SendAsync(new JsonObject { ["event"] = "setFeedback", ["context"] = c.Id, ["payload"] = payload });
    }

    /// <summary>Classic look: icon, title, percentage and a volume bar.</summary>
    private async Task RenderClassic(Ctx c)
    {
        await SetLayout(c, "layouts/icon.json");
        bool ready = _outputs.Status == LinkStatus.Ready;
        bool hp = _outputs.Output == Output.Headphones;
        int vol = _masterVolume.Percent;
        bool muted = _masterVolume.Muted;
        string title = ready ? (hp ? "Headphones" : "Speakers") : "Offline";
        if (ready && Str(c, "titleMode", "app") == "custom" && Str(c, "customTitle", "").Trim() is { Length: > 0 } custom) title = custom;
        var iconFile = ready ? IconFile(c, hp) : null;
        var art = (iconFile != null ? _art.ForFile(iconFile) : null) ?? DeviceArt(hp, ready);

        var payload = new JsonObject
        {
            ["title"] = new JsonObject { ["value"] = title, ["color"] = ready ? Standard : Dim },
            ["value"] = new JsonObject { ["value"] = vol < 0 ? "" : muted ? "Mute" : $"{vol}%", ["color"] = Standard },
            ["indicator"] = new JsonObject { ["value"] = Math.Max(vol, 0), ["bar_fill_c"] = muted ? MutedBar : Standard },
        };
        var state = payload.ToJsonString() + art.Id;
        if (state == c.LastJson) return;
        c.LastJson = state;
        if (c.ArtId != art.Id) { c.ArtId = art.Id; payload["icon"] = art.IconUrl; }
        await SendAsync(new JsonObject { ["event"] = "setFeedback", ["context"] = c.Id, ["payload"] = payload });
    }

    // ---------- settings panel ----------

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

        bool perAction = false;
        foreach (var key in new[] { "look", "speakersName", "headphonesName", "iconSpeakers", "iconHeadphones", "titleMode", "customTitle" })
            if (p[key] is { } value) { c.Settings[key] = value.DeepClone(); perAction = true; }
        if (perAction)
        {
            await SendAsync(new JsonObject { ["event"] = "setSettings", ["context"] = c.Id, ["payload"] = c.Settings.DeepClone() });
            c.KeyHash = null; c.LastJson = null; c.ArtId = null; c.Sent.Clear();
        }

        var s = _scarlett;
        bool any = p["reapply"] != null;
        if (p["speakerMix"] is { } a) { s.SpeakerMix = (int)a; any = true; }
        if (p["headphoneMix"] is { } b) { s.HeadphoneMix = (int)b; any = true; }
        if (p["volumeStep"] is { } v) { s.VolumeStep = Math.Clamp((int)v, 1, 10); any = true; }
        if (p["focusriteMode"] is { } f) { s.FocusriteMode = (FocusriteMode)Math.Clamp((int)f, 0, 2); any = true; }
        if (p["midiPort"] is { } m && !string.IsNullOrWhiteSpace((string?)m)) { s.MidiPort = ((string)m!).Trim(); any = true; }
        if (any) { s.Save(); _outputs.Reapply(); }

        await RenderAsync(c);
        await SendPi(c);
    }

    private async Task SendPi(Ctx c)
    {
        var d = _outputs.Device;
        var ports = new JsonArray();
        foreach (var n in MidiOut.GetPortNames()) ports.Add(n);
        await SendAsync(new JsonObject
        {
            ["event"] = "sendToPropertyInspector", ["context"] = c.Id, ["action"] = c.Action,
            ["payload"] = new JsonObject
            {
                ["encoder"] = c.Encoder,
                ["look"] = Str(c, "look", "player"),
                ["speakersName"] = Str(c, "speakersName", ""),
                ["headphonesName"] = Str(c, "headphonesName", ""),
                ["iconSpeakers"] = Str(c, "iconSpeakers", ""),
                ["iconHeadphones"] = Str(c, "iconHeadphones", ""),
                ["titleMode"] = Str(c, "titleMode", "app"),
                ["customTitle"] = Str(c, "customTitle", ""),
                ["status"] = StatusText(_outputs.Status),
                ["ready"] = _outputs.Status == LinkStatus.Ready,
                ["device"] = d == null ? "—" : $"{d.Name} (gen {d.Generation})",
                ["port"] = _outputs.PortName ?? "—",
                ["ports"] = ports,
                ["speakerMix"] = _scarlett.SpeakerMix,
                ["headphoneMix"] = _scarlett.HeadphoneMix,
                ["volumeStep"] = _scarlett.VolumeStep,
                ["focusriteMode"] = (int)_scarlett.FocusriteMode,
                ["midiPort"] = _scarlett.MidiPort,
            },
        });
    }

    // ---------- icons in the Stream Deck app ----------

    public static void WriteAssets(string dir)
    {
        Directory.CreateDirectory(dir);
        void Save(string name, Func<int, Bitmap> make, int s)
        {
            using var a = make(s); a.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
            using var b = make(s * 2); b.Save(Path.Combine(dir, name + "@2x.png"), ImageFormat.Png);
        }
        Func<int, Bitmap> OnBlack(Func<int, Color, Bitmap> glyph, Color c) => s =>
        {
            var k = ArtworkService.Solid(s, s, Color.Black);
            using var g = glyph(s, c);
            using var gr = ArtworkService.HighQuality(k);
            int m = s / 8;
            gr.DrawImage(g, m, m, s - m * 2, s - m * 2);
            return k;
        };
        var off = Color.FromArgb(0x70, 0x70, 0x70);
        Save("plugin", s => Glyphs.Headphones(s, Color.White), 128);
        Save("category", s => Glyphs.Headphones(s, Color.White), 28);
        Save("action-output", s => Glyphs.Headphones(s, Color.White), 20);
        Save("action-speakers", s => Glyphs.Speakers(s, Color.White), 20);
        Save("action-headphones", s => Glyphs.Headphones(s, Color.White), 20);
        Save("key-speakers-on", OnBlack(Glyphs.Speakers, Color.White), 72);
        Save("key-speakers-off", OnBlack(Glyphs.Speakers, off), 72);
        Save("key-headphones-on", OnBlack(Glyphs.Headphones, Color.White), 72);
        Save("key-headphones-off", OnBlack(Glyphs.Headphones, off), 72);
    }
}
