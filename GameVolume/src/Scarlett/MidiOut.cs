using System.Runtime.InteropServices;

namespace ScarlettSwitch.Core;

/// <summary>Minimal WinMM MIDI output (in-box Windows API, no extra dependencies).</summary>
public sealed class MidiOut : IDisposable
{
    private IntPtr _handle;

    public string PortName { get; }

    private MidiOut(IntPtr handle, string name) { _handle = handle; PortName = name; }

    public static IReadOnlyList<string> GetPortNames()
    {
        var list = new List<string>();
        int n = midiOutGetNumDevs();
        for (int i = 0; i < n; i++)
        {
            var caps = new MIDIOUTCAPS();
            if (midiOutGetDevCaps((IntPtr)i, ref caps, Marshal.SizeOf<MIDIOUTCAPS>()) == 0)
                list.Add(caps.szPname);
        }
        return list;
    }

    /// <summary>
    /// Opens a port whose name contains <paramref name="namePart"/> (case-insensitive).
    /// For a Windows MIDI Services loopback pair ("… A" / "… B") the A side is preferred,
    /// so messages arrive on the B side that Focusrite Control 2 listens to.
    /// </summary>
    public static MidiOut? Open(string namePart)
    {
        var names = GetPortNames();
        var candidates = Enumerable.Range(0, names.Count)
            .Where(i => names[i].Contains(namePart, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => names[i].TrimEnd().EndsWith(" B", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        foreach (int i in candidates)
        {
            if (midiOutOpen(out var h, i, IntPtr.Zero, IntPtr.Zero, 0) == 0)
                return new MidiOut(h, names[i]);
        }
        return null;
    }

    public void ControlChange(int channel, int cc, int value)
    {
        int msg = (0xB0 | (channel & 0x0F)) | ((cc & 0x7F) << 8) | ((value & 0x7F) << 16);
        int rc = midiOutShortMsg(_handle, msg);
        if (rc != 0) throw new IOException($"MIDI send failed (MMRESULT {rc}).");
    }

    /// <summary>Sends a 7-bit NRPN value (CC99/CC98 select, CC6 data entry).</summary>
    public void Nrpn(int channel, int msb, int lsb, int value)
    {
        ControlChange(channel, 99, msb);
        ControlChange(channel, 98, lsb);
        ControlChange(channel, 6, value);
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        midiOutReset(_handle);
        midiOutClose(_handle);
        _handle = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MIDIOUTCAPS
    {
        public ushort wMid, wPid;
        public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public ushort wTechnology, wVoices, wNotes, wChannelMask;
        public uint dwSupport;
    }

    [DllImport("winmm.dll")] private static extern int midiOutGetNumDevs();
    [DllImport("winmm.dll", EntryPoint = "midiOutGetDevCapsW", CharSet = CharSet.Unicode)]
    private static extern int midiOutGetDevCaps(IntPtr id, ref MIDIOUTCAPS caps, int size);
    [DllImport("winmm.dll")] private static extern int midiOutOpen(out IntPtr h, int id, IntPtr cb, IntPtr inst, int flags);
    [DllImport("winmm.dll")] private static extern int midiOutShortMsg(IntPtr h, int msg);
    [DllImport("winmm.dll")] private static extern int midiOutReset(IntPtr h);
    [DllImport("winmm.dll")] private static extern int midiOutClose(IntPtr h);
}
