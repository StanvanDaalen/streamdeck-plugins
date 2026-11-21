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
    private List<AudioSessionControl> _sessions = new();
    private MMDevice? _capture;
    private string? _captureId;

    public bool HasSessions => _sessions.Count > 0;

    public void Refresh()
    {
        foreach (var s in _sessions) try { s.Dispose(); } catch { }
        foreach (var d in _devices) try { d.Dispose(); } catch { }
        _devices.Clear();
        var list = new List<AudioSessionControl>();
        var pids = System.Diagnostics.Process.GetProcessesByName(Process).Select(p => (uint)p.Id).ToHashSet();
        if (pids.Count > 0)
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
                        if (!c.IsSystemSoundsSession && pids.Contains(c.GetProcessID)
                            && c.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired)
                            list.Add(c);
                        else c.Dispose();
                    }
                }
                catch (Exception e) { Log.Write("audio device: " + e.Message); }
            }
        }
        _sessions = list;
    }

    public float? Volume
    {
        get
        {
            float? max = null;
            foreach (var s in _sessions)
                try { var v = s.SimpleAudioVolume.Volume; if (max == null || v > max) max = v; } catch { }
            return max;
        }
    }

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
