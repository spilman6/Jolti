using System.Text;
using LocalFlow.Core;
using Whisper.net;
using Whisper.net.LibraryLoader;
using LocalFlow.Infrastructure;
namespace LocalFlow.Services;

/// <summary>Explicit local/test routing; no cloud or fake fallback on errors.</summary>
public sealed class WhisperTranscriptionService : IConfigurableTranscriptionService
{
    static WhisperTranscriptionService() => RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];
    private string _mode = "Local Whisper";
    private string _modelPath = new AppSettings().ModelPath;
    public void Configure(AppSettings settings)
    {
        if (settings.TranscriptionMode is not ("Local Whisper" or "Fake (test only)"))
            throw new InvalidOperationException("Choose Local Whisper or Fake (test only) in Settings.");
        _mode = settings.TranscriptionMode; _modelPath = settings.ModelPath;
    }
    public void Validate()
    {
        if (_mode == "Fake (test only)") return;
        using var verified = new VerifiedModel().Open(_modelPath);
    }
    public Task<string> TranscribeAsync(byte[] wavAudio, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_mode == "Fake (test only)") return new FakeTranscriptionService().TranscribeAsync(wavAudio, cancellationToken);
        var path = _modelPath;
        // Run model loading and inference off the dispatcher. Fresh processors prevent cross-session context.
        return Task.Run(async () =>
        {
            try
            {
                using var verified = new VerifiedModel().Open(path);
                cancellationToken.ThrowIfCancellationRequested();
                using var factory = WhisperFactory.FromPath(verified.Name);
                cancellationToken.ThrowIfCancellationRequested();
                using var processor = factory.CreateBuilder()
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
        }, cancellationToken);
    }
}
