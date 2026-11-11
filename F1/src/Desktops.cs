using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace F1;

/// <summary>
/// Windows virtual desktops: the list and the current one come from the registry, switching uses the
/// Ctrl+Win shortcuts, and which desktop a window is on comes from the public IVirtualDesktopManager.
/// </summary>
public static class Desktops
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

    /// <summary>All desktops in order, and the 0-based index of the current one.</summary>
    public static (List<Guid> Ids, int Current) Info()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        var raw = k?.GetValue("VirtualDesktopIDs") as byte[];
        var cur = k?.GetValue("CurrentVirtualDesktop") as byte[];
        var ids = new List<Guid>();
        if (raw != null)
            for (int i = 0; i + 16 <= raw.Length; i += 16) ids.Add(new Guid(raw.AsSpan(i, 16)));
        if (ids.Count == 0) return (ids, 0);
        int index = cur is { Length: 16 } ? Math.Max(0, ids.IndexOf(new Guid(cur))) : 0;
        return (ids, index);
    }

    /// <summary>Switches to a desktop (0-based).</summary>
    public static async Task GoToAsync(int target)
    {
        var (ids, cur) = Info();
        target = Math.Clamp(target, 0, Math.Max(0, ids.Count - 1));
        int steps = target - cur;
        for (int i = 0; i < Math.Abs(steps); i++)
        {
            CtrlWin(steps > 0 ? VK_RIGHT : VK_LEFT);
            await Task.Delay(350);
        }
    }

    /// <summary>
    /// Goes to the desktop F1 should run on: desktop 2 when it has no windows (other than F1's own
    /// apps), otherwise a brand-new desktop. Returns its id.
    /// </summary>
    public static async Task<Guid?> PrepareF1DesktopAsync()
    {
        var (ids, _) = Info();
        if (ids.Count >= 2 && IsEmpty(ids[1], ignoreF1Apps: true))
        {
            await GoToAsync(1);
            return ids[1];
        }
        CtrlWin(VK_D); // new desktop at the end; Windows switches to it straight away
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(300);
            var (now, cur) = Info();
            if (now.Count > ids.Count) return now[cur];
        }
        Store.Log("nieuw bureaublad niet gezien");
        return null;
    }

    /// <summary>Removes a desktop when nothing is left on it, then goes to desktop 1.</summary>
    public static async Task CloseIfEmptyAsync(Guid desk)
    {
        var (ids, _) = Info();
        int index = ids.IndexOf(desk);
        if (index > 0 && IsEmpty(desk, ignoreF1Apps: false))
        {
            await GoToAsync(index);
            await Task.Delay(300);
            // Ctrl+Win+F4 closes whatever desktop is current, so only press it when that is really
            // the (still empty) F1 desktop; a dropped switch keystroke must not close another one.
            var (now, cur) = Info();
            if (cur < now.Count && now[cur] == desk && IsEmpty(desk, ignoreF1Apps: false))
            {
                CtrlWin(VK_F4);
                await Task.Delay(500);
            }
            else Store.Log("bureaublad niet gesloten: niet op het F1-bureaublad of niet leeg");
        }
        await GoToAsync(0);
    }

    /// <summary>True when no normal application window is on the desktop.</summary>
    public static bool IsEmpty(Guid desk, bool ignoreF1Apps)
    {
        IVirtualDesktopManager vdm;
        try { vdm = (IVirtualDesktopManager)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_VirtualDesktopManager)!)!; }
        catch (Exception e) { Store.Log("desktop manager: " + e.Message); return false; }

        bool empty = true;
        EnumWindows((hwnd, _) =>
        {
            if (!IsAppWindow(hwnd)) return true;
            if (vdm.GetWindowDesktopId(hwnd, out var id) != 0 || id != desk) return true;
            if (ignoreF1Apps)
            {
                GetWindowThreadProcessId(hwnd, out var pid);
                try
                {
                    var name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
                    if (name is MultiViewer.Process or Uf1.Process) return true;
                }
                catch { }
            }
            empty = false;
            return false; // one window is enough
        }, IntPtr.Zero);
        Marshal.ReleaseComObject(vdm);
        return empty;
    }

    /// <summary>Visible, titled, top-level windows that show in the taskbar.</summary>
    private static bool IsAppWindow(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd) || GetWindowTextLength(hwnd) == 0) return false;
        if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return false;
        if ((GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0) return false;
        // Cloaked by the app itself (hidden UWP frames); windows on other desktops are cloaked by the shell instead.
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked == DWM_CLOAKED_APP) return false;
        return true;
    }

    public static bool LaunchWorkspace(string name)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var launcher = new[]
        {
            Path.Combine(local, "PowerToys", "PowerToys.WorkspacesLauncher.exe"),
            @"C:\Program Files\PowerToys\PowerToys.WorkspacesLauncher.exe",
        }.FirstOrDefault(File.Exists);
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(local, "Microsoft", "PowerToys", "Workspaces", "workspaces.json")));
            var id = json?["workspaces"]?.AsArray()
                .FirstOrDefault(w => string.Equals((string?)w?["name"], name, StringComparison.OrdinalIgnoreCase))?["id"];
            if (launcher == null || id == null) { Store.Log($"workspace '{name}' of launcher niet gevonden"); return false; }
            System.Diagnostics.Process.Start(new ProcessStartInfo(launcher, $"{(string)id!} 1") { UseShellExecute = false });
            return true;
        }
        catch (Exception e) { Store.Log("workspace: " + e.Message); return false; }
    }

    /// <summary>Closes an app's windows politely and ends it when it is still running after a few seconds.</summary>
    public static async Task CloseAppAsync(string processName)
    {
        var procs = System.Diagnostics.Process.GetProcessesByName(processName);
        foreach (var p in procs) { try { p.CloseMainWindow(); } catch { } }
        for (int i = 0; i < 10 && procs.Any(p => { try { return !p.HasExited; } catch { return false; } }); i++)
            await Task.Delay(300);
        foreach (var p in procs)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch (Exception e) { Store.Log($"close {processName}: {e.Message}"); }
            p.Dispose();
        }
    }

    // ------------------------------------------------------------ native

    private static readonly Guid CLSID_VirtualDesktopManager = new("aa509086-5ca9-4c25-8f95-589d3c07b48a");

    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr hwnd, out int onCurrent);
        [PreserveSig] int GetWindowDesktopId(IntPtr hwnd, out Guid desktopId);
        [PreserveSig] int MoveWindowToDesktop(IntPtr hwnd, ref Guid desktopId);
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    private const uint GW_OWNER = 4;
    private const int GWL_EXSTYLE = -20, DWMWA_CLOAKED = 14, DWM_CLOAKED_APP = 1;
    private const long WS_EX_TOOLWINDOW = 0x80;
    private const byte VK_CONTROL = 0x11, VK_LWIN = 0x5B, VK_D = 0x44, VK_F4 = 0x73, VK_LEFT = 0x25, VK_RIGHT = 0x27;
    private const uint KEYUP = 0x2, EXTENDED = 0x1;

    private static void CtrlWin(byte vk)
    {
        uint ext = vk is VK_LEFT or VK_RIGHT ? EXTENDED : 0;
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, EXTENDED, UIntPtr.Zero);
        keybd_event(vk, 0, ext, UIntPtr.Zero);
        keybd_event(vk, 0, ext | KEYUP, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, EXTENDED | KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYUP, UIntPtr.Zero);
    }
}
