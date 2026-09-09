using System.Collections.ObjectModel;
using Jolti.Core;

namespace Jolti.ViewModels;

public sealed partial class MainViewModel
{
    private IDictionaryRepository? _dictionaryStore;
    private DictionaryEntry? _selectedWord;
    private string _heardWord = "", _preferredSpelling = "";
    public ObservableCollection<DictionaryEntry> DictionaryEntries { get; } = [];
    public string HeardWord { get => _heardWord; set => Set(ref _heardWord, value); }
    public string PreferredSpelling { get => _preferredSpelling; set => Set(ref _preferredSpelling, value); }
    public DictionaryEntry? SelectedWord
    {
        get => _selectedWord;
        set
        {
            Set(ref _selectedWord, value);
            HeardWord = value?.Heard ?? "";
            PreferredSpelling = value?.Spelling ?? "";
            DeleteWordCommand?.Refresh();
        }
    }
    public RelayCommand SaveWordCommand { get; private set; } = null!;
    public RelayCommand DeleteWordCommand { get; private set; } = null!;
    public RelayCommand NewWordCommand { get; private set; } = null!;
    private void InitializeDictionary(IDictionaryRepository? repository)
    {
        _dictionaryStore = repository;
        SaveWordCommand = new(_ => Guard(SaveWord), () => CanEdit);
        NewWordCommand = new(_ => { SelectedWord = null; HeardWord = ""; PreferredSpelling = ""; }, () => CanEdit);
        DeleteWordCommand = new(_ => Guard(() =>
        {
            if (!CanEdit || SelectedWord == null) return;
            PersistDictionary(DictionaryEntries.Where(x => x.Id != SelectedWord.Id).ToList());
            Message = "Dictionary entry deleted.";
        }), () => CanEdit && SelectedWord != null);
    }
    private static void ValidateWord(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 120 || value.Any(char.IsControl))
            throw new InvalidDataException("Enter a word or phrase of 1–120 characters on one line.");
    }
    private void LoadDictionary()
    {
        var entries = _dictionaryStore?.LoadDictionary() ?? [];
        if (entries.Count > 500) throw new InvalidDataException("The dictionary supports up to 500 entries.");
        foreach (var entry in entries) { ValidateWord(entry.Heard); ValidateWord(entry.Spelling); }
        if (entries.Select(x => x.Heard).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
            throw new InvalidDataException("Dictionary contains duplicate heard words.");
        foreach (var entry in entries) DictionaryEntries.Add(entry);
    }
    private void SaveWord()
    {
        if (!CanEdit) return;
        var spelling = PreferredSpelling.Trim();
        var heard = string.IsNullOrWhiteSpace(HeardWord) ? spelling : HeardWord.Trim();
        ValidateWord(heard); ValidateWord(spelling);
        var id = SelectedWord?.Id ?? Guid.NewGuid();
        if (DictionaryEntries.Any(x => x.Id != id && string.Equals(x.Heard, heard, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("That heard word already exists. Select its entry to edit it.");
        var entries = DictionaryEntries.Where(x => x.Id != id).Append(new DictionaryEntry(id, heard, spelling)).ToList();
        if (entries.Count > 500) throw new InvalidOperationException("The dictionary supports up to 500 entries.");
        PersistDictionary(entries);
        Message = "Dictionary saved. Your correction applies to the next dictation.";
    }
    private void PersistDictionary(IReadOnlyList<DictionaryEntry> entries)
    {
        if (_dictionaryStore == null) throw new InvalidOperationException("Dictionary storage is unavailable.");
        _dictionaryStore.SaveDictionary(entries); // Update the UI only after the atomic save succeeds.
        SelectedWord = null;
        DictionaryEntries.Clear();
        foreach (var entry in entries.OrderBy(x => x.Spelling, StringComparer.OrdinalIgnoreCase)) DictionaryEntries.Add(entry);
        Status = "Idle";
    }
}
