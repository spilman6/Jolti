using System.Text;
using Jolti.Core;
using Whisper.net;
using Whisper.net.LibraryLoader;
using Jolti.Infrastructure;
namespace Jolti.Services;

/// <summary>Explicit local/test routing; no cloud or fake fallback on errors.</summary>
public sealed class WhisperTranscriptionService : IConfigurableTranscriptionService, IDisposable
{
    static WhisperTranscriptionService() => RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FileStream? _verified;
    private WhisperFactory? _factory;
    private bool _disposed;
    private void ReleaseModel() { _factory?.Dispose(); _factory = null; _verified?.Dispose(); _verified = null; }
    public void Dispose() { _gate.Wait(); try { _disposed = true; ReleaseModel(); } finally { _gate.Release(); } }
    private void VerifyModel() { ObjectDisposedException.ThrowIf(_disposed, this); _verified ??= new VerifiedModel().Open(_modelPath); }
    private string _mode = "Local Whisper";
    private string _modelPath = new AppSettings().ModelPath;
    public void Configure(AppSettings settings)
    {
        if (settings.TranscriptionMode is not ("Local Whisper" or "Fake (test only)"))
            throw new InvalidOperationException("Choose Local Whisper or Fake (test only) in Settings.");
        _gate.Wait();
        try {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_mode != settings.TranscriptionMode || _modelPath != settings.ModelPath) ReleaseModel();
            _mode = settings.TranscriptionMode; _modelPath = settings.ModelPath;
        } finally { _gate.Release(); }
    }
    public void Validate()
    {
        _gate.Wait();
        try { if (_mode != "Fake (test only)") VerifyModel(); }
        finally { _gate.Release(); }
    }
    public Task<string> TranscribeAsync(byte[] wavAudio, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_mode == "Fake (test only)") return new FakeTranscriptionService().TranscribeAsync(wavAudio, cancellationToken);

        // Keep verified weights in memory, but use a fresh processor without prior speech context.
        return Task.Run(async () =>
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                VerifyModel();
                cancellationToken.ThrowIfCancellationRequested();
                _factory ??= WhisperFactory.FromPath(_verified!.Name);
                cancellationToken.ThrowIfCancellationRequested();
                using var processor = _factory.CreateBuilder()
                    .WithLanguage("en")
                    .WithNoContext().WithThreads(Math.Max(1, Math.Min(8, Environment.ProcessorCount - 1))).Build();
                using var stream = new MemoryStream(wavAudio, writable: false);
                var text = new StringBuilder();
                await foreach (var segment in processor.ProcessAsync(stream, cancellationToken))
                    if (!string.IsNullOrWhiteSpace(segment.Text)) text.Append(segment.Text.Trim()).Append(' ');
                cancellationToken.ThrowIfCancellationRequested();
                var result = text.ToString().Trim();
                if (result.Length == 0) throw new InvalidOperationException("No speech recognized. Check the microphone and try speaking closer to it.");
                return result;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or TypeInitializationException)
            {
                throw new InvalidOperationException("Whisper could not load its native runtime. Keep the full publish folder together and install the Microsoft Visual C++ 2015-2022 x64 Redistributable. " + ex.Message, ex);
            }
            finally { _gate.Release(); }
        }, cancellationToken);
    }
}
