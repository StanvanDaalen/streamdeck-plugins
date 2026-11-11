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

    public PlayerInfo? Main => Players.FirstOrDefault(p => p.Main) ?? Players.FirstOrDefault();

    public string TrackStatus => (string?)Timing?["TrackStatus"]?["Status"] ?? "1";

    /// <summary>"Race", "Qualifying", "Practice 1", …</summary>
    public string? Session => (string?)Timing?["SessionInfo"]?["Name"];

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
        "{ f1LiveTimingState { TrackStatus LapCount SessionInfo RaceControlMessages } " +
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
