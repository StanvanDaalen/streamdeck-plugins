namespace GameVolume;

/// <summary>
/// The Stream Deck Now Playing Chrome extension ships inside this plugin (sdPlugin\chrome-extension). Chrome only
/// loads such an extension by hand, from a folder that must stay put, so the plugin keeps a copy in a fixed folder
/// outside Downloads and the plugin folder (which a plugin update replaces), and refreshes it after every update.
/// </summary>
public static class ChromeExtension
{
    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Stream Deck Now Playing");

    private static string Source => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "chrome-extension"));

    /// <summary>True when a browser reported only a service's name, which is what happens without the extension.</summary>
    public static volatile bool NeedsLoading;

    /// <summary>Copies the bundled extension to its fixed folder when anything in it changed.</summary>
    public static void Sync()
    {
        try
        {
            if (!Directory.Exists(Source)) return;
            Directory.CreateDirectory(Folder);
            foreach (var file in Directory.GetFiles(Source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(Folder, Path.GetRelativePath(Source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (!File.Exists(target) || !File.ReadAllBytes(target).AsSpan().SequenceEqual(File.ReadAllBytes(file)))
                    File.Copy(file, target, true);
            }
        }
        catch (Exception e) { SettingsStore.Log("chrome extension: " + e.Message); }
    }

    /// <summary>Opens the fixed folder in Explorer, for Chrome's "Load unpacked".</summary>
    public static void OpenFolder()
    {
        Sync();
        try { System.Diagnostics.Process.Start("explorer.exe", $"\"{Folder}\""); }
        catch (Exception e) { SettingsStore.Log("open extension folder: " + e.Message); }
    }
}
