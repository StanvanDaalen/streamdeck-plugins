using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace F1;

/// <summary>
/// Ultimate F1 Viewer, driven through Chromium's remote-debugging port (the workspace starts it with
/// --remote-debugging-port). Its main window has the same restoreLayout/saveLayout functions as its
/// LOAD and SAVE buttons, so layouts behave exactly as when clicked in the app.
/// </summary>
public static class Uf1
{
    public const string Process = "Ultimate-F1Viewer-With-F1MV";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private static string Base => $"http://127.0.0.1:{Store.Current.Uf1Port}";

    private static async Task<List<JsonObject>> TargetsAsync()
    {
        try
        {
            var list = JsonNode.Parse(await Http.GetStringAsync(Base + "/json/list"))!.AsArray();
            return list.OfType<JsonObject>().Where(t => (string?)t["type"] == "page").ToList();
        }
        catch { return new(); }
    }

    private static bool IsMain(JsonObject t) => ((string?)t["url"] ?? "").Contains("/main/index.html");

    public static async Task<bool> ReadyAsync() => (await TargetsAsync()).Any(IsMain);

    /// <summary>Evaluates JavaScript in the main window and returns the value (JSON), or null on failure.</summary>
    private static async Task<JsonNode?> EvalAsync(string expression, bool awaitPromise = true, int timeoutSeconds = 10)
    {
        var main = (await TargetsAsync()).FirstOrDefault(IsMain);
        if ((string?)main?["webSocketDebuggerUrl"] is not { } wsUrl) return null;
        using var ws = new ClientWebSocket();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        await ws.ConnectAsync(new Uri(wsUrl), cts.Token);
        var msg = new JsonObject
        {
            ["id"] = 1, ["method"] = "Runtime.evaluate",
            ["params"] = new JsonObject { ["expression"] = expression, ["awaitPromise"] = awaitPromise, ["returnByValue"] = true },
        };
        await ws.SendAsync(Encoding.UTF8.GetBytes(msg.ToJsonString()), WebSocketMessageType.Text, true, cts.Token);
        var buffer = new byte[1 << 16];
        using var ms = new MemoryStream();
        while (true)
        {
            ms.SetLength(0);
            WebSocketReceiveResult r;
            do { r = await ws.ReceiveAsync(buffer, cts.Token); ms.Write(buffer, 0, r.Count); } while (!r.EndOfMessage);
            var reply = JsonNode.Parse(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
            if ((int?)reply?["id"] != 1) continue; // events from the page
            if (reply?["result"]?["exceptionDetails"] is { } ex) { Store.Log("uf1 js: " + ex.ToJsonString()); return null; }
            return reply?["result"]?["result"]?["value"];
        }
    }

    /// <summary>The F1 TV live session as the viewer sees it: (found, session type such as "Race" or "Qualifying").</summary>
    public static async Task<(bool Live, string? Type)> LiveSessionAsync()
    {
        try
        {
            var v = await EvalAsync("JSON.stringify(typeof liveSessionInfo === 'undefined' ? null : liveSessionInfo)");
            var info = (string?)v is { } s ? JsonNode.Parse(s) : null;
            return ((bool?)info?["liveSessionFound"] ?? false, (string?)info?["sessionInfo"]?["sessionType"]);
        }
        catch (Exception e) { Store.Log("uf1 live: " + e.Message); return (false, null); }
    }

    /// <summary>Layout id in the viewer's settings whose name fits "race" or "quali".</summary>
    public static string? FindLayout(string kind)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Process, "config.json");
            var layouts = JsonNode.Parse(File.ReadAllText(path))?["layouts"]?.AsObject();
            if (layouts == null) return null;
            var named = layouts.Select(kv => (Id: kv.Key, Name: ((string?)kv.Value?["name"] ?? "").ToLowerInvariant())).ToList();
            return kind == "quali"
                ? named.FirstOrDefault(l => l.Name.Contains("qual")).Id
                : (named.FirstOrDefault(l => l.Name == "race").Id ?? named.FirstOrDefault(l => l.Name.Contains("race")).Id);
        }
        catch (Exception e) { Store.Log("uf1 layouts: " + e.Message); return null; }
    }

    /// <summary>
    /// Opens a layout. Without a live session the MultiViewer streams need a content id (a replay
    /// that is open), just like the field in the viewer. Not awaited inside the viewer: before a
    /// session starts it keeps waiting until the session goes live.
    /// </summary>
    public static async Task<bool> RestoreLayoutAsync(string id, string? contentId)
    {
        // Awaited, so a failure in the viewer shows as an alert on the key instead of a false OK.
        // Before a session starts the viewer keeps waiting for it; that wait ends here as a timeout,
        // which is fine: it carries on and opens the streams when the session goes live.
        var js = $"(async () => {{ const f = document.getElementById('content-id-field'); if (f) f.value = {JsonSerializer.Serialize(contentId ?? "")}; " +
                 $"await restoreLayout({int.Parse(id)}); return true; }})()";
        try { return (bool?)await EvalAsync(js, awaitPromise: true, timeoutSeconds: 60) == true; }
        catch (OperationCanceledException) { Store.Log("uf1 restore: wacht op sessie"); return true; }
        catch (Exception e) { Store.Log("uf1 restore: " + e.Message); return false; }
    }

    public static async Task<bool> SaveLayoutAsync(string id)
    {
        try { return (bool?)await EvalAsync($"saveLayout({int.Parse(id)}).then(() => true)") == true; }
        catch (Exception e) { Store.Log("uf1 save: " + e.Message); return false; }
    }

    /// <summary>Closes the viewer's extra windows (not the main window, not auto camera switching).</summary>
    public static async Task CloseExtraWindowsAsync()
    {
        foreach (var t in (await TargetsAsync()).Where(t => !IsMain(t) && (string?)t["title"] != "Auto Onboard Camera Switching"))
        {
            try { using var _ = await Http.GetAsync($"{Base}/json/close/{(string?)t["id"]}"); }
            catch (Exception e) { Store.Log("uf1 close: " + e.Message); }
        }
    }
}
