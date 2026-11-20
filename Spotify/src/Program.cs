namespace StanSpotify;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string? Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        if (Arg("--dump") is { } file)
        {
            // Diagnostic output: what the plugin sees of Spotify right now.
            using var media = new SpotifyMedia();
            await media.RefreshAsync();
            var s = media.State;
            File.WriteAllText(file,
                $"running={SpotifyMedia.IsRunning} session={s.HasSession} playing={s.Playing} title={s.Title} artist={s.Artist}\n" +
                $"position={s.Position} duration={s.Duration} shuffle={s.Shuffle} repeat={s.Repeat} art={s.Artwork?.Length ?? 0}\n" +
                $"volume={SpotifyAudio.Volume()} muted={SpotifyAudio.Muted()}\n");
            return 0;
        }

        var port = Arg("-port");
        var uuid = Arg("-pluginUUID");
        var register = Arg("-registerEvent");
        if (port == null || uuid == null || register == null) return 1;

        try { await new Plugin(int.Parse(port), uuid, register).RunAsync(); }
        catch (Exception e) { Log.Write("fatal: " + e); return 1; }
        return 0;
    }
}

internal static class Log
{
    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StanSpotify");

    public static void Write(string line)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var path = Path.Combine(Dir, "plugin.log");
            if (File.Exists(path) && new FileInfo(path).Length > 512_000) File.Delete(path);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}\n");
        }
        catch { }
    }
}
