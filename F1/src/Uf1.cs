using System.Text.Json;
using System.Text.Json.Nodes;

namespace F1;

/// <summary>
/// Ultimate F1 Viewer, driven through Chromium's remote-debugging port (the workspace starts it with
/// --remote-debugging-port). Its main window has the same restoreLayout/saveLayout functions as its
/// LOAD and SAVE buttons, so layouts behave exactly as when clicked in the app.
/// </summary>
public static class Uf1
{
    public const string Process = "Ultimate-F1Viewer-With-F1MV";

    private static Task<List<JsonObject>> TargetsAsync() => Cdp.PagesAsync(Store.Current.Uf1Port);

    private static bool IsMain(JsonObject t) => ((string?)t["url"] ?? "").Contains("/main/index.html");

    public static async Task<bool> ReadyAsync() => (await TargetsAsync()).Any(IsMain);

    /// <summary>
    /// Makes sure the viewer can be driven: when it runs without the debug port (started outside the
    /// workspace) it is restarted with it; when it does not run, it is started. Its own windows close
    /// in a restart, which is fine before opening a layout (that replaces them anyway).
    /// </summary>
    public static async Task<bool> EnsureAsync()
    {
        // Only restart when the port really does not answer (three tries), not after one slow reply.
        for (int i = 0; i < 3; i++)
        {
            if (await ReadyAsync()) return true;
            await Task.Delay(500);
        }
        var exe = Apps.Uf1Exe;
        // On Windows the running copy tells where it is installed. A Linux AppImage runs from a temporary
        // mount that is gone once it closes, so there the found AppImage itself is started again.
        if (OperatingSystem.IsWindows() || exe == null)
            foreach (var p in Desktops.Find(Process))
            {
                try { if (p.MainModule?.FileName is { } f && !f.Contains("/.mount_")) exe = f; } catch { }
                p.Dispose();
            }
        if (exe == null) return false; // not installed: the viewer is optional
        if (Desktops.IsRunning(Process))
        {
            Store.Log("uf1 draait zonder debugpoort: herstarten met poort");
            await Desktops.CloseAppAsync(Process);
            await Task.Delay(1000);
        }
        if (!File.Exists(exe)) { Store.Log("uf1 niet gevonden: " + exe); return false; }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, $"--remote-debugging-port={Store.Current.Uf1Port}") { UseShellExecute = false });
        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(750);
            if (await ReadyAsync()) { await Task.Delay(1500); return true; } // let its session lookup run once
        }
        Store.Log("uf1 gestart maar debugpoort niet bereikbaar");
        return false;
    }

    /// <summary>Evaluates JavaScript in the main window and returns the value (JSON), or null on failure.</summary>
    private static async Task<JsonNode?> EvalAsync(string expression, bool awaitPromise = true, int timeoutSeconds = 10)
    {
        var main = (await TargetsAsync()).FirstOrDefault(IsMain);
        return main == null ? null : await Cdp.EvalAsync(main, expression, awaitPromise, timeoutSeconds);
    }

    /// <summary>The F1 TV live session as the viewer sees it: (found, session type such as "Race" or "Qualifying").</summary>
    public static async Task<(bool Live, string? Type)> LiveSessionAsync()
    {
        try
        {
            var v = await EvalAsync("JSON.stringify(typeof liveSessionInfo === 'undefined' ? null : liveSessionInfo)");
            var info = (string?)v is { } s ? JsonNode.Parse(s) : null;
            return ((bool?)info?["liveSessionFound"] ?? false, (string?)info?["sessionInfo"]?["sessionType"]);
        }
        catch (Exception e) { Store.Log("uf1 live: " + e.Message); return (false, null); }
    }

    /// <summary>
    /// Waits (at most <paramref name="maxSeconds"/>) until the viewer has looked up the live session,
    /// then tells whether there is one: no fixed long wait when there simply is no live session.
    /// </summary>
    public static async Task<(bool Live, string? Type)> WaitLiveSessionAsync(int maxSeconds = 8)
    {
        for (int i = 0; i < maxSeconds * 2; i++)
        {
            try
            {
                var v = await EvalAsync("JSON.stringify(typeof liveSessionInfo === 'undefined' ? null : liveSessionInfo)");
                var info = (string?)v is { } s ? JsonNode.Parse(s) : null;
                if (info != null) return ((bool?)info["liveSessionFound"] ?? false, (string?)info["sessionInfo"]?["sessionType"]);
            }
            catch { }
            await Task.Delay(500);
        }
        return (false, null);
    }

    /// <summary>F1 TV content id of the live session (as the viewer found it), or null.</summary>
    public static async Task<string?> LiveContentIdAsync()
    {
        try
        {
            var v = await EvalAsync("JSON.stringify(typeof liveSessionInfo === 'undefined' ? null : liveSessionInfo)");
            var info = (string?)v is { } s ? JsonNode.Parse(s) : null;
            return (bool?)info?["liveSessionFound"] == true ? info?["contentInfo"]?["contentId"]?.ToString() : null;
        }
        catch { return null; }
    }

    /// <summary>All layouts saved in the viewer: (id, name, number of viewer windows, number of streams).</summary>
    public static List<(string Id, string Name, int Windows, int Streams)> Layouts()
    {
        try
        {
            var path = LinuxApps.ConfigFile(Process, "config.json");
            var layouts = JsonNode.Parse(File.ReadAllText(path))?["layouts"]?.AsObject();
            return layouts?.Select(kv => (kv.Key, (string?)kv.Value?["name"] ?? kv.Key,
                    (kv.Value?["uf1Windows"] as JsonArray)?.Count ?? 0, (kv.Value?["mvWindows"] as JsonArray)?.Count ?? 0))
                .ToList() ?? new();
        }
        catch (Exception e) { Store.Log("uf1 layouts: " + e.Message); return new(); }
    }

    /// <summary>Layout id for "race" or "quali": the one chosen in the key's settings, else found by name.</summary>
    public static string? FindLayout(string kind)
    {
        var all = Layouts();
        var chosen = kind == "quali" ? Store.Current.QualiLayout : Store.Current.RaceLayout;
        if (chosen != null && all.Any(l => l.Id == chosen)) return chosen;
        var named = all.Select(l => (l.Id, Name: l.Name.ToLowerInvariant())).ToList();
        return kind == "quali"
            ? named.FirstOrDefault(l => l.Name.Contains("qual")).Id
            : (named.FirstOrDefault(l => l.Name == "race").Id ?? named.FirstOrDefault(l => l.Name.Contains("race")).Id);
    }

    /// <summary>
    /// Opens a layout. Without a live session the MultiViewer streams need a content id (a replay
    /// that is open), just like the field in the viewer. Not awaited inside the viewer: before a
    /// session starts it keeps waiting until the session goes live.
    /// </summary>
    public static async Task<bool> RestoreLayoutAsync(string id, string? contentId)
    {
        // Awaited, so a failure in the viewer shows as an alert on the key instead of a false OK.
        // Before a session starts the viewer keeps waiting for it; that wait ends here as a timeout,
        // which is fine: it carries on and opens the streams when the session goes live.
        var js = $"(async () => {{ const f = document.getElementById('content-id-field'); if (f) f.value = {JsonSerializer.Serialize(contentId ?? "")}; " +
                 $"await restoreLayout({int.Parse(id)}); return true; }})()";
        // One retry: the connection can drop while the viewer is still settling after a (re)start.
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try { return (bool?)await EvalAsync(js, awaitPromise: true, timeoutSeconds: 60) == true; }
            catch (OperationCanceledException) { Store.Log("uf1 restore: wacht op sessie"); return true; }
            catch (Exception e)
            {
                Store.Log($"uf1 restore (poging {attempt}): " + e.Message);
                if (attempt == 1)
                {
                    await Task.Delay(2000);
                    await CloseExtraWindowsAsync(); // whatever the first attempt half-opened, so nothing doubles
                }
            }
        }
        return false;
    }

    public static async Task<bool> SaveLayoutAsync(string id)
    {
        try { return (bool?)await EvalAsync($"saveLayout({int.Parse(id)}).then(() => true)") == true; }
        catch (Exception e) { Store.Log("uf1 save: " + e.Message); return false; }
    }

    /// <summary>
    /// Battle mode windows (left and right of the screens): shows exactly these drivers, by clicking the
    /// window's own driver buttons. Returns false while a window has not loaded its driver list yet.
    /// </summary>
    private static readonly Dictionary<string, string> _lastBattle = new();

    /// <summary>
    /// Reloads the battle mode windows. They fetch the driver list once when they load; one that loaded
    /// before the session's timing existed gives up for good, and a reload makes it start over.
    /// </summary>
    public static async Task ReloadBattleModeAsync()
    {
        foreach (var p in (await TargetsAsync()).Where(t => ((string?)t["url"] ?? "").Contains("/battlemode/")))
        {
            try { await Cdp.EvalAsync(p, "location.reload(); true", awaitPromise: false); Store.Log("battle mode opnieuw geladen"); }
            catch (Exception e) { Store.Log("battle mode herladen: " + e.Message); }
        }
    }

    public static async Task<bool> SetBattleDriversAsync(List<int> left, List<int> right)
    {
        var pages = (await TargetsAsync()).Where(t => ((string?)t["url"] ?? "").Contains("/battlemode/")).ToList();
        if (pages.Count == 0) return true;
        var placed = new List<(JsonObject Page, double X)>();
        foreach (var p in pages)
        {
            try { placed.Add((p, (double?)await Cdp.EvalAsync(p, "window.screenX") ?? 0)); }
            catch { }
        }
        placed = placed.OrderBy(x => x.X).ToList();
        bool allReady = true;
        for (int i = 0; i < placed.Count; i++)
        {
            var want = i == placed.Count - 1 && placed.Count > 1 ? right : left;
            if (want.Count == 0) continue;
            // The window makes its driver buttons once, at start. When that was before the session's
            // driver list arrived, the buttons are missing: let it make them again from the current list.
            var js = "(want => { if (typeof toggleDriver !== 'function' || typeof addButtons !== 'function' || typeof driverList === 'undefined' || !driverList) return 'not-ready'; " +
                     "const missing = () => want.some(n => !document.getElementById('button' + n)); " +
                     "if (missing()) { document.querySelectorAll('#buttons .driver-button').forEach(b => b.remove()); addButtons(); } " +
                     "if (missing()) return 'no-buttons'; " +
                     "const shown = [...document.querySelectorAll('.driver')].map(e => parseInt(e.id.replace('driver', ''))); " +
                     "shown.filter(n => !want.includes(n)).forEach(n => toggleDriver(n)); " +
                     "want.filter(n => !shown.includes(n)).forEach(n => toggleDriver(n)); return 'ok'; })(" +
                     JsonSerializer.Serialize(want) + ")";
            var side = i == placed.Count - 1 && placed.Count > 1 ? "rechts" : "links";
            try
            {
                var r = (string?)await Cdp.EvalAsync(placed[i].Page, js) ?? "js-fout";
                if (r != "ok") allReady = false;
                if (r != _lastBattle.GetValueOrDefault(side)) Store.Log($"battle mode {side}: {r} [{string.Join(",", want)}]");
                _lastBattle[side] = r;
            }
            catch (Exception e) { Store.Log("battle mode: " + e.Message); allReady = false; }
        }
        return allReady;
    }

    /// <summary>
    /// Windows like Battle mode and Crash detection open in "Move Mode" (dragging instead of working);
    /// pressing Escape in them switches it off. This does the same, only for windows still in move
    /// mode. Returns how many windows were still loading (to try again).
    /// </summary>
    public static async Task<int> LockWindowsAsync()
    {
        const string js = "(() => { const b = document.getElementById('background'); " +
                          "if (!b || typeof toggleMoveMode !== 'function') return document.readyState === 'complete' ? 'none' : 'loading'; " +
                          "if (b.classList.contains('drag')) { toggleMoveMode(); return 'locked'; } return 'ok'; })()";
        int loading = 0;
        foreach (var page in (await TargetsAsync()).Where(t => !IsMain(t)))
        {
            try
            {
                var r = (string?)await Cdp.EvalAsync(page, js);
                if (r == "loading") loading++;
                else if (r == "locked") Store.Log($"move mode uit: {(string?)page["title"]}");
            }
            catch (Exception e) { Store.Log("move mode: " + e.Message); loading++; }
        }
        return loading;
    }

    /// <summary>Closes the viewer's extra windows (not the main window, not auto camera switching).</summary>
    public static async Task CloseExtraWindowsAsync()
    {
        foreach (var t in (await TargetsAsync()).Where(t => !IsMain(t) && (string?)t["title"] != "Auto Onboard Camera Switching"))
            await Cdp.CloseAsync(Store.Current.Uf1Port, (string)t["id"]!);
    }
}
