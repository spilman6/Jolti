using Jolti.Core;

namespace Jolti.Services;

public static class SnippetExpansion
{
    public static void Validate(IReadOnlyList<SnippetEntry> entries)
    {
        if (entries.Count > 500) throw new InvalidDataException("Snippets support up to 500 entries.");
        foreach (var entry in entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Trigger) || entry.Trigger.Length > 120 ||
                entry.Trigger != entry.Trigger.Trim() || entry.Trigger.Any(char.IsControl) ||
                !entry.Trigger.Any(char.IsLetterOrDigit))
                throw new InvalidDataException("Enter a trigger of 1-120 characters on one line, including a letter or number.");
            if (string.IsNullOrWhiteSpace(entry.Expansion) || entry.Expansion.Length > 4000 ||
                entry.Expansion.Any(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t'))
                throw new InvalidDataException("Enter text of 1-4,000 characters. Line breaks and tabs are supported.");
        }
        if (entries.Select(x => x.Id).Distinct().Count() != entries.Count ||
            entries.Select(x => x.Trigger).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
            throw new InvalidDataException("That trigger already exists. Select its snippet to edit it.");
    }

    public static string Apply(string text, IEnumerable<SnippetEntry> entries)
    {
        var ordered = entries.OrderByDescending(x => x.Trigger.Length).ToArray();
        var trimmed = text.Trim();
        foreach (var entry in ordered)
        {
            if (trimmed.Equals(entry.Trigger, StringComparison.OrdinalIgnoreCase)) return entry.Expansion;
            if (trimmed.StartsWith(entry.Trigger, StringComparison.OrdinalIgnoreCase) &&
                trimmed[entry.Trigger.Length..] is { Length: > 0 } suffix &&
                suffix.All(c => c is '.' or '!' or '?')) return entry.Expansion;
        }
        // Reuse literal, longest-first, Unicode whole-word matching; expansion is a single pass.
        return DictionaryCorrections.Apply(text, ordered.Select(x => new DictionaryEntry(x.Id, x.Trigger, x.Expansion)));
    }
}
