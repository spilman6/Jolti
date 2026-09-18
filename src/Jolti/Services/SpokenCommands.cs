using System.Text;
using System.Text.RegularExpressions;

namespace Jolti.Services;

public static partial class SpokenCommands
{
    [GeneratedRegex(@"\b(scratch that|delete that|undo that|new paragraph|new line|bullet point|bullet item|numbered item|end list|question mark|exclamation (?:point|mark)|full stop|semicolon|colon|comma|period)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CommandPattern();

    public static string Apply(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var output = new StringBuilder();
        var numberedItem = 0;
        var position = 0;
        foreach (Match match in CommandPattern().Matches(text))
        {
            AppendWords(output, text[position..match.Index]);
            var command = match.Value.ToLowerInvariant();
            switch (command)
            {
                case "scratch that": case "delete that": case "undo that": Backtrack(output); break;
                case "new line": TrimEnd(output); output.Append('\n'); break;
                case "new paragraph": TrimEnd(output); output.Append("\n\n"); numberedItem = 0; break;
                case "bullet point": case "bullet item": StartItem(output, "• "); numberedItem = 0; break;
                case "numbered item": StartItem(output, $"{++numberedItem}. "); break;
                case "end list": TrimEnd(output); output.Append('\n'); numberedItem = 0; break;
                case "comma": AppendPunctuation(output, ','); break;
                case "period": case "full stop": AppendPunctuation(output, '.'); break;
                case "question mark": AppendPunctuation(output, '?'); break;
                case "exclamation point": case "exclamation mark": AppendPunctuation(output, '!'); break;
                case "colon": AppendPunctuation(output, ':'); break;
                case "semicolon": AppendPunctuation(output, ';'); break;
            }
            position = match.Index + match.Length;
        }
        AppendWords(output, text[position..]);
        var result = Regex.Replace(output.ToString().Trim(), @"[ \t]+([,.!?;:])", "$1");
        // Cleanup may have supplied a final period after a phrase that was itself a command.
        return Regex.Replace(result, @"([,.!?;:])[.!?]+$", "$1");
    }

    private static void AppendWords(StringBuilder output, string words)
    {
        words = words.Trim();
        if (words.Length == 0) return;
        if (output.Length > 0 && output[^1] is not (' ' or '\n' or '•')) output.Append(' ');
        output.Append(words);
    }
    private static void AppendPunctuation(StringBuilder output, char punctuation)
    {
        TrimEnd(output);
        if (output.Length > 0 && !char.IsPunctuation(output[^1])) output.Append(punctuation);
        output.Append(' ');
    }
    private static void StartItem(StringBuilder output, string marker)
    {
        TrimEnd(output);
        if (output.Length > 0 && output[^1] != '\n') output.Append('\n');
        output.Append(marker);
    }
    private static void Backtrack(StringBuilder output)
    {
        TrimEnd(output);
        if (output.Length == 0) return;
        var index = output.Length - 1;
        if (index >= 0 && ".!?".Contains(output[index])) index--;
        while (index >= 0 && output[index] is not ('.' or '!' or '?' or '\n')) index--;
        output.Length = Math.Max(0, index + 1);
        TrimEnd(output);
    }
    private static void TrimEnd(StringBuilder output)
    {
        while (output.Length > 0 && output[^1] is ' ' or '\t') output.Length--;
    }
}
