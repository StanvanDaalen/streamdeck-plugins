using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace F1;

/// <summary>
/// What F1 does with the Windows taskbar while it runs (setting TaskbarMode):
/// "main" = on the main monitor only (Settings → Taskbar → "Show my taskbar on all displays" off),
/// "autohide" = automatically hide the taskbar (on every monitor), "none" = leave it as it is.
/// Both are switched directly and announced to the shell: no settings window, no Explorer restart.
/// Closing F1 puts back only what F1 changed.
/// </summary>
public static class Taskbar
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string Value = "MMTaskbarEnabled";

    /// <summary>True when the taskbar shows on all displays; null where there is no Windows taskbar.</summary>
    public static bool? OnAllDisplays()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        return k?.GetValue(Value) is int v ? v != 0 : null;
    }

    public static void SetOnAllDisplays(bool on)
    {
        if (!OperatingSystem.IsWindows()) return;
        using (var k = Registry.CurrentUser.CreateSubKey(Key))
            k.SetValue(Value, on ? 1 : 0, RegistryValueKind.DWord);
        // The shell rereads its taskbar settings on this broadcast (what the Settings app does too).
        SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "TraySettings", SMTO_ABORTIFHUNG, 2000, out _);
    }

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
    /// Applies the chosen taskbar mode for F1 and remembers what it changed, so closing F1 can put it
    /// back. Does nothing when the taskbar already is that way.
    /// </summary>
    public static void ApplyForF1()
    {
        var s = Store.Current;
        switch (s.TaskbarMode)
        {
            case "main" when OnAllDisplays() == true:
                SetOnAllDisplays(false);
                s.TaskbarRestore = true;
                Store.Save();
                Store.Log("taakbalk: alleen op het hoofdscherm");
                break;
            case "autohide" when IsAutoHide() == false:
                SetAutoHide(true);
                s.TaskbarRestoreAutoHide = true;
                Store.Save();
                Store.Log("taakbalk: automatisch verbergen aan");
                break;
        }
    }

    /// <summary>Taskbar back as it was before F1 (only what F1 changed).</summary>
    public static void RestoreAfterF1()
    {
        var s = Store.Current;
        if (!s.TaskbarRestore && !s.TaskbarRestoreAutoHide) return;
        if (s.TaskbarRestore) { SetOnAllDisplays(true); Store.Log("taakbalk: weer op alle schermen"); }
        if (s.TaskbarRestoreAutoHide) { SetAutoHide(false); Store.Log("taakbalk: weer altijd zichtbaar"); }
        s.TaskbarRestore = s.TaskbarRestoreAutoHide = false;
        Store.Save();
    }

    private static readonly IntPtr HWND_BROADCAST = new(0xffff);
    private const uint WM_SETTINGCHANGE = 0x001A, SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam,
        uint flags, uint timeout, out UIntPtr result);

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
