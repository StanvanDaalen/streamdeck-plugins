using System.Runtime.InteropServices;

namespace MonitorPlugin;

/// <summary>
/// LG monitors ignore the standard input command (VCP 0x60). They switch on LG's own code 0xF4,
/// but only when the DDC/CI message uses source address 0x50, which the Windows monitor API cannot send.
/// On an NVIDIA card the raw message goes out through NvAPI's I2C write instead.
/// </summary>
public static class LgInput
{
    /// <summary>The input each LG monitor was last switched to, because LG keeps reporting the same one over 0x60.</summary>
    public static readonly Dictionary<string, int> Last = new();

    /// <summary>LG's EDID manufacturer id is GSM, and it is part of the device path that serves as the monitor key.</summary>
    public static bool IsLg(string key) => key.Contains("#GSM", StringComparison.OrdinalIgnoreCase);

    private static int? LgCode(int standard) => standard switch
    {
        0x11 => 0x90, // HDMI 1
        0x12 => 0x91, // HDMI 2
        0x0F => 0xD0, // DisplayPort 1
        0x10 => 0xD1, // DisplayPort 2
        0x1B => 0xD2, // USB-C
        _ => null,
    };

    public static bool TrySet(int display, int standardCode)
    {
        if (LgCode(standardCode) is not int value) return false;
        try
        {
            // The driver now and then refuses the first try, for example right after the screen layout changed.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var status = Write($@"\\.\DISPLAY{display}", 0xF4, value);
                if (status == 0) return true;
                Log.Write($"lg nvapi DISPLAY{display}: status {status}");
                Thread.Sleep(400);
            }
            return false;
        }
        catch (Exception e)
        {
            Log.Write("lg nvapi: " + e.Message); // no NVIDIA card or driver
            return false;
        }
    }

    private const byte DdcAddress = 0x6E, LgSource = 0x50;

    private static int Write(string displayName, byte vcp, int value)
    {
        int s = Fn<InitFn>(0x0150E828)();
        if (s != 0) return s;
        s = Fn<DisplayHandleFn>(0x35C29134)(displayName, out var display);
        if (s != 0) return s;
        s = Fn<OutputIdFn>(0xD995937E)(display, out uint mask);
        if (s != 0) return s;
        var gpus = new IntPtr[64];
        s = Fn<GpusFn>(0x34EF9506)(display, gpus, out uint count);
        if (s != 0) return s;
        if (count == 0) return -1;

        byte[] data = { 0x84, 0x03, vcp, (byte)(value >> 8), (byte)value, 0 };
        byte check = DdcAddress ^ LgSource;
        for (int i = 0; i < 5; i++) check ^= data[i];
        data[5] = check;

        IntPtr reg = Marshal.AllocHGlobal(1), buf = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.WriteByte(reg, LgSource);
            Marshal.Copy(data, 0, buf, data.Length);
            var info = new I2cInfo
            {
                displayMask = mask, bIsDDCPort = 1, i2cDevAddress = DdcAddress,
                pbI2cRegAddress = reg, regAddrSize = 1, pbData = buf, cbSize = (uint)data.Length,
                i2cSpeed = 0xFFFF, i2cSpeedKhz = 0,
            };
            info.version = (uint)Marshal.SizeOf<I2cInfo>() | (3u << 16);
            return Fn<I2cWriteFn>(0xE812EB07)(gpus[0], ref info);
        }
        finally { Marshal.FreeHGlobal(reg); Marshal.FreeHGlobal(buf); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct I2cInfo
    {
        public uint version; public uint displayMask; public byte bIsDDCPort; public byte i2cDevAddress;
        public IntPtr pbI2cRegAddress; public uint regAddrSize; public IntPtr pbData; public uint cbSize;
        public uint i2cSpeed; public int i2cSpeedKhz; public byte portId; public uint bIsPortIdSet;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private delegate int DisplayHandleFn(string name, out IntPtr display);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OutputIdFn(IntPtr display, out uint mask);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GpusFn(IntPtr display, [Out] IntPtr[] gpus, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int I2cWriteFn(IntPtr gpu, ref I2cInfo info);

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr QueryInterface(uint id);

    private static T Fn<T>(uint id) where T : Delegate
    {
        var p = QueryInterface(id);
        if (p == IntPtr.Zero) throw new EntryPointNotFoundException($"NvAPI 0x{id:X8}");
        return Marshal.GetDelegateForFunctionPointer<T>(p);
    }
}
