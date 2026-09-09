using System.Text.RegularExpressions;
using Jolti.Core;

namespace Jolti.Services;

/// <summary>Deliberately does not recognize speech. Replace this DI registration with local Whisper.</summary>
public sealed class FakeTranscriptionService : ITranscriptionService
{
    public async Task<string> TranscribeAsync(byte[] wavAudio, CancellationToken cancellationToken)
    {
        if (wavAudio.Length <= 44) throw new InvalidOperationException("No audio captured. Hold the hotkey longer and check your microphone.");
        await Task.Delay(600, cancellationToken);
        return "um this is a jolti test transcript";
    }
}

public sealed class TextCleanupService : ITextCleanupService
{
    public string Clean(string text)
    {
        var cleaned = Regex.Replace(text, @"\b(um|uh|erm)\b[,\s]*", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        cleaned = Regex.Replace(cleaned, @"\s+([,.!?;:])", "$1");
        cleaned = Regex.Replace(cleaned, @"\bi\b", "I");
        cleaned = Regex.Replace(cleaned, @"(^|[.!?]\s+)(\p{L})", m => m.Groups[1].Value + m.Groups[2].Value.ToUpperInvariant());
        if (cleaned.Length > 0 && !".!?".Contains(cleaned[^1])) cleaned += ".";
        return cleaned;
    }
}
