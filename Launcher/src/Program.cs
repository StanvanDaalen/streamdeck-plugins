using System.Drawing;
using System.Drawing.Imaging;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GameVolume;

namespace LauncherPlugin;

internal sealed class Ctx
{
    public required string Id { get; init; }
    public required string Action { get; init; }
    public JsonObject Settings { get; set; } = new();
    public string? Hash;
    public bool HoldFired;
    public CancellationTokenSource? HoldTimer;

    // What the key shows now, so the name animation can redraw it without detecting games again.
    public Bitmap? Icon;
    public string Name = "", Time = "";
    public bool Active, Scrolls;
    public int Overflow, Offset;
    public DateTime CycleStart;
}

internal static class Program
{
    private const string CurrentGame = "com.stan.launcher.game";
    private const string FixedGame = "com.stan.launcher.fixedgame";
    private const string App = "com.stan.launcher.app";
    private const string Dim = "#777777";

    private static readonly string ExesPath = Path.Combine(SettingsStore.Dir, "launcher.json");
    private static readonly ClientWebSocket Ws = new();
    private static readonly SemaphoreSlim Gate = new(1, 1), SendLock = new(1, 1);
    private static readonly Dictionary<string, Ctx> Contexts = new();
    private static readonly GameLibrary Library = new();
    private static readonly GameDetector Detector = new(Library);
    private static ArtworkService _art = new(Library);

    private static RunningGame? _game;
    private static int _missedScans;
    private static DateTime _settingsTime, _libraryLoaded = DateTime.MinValue;
    private static Dictionary<string, string> _gameExes = new();
    private static bool _exesDirty;

    private static GlobalSettings G => SettingsStore.Current;

    private static async Task<int> Main(string[] args)
    {
        string? Arg(string n) { int i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        if (Arg("--assets") is { } assets) { WriteAssets(assets); return 0; }
        if (Arg("--scroll-test") is { } testOut)
        {
            using var glyph = Glyphs.Controller(96, Color.White);
            const string name = "Librarian: Tidy Up the Arcane Library!";
            int overflow = NameOverflow(name);
            foreach (var (suffix, offset) in new[] { ("start", 0), ("end", overflow) })
            {
                var url = KeyImage(glyph, name, "12u 05m", true, offset);
                File.WriteAllBytes($"{testOut}-{suffix}.png", Convert.FromBase64String(url[(url.IndexOf(',') + 1)..]));
            }
            using var fits = Glyphs.Controller(96, Color.White);
            var shortUrl = KeyImage(fits, "Fall Guys", "3m", true);
            File.WriteAllBytes($"{testOut}-short.png", Convert.FromBase64String(shortUrl[(shortUrl.IndexOf(',') + 1)..]));
            File.WriteAllText($"{testOut}.txt", $"overflow={overflow} short={NameOverflow("Fall Guys")}");
            return 0;
        }

        // Launcher only reads the shared settings: Audio is the one process that writes them.
        SettingsStore.LogFile = "launcher.log";
        SettingsStore.Load();
        _settingsTime = SettingsStore.FileTime;
        LoadExes();

        var port = Arg("-port"); var uuid = Arg("-pluginUUID"); var reg = Arg("-registerEvent");
        if (port == null || uuid == null || reg == null) return 1;

        await Ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), CancellationToken.None);
        await Send(new JsonObject { ["event"] = reg, ["uuid"] = uuid });
        _ = Task.Run(TickLoop);
        _ = Task.Run(AnimateLoop);

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

