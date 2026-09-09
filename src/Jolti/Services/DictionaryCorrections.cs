using System.Text.RegularExpressions;
using Jolti.Core;

namespace Jolti.Services;

public static class DictionaryCorrections
{
    public static string Apply(string text, IEnumerable<DictionaryEntry> entries)
    {
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
            replacements.TryAdd(entry.Heard, entry.Spelling);
        if (replacements.Count == 0) return text;
        // Longest phrases win. One pass prevents replacements from correcting each other.
        // Unicode boundaries avoid replacing a short name inside another word.
        var pattern = @"(?<![\p{L}\p{M}\p{N}_])(?:" +
            string.Join("|", replacements.Keys.OrderByDescending(x => x.Length).Select(Regex.Escape)) +
            @")(?![\p{L}\p{M}\p{N}_])";
        return Regex.Replace(text, pattern, match => replacements[match.Value],
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}
