using System.Net.Http;
using System.Security.Cryptography;

namespace Jolti.Infrastructure;

public sealed record WhisperModel(string Name, string FileName, long Bytes, string Sha256)
{
    public string Path => System.IO.Path.Combine(VerifiedModel.DirectoryPath, FileName);
}

public static class WhisperModels
{
    public static IReadOnlyList<WhisperModel> All { get; } = Array.AsReadOnly(new[] {
        new WhisperModel("Base English — 148 MB", "ggml-base.en.bin", 147964211, VerifiedModel.BaseEnglishSha256),
        new WhisperModel("Small English — 488 MB", "ggml-small.en.bin", 487614201, "c6138d6d58ecc8322097e0f987c32f1be8bb0a18532a3f88f734d1bbf9c41e5d"),
        new WhisperModel("Medium English — 1.53 GB", "ggml-medium.en.bin", 1533774781, "cc37e93478338ec7700281a7ac30a10128929eb8f427dda2e865faa8f6da4356")
    });

    public static async Task DownloadAsync(WhisperModel model, IProgress<double> progress, CancellationToken token)
    {
        if (!All.Contains(model)) throw new InvalidOperationException("Unsupported model.");
        Directory.CreateDirectory(VerifiedModel.DirectoryPath);
        for (var dir = new DirectoryInfo(VerifiedModel.DirectoryPath); dir != null; dir = dir.Parent)
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The model directory cannot use symbolic links or junctions.");
        if (File.Exists(model.Path))
        {
            try
            {
                using var existing = new VerifiedModel().Open(model.Path);
                progress.Report(100);
                return;
            }
            catch (InvalidDataException) { /* Replace a corrupt copy only after the download verifies. */ }
        }
        var partial = model.Path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromHours(2) };
            using var response = await client.GetAsync("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + model.FileName, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true))
            {
                await using var input = await response.Content.ReadAsStreamAsync(token);
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    total += read;
                    if (total > model.Bytes) throw new InvalidDataException("Unexpected model size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    progress.Report(total * 100.0 / model.Bytes);
                }
                output.Position = 0;
                var digest = Convert.ToHexString(await SHA256.HashDataAsync(output, token));
                if (total != model.Bytes || !digest.Equals(model.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Model verification failed. Please retry the download.");
            }
            token.ThrowIfCancellationRequested();
            File.Move(partial, model.Path, overwrite: true);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
}
