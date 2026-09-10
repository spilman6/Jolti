namespace Jolti.Core;

public sealed record SnippetEntry(Guid Id, string Trigger, string Expansion);

public interface ISnippetRepository
{
    IReadOnlyList<SnippetEntry> LoadSnippets();
    void SaveSnippets(IReadOnlyList<SnippetEntry> entries);
}
