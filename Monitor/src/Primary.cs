using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace MonitorPlugin;

/// <summary>
/// Makes another monitor the Windows main display (the one with the system tray) without moving the physical
/// layout: every display keeps its place, only the origin of the virtual desktop moves, like "Make this my main
/// display" in Windows Settings. Desktop icon positions are saved before and put back after, because Explorer
/// tends to shuffle them when the main display changes.
/// </summary>
public static class Primary
{
    // ---------- display configuration ----------

    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct PathSource { public Luid Adapter; public uint Id; public uint ModeIndex; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct Rational { public uint N; public uint D; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PathTarget
    {
        public Luid Adapter; public uint Id; public uint ModeIndex; public uint Tech; public uint Rotation; public uint Scaling;
        public Rational Refresh; public uint Scanline; public int Available; public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PathInfo { public PathSource Source; public PathTarget Target; public uint Flags; }

    // DISPLAYCONFIG_MODE_INFO is 64 bytes; only the source-mode position is changed, all other bytes pass through untouched.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct ModeInfo
    {
        [FieldOffset(0)] public uint InfoType;   // 1 = source, 2 = target, 3 = desktop image
        [FieldOffset(4)] public uint Id;
        [FieldOffset(8)] public Luid Adapter;
        [FieldOffset(28)] public int X;          // source mode: position.x
        [FieldOffset(32)] public int Y;          // source mode: position.y
    }

    [StructLayout(LayoutKind.Sequential)] private struct Header { public uint Type; public uint Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TargetName
    {
        public Header Header; public uint Flags; public uint Tech; public ushort Manufacturer; public ushort Product; public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
    }

    private const uint QdcOnlyActivePaths = 0x2;
    private const uint SdcApply = 0x80, SdcUseSuppliedDisplayConfig = 0x20, SdcSaveToDatabase = 0x200, SdcAllowChanges = 0x400;

    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, [In, Out] PathInfo[] p, ref uint modes, [In, Out] ModeInfo[] m, IntPtr topology);
    [DllImport("user32.dll")] private static extern int SetDisplayConfig(uint paths, [In] PathInfo[] p, uint modes, [In] ModeInfo[] m, uint flags);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref TargetName request);

    private static bool Query(out PathInfo[] paths, out ModeInfo[] modes)
    {
        paths = Array.Empty<PathInfo>(); modes = Array.Empty<ModeInfo>();
        if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var np, out var nm) != 0) return false;
        paths = new PathInfo[np]; modes = new ModeInfo[nm];
        if (QueryDisplayConfig(QdcOnlyActivePaths, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return false;
        Array.Resize(ref paths, (int)np); Array.Resize(ref modes, (int)nm);
        return true;
    }

    private static string DevicePath(PathInfo p)
    {
        var name = new TargetName();
        name.Header.Type = 2; name.Header.Size = (uint)Marshal.SizeOf<TargetName>();
        name.Header.Adapter = p.Target.Adapter; name.Header.Id = p.Target.Id;
        return DisplayConfigGetDeviceInfo(ref name) == 0 ? name.DevicePath ?? "" : "";
    }

    /// <summary>Monitor key (device path, as used for DDC) of the current main display, or "".</summary>
    public static string Current()
    {
        if (!Query(out var paths, out var modes)) return "";
        foreach (var p in paths)
        {
            var m = modes[p.Source.ModeIndex];
            if (m.X == 0 && m.Y == 0) return DevicePath(p);
        }
        return "";
    }

    /// <summary>Makes the monitor with this key the main display by shifting the virtual desktop origin onto it.</summary>
    public static bool MakeMain(string key)
    {
        if (!Query(out var paths, out var modes)) { Log.Write("primary: query failed"); return false; }
        var target = paths.FirstOrDefault(p => string.Equals(DevicePath(p), key, StringComparison.OrdinalIgnoreCase));
        if (DevicePath(target).Length == 0) { Log.Write("primary: monitor not active"); return false; }
        var origin = modes[target.Source.ModeIndex];
        if (origin.X == 0 && origin.Y == 0) return true;
        int dx = origin.X, dy = origin.Y;
        for (int i = 0; i < modes.Length; i++)
            if (modes[i].InfoType == 1) { modes[i].X -= dx; modes[i].Y -= dy; }
        int r = SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes,
            SdcApply | SdcUseSuppliedDisplayConfig | SdcSaveToDatabase | SdcAllowChanges);
        Log.Write($"primary: {Label(key)} -> result {r}");
        return r == 0;
    }

    private static string Label(string key) => key.Split('#').Skip(1).FirstOrDefault() ?? key;

    // ---------- desktop icons ----------

    private const int LvmGetItemCount = 0x1004, LvmGetItemPosition = 0x1010, LvmSetItemPosition32 = 0x1031, LvmGetItemTextW = 0x1073;
    private const int LvsAutoArrange = 0x100, GwlStyle = -16;
    private const uint ProcessVmAll = 0x0008 | 0x0010 | 0x0020 | 0x0400; // operation, read, write, query
    private const uint MemCommitReserve = 0x3000, MemRelease = 0x8000, PageReadWrite = 0x04;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] private static extern IntPtr VirtualAllocEx(IntPtr proc, IntPtr addr, UIntPtr size, uint type, uint protect);
    [DllImport("kernel32.dll")] private static extern bool VirtualFreeEx(IntPtr proc, IntPtr addr, UIntPtr size, uint type);
    [DllImport("kernel32.dll")] private static extern bool ReadProcessMemory(IntPtr proc, IntPtr addr, byte[] buffer, UIntPtr size, out UIntPtr read);
    [DllImport("kernel32.dll")] private static extern bool WriteProcessMemory(IntPtr proc, IntPtr addr, byte[] buffer, UIntPtr size, out UIntPtr written);

