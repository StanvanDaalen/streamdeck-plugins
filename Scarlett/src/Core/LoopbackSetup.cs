using System.Diagnostics;

namespace ScarlettSwitch.Core;

/// <summary>
/// Creates the MIDI loopback pair "Scarlett Switch" (A, plugin sends here) ↔ "FC2 MIDI Control" (B, selected in
/// Focusrite Control 2) with Microsoft's Windows MIDI Services console tool, when it is missing.
/// Uses only the supported tool; no configuration files are touched.
/// </summary>
public static class LoopbackSetup
{
    public const string NameA = "Scarlett Switch";
    public const string NameB = "FC2 MIDI Control";
    private static readonly string MidiExe = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows MIDI Services", "Tools", "midi.exe");
    private static DateTime _lastAttempt = DateTime.MinValue;

    public static bool ToolInstalled => File.Exists(MidiExe);

    /// <returns>true when a create was attempted and succeeded.</returns>
    public static bool EnsureDefault(string configuredPort)
    {
        // Only manage our own default port name, and at most once per 30 s.
        if (!string.Equals(configuredPort, NameA, StringComparison.OrdinalIgnoreCase) || !ToolInstalled) return false;
        if (DateTime.UtcNow - _lastAttempt < TimeSpan.FromSeconds(30)) return false;
        _lastAttempt = DateTime.UtcNow;
        try
        {
            var psi = new ProcessStartInfo(MidiExe)
            {
                ArgumentList = { "loopback", "create", "-a", NameA, "-b", NameB, "-u", "scarlettswitch" },
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            if (!p.WaitForExit(8000)) { p.Kill(); return false; }
            if (p.ExitCode != 0) return false;
            Thread.Sleep(500); // let WinMM pick up the new ports
            return true;
        }
        catch { return false; }
    }
}
