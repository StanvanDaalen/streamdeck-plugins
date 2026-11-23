using System.Collections.Concurrent;
using System.Drawing;

namespace GameVolume;

/// <summary>
/// Country flags for motorsport weekends (F1 TV, MotoGP, WEC), from flagcdn.com: only the two-letter country code is
/// sent. Kept on disk, so each flag is downloaded once.
/// </summary>
public static class Flags
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly ConcurrentDictionary<string, Task<EpisodeArt.Art?>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string Dir = Path.Combine(SettingsStore.Dir, "flags");

    /// <summary>The flag when it is ready; otherwise starts fetching it and returns null for now.</summary>
    public static EpisodeArt.Art? TryGet(string code)
    {
        var task = Cache.GetOrAdd(code, c => Task.Run(() => Load(c)));
        return task.IsCompletedSuccessfully ? task.Result : null;
    }

    private static async Task<EpisodeArt.Art?> Load(string code)
    {
        try
        {
            var file = Path.Combine(Dir, code.ToLowerInvariant() + ".png");
            if (!File.Exists(file))
            {
                var bytes = await Http.GetByteArrayAsync($"https://flagcdn.com/w320/{code.ToLowerInvariant()}.png");
                Directory.CreateDirectory(Dir);
                await File.WriteAllBytesAsync(file, bytes);
            }
            using var bitmap = new Bitmap(new MemoryStream(await File.ReadAllBytesAsync(file)));
            var (cover, backdrop, dim) = MediaControl.RenderFlag(bitmap);
            return cover == null ? null : new EpisodeArt.Art(cover, backdrop, dim);
        }
        catch (Exception e)
        {
            // No connection: try again in a minute instead of remembering "no flag".
            SettingsStore.Log("flag: " + e.Message);
            _ = Task.Delay(TimeSpan.FromMinutes(1)).ContinueWith(t => Cache.TryRemove(code, out _));
            return null;
        }
    }
}
