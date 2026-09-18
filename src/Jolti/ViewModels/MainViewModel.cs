using System.Collections.ObjectModel;
using System.Windows.Threading;
using Jolti.Core;
using Jolti.Services;
namespace Jolti.ViewModels;
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IAudioRecorder _audio;
    private readonly ITranscriptionService _transcriber;
    private readonly ITextCleanupService _cleanup;
    private readonly ITextPaster _paster;
    private readonly IHotkeyService _hotkey;
    private readonly ISettingsStore _store;
    private readonly IHistoryRepository _history;
    private readonly IRecordingSoundService? _sound;
    private readonly IPlaybackMuter? _playbackMuter;
    private bool _recordingSoundEnabled = true;
    private bool _mutePlaybackWhileRecording;
    private CancellationTokenSource? _soundPlayback;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _limit = new() { Interval = TimeSpan.FromMinutes(2) };
    private AppSettings _saved = new();
    private bool _recording, _busy, _countdown, _heldRecording;
    private nint _target;
    private string _status = "Idle", _message = "Ready. Focus a text field, then hold Ctrl + Win.", _raw = "", _final = "";
    private int _microphoneId = -1;
    private string _selectedHotkey = "Ctrl + Win";
    private bool _cleanupEnabled = true, _saveHistory, _autoStopOnSilence = true, _spokenCommandsEnabled = true;
    private CancellationTokenSource? _processing;
    private string _transcriptionMode = "Local Whisper";
    private string _modelPath = new AppSettings().ModelPath;
    public ObservableCollection<Microphone> Microphones { get; } = [];
    public ObservableCollection<HistoryEntry> History { get; } = [];
    public string[] Hotkeys => HotkeyService.SupportedHotkeys;
    public string[] TranscriptionModes { get; } = ["Local Whisper", "Fake (test only)"];
    public string TranscriptionMode { get => _transcriptionMode; set { Set(ref _transcriptionMode, value); SettingsChanged(); } }
    public string ModelPath { get => _modelPath; set { Set(ref _modelPath, value); Notify(nameof(SelectedModel)); RefreshModelState(true); SettingsChanged(); } }
    public string ActiveHotkey => _saved.Hotkey;
    public bool IsTestMode => _saved.TranscriptionMode == "Fake (test only)";
    public bool IsProcessing => _busy;
    public bool HasResult => !string.IsNullOrWhiteSpace(FinalText);
    public string HistoryNote => _saved.SaveHistory ? "New transcripts are saved on this device." : "History is off. Turn it on in Settings to save future transcripts.";
    public string SettingsNote => MicrophoneId != _saved.MicrophoneId || SelectedHotkey != _saved.Hotkey ||
        TranscriptionMode != _saved.TranscriptionMode || ModelPath != _saved.ModelPath ||
        CleanupEnabled != _saved.CleanupEnabled || SaveHistory != _saved.SaveHistory || RecordingSoundEnabled != _saved.RecordingSoundEnabled ||
        MutePlaybackWhileRecording != _saved.MutePlaybackWhileRecording || AutoStopOnSilence != _saved.AutoStopOnSilence ||
        SpokenCommandsEnabled != _saved.SpokenCommandsEnabled
        ? "You have unsaved changes." : "Your settings are up to date.";
    private void SettingsChanged() { Notify(nameof(SettingsNote)); Notify(nameof(DownloadStatus)); }
    public string ProviderDescription => _saved.TranscriptionMode == "Fake (test only)"
        ? "TEST MODE - A fixed sentence is returned. Select Local Whisper in Settings to recognize speech."
        : "LOCAL WHISPER - Speech is transcribed on this device. No audio is uploaded. Loading the model can take a moment.";
    public RelayCommand CancelCommand { get; }
    public string Status { get => _status; private set { Set(ref _status, value); Notify(nameof(StatusColor)); } }
    public string StatusColor => Status switch { "Recording" => "#E05266", "Transcribing" => "#E0A52D", "Text inserted" => "#178773", "Error" => "#CA3F4F", _ => "#54738B" };
    public string Message { get => _message; private set => Set(ref _message, value); }
    public string RawTranscript { get => _raw; private set => Set(ref _raw, value); }
    public string FinalText { get => _final; private set { Set(ref _final, value); Notify(nameof(HasResult)); } }
    public int MicrophoneId { get => _microphoneId; set { Set(ref _microphoneId, value); SettingsChanged(); } }
    public string SelectedHotkey { get => _selectedHotkey; set { Set(ref _selectedHotkey, value); SettingsChanged(); } }
    public bool CleanupEnabled { get => _cleanupEnabled; set { Set(ref _cleanupEnabled, value); SettingsChanged(); } }
    public bool SaveHistory { get => _saveHistory; set { Set(ref _saveHistory, value); SettingsChanged(); } }
    public bool RecordingSoundEnabled { get => _recordingSoundEnabled; set { Set(ref _recordingSoundEnabled, value); SettingsChanged(); } }
    public bool MutePlaybackWhileRecording { get => _mutePlaybackWhileRecording; set { Set(ref _mutePlaybackWhileRecording, value); SettingsChanged(); } }
    public bool AutoStopOnSilence { get => _autoStopOnSilence; set { Set(ref _autoStopOnSilence, value); SettingsChanged(); } }
    public bool SpokenCommandsEnabled { get => _spokenCommandsEnabled; set { Set(ref _spokenCommandsEnabled, value); SettingsChanged(); } }
    public bool CanEdit => !_busy && !_recording && !_countdown;
    public string RecordButtonText => _recording ? "Stop dictation" : "Start in 3 seconds";
    public RelayCommand ToggleCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand RefreshMicrophonesCommand { get; }
    public RelayCommand CopyFinalCommand { get; }
    public RelayCommand CopyHistoryCommand { get; }
    public RelayCommand DeleteHistoryCommand { get; }
    public RelayCommand ClearHistoryCommand { get; }
    public MainViewModel(IAudioRecorder audio, ITranscriptionService transcriber, ITextCleanupService cleanup,
        ITextPaster paster, IHotkeyService hotkey, ISettingsStore store, IHistoryRepository history, IRecordingSoundService? sound = null, IDictionaryRepository? dictionary = null, ISnippetRepository? snippets = null, IPlaybackMuter? playbackMuter = null)
    {
        (_audio, _transcriber, _cleanup, _paster, _hotkey, _store, _history) = (audio, transcriber, cleanup, paster, hotkey, store, history);
        _sound = sound;
        _playbackMuter = playbackMuter;
        InitializeModels();
        InitializeDictionary(dictionary ?? store as IDictionaryRepository);
        InitializeSnippets(snippets ?? store as ISnippetRepository);
        ToggleCommand = new(_ => Toggle(), () => !_busy && !_countdown);
        CancelCommand = new(_ => _processing?.Cancel(), () => _busy && _processing != null);
        SaveSettingsCommand = new(_ => Guard(SaveSettings), () => CanEdit);
        RefreshMicrophonesCommand = new(_ => Guard(RefreshMicrophones), () => CanEdit);
        CopyFinalCommand = new(_ => Guard(() => { _paster.Copy(FinalText); Message = "Final text copied."; }), () => FinalText.Length > 0);
        CopyHistoryCommand = new(p => Guard(() => { if (p is HistoryEntry entry) { _paster.Copy(entry.FinalText); Message = "History text copied."; } }));
        DeleteHistoryCommand = new(p => Guard(() => { if (p is HistoryEntry entry) { _history.Delete(entry.Id); History.Remove(entry); } }), () => CanEdit);
        ClearHistoryCommand = new(_ => Guard(() => { _history.Clear(); History.Clear(); RawTranscript = ""; FinalText = ""; Status = "Idle"; Refresh(); Message = "All saved history and the current result have been deleted."; }), () => CanEdit);
        _hotkey.Pressed += OnPressed; _hotkey.Released += OnReleased; _audio.Failed += OnAudioFailed; _audio.SilenceDetected += OnSilenceDetected; _limit.Tick += OnLimit;
    }
    public void Initialize()
    {
        Guard(() =>
        {
            _saved = _store.Load();
            if (!Hotkeys.Contains(_saved.Hotkey)) _saved.Hotkey = Hotkeys[0];
            MicrophoneId = _saved.MicrophoneId; SelectedHotkey = _saved.Hotkey;
            CleanupEnabled = _saved.CleanupEnabled; SaveHistory = _saved.SaveHistory;
            RecordingSoundEnabled = _saved.RecordingSoundEnabled;
            MutePlaybackWhileRecording = _saved.MutePlaybackWhileRecording;
            AutoStopOnSilence = _saved.AutoStopOnSilence;
            SpokenCommandsEnabled = _saved.SpokenCommandsEnabled;
            TranscriptionMode = _saved.TranscriptionMode; ModelPath = _saved.ModelPath;
            if (_transcriber is IConfigurableTranscriptionService configurable) configurable.Configure(_saved);
            Notify(nameof(ProviderDescription));
            Notify(nameof(ActiveHotkey)); Notify(nameof(IsTestMode)); Notify(nameof(HistoryNote)); SettingsChanged();
        });
        // Verify once at launch so a hotkey press does not hash the model on the UI thread.
        Guard(() => { if (_transcriber is IConfigurableTranscriptionService service) service.Validate(); });
        Guard(LoadDictionary);
        Guard(LoadSnippets);
        Guard(RefreshMicrophones);
        Guard(() => { foreach (var entry in _history.Load()) History.Add(entry); });
        Guard(() => _hotkey.Configure(_saved.Hotkey));
    }
    private void RefreshMicrophones()
    {
        var selected = MicrophoneId;
        Microphones.Clear();
        foreach (var microphone in _audio.GetMicrophones()) Microphones.Add(microphone);
        MicrophoneId = Microphones.Any(x => x.Id == selected) ? selected : -1;
    }
    private void SaveSettings()
    {
        var settings = new AppSettings { MicrophoneId = MicrophoneId, Hotkey = SelectedHotkey,
            CleanupEnabled = CleanupEnabled, SaveHistory = SaveHistory, TranscriptionMode = TranscriptionMode, ModelPath = ModelPath.Trim(), RecordingSoundEnabled = RecordingSoundEnabled,
            MutePlaybackWhileRecording = MutePlaybackWhileRecording, AutoStopOnSilence = AutoStopOnSilence, SpokenCommandsEnabled = SpokenCommandsEnabled };
        if (!TranscriptionModes.Contains(settings.TranscriptionMode)) throw new InvalidOperationException("Choose a transcription mode.");
        if (settings.TranscriptionMode == "Local Whisper")
        {
            using var validation = new WhisperTranscriptionService(); validation.Configure(settings); validation.Validate();
        }
        _store.Save(settings); _saved = settings; _hotkey.Configure(settings.Hotkey);
        if (_transcriber is IConfigurableTranscriptionService configurable) configurable.Configure(settings);
        Notify(nameof(ProviderDescription));
        Notify(nameof(ActiveHotkey)); Notify(nameof(IsTestMode)); Notify(nameof(HistoryNote)); SettingsChanged();
        Status = "Idle"; Message = "Settings saved. Hold " + settings.Hotkey + " in your target app.";
    }
    private void OnPressed() { if (CanEdit) Start(true); }
    private async void OnReleased() { if (_recording && _heldRecording) await StopAsync(); }
    private async void OnLimit(object? sender, EventArgs e) { if (_recording) await StopAsync(); }
    private void OnSilenceDetected() => _dispatcher.BeginInvoke(new Action(async () =>
    {
        if (!_recording || !_saved.AutoStopOnSilence) return;
        Message = "Silence detected. Finishing dictation...";
        await StopAsync();
    }));
    private void OnAudioFailed(Exception error) => _dispatcher.BeginInvoke(new Action(async () =>
    {
        if (!_recording) return;
        await StopAsync();
        Error("Microphone capture failed. Check Windows microphone permissions and device connection. " + error.Message);
    }));
    public async void Toggle()
    {
        if (_recording) { await StopAsync(); return; }
        if (!CanEdit) return;
        _countdown = true; Refresh();
        try
        {
            for (var seconds = 3; seconds > 0; seconds--)
            {
                Status = "Idle"; Message = $"Recording starts in {seconds}… focus the destination text field.";
                await Task.Delay(1000, _lifetime.Token);
            }
            Start(false);
        }
        catch (OperationCanceledException) { }
        finally { _countdown = false; Refresh(); }
    }
    private void Start(bool held)
    {
        if (_transcriber is IConfigurableTranscriptionService configurable)
        {
            try { configurable.Validate(); }
            catch (Exception ex) { Error(ex.Message); return; }
        }
        try
        {
            _target = _paster.CaptureTarget(); _audio.Start(_saved.MicrophoneId, _saved.AutoStopOnSilence);
            _heldRecording = held; _recording = true; RawTranscript = ""; FinalText = ""; Status = "Recording";
            Message = _saved.AutoStopOnSilence
                ? "Microphone is on. Jolti stops after 1.5 seconds of silence; you can also stop manually."
                : held ? "Microphone is on. Release the hotkey to stop." : "Microphone is on. Use Stop dictation in the tray. Maximum duration: 2 minutes.";
            _limit.Start();
            if (_saved.RecordingSoundEnabled && _sound != null) PlayStartSound();
            else MutePlayback();
        }
        catch (Exception ex) { Error("Could not start microphone. Check Windows Settings → Privacy & security → Microphone. " + ex.Message); }
        Refresh();
    }
    private async Task StopAsync()
    {
        if (!_recording) return;
        _soundPlayback?.Cancel();
        _recording = false; _busy = true; _limit.Stop();

        _processing = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _processing.CancelAfter(TimeSpan.FromMinutes(5));
        Refresh();
        byte[]? audio = null;
        try
        {
            Status = "Transcribing"; Message = _saved.TranscriptionMode == "Local Whisper" ? "Transcribing on your device with Whisper..." : "Processing with the fake test provider...";
            try { audio = await _audio.StopAsync(); }
            finally { RestorePlayback(); }
            // Play the release cue only after capture ends so it cannot enter this recording.
            if (_saved.RecordingSoundEnabled && _sound != null) PlayEndSound();
            RawTranscript = await _transcriber.TranscribeAsync(audio, _processing.Token);
            _processing.Token.ThrowIfCancellationRequested();
            FinalText = _saved.CleanupEnabled ? _cleanup.Clean(RawTranscript) : RawTranscript;
            if (_saved.SpokenCommandsEnabled) FinalText = SpokenCommands.Apply(FinalText);
            FinalText = DictionaryCorrections.Apply(FinalText, DictionaryEntries);
            FinalText = SnippetExpansion.Apply(FinalText, Snippets);
            if (string.IsNullOrWhiteSpace(FinalText)) throw new InvalidOperationException("No text was returned.");
            string? historyError = null;
            if (_saved.SaveHistory)
            {
                try { var entry = new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, RawTranscript, FinalText); _history.Add(entry); History.Insert(0, entry); }
                catch (Exception ex) { historyError = "History could not be saved: " + ex.Message; }
            }
            await _paster.PasteAsync(FinalText, _target, _processing.Token);
            Status = "Text inserted";
            Message = "Text input sent. Your clipboard is unchanged. Destination acceptance cannot be verified.";
            if (historyError != null) Error("Text input sent. " + historyError);
        }
        catch (OperationCanceledException) { Status = "Idle"; Message = "Transcription canceled or timed out. No text input was sent."; }
        catch (Exception ex) { Error(ex.Message); }
        finally { if (audio != null) Array.Clear(audio); _processing?.Dispose(); _processing = null; _busy = false; Refresh(); }
    }
    private void Refresh()
    {
        DownloadModelCommand.Refresh();
        SaveSnippetCommand.Refresh(); DeleteSnippetCommand.Refresh(); NewSnippetCommand.Refresh();
        SaveWordCommand.Refresh(); DeleteWordCommand.Refresh(); NewWordCommand.Refresh();
        Notify(nameof(CanEdit)); Notify(nameof(RecordButtonText));
        Notify(nameof(IsProcessing));
        ToggleCommand.Refresh(); SaveSettingsCommand.Refresh(); RefreshMicrophonesCommand.Refresh();
        CopyFinalCommand.Refresh(); DeleteHistoryCommand.Refresh(); ClearHistoryCommand.Refresh();
        CancelCommand.Refresh();
    }
    private void Guard(Action action) { try { action(); } catch (Exception ex) { Error(ex.Message); } }
    private async void PlayStartSound()
    {
        var playback = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _soundPlayback = playback;
        try { await _sound!.PlayStartAsync(playback.Token); }
        catch (OperationCanceledException) { }
        catch (Exception) { if (_recording && _soundPlayback == playback) Message += " Start sound unavailable; recording continues."; }
        finally
        {
            if (_soundPlayback == playback)
            {
                _soundPlayback = null;
                if (_recording) MutePlayback();
            }
            playback.Dispose();
        }
    }
    private async void PlayEndSound()
    {
        var playback = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _soundPlayback = playback;
        try { await _sound!.PlayEndAsync(playback.Token); }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        finally { if (_soundPlayback == playback) _soundPlayback = null; playback.Dispose(); }
    }
    private void Error(string message) { Status = "Error"; Message = message; }
    private void MutePlayback()
    {
        if (!_saved.MutePlaybackWhileRecording || _playbackMuter == null) return;
        try { _playbackMuter.Mute(); }
        catch (Exception ex) { Message += " Could not mute playback: " + ex.Message; }
    }
    private void RestorePlayback()
    {
        try { _playbackMuter?.Restore(); }
        catch (Exception ex) { Message += " Could not restore playback: " + ex.Message; }
    }
    public void Dispose()
    {
        _lifetime.Cancel(); _limit.Stop();
        RestorePlayback();
        _hotkey.Pressed -= OnPressed; _hotkey.Released -= OnReleased; _audio.Failed -= OnAudioFailed; _audio.SilenceDetected -= OnSilenceDetected;
        _hotkey.Dispose(); _audio.Dispose();
    }
}
