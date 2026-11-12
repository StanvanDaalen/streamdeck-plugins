using System.Globalization;
using System.Text.Json.Nodes;

namespace F1;

/// <summary>One F1 session from the calendar.</summary>
public sealed record CalSession(string Name, string Type, string Location, DateTimeOffset Start, DateTimeOffset End, int Meeting);

/// <summary>
/// The F1 calendar from OpenF1 (public), refreshed every half hour: the next session for the Session key
/// and, for the Race and Quali keys, which race or qualifying they would open.
/// </summary>
public static class Schedule
{
    private static readonly HttpClient Http = CreateClient();
    private static List<CalSession> _sessions = new();
    private static DateTime _loaded = DateTime.MinValue;
    private static readonly SemaphoreSlim Lock = new(1, 1);

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 StreamDeckF1");
        return c;
    }

    /// <summary>Loads the calendar when it is older than 30 minutes (quietly keeps the old one on failure).</summary>
    public static async Task RefreshAsync()
    {
        if (DateTime.UtcNow - _loaded < TimeSpan.FromMinutes(30)) return;
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
                        (string?)s["location"] ?? (string?)s["circuit_short_name"] ?? "", start, end, (int?)s["meeting_key"] ?? 0));
                }
                if (y == year + 1 && list.Count == 0) break;
            }
            if (list.Count > 0) { _sessions = list.OrderBy(s => s.Start).ToList(); _loaded = DateTime.UtcNow; }
        }
        catch (Exception e) { Store.Log("kalender: " + e.Message); _loaded = DateTime.UtcNow.AddMinutes(-25); } // try again in 5 min
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

    /// <summary>When a session starts, in this PC's time: "Today 16:00", "Tomorrow 01:30" or "Fri 01:30".</summary>
    public static string When(DateTimeOffset start)
    {
        var local = start.ToLocalTime();
        var days = (local.Date - DateTime.Now.Date).Days;
        var day = days == 0 ? "Today" : days == 1 ? "Tomorrow" : days < 7 ? local.ToString("ddd", CultureInfo.InvariantCulture) : local.ToString("d MMM", CultureInfo.InvariantCulture);
        return $"{day} {local:HH:mm}";
    }
}
