using System.Runtime.InteropServices;

namespace F1;

/// <summary>One monitor's wallpaper as it was before F1 made the background black.</summary>
public sealed class WallpaperEntry
{
    public string Monitor { get; set; } = "";
    public string Path { get; set; } = "";
}

/// <summary>
/// While F1 runs, the desktop background is plain black on every monitor, so a pixel between two
/// windows shows as black instead of a bright wallpaper. Closing F1 puts each monitor's own wallpaper
/// back. What was there is kept in the settings, so it survives a restart of the plugin.
/// </summary>
public static class Wallpaper
{
    /// <summary>Black background for F1 (when that setting is on); does nothing when it already is.</summary>
    public static void BlackForF1()
    {
        var s = Store.Current;
        if (!s.BlackWallpaper || s.WallpaperRestore != null || !OperatingSystem.IsWindows()) return;
        try
        {
            var w = (IDesktopWallpaper)new DesktopWallpaperClass();
            var saved = new List<WallpaperEntry>();
            for (uint i = 0; i < w.GetMonitorDevicePathCount(); i++)
            {
                var id = w.GetMonitorDevicePathAt(i);
                try { saved.Add(new WallpaperEntry { Monitor = id, Path = w.GetWallpaper(id) ?? "" }); } catch { }
            }
            s.WallpaperRestore = saved;
            s.WallpaperColor = w.GetBackgroundColor();
            Store.Save(); // first remember, then change: a crash in between still restores
            w.SetBackgroundColor(0x000000);
            foreach (var e in saved)
                try { w.SetWallpaper(e.Monitor, ""); } catch { } // monitors that are not connected refuse
            Store.Log("achtergrond: zwart");
        }
        catch (Exception e) { Store.Log("achtergrond zwart: " + e.Message); }
    }

    /// <summary>Each monitor's own wallpaper back (only when F1 changed it).</summary>
    public static void RestoreAfterF1()
    {
        var s = Store.Current;
        if (s.WallpaperRestore is not { } saved || !OperatingSystem.IsWindows()) return;
        try
        {
            var w = (IDesktopWallpaper)new DesktopWallpaperClass();
            if (s.WallpaperColor is { } c) w.SetBackgroundColor(c);
            foreach (var e in saved.Where(e => e.Path.Length > 0))
                try { w.SetWallpaper(e.Monitor, e.Path); } catch { }
            Store.Log("achtergrond: terug");
        }
        catch (Exception e) { Store.Log("achtergrond terug: " + e.Message); }
        s.WallpaperRestore = null;
        s.WallpaperColor = null;
        Store.Save();
    }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint index);
        uint GetMonitorDevicePathCount();
        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out Rect rect);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        // Further members (position, slideshow, enable) are not used; leaving them out keeps the vtable valid.
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")] private class DesktopWallpaperClass { }
}
