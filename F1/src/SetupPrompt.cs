using System.Diagnostics;

namespace F1;

/// <summary>
/// The setup prompt (setup-prompt.md, shipped next to the plugin): the part people paste into an AI assistant
/// that builds their own MultiViewer setups and Ultimate F1 Viewer layouts, put on the clipboard.
/// </summary>
public static class SetupPrompt
{
    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "..", "setup-prompt.md");

    /// <summary>The prompt itself: everything after the first line that is just "---".</summary>
    public static string? Text()
    {
        try
        {
            var all = File.ReadAllText(FilePath).Replace("\r\n", "\n");
            var cut = all.IndexOf("\n---\n", StringComparison.Ordinal);
            return (cut >= 0 ? all[(cut + 5)..] : all).Trim();
        }
        catch (Exception e) { Store.Log("setup prompt: " + e.Message); return null; }
    }

    public static bool CopyToClipboard()
    {
        if (Text() is not { } text) return false;
        var tmp = Path.Combine(Path.GetTempPath(), "f1-setup-prompt.txt");
        try
        {
            File.WriteAllText(tmp, text);
            ProcessStartInfo psi;
            if (OperatingSystem.IsWindows())
                psi = new("powershell", $"-NoProfile -NonInteractive -Command \"Get-Content -Raw -Encoding UTF8 -LiteralPath '{tmp}' | Set-Clipboard\"");
            else if (Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") != null)
                psi = new("sh", $"-c \"wl-copy < '{tmp}'\"");
            else
                psi = new("sh", $"-c \"xclip -selection clipboard < '{tmp}' || xsel --clipboard --input < '{tmp}'\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            using var p = Process.Start(psi);
            if (p == null || !p.WaitForExit(10000)) return false;
            return p.ExitCode == 0;
        }
        catch (Exception e) { Store.Log("setup prompt kopi\u00ebren: " + e.Message); return false; }
    }
}
