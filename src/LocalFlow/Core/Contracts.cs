namespace LocalFlow.Core;

public sealed class AppSettings
{
    public int MicrophoneId { get; set; } = -1;
    public string Hotkey { get; set; } = "Ctrl + Win";
    public string TranscriptionMode { get; set; } = "Local Whisper";
    public string ModelPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalFlow", "models", "ggml-base.en.bin");
    public bool CleanupEnabled { get; set; } = true;
    public bool SaveHistory { get; set; }
    public bool RecordingSoundEnabled { get; set; } = true;
}

public sealed record Microphone(int Id, string Name);
public sealed record HistoryEntry(Guid Id, DateTimeOffset Date, string RawTranscript, string FinalText);
public interface IAudioRecorder : IDisposable
{
    IReadOnlyList<Microphone> GetMicrophones();
    void Start(int microphoneId);
    Task<byte[]> StopAsync();
    event Action<Exception>? Failed;
}
public interface ITranscriptionService
{
    Task<string> TranscribeAsync(byte[] wavAudio, CancellationToken cancellationToken);
}
public interface ITextCleanupService { string Clean(string text); }
public interface IRecordingSoundService
{
    Task PlayStartAsync(CancellationToken cancellationToken);
    Task PlayEndAsync(CancellationToken cancellationToken);
}
public interface IConfigurableTranscriptionService : ITranscriptionService
{
    void Configure(AppSettings settings);
    void Validate();
}
public interface ITextPaster
{
    nint CaptureTarget();
    Task PasteAsync(string text, nint target, CancellationToken cancellationToken);
    void Copy(string text);
}
public interface IHotkeyService : IDisposable
{
    event Action? Pressed;
    event Action? Released;
    void Configure(string hotkey);
}
public interface IHistoryRepository
{
    IReadOnlyList<HistoryEntry> Load();
    void Add(HistoryEntry entry);
    void Delete(Guid id);
    void Clear();
}
public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}
