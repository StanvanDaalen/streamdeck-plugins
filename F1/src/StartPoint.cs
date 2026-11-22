using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace F1;

/// <summary>
/// Where Race and Quali start: where you left off in that session, else a minute before the session starts,
/// also when it is already running (you watch it from the start instead of halfway). Positions are kept as the
/// real-world time on screen (MultiViewer's live timing clock), which is the same for a live stream and its
/// replay, in %APPDATA%\StanF1\resume.json.
/// </summary>
public static class StartPoint
{
    private static readonly string FilePath = Path.Combine(Store.Dir, "resume.json");
    private static readonly object Lock = new();
    private static Dictionary<string, Spot>? _spots;
    private static DateTime _lastSave = DateTime.MinValue;

    /// <summary>A place in a session: the real-world time on screen (UTC ms) and when it was seen.</summary>
    public sealed record Spot(long TrackMs, DateTime Seen);

    /// <summary>A start this far before the session start.</summary>
    private static readonly TimeSpan Lead = TimeSpan.FromMinutes(1);
    /// <summary>Where you left off is kept this long (a replay watched over a weekend, not a month later).</summary>
    private static readonly TimeSpan Keep = TimeSpan.FromDays(14);

    private static Dictionary<string, Spot> Spots
    {
        get
        {
            if (_spots != null) return _spots;
            try { _spots = File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, Spot>>(File.ReadAllText(FilePath)) : null; }
            catch { }
            return _spots ??= new();
        }
    }

    /// <summary>The main stream's session, the moment on screen and whether it plays (null when unknown).</summary>
    private static async Task<(string Content, string PlayerId, long TrackMs, bool Paused)?> NowAsync()
    {
        var d = await MultiViewer.QueryAsync(
            "{ f1LiveTimingClock { paused systemTime trackTime } " +
            "players { id type state { paused } streamData { contentId title } driverData { tla } } }");
        var clock = d?["f1LiveTimingClock"];
        if (!long.TryParse(clock?["trackTime"]?.ToString(), out var track) || track <= 0) return null;
        bool clockPaused = (bool?)clock?["paused"] ?? false;
        if (!clockPaused && long.TryParse(clock?["systemTime"]?.ToString(), out var sys))
            track += DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - sys; // the clock runs on from its last tick
        var main = (d?["players"] as JsonArray)?.OfType<JsonObject>()
            .Where(p => (string?)p["type"] != "OBC" && p["driverData"] == null && p["streamData"]?["contentId"] != null)
            .OrderByDescending(p => (string?)p["streamData"]?["title"] == "F1 LIVE")
            .FirstOrDefault();
        if (main == null) return null;
        return ((string)main["streamData"]!["contentId"]!.ToString(), (string)main["id"]!, track, (bool?)main["state"]?["paused"] ?? false);
    }

    /// <summary>Every few seconds while F1 runs: remember where the main stream is (saved at most every 30 s).</summary>
    public static async Task RememberAsync()
    {
        try
        {
            if (await NowAsync() is not { } now || now.Paused) return;
            lock (Lock)
            {
                Spots[now.Content] = new Spot(now.TrackMs, DateTime.UtcNow);
                if (DateTime.UtcNow - _lastSave < TimeSpan.FromSeconds(30)) return;
                _lastSave = DateTime.UtcNow;
                foreach (var old in Spots.Where(kv => DateTime.UtcNow - kv.Value.Seen > Keep).Select(kv => kv.Key).ToList()) Spots.Remove(old);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Spots));
            }
        }
        catch { }
    }

    /// <summary>The session's start (UTC ms): the scheduled start, or when the timing says it started if earlier.</summary>
    private static async Task<long?> SessionStartAsync()
    {
        var d = await MultiViewer.QueryAsync("{ f1LiveTimingState { SessionInfo SessionData } }");
        var info = d?["f1LiveTimingState"]?["SessionInfo"];
        long? start = null;
        if (DateTime.TryParse((string?)info?["StartDate"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var local)
            && TimeSpan.TryParse(((string?)info?["GmtOffset"] ?? "0:00:00").TrimStart('+'), CultureInfo.InvariantCulture, out var off))
            start = new DateTimeOffset(local - off, TimeSpan.Zero).ToUnixTimeMilliseconds();
        IEnumerable<JsonNode?> series = d?["f1LiveTimingState"]?["SessionData"]?["StatusSeries"] switch
        {
            JsonArray a => a,
            JsonObject o => o.Select(kv => kv.Value),
            _ => Array.Empty<JsonNode?>(),
        };
        foreach (var s in series)
        {
            if ((string?)s?["SessionStatus"] != "Started") continue;
            if (DateTimeOffset.TryParse((string?)s?["Utc"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
                && (start == null || t.ToUnixTimeMilliseconds() < start)) start = t.ToUnixTimeMilliseconds();
        }
        return start;
    }

    /// <summary>
    /// Right after Race/Quali opened a session (players running, timing loaded): to where you left off, else to a
    /// minute before the start (forward past a pre-session show too).
    /// </summary>
    public static async Task ApplyAsync()
    {
        try
        {
            if (await NowAsync() is not { } now) { Store.Log("startpunt: geen klok of stream"); return; }
            Spot? spot;
            lock (Lock) Spots.TryGetValue(now.Content, out spot);
            long target;
            string why;
            if (spot != null)
            {
                target = spot.TrackMs;
                why = "waar je was";
            }
            else if (await SessionStartAsync() is { } start)
            {
                target = start - (long)Lead.TotalMilliseconds;
                why = "1 minuut voor de start";
            }
            else { Store.Log("startpunt: starttijd onbekend"); return; }

            var jump = (target - now.TrackMs) / 1000.0;
            // Back, or ahead (a replay that opens with a pre-session show); a live stream stops at its live edge.
            if (Math.Abs(jump) <= 5) { Store.Log($"startpunt ({why}): al goed ({jump:0} s)"); return; }
            Store.Log($"startpunt: {why}, {jump:0} s");
            await MultiViewer.SeekRelativeAsync(now.PlayerId, jump);
            await Job.Delay(2500);
            await MultiViewer.SyncAsync(now.PlayerId); // the onboards follow
        }
        catch (Exception e) { Store.Log("startpunt: " + e.Message); }
    }
}
