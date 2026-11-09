using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MonitorPlugin;

public sealed class MonitorCaps
{
    public string Model { get; set; } = "";
    /// <summary>The user's own name for this monitor, such as "Main" or "Left".</summary>
    public string Name { get; set; } = "";
    public int Display { get; set; }
    public List<int> Inputs { get; set; } = new();
}

public sealed record OpenMonitor(string Key, int Display, string Description, IntPtr Handle);

/// <summary>Monitor input control over DDC/CI (VCP code 0x60), the same channel PowerDisplay uses.</summary>
public static class Ddc
{
    private const byte InputSelect = 0x60;
    private static readonly string CachePath = Path.Combine(Log.Dir, "monitors.json");

    public static Dictionary<string, MonitorCaps> Known { get; private set; } = new();

    public static void LoadCache()
    {
        try { if (File.Exists(CachePath)) Known = JsonSerializer.Deserialize<Dictionary<string, MonitorCaps>>(File.ReadAllText(CachePath)) ?? new(); }
        catch (Exception e) { Log.Write("cache: " + e.Message); }
    }

    private static void SaveCache()
    {
        try { File.WriteAllText(CachePath, JsonSerializer.Serialize(Known, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception e) { Log.Write("cache save: " + e.Message); }
    }

    public static void SetName(string key, string name)
    {
        if (!Known.TryGetValue(key, out var caps)) return;
        caps.Name = name;
        SaveCache();
    }

    public static string DefaultInputName(int code) => code switch
    {
        0x01 => "VGA 1", 0x02 => "VGA 2", 0x03 => "DVI 1", 0x04 => "DVI 2",
        0x0F => "DisplayPort 1", 0x10 => "DisplayPort 2",
        0x11 => "HDMI 1", 0x12 => "HDMI 2",
        0x1B => "USB-C",
        _ => $"Input {code:X2}",
    };

    /// <summary>Opens every physical monitor for the duration of the callback.</summary>
    public static T WithMonitors<T>(Func<List<OpenMonitor>, T> use)
    {
        var groups = new List<(uint Count, PHYSICAL_MONITOR[] Items)>();
        var open = new List<OpenMonitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hmon, _, _, _) =>
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hmon, out uint n) || n == 0) return true;
            var items = new PHYSICAL_MONITOR[n];
            if (!GetPhysicalMonitorsFromHMONITOR(hmon, n, items)) return true;
            groups.Add((n, items));
            var (key, display) = Identify(hmon);
            for (int i = 0; i < n; i++)
                open.Add(new OpenMonitor(n > 1 ? $"{key}#{i}" : key, display, items[i].Description, items[i].Handle));
            return true;
        }, IntPtr.Zero);
        try { return use(open); }
        finally { foreach (var (n, items) in groups) DestroyPhysicalMonitors(n, items); }
    }

    // The device interface path stays the same across reboots and tells two identical models apart.
    private static (string Key, int Display) Identify(IntPtr hmon)
    {
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        GetMonitorInfo(hmon, ref info);
        var device = info.szDevice ?? "";
        int.TryParse(Regex.Match(device, @"\d+$").Value, out int display);
        var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        for (uint i = 0; EnumDisplayDevices(device, i, ref dd, 1); i++)
            if ((dd.StateFlags & 1) != 0 && !string.IsNullOrEmpty(dd.DeviceID)) return (dd.DeviceID, display);
        return (device, display);
    }

    /// <summary>Reads capabilities for monitors not seen before; this is slow, so results are cached on disk.</summary>
    public static void RefreshCaps(bool force = false)
    {
        var changed = WithMonitors(list =>
        {
            bool any = false;
            foreach (var m in list)
            {
                if (Known.TryGetValue(m.Key, out var old) && !force)
                {
                    if (old.Display != m.Display) { old.Display = m.Display; any = true; }
                    continue;
                }
                var caps = ReadCaps(m);
                if (caps == null) { Log.Write("no capabilities: " + m.Description); continue; }
                caps.Name = Known.GetValueOrDefault(m.Key)?.Name ?? "";
                Known[m.Key] = caps;
                any = true;
            }
            return any;
        });
        if (changed) SaveCache();
    }

    private static MonitorCaps? ReadCaps(OpenMonitor m)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (!GetCapabilitiesStringLength(m.Handle, out uint len) || len == 0) { Thread.Sleep(100); continue; }
            var sb = new StringBuilder((int)len + 1);
            if (!CapabilitiesRequestAndCapabilitiesReply(m.Handle, sb, len)) { Thread.Sleep(100); continue; }
            var text = sb.ToString();
            var inputs = Regex.Match(text, @"\b60\s*\(([^)]*)\)").Groups[1].Value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(h => int.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out int v) ? v : -1)
                .Where(v => v > 0).Distinct().ToList();
            if (inputs.Count == 0) continue;
            var model = Regex.Match(text, @"model\(([^)]*)\)").Groups[1].Value;
            return new MonitorCaps { Model = model.Length > 0 ? model : m.Description, Display = m.Display, Inputs = inputs };
        }
        return null;
    }

    /// <summary>Current input per connected monitor. Some monitors repeat the value in the high byte, so only the low byte counts.</summary>
    public static Dictionary<string, int> ReadCurrent(IEnumerable<string> keys)
    {
        var wanted = keys.ToHashSet();
        return WithMonitors(list =>
        {
            var result = new Dictionary<string, int>();
            foreach (var m in list.Where(m => wanted.Contains(m.Key)))
                result[m.Key] = ReadInput(m);
            return result;
        });
    }

    private static int ReadInput(OpenMonitor m)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (GetVCPFeatureAndVCPFeatureReply(m.Handle, InputSelect, IntPtr.Zero, out uint current, out _))
                return (int)(current & 0xFF);
            Thread.Sleep(60);
        }
        return -1; // connected, but no answer right now
    }

    public static bool SetInput(string key, int code) => WithMonitors(list =>
    {
        var m = list.FirstOrDefault(x => x.Key == key);
        return m != null && SetVCPFeature(m.Handle, InputSelect, (uint)code);
    });

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR { public IntPtr Handle; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize; public int l1, t1, r1, b1, l2, t2, r2, b2; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    private delegate bool MonitorEnumProc(IntPtr hmon, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(IntPtr hmon, ref MONITORINFOEX info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevicesW")] private static extern bool EnumDisplayDevices(string device, uint index, ref DISPLAY_DEVICE dd, uint flags);
    [DllImport("dxva2.dll")] private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hmon, out uint count);
    [DllImport("dxva2.dll")] private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hmon, uint count, [Out] PHYSICAL_MONITOR[] items);
    [DllImport("dxva2.dll")] private static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] items);
    [DllImport("dxva2.dll")] private static extern bool GetCapabilitiesStringLength(IntPtr h, out uint length);
    [DllImport("dxva2.dll", CharSet = CharSet.Ansi)] private static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr h, StringBuilder text, uint length);
    [DllImport("dxva2.dll")] private static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, IntPtr type, out uint current, out uint maximum);
    [DllImport("dxva2.dll")] private static extern bool SetVCPFeature(IntPtr h, byte code, uint value);
}

public static class Log
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StanMonitor");
    private static readonly string FilePath = Path.Combine(Dir, "plugin.log");
    private static readonly object Lock = new();

    public static void Reset()
    {
        Directory.CreateDirectory(Dir);
        try { File.WriteAllText(FilePath, ""); } catch { }
    }

    public static void Write(string msg)
    {
        lock (Lock) { try { File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss} {msg}\n"); } catch { } }
    }
}
