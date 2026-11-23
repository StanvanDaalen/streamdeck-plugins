using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GameVolume;

/// <summary>
/// The launcher keys: Game Key and App Key. They used to be a separate Launcher plugin; they now run in this
/// plugin and share its game library, game detection and artwork, so a name or logo set anywhere shows everywhere.
/// </summary>
internal sealed class LauncherKeys
{
    public const string GameKey = "com.stan.gamevolume.gamekey";
    public const string AppKey = "com.stan.gamevolume.appkey";
    private const string Dim = "#777777";

    public static bool Handles(string? action) => action is GameKey or AppKey;

    private sealed class Key
    {
        public required string Id { get; init; }
        public required string Action { get; init; }
        public JsonObject Settings { get; set; } = new();
        public string? Hash;
        public bool HoldFired;
        public CancellationTokenSource? HoldTimer;
        public int Clicks;
        public CancellationTokenSource? ClickTimer;
    }

    private static readonly string ExesPath = Path.Combine(SettingsStore.Dir, "launcher.json");

    private readonly GameLibrary _library;
    private readonly GameDetector _detector;
    private readonly ArtworkService _art;
    private readonly Func<JsonObject, Task> _send;
    private readonly Func<Func<Task>, Task> _withGate;
    private readonly Func<string, Task> _logoChanged;
    private readonly Func<string, MediaInfo?> _media;
    private readonly Func<string, bool> _muted;
    private readonly Func<string, string, Task> _appAction;
    private readonly Dictionary<string, Key> _keys = new();
    private RunningGame? _game;
    private Dictionary<string, string> _gameExes = new();
    private bool _exesDirty;

    private static GlobalSettings G => SettingsStore.Current;