    private static IntPtr DesktopList()
    {
        IntPtr view = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
        for (IntPtr w = IntPtr.Zero; view == IntPtr.Zero && (w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero;)
            view = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
        return view == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
    }

    /// <summary>Runs an action with a scratch buffer inside Explorer's process (list-view messages need memory there).</summary>
    private static T WithExplorerBuffer<T>(IntPtr list, Func<IntPtr, IntPtr, T> action, T fallback)
    {
        GetWindowThreadProcessId(list, out var pid);
        var proc = OpenProcess(ProcessVmAll, false, pid);
        if (proc == IntPtr.Zero) return fallback;
        var buf = VirtualAllocEx(proc, IntPtr.Zero, (UIntPtr)4096, MemCommitReserve, PageReadWrite);
        try { return buf == IntPtr.Zero ? fallback : action(proc, buf); }
        finally
        {
            if (buf != IntPtr.Zero) VirtualFreeEx(proc, buf, UIntPtr.Zero, MemRelease);
            CloseHandle(proc);
        }
    }

    // Buffer layout inside Explorer: 0..7 POINT, 64..151 LVITEMW (x64), 512.. item text (UTF-16).
    private static string ItemName(IntPtr list, IntPtr proc, IntPtr buf, int index)
    {
        var item = new byte[88];
        IntPtr textAddr = buf + 512;
        BitConverter.TryWriteBytes(item.AsSpan(0), 1u);              // mask = LVIF_TEXT
        BitConverter.TryWriteBytes(item.AsSpan(4), index);           // iItem
        BitConverter.TryWriteBytes(item.AsSpan(24), (long)textAddr); // pszText
        BitConverter.TryWriteBytes(item.AsSpan(32), 260);            // cchTextMax
        WriteProcessMemory(proc, buf + 64, item, (UIntPtr)item.Length, out _);
        int len = (int)SendMessage(list, LvmGetItemTextW, (IntPtr)index, buf + 64);
        var text = new byte[520];
        ReadProcessMemory(proc, textAddr, text, (UIntPtr)text.Length, out _);
        return Encoding.Unicode.GetString(text, 0, Math.Clamp(len, 0, 259) * 2);
    }

    private static (int X, int Y)? ItemPosition(IntPtr list, IntPtr proc, IntPtr buf, int index)
    {
        var point = new byte[8];
        if (SendMessage(list, LvmGetItemPosition, (IntPtr)index, buf) == IntPtr.Zero) return null;
        if (!ReadProcessMemory(proc, buf, point, (UIntPtr)8, out var read) || (ulong)read != 8) return null;
        return (BitConverter.ToInt32(point, 0), BitConverter.ToInt32(point, 4));
    }

    /// <summary>
    /// Icon name -> positions on the desktop (one per item with that name, in list order, so same-named items stay
    /// apart). Null when icons are auto-arranged, the desktop is not found, or any read failed: a partial snapshot is
    /// never used, so icons can never be sent to (0,0) by a failed read.
    /// </summary>
    public static Dictionary<string, List<(int X, int Y)>>? SaveIcons()
    {
        var list = DesktopList();
        if (list == IntPtr.Zero || (GetWindowLong(list, GwlStyle) & LvsAutoArrange) != 0) return null;
        return WithExplorerBuffer<Dictionary<string, List<(int X, int Y)>>?>(list, (proc, buf) =>
        {
            var result = new Dictionary<string, List<(int X, int Y)>>();
            int count = (int)SendMessage(list, LvmGetItemCount, IntPtr.Zero, IntPtr.Zero);
            for (int i = 0; i < count; i++)
            {
                var name = ItemName(list, proc, buf, i);
                if (ItemPosition(list, proc, buf, i) is not { } pos) return null;
                if (name.Length == 0) continue;
                if (!result.TryGetValue(name, out var spots)) result[name] = spots = new();
                spots.Add(pos);
            }
            return result;
        }, null);
    }

    /// <summary>Puts every icon that moved back where it was. Desktop coordinates cover the whole virtual screen, so
    /// the same numbers still mean the same physical spot after the main display changed.</summary>
    public static void RestoreIcons(Dictionary<string, List<(int X, int Y)>>? saved)
    {
        if (saved == null || saved.Count == 0) return;
        var list = DesktopList();
        if (list == IntPtr.Zero || (GetWindowLong(list, GwlStyle) & LvsAutoArrange) != 0) return;
        WithExplorerBuffer(list, (proc, buf) =>
        {
            int count = (int)SendMessage(list, LvmGetItemCount, IntPtr.Zero, IntPtr.Zero), moved = 0;
            var seen = new Dictionary<string, int>();
            var point = new byte[8];
            for (int i = 0; i < count; i++)
            {
                var name = ItemName(list, proc, buf, i);
                int nth = seen.GetValueOrDefault(name); seen[name] = nth + 1;
                if (!saved.TryGetValue(name, out var spots) || nth >= spots.Count) continue;
                var pos = spots[nth];
                if (ItemPosition(list, proc, buf, i) is not { } now || now == pos) continue;
                BitConverter.TryWriteBytes(point.AsSpan(0), pos.X);
                BitConverter.TryWriteBytes(point.AsSpan(4), pos.Y);
                if (!WriteProcessMemory(proc, buf, point, (UIntPtr)8, out _)) continue;
                SendMessage(list, LvmSetItemPosition32, (IntPtr)i, buf);
                moved++;
            }
            Log.Write($"icons: {moved} put back");
            return 0;
        }, 0);
    }

    // ---------- state that survives a plugin restart ----------

    private static readonly string StateFile = Path.Combine(Log.Dir, "primary.json");

    public sealed class State
    {
        public string Original { get; set; } = "";
        public string Applied { get; set; } = "";
        public Dictionary<string, int[]> Icons { get; set; } = new();   // name -> x1,y1,x2,y2,...
    }

    public static State Load()
    {
        foreach (var file in new[] { StateFile, StateFile + ".bak" })
            try { if (File.Exists(file)) return JsonSerializer.Deserialize<State>(File.ReadAllText(file)) ?? new(); }
            catch { /* try the previous copy */ }
        return new();
    }

    /// <summary>Atomic save (temp file, then replace, keeping the previous copy). False when it could not be written.</summary>
    public static bool Save(State s)
    {
        try
        {
            Directory.CreateDirectory(Log.Dir);
            var tmp = StateFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s));
            if (File.Exists(StateFile)) File.Replace(tmp, StateFile, StateFile + ".bak");
            else File.Move(tmp, StateFile);
            return true;
        }
        catch (Exception e) { Log.Write("primary state: " + e.Message); return false; }
    }

    public static Dictionary<string, int[]> Pack(Dictionary<string, List<(int X, int Y)>>? icons) =>
        icons?.ToDictionary(k => k.Key, v => v.Value.SelectMany(p => new[] { p.X, p.Y }).ToArray()) ?? new();

    public static Dictionary<string, List<(int X, int Y)>> Unpack(Dictionary<string, int[]> icons) =>
        icons.Where(k => k.Value.Length >= 2 && k.Value.Length % 2 == 0)
             .ToDictionary(k => k.Key, v => Enumerable.Range(0, v.Value.Length / 2).Select(i => (v.Value[2 * i], v.Value[2 * i + 1])).ToList());

    /// <summary>True when a display with this key is active right now.</summary>
    public static bool IsActive(string key) =>
        Query(out var paths, out _) && paths.Any(p => string.Equals(DevicePath(p), key, StringComparison.OrdinalIgnoreCase));
}
