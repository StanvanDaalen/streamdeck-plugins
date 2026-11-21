namespace PS5Plugin;

public static class Log
{
    // Next to the plugin itself: a folder that already exists, so a Store app (such as Claude) that
    // starts the plugin for testing does not get a private redirected copy of it.
    public static readonly string Dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    private static readonly string FilePath = Path.Combine(Dir, "plugin.log");
    private static readonly object Lock = new();

    public static void Reset()
    {
        Directory.CreateDirectory(Dir);
        try { File.WriteAllText(FilePath, ""); } catch { }
    }

    public static void Write(string msg)
    {
        lock (Lock) { try { File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss} {msg}\n"); } catch { } }
    }
}
