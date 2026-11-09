using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ScarlettSwitch.Core;

/// <summary>Detects a connected Focusrite Scarlett interface (USB VID 0x1235) and its generation.</summary>
public sealed record ScarlettDevice(string Name, int ProductId, string Serial, int Generation)
{
    /// <summary>Focusrite Control 2 (with MIDI control) supports Scarlett 4th Gen; older generations use Focusrite Control 1.</summary>
    public bool SupportedByFocusriteControl2 => Generation >= 4;

    public static ScarlettDevice? Detect()
    {
        using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
        if (usb == null) return null;
        foreach (var vidPid in usb.GetSubKeyNames())
        {
            var m = Regex.Match(vidPid, @"^VID_1235&PID_([0-9A-F]{4})$", RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            using var key = usb.OpenSubKey(vidPid);
            if (key == null) continue;
            foreach (var serial in key.GetSubKeyNames())
            {
                string instanceId = $@"USB\{vidPid}\{serial}";
                if (!IsPresent(instanceId)) continue;
                using var inst = key.OpenSubKey(serial);
                string name = Clean(inst?.GetValue("FriendlyName") as string)
                              ?? Clean(inst?.GetValue("DeviceDesc") as string) ?? "Focusrite device";
                if (!name.Contains("Scarlett", StringComparison.OrdinalIgnoreCase)) continue;
                int pid = Convert.ToInt32(m.Groups[1].Value, 16);
                return new ScarlettDevice(name, pid, serial, GenerationOf(name, pid));
            }
        }
        return null;
    }

    private static int GenerationOf(string name, int pid)
    {
        var g = Regex.Match(name, @"(\d)(st|nd|rd|th) Gen", RegexOptions.IgnoreCase);
        if (g.Success) return int.Parse(g.Groups[1].Value);
        // Fallback on known product-id ranges.
        return pid switch
        {
            >= 0x8218 and <= 0x821F => 4,
            >= 0x8210 and <= 0x8217 => 3,
            >= 0x8200 and <= 0x820F => 2,
            _ => 1,
        };
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s[(s.LastIndexOf(';') + 1)..].Trim();

    private static bool IsPresent(string instanceId) => CM_Locate_DevNodeW(out _, instanceId, 0) == 0;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, int flags);
}
