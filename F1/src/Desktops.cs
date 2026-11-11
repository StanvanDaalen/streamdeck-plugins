using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace F1;

/// <summary>Windows virtual desktops (via the registry and Ctrl+Win shortcuts) and PowerToys Workspaces.</summary>
public static class Desktops
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

    /// <summary>Number of desktops and the 0-based index of the current one.</summary>
    public static (int Count, int Current) Info()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        var ids = k?.GetValue("VirtualDesktopIDs") as byte[];
        var cur = k?.GetValue("CurrentVirtualDesktop") as byte[];
        if (ids == null || ids.Length < 16) return (1, 0);
        int count = ids.Length / 16, index = 0;
        if (cur is { Length: 16 })
            for (int i = 0; i < count; i++)
                if (ids.AsSpan(i * 16, 16).SequenceEqual(cur)) { index = i; break; }
        return (count, index);
    }

    /// <summary>Switches to a desktop (0-based), creating desktops when there are not enough.</summary>
    public static async Task GoToAsync(int target)
    {
        var (count, cur) = Info();
        for (int tries = 0; count <= target && tries <= target; tries++)
        {
            CtrlWin(VK_D); // creates a new desktop and switches to it
            await Task.Delay(500);
            (count, cur) = Info();
        }
        int steps = target - cur;
        for (int i = 0; i < Math.Abs(steps); i++)
        {
            CtrlWin(steps > 0 ? VK_RIGHT : VK_LEFT);
            await Task.Delay(350);
        }
    }

    public static bool LaunchWorkspace(string name)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "PowerToys", "Workspaces");
        var launcher = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerToys", "PowerToys.WorkspacesLauncher.exe"),
            @"C:\Program Files\PowerToys\PowerToys.WorkspacesLauncher.exe",
        }.FirstOrDefault(File.Exists);
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "workspaces.json")));
            var id = json?["workspaces"]?.AsArray()
                .FirstOrDefault(w => string.Equals((string?)w?["name"], name, StringComparison.OrdinalIgnoreCase))?["id"];
            if (launcher == null || id == null) { Store.Log($"workspace '{name}' of launcher niet gevonden"); return false; }
            Process.Start(new ProcessStartInfo(launcher, $"{(string)id!} 1") { UseShellExecute = false });
            return true;
        }
        catch (Exception e) { Store.Log("workspace: " + e.Message); return false; }
    }

    private const byte VK_CONTROL = 0x11, VK_LWIN = 0x5B, VK_D = 0x44, VK_LEFT = 0x25, VK_RIGHT = 0x27;
    private const uint KEYUP = 0x2, EXTENDED = 0x1;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

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
