using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ScarlettSwitch.Core;

/// <summary>
/// Starts Focusrite Control 2 when it is closed and a switch is needed, and closes it again after an idle period.
/// It is started on a separate, never-shown Windows desktop, so no window appears and focus is never taken
/// (important while gaming). The Scarlett keeps its mixer state in hardware, so the switch persists after FC2 exits.
/// An FC2 the user opened is never touched.
/// </summary>
public sealed class FocusriteLauncher : IDisposable
{
    public const string ProcessName = "Focusrite Control 2";
    private const string DesktopName = "ScarlettSwitchHidden";
    private static readonly string Exe = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Focusrite", "Focusrite Control 2", "Focusrite Control 2.exe");

    private readonly object _gate = new();
    private readonly System.Threading.Timer _closeTimer;
    private Process? _owned;
    private IntPtr _desktop;

    public FocusriteLauncher() => _closeTimer = new System.Threading.Timer(_ => CloseOwned());

    public static bool Installed => File.Exists(Exe);
    public static bool IsRunning => Process.GetProcessesByName(ProcessName).Length > 0;

    /// <summary>True while an instance started by us is alive.</summary>
    public bool OwnsInstance { get { lock (_gate) return _owned is { HasExited: false }; } }

    /// <summary>Starts FC2 on the hidden desktop. Returns false if it was already running or could not start.</summary>
    public bool StartHidden()
    {
        lock (_gate)
        {
            if (IsRunning || !Installed) return false;
            if (_desktop == IntPtr.Zero)
                _desktop = CreateDesktopW(DesktopName, IntPtr.Zero, IntPtr.Zero, 0, DESKTOP_ALL, IntPtr.Zero);
            if (_desktop == IntPtr.Zero) return false;

            var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = @"WinSta0\" + DesktopName };
            string cmd = $"\"{Exe}\"";
            if (!CreateProcessW(Exe, new StringBuilder(cmd), IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero,
                    Path.GetDirectoryName(Exe), ref si, out var pi))
                return false;
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            try { _owned = Process.GetProcessById(pi.dwProcessId); } catch { _owned = null; }
            return _owned != null;
        }
    }

    /// <summary>(Re)starts the idle countdown after which the instance we started is closed.</summary>
    public void ScheduleClose(TimeSpan idle)
    {
        lock (_gate) if (_owned != null) _closeTimer.Change(idle, Timeout.InfiniteTimeSpan);
    }

    private void CloseOwned()
    {
        Process? p; IntPtr desk;
        lock (_gate) { p = _owned; desk = _desktop; }
        if (p == null) return;
        try
        {
            if (!p.HasExited)
            {
                // Graceful close (as when the user closes the window), so FC2 saves its settings normally.
                foreach (var h in WindowsOf(desk, p.Id).Where(h => Title(h) == ProcessName))
                    PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                if (!p.WaitForExit(5000))
                    foreach (var h in WindowsOf(desk, p.Id)) PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                p.WaitForExit(5000);
            }
        }
        catch { /* leave it running */ }
        lock (_gate) { _owned?.Dispose(); _owned = null; }
    }

    private static List<IntPtr> WindowsOf(IntPtr desktop, int pid)
    {
        var list = new List<IntPtr>();
        EnumDesktopWindows(desktop, (h, _) =>
        {
            GetWindowThreadProcessId(h, out uint p);
            if (p == pid) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static string Title(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public void Dispose()
    {
        _closeTimer.Dispose();
        lock (_gate) if (_desktop != IntPtr.Zero) { CloseDesktop(_desktop); _desktop = IntPtr.Zero; }
    }

    private const uint DESKTOP_ALL = 0x01FF;
    private const uint WM_CLOSE = 0x0010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb; public string? lpReserved; public string? lpDesktop; public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktopW(string name, IntPtr device, IntPtr devmode, uint flags, uint access, IntPtr sa);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] private static extern bool EnumDesktopWindows(IntPtr desktop, EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags,
        IntPtr env, string? dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
}
