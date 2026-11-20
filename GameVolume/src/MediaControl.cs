using Windows.Media.Control;

namespace GameVolume;

/// <summary>
/// Play/pause and track skipping for one specific app, through the Windows media sessions
/// (the same ones the volume flyout shows). Unlike a media key, this never hits another player.
/// </summary>
public static class MediaControl
{
    private static GlobalSystemMediaTransportControlsSessionManager? _manager;

    private static async Task<GlobalSystemMediaTransportControlsSession?> FindAsync(string process)
    {
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var name = process.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
        // Chrome reports "Chrome", Spotify "Spotify.exe", Edge "MSEdge": the process name is part of the id.
        return _manager.GetSessions().FirstOrDefault(s => s.SourceAppUserModelId.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Runs the action and returns a short status for the dial, or null when the app plays no media.</summary>
    public static async Task<string?> RunAsync(string process, string action)
    {
        try
        {
            var session = await FindAsync(process);
            if (session == null) return null;
            switch (action)
            {
                case "playpause":
                    bool playing = session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    await session.TryTogglePlayPauseAsync();
                    return playing ? "Paused" : "Playing";
                case "next":
                    await session.TrySkipNextAsync();
                    return "Next";
                case "previous":
                    await session.TrySkipPreviousAsync();
                    return "Previous";
            }
        }
        catch (Exception e) { SettingsStore.Log("media: " + e.Message); }
        return null;
    }
}
