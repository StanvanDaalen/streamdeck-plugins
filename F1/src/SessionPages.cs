using System.Globalization;
using System.Text.Json.Nodes;

namespace F1;

/// <summary>
/// The Session key's pages, one per press, in this order: laps or time left, weather, wind, chance of rain (live
/// only), who leads, the fastest lap (race), and safety cars and red flags (when there were any). A page without
/// data is skipped; after 30 seconds the key goes back to the first page. Works for every session type.
/// </summary>
public static class SessionPages
{
    public const string Main = "main", Weather = "weather", Wind = "wind", Rain = "rain", Leader = "leader", Fastest = "fastest", Flags = "flags";
    private static readonly string[] Order = { Main, Weather, Wind, Rain, Leader, Fastest, Flags };

    private static JsonObject? _extra;          // TimingData and DriverList, fetched only while a page needs them
    private static DateTime _extraAt = DateTime.MinValue;
    private static (string Place, int? Chance, DateTime At)? _rain;
    private static bool _rainBusy;

    private static double? Num(JsonNode? n) =>
        double.TryParse(n?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>The next page with something to show after <paramref name="current"/>.</summary>
    public static string Next(string current, MvState mv)
    {
        int i = Array.IndexOf(Order, current);
        for (int step = 1; step <= Order.Length; step++)
        {
            var page = Order[(i + step) % Order.Length];
            if (page == Main || Has(page, mv)) return page;
        }
        return Main;
    }

    private static bool Has(string page, MvState mv) => page switch
    {
        Weather => mv.Weather.Track != null || mv.Weather.Air != null,
        Wind => Num(mv.Timing?["WeatherData"]?["WindSpeed"]) != null,
        Rain => IsLiveish(mv),
        Leader => true,   // filled in once the timing is there
        Fastest => mv.IsRace,
        Flags => Incidents(mv).Any,
        _ => true,
    };

    /// <summary>A session that is on now (or was a few hours ago): only then the forecast means something.</summary>
    private static bool IsLiveish(MvState mv)
    {
        var info = mv.Timing?["SessionInfo"];
        if (!DateTime.TryParse((string?)info?["StartDate"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var local)
            || !TimeSpan.TryParse(((string?)info?["GmtOffset"] ?? "0:00:00").TrimStart('+'), CultureInfo.InvariantCulture, out var off)) return false;
        var start = local - off;
        return Math.Abs((DateTime.UtcNow - start).TotalHours) < 4;
    }

    /// <summary>Every poll while a page needs more than the basic state: driver list and timing (every 3 s at most).</summary>
    public static async Task RefreshAsync(string page, MvState mv)
    {
        if (page is Leader or Fastest && DateTime.UtcNow - _extraAt > TimeSpan.FromSeconds(3))
        {
            _extraAt = DateTime.UtcNow;
            try { _extra = (await MultiViewer.QueryAsync("{ f1LiveTimingState { TimingData DriverList } }"))?["f1LiveTimingState"] as JsonObject; }
            catch { }
        }
        if (page == Rain && !_rainBusy)
        {
            var place = (string?)mv.Timing?["SessionInfo"]?["Meeting"]?["Location"];
            if (place != null && (_rain is not { } r || r.Place != place || DateTime.UtcNow - r.At > TimeSpan.FromMinutes(15)))
            {
                _rainBusy = true;
                try { _rain = (place, await Forecast.RainChanceAsync(place), DateTime.UtcNow); }
                finally { _rainBusy = false; }
            }
        }
    }

    private sealed record Line(string Tla, int Pos, double? Best, string? BestText, string? Interval);

    private static List<Line> Lines()
    {
        var lines = _extra?["TimingData"]?["Lines"] as JsonObject;
        var drivers = _extra?["DriverList"] as JsonObject;
        if (lines == null) return new();
        return lines.Select(kv =>
        {
            var l = kv.Value;
            var best = (string?)l?["BestLapTime"]?["Value"];
            return new Line((string?)drivers?[kv.Key]?["Tla"] ?? kv.Key,
                int.TryParse((string?)l?["Position"], out var p) ? p : 99,
                LapSeconds(best), string.IsNullOrEmpty(best) ? null : best,
                (string?)l?["IntervalToPositionAhead"]?["Value"]);
        }).OrderBy(l => l.Pos).ToList();
    }

    /// <summary>"1:34.876" → 94.876.</summary>
    private static double? LapSeconds(string? t)
    {
        if (string.IsNullOrEmpty(t)) return null;
        var parts = t.Split(':');
        double total = 0;
        foreach (var p in parts)
        {
            if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return null;
            total = total * 60 + v;
        }
        return total;
    }

    private static (int Sc, int Vsc, int Red, bool Any) Incidents(MvState mv)
    {
        IEnumerable<JsonNode?> msgs = mv.Timing?["RaceControlMessages"]?["Messages"] switch
        {
            JsonArray a => a,
            JsonObject o => o.Select(kv => kv.Value),
            _ => Array.Empty<JsonNode?>(),
        };
        int sc = 0, vsc = 0, red = 0;
        foreach (var m in msgs)
        {
            var text = ((string?)m?["Message"] ?? "").ToUpperInvariant();
            if ((string?)m?["Flag"] == "RED") red++;
            else if (text.Contains("VIRTUAL SAFETY CAR DEPLOYED")) vsc++;
            else if (text.Contains("SAFETY CAR DEPLOYED")) sc++;
        }
        return (sc, vsc, red, sc + vsc + red > 0);
    }

    private static string Compass(double deg) =>
        new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" }[(int)Math.Round(((deg % 360) + 360) % 360 / 45) % 8];

    /// <summary>The key for a page (null for the main page, which the plugin draws itself).</summary>
    public static string? Render(string page, MvState mv)
    {
        var w = mv.Timing?["WeatherData"];
        switch (page)
        {
            case Weather:
            {
                var (track, air, rain) = mv.Weather;
                return Art.InfoKey("Weather", track is { } t ? $"{t:0}°" : "–",
                    (air is { } a ? $"Air {a:0}°" : "") + (rain ? " · Rain" : " · Dry"), rain ? "#2D9CDB" : Art.White, "Track");
            }
            case Wind:
            {
                var speed = Num(w?["WindSpeed"]) * 3.6; // m/s → km/h
                var dir = Num(w?["WindDirection"]);
                var hum = Num(w?["Humidity"]);
                return Art.InfoKey("Wind", speed is { } s ? $"{s:0} km/h" : "–",
                    (dir is { } d ? "From " + Compass(d) : "") + (hum is { } h ? $" · {h:0}% hum." : ""), Art.White);
            }
            case Rain:
            {
                var chance = _rain?.Chance;
                return Art.InfoKey("Rain", chance is { } c ? $"{c}%" : "…", "Chance · next hour", chance is >= 50 ? "#2D9CDB" : Art.White);
            }
            case Leader:
            {
                var lines = Lines();
                if (lines.Count == 0 || lines[0].Pos != 1) return Art.InfoKey("Leader", "…", "Loading", Art.Grey);
                var p1 = lines[0];
                string caption;
                if (mv.IsRace)
                {
                    var p2 = lines.Count > 1 ? lines[1].Interval : null;
                    caption = string.IsNullOrEmpty(p2) ? "P1" : $"P1 · {p2} s ahead";
                }
                else
                {
                    var gap = lines.Count > 1 && lines[1].Best is { } b2 && p1.Best is { } b1 ? $" · +{b2 - b1:0.000}" : "";
                    caption = (p1.BestText ?? "No time yet") + gap;
                }
                return Art.InfoKey("Leader", p1.Tla, caption, Art.White);
            }
            case Fastest:
            {
                var best = Lines().Where(l => l.Best != null).MinBy(l => l.Best);
                return best == null
                    ? Art.InfoKey("Fastest lap", "…", "No laps yet", Art.Grey)
                    : Art.InfoKey("Fastest lap", best.BestText!, best.Tla, "#A855F7"); // F1's purple
            }
            case Flags:
            {
                var (sc, vsc, red, _) = Incidents(mv);
                var parts = new List<string>();
                if (sc > 0) parts.Add($"SC {sc}");
                if (vsc > 0) parts.Add($"VSC {vsc}");
                if (red > 0) parts.Add($"Red {red}");
                return Art.InfoKey("Incidents", parts.Count == 1 ? parts[0] : $"{sc + vsc + red}", string.Join(" · ", parts), red > 0 ? Art.Red : Art.Amber);
            }
        }
        return null;
    }
}

/// <summary>Chance of rain at the circuit for the coming hour (Open-Meteo, free and without a key).</summary>
public static class Forecast
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly Dictionary<string, (double Lat, double Lon)> Places = new();

    public static async Task<int?> RainChanceAsync(string place)
    {
        try
        {
            if (!Places.TryGetValue(place, out var at))
            {
                var g = JsonNode.Parse(await Http.GetStringAsync($"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(place)}&count=1"));
                if (g?["results"]?[0] is not { } r) return null;
                at = ((double)r["latitude"]!, (double)r["longitude"]!);
                Places[place] = at;
            }
            var url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={at.Lat:0.###}&longitude={at.Lon:0.###}&hourly=precipitation_probability&forecast_hours=2&timezone=UTC");
            var f = JsonNode.Parse(await Http.GetStringAsync(url));
            var values = (f?["hourly"]?["precipitation_probability"] as JsonArray)?.Select(v => (int?)v).Where(v => v != null).ToList();
            return values is { Count: > 0 } ? values.Max() : null;
        }
        catch (Exception e) { Store.Log("regenkans: " + e.Message); return null; }
    }
}
