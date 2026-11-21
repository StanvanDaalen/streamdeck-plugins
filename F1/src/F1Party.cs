using System.Diagnostics;
using System.Text.Json.Nodes;

namespace F1;

/// <summary>
/// F1 Party (github.com/Jeremy0538/f1party), the watch-party app, when it is installed: the Sync key syncs to
/// the party's host when you are a guest, and holding it hosts a party and posts the Discord invite.
/// F1 Party listens on 127.0.0.1:47821 and writes where it is installed to f1party/app.json.
/// </summary>
public static class F1Party
{
    private const string Control = "http://127.0.0.1:47821/control/";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static string AppInfoFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "f1party", "app.json");

    /// <summary>"idle", "host" or "guest" while F1 Party runs, else null.</summary>
    public static async Task<string?> ModeAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var json = JsonNode.Parse(await Http.GetStringAsync(Control + "status", cts.Token));
            return (string?)json?["mode"];
        }
        catch { return null; }
    }

    /// <summary>Guest: jump to the host and take the onboards along.</summary>
    public static async Task<bool> SyncAsync()
    {
        try { return (await Http.PostAsync(Control + "sync", null)).IsSuccessStatusCode; }
        catch (Exception e) { Store.Log("f1party sync: " + e.Message); return false; }
    }

    /// <summary>Hosts a party (or keeps the one hosted now) and posts the Discord invite; starts F1 Party first when needed.</summary>
    public static async Task<bool> HostAsync()
    {
        if (await ModeAsync() == null)
        {
            var exe = Exe();
            if (exe == null) { Store.Log("F1 Party: niet gevonden (nog nooit gestart?)"); return false; }
            Store.Log("F1 Party starten met --host");
            Process.Start(new ProcessStartInfo(exe, "--host") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
            return true; // it hosts and invites by itself once it is up
        }
        try
        {
            var res = await Http.PostAsync(Control + "host", null);
            var body = await res.Content.ReadAsStringAsync();
            Store.Log($"F1 Party host: {(int)res.StatusCode} {body.Trim()}");
            return res.IsSuccessStatusCode;
        }
        catch (Exception e) { Store.Log("f1party host: " + e.Message); return false; }
    }

    private static string? Exe()
    {
        try
        {
            var exe = (string?)JsonNode.Parse(File.ReadAllText(AppInfoFile))?["exe"];
            return exe != null && File.Exists(exe) ? exe : null;
        }
        catch { return null; }
    }
}
