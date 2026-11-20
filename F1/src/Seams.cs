using System.Runtime.InteropServices;
using System.Text;

namespace F1;

/// <summary>
/// Makes F1's windows join without anything showing between them:
/// 1. Windows 11 draws a 1–2 px semi-transparent edge and rounded corners around every window; the
///    desktop shows through them as a thin line. Those are switched off on all F1 windows.
/// 2. Window edges that lie within a few pixels of each other (or of the screen edge) are put exactly
///    together; restoring a setup lands a pixel or two off because of scaling and aspect ratios.
/// The overlays of Ultimate F1 Viewer (flags, battle mode, crash detection, sector statuses) are only
/// given square corners; they are not moved.
/// </summary>
public static class Seams
{
    /// <summary>Edges this close together are meant to touch.</summary>
    private const int Snap = 8;

    private const string Uf1MainTitle = "Ultimate Formula 1 Viewer";

    private static readonly string[] Overlays ={ "Battle mode", "Flag Display", "Crash Detection", "Sector Statuses" };

    /// <summary>Edges and corners off on every F1 window (cheap; new windows need it too).</summary>
    public static int Flatten()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        var old = SetThreadDpiAwarenessContext(PerMonitorV2);
        try
        {
            int n = 0;
            foreach (var w in Windows(includeOtherDesktops: true))
            {
                int none = DWMWA_COLOR_NONE, square = DWMWCP_DONOTROUND;
                DwmSetWindowAttribute(w.Hwnd, DWMWA_BORDER_COLOR, ref none, 4);
                DwmSetWindowAttribute(w.Hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref square, 4);
                n++;
            }
            return n;
        }
        finally { SetThreadDpiAwarenessContext(old); }
    }

    /// <summary>Flattens, then puts nearly touching edges exactly together. Returns the windows moved.</summary>
    public static int Close()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        Flatten();
        var old = SetThreadDpiAwarenessContext(PerMonitorV2);
        try
        {
            int moved = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                var wins = Windows().Where(w => !Overlays.Any(o => w.Title.StartsWith(o, StringComparison.Ordinal))).ToList();
                var work = Monitors();
                var target = wins.ToDictionary(w => w.Hwnd, w => w.Visible);

                foreach (var mon in work)
                {
                    var here = wins.Where(w => Contains(mon, (w.Visible.L + w.Visible.R) / 2, (w.Visible.T + w.Visible.B) / 2)).ToList();
                    Cluster(here, mon.L, mon.R, vertical: true, target);
                    Cluster(here, mon.T, mon.B, vertical: false, target);
                }

                int changed = 0;
                foreach (var w in wins)
                {
                    var t = target[w.Hwnd];
                    if (t.Equals(w.Visible)) continue;
                    int x = w.Outer.L + (t.L - w.Visible.L), y = w.Outer.T + (t.T - w.Visible.T);
                    int r = w.Outer.R + (t.R - w.Visible.R), b = w.Outer.B + (t.B - w.Visible.B);
                    SetWindowPos(w.Hwnd, IntPtr.Zero, x, y, r - x, b - y, SWP_NOZORDER | SWP_NOACTIVATE);
                    changed++;
                }
                if (changed == 0) break;
                moved = Math.Max(moved, changed);
                Thread.Sleep(300);
            }
            return moved;
        }
        finally { SetThreadDpiAwarenessContext(old); }
    }

    /// <summary>
    /// Groups the edges along one axis that lie within <see cref="Snap"/> px of each other and gives
    /// each group one position: the screen edge when it is in the group, else the middle of the group.
    /// </summary>
    private static void Cluster(List<Win> wins, int lo, int hi, bool vertical, Dictionary<IntPtr, Rect> target)
    {
        var edges = new List<(int Pos, IntPtr Hwnd, bool Start)>();
        foreach (var w in wins)
        {
            edges.Add((vertical ? w.Visible.L : w.Visible.T, w.Hwnd, true));
            edges.Add((vertical ? w.Visible.R : w.Visible.B, w.Hwnd, false));
        }
        edges.Sort((a, b) => a.Pos.CompareTo(b.Pos));
        int i = 0;
        while (i < edges.Count)
        {
            int j = i;
            while (j + 1 < edges.Count && edges[j + 1].Pos - edges[j].Pos <= Snap) j++;
            var group = edges.GetRange(i, j - i + 1);
            int pos;
            if (group.Any(e => Math.Abs(e.Pos - lo) <= Snap)) pos = lo;
            else if (group.Any(e => Math.Abs(e.Pos - hi) <= Snap)) pos = hi;
            else pos = group.OrderBy(e => e.Pos).ElementAt(group.Count / 2).Pos;
            // Edges past the screen edge on purpose (more than Snap) are left alone by the test above.
            foreach (var e in group)
            {
                var t = target[e.Hwnd];
                if (vertical) { if (e.Start) t.L = pos; else t.R = pos; }
                else { if (e.Start) t.T = pos; else t.B = pos; }
                target[e.Hwnd] = t;
            }
            i = j + 1;
        }
    }

    private static bool Contains(Rect m, int x, int y) => x >= m.L && x < m.R && y >= m.T && y < m.B;

    private sealed record Win(IntPtr Hwnd, string Title, Rect Visible, Rect Outer);

    /// <summary>F1's windows that are not minimised, on the desktop that shows (or on any desktop).</summary>
    private static List<Win> Windows(bool includeOtherDesktops = false)
    {
        var list = new List<Win>();
        foreach (var h in Desktops.AppWindows(MultiViewer.Process, Uf1.Process))
        {
            if (!IsWindowVisible(h) || IsIconic(h)) continue;
            if (!includeOtherDesktops && DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) continue; // other desktop
            if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out Rect v, Marshal.SizeOf<Rect>()) != 0) continue;
            if (!GetWindowRect(h, out Rect o)) continue;
            var sb = new StringBuilder(256);
            GetWindowText(h, sb, sb.Capacity);
            var title = sb.ToString();
            // Ultimate F1 Viewer's main window draws nothing in its outer 2 px left, right and bottom
            // (its own transparent edge, measured on screen): its content is what has to touch.
            if (title == Uf1MainTitle) { v.L += 2; v.R -= 2; v.B -= 2; }
            list.Add(new Win(h, title, v, o));
        }
        return list;
    }

    /// <summary>Work areas of all monitors (the taskbar left out), in physical pixels.</summary>
    private static List<Rect> Monitors()
    {
        var list = new List<Rect>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr dc, ref Rect r, IntPtr d) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(m, ref info)) list.Add(info.Work);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int L, T, R, B; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }

    private delegate bool MonitorEnum(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);

    private static readonly IntPtr PerMonitorV2 = new(-4);
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnum cb, IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr m, ref MonitorInfo info);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out Rect value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
}
