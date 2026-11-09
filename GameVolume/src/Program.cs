using System.Text;

namespace GameVolume;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string? Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        if (Arg("--assets") is { } dir) { Glyphs.WriteAssets(dir); return 0; }
        SettingsStore.Load();
        if (Arg("--dump") is { } file) { Dump(file); return 0; }
        if (Arg("--close-test") is { } closeName)
        {
            var found = WindowControl.FindApp(closeName);
            SettingsStore.Log($"close-test {closeName}: window={(found != null)} pids={(found == null ? "" : string.Join(',', found.Pids))}");
            if (found != null) WindowControl.CloseWindows(found.Pids);
            return 0;
        }
        if (Arg("--icon-test") is { } testOut)
        {
            var packs = ArtworkService.IconLibrary();
            var url = ArtworkService.FileImage(Arg("--icon")!)!;
            File.WriteAllBytes(testOut, Convert.FromBase64String(url[(url.IndexOf(',') + 1)..]));
            File.WriteAllText(testOut + ".txt",
                $"payload={packs.ToJsonString().Length}\n" +
                string.Join("\n", packs.Select(p => $"{p!["name"]}: {p["icons"]!.AsArray().Count} | {p["dir"]} | {p["icons"]![0]!["n"]} | {p["icons"]![0]!["f"]}")));
            return 0;
        }

        var port = Arg("-port");
        var uuid = Arg("-pluginUUID");
        var register = Arg("-registerEvent");
        if (port == null || uuid == null || register == null) return 1;

        try
        {
            await new Plugin(int.Parse(port), uuid, register).RunAsync();
        }
        catch (Exception e)
        {
            SettingsStore.Log("fatal: " + e);
            return 1;
        }
        return 0;
    }

    /// <summary>Diagnostic output: what the library and detector currently see.</summary>
    private static void Dump(string file)
    {
        var sb = new StringBuilder();
        var lib = new GameLibrary();
        lib.Reload();
        sb.AppendLine("LIBRARY");
        foreach (var e in lib.Entries.OrderBy(x => x.Name)) sb.AppendLine($"  {e.Name} | {e.Id} | {e.InstallDir}");

        sb.AppendLine("PROGRAMS");
        foreach (var e in lib.Entries.OrderBy(x => x.Name)) sb.AppendLine($"  {e.Name} -> {GameLibrary.GuessExe(e)}");

        var audio = new AudioSessions();
        audio.Refresh();
        sb.AppendLine("AUDIO");
        foreach (var s in audio.All) sb.AppendLine($"  {s.Process} pid={s.Pid} vol={AudioSessions.Volume(new[] { s })} path={s.Path}");

        var cur = new GameDetector(lib).FindCurrent(null);
        sb.AppendLine("CURRENT");
        if (cur == null) sb.AppendLine("  (none)");
        else
        {
            sb.AppendLine($"  {cur.Entry.Name} | exe={cur.ExePath} | pids={string.Join(',', cur.Pids)} | sessions={audio.ForGame(cur).Count()}");
            var art = new ArtworkService(lib).ForGame(cur);
            sb.AppendLine($"  accent={art.Accent} iconBytes={art.IconUrl.Length} bgBytes={art.BackgroundUrl.Length}");
        }
        var scarlett = ScarlettSwitch.Core.Settings.Load();
        using (var outputs = new ScarlettSwitch.Core.OutputController(scarlett))
        {
            Thread.Sleep(2500);
            sb.AppendLine("SCARLETT");
            sb.AppendLine($"  status={outputs.Status} device={outputs.Device?.Name} port={outputs.PortName} output={outputs.Output} speakerMix={scarlett.SpeakerMix} headphoneMix={scarlett.HeadphoneMix}");
        }
        using (var master = new ScarlettSwitch.Core.EndpointVolume())
        {
            master.Bind();
            sb.AppendLine($"  masterVolume={master.Percent} muted={master.Muted}");
        }
        File.WriteAllText(file, sb.ToString());
    }
}
