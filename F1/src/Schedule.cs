using System.Globalization;
using System.Text.Json.Nodes;

namespace F1;

/// <summary>One F1 session from the calendar.</summary>
public sealed record CalSession(string Name, string Type, string Location, DateTimeOffset Start, DateTimeOffset End, int Meeting, int Key = 0);

/// <summary>
/// The F1 calendar from OpenF1 (public): the next session for the Session key and, for the Race and Quali
/// keys, which race or qualifying they would open (and which replay to look for on F1 TV). Refreshed every 6
/// hours and kept in %APPDATA%\StanF1\schedule.json: OpenF1 shuts out everyone without a paid key while a
/// session is live, exactly when the keys need it, so the saved copy carries over (also after a restart).
/// </summary>
public static class Schedule
{
    private static readonly HttpClient Http = CreateClient();
    private static List<CalSession> _sessions = new();
    private static DateTime _loaded = DateTime.MinValue;
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static readonly string CachePath = Path.Combine(Store.Dir, "schedule.json");
    private static readonly TimeSpan Every = TimeSpan.FromHours(6), RetryAfter = TimeSpan.FromMinutes(10);
    private static bool _cacheRead;

    /// <summary>The saved copy, once, when nothing is loaded yet.</summary>
    private static void ReadCache()
    {
        if (_cacheRead) return;
        _cacheRead = true;
        try
        {
            if (!File.Exists(CachePath)) return;
            var list = System.Text.Json.JsonSerializer.Deserialize<List<CalSession>>(File.ReadAllText(CachePath));
            if (list is { Count: > 0 } && _sessions.Count == 0)
            {
                _sessions = list.OrderBy(s => s.Start).ToList();
                // a copy from less than 6 hours ago needs no update now
                _loaded = File.GetLastWriteTimeUtc(CachePath);
            }
        }
        catch (Exception e) { Store.Log("kalender (opgeslagen): " + e.Message); }
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 StreamDeckF1");
        return c;
    }

    /// <summary>Loads the calendar when it is older than 6 hours (quietly keeps the one it has on failure).</summary>
    public static async Task RefreshAsync()
    {
        ReadCache();
        if (DateTime.UtcNow - _loaded < Every) return;
        if (!await Lock.WaitAsync(0)) return;
        try
        {
            var list = new List<CalSession>();
            int year = DateTime.UtcNow.Year;
            foreach (var y in new[] { year - 1, year, year + 1 })
            {
                var arr = JsonNode.Parse(await Http.GetStringAsync($"https://api.openf1.org/v1/sessions?year={y}"))?.AsArray();
                foreach (var s in arr?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                {
                    if ((bool?)s["is_cancelled"] == true) continue;
                    if (!DateTimeOffset.TryParse((string?)s["date_start"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
                        !DateTimeOffset.TryParse((string?)s["date_end"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)) continue;
                    list.Add(new CalSession((string?)s["session_name"] ?? "", (string?)s["session_type"] ?? "",
                        (string?)s["location"] ?? (string?)s["circuit_short_name"] ?? "", start, end, (int?)s["meeting_key"] ?? 0,
                        (int?)s["session_key"] ?? 0));
                }
                if (y == year + 1 && list.Count == 0) break;
            }
            if (list.Count > 0)
            {
                _sessions = list.OrderBy(s => s.Start).ToList(); _loaded = DateTime.UtcNow;
                try { File.WriteAllText(CachePath, System.Text.Json.JsonSerializer.Serialize(_sessions)); } catch { }
            }
        }
        catch (Exception e)
        {
            // OpenF1 answers 401 to everyone while a session is live: keep the calendar we have, try later
            Store.Log("kalender: " + e.Message + (_sessions.Count > 0 ? " (opgeslagen kalender blijft)" : ""));
            _loaded = DateTime.UtcNow - Every + RetryAfter;
        }
        finally { Lock.Release(); }
    }

    private static bool IsKind(CalSession s, string kind) => kind == "quali" ? s.Name == "Qualifying" : s.Name == "Race";

    /// <summary>The next session that has not started yet (null when the calendar is unknown or the season is over).</summary>
    public static CalSession? Next() => _sessions.FirstOrDefault(s => s.Start > DateTimeOffset.UtcNow);

    /// <summary>A race or qualifying on track right now.</summary>
    public static CalSession? LiveNow(string kind)
    {
        var now = DateTimeOffset.UtcNow;
        return _sessions.FirstOrDefault(s => IsKind(s, kind) && s.Start <= now && now <= s.End.AddMinutes(30));
    }

    /// <summary>Any F1 session on track right now (practice, qualifying, sprint, race), or null.</summary>
    public static CalSession? OnTrackNow()
    {
        var now = DateTimeOffset.UtcNow;
        return _sessions.FirstOrDefault(s => s.Start <= now && now <= s.End.AddMinutes(15));
    }

    /// <summary>"practice", "quali" or "race" for a session type or name ("Practice 2", "Sprint Qualifying", "Race").</summary>
    public static string KindOf(string? type, string? name = null)
    {
        var t = (type ?? "") + " " + (name ?? "");
        if (t.Contains("Practice", StringComparison.OrdinalIgnoreCase)) return "practice";
        if (t.Contains("Qualifying", StringComparison.OrdinalIgnoreCase) || t.Contains("Shootout", StringComparison.OrdinalIgnoreCase)) return "quali";
        return "race";
    }

    /// <summary>The last race or qualifying that has finished (what the key opens as a replay).</summary>
    public static CalSession? LatestDone(string kind) =>
        _sessions.LastOrDefault(s => IsKind(s, kind) && s.End < DateTimeOffset.UtcNow);

    /// <summary>"FP1", "Quali", "Sprint Q" … short enough for the big line of a key.</summary>
    public static string Short(string name) => name switch
    {
        "Practice 1" => "FP1",
        "Practice 2" => "FP2",
        "Practice 3" => "FP3",
        "Qualifying" => "Quali",
        "Sprint Qualifying" or "Sprint Shootout" => "Sprint Q",
        _ => name,
    };

    /// <summary>Time until a session starts: "6d 21h", "3h 25m", "12m" or "now".</summary>
    public static string Countdown(DateTimeOffset start)
    {
        var left = start - DateTimeOffset.UtcNow;
        if (left <= TimeSpan.Zero) return "now";
        if (left.TotalDays >= 1) return $"{(int)left.TotalDays}d {left.Hours}h";
        if (left.TotalHours >= 1) return $"{(int)left.TotalHours}h {left.Minutes:00}m";
        return $"{Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))}m";
    }

    /// <summary>When a session starts, in this PC's time: "Today 16:00", "Tomorrow 01:30" or "Fri 01:30".</summary>
    public static string When(DateTimeOffset start)
    {
        var local = start.ToLocalTime();
        var days = (local.Date - DateTime.Now.Date).Days;
        var day = days == 0 ? "Today" : days == 1 ? "Tomorrow" : days < 7 ? local.ToString("ddd", CultureInfo.InvariantCulture) : local.ToString("d MMM", CultureInfo.InvariantCulture);
        return $"{day} {local:HH:mm}";
    }
}
