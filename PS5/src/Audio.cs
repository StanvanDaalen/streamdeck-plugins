using NAudio.CoreAudioApi;

namespace PS5Plugin;

/// <summary>
/// What you hear of the PS5 is OBS's monitoring output, so the volume is OBS's own entry in the
/// Windows volume mixer: it changes only what you hear, never the stream or recording.
/// The capture card's input level shows whether sound is coming in.
/// </summary>
public sealed class Audio
{
    private const string Process = "obs64";
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly List<MMDevice> _devices = new();
    private List<AudioSessionControl> _sessions = new(), _discord = new();
    private MMDevice? _capture;
    private string? _captureId;

    public bool HasSessions => _sessions.Count > 0;

    public void Refresh()
    {
        foreach (var x in _sessions.Concat(_discord)) try { x.Dispose(); } catch { }
        foreach (var d in _devices) try { d.Dispose(); } catch { }
        _devices.Clear();
        var obs = new List<AudioSessionControl>();
        var discord = new List<AudioSessionControl>();
        var obsPids = Pids(Process);
        var discordPids = Pids("Discord");
        if (obsPids.Count > 0 || discordPids.Count > 0)
        {
            foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                _devices.Add(device);
                try
                {
                    var mgr = device.AudioSessionManager;
                    mgr.RefreshSessions();
                    var sessions = mgr.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var c = sessions[i];
                        bool live = !c.IsSystemSoundsSession && c.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired;
                        if (live && obsPids.Contains(c.GetProcessID)) obs.Add(c);
                        else if (live && discordPids.Contains(c.GetProcessID)) discord.Add(c);
                        else c.Dispose();
                    }
                }
                catch (Exception e) { Log.Write("audio device: " + e.Message); }
            }
        }
        _sessions = obs;
        _discord = discord;
    }

    private static HashSet<uint> Pids(string name) =>
        System.Diagnostics.Process.GetProcessesByName(name).Select(p => (uint)p.Id).ToHashSet();

    /// <summary>Discord's own entry in the Windows volume mixer (what you hear of the call).</summary>
    public bool HasDiscord => _discord.Count > 0;

    public float? DiscordVolume => MaxVolume(_discord);

    public void SetDiscordVolume(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        foreach (var s in _discord) try { s.SimpleAudioVolume.Volume = value; } catch { }
    }

    private static float? MaxVolume(List<AudioSessionControl> list)
    {
        float? max = null;
        foreach (var s in list)
            try { var v = s.SimpleAudioVolume.Volume; if (max == null || v > max) max = v; } catch { }
        return max;
    }

    public float? Volume => MaxVolume(_sessions);

    public bool Muted
    {
        get
        {
            var any = false;
            foreach (var s in _sessions)
                try { if (!s.SimpleAudioVolume.Mute) return false; any = true; } catch { }
            return any;
        }
    }

    public void SetVolume(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        foreach (var s in _sessions) try { s.SimpleAudioVolume.Volume = value; } catch { }
    }

    public void SetMute(bool mute)
    {
        foreach (var s in _sessions) try { s.SimpleAudioVolume.Mute = mute; } catch { }
    }

    /// <summary>The capture card's audio input, as OBS names it ("default" = the default recording device).</summary>
    public void UseCapture(string? id)
    {
        if (id == _captureId && _capture != null) return;
        try { _capture?.Dispose(); } catch { }
        _capture = null;
        _captureId = id;
        if (string.IsNullOrEmpty(id)) return;
        try
        {
            _capture = id == "default"
                ? _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console)
                : _enumerator.GetDevice(id);
        }
        catch (Exception e) { Log.Write("capture device: " + e.Message); }
    }

    /// <summary>Peak 0..1 of the incoming PS5 sound; only meaningful while OBS captures it. Null when unknown.</summary>
    public float? InputPeak
    {
        get
        {
            if (_capture == null) return null;
            try { return _capture.AudioMeterInformation.MasterPeakValue; } catch { return null; }
        }
    }
}
