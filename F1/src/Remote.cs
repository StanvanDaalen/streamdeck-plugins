using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace F1;

/// <summary>
/// A small local door for other apps on this computer (F1 Party): they can ask for Race / Quali / Practice,
/// optionally for a given F1 TV session, exactly as pressing the key does. Listens on 127.0.0.1 only and
/// refuses requests from web pages (those carry an Origin header).
///   GET  /f1/status → { "plugin": "F1 Race Control", "version": "…", "busy": bool }
///   POST /f1/open   { "kind": "race" | "quali" | "practice", "contentId": "1000010439" (optional) }
///   POST /f1/rest   → fill the best-of-the-rest onboards now (after the caller moved the replay in time)
/// </summary>
public static class Remote
{
    public const int Port = 9335;

    public static void Start(Func<string, string?, bool> open, Func<bool> busy, Func<Task<bool>> refreshRest)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        try { listener.Start(); }
        catch (Exception e) { Store.Log($"remote: poort {Port} niet beschikbaar: {e.Message}"); return; }
        Store.Log($"remote: luistert op 127.0.0.1:{Port}");
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { break; }
                _ = Task.Run(() => Handle(ctx, open, busy, refreshRest));
            }
        });
    }

    private static async Task Handle(HttpListenerContext ctx, Func<string, string?, bool> open, Func<bool> busy, Func<Task<bool>> refreshRest)
    {
        var req = ctx.Request;
        int code = 200;
        JsonObject body;
        try
        {
            if (req.Headers["Origin"] != null) { code = 403; body = Error("forbidden"); }
            else if (req.HttpMethod == "GET" && req.Url?.AbsolutePath == "/f1/status")
                body = new JsonObject { ["plugin"] = "F1 Race Control", ["version"] = Version, ["busy"] = busy() };
            else if (req.HttpMethod == "POST" && req.Url?.AbsolutePath == "/f1/open")
            {
                using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
                var input = JsonNode.Parse(await reader.ReadToEndAsync()) as JsonObject;
                var kind = (string?)input?["kind"] ?? "";
                var content = (string?)input?["contentId"];
                if (kind is not ("race" or "quali" or "practice")) { code = 400; body = Error("kind must be race, quali or practice"); }
                else if (content != null && (content.Length > 20 || !content.All(char.IsAsciiDigit))) { code = 400; body = Error("bad contentId"); }
                else
                {
                    Store.Log($"remote: {kind} gevraagd{(content != null ? " voor " + content : "")}");
                    body = new JsonObject { ["started"] = open(kind, content) };
                }
            }
            else if (req.HttpMethod == "POST" && req.Url?.AbsolutePath == "/f1/rest")
            {
                Store.Log("remote: best of the rest verversen");
                body = new JsonObject { ["done"] = await refreshRest() };
            }
            else { code = 404; body = Error("not found"); }
        }
        catch (Exception e) { code = 500; body = Error(e.Message); }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(body.ToJsonString());
            ctx.Response.StatusCode = code;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
        catch { }
    }

    private static JsonObject Error(string msg) => new() { ["error"] = msg };

    /// <summary>The plugin's version as Stream Deck shows it (manifest.json, one folder above bin).</summary>
    private static string Version
    {
        get
        {
            try
            {
                var manifest = Path.Combine(AppContext.BaseDirectory, "..", "manifest.json");
                return (string?)JsonNode.Parse(File.ReadAllText(manifest))?["Version"] ?? "";
            }
            catch { return ""; }
        }
    }
}
