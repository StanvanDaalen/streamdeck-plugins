using System.Runtime.InteropServices;

namespace MonitorPlugin;

/// <summary>
/// Takes a screen out of the Windows desktop while it shows another device (such as a console), so the mouse
/// moves straight between the screens left and right of it, and puts it back afterwards.
/// </summary>
public static class DisplayLayout
{
    private const int PathSize = 72, ModeSize = 64;
    private const uint OnlyActive = 2, Apply = 0x80, Supplied = 0x20, AllowChanges = 0x400, Extend = 4;

    /// <summary>Detaches the screen with this GDI name (\\.\DISPLAYn) and closes the gap it leaves.</summary>
    public static bool Detach(string gdiName)
    {
        if (GetDisplayConfigBufferSizes(OnlyActive, out uint np, out uint nm) != 0) return false;
        IntPtr paths = Marshal.AllocHGlobal((int)np * PathSize), modes = Marshal.AllocHGlobal((int)nm * ModeSize);
        try
        {
            if (QueryDisplayConfig(OnlyActive, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return false;
            int gone = -1, goneX = 0, goneW = 0;
            var srcMode = new int[np];
            for (int i = 0; i < np; i++)
            {
                IntPtr pa = paths + i * PathSize;
                srcMode[i] = Marshal.ReadInt32(pa, 12);
                if (!SourceName(Marshal.ReadInt64(pa, 0), (uint)Marshal.ReadInt32(pa, 8)).Equals(gdiName, StringComparison.OrdinalIgnoreCase)) continue;
                IntPtr mo = modes + srcMode[i] * ModeSize;
                gone = i; goneX = Marshal.ReadInt32(mo, 28); goneW = Marshal.ReadInt32(mo, 16);
            }
            if (gone < 0 || np < 2) return false;

            // Screens right of the detached one move left by its width; the largest remaining screen becomes the main one at 0,0.
            int keep = -1; long bestArea = -1; int bestX = int.MaxValue;
            for (int i = 0; i < np; i++)
            {
                if (i == gone) continue;
                IntPtr mo = modes + srcMode[i] * ModeSize;
                int x = Marshal.ReadInt32(mo, 28);
                if (x >= goneX + goneW) { x -= goneW; Marshal.WriteInt32(mo, 28, x); }
                long area = (long)Marshal.ReadInt32(mo, 16) * Marshal.ReadInt32(mo, 20);
                if (area > bestArea || (area == bestArea && x < bestX)) { bestArea = area; bestX = x; keep = i; }
            }
            IntPtr km = modes + srcMode[keep] * ModeSize;
            int dx = Marshal.ReadInt32(km, 28), dy = Marshal.ReadInt32(km, 32);
            for (int i = 0; i < np; i++)
            {
                if (i == gone) continue;
                IntPtr mo = modes + srcMode[i] * ModeSize;
                Marshal.WriteInt32(mo, 28, Marshal.ReadInt32(mo, 28) - dx);
                Marshal.WriteInt32(mo, 32, Marshal.ReadInt32(mo, 32) - dy);
            }

            IntPtr gp = paths + gone * PathSize;
            Marshal.WriteInt32(gp, 68, Marshal.ReadInt32(gp, 68) & ~1); // path inactive
            Marshal.WriteInt32(gp, 12, -1);                              // no source mode
            Marshal.WriteInt32(gp, 32, -1);                              // no target mode
            int r = SetDisplayConfig(np, paths, nm, modes, Apply | Supplied | AllowChanges);
            if (r != 0) Log.Write($"detach {gdiName}: {r}");
            return r == 0;
        }
        finally { Marshal.FreeHGlobal(paths); Marshal.FreeHGlobal(modes); }
    }

    /// <summary>Puts every connected screen back the way Windows last had them extended.</summary>
    public static bool Restore()
    {
        int r = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, Apply | Extend);
        if (r != 0) Log.Write("restore layout: " + r);
        return r == 0;
    }

    private static string SourceName(long adapter, uint sourceId)
    {
        IntPtr p = Marshal.AllocHGlobal(84);
        try
        {
            for (int i = 0; i < 84; i++) Marshal.WriteByte(p, i, 0);
            Marshal.WriteInt32(p, 0, 1); // DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME
            Marshal.WriteInt32(p, 4, 84);
            Marshal.WriteInt64(p, 8, adapter);
            Marshal.WriteInt32(p, 16, (int)sourceId);
            return DisplayConfigGetDeviceInfo(p) == 0 ? Marshal.PtrToStringUni(p + 20) ?? "" : "";
        }
        finally { Marshal.FreeHGlobal(p); }
    }

    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint np, IntPtr paths, ref uint nm, IntPtr modes, IntPtr topology);
    [DllImport("user32.dll")] private static extern int SetDisplayConfig(uint np, IntPtr paths, uint nm, IntPtr modes, uint flags);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(IntPtr packet);
}
