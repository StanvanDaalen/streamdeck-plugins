using System.Runtime.InteropServices;

namespace ScarlettSwitch.Core;

/// <summary>
/// Windows (Core Audio) master volume of the Focusrite playback endpoint, with change notifications.
/// Used only for the dial rotation; output switching never touches it.
/// </summary>
public sealed class EndpointVolume : IDisposable
{
    private IAudioEndpointVolume? _vol;
    private Callback? _cb;

    public event Action? Changed;

    public bool Available => _vol != null;

    /// <summary>(Re)binds to the first active render endpoint whose name contains Focusrite/Scarlett.</summary>
    public bool Bind()
    {
        if (_vol != null) return true;
        try
        {
            // Created by CLSID: NAudio registers its own managed wrapper for this same COM class,
            // which would make a typed "new" fail with an invalid cast.
            var en = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
            en.EnumAudioEndpoints(0 /*eRender*/, 1 /*ACTIVE*/, out var col);
            col.GetCount(out uint n);
            for (uint i = 0; i < n; i++)
            {
                col.Item(i, out var dev);
                dev.OpenPropertyStore(0, out var store);
                var key = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
                store.GetValue(ref key, out var pv);
                string name = pv.vt == 31 ? Marshal.PtrToStringUni(pv.ptr) ?? "" : "";
                PropVariantClear(ref pv);
                if (!name.Contains("Focusrite", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("Scarlett", StringComparison.OrdinalIgnoreCase)) continue;

                var iid = typeof(IAudioEndpointVolume).GUID;
                dev.Activate(ref iid, 1 /*CLSCTX_INPROC_SERVER*/, IntPtr.Zero, out var obj);
                _vol = (IAudioEndpointVolume)obj;
                _cb = new Callback(this);
                _vol.RegisterControlChangeNotify(_cb);
                return true;
            }
        }
        catch (Exception e) { GameVolume.SettingsStore.Log("endpoint bind: " + e.Message); Release(); }
        return false;
    }

    /// <summary>Volume 0-100, or -1 when unavailable.</summary>
    public int Percent
    {
        get
        {
            try { if (_vol != null) { _vol.GetMasterVolumeLevelScalar(out float v); return (int)Math.Round(v * 100); } }
            catch { Release(); }
            return -1;
        }
    }

    public bool Muted
    {
        get
        {
            try { if (_vol != null) { _vol.GetMute(out bool m); return m; } }
            catch { Release(); }
            return false;
        }
    }

    public void Adjust(int deltaPercent)
    {
        if (!Bind()) return;
        try
        {
            int p = Math.Clamp(Percent + deltaPercent, 0, 100);
            var ctx = Guid.Empty;
            _vol!.SetMasterVolumeLevelScalar(p / 100f, ref ctx);
            if (Muted && deltaPercent > 0) _vol.SetMute(false, ref ctx);
        }
        catch { Release(); }
    }

    public void ToggleMute()
    {
        if (!Bind()) return;
        try { var ctx = Guid.Empty; _vol!.SetMute(!Muted, ref ctx); }
        catch { Release(); }
    }

    private void Release()
    {
        try { if (_vol != null && _cb != null) _vol.UnregisterControlChangeNotify(_cb); } catch { }
        _vol = null;
        _cb = null;
    }

    public void Dispose() => Release();

    private sealed class Callback(EndpointVolume owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr data) { owner.Changed?.Invoke(); return 0; }
    }

    // ---- Core Audio interop (only the members used; vtable order matters) ----
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }

    [StructLayout(LayoutKind.Sequential)] private struct PROPERTYKEY { public Guid fmtid; public int pid; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PROPVARIANT { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr ptr; }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PROPVARIANT pv);

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr notifyData);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        void UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        void GetChannelCount(out uint count);
        void SetMasterVolumeLevel(float levelDb, ref Guid ctx);
        void SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        void GetMasterVolumeLevel(out float levelDb);
        void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(uint ch, float levelDb, ref Guid ctx);
        void SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
        void GetChannelVolumeLevel(uint ch, out float levelDb);
        void GetChannelVolumeLevelScalar(uint ch, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
