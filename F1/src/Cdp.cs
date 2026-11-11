using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace F1;

/// <summary>
/// Chromium remote debugging, as used for the two Electron apps (started with --remote-debugging-port).
/// Only listens on 127.0.0.1, so it is reachable from this PC only.
/// </summary>
public static class Cdp
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    /// <summary>The app's windows (pages); empty when the port is not open.</summary>
    public static async Task<List<JsonObject>> PagesAsync(int port)
    {
        try
        {
            var list = JsonNode.Parse(await Http.GetStringAsync($"http://127.0.0.1:{port}/json/list"))!.AsArray();
            return list.OfType<JsonObject>().Where(t => (string?)t["type"] == "page").ToList();
        }
        catch { return new(); }
    }

    public static async Task CloseAsync(int port, string targetId)
    {
        try { using var _ = await Http.GetAsync($"http://127.0.0.1:{port}/json/close/{targetId}"); }
        catch (Exception e) { Store.Log("cdp close: " + e.Message); }
    }

    /// <summary>Evaluates JavaScript in one window and returns the value (as JSON), or null on a script error.</summary>
    public static async Task<JsonNode?> EvalAsync(JsonObject page, string expression, bool awaitPromise = true, int timeoutSeconds = 10)
    {
        if ((string?)page["webSocketDebuggerUrl"] is not { } wsUrl) return null;
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
            if (reply?["result"]?["exceptionDetails"] is { } ex) { Store.Log("cdp js: " + ex.ToJsonString()); return null; }
            return reply?["result"]?["result"]?["value"];
        }
    }
}
