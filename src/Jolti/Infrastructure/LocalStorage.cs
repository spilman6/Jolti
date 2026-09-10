using System.Text.Json;
using Jolti.Core;

namespace Jolti.Infrastructure;

public sealed class LocalStorage : ISettingsStore, IHistoryRepository, IDictionaryRepository, ISnippetRepository
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jolti");
    private readonly string _directory;
    public LocalStorage() : this(DataDirectory) { }
    public LocalStorage(string directory) { _directory = directory; }
    private string SettingsPath => Path.Combine(_directory, "settings.json");
    private string HistoryPath => Path.Combine(_directory, "history.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static T Read<T>(string path, T fallback) => File.Exists(path)
        ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? throw new InvalidDataException("Local data is invalid: " + Path.GetFileName(path))
        : fallback;

    private void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(_directory);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public IReadOnlyList<DictionaryEntry> LoadDictionary() => Read(Path.Combine(_directory, "dictionary.json"), new List<DictionaryEntry>());
    public void SaveDictionary(IReadOnlyList<DictionaryEntry> entries) => Write(Path.Combine(_directory, "dictionary.json"), entries);
    public IReadOnlyList<SnippetEntry> LoadSnippets() => Read(Path.Combine(_directory, "snippets.json"), new List<SnippetEntry>());
    public void SaveSnippets(IReadOnlyList<SnippetEntry> entries) => Write(Path.Combine(_directory, "snippets.json"), entries);
    public AppSettings Load() => Read(SettingsPath, new AppSettings());
    public void Save(AppSettings settings) => Write(SettingsPath, settings);
    IReadOnlyList<HistoryEntry> IHistoryRepository.Load() => Read(HistoryPath, new List<HistoryEntry>());
    public void Add(HistoryEntry entry)
    {
        var entries = Read(HistoryPath, new List<HistoryEntry>());
        entries.Insert(0, entry);
        Write(HistoryPath, entries);
    }
    public void Delete(Guid id) => Write(HistoryPath, Read(HistoryPath, new List<HistoryEntry>()).Where(x => x.Id != id).ToList());
    public void Clear()
    {
        // Delete without deserializing, so damaged history can also be removed.
        foreach (var path in new[] { HistoryPath, HistoryPath + ".tmp" })
            if (File.Exists(path)) File.Delete(path);
    }
}