    public LauncherKeys(GameLibrary library, GameDetector detector, ArtworkService art,
        Func<JsonObject, Task> send, Func<Func<Task>, Task> withGate, Func<string, Task> logoChanged,
        Func<string, MediaInfo?> media, Func<string, bool> muted, Func<string, string, Task> appAction)
    {
        _library = library; _detector = detector; _art = art;
        _send = send; _withGate = withGate; _logoChanged = logoChanged;
        _media = media; _muted = muted; _appAction = appAction;
        try { if (File.Exists(ExesPath)) _gameExes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ExesPath)) ?? new(); }
        catch (Exception e) { SettingsStore.Log("launcher exes: " + e.Message); }
    }

    private void SaveExes()
    {
        if (!_exesDirty) return;
        _exesDirty = false;
        try { File.WriteAllText(ExesPath, JsonSerializer.Serialize(_gameExes)); }
        catch (Exception e) { SettingsStore.Log("launcher exes save: " + e.Message); }
    }

    // ---------- called by the plugin ----------

    /// <summary>Every tick, with the game the plugin detected.</summary>
    public async Task Tick(RunningGame? game, bool gameChanged)
    {
        _game = game;
        if (_game != null) Remember(_game);
        // The game key's panel follows the game that is running, so its logo can be set right away.
        if (gameChanged) foreach (var k in _keys.Values.Where(k => k.Action == GameKey && !IsFixed(k)).ToList()) await SendPi(k);
        foreach (var k in _keys.Values.ToList()) await Render(k);
        SaveExes();
    }

    /// <summary>Apps whose App Key shows what they play, so the plugin keeps track of their media.</summary>
    public IEnumerable<string> MediaApps() =>
        _keys.Values.Where(k => k.Action == AppKey && Bool(k, "showMedia", true)).Select(k => Str(k, "app", "")).Where(a => a.Length > 0);

    /// <summary>What an app plays (or its mute) changed: draw its App Keys again.</summary>
    public async Task MediaChanged(string app)
    {
        foreach (var k in _keys.Values.Where(k => k.Action == AppKey && Str(k, "app", "").Equals(app, StringComparison.OrdinalIgnoreCase)).ToList())
            await RenderApp(k);
    }

    /// <summary>Names or logos changed: draw every key again.</summary>
    public async Task Redraw()
    {
        foreach (var k in _keys.Values) k.Hash = null;
        foreach (var k in _keys.Values.ToList()) await Render(k);
    }

    public async Task Handle(string ev, string id, string action, JsonObject? payload)
    {
        if (ev == "willAppear")
        {
            var added = new Key { Id = id, Action = action, Settings = payload?["settings"]?.DeepClone() as JsonObject ?? new() };
            _keys[id] = added;
            await Render(added);
            return;
        }
        if (ev == "willDisappear") { _keys.Remove(id); return; }
        if (!_keys.TryGetValue(id, out var k)) return;

        switch (ev)
        {
            case "didReceiveSettings":
                k.Settings = payload?["settings"]?.DeepClone() as JsonObject ?? new();
                await Render(k);
                break;
            case "keyDown": KeyDown(k); break;
            case "keyUp": KeyUp(k); break;
            case "propertyInspectorDidAppear": await SendPi(k); break;
            case "sendToPlugin": await OnPi(k, payload ?? new()); break;
        }
    }

    private static string Str(Key k, string key, string fallback) =>
        k.Settings[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : fallback;

    private static bool Bool(Key k, string key, bool fallback) =>
        k.Settings[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;

    // ---------- press and hold ----------

    private void KeyDown(Key k)
    {
        k.HoldFired = false;
        k.HoldTimer?.Cancel();
        k.HoldTimer = null;
        if (k.Action == AppKey && Str(k, "holdAction", "close") == "none") return;
        var cts = k.HoldTimer = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(1000, cts.Token); } catch (OperationCanceledException) { return; }
            await _withGate(async () =>
            {
                if (cts.IsCancellationRequested || !_keys.ContainsKey(k.Id)) return;
                k.HoldTimer = null;
                k.HoldFired = true;
                k.ClickTimer?.Cancel();
                k.Clicks = 0;
                if (k.Action == AppKey && Str(k, "holdAction", "close") != "close") { await RunApp(k, Str(k, "holdAction", "close")); return; }
                Close(k);
                await _send(new JsonObject { ["event"] = "showOk", ["context"] = k.Id });
            });
        });
    }

    private void KeyUp(Key k)
    {
        k.HoldTimer?.Cancel();
        k.HoldTimer = null;
        if (k.HoldFired) return;
        if (k.Action == AppKey) ClickApp(k);
        else if (IsFixed(k)) PressFixed(k);
        else if (_game != null) ToggleGame(_game);
    }

    /// <summary>A Game Key set to one game; without a game it follows the game that is open.</summary>
    private static bool IsFixed(Key k) => k.Action == GameKey && Str(k, "gameId", "").Length > 0;

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

    private string? LaunchTarget(GameEntry e)
    {
        if (e.Id.StartsWith("steam:")) return "steam://rungameid/" + e.Id[6..];
        if (e.Id.StartsWith("epic:")) return $"com.epicgames.launcher://apps/{e.Id[5..]}?action=launch&silent=true";
        if (e.Id == GameLibrary.Minecraft.Id) return GameLibrary.Minecraft.IconExe;
        return KnownExe(e);
    }

    private void PressFixed(Key k)
    {
        if (FixedEntry(k) is not { } entry) return;
        if (Running(entry) is { } running) ToggleGame(running);
        else if (LaunchTarget(entry) is { } target) Start(target);
    }

    private static string? AppExe(Key k, AppWindow? window)
    {
        var app = Str(k, "app", "");
        return window?.ExePath ?? G.AppExes.GetValueOrDefault(app) ?? (Str(k, "appExe", "") is { Length: > 0 } saved ? saved : null);
    }

    /// <summary>Click and double-click on an App Key, each with its own action (like the App Volume dial).</summary>
    private void ClickApp(Key k)
    {
        var single = Str(k, "singleAction", "window");
        var dbl = Str(k, "doubleAction", "playpause");
        k.ClickTimer?.Cancel();
        k.ClickTimer = null;
        if (dbl == "none") { _ = _withGate(() => RunApp(k, single)); return; }
        if (++k.Clicks >= 2) { k.Clicks = 0; _ = _withGate(() => RunApp(k, dbl)); return; }
        var cts = k.ClickTimer = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(300, cts.Token); } catch (OperationCanceledException) { return; }
            await _withGate(async () =>
            {
                if (cts.IsCancellationRequested || !_keys.ContainsKey(k.Id)) return;
                k.ClickTimer = null;
                k.Clicks = 0;
                await RunApp(k, single);
            });
        });
    }

    private async Task RunApp(Key k, string action)
    {
        var app = Str(k, "app", "");
        if (app.Length == 0 || action == "none") return;
        // A closed app opens first, whatever the gesture would do.
        var procs = System.Diagnostics.Process.GetProcessesByName(app);
        bool running = procs.Length > 0;
        foreach (var p in procs) p.Dispose();
        if (!running && action != "close") { PressApp(k); return; }
        switch (action)
        {
            case "window": PressApp(k); break;
            case "close":
                Close(k);
                await _send(new JsonObject { ["event"] = "showOk", ["context"] = k.Id });
                break;
            default: await _appAction(app, action); break; // playpause, next, previous, mute
        }
    }

    private static void PressApp(Key k)
    {
        var app = Str(k, "app", "");
        if (app.Length == 0) return;
        if (WindowControl.FindApp(app) is { } window) { WindowControl.ToggleApp(window); return; }
        if (AppExe(k, null) is { } exe && File.Exists(exe)) Start(exe);
    }

    private void Close(Key k)
    {
        if (IsFixed(k))
        {
            if (FixedEntry(k) is { } entry && Running(entry) is { } running) WindowControl.CloseWindows(running.Pids);
            return;
        }
        if (k.Action == GameKey)
        {
            if (_game != null) WindowControl.CloseWindows(_game.Pids);
            return;
        }
        var app = Str(k, "app", "");
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

    private GameEntry? FixedEntry(Key k)
    {
        var id = Str(k, "gameId", "");
        return id.Length == 0 ? null : _library.Entries.FirstOrDefault(e => e.Id == id);
    }

    private RunningGame? Running(GameEntry e) => _game?.Entry.Id == e.Id ? _game : _detector.FindById(e.Id);

    private void Remember(RunningGame g)
    {
        if (g.ExePath == null || _gameExes.GetValueOrDefault(g.Entry.Id) == g.ExePath) return;
        _gameExes[g.Entry.Id] = g.ExePath;
        _exesDirty = true;
    }

    private string? KnownExe(GameEntry e)
    {
        // A path remembered by an older version can be an anti-cheat helper, whose icon is not the game's.
        if (_gameExes.TryGetValue(e.Id, out var known) && !GameLibrary.IsHelper(known)) return known;
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
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : $"{(int)t.TotalMinutes}m";
    }

    // ---------- rendering ----------

    private Task Render(Key k) => k.Action == AppKey ? RenderApp(k) : IsFixed(k) ? RenderFixed(k) : RenderCurrent(k);

    private async Task SetImage(Key k, string hash, Bitmap icon, string name, string time, bool active)
    {
        if (hash == k.Hash) return;
        k.Hash = hash;
        await _send(new JsonObject
        {
            ["event"] = "setImage", ["context"] = k.Id,
            ["payload"] = new JsonObject { ["image"] = KeyImage(icon, name, time, active), ["target"] = 0 },
        });
    }

    private Task RenderCurrent(Key k)
    {
        var name = _game == null ? "No game" : Bool(k, "showNamePlaying", true) ? DisplayName(_game.Entry) : "";
        var time = _game != null && Bool(k, "showPlaytime", true) ? Playtime(_game) : "";
        var art = _game != null ? _art.ForGame(_game) : _art.NoGame;
        return SetImage(k, $"{art.Id}|{name}|{time}", art.KeyIcon, name, time, _game != null);
    }

    private Task RenderFixed(Key k)
    {
        var entry = FixedEntry(k);
        RunningGame? running = entry != null ? Running(entry) : null;
        if (running != null) Remember(running);

        var art = _art.NoGame;
        if (entry != null)
            art = _art.ForGame(running ?? new RunningGame
            {
                Entry = entry, Window = IntPtr.Zero, ExePath = KnownExe(entry), Pids = new(), Started = DateTime.Now,
            });

        bool showName = Bool(k, running != null ? "showNamePlaying" : "showName", true);
        var name = entry == null ? "Choose a game" : showName ? DisplayName(entry) : "";
        var time = running != null && Bool(k, "showPlaytime", true) ? Playtime(running) : "";
        return SetImage(k, $"{art.Id}|{name}|{time}|{running != null}", art.KeyIcon, name, time, running != null);
    }

    private async Task RenderApp(Key k)
    {
        var app = Str(k, "app", "");
        if (app.Length > 0 && Bool(k, "showMedia", true) && _media(app) is { } m)
        {
            await RenderPlaying(k, app, m);
            return;
        }
        var window = app.Length > 0 ? WindowControl.FindApp(app) : null;
        if (window?.ExePath != null && window.ExePath != Str(k, "appExe", ""))
        {
            k.Settings["appExe"] = window.ExePath;
            await _send(new JsonObject { ["event"] = "setSettings", ["context"] = k.Id, ["payload"] = k.Settings.DeepClone() });
        }
        var exe = AppExe(k, window);
        var art = (app.Length > 0 && exe != null ? _art.ForExe("app:" + app.ToLowerInvariant(), exe) : null) ?? _art.NoGame;
        var name = app.Length == 0 ? "Choose an app" : Bool(k, "showName", true) ? AppName(app, exe) : "";
        await SetImage(k, $"{art.Id}|{name}|{window != null}", art.KeyIcon, name, "", window != null);
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

    /// <summary>The App Key while its app plays something: picture, title, series, timeline and service logo.</summary>
    private async Task RenderPlaying(Key k, string app, MediaInfo m)
    {
        var exe = AppExe(k, null);
        var art = (exe != null ? _art.ForExe("app:" + app.ToLowerInvariant(), exe) : null) ?? _art.NoGame;
        string? color = null, badge = null;
        if (m.Service is { } s) { color = s.Color; badge = s.Badge; }
        else if (app.Equals("spotify", StringComparison.OrdinalIgnoreCase)) { color = "#1ED760"; badge = "images/media/badge-spotify.svg"; }
        if (!Bool(k, "accentTimeline", true)) color = null;
        if (!Bool(k, "accentLogo", true)) badge = null;
        bool muted = _muted(app);
        // The timeline moves in tenths of a percent, so the key is redrawn at most about once a second.
        int part = m.HasTimeline ? (int)(1000 * m.Position.TotalSeconds / m.Duration.TotalSeconds) : -1;
        var hash = $"media|{m.Title}|{m.Artist}|{m.Playing}|{part}|{m.Cover?.Length}|{color}|{badge}|{muted}";
        if (hash == k.Hash) return;
        k.Hash = hash;
        var image = KeyMedia.Render(m, MediaControl.KeyArtFor(m.Cover), art.IconUrl, color, badge, muted);
        await _send(new JsonObject { ["event"] = "setImage", ["context"] = k.Id, ["payload"] = new JsonObject { ["image"] = image, ["target"] = 0 } });
    }

    private const int NameLeft = 4, NameWidth = 136;
    private const float LargestName = 19f, SmallestName = 15f;

    /// <summary>Key layout: optional name on top, optional large playtime at the bottom, and the logo fills whatever is left.</summary>
    public static string KeyImage(Bitmap icon, string name, string subtitle, bool active)
    {
        using var bmp = new Bitmap(144, 144, PixelFormat.Format32bppArgb);
        using (var g = ArtworkService.HighQuality(bmp))
        {
            g.Clear(Color.Black);
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            using var subFont = new Font("Segoe UI Semibold", 31f, GraphicsUnit.Pixel);
            using var main = new SolidBrush(active ? Color.White : ColorTranslator.FromHtml(Dim));

            int top = 6;
            if (name.Length > 0) top = DrawName(g, name, main, fmt) + 3;
            bool timed = subtitle.Length > 0;
            int bottom = timed ? 98 : 138;
            int size = Math.Min(bottom - top, 128);
            g.DrawImage(icon, (144 - size) / 2, top + (bottom - top - size) / 2, size, size);
            if (timed) g.DrawString(subtitle, subFont, Brushes.White, new RectangleF(2, 100, 140, 40), fmt);
        }
        return ArtworkService.DataUrl(bmp);
    }

    /// <summary>Shrinks a long name to fit, then wraps it onto two lines. Returns the bottom of the text.</summary>
    private static int DrawName(Graphics g, string name, Brush brush, StringFormat singleLine)
    {
        for (float size = LargestName; size >= SmallestName; size--)
        {
            using var font = new Font("Segoe UI Semibold", size, GraphicsUnit.Pixel);
            if (g.MeasureString(name, font, int.MaxValue, singleLine).Width > NameWidth) continue;
            g.DrawString(name, font, brush, new RectangleF(NameLeft, 3, NameWidth, 26), singleLine);
            return 29;
        }
        using var small = new Font("Segoe UI Semibold", SmallestName, GraphicsUnit.Pixel);
        using var wrapped = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord };
        g.DrawString(name, small, brush, new RectangleF(NameLeft, 2, NameWidth, 38), wrapped);
        return 40;
    }

    // ---------- settings panel ----------

    private async Task OnPi(Key k, JsonObject p)
    {
        bool changed = false;
        foreach (var field in new[] { "app", "gameId", "showPlaytime", "showName", "showNamePlaying", "showMedia", "accentTimeline", "accentLogo", "singleAction", "doubleAction", "holdAction" })
            if (p[field] is { } value) { k.Settings[field] = value.DeepClone(); changed = true; }
        if (changed)
            await _send(new JsonObject { ["event"] = "setSettings", ["context"] = k.Id, ["payload"] = k.Settings.DeepClone() });

        // The logo is stored per game in the shared game library, so every key and dial shows it too.
        if (p["customImage"] is JsonValue image && LogoTarget(k) is { } gameId)
        {
            if (!G.Overrides.TryGetValue(gameId, out var o)) G.Overrides[gameId] = o = new GameOverride();
            var path = image.GetValue<string>();
            o.Image = path.Length > 0 ? SettingsStore.KeepLogo(gameId, path) : null;
            SettingsStore.TouchOverride(gameId);
            SettingsStore.SaveIfDirty();
            await _logoChanged(gameId);
        }
        k.Hash = null;
        await Render(k);
        await SendPi(k);
    }

    /// <summary>The game whose logo this key's panel edits: the chosen game, or for a following key the one running now.</summary>
    private string? LogoTarget(Key k) =>
        k.Action == AppKey ? null : IsFixed(k) ? Str(k, "gameId", "") : _game?.Entry.Id;

    private async Task SendPi(Key k)
    {
        var names = new SortedSet<string>(G.AppExes.Keys, StringComparer.OrdinalIgnoreCase);
        names.UnionWith(WindowControl.ProcessesWithWindows());
        var apps = new JsonArray();
        foreach (var a in names) apps.Add(a);

        var games = new JsonArray();
        foreach (var e in _library.Entries.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            games.Add(new JsonObject { ["id"] = e.Id, ["name"] = DisplayName(e), ["original"] = e.Name });

        var target = LogoTarget(k);
        await _send(new JsonObject
        {
            ["event"] = "sendToPropertyInspector", ["context"] = k.Id,
            ["payload"] = new JsonObject
            {
                ["app"] = Str(k, "app", ""), ["gameId"] = Str(k, "gameId", ""),
                ["logoFor"] = target != null && _library.Entries.FirstOrDefault(e => e.Id == target) is { } t ? DisplayName(t) : "",
                ["customImage"] = target != null ? G.Overrides.GetValueOrDefault(target)?.Image ?? "" : "",
                ["showPlaytime"] = Bool(k, "showPlaytime", true),
                ["showName"] = Bool(k, "showName", true),
                ["showNamePlaying"] = Bool(k, "showNamePlaying", true),
                ["showMedia"] = Bool(k, "showMedia", true),
                ["accentTimeline"] = Bool(k, "accentTimeline", true),
                ["accentLogo"] = Bool(k, "accentLogo", true),
                ["singleAction"] = Str(k, "singleAction", "window"),
                ["doubleAction"] = Str(k, "doubleAction", "playpause"),
                ["holdAction"] = Str(k, "holdAction", "close"),
                ["apps"] = apps, ["games"] = games,
            },
        });
    }
}
