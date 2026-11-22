using System.Diagnostics;
using System.Text.Json.Nodes;

namespace F1;

/// <summary>
/// F1 Party (github.com/Jeremy0538/f1party), the watch-party app, when it is installed. The keys then only
/// tell F1 Party what to do: the Party key (sync, code, invite, leave), and Race, Quali, Session and Close
/// F1, which F1 Party opens and closes the way its "Open with" says.
/// F1 Party listens on 127.0.0.1:47821 and writes where it is installed to f1party/app.json.
/// </summary>
public static class F1Party
{
    private const string Control = "http://127.0.0.1:47821/control/";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static string AppInfoFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "f1party", "app.json");

    /// <summary>The party for the Sync key: mode ("idle", "host", "guest"), code, members and how well they are in sync.</summary>
    /// <param name="Desktop">F1's own desktop as set in F1 Party ("" = none), null from an older F1 Party.</param>
    public sealed record Info(string Mode, string Code, int Members, string Sync, int Countdown = 0, bool Session = false, string? Desktop = null);

    /// <summary>The party while F1 Party runs, else null.</summary>
    public static async Task<Info?> InfoAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var j = JsonNode.Parse(await Http.GetStringAsync(Control + "status", cts.Token));
            return new Info((string?)j?["mode"] ?? "idle", (string?)j?["code"] ?? "", (int?)j?["members"] ?? 0, (string?)j?["sync"] ?? "",
                (int?)j?["countdown"] ?? 0, (bool?)j?["session"] ?? false, (string?)j?["desktop"]);
        }
        catch { return null; }
    }

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

    /// <summary>Pause key in a party: the whole party (everyone) or just this computer.</summary>
    public static async Task PauseAsync(bool paused, bool everyone)
    {
        try
        {
            var body = new StringContent(new JsonObject { ["paused"] = paused, ["everyone"] = everyone }.ToJsonString(),
                System.Text.Encoding.UTF8, "application/json");
            var res = await Http.PostAsync(Control + "pause", body);
            Store.Log($"F1 Party pauze ({(everyone ? "hele party" : "alleen ik")}, {(paused ? "pauze" : "verder")}): {(int)res.StatusCode}");
        }
        catch (Exception e) { Store.Log("f1party pause: " + e.Message); }
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

    /// <summary>Whether F1 Party is installed (it has run once on this computer).</summary>
    public static bool Installed => Exe() != null;

    /// <summary>
    /// A command for F1 Party ("party", "copy", "invite", "leave", "close" (End session), "calendar" (Start
    /// session: the window on the calendar), "start" (Start
    /// together), "watch" with which = "race" |
    /// "quali" | "live"). Starts F1 Party first when it isn't running and waits for it (up to 20 s).
    /// </summary>
    public static async Task<bool> CommandAsync(string command, string? which = null)
    {
        if (await ModeAsync() == null)
        {
            if (command is "leave" or "close") return false; // nothing to leave or close in F1 Party
            var exe = Exe();
            if (exe == null) { Store.Log("F1 Party: niet gevonden (nog nooit gestart?)"); return false; }
            Store.Log($"F1 Party starten voor {command}");
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
            for (int i = 0; i < 40 && await ModeAsync() == null; i++) await Task.Delay(500);
        }
        try
        {
            HttpContent? body = which == null ? null : new StringContent(new JsonObject { ["which"] = which }.ToJsonString(),
                System.Text.Encoding.UTF8, "application/json");
            var res = await Http.PostAsync(Control + command, body);
            var text = await res.Content.ReadAsStringAsync();
            Store.Log($"F1 Party {command}{(which != null ? " " + which : "")}: {(int)res.StatusCode} {text.Trim()}");
            return res.IsSuccessStatusCode;
        }
        catch (Exception e) { Store.Log($"f1party {command}: " + e.Message); return false; }
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
