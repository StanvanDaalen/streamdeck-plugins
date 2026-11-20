using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace F1;

public sealed record PlayerInfo(string Id, bool Main, double Volume, bool Muted, bool Paused, bool Live, string? Title, bool Fullscreen);

public sealed class MvState
{
    public bool Online;
    /// <summary>Null when no session is open in MultiViewer.</summary>
    public JsonObject? Timing;
    public List<PlayerInfo> Players = new();

    /// <summary>
    /// The main feed: "F1 LIVE" (the one with commentary and sound), else the player whose sound is on,
    /// else the first player without a driver. The Driver Tracker also has no driver, so order matters.
    /// </summary>
    public PlayerInfo? Main =>
        Players.FirstOrDefault(p => string.Equals(p.Title, "F1 LIVE", StringComparison.OrdinalIgnoreCase))
        ?? Players.FirstOrDefault(p => p.Main && !p.Muted)
        ?? Players.FirstOrDefault(p => !p.Muted)
        ?? Players.FirstOrDefault(p => p.Main)
        ?? Players.FirstOrDefault();

    public string TrackStatus => (string?)Timing?["TrackStatus"]?["Status"] ?? "1";

    /// <summary>"Race", "Qualifying", "Practice 1", …</summary>
    public string? Session => (string?)Timing?["SessionInfo"]?["Name"];

    /// <summary>Timing time in ms since 1970 (follows replays), from MultiViewer's clock.</summary>
    public long? TrackTimeMs;

    public bool IsRace => (string?)Timing?["SessionInfo"]?["Type"] == "Race";

    /// <summary>Short session name for a key: FP1, Quali, Sprint Q, Sprint, Race.</summary>
    public string? SessionShort => Session switch
    {
        null => null,
        var s when s.StartsWith("Practice ") => "FP" + s["Practice ".Length..],
        "Qualifying" => "Quali",
        "Sprint Qualifying" or "Sprint Shootout" => "Sprint Q",
        var s => s,
    };

    public string? LapShort
    {
        get
        {
            var lc = Timing?["LapCount"];
            return lc?["CurrentLap"] is { } cur ? (lc["TotalLaps"] is { } tot ? $"{cur}/{tot}" : $"{cur}") : null;
        }
    }

