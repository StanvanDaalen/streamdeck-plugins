using NAudio.CoreAudioApi;

namespace GameVolume;

public sealed record Session(int Pid, string? Path, string Process, string Id, AudioSessionControl Control);

/// <summary>Per-process audio sessions across every active output device.</summary>
public sealed class AudioSessions
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly List<MMDevice> _devices = new();
    public List<Session> All { get; private set; } = new();

    public void Refresh()
    {
        foreach (var s in All) try { s.Control.Dispose(); } catch { }
        foreach (var d in _devices) try { d.Dispose(); } catch { }
        _devices.Clear();
        var list = new List<Session>();

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
                    if (c.IsSystemSoundsSession || c.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired) continue;
                    int pid = (int)c.GetProcessID;
                    if (pid == 0) continue;
                    var path = Native.ProcessPath((uint)pid);
                    var proc = path != null ? System.IO.Path.GetFileNameWithoutExtension(path) : "";
                    list.Add(new Session(pid, path, proc, c.GetSessionInstanceIdentifier, c));
                }
            }
            catch (Exception e) { SettingsStore.Log("audio device: " + e.Message); }
        }
        All = list;
    }

    public IEnumerable<Session> ForProcess(string name) =>
        All.Where(s => s.Process.Equals(name, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<Session> ForGame(RunningGame g) =>
        All.Where(s => g.Pids.Contains(s.Pid)
                       || (g.Entry.InstallDir != null && s.Path != null
                           && s.Path.StartsWith(g.Entry.InstallDir, StringComparison.OrdinalIgnoreCase)));

    public static float? Volume(IEnumerable<Session> sessions)
    {
        float? max = null;
        foreach (var s in sessions)
            try { var v = s.Control.SimpleAudioVolume.Volume; if (max == null || v > max) max = v; } catch { }
        return max;
    }

    public static bool Muted(IEnumerable<Session> sessions)
    {
        var any = false;
        foreach (var s in sessions)
            try { if (!s.Control.SimpleAudioVolume.Mute) return false; any = true; } catch { }
        return any;
    }

    public static void SetVolume(IEnumerable<Session> sessions, float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        foreach (var s in sessions) try { s.Control.SimpleAudioVolume.Volume = value; } catch { }
    }

    public static void SetMute(IEnumerable<Session> sessions, bool mute)
    {
        foreach (var s in sessions) try { s.Control.SimpleAudioVolume.Mute = mute; } catch { }
    }
}
