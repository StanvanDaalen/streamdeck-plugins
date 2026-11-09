namespace ScarlettSwitch.Core;

public enum LinkStatus { Ready, NoDevice, UnsupportedDevice, NoFocusriteControl, NoMidiPort, Error }

/// <summary>
/// Switches the physical output via Focusrite Control 2's official MIDI control:
/// the mix bus feeding the inactive output is muted (NRPN 58:2/6/10), the active one is unmuted.
/// Levels are never touched, so volumes stay exactly as set. When FC2 is closed it is started
/// invisibly for the switch and closed again afterwards (the Scarlett keeps the state in hardware).
/// </summary>
public sealed class OutputController : IDisposable
{
    private const int MixBusMsb = 58;                                   // "Mix bus" in FC2 mixer MSB table
    private static readonly TimeSpan BurstLength = TimeSpan.FromSeconds(7); // FC2 cold start → MIDI ready ≈ 1–2 s
    private static readonly TimeSpan BurstInterval = TimeSpan.FromMilliseconds(250);

    private readonly Settings _settings;
    private readonly SynchronizationContext _ui;
    private readonly System.Threading.Timer _monitor;
    private readonly FocusriteLauncher _launcher = new();
    private readonly object _gate = new();
    private MidiOut? _midi;
    private bool _fc2WasRunning, _applyOnNextPoll, _started, _bursting;
    private DateTime _burstUntil;

    public Output Output => _settings.Output;
    public LinkStatus Status { get; private set; } = LinkStatus.NoDevice;
    public ScarlettDevice? Device { get; private set; }
    public string? PortName => _midi?.PortName;

    /// <summary>Raised whenever output or status changes.</summary>
    public event Action? Changed;

    public OutputController(Settings settings)
    {
        _settings = settings;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _monitor = new System.Threading.Timer(_ => Poll(), null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    public void Set(Output output)
    {
        lock (_gate)
        {
            _settings.Output = output;
            _settings.Save();
            Apply(userAction: true);
        }
        RaiseChanged();
    }

    public void Toggle() => Set(Output == Output.Speakers ? Output.Headphones : Output.Speakers);

    /// <summary>Re-sends the current state (after a settings change).</summary>
    public void Reapply()
    {
        lock (_gate) { ReopenPort(); Apply(userAction: false); }
        RaiseChanged();
    }

    private void Poll()
    {
        bool changed;
        lock (_gate)
        {
            var before = (Status, Device);
            Device = ScarlettDevice.Detect();
            bool fc2 = FocusriteLauncher.IsRunning;
            bool userStartedFc2 = fc2 && !_fc2WasRunning && !_launcher.OwnsInstance;
            _fc2WasRunning = fc2;

            if (_midi == null) ReopenPort();
            if (userStartedFc2 && _started)
            {
                // FC2 was opened by the user: give it one poll interval to open its MIDI input, then assert our state.
                _applyOnNextPoll = true;
                Status = Evaluate();
            }
            else if (_applyOnNextPoll || before.Status != LinkStatus.Ready)
            {
                _applyOnNextPoll = false;
                Apply(userAction: false);
            }
            else Status = Evaluate();
            _started = true;
            changed = before != (Status, Device);
        }
        if (changed) RaiseChanged();
    }

    private LinkStatus Evaluate()
    {
        if (Device == null) return LinkStatus.NoDevice;
        if (!Device.SupportedByFocusriteControl2) return LinkStatus.UnsupportedDevice;
        if (!FocusriteLauncher.IsRunning && !(CanLaunch && FocusriteLauncher.Installed)) return LinkStatus.NoFocusriteControl;
        if (_midi == null) return LinkStatus.NoMidiPort;
        return LinkStatus.Ready;
    }

    private bool CanLaunch => _settings.FocusriteMode != FocusriteMode.Manual;

    private void ReopenPort()
    {
        _midi?.Dispose();
        _midi = MidiOut.Open(_settings.MidiPort);
        if (_midi == null && LoopbackSetup.EnsureDefault(_settings.MidiPort))
            _midi = MidiOut.Open(_settings.MidiPort);
    }

    /// <summary>Sends the state; on a user action starts FC2 (hidden) first when it is not running.</summary>
    private void Apply(bool userAction)
    {
        Status = Evaluate();
        if (Status != LinkStatus.Ready || _midi == null) return;

        if (!FocusriteLauncher.IsRunning)
        {
            // Without a user action there is nothing to do: the Scarlett already holds the last state in hardware.
            if (!userAction || !_launcher.StartHidden()) return;
            _fc2WasRunning = true;
        }

        Send();
        if (_launcher.OwnsInstance)
        {
            // FC2 needs ~1–2 s after start to listen; absolute values make resending harmless.
            _burstUntil = DateTime.UtcNow + BurstLength;
            if (!_bursting) { _bursting = true; _ = Task.Run(Burst); }
            if (_settings.FocusriteMode == FocusriteMode.AutoClose)
                _launcher.ScheduleClose(BurstLength + TimeSpan.FromSeconds(_settings.CloseAfterSeconds));
        }
    }

    private async Task Burst()
    {
        while (true)
        {
            await Task.Delay(BurstInterval);
            lock (_gate)
            {
                if (DateTime.UtcNow > _burstUntil || !_launcher.OwnsInstance) { _bursting = false; return; }
                Send();
            }
        }
    }

    private void Send()
    {
        int on = _settings.Output == Output.Speakers ? _settings.SpeakerMix : _settings.HeadphoneMix;
        int off = _settings.Output == Output.Speakers ? _settings.HeadphoneMix : _settings.SpeakerMix;
        try
        {
            // Mute the inactive output first so both are never audible at the same time.
            if (off != on) _midi!.Nrpn(0, MixBusMsb, MuteLsb(off), 127);
            _midi!.Nrpn(0, MixBusMsb, MuteLsb(on), 0);
        }
        catch
        {
            ReopenPort();
            Status = LinkStatus.Error;
        }
    }

    private static int MuteLsb(int mix) => mix * 4 + 2; // Mix A = 2, Mix B = 6, Mix C = 10 ...

    private void RaiseChanged() => _ui.Post(_ => Changed?.Invoke(), null);

    public void Dispose()
    {
        _monitor.Dispose();
        _launcher.Dispose();
        lock (_gate) { _midi?.Dispose(); _midi = null; }
    }
}
