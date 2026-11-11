using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace F1;

/// <summary>
/// Windows virtual desktops: the list and the current one come from the registry, switching uses the
/// Ctrl+Win shortcuts, and which desktop a window is on comes from the public IVirtualDesktopManager.
/// On other systems (Linux under OpenDeck) there are no desktops or windows to manage: those calls do
/// nothing, while finding and closing the apps' processes works everywhere.
/// </summary>
public static class Desktops
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

    /// <summary>All desktops in order, and the 0-based index of the current one.</summary>
    public static (List<Guid> Ids, int Current) Info()
    {
        if (!OperatingSystem.IsWindows()) return (new List<Guid>(), 0);
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
        if (!OperatingSystem.IsWindows()) return;
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
        if (!OperatingSystem.IsWindows()) return null;
        var (ids, _) = Info();
        if (ids.Count >= 2 && IsEmpty(ids[1], ignoreF1Apps: true))
        {
            await GoToAsync(1);
            return ids[1];
        }
        return await NewDesktopAsync();
    }

    /// <summary>Creates a desktop at the end (Windows switches to it) and returns its id.</summary>
    public static async Task<Guid?> NewDesktopAsync()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var (ids, _) = Info();
        CtrlWin(VK_D);
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(300);
            var (now, cur) = Info();
            if (now.Count > ids.Count) return now[cur];
        }
        Store.Log("nieuw bureaublad niet gezien");
        return null;
    }

    // ------------------------------------------------------------ the named "F1" desktop

    /// <summary>The desktop with this name (as shown in Task View), or null.</summary>
    public static Guid? FindByName(string name)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var (ids, _) = Info();
        foreach (var id in ids)
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"{Key}\Desktops\{id:B}");
            if (string.Equals(k?.GetValue("Name") as string, name, StringComparison.OrdinalIgnoreCase)) return id;
        }
        return null;
    }

    /// <summary>Goes to the desktop called <paramref name="name"/>, creating and naming it when it does not exist yet.</summary>
    public static async Task<Guid?> GoToNamedAsync(string name)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (FindByName(name) is { } existing)
        {
            await GoToAsync(Info().Ids.IndexOf(existing));
            return existing;
        }
        var created = await NewDesktopAsync();
        if (created is { } id) SetName(id, name);
        return created;
    }

    /// <summary>
    /// Names a desktop. Windows only offers this through its internal virtual desktop interface (the
    /// Windows 11 24H2+ layout is used here); when that is not available the name is written where
    /// Windows keeps it, which Task View shows after the next sign-in.
    /// </summary>
    public static void SetName(Guid desk, string name)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var shell = (IServiceProvider10)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_ImmersiveShell)!)!;
            var service = CLSID_VirtualDesktopManagerInternal; var iid = typeof(IVirtualDesktopManagerInternal).GUID;
            var mgr = (IVirtualDesktopManagerInternal)shell.QueryService(ref service, ref iid);
            var d = mgr.FindDesktop(ref desk);
            WindowsCreateString(name, name.Length, out var hs);
            try { mgr.SetDesktopName(d, hs); }
            finally { WindowsDeleteString(hs); Marshal.Release(d); }
            Store.Log($"bureaublad heet nu '{name}'");
            return;
        }
        catch (Exception e) { Store.Log("bureaublad naam via Windows: " + e.Message); }
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey($@"{Key}\Desktops\{desk:B}");
            k.SetValue("Name", name, RegistryValueKind.String);
            Store.Log($"bureaublad naam '{name}' in het register gezet");
        }
        catch (Exception e) { Store.Log("bureaublad naam: " + e.Message); }
    }

    private static readonly Guid CLSID_ImmersiveShell = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid CLSID_VirtualDesktopManagerInternal = new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider10
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object QueryService(ref Guid service, ref Guid riid);
    }

    // Windows 11 24H2 and later. Only the methods up to SetDesktopName are used; earlier ones are
    // declared so the vtable order is right (their parameter types do not matter here).
    [ComImport, Guid("53F5CA0B-158F-4124-900C-057158060B27"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManagerInternal
    {
        int GetCount();
        void MoveViewToDesktop(IntPtr view, IntPtr desktop);
        bool CanViewMoveDesktops(IntPtr view);
        IntPtr GetCurrentDesktop();
        void GetDesktops(out IntPtr desktops);
        [PreserveSig] int GetAdjacentDesktop(IntPtr from, int direction, out IntPtr desktop);
        void SwitchDesktop(IntPtr desktop);
        void SwitchDesktopAndMoveForegroundView(IntPtr desktop);
        IntPtr CreateDesktop();
        void MoveDesktop(IntPtr desktop, int index);
        void RemoveDesktop(IntPtr desktop, IntPtr fallback);
        IntPtr FindDesktop(ref Guid desktopId);
        void GetDesktopSwitchIncludeExcludeViews(IntPtr desktop, out IntPtr include, out IntPtr exclude);
        void SetDesktopName(IntPtr desktop, IntPtr name);
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string s, int length, out IntPtr hstring);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr hstring);

    /// <summary>Removes a desktop when nothing is left on it, then goes to desktop 1.</summary>
    public static async Task CloseIfEmptyAsync(Guid desk)
    {
        if (!OperatingSystem.IsWindows()) return;
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
        if (!OperatingSystem.IsWindows()) return true;
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
                    if (Matches(name, MultiViewer.Process) || Matches(name, Uf1.Process)) return true;
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

    /// <summary>Closes an app's windows politely and ends it when it is still running after a few seconds.</summary>
    public static async Task CloseAppAsync(string processName)
    {
        var procs = Find(processName);
        foreach (var p in procs) { try { p.CloseMainWindow(); } catch { } }
        for (int i = 0; i < 10 && procs.Any(p => { try { return !p.HasExited; } catch { return false; } }); i++)
            await Task.Delay(300);
        foreach (var p in procs)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch (Exception e) { Store.Log($"close {processName}: {e.Message}"); }
            p.Dispose();
        }
    }

    public static bool IsRunning(string processName)
    {
        var found = Find(processName);
        foreach (var p in found) p.Dispose();
        return found.Length > 0;
    }

    /// <summary>
    /// The app's processes. On Windows by exact process name. On Linux the names differ per package
    /// (multiviewer from the .deb/.rpm, the AppImage's own file name, a Flatpak), so any process whose
    /// name starts with the app's name, ignoring case, counts.
    /// </summary>
    public static System.Diagnostics.Process[] Find(string processName)
    {
        if (OperatingSystem.IsWindows()) return System.Diagnostics.Process.GetProcessesByName(processName);
        var list = new List<System.Diagnostics.Process>();
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            bool keep = false;
            try { keep = p.Id != Environment.ProcessId && (Matches(p.ProcessName, processName) || Matches(CommandName(p.Id), processName)); } catch { }
            if (keep) list.Add(p); else p.Dispose();
        }
        return list.ToArray();
    }

    /// <summary>
    /// The program name from /proc/&lt;pid&gt;/cmdline (Linux keeps only 15 characters of a process name, too
    /// short for "Ultimate-F1Viewer-With-F1MV"); empty when it cannot be read.
    /// </summary>
    private static string CommandName(int pid)
    {
        try
        {
            var cmd = File.ReadAllText($"/proc/{pid}/cmdline");
            var argv0 = cmd.Split('\0')[0];
            return Path.GetFileName(argv0);
        }
        catch { return ""; }
    }

    private static bool Matches(string name, string processName) =>
        OperatingSystem.IsWindows()
            ? string.Equals(name, processName, StringComparison.OrdinalIgnoreCase)
            : name.StartsWith(processName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Visible application windows of the given processes (none outside Windows).</summary>
    public static List<IntPtr> AppWindows(params string[] processNames)
    {
        if (!OperatingSystem.IsWindows()) return new List<IntPtr>();
        var pids = new HashSet<uint>(processNames.SelectMany(System.Diagnostics.Process.GetProcessesByName).Select(p => (uint)p.Id));
        var list = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (IsAppWindow(hwnd) && GetWindowThreadProcessId(hwnd, out var pid) != 0 && pids.Contains(pid)) list.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static void Minimize(IEnumerable<IntPtr> windows) { if (!OperatingSystem.IsWindows()) return; foreach (var w in windows) ShowWindow(w, SW_MINIMIZE); }
    public static void Restore(IEnumerable<IntPtr> windows) { if (!OperatingSystem.IsWindows()) return; foreach (var w in windows) if (IsWindow(w)) ShowWindow(w, SW_RESTORE); }

    // ------------------------------------------------------------ native

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    private const int SW_MINIMIZE = 6, SW_RESTORE = 9;

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
