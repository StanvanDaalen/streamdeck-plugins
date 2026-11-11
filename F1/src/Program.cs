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

        // Build check: the dial icons as the touch strip gets them (PNG).
        if (Arg("--png-test") is { } pngDir)
        {
            Directory.CreateDirectory(pngDir);
            foreach (var (name, glyph) in new[] { ("flag", Art.Flag(Art.Green)), ("bulb", Art.Bulb()), ("speaker", Art.Speaker()), ("clock", Art.Clock(Art.Red)) })
            {
                var url = Art.Png(Art.Icon(glyph), 72);
                File.WriteAllBytes(Path.Combine(pngDir, name + ".png"), Convert.FromBase64String(url[(url.IndexOf(',') + 1)..]));
            }
            if (Art.KeyWithLogo("F1 Mode") is { } logo)
                File.WriteAllBytes(Path.Combine(pngDir, "key-mode-logo.png"), Convert.FromBase64String(logo[(logo.IndexOf(',') + 1)..]));
            return 0;
        }

        Store.Load(freshLog: Arg("--check") == null);

        // Diagnostics without Stream Deck: what the plugin sees right now.
        // (WinExe has no console, so the report goes to a file.)
        if (Arg("--check") is { } report)
        {
            var mv = await MultiViewer.StateAsync();
            var zone = Store.Current.ZoneList.FirstOrDefault(z => z.Name == Store.Current.Group)
                       ?? new ZoneDef { Name = Store.Current.Group, Group = Store.Current.Group };
            var lights = await Hue.LightsAsync(zone);
            var perZone = new List<string>();
            foreach (var z in Store.Current.ZoneList) perZone.Add($"{z.Name}={(await Hue.LightsAsync(z)).Count}");
            var (ids, cur) = Desktops.Info();
            var empty = ids.Select((id, i) => $"{i + 1}:{(Desktops.IsEmpty(id, ignoreF1Apps: true) ? "leeg" : "bezet")}");
            var (live, type) = await Uf1.LiveSessionAsync();
            File.WriteAllLines(report, new[]
            {
                $"hue: paired={Hue.Paired} group='{Store.Current.Group}' lights={lights.Count} zones: {string.Join(", ", perZone)}",
                $"multiviewer: online={mv.Online} session={mv.Session ?? "-"} track={mv.TrackStatus} players={mv.Players.Count} main={mv.Main?.Title ?? "-"}",
                $"desktops: {ids.Count}, current={cur + 1}, {string.Join(' ', empty)}",
                $"uf1: ready={await Uf1.ReadyAsync()} live={live} type={type ?? "-"} race={Uf1.FindLayout("race") ?? "-"} quali={Uf1.FindLayout("quali") ?? "-"}",
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
