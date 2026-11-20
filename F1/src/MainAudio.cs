using System.Globalization;
using System.Text.Json.Nodes;

namespace F1;

/// <summary>Sound of the stream the volume dial controls.</summary>
public sealed record AudioState(string Title, double Volume, bool Muted);

/// <summary>
/// The volume dial controls the stream on the main monitor: the largest video window of MultiViewer on
/// the primary screen (F1 Live in the usual layout). It works inside that player window through
/// MultiViewer's debug port, on the video element itself, which is what the player's own volume slider
/// does and what MultiViewer reads its state from. MultiViewer's API is not used for this: it loses
/// track of players that a setup reopened, and then the dial did nothing.
/// </summary>
public static class MainAudio
{
    private static JsonObject? _page;
    private static int _tick;

    /// <summary>Null when no stream is open.</summary>
    public static AudioState? Current { get; private set; }

    // Per window: is it on the primary screen (its origin is 0,0), how big is it, and its sound.
    private const string Probe =
        "(() => { const v = document.querySelector('video'); if (!v) return null; " +
        "return JSON.stringify({ primary: screen.availLeft === 0 && screen.availTop === 0, " +
        "area: outerWidth * outerHeight, title: document.title, vol: v.volume, muted: v.muted }); })()";

    /// <summary>Called every poll: reads the sound; looks again which window it is every few seconds.</summary>
    public static async Task RefreshAsync()
    {
        try
        {
            if (_page == null || ++_tick % 10 == 0) { await ChooseAsync(); return; }
            var s = await ProbeAsync(_page);
            if (s == null) { _page = null; Current = null; return; }
            Current = new AudioState(s.Value.Title, s.Value.Vol, s.Value.Muted);
        }
        catch { _page = null; Current = null; }
    }

    private static async Task ChooseAsync()
    {
        JsonObject? best = null; (bool Primary, long Area, string Title, double Vol, bool Muted)? bestState = null;
        foreach (var page in await Cdp.PagesAsync(Store.Current.MvPort))
        {
            (bool Primary, long Area, string Title, double Vol, bool Muted)? s;
            try { s = await ProbeAsync(page); } catch { continue; }
            if (s is not { } st) continue;
            bool better = bestState is not { } b
                || (st.Primary && !b.Primary)
                || (st.Primary == b.Primary && st.Primary && st.Area > b.Area)
                || (!st.Primary && !b.Primary && IsF1Live(st.Title) && !IsF1Live(b.Title));
            if (better) { best = page; bestState = st; }
        }
        _page = best;
        Current = bestState is { } c ? new AudioState(c.Title, c.Vol, c.Muted) : null;
    }

    private static bool IsF1Live(string title) => title.StartsWith("F1 Live", StringComparison.OrdinalIgnoreCase);

    private static async Task<(bool Primary, long Area, string Title, double Vol, bool Muted)?> ProbeAsync(JsonObject page)
    {
        if ((string?)await Cdp.EvalAsync(page, Probe, awaitPromise: false, timeoutSeconds: 3) is not { } json) return null;
        var n = JsonNode.Parse(json)!;
        var title = ((string?)n["title"] ?? "").Split(" — ")[0].Trim();
        return ((bool?)n["primary"] ?? false, (long?)n["area"] ?? 0, title, (double?)n["vol"] ?? 0, (bool?)n["muted"] ?? false);
    }

    /// <summary>Sets the volume (0–1) and turns the sound on.</summary>
    public static async Task<bool> SetVolumeAsync(double volume)
    {
        if (_page == null) await ChooseAsync();
        if (_page == null) return false;
        volume = Math.Clamp(volume, 0, 1);
        var v = volume.ToString("0.###", CultureInfo.InvariantCulture);
        var ok = (bool?)await Cdp.EvalAsync(_page,
            $"(() => {{ const e = document.querySelector('video'); if (!e) return false; e.muted = false; e.volume = {v}; return true; }})()",
            awaitPromise: false, timeoutSeconds: 3) == true;
        if (ok && Current != null) Current = Current with { Volume = volume, Muted = false };
        return ok;
    }

    public static async Task<bool> ToggleMuteAsync()
    {
        if (_page == null) await ChooseAsync();
        if (_page == null) return false;
        var muted = (bool?)await Cdp.EvalAsync(_page,
            "(() => { const e = document.querySelector('video'); if (!e) return null; e.muted = !e.muted; return e.muted; })()",
            awaitPromise: false, timeoutSeconds: 3);
        if (muted is not { } m) return false;
        if (Current != null) Current = Current with { Muted = m };
        return true;
    }
}
