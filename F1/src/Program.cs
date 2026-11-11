namespace F1;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string? Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        // Build step: write the static SVG images into the plugin folder.
        if (Arg("--assets") is { } dir)
        {
            Directory.CreateDirectory(dir);
            foreach (var (name, svg) in Art.Assets()) File.WriteAllText(Path.Combine(dir, name + ".svg"), svg);
            return 0;
        }

        Store.Load();

        // Diagnostics without Stream Deck: what the plugin sees right now.
        // (WinExe has no console, so the report goes to a file.)
        if (Arg("--check") is { } report)
        {
            var mv = await MultiViewer.StateAsync();
            var lights = await Hue.LightsAsync(Store.Current.Group);
            var (count, cur) = Desktops.Info();
            File.WriteAllLines(report, new[]
            {
                $"hue: paired={Hue.Paired} group='{Store.Current.Group}' lights={lights.Count}",
                $"multiviewer: online={mv.Online} session={(mv.Timing != null)} track={mv.TrackStatus} players={mv.Players.Count}",
                $"desktops: {count}, current={cur + 1}",
            });
            return 0;
        }

        var port = Arg("-port");
        var uuid = Arg("-pluginUUID");
        var register = Arg("-registerEvent");
        if (port == null || uuid == null || register == null) return 1;

        try { await new Plugin(int.Parse(port), uuid, register).RunAsync(); }
        catch (Exception e) { Store.Log("fatal: " + e); return 1; }
        return 0;
    }
}
