using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace F1;

/// <summary>Where an app's main window goes, in physical screen pixels.</summary>
public sealed class AppPlace
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }

    public override string ToString() => Maximized ? $"gemaximaliseerd op {X},{Y}" : $"{Width}×{Height} op {X},{Y}";
}

/// <summary>
/// Starts MultiViewer and Ultimate F1 Viewer and puts their main windows in place, without
/// PowerToys Workspaces. Apps that are already open are left where they are.
/// </summary>
public static class Apps
{
    /// <summary>
    /// The installed MultiViewer version itself (app-x.y.z), not the updater stub in front of it, so the
    /// debug-port argument reaches the app.
    /// </summary>
    public static string MultiViewerExe
    {
        get
        {
            if (Store.Current.MultiViewerPath is { Length: > 0 } own) return own;
            if (!OperatingSystem.IsWindows())
                return LinuxApps.Find("multiviewer",
                    "/usr/bin/multiviewer", "/usr/bin/multiviewer-for-f1",
                    "/usr/lib/multiviewer/multiviewer", "/usr/lib/multiviewer-for-f1/multiviewer-for-f1",
                    "/opt/MultiViewer/multiviewer", "/opt/multiviewer/multiviewer")
                       ?? "/usr/bin/multiviewer";
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "multiviewer");
            var latest = Directory.Exists(root)
                ? Directory.GetDirectories(root, "app-*")
                    .Select(d => (Dir: d, Ver: Version.TryParse(Path.GetFileName(d)[4..], out var v) ? v : new Version(0, 0)))
                    .OrderByDescending(x => x.Ver).Select(x => Path.Combine(x.Dir, "MultiViewer.exe")).FirstOrDefault(File.Exists)
                : null;
            return latest ?? Path.Combine(root, "MultiViewer.exe");
        }
    }
    public static bool MultiViewerInstalled => File.Exists(MultiViewerExe);

    /// <summary>Ultimate F1 Viewer's exe, wherever it was installed (null when it is not installed).</summary>
    public static string? Uf1Exe
    {
        get
        {
            if (Store.Current.Uf1Path is { Length: > 0 } own) return File.Exists(own) ? own : null;
            // Linux: the viewer comes as an AppImage (wherever it was saved) or through a .desktop entry.
            if (!OperatingSystem.IsWindows())
                return LinuxApps.Find("ultimate-f1viewer", "/opt/Ultimate-F1Viewer-With-F1MV/ultimate-f1viewer-with-f1mv");
            // The installer's own record first (any install folder), then the usual places.
            foreach (var root in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
                foreach (var path in new[] { @"Software\Microsoft\Windows\CurrentVersion\Uninstall", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
                {
                    using var k = root.OpenSubKey(path);
                    if (k == null) continue;
                    foreach (var name in k.GetSubKeyNames())
                    {
                        using var app = k.OpenSubKey(name);
                        if (app?.GetValue("DisplayName") is not string dn || !dn.Contains("Ultimate-F1Viewer", StringComparison.OrdinalIgnoreCase)) continue;
                        var icon = (app.GetValue("DisplayIcon") as string ?? "").Split(',')[0].Trim('"');
                        if (File.Exists(icon)) return icon;
                    }
                }
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ultimate-F1Viewer-With-F1MV", "Ultimate-F1Viewer-With-F1MV.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ultimate-f1viewer-with-f1mv", "Ultimate-F1Viewer-With-F1MV.exe"),
            };
            return candidates.FirstOrDefault(File.Exists);
        }
    }

    public const string MultiViewerDownload = "https://multiviewer.app/download";
    public const string Uf1Download = "https://github.com/MRAJEKO/UF1-Viewer-with-F1MV/releases/latest";

    private static readonly HashSet<string> _offered = new();

    /// <summary>
    /// Tells the user an app is missing and offers its download page (a yes/no box; yes opens the page
    /// in the browser). With <paramref name="oncePerRun"/>, it is asked only once until the plugin restarts.
    /// </summary>
    public static void OfferDownload(string app, string why, string url, bool oncePerRun)
    {
        lock (_offered) { if (oncePerRun && !_offered.Add(app)) return; }
        Store.Log($"{app} is niet geïnstalleerd");
        if (!OperatingSystem.IsWindows())
        {
            LinuxApps.OfferDownload(app, why, url);
            return;
        }
        var answer = MessageBox(IntPtr.Zero,
            $"{app} is niet geïnstalleerd.\n\n{why}\n\nDownloadpagina openen?",
            "F1 voor Stream Deck", MB_YESNO | MB_ICONINFORMATION | MB_SETFOREGROUND | MB_TOPMOST);
        if (answer == IDYES)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception e) { Store.Log("downloadpagina: " + e.Message); }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBox(IntPtr hwnd, string text, string caption, uint type);
    private const uint MB_YESNO = 0x4, MB_ICONINFORMATION = 0x40, MB_SETFOREGROUND = 0x10000, MB_TOPMOST = 0x40000;
    private const int IDYES = 6;

    /// <summary>The viewer's main window; MultiViewer's main window is "MultiViewer" or the open session's window.</summary>
    private static bool IsUf1Main(string title) => title == "Ultimate Formula 1 Viewer";

    private static bool IsMultiViewerMain(string title, ICollection<string> playerTitles) =>
        title == "MultiViewer" ||
        (title.EndsWith("— MultiViewer") && !title.Contains("Live Timing") &&
         !playerTitles.Any(p => title.StartsWith(p + " —")));

    public static async Task<bool> LaunchF1AppsAsync()
    {
        var mv = LaunchAsync(MultiViewer.Process, MultiViewerExe, $"--remote-debugging-port={Store.Current.MvPort}",
            Store.Current.PlaceMultiViewer, t => IsMultiViewerMain(t, Array.Empty<string>()));
        // Ultimate F1 Viewer is optional: without it everything else still works.
        var uf1 = Uf1Exe is { } exe
            ? LaunchAsync(Uf1.Process, exe, $"--remote-debugging-port={Store.Current.Uf1Port}", Store.Current.PlaceUf1, IsUf1Main)
            : Task.FromResult(true);
        var results = await Task.WhenAll(mv, uf1);
        return results.All(r => r);
    }

    /// <summary>Starts the app when it is not running and places its main window.</summary>
    public static async Task<bool> LaunchAsync(string process, string exe, string args, AppPlace? place, Func<string, bool> isMain)
    {
        if (Desktops.IsRunning(process)) return true;
        if (!File.Exists(exe)) { Store.Log($"{process} niet gevonden: {exe}"); return false; }
        Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
        // Window placement uses Windows' own window calls; elsewhere the window manager decides.
        if (place == null || !OperatingSystem.IsWindows()) return true;

        // Wait for the main window; when only one window has appeared after a while, that one is it.
        IntPtr hwnd = IntPtr.Zero;
        for (int i = 0; i < 60 && hwnd == IntPtr.Zero; i++)
        {
            await Task.Delay(500);
            var windows = Windows(process);
            hwnd = windows.FirstOrDefault(w => isMain(w.Title)).Hwnd;
            if (hwnd == IntPtr.Zero && i >= 20 && windows.Count == 1) hwnd = windows[0].Hwnd;
        }
        if (hwnd == IntPtr.Zero) { Store.Log($"{process}: hoofdvenster niet gevonden"); return true; }
        await Task.Delay(500); // apps often set their own last position right after showing
        Place(hwnd, place);
        return true;
    }

    private static void Place(IntPtr hwnd, AppPlace p)
    {
        ShowWindow(hwnd, SW_RESTORE);
        SetWindowPos(hwnd, IntPtr.Zero, p.X, p.Y, p.Width, p.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        if (p.Maximized) ShowWindow(hwnd, SW_MAXIMIZE);
    }

    /// <summary>Records where the main windows are now (used by "Huidige posities opslaan").</summary>
    public static (AppPlace? MultiViewer, AppPlace? Uf1) Capture(ICollection<string> playerTitles)
    {
        if (!OperatingSystem.IsWindows()) return (null, null);
        AppPlace? Get(string process, Func<string, bool> isMain)
        {
            var hwnd = Windows(process).FirstOrDefault(w => isMain(w.Title)).Hwnd;
            if (hwnd == IntPtr.Zero) return null;
            var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            GetWindowPlacement(hwnd, ref wp);
            bool max = wp.showCmd == SW_SHOWMAXIMIZED;
            // Maximised: store its real normal size and place (on the same monitor) plus the flag.
            var n = wp.normal;
            if (max) return new AppPlace { X = n.Left, Y = n.Top, Width = n.Right - n.Left, Height = n.Bottom - n.Top, Maximized = true };
            GetWindowRect(hwnd, out var r);
            return new AppPlace { X = r.Left, Y = r.Top, Width = r.Right - r.Left, Height = r.Bottom - r.Top };
        }
        return (Get(MultiViewer.Process, t => IsMultiViewerMain(t, playerTitles)), Get(Uf1.Process, IsUf1Main));
    }

    /// <summary>
    /// One-off: take the window places from the PowerToys workspace "F1" (if it still exists).
    /// Workspaces stores them DPI-unaware per monitor; this converts them to physical pixels.
    /// </summary>
    public static void ImportFromWorkspace(F1Settings s)
    {
        if (!OperatingSystem.IsWindows() || (s.PlaceMultiViewer != null && s.PlaceUf1 != null)) return;
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "PowerToys", "Workspaces", "workspaces.json");
            if (!File.Exists(path)) return;
            var ws = JsonNode.Parse(File.ReadAllText(path))?["workspaces"]?.AsArray()
                .FirstOrDefault(w => string.Equals((string?)w?["name"], "F1", StringComparison.OrdinalIgnoreCase));
            if (ws == null) return;
            var monitors = ws["monitor-configuration"]?.AsArray().OfType<JsonObject>().ToList() ?? new();
            foreach (var app in ws["applications"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            {
                var mon = monitors.FirstOrDefault(m => (int?)m["monitor-number"] == (int?)app["monitor"]);
                var pos = app["position"];
                if (mon == null || pos == null) continue;
                double scale = ((int?)mon["dpi"] ?? 96) / 96.0;
                var aware = mon["monitor-rect-dpi-aware"]; var unaware = mon["monitor-rect-dpi-unaware"];
                int ox = (int?)aware?["left"] ?? 0, oy = (int?)aware?["top"] ?? 0;
                int ux = (int?)unaware?["left"] ?? 0, uy = (int?)unaware?["top"] ?? 0;
                var place = new AppPlace
                {
                    X = ox + (int)Math.Round((((int?)pos["X"] ?? 0) - ux) * scale),
                    Y = oy + (int)Math.Round((((int?)pos["Y"] ?? 0) - uy) * scale),
                    Width = (int)Math.Round(((int?)pos["width"] ?? 1280) * scale),
                    Height = (int)Math.Round(((int?)pos["height"] ?? 800) * scale),
                    Maximized = (bool?)app["maximized"] ?? false,
                };
                var exe = (string?)app["application-path"] ?? "";
                if (exe.EndsWith("MultiViewer.exe", StringComparison.OrdinalIgnoreCase)) s.PlaceMultiViewer ??= place;
                else if (exe.Contains("Ultimate-F1Viewer", StringComparison.OrdinalIgnoreCase)) s.PlaceUf1 ??= place;
            }
            Store.Log($"posities uit werkruimte: MultiViewer {s.PlaceMultiViewer}, viewer {s.PlaceUf1}");
        }
        catch (Exception e) { Store.Log("werkruimte lezen: " + e.Message); }
    }

    /// <summary>
    /// Stacks MultiViewer's windows by height: a window that starts higher on the screen lies in front
    /// of the ones below it, so its bottom edge covers their title bars. Works for any layout, on any
    /// monitor arrangement. The main window is left out. Nothing is activated (focus stays put).
    /// </summary>
    /// <param name="skipTitles">Titles of windows that do not take part (MultiViewer's main window).</param>
    public static int StackByHeight(ICollection<string> skipTitles)
    {
        if (!OperatingSystem.IsWindows()) return 0;
        var windows = Windows(MultiViewer.Process)
            .Where(w => w.Title != "MultiViewer" && !skipTitles.Contains(w.Title))
            .Select(w => { GetWindowRect(w.Hwnd, out var r); return (w.Hwnd, r.Top, r.Left); })
            .OrderByDescending(w => w.Top).ThenByDescending(w => w.Left) // lowest first, so the highest ends on top
            .ToList();
        foreach (var w in windows)
            SetWindowPos(w.Hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        return windows.Count;
    }

    private static readonly IntPtr HWND_TOP = IntPtr.Zero;

    /// <summary>
    /// Keeps the viewer's "Battle mode" windows in front of its "Flag Display" windows (both are
    /// always-on-top; the flag display is the coloured background behind the transparent battle mode).
    /// Only acts when a flag display is actually above a battle mode window, and never takes focus.
    /// </summary>
    public static void KeepBattleModeOnTop()
    {
        if (!OperatingSystem.IsWindows()) return;
        var windows = Windows(Uf1.Process);
        var flags = windows.Where(w => w.Title == "Flag Display").Select(w => w.Hwnd).ToHashSet();
        if (flags.Count == 0) return;
        foreach (var battle in windows.Where(w => w.Title == "Battle mode").Select(w => w.Hwnd))
        {
            bool covered = false;
            for (var h = GetWindow(battle, GW_HWNDPREV); h != IntPtr.Zero; h = GetWindow(h, GW_HWNDPREV))
                if (flags.Contains(h)) { covered = true; break; }
            if (covered)
                SetWindowPos(battle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }
    }

    // ------------------------------------------------------------ native

    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    private const uint GW_HWNDPREV = 3, SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1, SWP_NOOWNERZORDER = 0x200;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private static List<(IntPtr Hwnd, string Title)> Windows(string process)
    {
        var list = new List<(IntPtr, string)>();
        foreach (var hwnd in Desktops.AppWindows(process))
        {
            var sb = new StringBuilder(512);
            GetWindowText(hwnd, sb, sb.Capacity);
            list.Add((hwnd, sb.ToString()));
        }
        return list;
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT { public int length, flags, showCmd; public POINT min, max; public RECT normal; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT wp);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    private const int SW_RESTORE = 9, SW_MAXIMIZE = 3, SW_SHOWMAXIMIZED = 3;
    private const uint SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    /// <summary>Window positions are in physical pixels only when the process is per-monitor DPI aware.</summary>
    public static void MakeDpiAware()
    {
        if (OperatingSystem.IsWindows()) SetProcessDpiAwarenessContext(new IntPtr(-4)); // PER_MONITOR_AWARE_V2
    }
}
