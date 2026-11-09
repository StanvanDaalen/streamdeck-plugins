using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GameVolume;

public sealed record GameEntry(string Id, string Name, string? InstallDir, string? SteamAppId, string? ExactExe = null, string? IconExe = null);

public sealed class RunningGame
{
    public required GameEntry Entry { get; init; }
    public required IntPtr Window { get; set; }
    public required string? ExePath { get; set; }
    public required HashSet<int> Pids { get; init; }
    public required DateTime Started { get; init; }
}

public sealed class GameLibrary
{
    private static readonly Regex NotAGame = new(
        @"crash|report|launcher|bootstrap|easyanticheat|beservice|battleye|webhelper|cefprocess|redist|setup|unins|install|updater|overlay",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, string> KnownNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Minecraft for Windows"] = "Minecraft",
    };

    public static readonly GameEntry Minecraft = new("lunar:minecraft", "Minecraft", null, null, null,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Lunar Client", "Lunar Client.exe"));

    private List<GameEntry> _entries = new();
    public IReadOnlyList<GameEntry> Entries => _entries;
    public string? SteamRoot { get; private set; }

    public void Reload()
    {
        var list = new List<GameEntry>();
        try { LoadSteam(list); } catch (Exception e) { SettingsStore.Log("steam: " + e.Message); }
        try { LoadEpic(list); } catch (Exception e) { SettingsStore.Log("epic: " + e.Message); }
        try { LoadXbox(list); } catch (Exception e) { SettingsStore.Log("xbox: " + e.Message); }
        list.Add(Minecraft);
        foreach (var c in SettingsStore.Current.CustomGames)
            list.Add(new GameEntry("custom:" + c.Exe.ToLowerInvariant(), c.Name, null, null, c.Exe));
        _entries = list;
    }

    /// <summary>Best guess for a game that has not been seen running: the largest program in its folder.</summary>
    public static string? GuessExe(GameEntry e)
    {
        if (e.ExactExe != null) return e.ExactExe;
        if (e.InstallDir == null || !Directory.Exists(e.InstallDir)) return null;
        try
        {
            static string Letters(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            var folder = Letters(Path.GetFileName(e.InstallDir.TrimEnd('\\')));
            // A program named after its game folder beats a larger helper tool in the same folder.
            return new DirectoryInfo(e.InstallDir).EnumerateFiles("*.exe", SearchOption.AllDirectories)
                .Where(f => !NotAGame.IsMatch(Path.GetFileNameWithoutExtension(f.Name)))
                .OrderByDescending(f =>
                {
                    var name = Letters(Path.GetFileNameWithoutExtension(f.Name));
                    return name.Contains(folder) || folder.Contains(name);
                })
                .ThenByDescending(f => f.Length).FirstOrDefault()?.FullName;
        }
        catch (Exception ex) { SettingsStore.Log("guess exe: " + ex.Message); return null; }
    }

    private static bool Hidden(GameEntry e) => SettingsStore.Current.HiddenGames.Contains(e.Id);

    public GameEntry? Match(string? path, string title)
    {
        var e = MatchAny(path, title);
        return e != null && Hidden(e) ? null : e;
    }

    public static string CleanName(string name)
    {
        var s = Regex.Replace(name, "[®™©]", "");
        s = Regex.Replace(s, @"\s{2,}", " ").Trim();
        return KnownNames.TryGetValue(s, out var known) ? known : s;
    }

    private static string WithSlash(string dir) => dir.TrimEnd('\\', '/').Replace('/', '\\') + "\\";

    private void LoadSteam(List<GameEntry> list)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var root = (key?.GetValue("SteamPath") as string)?.Replace('/', '\\')
                   ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        SteamRoot = root;
        var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        var libs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        if (File.Exists(vdf))
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"(.+?)\""))
                libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));

        foreach (var lib in libs)
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                var t = File.ReadAllText(acf);
                string Field(string f) => Regex.Match(t, $"\"{f}\"\\s+\"(.*?)\"", RegexOptions.IgnoreCase).Groups[1].Value;
                var id = Field("appid");
                var name = Field("name");
                var dir = Field("installdir");
                if (id == "228980" || dir.Length == 0 || name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new GameEntry("steam:" + id, CleanName(name), WithSlash(Path.Combine(apps, "common", dir)), id));
            }
        }
    }

    private void LoadEpic(List<GameEntry> list)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*.item"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(f));
            var r = doc.RootElement;
            if (r.TryGetProperty("bIsIncompleteInstall", out var inc) && inc.ValueKind == JsonValueKind.True) continue;
            var name = r.GetProperty("DisplayName").GetString();
            var loc = r.GetProperty("InstallLocation").GetString();
            var app = r.TryGetProperty("AppName", out var a) ? a.GetString() : name;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(loc)) continue;
            list.Add(new GameEntry("epic:" + app, CleanName(name), WithSlash(loc), null));
        }
    }

    private void LoadXbox(List<GameEntry> list)
    {
        const string root = @"C:\XboxGames";
        if (!Directory.Exists(root)) return;
        foreach (var d in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(d);
            if (name.Equals("GameSave", StringComparison.OrdinalIgnoreCase) || name.Contains("Launcher", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(new GameEntry("xbox:" + name.ToLowerInvariant(), CleanName(name), WithSlash(d), null));
        }
    }

    private GameEntry? MatchAny(string? path, string title)
    {
        if (path is null)
        {
            // Anti-cheat can hide a process path: fall back to an exact window-title match.
            var norm = CleanName(title);
            return norm.Length == 0 ? null : _entries.FirstOrDefault(e => e.Name.Equals(norm, StringComparison.OrdinalIgnoreCase));
        }

        var file = Path.GetFileNameWithoutExtension(path);
        if (file.Equals("java", StringComparison.OrdinalIgnoreCase) || file.Equals("javaw", StringComparison.OrdinalIgnoreCase))
        {
            var isMc = path.Contains(@"\.lunarclient\", StringComparison.OrdinalIgnoreCase)
                       || title.StartsWith("Minecraft", StringComparison.OrdinalIgnoreCase)
                       || title.StartsWith("Lunar Client", StringComparison.OrdinalIgnoreCase);
            return isMc ? Minecraft : null;
        }

        var exact = _entries.FirstOrDefault(e => e.ExactExe != null && e.ExactExe.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;
        if (NotAGame.IsMatch(file)) return null;

        GameEntry? best = null;
        foreach (var e in _entries)
            if (e.InstallDir != null && path.StartsWith(e.InstallDir, StringComparison.OrdinalIgnoreCase)
                && (best == null || e.InstallDir.Length > best.InstallDir!.Length))
                best = e;
        return best;
    }
}

public sealed class GameDetector
{
    private readonly GameLibrary _lib;
    public GameDetector(GameLibrary lib) => _lib = lib;

    public RunningGame? FindCurrent(RunningGame? previous)
    {
        var found = Scan(previous);
        if (found.Count == 0) return null;
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint fgPid);
        return found.Values.FirstOrDefault(g => g.Pids.Contains((int)fgPid))
               ?? found.Values.OrderByDescending(g => g.Started).First();
    }

    public RunningGame? FindById(string id) => Scan(null).GetValueOrDefault(id);

    private Dictionary<string, RunningGame> Scan(RunningGame? previous)
    {
        var found = new Dictionary<string, RunningGame>();
        var best = new Dictionary<string, long>();

        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || Native.GetWindow(hwnd, 4) != IntPtr.Zero || Native.IsCloaked(hwnd)) return true;
            var title = Native.WindowTitle(hwnd);
            if (title.Length == 0) return true;
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            var path = Native.ProcessPath(pid);
            var entry = _lib.Match(path, title);
            if (entry == null) return true;

            if (!found.TryGetValue(entry.Id, out var g))
            {
                g = new RunningGame
                {
                    Entry = entry, Window = hwnd, ExePath = path, Pids = new(),
                    Started = previous?.Entry.Id == entry.Id ? previous.Started : StartTime(pid),
                };
                found[entry.Id] = g;
                best[entry.Id] = -1;
            }
            g.Pids.Add((int)pid);
            var area = Native.IsIconic(hwnd) ? 0 : Native.WindowArea(hwnd);
            if (area > best[entry.Id]) { best[entry.Id] = area; g.Window = hwnd; g.ExePath = path; }
            return true;
        }, IntPtr.Zero);

        return found;
    }

    private static DateTime StartTime(uint pid)
    {
        try { using var p = System.Diagnostics.Process.GetProcessById((int)pid); return p.StartTime; }
        catch { return DateTime.Now; }
    }
}

