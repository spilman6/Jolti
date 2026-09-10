using Jolti.Infrastructure;

namespace Jolti.ViewModels;

public sealed partial class MainViewModel
{
    private CancellationTokenSource? _modelDownload;
    private string _downloadStatus = "";
    private double _downloadProgress;
    public IReadOnlyList<WhisperModel> AvailableModels => WhisperModels.All;
    public WhisperModel? SelectedModel
    {
        get => AvailableModels.FirstOrDefault(m => string.Equals(m.Path, ModelPath, StringComparison.OrdinalIgnoreCase));
        set { if (value != null) ModelPath = value.Path; }
    }
    public string DownloadStatus
    {
        get => _downloadStatus.Length > 0 ? _downloadStatus : SelectedModel == null
            ? "Choose a Whisper model."
            : !IsModelInstalled ? "Download this model to use it on your device."
            : ModelPath == _saved.ModelPath && _saved.TranscriptionMode == "Local Whisper"
                ? "Selected for speech recognition. Model files are on this device."
                : "Model files are on this device. Save settings to use this model.";
        private set => Set(ref _downloadStatus, value);
    }
    public bool IsModelInstalled => SelectedModel is { } model && File.Exists(model.Path);
    public bool IsDownloading => _modelDownload != null;
    public bool ShowDownloadButton => !IsDownloading && !IsModelInstalled && SelectedModel != null;
    public double DownloadProgress { get => _downloadProgress; private set => Set(ref _downloadProgress, value); }
    public bool CanChooseModel => _modelDownload == null;
    public RelayCommand DownloadModelCommand { get; private set; } = null!;
    public RelayCommand CancelModelDownloadCommand { get; private set; } = null!;

    private void RefreshModelState(bool clearStatus = false)
    {
        if (clearStatus && !IsDownloading) _downloadStatus = "";
        Notify(nameof(DownloadStatus)); Notify(nameof(IsModelInstalled));
        Notify(nameof(IsDownloading)); Notify(nameof(ShowDownloadButton)); Notify(nameof(CanChooseModel));
        DownloadModelCommand?.Refresh(); CancelModelDownloadCommand?.Refresh();
    }

    private void InitializeModels()
    {
        DownloadModelCommand = new(async _ => await DownloadModelAsync(), () => CanEdit && ShowDownloadButton);
        CancelModelDownloadCommand = new(_ => _modelDownload?.Cancel(), () => _modelDownload != null);
    }

    private async Task DownloadModelAsync()
    {
        if (!CanEdit || !CanChooseModel || SelectedModel is not { } model) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _modelDownload = cancellation;
        RefreshModelState();
        Notify(nameof(CanChooseModel));
        DownloadModelCommand.Refresh(); CancelModelDownloadCommand.Refresh();
        DownloadProgress = 0;
        DownloadStatus = "Downloading " + model.Name + "...";
        try
        {
            var progress = new Progress<double>(value => DownloadProgress = value);
            await Task.Run(() => WhisperModels.DownloadAsync(model, progress, cancellation.Token));
            DownloadStatus = "";
        }
        catch (OperationCanceledException) { DownloadStatus = "Download canceled. You can retry when ready."; }
        catch (Exception ex) { DownloadStatus = "Download failed: " + ex.Message; }
        finally
        {
            _modelDownload = null;
            RefreshModelState();
            Notify(nameof(CanChooseModel));
            DownloadModelCommand.Refresh(); CancelModelDownloadCommand.Refresh();
        }
    }
}
