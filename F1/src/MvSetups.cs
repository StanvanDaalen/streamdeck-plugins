using System.Text.Json;
using System.Text.Json.Nodes;

namespace F1;

/// <summary>
/// MultiViewer's own setups (players + live timing windows), opened the way its "Open setup" dialog
/// does: window.f1mv.setupManager.restoreSetup(setup, payload) in the session window, reached through
/// the remote-debugging port MultiViewer is started with. "Best of the rest" onboard slots are filled
/// with the best-placed drivers outside the top teams before the setup opens, and kept up to date.
/// </summary>
public static class MvSetups
{
    private static string SetupsFile => LinuxApps.ConfigFile("MultiViewer", "setups.config.json");

    public static List<(string Name, int Windows, int Players)> List()
    {
        try
        {
            var setups = JsonNode.Parse(File.ReadAllText(SetupsFile))?["setups"]?.AsArray();
            return setups?.OfType<JsonObject>().Select(s =>
            {
                var w = s["windows"]?.AsArray() ?? new JsonArray();
                return ((string?)s["name"] ?? "?", w.Count, w.Count(x => (string?)x?["type"] == "f1tv.player"));
            }).ToList() ?? new();
        }
        catch (Exception e) { Store.Log("mv setups: " + e.Message); return new(); }
    }

