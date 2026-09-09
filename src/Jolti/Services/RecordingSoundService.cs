using Jolti.Core;
using NAudio.Wave;
namespace Jolti.Services;

public sealed class RecordingSoundService : IRecordingSoundService
{
    public Task PlayStartAsync(CancellationToken cancellationToken) => PlayAsync("bloop.mp3", cancellationToken);
    public Task PlayEndAsync(CancellationToken cancellationToken) => PlayStartAsync(cancellationToken);

    private static Task PlayAsync(string fileName, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var audio = new AudioFileReader(Path.Combine(AppContext.BaseDirectory, "Assets", fileName));
        using var output = new WaveOutEvent();
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, e) =>
        {
            if (e.Exception != null) finished.TrySetException(e.Exception);
            else finished.TrySetResult(true);
        };
        output.Init(audio);
        output.Play();
        // Playback is independent of capture. It cannot block hotkey release or delay recording.
        try { await finished.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken); }
        finally { output.Stop(); }
    }, cancellationToken);
}