    /// <summary>Session time left ("12:34"), counted on from the last clock message while the clock runs.</summary>
    public string? Remaining
    {
        get
        {
            var clock = Timing?["ExtrapolatedClock"];
            if ((string?)clock?["Remaining"] is not { } rem || !TimeSpan.TryParse(rem, CultureInfo.InvariantCulture, out var left)) return null;
            if ((bool?)clock["Extrapolating"] == true && TrackTimeMs is { } now &&
                DateTimeOffset.TryParse((string?)clock["Utc"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
                left -= TimeSpan.FromMilliseconds(now - at.ToUnixTimeMilliseconds());
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            return left.TotalHours >= 1 ? left.ToString(@"h\:mm\:ss") : left.ToString(@"mm\:ss");
        }
    }

    public (double? Track, double? Air, bool Rain) Weather
    {
        get
        {
            var w = Timing?["WeatherData"];
            static double? Num(JsonNode? n) =>
                double.TryParse(n?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
            return (Num(w?["TrackTemp"]), Num(w?["AirTemp"]), Num(w?["Rainfall"]) is > 0);
        }
    }

    public string? Lap
    {
        get
        {
            var lc = Timing?["LapCount"];
            var cur = lc?["CurrentLap"]; var tot = lc?["TotalLaps"];
            return cur == null ? null : tot == null ? $"Lap {cur}" : $"Lap {cur}/{tot}";
        }
    }

    public int Chequered
    {
        get
        {
            var msgs = Timing?["RaceControlMessages"]?["Messages"];
            IEnumerable<JsonNode?> list = msgs switch
            {
                JsonArray a => a,
                JsonObject o => o.Select(kv => kv.Value), // sometimes keyed by index
                _ => Array.Empty<JsonNode?>(),
            };
            return list.Count(m => string.Equals((string?)m?["Flag"], "CHEQUERED", StringComparison.OrdinalIgnoreCase));
        }
    }
}

/// <summary>MultiViewer's local GraphQL API (port 10101).</summary>
public static class MultiViewer
{
    public const string Process = "MultiViewer";
    private const string Url = "http://127.0.0.1:10101/api/graphql";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    private const string StateQuery =
        "{ f1LiveTimingState { TrackStatus LapCount SessionInfo RaceControlMessages ExtrapolatedClock WeatherData } " +
        "f1LiveTimingClock { trackTime } " +
        "players { id type fullscreen state { volume muted paused live } driverData { tla } streamData { title } } }";

    private static async Task<JsonNode?> QueryAsync(string query)
    {
        var body = new JsonObject { ["query"] = query };
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var r = await Http.PostAsync(Url, content);
        var n = JsonNode.Parse(await r.Content.ReadAsStringAsync());
        if (n?["errors"] is JsonArray { Count: > 0 } e)
        {
            Store.Log("mv: " + e.ToJsonString());
            throw new InvalidOperationException("MultiViewer: " + (string?)e[0]?["message"]);
        }
        return n?["data"];
    }

    public static async Task<MvState> StateAsync()
    {
        var s = new MvState();
        try
        {
            var d = await QueryAsync(StateQuery);
            s.Online = true;
            s.Timing = d?["f1LiveTimingState"] as JsonObject;
            if (long.TryParse(d?["f1LiveTimingClock"]?["trackTime"]?.ToString(), out var tt)) s.TrackTimeMs = tt;
            if (d?["players"] is JsonArray players)
            {
                foreach (var p in players)
                {
                    var st = p?["state"];
                    if (p == null || st == null) continue;
                    // OBC = onboard camera; the broadcast feed is the player without a driver.
                    bool main = (string?)p["type"] != "OBC" && p["driverData"] == null;
                    s.Players.Add(new PlayerInfo((string)p["id"]!, main,
                        (double?)st["volume"] ?? 0, (bool?)st["muted"] ?? false,
                        (bool?)st["paused"] ?? false, (bool?)st["live"] ?? false,
                        (string?)p["streamData"]?["title"], (bool?)p["fullscreen"] ?? false));
                }
            }
        }
        catch { s.Online = false; }
        return s;
    }

    private static string Id(string id) => JsonSerializer.Serialize(id);
    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Runs a mutation; false when MultiViewer rejected it or did not answer.</summary>
    private static async Task<bool> MutateAsync(string m)
    {
        try { await QueryAsync("mutation { " + m + " }"); return true; }
        catch (Exception e) { Store.Log("mv mutation: " + e.Message); return false; }
    }

    public static Task<bool> SetVolumeAsync(string id, double v) => MutateAsync($"playerSetVolume(id: {Id(id)}, volume: {Num(v)})");
    public static Task<bool> SetMutedAsync(string id, bool m) => MutateAsync($"playerSetMuted(id: {Id(id)}, muted: {(m ? "true" : "false")})");
    public static Task<bool> SetPausedAsync(string id, bool p) => MutateAsync($"playerSetPaused(id: {Id(id)}, paused: {(p ? "true" : "false")})");
    public static Task<bool> SeekRelativeAsync(string id, double seconds) => MutateAsync($"playerSeekTo(id: {Id(id)}, relative: {Num(seconds)})");
    public static Task<bool> SyncAsync(string id) => MutateAsync($"playerSync(id: {Id(id)})");
    public static Task<bool> SetFullscreenAsync(string id, bool on) => MutateAsync($"playerSetFullscreen(id: {Id(id)}, fullscreen: {(on ? "true" : "false")})");

    /// <summary>Positions and teams: TimingData and DriverList from the live timing (null when not available).</summary>
    public static async Task<JsonObject?> TimingAsync()
    {
        try { return (await QueryAsync("{ f1LiveTimingState { TimingData DriverList ChampionshipPrediction } }"))?["f1LiveTimingState"] as JsonObject; }
        catch { return null; }
    }

    /// <summary>Driver numbers by session position, best first (from TimingData).</summary>
    public static List<int> ByPosition(JsonObject? timing) =>
        (timing?["TimingData"]?["Lines"] as JsonObject)?
            .Select(kv => (Num: int.TryParse(kv.Key, out var n) ? n : -1, Pos: int.TryParse(kv.Value?["Position"]?.ToString(), out var p) ? p : 99))
            .Where(x => x.Num > 0 && x.Pos < 99).OrderBy(x => x.Pos).Select(x => x.Num).ToList() ?? new();

    /// <summary>Driver numbers by championship position at the start of the session (races only), best first.</summary>
    public static List<int> ByChampionship(JsonObject? timing) =>
        (timing?["ChampionshipPrediction"]?["Drivers"] as JsonObject)?
            .Select(kv => (Num: int.TryParse(kv.Key, out var n) ? n : -1, Pos: int.TryParse(kv.Value?["CurrentPosition"]?.ToString(), out var p) ? p : 99))
            .Where(x => x.Num > 0 && x.Pos < 99).OrderBy(x => x.Pos).Select(x => x.Num).ToList() ?? new();

    public sealed record SlotPlayer(string Id, string? ContentId, int? DriverNumber, string? TeamName, string? Tla,
        int X, int Y, int Width, int Height, bool Muted, double Volume);

    public static async Task<List<SlotPlayer>> SlotPlayersAsync()
    {
        try
        {
            var d = await QueryAsync("{ players { id type state { muted volume } driverData { driverNumber teamName tla } " +
                                     "streamData { contentId } bounds { x y width height } } }");
            return (d?["players"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>()
                .Where(p => (string?)p["type"] == "OBC")
                .Select(p => new SlotPlayer((string)p["id"]!, (string?)p["streamData"]?["contentId"],
                    (int?)p["driverData"]?["driverNumber"], (string?)p["driverData"]?["teamName"], (string?)p["driverData"]?["tla"],
                    (int?)p["bounds"]?["x"] ?? 0, (int?)p["bounds"]?["y"] ?? 0, (int?)p["bounds"]?["width"] ?? 0, (int?)p["bounds"]?["height"] ?? 0,
                    (bool?)p["state"]?["muted"] ?? true, (double?)p["state"]?["volume"] ?? 100))
                .ToList();
        }
        catch { return new(); }
    }

    /// <summary>Replaces an onboard with another driver's onboard in exactly the same place.</summary>
    public static async Task SwapDriverAsync(SlotPlayer old, int driverNumber)
    {
        if (old.ContentId == null) return;
        try
        {
            var d = await QueryAsync(
                $"mutation {{ playerCreate(input: {{ contentId: {Id(old.ContentId)}, driverNumber: {driverNumber}, " +
                $"bounds: {{ x: {old.X}, y: {old.Y}, width: {old.Width}, height: {old.Height} }}, maintainAspectRatio: true }}) }}");
            if ((string?)d?["playerCreate"] is not { } id) { Store.Log("swap: nieuwe speler niet gemaakt, oude blijft"); return; }
            await Task.Delay(1500);
            if (!await SetMutedAsync(id, old.Muted) || !await SetVolumeAsync(id, old.Volume))
                Store.Log("swap: geluid van de nieuwe speler niet overgezet");
            if (!await MutateAsync($"playerDelete(id: {Id(old.Id)})")) Store.Log("swap: oude speler niet gesloten");
        }
        catch (Exception e) { Store.Log("swap: " + e.Message); }
    }

    /// <summary>Content id of the session the open players show (a replay or live), or null.</summary>
    public static async Task<string?> ContentIdAsync()
    {
        try
        {
            var d = await QueryAsync("{ players { streamData { contentId } } }");
            return d?["players"]?.AsArray().Select(p => (string?)p?["streamData"]?["contentId"]).FirstOrDefault(c => c != null);
        }
        catch { return null; }
    }

    public static async Task DeleteAllPlayersAsync()
    {
        try
        {
            var d = await QueryAsync("{ players { id } }");
            foreach (var p in d?["players"]?.AsArray() ?? new JsonArray())
                await MutateAsync($"playerDelete(id: {Id((string)p!["id"]!)})");
        }
        catch (Exception e) { Store.Log("mv delete: " + e.Message); }
    }
}