    /// <summary>Setup name for "race" or "quali": the one chosen in the key's settings, else found by name.</summary>
    public static string? Find(string kind)
    {
        var names = List().Select(s => s.Name).ToList();
        var chosen = kind == "quali" ? Store.Current.QualiMvSetup : Store.Current.RaceMvSetup;
        if (chosen != null && names.Contains(chosen)) return chosen;
        return kind == "quali"
            ? names.FirstOrDefault(n => n.Contains("qual", StringComparison.OrdinalIgnoreCase))
            : names.FirstOrDefault(n => n.Equals("race", StringComparison.OrdinalIgnoreCase))
              ?? names.FirstOrDefault(n => n.Contains("race", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<bool> ReadyAsync() => (await Cdp.PagesAsync(Store.Current.MvPort)).Count > 0;

    /// <summary>
    /// MultiViewer with its debug port: started when it is not running, restarted when it runs without
    /// the port (Race/Quali open their session themselves afterwards, so nothing is lost).
    /// </summary>
    public static async Task<bool> EnsureAsync()
    {
        for (int i = 0; i < 3; i++)
        {
            if (await ReadyAsync()) return true;
            await Task.Delay(500);
        }
        if (Desktops.IsRunning(MultiViewer.Process))
        {
            Store.Log("MultiViewer draait zonder debugpoort: herstarten met poort");
            await Desktops.CloseAppAsync(MultiViewer.Process);
            await Task.Delay(1000);
        }
        await Apps.LaunchAsync(MultiViewer.Process, Apps.MultiViewerExe, $"--remote-debugging-port={Store.Current.MvPort}",
            Store.Current.PlaceMultiViewer, t => t == "MultiViewer");
        for (int i = 0; i < 40; i++)
        {
            if (await ReadyAsync()) { await Task.Delay(1000); return true; }
            await Task.Delay(750);
        }
        Store.Log("MultiViewer gestart maar debugpoort niet bereikbaar");
        return false;
    }

    /// <summary>
    /// Stacks MultiViewer's player and live timing windows by height (higher = in front). Its main window
    /// (session page or home) is recognised by its address, not its title, and left alone.
    /// </summary>
    public static async Task<int> StackAsync()
    {
        var skip = (await Cdp.PagesAsync(Store.Current.MvPort))
            .Where(p => ((string?)p["url"] ?? "") is var u && !u.Contains("/player") && !u.Contains("/live-timing"))
            .Select(p => (string?)p["title"] ?? "").Where(t => t.Length > 0).ToHashSet();
        return Apps.StackByHeight(skip);
    }

    /// <summary>The session page's content id, or null when no session is open in MultiViewer.</summary>
    public static async Task<string?> OpenContentAsync()
    {
        const string js = """
            (() => {
              for (const el of document.querySelectorAll('button, [role=button], a')) {
                const k = Object.keys(el).find(k => k.startsWith('__reactFiber$'));
                for (let f = k && el[k]; f; f = f.return) {
                  const p = f.memoizedProps;
                  if (p && p.contentId && Array.isArray(p.streams)) return String(p.contentId);
                }
              }
              return null;
            })()
            """;
        foreach (var page in await Cdp.PagesAsync(Store.Current.MvPort))
        {
            try { if ((string?)await Cdp.EvalAsync(page, js, awaitPromise: false) is { } id) return id; }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Driver numbers of the best-placed drivers outside the top teams, best first (empty when the
    /// timing has no positions yet, e.g. before the session starts).
    /// </summary>
    public static async Task<List<int>> BestOfRestAsync()
    {
        var timing = await MultiViewer.TimingAsync();
        var lines = timing?["TimingData"]?["Lines"] as JsonObject;
        var drivers = timing?["DriverList"] as JsonObject;
        if (lines == null || drivers == null) return new();
        var top = new HashSet<string>(Store.Current.TopTeams, StringComparer.OrdinalIgnoreCase);
        return lines
            .Select(kv => (Num: kv.Key, Line: kv.Value))
            .Where(x => int.TryParse(x.Line?["Position"]?.ToString(), out _))
            .Where(x => (bool?)x.Line?["Retired"] != true && (bool?)x.Line?["Stopped"] != true)
            .Where(x => !top.Contains((string?)drivers[x.Num]?["TeamName"] ?? ""))
            .OrderBy(x => int.Parse(x.Line!["Position"]!.ToString()))
            .Select(x => int.TryParse(x.Num, out var n) ? n : -1)
            .Where(n => n > 0)
            .ToList();
    }

    /// <summary>
    /// Opens a setup for the session open in MultiViewer. Returns "ok", "no-setup", "no-session"
    /// (no session window open) or "no-port" (MultiViewer runs without the debug port).
    /// </summary>
    public static async Task<string> RestoreAsync(string name, List<int> rest)
    {
        var pages = await Cdp.PagesAsync(Store.Current.MvPort);
        if (pages.Count == 0) return "no-port";
        var js = RestoreScript
            .Replace("__NAME__", JsonSerializer.Serialize(name))
            .Replace("__REST__", JsonSerializer.Serialize(Store.Current.RestEnabled ? rest : new List<int>()))
            .Replace("__TOP__", JsonSerializer.Serialize(Store.Current.TopTeams));
        foreach (var page in pages)
        {
            try
            {
                var r = (string?)await Cdp.EvalAsync(page, js, awaitPromise: true, timeoutSeconds: 60);
                if (r is "ok" or "no-setup") return r;
            }
            catch (OperationCanceledException) { return "ok"; } // still opening windows; that carries on
            catch (Exception e) { Store.Log("mv restore: " + e.Message); }
        }
        return "no-session";
    }

    // Runs inside a MultiViewer window. Finds the session page's props (contentId, streams,
    // sessionInfo) through React, swaps the best-of-the-rest slots and restores the setup.
    private const string RestoreScript = """
        (async () => {
          const name = __NAME__, rest = __REST__, topTeams = __TOP__.map(t => t.toLowerCase());
          if (!window.f1mv || !window.f1mv.setupManager) return 'no-api';
          const setups = await window.f1mv.setupManager.getSetups();
          const setup = (setups || []).find(s => (s.name || '').toLowerCase() === name.toLowerCase());
          if (!setup) return 'no-setup';

          let props = null;
          const seen = new Set();
          const visit = fiber => {
            for (let f = fiber; f && !props; f = f.return) {
              if (seen.has(f)) break; seen.add(f);
              const p = f.memoizedProps;
              if (p && p.contentId && Array.isArray(p.streams)) props = p;
            }
          };
          for (const el of document.querySelectorAll('button, [role=button], a')) {
            const k = Object.keys(el).find(k => k.startsWith('__reactFiber$'));
            if (k) visit(el[k]);
            if (props) break;
          }
          if (!props) return 'no-session';
          const payload = { f1: { sessionInfo: props.sessionInfo }, f1tv: { contentId: props.contentId, streams: props.streams } };

          const copy = JSON.parse(JSON.stringify(setup));
          if (rest.length) {
            const slots = copy.windows
              .filter(w => w.type === 'f1tv.player' && w.state && w.state.driverData &&
                           !topTeams.includes(String(w.state.driverData.teamName || '').toLowerCase()))
              .sort((a, b) => (a.windowState.bounds.y - b.windowState.bounds.y) || (a.windowState.bounds.x - b.windowState.bounds.x));
            slots.forEach((w, i) => {
              const s = props.streams.find(s => String(s.racingNumber) === String(rest[i]));
              if (!s) return;
              w.state.driverData = { ...w.state.driverData, driverNumber: s.racingNumber, tla: s.title, firstName: undefined, lastName: undefined };
              w.state.streamData = { ...(w.state.streamData || {}), title: s.title, channelId: s.channelId };
            });
          }
          await window.f1mv.setupManager.restoreSetup(copy, payload);
          return 'ok';
        })()
        """;

    // ------------------------------------------------------------ live updates of the slots

    private static List<int>? _lastWanted;

    /// <summary>Set when Race/Quali opened a setup; only then are slots updated (never onboards placed by hand).</summary>
    public static bool Active { get; set; }

    /// <summary>
    /// Keeps the best-of-the-rest onboards current: when the two best drivers outside the top teams
    /// change (and stay changed for two checks), the slot players are swapped in place.
    /// </summary>
    /// <param name="force">Right after opening a setup: fill the slots at once, without the two-check wait.</param>
    public static async Task UpdateRestSlotsAsync(bool force = false)
    {
        if (!Active || !Store.Current.RestEnabled || (!force && !Store.Current.RestLive)) return;
        if (!await Lock.WaitAsync(force ? 5000 : 0)) return; // a setup is being opened right now
        try { await UpdateRestSlotsCoreAsync(force); }
        finally { Lock.Release(); }
    }

    private static async Task UpdateRestSlotsCoreAsync(bool force)
    {
        var players = await MultiViewer.SlotPlayersAsync();
        var top = new HashSet<string>(Store.Current.TopTeams, StringComparer.OrdinalIgnoreCase);
        var slots = players.Where(p => p.DriverNumber != null && !top.Contains(p.TeamName ?? "")).OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
        if (slots.Count == 0) return;

        var wanted = (await BestOfRestAsync()).Take(slots.Count).ToList();
        if (wanted.Count < slots.Count) { _lastWanted = null; return; }
        bool stable = force || (_lastWanted != null && _lastWanted.SequenceEqual(wanted));
        _lastWanted = wanted;
        if (!stable) return;

        // Slot i (top-left first) shows the i-th best; this also reorders when two of them swap places.
        bool swapped = false;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].DriverNumber == wanted[i]) continue;
            Store.Log($"best of the rest: vak {i + 1} {slots[i].Tla} -> #{wanted[i]}");
            await MultiViewer.SwapDriverAsync(slots[i], wanted[i]);
            swapped = true;
        }
        // A new player opens on top of everything; put the stacking right again.
        if (swapped) { await Task.Delay(1000); await StackAsync(); }
    }

    /// <summary>Opening a setup and updating its slots never run at the same time.</summary>
    public static readonly SemaphoreSlim Lock = new(1, 1);
}
