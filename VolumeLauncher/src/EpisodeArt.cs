using System.Collections.Concurrent;
using System.Drawing;
using System.Text.Json;

namespace GameVolume;

/// <summary>
/// Pictures for series episodes whose service gives the browser none (Paramount+, Netflix, …). Looks the series up
/// on TVmaze, a free public TV database without an account: only the series name is sent. Uses the episode's own
/// still, or the series poster when the episode has none. Every answer, also "not found", is asked once per run.
/// </summary>
public static class EpisodeArt
{
    public sealed record Art(string? Cover, string? Backdrop, string? BackdropDim);

    private static readonly HttpClient Http = CreateClient();
    private static readonly ConcurrentDictionary<string, Task<JsonElement?>> Shows = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Task<Art?>> Pictures = new(StringComparer.OrdinalIgnoreCase);

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("StreamDeck-VolumeLauncher/3.1");
        return http;
    }

    /// <summary>The picture when it is ready; otherwise starts looking it up and returns null for now.</summary>
    public static Art? TryGet(string series, int? season, int? episode, string title)
    {
        if (series.Length == 0) return null;
        var key = $"{series}\u0001{season}\u0001{episode}\u0001{title}";
        var task = Pictures.GetOrAdd(key, _ => Task.Run(() => Load(series, season, episode, title)));
        return task.IsCompletedSuccessfully ? task.Result : null;
    }

    private static async Task<Art?> Load(string series, int? season, int? episode, string title)
    {
        try
        {
            if (await Shows.GetOrAdd(series, FindShow) is not { } show) return null;
            string? url = null;
            if (show.TryGetProperty("_embedded", out var embedded) && embedded.TryGetProperty("episodes", out var episodes))
            {
                // By season and number when the title names them, else by the episode's name.
                var match = episodes.EnumerateArray().FirstOrDefault(e =>
                    episode != null && Int(e, "number") == episode && (season == null || Int(e, "season") == season)
                    || episode == null && Text(e, "name").Equals(title, StringComparison.OrdinalIgnoreCase));
                if (match.ValueKind == JsonValueKind.Object) url = Image(match);
            }
            url ??= Image(show);
            if (url == null) return null;
            await using var stream = await Http.GetStreamAsync(url);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            memory.Position = 0;
            using var bitmap = new Bitmap(memory);
            var (cover, backdrop, dim) = MediaControl.RenderArt(bitmap);
            return cover == null ? null : new Art(cover, backdrop, dim);
        }
        catch (Exception e)
        {
            // No connection: try again on a later episode change instead of remembering "nothing".
            SettingsStore.Log("episode art: " + e.Message);
            Shows.TryRemove(series, out _);
            _ = Task.Delay(TimeSpan.FromMinutes(1)).ContinueWith(t => Pictures.TryRemove($"{series}{season}{episode}{title}", out _));
            return null;
        }
    }

    private static async Task<JsonElement?> FindShow(string series)
    {
        var url = "https://api.tvmaze.com/singlesearch/shows?embed=episodes&q=" + Uri.EscapeDataString(series);
        using var response = await Http.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null; // 404: not a known series (sport, films, …)
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";

    private static string? Image(JsonElement e) =>
        e.TryGetProperty("image", out var img) && img.ValueKind == JsonValueKind.Object
            ? Text(img, "medium") is { Length: > 0 } m ? m : Text(img, "original") is { Length: > 0 } o ? o : null
            : null;
}
