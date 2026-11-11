using System.Diagnostics;
using System.Text.Json.Nodes;

namespace F1;

/// <summary>
/// Finds the latest race or qualifying replay on F1 TV and opens it in MultiViewer.
/// OpenF1 (public) gives the latest finished session and its meeting; F1 TV's own search, filtered
/// on that meeting, gives the replay's content id; MultiViewer opens it via multiviewer://f1tv/detail/….
/// </summary>
public static class Replays
{
    private static readonly HttpClient Http = CreateClient();

    /// <summary>GET with a short back-off when the service says "too many requests" (OpenF1 rate-limits).</summary>
    private static async Task<string> GetAsync(string url)
    {
        for (int attempt = 1; ; attempt++)
        {
            using var r = await Http.GetAsync(url);
            if ((int)r.StatusCode == 429 && attempt < 4) { await Task.Delay(1200 * attempt); continue; }
            r.EnsureSuccessStatusCode();
            return await r.Content.ReadAsStringAsync();
        }
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 StreamDeckF1");
        return c;
    }

    /// <summary>
    /// Best guess of the order before the session's own timing is there: for a race its starting grid,
    /// for qualifying the result of the weekend's last practice. Only drivers outside the top teams,
    /// best first. Empty when OpenF1 does not have it.
    /// </summary>
    public static async Task<List<int>> ExpectedRestAsync(string kind, int meetingKey)
    {
        try
        {
            var sessions = JsonNode.Parse(await GetAsync($"https://api.openf1.org/v1/sessions?meeting_key={meetingKey}"))?.AsArray()
                .OfType<JsonObject>().ToList() ?? new();
            JsonObject? source = kind == "race"
                ? sessions.FirstOrDefault(s => (string?)s["session_name"] == "Qualifying")
                : sessions.Where(s => (string?)s["session_type"] == "Practice")
                    .OrderByDescending(s => (string?)s["date_end"]).FirstOrDefault();
            if ((int?)source?["session_key"] is not { } key) return new();

            var url = kind == "race"
                ? $"https://api.openf1.org/v1/starting_grid?session_key={key}"
                : $"https://api.openf1.org/v1/session_result?session_key={key}";
            var order = JsonNode.Parse(await GetAsync(url))?.AsArray().OfType<JsonObject>()
                .Where(r => (int?)r["position"] != null)
                .OrderBy(r => (int)r["position"]!).Select(r => (int)r["driver_number"]!).ToList() ?? new();
            var teams = JsonNode.Parse(await GetAsync($"https://api.openf1.org/v1/drivers?session_key={key}"))?.AsArray()
                .OfType<JsonObject>().ToDictionary(d => (int)d["driver_number"]!, d => (string?)d["team_name"] ?? "") ?? new();
            var top = new HashSet<string>(Store.Current.TopTeams, StringComparer.OrdinalIgnoreCase);
            return order.Where(n => !top.Contains(teams.GetValueOrDefault(n, ""))).ToList();
        }
        catch (Exception e) { Store.Log("openf1 volgorde: " + e.Message); return new(); }
    }

    /// <summary>Meeting key of the latest finished F1 race/qualifying, from the last LatestAsync call.</summary>
    public static int? LastMeetingKey { get; private set; }

    /// <summary>Content id and title of the latest finished F1 race ("race") or qualifying ("quali").</summary>
    public static async Task<(string Id, string Title)?> LatestAsync(string kind)
    {
        string sessionName = kind == "quali" ? "Qualifying" : "Race";
        var now = DateTimeOffset.UtcNow;
        foreach (var year in new[] { now.Year, now.Year - 1 })
        {
            JsonArray? sessions;
            try { sessions = JsonNode.Parse(await GetAsync($"https://api.openf1.org/v1/sessions?year={year}"))?.AsArray(); }
            catch (Exception e) { Store.Log("openf1: " + e.Message); return null; }

            var latest = sessions?.OfType<JsonObject>()
                .Where(s => (string?)s["session_name"] == sessionName)
                .Select(s => (S: s, End: DateTimeOffset.TryParse((string?)s["date_end"], out var d) ? d : DateTimeOffset.MaxValue))
                .Where(x => x.End < now)
                .OrderByDescending(x => x.End)
                .FirstOrDefault().S;
            if (latest == null) continue;

            var meeting = (int?)latest["meeting_key"];
            var session = (int?)latest["session_key"];
            LastMeetingKey = meeting;
            try
            {
                var url = $"https://f1tv.formula1.com/2.0/R/ENG/WEB_DASH/ALL/PAGE/SEARCH/VOD/F1_TV_Pro_Annual/2?filter_MeetingKey={meeting}";
                var items = JsonNode.Parse(await GetAsync(url))?["resultObj"]?["containers"]?.AsArray();
                var hit = items?.OfType<JsonObject>().Select(i => i["metadata"]).FirstOrDefault(m =>
                    (string?)m?["emfAttributes"]?["Series"] == "FORMULA 1" &&
                    (string?)m?["titleBrief"] == sessionName &&
                    ((string?)m?["emfAttributes"]?["MeetingSessionKey"] is not { Length: > 0 } sk || sk == session?.ToString()));
                if (hit?["contentId"] is { } id) return (id.ToString(), (string?)hit["title"] ?? sessionName);
                Store.Log($"replay {kind}: geen F1 TV-content voor meeting {meeting}");
            }
            catch (Exception e) { Store.Log("f1tv zoeken: " + e.Message); }
            return null;
        }
        return null;
    }

    /// <summary>Opens F1 TV content in the running MultiViewer (its own deep link).</summary>
    public static void Open(string contentId)
    {
        try { Process.Start(new ProcessStartInfo($"multiviewer://f1tv/detail/{contentId}") { UseShellExecute = true }); }
        catch (Exception e) { Store.Log("multiviewer-link: " + e.Message); }
    }
}
