using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json.Nodes;

namespace PS5Plugin;

public enum Ps5State { ObsClosed, Unknown, On, Off }

/// <summary>
/// Decides whether the PlayStation is on. Without HDMI signal the capture card delivers a black
/// picture and no sound; a frozen last frame is caught because a live console picture or its sound
/// always changes within a minute.
/// </summary>
public sealed class Detector
{
    private const float SoundThreshold = 0.004f; // about -48 dB
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(60);

    private DateTime _lastSound = DateTime.MinValue, _lastChange = DateTime.MinValue;
    private long _lastHash;
    private int _offVotes;
    private bool _captureResolved;
    private string _resolvedFor = "";

    public Ps5State State { get; private set; } = Ps5State.Unknown;
    public string Detail { get; private set; } = "";
    public bool VideoFound { get; private set; }
    public bool AudioFound { get; private set; }

    public void Sample(float? peak)
    {
        if (peak is > SoundThreshold) _lastSound = DateTime.UtcNow;
    }

    public void Reset()
    {
        _captureResolved = false;
        _offVotes = 0;
        State = Ps5State.Unknown;
    }

    public async Task Update(Obs obs, Audio audio, string videoSource, string audioSource)
    {
        if (!Obs.Running) { State = Ps5State.ObsClosed; Detail = "OBS not running"; _captureResolved = false; VideoFound = AudioFound = false; return; }
        if (!obs.Connected)
        {
            // Without obs-websocket the sound alone decides: the capture card's input is read from the
            // OBS scene file, so this still works when the WebSocket server is switched off.
            _captureResolved = false;
            var device = SceneDevice(audioSource);
            AudioFound = device != null;
            audio.UseCapture(device);
            Decide(video: null, soundOnly: true, device != null ? "by sound, no WebSocket" : $"source \"{audioSource}\" not found");
            return;
        }

        if (!_captureResolved || _resolvedFor != audioSource)
        {
            var settings = await obs.Request("GetInputSettings", new JsonObject { ["inputName"] = audioSource });
            AudioFound = settings != null;
            audio.UseCapture(settings?["inputSettings"]?["device_id"]?.GetValue<string>() ?? (settings != null ? "default" : null));
            _captureResolved = true;
            _resolvedFor = audioSource;
        }

        var shot = await obs.Request("GetSourceScreenshot", new JsonObject
        {
            ["sourceName"] = videoSource, ["imageFormat"] = "png", ["imageWidth"] = 32, ["imageHeight"] = 18,
        });
        VideoFound = shot != null;
        var frame = shot?["imageData"]?.GetValue<string>() is { } data ? Analyse(data) : null;
        Decide(frame, soundOnly: frame == null, frame == null ? $"source \"{videoSource}\" not found, sound only" : "");
    }

    private string? _sceneDevice, _sceneDeviceFor;
    private DateTime _sceneRead = DateTime.MinValue;

    /// <summary>The audio device of an OBS source, from the active scene collection on disk.</summary>
    private string? SceneDevice(string source)
    {
        if (_sceneDeviceFor == source && DateTime.UtcNow - _sceneRead < TimeSpan.FromSeconds(30)) return _sceneDevice;
        _sceneRead = DateTime.UtcNow; _sceneDeviceFor = source; _sceneDevice = null;
        try
        {
            var obsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio");
            string? file = null;
            foreach (var ini in new[] { "user.ini", "global.ini" })
            {
                var p = Path.Combine(obsDir, ini);
                if (!File.Exists(p)) continue;
                file = File.ReadLines(p).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("SceneCollectionFile="))?["SceneCollectionFile=".Length..];
                if (file != null) break;
            }
            // The active collection first, then every other one: the source is the same capture card everywhere.
            var dir = Path.Combine(obsDir, "basic", "scenes");
            if (file != null && !file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) file += ".json";
            var files = Directory.GetFiles(dir, "*.json")
                .OrderBy(f => file != null && Path.GetFileName(f).Equals(file, StringComparison.OrdinalIgnoreCase) ? 0 : 1);
            foreach (var f in files)
            {
                JsonNode? json;
                try { json = JsonNode.Parse(File.ReadAllText(f)); } catch { continue; }
                foreach (var s in json?["sources"]?.AsArray() ?? new JsonArray())
                    if (s?["name"]?.GetValue<string>() == source)
                        return _sceneDevice = s["settings"]?["device_id"]?.GetValue<string>() ?? "default";
            }
        }
        catch (Exception e) { Log.Write("scene file: " + e.Message); }
        return null;
    }

    private void Decide((int maxLuma, double dark, long hash)? video, bool soundOnly, string detail)
    {
        var now = DateTime.UtcNow;
        bool soundRecent = now - _lastSound < TimeSpan.FromSeconds(30);
        bool on, certainOff = false;
        if (video is { } v)
        {
            if (v.hash != _lastHash) { _lastHash = v.hash; _lastChange = now; }
            bool still = now - _lastChange >= Quiet;
            // A picture that stood still for a minute without sound is the card's own "no signal" screen
            // (or a frozen frame): remember it, so next time it is recognised at once.
            if (still && !soundRecent && v.maxLuma > 0) RememberDead(v.hash);
            bool dead = DeadFrames.Contains(v.hash);
            bool empty = v.maxLuma <= 24 || v.dark >= 0.93; // black, or black with only a small logo/text
            on = soundRecent || (!dead && !empty && !still);
            certainOff = !soundRecent && dead;
            Detail = soundRecent ? "sound" : dead || empty ? "no signal" : still ? "still picture, no sound" : "picture";
        }
        else
        {
            on = now - _lastSound < TimeSpan.FromSeconds(20);
            Detail = detail;
        }

        // On shows at once. Off shows at once for a known "no signal" screen; otherwise only after about
        // 10 seconds, so a dark, silent loading screen does not flicker the dial.
        if (on) { _offVotes = 0; State = Ps5State.On; }
        else if (certainOff || ++_offVotes >= 5 || State is Ps5State.Unknown or Ps5State.ObsClosed) State = Ps5State.Off;
    }

    private static readonly string DeadFile = Path.Combine(Log.Dir, "no-signal-frames.txt");
    private static readonly HashSet<long> DeadFrames = LoadDead();

    private static HashSet<long> LoadDead()
    {
        try { return File.ReadAllLines(DeadFile).Select(l => long.TryParse(l, out var h) ? h : 0).Where(h => h != 0).ToHashSet(); }
        catch { return new(); }
    }

    private static void RememberDead(long hash)
    {
        if (!DeadFrames.Add(hash)) return;
        Log.Write($"remembered no-signal frame {hash}");
        try { Directory.CreateDirectory(Log.Dir); File.WriteAllLines(DeadFile, DeadFrames.TakeLast(20).Select(h => h.ToString())); } catch { }
    }

    private static (int, double, long)? Analyse(string dataUrl)
    {
        try
        {
            var b64 = dataUrl[(dataUrl.IndexOf(',') + 1)..];
            using var ms = new MemoryStream(Convert.FromBase64String(b64));
            using var bmp = new Bitmap(ms);
            int max = 0, darkCount = 0;
            long hash = 17;
            for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                int l = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
                if (l > max) max = l;
                if (l < 20) darkCount++;
                hash = hash * 31 + c.ToArgb();
            }
            return (max, (double)darkCount / (bmp.Width * bmp.Height), hash);
        }
        catch (Exception e) { Log.Write("screenshot: " + e.Message); return null; }
    }
}