public sealed record AppWindow(IntPtr Window, HashSet<int> Pids, string? ExePath);

public static class WindowControl
{
    private static void ForEachAppWindow(Action<IntPtr, uint, string> visit) =>
        Native.EnumWindows((hwnd, _) =>
        {
            if (Native.IsWindowVisible(hwnd) && Native.GetWindow(hwnd, 4) == IntPtr.Zero && !Native.IsCloaked(hwnd)
                && Native.WindowTitle(hwnd).Length > 0)
            {
                Native.GetWindowThreadProcessId(hwnd, out uint pid);
                if (Native.ProcessPath(pid) is { } path) visit(hwnd, pid, path);
            }
            return true;
        }, IntPtr.Zero);

    public static List<string> ProcessesWithWindows()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ForEachAppWindow((_, _, path) => names.Add(Path.GetFileNameWithoutExtension(path)));
        return names.ToList();
    }

    public static AppWindow? FindApp(string process)
    {
        AppWindow? found = null;
        long best = -1;
        var pids = new HashSet<int>();
        ForEachAppWindow((hwnd, pid, path) =>
        {
            if (!Path.GetFileNameWithoutExtension(path).Equals(process, StringComparison.OrdinalIgnoreCase)) return;
            pids.Add((int)pid);
            var area = Native.IsIconic(hwnd) ? 0 : Native.WindowArea(hwnd);
            if (area > best) { best = area; found = new AppWindow(hwnd, pids, path); }
        });
        return found;
    }

    /// <summary>Asks every window of these processes to close, exactly like clicking its X. Never forces anything.</summary>
    public static void CloseWindows(HashSet<int> pids)
    {
        ForEachAppWindow((hwnd, pid, _) =>
        {
            if (pids.Contains((int)pid)) Native.PostMessage(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
        });
    }

    public static void ToggleApp(AppWindow app)
    {
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint pid);
        if (app.Pids.Contains((int)pid)) Minimize(app.Window);
        else Activate(app.Window);
    }

    public static bool IsForeground(RunningGame g)
    {
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint pid);
        return g.Pids.Contains((int)pid);
    }

    public static void Minimize(IntPtr hwnd) => Native.ShowWindowAsync(hwnd, 6);

    public static void Activate(IntPtr hwnd)
    {
        if (Native.IsIconic(hwnd)) Native.ShowWindowAsync(hwnd, 9);
        var fg = Native.GetForegroundWindow();
        uint fgThread = Native.GetWindowThreadProcessId(fg, out _);
        uint me = Native.GetCurrentThreadId();
        var attached = fgThread != 0 && fgThread != me && Native.AttachThreadInput(me, fgThread, true);
        Native.BringWindowToTop(hwnd);
        Native.SetForegroundWindow(hwnd);
        if (attached) Native.AttachThreadInput(me, fgThread, false);

        if (Native.GetForegroundWindow() != hwnd)
        {
            // Windows only allows a focus change right after input; a bare Alt tap satisfies that.
            Native.keybd_event(0x12, 0, 0, UIntPtr.Zero);
            Native.keybd_event(0x12, 0, 2, UIntPtr.Zero);
            Native.SetForegroundWindow(hwnd);
        }
    }
}
