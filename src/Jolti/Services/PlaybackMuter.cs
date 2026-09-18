using NAudio.CoreAudioApi;
using Jolti.Core;

namespace Jolti.Services;

public sealed class PlaybackMuter : IPlaybackMuter
{
    private MMDevice? _device;
    private bool _wasMuted;

    public void Mute()
    {
        if (_device != null) return;
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        try
        {
            _wasMuted = device.AudioEndpointVolume.Mute;
            if (!_wasMuted) device.AudioEndpointVolume.Mute = true;
            _device = device;
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }

    public void Restore()
    {
        var device = _device;
        _device = null;
        if (device == null) return;
        try { device.AudioEndpointVolume.Mute = _wasMuted; }
        finally { device.Dispose(); }
    }

    public void Dispose() => Restore();
}