    private static void LoadExes()
    {
        try { if (File.Exists(ExesPath)) _gameExes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ExesPath)) ?? new(); }
        catch (Exception e) { SettingsStore.Log("exes: " + e.Message); }
    }

    private static void SaveExes()
    {
        if (!_exesDirty) return;
        _exesDirty = false;
        try { File.WriteAllText(ExesPath, JsonSerializer.Serialize(_gameExes)); }
        catch (Exception e) { SettingsStore.Log("exes save: " + e.Message); }
    }

    private static async Task Locked(Func<Task> f)
    {
        await Gate.WaitAsync();
        try { await f(); }
        catch (Exception e) { SettingsStore.Log("error: " + e); }
        finally { Gate.Release(); }
    }

    private static async Task Send(JsonObject o)
    {
        var bytes = Encoding.UTF8.GetBytes(o.ToJsonString());
        await SendLock.WaitAsync();
        try { if (Ws.State == WebSocketState.Open) await Ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
        finally { SendLock.Release(); }
    }

    private static async Task TickLoop()
    {
        while (Ws.State == WebSocketState.Open)
        {
            await Locked(Tick);
            await Task.Delay(2000);
        }
    }

    private static async Task Tick()
    {
        var time = SettingsStore.FileTime;
        bool settingsChanged = time != _settingsTime;
        if (settingsChanged)
        {
            _settingsTime = time;
            SettingsStore.Reload();
            _art = new ArtworkService(Library); // names or images may have changed
        }
        if (settingsChanged || DateTime.UtcNow - _libraryLoaded > TimeSpan.FromMinutes(5))
        {
            Library.Reload();
            _libraryLoaded = DateTime.UtcNow;
        }

        var found = Detector.FindCurrent(_game);
        if (found == null && _game != null && ++_missedScans < 2) found = _game; // ride out a brief window swap
        else if (found != null) _missedScans = 0;
        _game = found;
        if (_game != null) Remember(_game);

        foreach (var c in Contexts.Values.ToList()) await Render(c);
        SaveExes();
    }

    private static async Task Handle(JsonObject m)
    {
        var ev = m["event"]?.GetValue<string>();
        var id = m["context"]?.GetValue<string>();
        var action = m["action"]?.GetValue<string>();
        var payload = m["payload"] as JsonObject;

        if (ev == "willAppear" && id != null && action != null)
        {
            var added = new Ctx { Id = id, Action = action, Settings = payload?["settings"]?.DeepClone() as JsonObject ?? new() };
            Contexts[id] = added;
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
            case "keyUp": KeyUp(c); break;
            case "propertyInspectorDidAppear": await SendPi(c); break;
            case "sendToPlugin": await OnPi(c, payload ?? new()); break;
        }
    }

    private static string Str(Ctx c, string key, string fallback) =>
        c.Settings[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : fallback;

    private static bool Bool(Ctx c, string key, bool fallback) =>
        c.Settings[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;

    // ---------- press and hold ----------

    private static void KeyDown(Ctx c)
    {
        c.HoldFired = false;
        c.HoldTimer?.Cancel();
        var cts = c.HoldTimer = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(1000, cts.Token); } catch (OperationCanceledException) { return; }
            await Locked(async () =>
            {
                if (cts.IsCancellationRequested || !Contexts.ContainsKey(c.Id)) return;
                c.HoldTimer = null;
                c.HoldFired = true;
                Close(c);
                await Send(new JsonObject { ["event"] = "showOk", ["context"] = c.Id });
            });
        });
    }

    private static void KeyUp(Ctx c)
    {
        c.HoldTimer?.Cancel();
        c.HoldTimer = null;
        if (c.HoldFired) return;
        switch (c.Action)
        {
            case App: PressApp(c); break;
            case FixedGame: PressFixed(c); break;
            default: if (_game != null) ToggleGame(_game); break;
        }
    }

    private static void ToggleGame(RunningGame g)
    {
        if (WindowControl.IsForeground(g)) WindowControl.Minimize(g.Window);
        else WindowControl.Activate(g.Window);
    }

    private static void Start(string target)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception e) { SettingsStore.Log("start: " + e.Message); }
    }

    private static string? LaunchTarget(GameEntry e)
    {
        if (e.Id.StartsWith("steam:")) return "steam://rungameid/" + e.Id[6..];
        if (e.Id.StartsWith("epic:")) return $"com.epicgames.launcher://apps/{e.Id[5..]}?action=launch&silent=true";
        if (e.Id == GameLibrary.Minecraft.Id) return GameLibrary.Minecraft.IconExe;
        return KnownExe(e);
    }

    private static void PressFixed(Ctx c)
    {
        if (FixedEntry(c) is not { } entry) return;
        if (Running(entry) is { } running) ToggleGame(running);
        else if (LaunchTarget(entry) is { } target) Start(target);
    }

    private static string? AppExe(Ctx c, AppWindow? window)
    {
        var app = Str(c, "app", "");
        return window?.ExePath ?? G.AppExes.GetValueOrDefault(app) ?? (Str(c, "appExe", "") is { Length: > 0 } saved ? saved : null);
    }

    private static void PressApp(Ctx c)
    {
        var app = Str(c, "app", "");
        if (app.Length == 0) return;
        if (WindowControl.FindApp(app) is { } window) { WindowControl.ToggleApp(window); return; }
        if (AppExe(c, null) is { } exe && File.Exists(exe)) Start(exe);
    }

    private static void Close(Ctx c)
    {
        if (c.Action == FixedGame)
        {
            if (FixedEntry(c) is { } entry && Running(entry) is { } running) WindowControl.CloseWindows(running.Pids);
            return;
        }
        if (c.Action == CurrentGame)
        {
            if (_game != null) WindowControl.CloseWindows(_game.Pids);
            return;
        }
        var app = Str(c, "app", "");
        if (app.Length == 0) return;
        if (WindowControl.FindApp(app) is { } window) WindowControl.CloseWindows(window.Pids);

        // Apps such as Discord keep running in the tray once their window is gone.
        _ = Task.Run(async () =>
        {
            await Task.Delay(3000);
            if (WindowControl.FindApp(app) != null) return; // a window remains, for example a save prompt
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(app))
                try { p.Kill(); } catch (Exception e) { SettingsStore.Log("close: " + e.Message); } finally { p.Dispose(); }
        });
    }

    // ---------- games ----------

    private static GameEntry? FixedEntry(Ctx c)
    {
        var id = Str(c, "gameId", "");
        return id.Length == 0 ? null : Library.Entries.FirstOrDefault(e => e.Id == id);
    }

    private static RunningGame? Running(GameEntry e) => _game?.Entry.Id == e.Id ? _game : Detector.FindById(e.Id);

    private static void Remember(RunningGame g)
    {
        if (g.ExePath == null || _gameExes.GetValueOrDefault(g.Entry.Id) == g.ExePath) return;
        _gameExes[g.Entry.Id] = g.ExePath;
        _exesDirty = true;
    }

    private static string? KnownExe(GameEntry e)
    {
        if (_gameExes.TryGetValue(e.Id, out var known)) return known;
        var guess = GameLibrary.GuessExe(e);
        if (guess != null)
        {
            _gameExes[e.Id] = guess;
            _exesDirty = true;
        }
        return guess;
    }

    private static string DisplayName(GameEntry e)
    {
        var custom = G.Overrides.GetValueOrDefault(e.Id)?.Name;
        return string.IsNullOrWhiteSpace(custom) ? e.Name : custom;
    }

    private static string Playtime(RunningGame g)
    {
        var t = DateTime.Now - g.Started;
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}u {t.Minutes:00}m" : $"{(int)t.TotalMinutes}m";
    }

    // ---------- rendering ----------

    private static Task Render(Ctx c) => c.Action switch
    {
        App => RenderApp(c),
        FixedGame => RenderFixed(c),
        _ => RenderCurrent(c),
    };

    private static async Task SetImage(Ctx c, string hash, Bitmap icon, string name, string time, bool active, bool scrolls = false)
    {
        if (name != c.Name || scrolls != c.Scrolls)
        {
            c.Overflow = scrolls ? NameOverflow(name) : 0;
            c.Offset = 0;
            c.CycleStart = DateTime.UtcNow;
        }
        (c.Icon, c.Name, c.Time, c.Active, c.Scrolls) = (icon, name, time, active, scrolls);

        if (hash == c.Hash) return;
        c.Hash = hash;
        await SendKey(c);
    }

    private static Task SendKey(Ctx c) => Send(new JsonObject
    {
        ["event"] = "setImage", ["context"] = c.Id,
        ["payload"] = new JsonObject { ["image"] = KeyImage(c.Icon!, c.Name, c.Time, c.Active, c.Offset), ["target"] = 0 },
    });

    // ---------- long names: mostly still on the left, with an occasional slide to the end ----------

    private const double RestSeconds = 8, EndPauseSeconds = 1.5, PixelsPerSecond = 45;

    private static async Task AnimateLoop()
    {
        while (Ws.State == WebSocketState.Open)
        {
            await Task.Delay(80);
            await Locked(AnimateFrame);
        }
    }

    private static async Task AnimateFrame()
    {
        foreach (var c in Contexts.Values.Where(x => x.Overflow > 0 && x.Icon != null).ToList())
        {
            int offset = ScrollOffset(c.Overflow, (DateTime.UtcNow - c.CycleStart).TotalSeconds);
            if (offset == c.Offset) continue;
            c.Offset = offset;
            await SendKey(c);
        }
    }

    private static int ScrollOffset(int overflow, double seconds)
    {
        double slide = overflow / PixelsPerSecond, back = slide / 2;
        double t = seconds % (RestSeconds + slide + EndPauseSeconds + back);
        if (t < RestSeconds) return 0;
        t -= RestSeconds;
        if (t < slide) return (int)(t * PixelsPerSecond);
        t -= slide;
        if (t < EndPauseSeconds) return overflow;
        t -= EndPauseSeconds;
        return Math.Max(0, overflow - (int)(t * PixelsPerSecond * 2));
    }
    private static Task RenderCurrent(Ctx c)
    {
        var name = _game != null ? DisplayName(_game.Entry) : "Geen game";
        var time = _game != null && Bool(c, "showPlaytime", true) ? Playtime(_game) : "";
        var art = _game != null ? _art.ForGame(_game) : _art.NoGame;
        return SetImage(c, $"{art.Id}|{name}|{time}", art.KeyIcon, name, time, _game != null, scrolls: true);
    }

    private static Task RenderFixed(Ctx c)
    {
        var entry = FixedEntry(c);
        RunningGame? running = entry != null ? Running(entry) : null;
        if (running != null) Remember(running);

        var art = _art.NoGame;
        if (entry != null)
            art = _art.ForGame(running ?? new RunningGame
            {
                Entry = entry, Window = IntPtr.Zero, ExePath = KnownExe(entry), Pids = new(), Started = DateTime.Now,
            });

        var name = entry == null ? "Kies een game" : DisplayName(entry);
        var time = running != null && Bool(c, "showPlaytime", true) ? Playtime(running) : "";
        return SetImage(c, $"{art.Id}|{name}|{time}|{running != null}", art.KeyIcon, name, time, running != null, scrolls: true);
    }

    private static async Task RenderApp(Ctx c)
    {
        var app = Str(c, "app", "");
        var window = app.Length > 0 ? WindowControl.FindApp(app) : null;
        if (window?.ExePath != null && window.ExePath != Str(c, "appExe", ""))
        {
            c.Settings["appExe"] = window.ExePath;
            await Send(new JsonObject { ["event"] = "setSettings", ["context"] = c.Id, ["payload"] = c.Settings.DeepClone() });
        }
        var exe = AppExe(c, window);
        var art = (app.Length > 0 && exe != null ? _art.ForExe("app:" + app.ToLowerInvariant(), exe) : null) ?? _art.NoGame;
        var name = app.Length == 0 ? "Kies een app" : AppName(app, exe);
        await SetImage(c, $"{art.Id}|{name}|{window != null}", art.KeyIcon, name, "", window != null);
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

    /// <summary>Key layout: name on top, logo in the middle, and the large playtime at the bottom when there is one.</summary>
    private const int NameLeft = 4, NameWidth = 136;

    private static Font NameFont() => new("Segoe UI Semibold", 19f, GraphicsUnit.Pixel);

    private static StringFormat ScrollFormat()
    {
        var f = new StringFormat(StringFormat.GenericTypographic) { LineAlignment = StringAlignment.Center };
        f.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
        return f;
    }

    /// <summary>How many pixels of the name do not fit on the key.</summary>
    private static int NameOverflow(string name)
    {
        using var bmp = new Bitmap(1, 1);
        using var g = ArtworkService.HighQuality(bmp);
        using var font = NameFont();
        using var fmt = ScrollFormat();
        return Math.Max(0, (int)Math.Ceiling(g.MeasureString(name, font, int.MaxValue, fmt).Width) - NameWidth);
    }

    private static string KeyImage(Bitmap icon, string name, string subtitle, bool active, int offset = 0)
    {
        using var bmp = new Bitmap(144, 144, PixelFormat.Format32bppArgb);
        using (var g = ArtworkService.HighQuality(bmp))
        {
            g.Clear(Color.Black);
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            using var nameFont = NameFont();
            using var subFont = new Font("Segoe UI Semibold", 31f, GraphicsUnit.Pixel);
            using var main = new SolidBrush(active ? Color.White : ColorTranslator.FromHtml(Dim));

            int overflow = NameOverflow(name);
            if (overflow > 0)
            {
                // Too long: start at the left edge and let the animation reveal the rest.
                using var scroll = ScrollFormat();
                g.SetClip(new Rectangle(NameLeft, 3, NameWidth, 26));
                g.DrawString(name, nameFont, main, new RectangleF(NameLeft - offset, 3, NameWidth + overflow + 20, 26), scroll);
                g.ResetClip();
            }
            else g.DrawString(name, nameFont, main, new RectangleF(NameLeft, 3, NameWidth, 26), fmt);

            bool timed = subtitle.Length > 0;
            int size = timed ? 66 : 104;
            g.DrawImage(icon, (144 - size) / 2, timed ? 32 : 34, size, size);
            if (timed) g.DrawString(subtitle, subFont, Brushes.White, new RectangleF(2, 100, 140, 40), fmt);
        }
        return ArtworkService.DataUrl(bmp);
    }

    // ---------- settings panel ----------

    private static async Task OnPi(Ctx c, JsonObject p)
    {
        bool changed = false;
        foreach (var field in new[] { "app", "gameId", "showPlaytime" })
            if (p[field] is { } value) { c.Settings[field] = value.DeepClone(); changed = true; }
        if (changed)
            await Send(new JsonObject { ["event"] = "setSettings", ["context"] = c.Id, ["payload"] = c.Settings.DeepClone() });
        c.Hash = null;
        await Render(c);
        await SendPi(c);
    }

    private static async Task SendPi(Ctx c)
    {
        if (_libraryLoaded == DateTime.MinValue)
        {
            Library.Reload();
            _libraryLoaded = DateTime.UtcNow;
        }

        var names = new SortedSet<string>(G.AppExes.Keys, StringComparer.OrdinalIgnoreCase);
        names.UnionWith(WindowControl.ProcessesWithWindows());
        var apps = new JsonArray();
        foreach (var a in names) apps.Add(a);

        var games = new JsonArray();
        foreach (var e in Library.Entries.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            games.Add(new JsonObject { ["id"] = e.Id, ["name"] = DisplayName(e), ["original"] = e.Name });

        await Send(new JsonObject
        {
            ["event"] = "sendToPropertyInspector", ["context"] = c.Id,
            ["payload"] = new JsonObject
            {
                ["app"] = Str(c, "app", ""), ["gameId"] = Str(c, "gameId", ""),
                ["showPlaytime"] = Bool(c, "showPlaytime", true),
                ["apps"] = apps, ["games"] = games,
            },
        });
    }

    private static void WriteAssets(string dir)
    {
        Directory.CreateDirectory(dir);
        void Save(string name, int size, bool key, Func<int, Color, Bitmap> glyph)
        {
            foreach (var (suffix, px) in new[] { ("", size), ("@2x", size * 2) })
            {
                using var icon = glyph(px, key ? Color.FromArgb(0x70, 0x70, 0x70) : Color.White);
                using var output = new Bitmap(px, px, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(output)) { if (key) g.Clear(Color.Black); g.DrawImage(icon, 0, 0, px, px); }
                output.Save(Path.Combine(dir, name + suffix + ".png"), ImageFormat.Png);
            }
        }
        Save("plugin", 128, false, Glyphs.Controller);
        Save("category", 28, false, Glyphs.Controller);
        Save("action-game", 20, false, Glyphs.Controller);
        Save("action-app", 20, false, Glyphs.Speaker);
        Save("key-game", 72, true, Glyphs.Controller);
        Save("key-app", 72, true, Glyphs.Speaker);
    }
}
