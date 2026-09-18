using Jolti.Core;
using NAudio.Wave;

namespace Jolti.Services;

public sealed class AudioRecorder : IAudioRecorder
{
    private WaveInEvent? _input;
    private MemoryStream? _stream;
    private WaveFileWriter? _writer;
    private TaskCompletionSource<byte[]>? _completion;
    private Exception? _captureError;
    private VoiceActivityDetector? _vad;
    private readonly object _gate = new();
    public event Action<Exception>? Failed;
    public event Action? SilenceDetected;

    public IReadOnlyList<Microphone> GetMicrophones()
    {
        var result = new List<Microphone> { new(-1, "Windows default microphone") };
        for (var i = 0; i < WaveIn.DeviceCount; i++) result.Add(new(i, WaveIn.GetCapabilities(i).ProductName));
        return result;
    }

    public void Start(int microphoneId, bool autoStopOnSilence = true)
    {
        if (_input != null) throw new InvalidOperationException("The microphone is already recording.");
        if (WaveIn.DeviceCount == 0) throw new InvalidOperationException("No microphone found. Connect one and allow desktop microphone access in Windows Settings.");
        try
        {
            _captureError = null;
            _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _stream = new MemoryStream();
            _input = new WaveInEvent { DeviceNumber = microphoneId, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 50 };
            _writer = new WaveFileWriter(_stream, _input.WaveFormat);
            _vad = autoStopOnSilence ? new VoiceActivityDetector() : null;
            _input.DataAvailable += OnData;
            _input.RecordingStopped += OnStopped;
            _input.StartRecording();
        }
        catch { Release(); throw; }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var silenceDetected = false;
        lock (_gate)
        {
            try
            {
                _writer?.Write(e.Buffer, 0, e.BytesRecorded);
                silenceDetected = _vad?.AddPcm16(e.Buffer.AsSpan(0, e.BytesRecorded)) == true;
            }
            catch (Exception ex) { _captureError = ex; _input?.StopRecording(); }
        }
        if (silenceDetected) SilenceDetected?.Invoke();
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        var error = e.Exception ?? _captureError;
        lock (_gate)
        {
            _vad = null;
            try
            {
                if (error != null) _completion?.TrySetException(error);
                else
                {
                    _writer?.Flush(); // Updates the WAV header before copying the in-memory buffer.
                    _completion?.TrySetResult(_stream?.ToArray() ?? []);
                }
            }
            catch (Exception ex) { error = ex; _completion?.TrySetException(ex); }
        }
        if (error != null) Failed?.Invoke(error);
    }

    public async Task<byte[]> StopAsync()
    {
        if (_input == null || _completion == null) throw new InvalidOperationException("The microphone is not recording.");
        try
        {
            _input.StopRecording();
            return await _completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { Release(); }
    }

    private void Release()
    {
        var input = _input;
        _input = null;
        if (input != null)
        {
            input.DataAvailable -= OnData;
            input.RecordingStopped -= OnStopped;
            input.Dispose();
        }
        lock (_gate)
        {
            _writer?.Dispose(); _writer = null;
            if (_stream != null)
            {
                if (_stream.TryGetBuffer(out var buffer)) Array.Clear(buffer.Array!, buffer.Offset, buffer.Count);
                _stream.Dispose(); _stream = null;
            }
        }
    }
    public void Dispose() => Release();
}
