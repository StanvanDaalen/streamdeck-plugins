using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace F1;

/// <summary>
/// While F1 runs, the Windows taskbar shows on the main monitor only (Settings → Taskbar → "Show my
/// taskbar on all displays" off). Switched directly in the registry and announced to the shell, so the
/// extra taskbars simply disappear: no settings window, no Explorer restart.
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

    /// <summary>
    /// Main monitor only for F1 (when that setting is on), remembering that it was on all displays, so
    /// closing F1 can put it back. Does nothing when it already is on the main monitor only.
    /// </summary>
    public static void MainOnlyForF1()
    {
        var s = Store.Current;
        if (!s.TaskbarMainOnly || OnAllDisplays() != true) return;
        SetOnAllDisplays(false);
        s.TaskbarRestore = true;
        Store.Save();
        Store.Log("taakbalk: alleen op het hoofdscherm");
    }

    /// <summary>Taskbar back on all displays, as before F1 (only when F1 changed it).</summary>
    public static void RestoreAfterF1()
    {
        var s = Store.Current;
        if (!s.TaskbarRestore) return;
        SetOnAllDisplays(true);
        s.TaskbarRestore = false;
        Store.Save();
        Store.Log("taakbalk: weer op alle schermen");
    }

    private static readonly IntPtr HWND_BROADCAST = new(0xffff);
    private const uint WM_SETTINGCHANGE = 0x001A, SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam,
        uint flags, uint timeout, out UIntPtr result);
}
