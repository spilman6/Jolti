using System.Security.Cryptography;

namespace Jolti.Infrastructure;

/// <summary>Trust comes from a pinned digest, never a user-writable checksum sidecar.</summary>
public sealed class VerifiedModel
{
    public const string BaseEnglishSha256 = "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002";
    public static string DirectoryPath => Path.Combine(LocalStorage.DataDirectory, "models");
    private readonly string _root;
    public VerifiedModel() : this(DirectoryPath) { }
    // Allows isolated filesystem tests; production always uses the parameterless constructor.
    public VerifiedModel(string directory) => _root = Path.GetFullPath(directory);

    public FileStream Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Select the verified model in Jolti's application data models folder.");
        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(fullPath), _root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Models must be installed in " + _root + ". Run scripts/Install-WhisperModel.ps1 with a pre-downloaded model, then select the installed file.");
        if (!File.Exists(fullPath)) throw new InvalidOperationException("Whisper model not found. Open Settings, choose a model, and click Download selected model.");
        // Reject junctions and symbolic links rather than allowing paths to escape AppData.
        for (var directory = new DirectoryInfo(_root); directory != null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("The model directory cannot use symbolic links or junctions.");
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The model cannot be a symbolic link.");

        // Keep a read-only sharing lease while native loading/inference uses the model,
        // preventing ordinary writes or replacement between verification and loading.
        var file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var digest = Convert.ToHexString(SHA256.HashData(file));
            if (!WhisperModels.All.Any(model => digest.Equals(model.Sha256, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Whisper model SHA-256 verification failed. Choose a verified Base, Small, or Medium English model.");
            file.Position = 0;
            return file;
        }
        catch { file.Dispose(); throw; }
    }
}
