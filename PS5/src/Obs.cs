using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace PS5Plugin;

/// <summary>
/// Minimal obs-websocket 5 client. The password is read from OBS's own config file on this PC
/// each time it connects, so it never has to be copied anywhere else.
/// </summary>
public sealed class Obs
{
    private ClientWebSocket? _ws;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Dictionary<string, TaskCompletionSource<JsonObject?>> _pending = new();
    private readonly object _pendingLock = new();
    private int _nextId;
    private DateTime _nextAttempt = DateTime.MinValue;

    public bool Connected => _ws?.State == WebSocketState.Open && Identified;
    public bool Identified { get; private set; }
    public string LastError { get; private set; } = "";

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio", "plugin_config", "obs-websocket", "config.json");

    public static bool Running => System.Diagnostics.Process.GetProcessesByName("obs64").Length > 0;

    /// <summary>Connects when OBS runs and no connection exists; quietly retries every few seconds.</summary>
    public async Task EnsureConnected()
    {
        if (Connected || DateTime.UtcNow < _nextAttempt) return;
        _nextAttempt = DateTime.UtcNow.AddSeconds(4);
        if (!Running) { Drop(); LastError = "OBS not running"; return; }
        try { await Connect(); }
        catch (Exception e)
        {
            if (e.Message != LastError) Log.Write("obs connect: " + e.Message); // once per kind of error, not every retry
            LastError = e.Message;
            Drop();
        }
    }

    private async Task Connect()
    {
        Drop();
        int port = 4455; string password = ""; bool auth = false;
        if (File.Exists(ConfigPath))
        {
            var cfg = JsonNode.Parse(File.ReadAllText(ConfigPath))!.AsObject();
            port = cfg["server_port"]?.GetValue<int>() ?? 4455;
            password = cfg["server_password"]?.GetValue<string>() ?? "";
            auth = cfg["auth_required"]?.GetValue<bool>() ?? false;
        }

        var ws = new ClientWebSocket();
        using var timeout = new CancellationTokenSource(3000);
        try { await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), timeout.Token); }
        catch { throw new Exception("WebSocket is off in OBS (Tools > WebSocket Server Settings)"); }
        _ws = ws;

        var hello = await ReceiveOne(ws, timeout.Token) ?? throw new Exception("no Hello from OBS");
        var identify = new JsonObject { ["rpcVersion"] = 1, ["eventSubscriptions"] = 0 };
        if (hello["d"]?["authentication"] is JsonObject a)
        {
            if (!auth && password.Length == 0) throw new Exception("OBS asks for a password");
            var secret = B64Sha(password + a["salt"]!.GetValue<string>());
            identify["authentication"] = B64Sha(secret + a["challenge"]!.GetValue<string>());
        }
        await SendRaw(new JsonObject { ["op"] = 1, ["d"] = identify });
        var identified = await ReceiveOne(ws, timeout.Token);
        if (identified?["op"]?.GetValue<int>() != 2) throw new Exception("OBS refused the connection (password?)");

        Identified = true;
        LastError = "";
        Log.Write("obs connected");
        _ = Task.Run(() => ReceiveLoop(ws));
    }

    private static string B64Sha(string s) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    private static async Task<JsonObject?> ReceiveOne(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[1 << 16];
        using var ms = new MemoryStream();
        WebSocketReceiveResult r;
        do
        {
            r = await ws.ReceiveAsync(buffer, ct);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, r.Count);
        } while (!r.EndOfMessage);
        return JsonNode.Parse(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length)) as JsonObject;
    }

    private async Task ReceiveLoop(ClientWebSocket ws)
    {
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var m = await ReceiveOne(ws, CancellationToken.None);
                if (m == null) break;
                if (m["op"]?.GetValue<int>() != 7) continue;
                var d = m["d"]!.AsObject();
                var id = d["requestId"]?.GetValue<string>() ?? "";
                TaskCompletionSource<JsonObject?>? tcs;
                lock (_pendingLock) { _pending.Remove(id, out tcs); }
                bool ok = d["requestStatus"]?["result"]?.GetValue<bool>() == true;
                tcs?.TrySetResult(ok ? d["responseData"] as JsonObject ?? new JsonObject() : null);
            }
        }
        catch (Exception e) { Log.Write("obs receive: " + e.Message); }
        if (ReferenceEquals(ws, _ws)) Drop();
    }

    private async Task SendRaw(JsonObject o)
    {
        var ws = _ws;
        if (ws == null) return;
        var bytes = Encoding.UTF8.GetBytes(o.ToJsonString());
        await _sendLock.WaitAsync();
        try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
        finally { _sendLock.Release(); }
    }

    /// <summary>Sends a request; null when OBS is gone, the request failed or no answer came within 2 s.</summary>
    public async Task<JsonObject?> Request(string type, JsonObject? data = null)
    {
        if (!Connected) return null;
        var id = Interlocked.Increment(ref _nextId).ToString();
        var tcs = new TaskCompletionSource<JsonObject?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pendingLock) _pending[id] = tcs;
        var d = new JsonObject { ["requestType"] = type, ["requestId"] = id };
        if (data != null) d["requestData"] = data;
        try { await SendRaw(new JsonObject { ["op"] = 6, ["d"] = d }); }
        catch (Exception e) { Log.Write("obs send: " + e.Message); Drop(); return null; }
        var done = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        lock (_pendingLock) _pending.Remove(id);
        return done == tcs.Task ? tcs.Task.Result : null;
    }

    private void Drop()
    {
        Identified = false;
        var ws = _ws; _ws = null;
        if (ws != null) try { ws.Abort(); ws.Dispose(); } catch { }
        lock (_pendingLock)
        {
            foreach (var t in _pending.Values) t.TrySetResult(null);
            _pending.Clear();
        }
    }

    /// <summary>Starts OBS the way its shortcut does (OBS needs its own folder as working directory).</summary>
    public static bool Start()
    {
        var exe = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "obs-studio", "bin", "64bit", "obs64.exe"),
            @"C:\Program Files\obs-studio\bin\64bit\obs64.exe",
        }.FirstOrDefault(File.Exists);
        if (exe == null) return false;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
            {
                WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = true,
                Arguments = "--disable-shutdown-check",
            });
            return true;
        }
        catch (Exception e) { Log.Write("obs start: " + e.Message); return false; }
    }
}
