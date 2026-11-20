using System.Runtime.InteropServices;

namespace F1;

/// <summary>
/// Windows taskbar auto-hide, switched directly through the shell (the same setting as "Automatically
/// hide the taskbar" in Settings), so nothing opens on screen. Applies to the taskbars on all monitors.
/// </summary>
public static class Taskbar
{
    /// <summary>True/false on Windows, null where there is no Windows taskbar.</summary>
    public static bool? IsAutoHide()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        return ((int)SHAppBarMessage(ABM_GETSTATE, ref data) & ABS_AUTOHIDE) != 0;
    }

    public static void SetAutoHide(bool on)
    {
        if (!OperatingSystem.IsWindows()) return;
        var data = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = FindWindow("Shell_TrayWnd", null),
            lParam = on ? ABS_AUTOHIDE : ABS_ALWAYSONTOP,
        };
        SHAppBarMessage(ABM_SETSTATE, ref data);
    }

    /// <summary>
    /// Turns auto-hide on for F1 (when that setting is on) and remembers that it was off, so closing
    /// F1 can put it back. Does nothing when auto-hide was already on.
    /// </summary>
    public static void HideForF1()
    {
        var s = Store.Current;
        if (!s.HideTaskbar || IsAutoHide() != false) return;
        SetAutoHide(true);
        s.TaskbarRestore = true;
        Store.Save();
        Store.Log("taakbalk: automatisch verbergen aan");
    }

    /// <summary>Puts the taskbar back as it was before F1 (only when F1 changed it).</summary>
    public static void RestoreAfterF1()
    {
        var s = Store.Current;
        if (!s.TaskbarRestore) return;
        SetAutoHide(false);
        s.TaskbarRestore = false;
        Store.Save();
        Store.Log("taakbalk: weer zichtbaar");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public int lParam;
    }

    private const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10;
    private const int ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;

    [DllImport("shell32.dll")] private static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? name);
}
