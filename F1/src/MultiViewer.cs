using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace F1;

public sealed record PlayerInfo(string Id, bool Main, double Volume, bool Muted, bool Paused, bool Live);

public sealed class MvState
{
    public bool Online;
    /// <summary>Null when no session is open in MultiViewer.</summary>
    public JsonObject? Timing;
    public List<PlayerInfo> Players = new();

    public PlayerInfo? Main => Players.FirstOrDefault(p => p.Main) ?? Players.FirstOrDefault();

    public string TrackStatus => (string?)Timing?["TrackStatus"]?["Status"] ?? "1";

    public string? Lap
    {
        get
        {
            var lc = Timing?["LapCount"];
            var cur = lc?["CurrentLap"]; var tot = lc?["TotalLaps"];
            return cur == null ? null : tot == null ? $"R{cur}" : $"R{cur}/{tot}";
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
    private const string Url = "http://127.0.0.1:10101/api/graphql";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    private const string StateQuery =
        "{ f1LiveTimingState { TrackStatus LapCount RaceControlMessages } " +
        "players { id type state { volume muted paused live } driverData { tla } } }";

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
                        (bool?)st["paused"] ?? false, (bool?)st["live"] ?? false));
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

    // ------------------------------------------------------------ layouts

    private const string LayoutQuery =
        "{ players { id type state { volume muted } streamData { contentId channelId title } " +
        "driverData { driverNumber tla } bounds { x y width height } fullscreen alwaysOnTop maintainAspectRatio } }";

    /// <summary>The players that are open now, or null when MultiViewer is not reachable.</summary>
    public static async Task<List<SavedPlayer>?> CaptureAsync()
    {
        try
        {
            var d = await QueryAsync(LayoutQuery);
            if (d?["players"] is not JsonArray players) return null;
            var list = new List<SavedPlayer>();
            foreach (var p in players)
            {
                if (p == null) continue;
                var b = p["bounds"];
                list.Add(new SavedPlayer
                {
                    Id = (string)p["id"]!,
                    ContentId = (string?)p["streamData"]?["contentId"],
                    ChannelId = (int?)p["streamData"]?["channelId"],
                    StreamTitle = (string?)p["streamData"]?["title"],
                    DriverNumber = (int?)p["driverData"]?["driverNumber"],
                    DriverTla = (string?)p["driverData"]?["tla"],
                    X = (int?)b?["x"] ?? 0, Y = (int?)b?["y"] ?? 0,
                    Width = (int?)b?["width"] ?? 960, Height = (int?)b?["height"] ?? 540,
                    Fullscreen = (bool?)p["fullscreen"] ?? false,
                    AlwaysOnTop = (bool?)p["alwaysOnTop"] ?? false,
                    MaintainAspectRatio = (bool?)p["maintainAspectRatio"] ?? true,
                    Volume = (double?)p["state"]?["volume"],
                    Muted = (bool?)p["state"]?["muted"] ?? false,
                });
            }
            return list;
        }
        catch { return null; }
    }

    /// <summary>
    /// Replaces the open players with a saved layout for the session that is open now.
    /// Returns false when no session player is open (the content id comes from it).
    /// </summary>
    public static async Task<bool> RestoreAsync(List<SavedPlayer> layout)
    {
        var open = await CaptureAsync();
        var contentId = open?.FirstOrDefault(p => p.ContentId != null)?.ContentId;
        if (open == null || contentId == null || layout.Count == 0) return false;

        // The layout's players are created first, so the session (and its content id) stays open in between.
        var created = new List<(string id, SavedPlayer p)>();
        foreach (var p in layout)
        {
            var input = new StringBuilder($"contentId: {Id(contentId)}");
            if (p.ChannelId != null) input.Append($", channelId: {p.ChannelId}");
            if (p.DriverNumber != null) input.Append($", driverNumber: {p.DriverNumber}");
            if (p.DriverTla != null) input.Append($", driverTla: {Id(p.DriverTla)}");
            if (p.StreamTitle != null) input.Append($", streamTitle: {Id(p.StreamTitle)}");
            input.Append($", bounds: {{ x: {p.X}, y: {p.Y}, width: {p.Width}, height: {p.Height} }}");
            input.Append($", fullscreen: {(p.Fullscreen ? "true" : "false")}");
            input.Append($", alwaysOnTop: {(p.AlwaysOnTop ? "true" : "false")}");
            input.Append($", maintainAspectRatio: {(p.MaintainAspectRatio ? "true" : "false")}");
            try
            {
                var d = await QueryAsync($"mutation {{ playerCreate(input: {{ {input} }}) }}");
                if ((string?)d?["playerCreate"] is { } id) created.Add((id, p));
            }
            catch (Exception e) { Store.Log("mv create: " + e.Message); }
        }
        if (created.Count < layout.Count)
        {
            // All or nothing: keep the working players and remove the incomplete new ones.
            Store.Log($"layout: {created.Count}/{layout.Count} spelers gemaakt, teruggedraaid");
            foreach (var (id, _) in created) await MutateAsync($"playerDelete(id: {Id(id)})");
            return false;
        }

        foreach (var p in open) await MutateAsync($"playerDelete(id: {Id(p.Id)})");

        // Give the new players a moment to load before applying sound settings.
        await Task.Delay(1500);
        foreach (var (id, p) in created)
        {
            if (p.Volume is { } v) await SetVolumeAsync(id, v);
            await SetMutedAsync(id, p.Muted);
        }
        return true;
    }
}

public sealed class SavedPlayer
{
    public string Id { get; set; } = "";
    public string? ContentId { get; set; }
    public int? ChannelId { get; set; }
    public string? StreamTitle { get; set; }
    public int? DriverNumber { get; set; }
    public string? DriverTla { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Fullscreen { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool MaintainAspectRatio { get; set; } = true;
    public double? Volume { get; set; }
    public bool Muted { get; set; }
}
