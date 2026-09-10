using System.Collections.ObjectModel;
using Jolti.Core;
using Jolti.Services;

namespace Jolti.ViewModels;

public sealed partial class MainViewModel
{
    private ISnippetRepository? _snippetStore;
    private SnippetEntry? _selectedSnippet;
    private string _snippetTrigger = "", _snippetText = "";
    public ObservableCollection<SnippetEntry> Snippets { get; } = [];
    public string SnippetTrigger { get => _snippetTrigger; set => Set(ref _snippetTrigger, value); }
    public string SnippetText { get => _snippetText; set => Set(ref _snippetText, value); }
    public SnippetEntry? SelectedSnippet
    {
        get => _selectedSnippet;
        set
        {
            Set(ref _selectedSnippet, value);
            SnippetTrigger = value?.Trigger ?? "";
            SnippetText = value?.Expansion ?? "";
            DeleteSnippetCommand?.Refresh();
        }
    }
    public RelayCommand SaveSnippetCommand { get; private set; } = null!;
    public RelayCommand DeleteSnippetCommand { get; private set; } = null!;
    public RelayCommand NewSnippetCommand { get; private set; } = null!;
    private void InitializeSnippets(ISnippetRepository? repository)
    {
        _snippetStore = repository;
        SaveSnippetCommand = new(_ => Guard(() =>
        {
            if (!CanEdit) return;
            var id = SelectedSnippet?.Id ?? Guid.NewGuid();
            var entries = Snippets.Where(x => x.Id != id)
                .Append(new SnippetEntry(id, SnippetTrigger.Trim(), SnippetText)).ToList();
            SnippetExpansion.Validate(entries);
            PersistSnippets(entries);
            Message = "Snippet saved. Say its trigger in your next dictation.";
        }), () => CanEdit);
        NewSnippetCommand = new(_ => { if (CanEdit) SelectedSnippet = null; }, () => CanEdit);
        DeleteSnippetCommand = new(_ => Guard(() =>
        {
            if (!CanEdit || SelectedSnippet == null) return;
            PersistSnippets(Snippets.Where(x => x.Id != SelectedSnippet.Id).ToList());
            Message = "Snippet deleted.";
        }), () => CanEdit && SelectedSnippet != null);
    }
    private void LoadSnippets()
    {
        var entries = _snippetStore?.LoadSnippets() ?? [];
        SnippetExpansion.Validate(entries);
        Snippets.Clear();
        foreach (var entry in entries.OrderBy(x => x.Trigger, StringComparer.OrdinalIgnoreCase)) Snippets.Add(entry);
    }
    private void PersistSnippets(IReadOnlyList<SnippetEntry> entries)
    {
        if (_snippetStore == null) throw new InvalidOperationException("Snippet storage is unavailable.");
        _snippetStore.SaveSnippets(entries);
        SelectedSnippet = null;
        Snippets.Clear();
        foreach (var entry in entries.OrderBy(x => x.Trigger, StringComparer.OrdinalIgnoreCase)) Snippets.Add(entry);
        Status = "Idle";
    }
}
