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
        var chosen = kind switch { "quali" => Store.Current.QualiMvSetup, "practice" => Store.Current.PracticeMvSetup, _ => Store.Current.RaceMvSetup };
        if (chosen != null && names.Contains(chosen)) return chosen;
        if (kind == "practice")
            return names.FirstOrDefault(n => n.Contains("practice", StringComparison.OrdinalIgnoreCase) || n.Contains("training", StringComparison.OrdinalIgnoreCase));
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
            await Job.Delay(500);
        }
        if (Desktops.IsRunning(MultiViewer.Process))
        {
            Store.Log("MultiViewer draait zonder debugpoort: herstarten met poort");
            await Desktops.CloseAppAsync(MultiViewer.Process);
            await Job.Delay(1000);
        }
        await Apps.LaunchAsync(MultiViewer.Process, Apps.MultiViewerExe, $"--remote-debugging-port={Store.Current.MvPort}",
            Store.Current.PlaceMultiViewer, t => t == "MultiViewer");
        for (int i = 0; i < 40; i++)
        {
            if (await ReadyAsync()) { await Job.Delay(1000); return true; }
            await Job.Delay(750);
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
                if (r != null && r.StartsWith("error:")) { Store.Log("MultiViewer weigerde de setup: " + r); return r; }
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
          // MultiViewer 2.17+ checks this payload and requires f1.isLive (a boolean, from the same session page).
          const payload = { f1: { sessionInfo: props.sessionInfo, isLive: props.isLive === true }, f1tv: { contentId: props.contentId, streams: props.streams } };

          const copy = JSON.parse(JSON.stringify(setup));
          // MultiViewer saves a player's video quality as { bitrate, width, height } but 2.17 refuses
          // that when the player starts (it wants a string): the player then never registers, its sound
          // cannot be set and the replay timing falls back to the last live session. Leave it out.
          for (const w of copy.windows) {
            const ps = w.state && w.state.playerState;
            if (ps && ps.videoQuality !== undefined && typeof ps.videoQuality !== 'string') delete ps.videoQuality;
            // Always the whole stream: the stream windows are 16:9, so 'cover' would only crop it.
            if (w.type === 'f1tv.player' && w.state) w.state.videoSizing = 'contain';
          }
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
          // MultiViewer replaces the windows a setup has, but leaves other timing windows open. Close the
          // timing windows this setup does not have (e.g. Sectors Leaderboard from the qualifying setup).
          try {
            const keep = new Set(copy.windows.map(w => w.type));
            const open = new Set((await window.f1mv.setupManager.getWindows()).map(w => w.type));
            for (const t of open)
              if (t.startsWith('f1.live-timing.') && !keep.has(t)) await window.f1mv.setupManager.closeWindowsOfType(t);
          } catch (e) { /* older MultiViewer without these calls: just open the setup */ }
          try { await window.f1mv.setupManager.restoreSetup(copy, payload); }
          catch (e) { return 'error: ' + ((e && e.message) || e); } // e.g. MultiViewer changed what it expects
          return 'ok';
        })()
        """;

    // ------------------------------------------------------------ live updates of the slots

    private static List<int>? _lastWanted;

    /// <summary>Set when Race/Quali opened a setup; only then are slots updated (never onboards placed by hand).</summary>
    public static bool Active { get; set; }

    /// <summary>Session type of the setup that is open: "race", "quali" or "practice".</summary>
    public static string Kind { get; set; } = "race";

    // Race: when a new order was first seen (lap), and the lap of the last swap.
    private static int? _changeSeenLap;
    private static int _lastSwapLap = -1;

    /// <summary>
    /// Keeps the best-of-the-rest onboards current: when the two best drivers outside the top teams
    /// change (and stay changed for two checks), the slot players are swapped in place.
    /// </summary>
    /// <param name="force">Right after opening a setup: fill the slots at once, without the two-check wait.</param>
    public static async Task UpdateRestSlotsAsync(bool force = false)
    {
        if (!Active || !Store.Current.RestEnabled || (!force && !Store.Current.RestLive)) return;
        if (Kind == "practice") return; // positions mean little in practice: the slots keep their drivers
        // A swap opens a new player window, and Windows puts it on the desktop that is showing. So swap
        // only while F1's own desktop shows; elsewhere the slot waits until you are back on it.
        if (!OnF1Desktop()) { if (force) Store.Log("best of the rest: niet op het F1-bureaublad, later"); return; }
        if (!await Lock.WaitAsync(force ? 5000 : 0)) { if (force) Store.Log("best of the rest: setup wordt nog geopend"); return; }
        try { await UpdateRestSlotsCoreAsync(force); }
        finally { Lock.Release(); }
    }

    private static bool OnF1Desktop()
    {
        var s = Store.Current;
        if (s.ModeDesktop == "current" || !Guid.TryParse(s.F1DesktopId, out var f1)) return true;
        var (ids, current) = Desktops.Info();
        return ids.Count == 0 || ids[current] == f1;
    }

    private static async Task UpdateRestSlotsCoreAsync(bool force)
    {
        var players = await MultiViewer.SlotPlayersAsync();
        var top = new HashSet<string>(Store.Current.TopTeams, StringComparer.OrdinalIgnoreCase);
        var slots = players.Where(p => p.DriverNumber != null && !top.Contains(p.TeamName ?? "")).OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
        if (slots.Count == 0) { if (force) Store.Log("best of the rest: geen vakken in deze setup"); return; }

        var wanted = (await BestOfRestAsync()).Take(slots.Count).ToList();
        if (wanted.Count < slots.Count)
        {
            if (force) Store.Log($"best of the rest: nog geen posities ({wanted.Count} van {slots.Count})");
            _lastWanted = null; _changeSeenLap = null; return;
        }
        // Who, not in which order: two of them changing places is no reason to reopen their onboards.
        bool sameAsBefore = _lastWanted != null && _lastWanted.ToHashSet().SetEquals(wanted);
        bool stable = force || sameAsBefore;
        _lastWanted = wanted;
        if (!stable) { _changeSeenLap = null; return; }
        var shown = slots.Select(s => s.DriverNumber ?? 0).ToHashSet();
        if (shown.SetEquals(wanted)) { _changeSeenLap = null; return; }

        var timing = force ? null : await MultiViewer.TimingAsync();
        var lines = timing?["TimingData"]?["Lines"] as System.Text.Json.Nodes.JsonObject;
        if (!force && Kind == "race")
        {
            // Calm in the race: the new pair must hold for 3 laps, at most one swap every 5 laps, and
            // nothing under a safety car or red flag (the order is frozen or meaningless then). Every swap
            // opens a new onboard, which takes a moment to load.
            int lap = int.TryParse(timing?["LapCount"]?["CurrentLap"]?.ToString(), out var l) ? l : 0;
            var status = timing?["TrackStatus"]?["Status"]?.ToString() ?? "1";
            if (status is "4" or "5" or "6" or "7") return;
            if (_changeSeenLap == null) { _changeSeenLap = lap; return; }
            if (lap < _changeSeenLap + 3 || (_lastSwapLap >= 0 && lap < _lastSwapLap + 5)) return;
            _lastSwapLap = lap;
        }
        _changeSeenLap = null;

        // A driver still wanted keeps their slot; only a slot whose driver dropped out gets a newcomer.
        var newcomers = new Queue<int>(wanted.Where(w => !shown.Contains(w)));
        var target = slots.Select(s => s.DriverNumber is { } n && wanted.Contains(n) ? n : (newcomers.Count > 0 ? newcomers.Dequeue() : n0(s))).ToList();
        static int n0(MultiViewer.SlotPlayer s) => s.DriverNumber ?? 0;
        bool swapped = false;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].DriverNumber == target[i]) continue;
            // Qualifying: never during a lap; only when the driver in that slot is back in the pits.
            if (!force && Kind == "quali" && lines?[slots[i].DriverNumber?.ToString() ?? ""]?["InPit"]?.ToString() != "true") continue;
            Store.Log($"best of the rest: vak {i + 1} {slots[i].Tla} -> #{target[i]}");
            await MultiViewer.SwapDriverAsync(slots[i], target[i]);
            swapped = true;
        }
        // A new player opens on top of everything; put the stacking right again.
        // The new player lands a pixel or two off and with Windows' own edge: close the seams again.
        if (swapped) { await Job.Delay(1000); await StackAsync(); Seams.Close(); }
    }

    /// <summary>Opening a setup and updating its slots never run at the same time.</summary>
    public static readonly SemaphoreSlim Lock = new(1, 1);

    /// <summary>
    /// The Live Timing window draws its rows at a fixed height, so on a wide, low screen it leaves an
    /// empty strip below the last driver. This zooms its page until the rows reach the bottom of the
    /// window (at most 40% larger, never smaller). Returns what it did per Live Timing window.
    /// </summary>
    /// <param name="fit">False: undo it (normal size, replay bar back).</param>
    public static async Task<string> FitLiveTimingAsync(bool fit = true)
    {
        var done = new List<string>();
        foreach (var page in await Cdp.PagesAsync(Store.Current.MvPort))
        {
            var title = (string?)page["title"] ?? "";
            if (!title.StartsWith("Live Timing", StringComparison.Ordinal) && !title.StartsWith("Replay Live Timing", StringComparison.Ordinal)) continue;
            try { done.Add((string?)await Cdp.EvalAsync(page, fit ? FitScript : UnfitScript, awaitPromise: false, timeoutSeconds: 5) ?? "?"); }
            catch (Exception e) { done.Add("fout: " + e.Message); }
        }
        return done.Count == 0 ? "geen Live Timing" : string.Join(", ", done);
    }

    private const string UnfitScript = """
        (() => {
          document.documentElement.style.zoom = '1';
          for (const bar of document.querySelectorAll('.MuiToolbar-root')) bar.style.display = '';
          return 'normaal';
        })()
        """;

    private const string FitScript = """
        (() => {
          const root = document.documentElement;
          root.style.zoom = '1';
          const h = innerHeight;
          // The replay bar at the bottom (play, timeline) only shows on mouse-over but keeps its 64 px
          // empty the rest of the time; the rows get that space. The replay stays under the Stream Deck keys.
          for (const bar of document.querySelectorAll('.MuiToolbar-root'))
            if (bar.getBoundingClientRect().top > h / 2) bar.style.display = 'none';
          let bottom = 0;
          // Lowest piece of text on the page (the last driver row), ignoring anything as tall as half the window.
          for (const el of document.body.querySelectorAll('*')) {
            if (el.children.length) continue;
            const r = el.getBoundingClientRect();
            if (r.width > 0 && r.height > 0 && r.height < h / 2 && (el.textContent || '').trim()) bottom = Math.max(bottom, r.bottom);
          }
          if (bottom < h / 2) return 'leeg';
          const z = Math.min(1.4, Math.max(1, (h - 4) / bottom));
          root.style.zoom = String(z);
          return 'zoom ' + z.toFixed(3);
        })()
        """;
}
