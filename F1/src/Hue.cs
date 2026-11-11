using System.Text;
using System.Text.Json.Nodes;

namespace F1;

/// <summary>Philips Hue bridge over the local v1 REST API.</summary>
public static class Hue
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public static bool Paired => Store.Current is { BridgeIp.Length: > 0, AppKey.Length: > 0 };

    private static string Base => $"http://{Store.Current.BridgeIp}/api/{Store.Current.AppKey}";

    public static async Task<JsonObject?> GetAsync(string path)
    {
        if (!Paired) return null;
        try { return JsonNode.Parse(await Http.GetStringAsync(Base + path)) as JsonObject; }
        catch (Exception e) { Store.Log($"hue GET {path}: {e.Message}"); return null; }
    }

    public static async Task PutStateAsync(string light, JsonObject state)
    {
        try
        {
            using var body = new StringContent(state.ToJsonString(), Encoding.UTF8, "application/json");
            using var r = await Http.PutAsync($"{Base}/lights/{light}/state", body);
        }
        catch (Exception e) { Store.Log($"hue light {light}: {e.Message}"); }
    }

    /// <summary>Sends the same state to every light at once, so they all change together.</summary>
    public static Task SetAllAsync(IEnumerable<string> lights, JsonObject state) =>
        Task.WhenAll(lights.Select(l => PutStateAsync(l, (JsonObject)state.DeepClone())));

    /// <summary>Rooms and zones (no entertainment areas), by name.</summary>
    public static async Task<List<string>> GroupNamesAsync()
    {
        var groups = await GetAsync("/groups");
        if (groups == null) return new();
        return groups.Select(g => g.Value!.AsObject())
            .Where(g => (string?)g["type"] is "Room" or "Zone")
            .Select(g => (string)g["name"]!)
            .Distinct()
            .ToList();
    }

    /// <summary>Colour lights in the configured group (plugs and white-only bulbs are skipped).</summary>
    public static async Task<List<string>> LightsAsync(string group)
    {
        var groups = await GetAsync("/groups");
        var lights = await GetAsync("/lights");
        if (groups == null || lights == null) return new();
        var g = groups.Select(x => x.Value!.AsObject())
            .FirstOrDefault(x => (string?)x["name"] == group && (string?)x["type"] is "Room" or "Zone");
        if (g == null) return new();
        return g["lights"]!.AsArray().Select(l => (string)l!)
            .Where(id => lights[id]?["state"]?["xy"] != null)
            .ToList();
    }

    /// <summary>Finds bridges on the network through Philips' discovery service.</summary>
    public static async Task<List<string>> DiscoverAsync()
    {
        try
        {
            var list = JsonNode.Parse(await Http.GetStringAsync("https://discovery.meethue.com"))!.AsArray();
            return list.Select(b => (string)b!["internalipaddress"]!).ToList();
        }
        catch (Exception e) { Store.Log("hue discover: " + e.Message); return new(); }
    }

    /// <summary>Asks a bridge for an app key; only works within 30 s after its link button is pressed.</summary>
    public static async Task<string?> TryPairAsync(string ip)
    {
        try
        {
            using var body = new StringContent("{\"devicetype\":\"streamdeck_f1#pc\"}", Encoding.UTF8, "application/json");
            using var r = await Http.PostAsync($"http://{ip}/api", body);
            var n = JsonNode.Parse(await r.Content.ReadAsStringAsync())!.AsArray();
            return (string?)n[0]?["success"]?["username"];
        }
        catch { return null; }
    }
}
