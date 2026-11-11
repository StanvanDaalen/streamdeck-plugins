using System.ComponentModel;
using System.Diagnostics;

namespace F1;

/// <summary>
/// Linux (OpenDeck): finding MultiViewer and Ultimate F1 Viewer, their settings folders, and asking
/// about a missing app. Not used on Windows.
/// </summary>
public static class LinuxApps
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// The app's program: a fixed install path (.deb/.rpm), a Flatpak launcher, a .desktop entry that
    /// points at it (AppImageLauncher, Gear Lever, a hand-made shortcut), or an AppImage / unpacked zip
    /// in the usual download folders. <paramref name="key"/> is matched in names, ignoring case.
    /// </summary>
    public static string? Find(string key, params string[] fixedPaths)
    {
        foreach (var p in fixedPaths) if (IsProgram(p)) return p;

        // Flatpak exports a launcher per app id; it passes extra arguments on to the app.
        foreach (var dir in new[] { "/var/lib/flatpak/exports/bin", Path.Combine(Home, ".local/share/flatpak/exports/bin") })
            foreach (var f in Files(dir))
                if (Path.GetFileName(f).Contains(key, StringComparison.OrdinalIgnoreCase)) return f;

        foreach (var dir in new[] { Path.Combine(Home, ".local/share/applications"), "/usr/share/applications", "/usr/local/share/applications" })
            foreach (var f in Files(dir))
            {
                if (!f.EndsWith(".desktop") || !Path.GetFileName(f).Contains(key, StringComparison.OrdinalIgnoreCase)) continue;
                if (ExecOf(f) is { } exe) return exe;
            }

        foreach (var dir in new[] { "Applications", "AppImages", ".local/bin", "bin", "Downloads", "Apps", "" }.Select(d => Path.Combine(Home, d)))
        {
            foreach (var f in Files(dir))
                if (Path.GetFileName(f).Contains(key, StringComparison.OrdinalIgnoreCase) &&
                    (f.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase) || IsProgram(f)))
                    return f;
            // An unpacked zip: a folder named after the app with the program inside.
            foreach (var sub in Dirs(dir).Where(d => Path.GetFileName(d).Contains(key, StringComparison.OrdinalIgnoreCase)))
                foreach (var f in Files(sub))
                    if (Path.GetFileName(f).StartsWith(key, StringComparison.OrdinalIgnoreCase) && IsProgram(f)) return f;
        }
        return null;
    }

    /// <summary>The program in a .desktop file's Exec line, when it is a file (not "flatpak run …" or "env …").</summary>
    private static string? ExecOf(string desktopFile)
    {
        try
        {
            var line = File.ReadLines(desktopFile).FirstOrDefault(l => l.StartsWith("Exec="));
            if (line == null) return null;
            var cmd = line[5..].Trim();
            var exe = cmd.StartsWith('"') ? cmd[1..].Split('"')[0] : cmd.Split(' ')[0];
            return exe.StartsWith('/') && File.Exists(exe) && !Path.GetFileName(exe).Equals("flatpak") && !Path.GetFileName(exe).Equals("env") ? exe : null;
        }
        catch { return null; }
    }

    private static bool IsProgram(string path)
    {
        try
        {
            if (!File.Exists(path) || OperatingSystem.IsWindows()) return false;
            return (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch { return false; }
    }

    private static IEnumerable<string> Files(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetFiles(dir) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> Dirs(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>
    /// An Electron app's settings file: ~/.config/&lt;app&gt;/&lt;file&gt; (on Windows %APPDATA%), or for a Flatpak
    /// ~/.var/app/&lt;id&gt;/config/&lt;app&gt;/&lt;file&gt;.
    /// </summary>
    public static string ConfigFile(string appDir, string file)
    {
        var normal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appDir, file);
        if (OperatingSystem.IsWindows() || File.Exists(normal)) return normal;
        foreach (var app in Dirs(Path.Combine(Home, ".var/app")))
        {
            var p = Path.Combine(app, "config", appDir, file);
            if (File.Exists(p)) return p;
        }
        return normal;
    }

    private static readonly HashSet<string> _opened = new();

    /// <summary>
    /// A yes/no question through zenity or kdialog (whichever is there); yes opens the download page
    /// with xdg-open. Without either, a notification is shown and the page opened once per run.
    /// </summary>
    public static void OfferDownload(string app, string why, string url)
    {
        const string title = "F1 voor Stream Deck";
        var text = $"{app} is niet geïnstalleerd.\n\n{why}\n\nDownloadpagina openen?";
        bool yes;
        if (Run("zenity", "--question", "--title", title, "--text", text) is { } z) yes = z == 0;
        else if (Run("kdialog", "--title", title, "--yesno", text) is { } k) yes = k == 0;
        else
        {
            Run("notify-send", title, $"{app} is niet geïnstalleerd. {why}");
            lock (_opened) yes = _opened.Add(app);
        }
        if (yes && Run("xdg-open", url) is null) Store.Log("downloadpagina: xdg-open niet gevonden");
    }

    /// <summary>Runs a program and waits for it; null when it is not installed.</summary>
    private static int? Run(string file, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p == null) return null;
            // xdg-open and notify-send return at once; a question waits for the answer.
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Win32Exception) { return null; }
        catch (Exception e) { Store.Log($"{file}: {e.Message}"); return null; }
    }
}
