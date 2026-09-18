namespace Jolti.Services;

/// <summary>Small local energy-based VAD for 16 kHz, mono, signed 16-bit PCM.</summary>
public sealed class VoiceActivityDetector
{
    private const double MinimumSpeechRms = 0.012;
    private const int SpeechFramesRequired = 4; // 200 ms with the recorder's 50 ms buffers.
    private const int SilenceFramesRequired = 30; // 1.5 seconds.
    private double _noiseFloor = 0.002;
    private int _speechFrames;
    private int _silentFrames;
    private bool _speechStarted;
    private bool _triggered;

    public bool AddPcm16(ReadOnlySpan<byte> pcm)
    {
        if (_triggered || pcm.Length < 2) return false;
        double squares = 0;
        var samples = pcm.Length / 2;
        for (var i = 0; i < samples; i++)
        {
            var sample = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
            var normalized = sample / 32768d;
            squares += normalized * normalized;
        }
        var rms = Math.Sqrt(squares / samples);
        var threshold = Math.Max(MinimumSpeechRms, _noiseFloor * 3.5);

        if (!_speechStarted)
        {
            if (rms >= threshold)
            {
                if (++_speechFrames >= SpeechFramesRequired) _speechStarted = true;
            }
            else
            {
                _speechFrames = 0;
                _noiseFloor = (_noiseFloor * 0.95) + (rms * 0.05);
            }
            return false;
        }

        _silentFrames = rms < threshold ? _silentFrames + 1 : 0;
        if (_silentFrames < SilenceFramesRequired) return false;
        _triggered = true;
        return true;
    }
}
