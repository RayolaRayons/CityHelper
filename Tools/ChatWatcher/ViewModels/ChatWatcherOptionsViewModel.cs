using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.Tools.ChatWatcher.ViewModels;

/// <summary>One line of the filter grid (read-only summary; Edit opens the editor).</summary>
public sealed partial class FilterRowViewModel : ObservableObject
{
    private readonly Func<FilterRowViewModel, Task> _edit;

    public FilterRowViewModel(WatchFilter filter, Func<FilterRowViewModel, Task> edit)
    {
        Filter = filter;
        _edit = edit;
    }

    public WatchFilter Filter { get; set; }

    public string Name => Filter.Name;
    public string Keywords => Filter.Keywords;
    public string TellText => Filter.Tell ? "✓" : "";
    public string LocalText => Filter.Local ? "✓" : "";
    public string BroadcastText => Filter.Broadcast ? "✓" : "";
    public string RequestText => Filter.Request ? "✓" : "";

    public void Refresh() => OnPropertyChanged(string.Empty);

    [RelayCommand]
    private Task Edit() => _edit(this);
}

/// <summary>Working copy of the filters and send delays; nothing is written until Save.</summary>
public sealed partial class ChatWatcherOptionsViewModel : OptionsSectionViewModel
{
    private readonly ChatWatcherSettings _settings;

    public ChatWatcherOptionsViewModel(ChatWatcherSettings settings)
    {
        _settings = settings;
        foreach (var f in settings.Filters)
            Rows.Add(new FilterRowViewModel(f.Clone(), EditRowAsync));
        SelectedRow = Rows.FirstOrDefault();

        _switchDelayMs = settings.SwitchDelayMs;
        _pasteDelayMs = settings.PasteDelayMs;
        _sendDelayMs = settings.SendDelayMs;
        _playSound = settings.PlaySound;
    }

    /// <summary>Shows the filter editor; returns true if it was saved. Provided by the view.</summary>
    public Func<FilterEditorViewModel, Task<bool>>? EditorDialog { get; set; }

    public override string Title => "ChatWatcher";

    public ObservableCollection<FilterRowViewModel> Rows { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private FilterRowViewModel? _selectedRow;

    // Delays in milliseconds
    [ObservableProperty] private decimal? _switchDelayMs;
    [ObservableProperty] private decimal? _pasteDelayMs;
    [ObservableProperty] private decimal? _sendDelayMs;
    [ObservableProperty] private bool _playSound;

    private async Task EditRowAsync(FilterRowViewModel row)
    {
        SelectedRow = row;
        if (EditorDialog is null)
            return;

        var others = Rows.Where(r => r != row).Select(r => r.Name).ToList();
        var editor = new FilterEditorViewModel(row.Filter.Clone(), others);
        if (await EditorDialog(editor))
        {
            row.Filter = editor.Result;
            row.Refresh();
        }
    }

    [RelayCommand]
    private async Task Add()
    {
        var names = Rows.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int n = 1;
        while (names.Contains($"New Filter {n}")) n++;

        var row = new FilterRowViewModel(new WatchFilter { Name = $"New Filter {n}", Tell = true, Local = true }, EditRowAsync);
        Rows.Add(row);
        await EditRowAsync(row);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove()
    {
        if (SelectedRow is not { } row)
            return;
        int index = Rows.IndexOf(row);
        Rows.Remove(row);
        SelectedRow = Rows.Count == 0 ? null : Rows[Math.Min(index, Rows.Count - 1)];
    }

    private bool HasSelection => SelectedRow != null;

    public override string? Save()
    {
        _settings.Filters.Clear();
        _settings.Filters.AddRange(Rows.Select(r => r.Filter));
        _settings.SwitchDelayMs = Math.Max(0, (int)(SwitchDelayMs ?? _settings.SwitchDelayMs));
        _settings.PasteDelayMs = Math.Max(0, (int)(PasteDelayMs ?? _settings.PasteDelayMs));
        _settings.SendDelayMs = Math.Max(0, (int)(SendDelayMs ?? _settings.SendDelayMs));
        _settings.PlaySound = PlaySound;
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Could not write chatwatcher.ini:\n" + ex.Message;
        }
        return null;
    }
}

/// <summary>Edits one filter: name, keywords and the four channels.</summary>
public sealed partial class FilterEditorViewModel : ViewModelBase
{
    private readonly HashSet<string> _otherNames;

    public FilterEditorViewModel(WatchFilter filter, IEnumerable<string> otherNames)
    {
        _otherNames = new HashSet<string>(otherNames, StringComparer.OrdinalIgnoreCase);
        _name = filter.Name;
        _keywords = filter.Keywords;
        _tell = filter.Tell;
        _local = filter.Local;
        _broadcast = filter.Broadcast;
        _request = filter.Request;
    }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _keywords;
    [ObservableProperty] private bool _tell;
    [ObservableProperty] private bool _local;
    [ObservableProperty] private bool _broadcast;
    [ObservableProperty] private bool _request;
    [ObservableProperty] private string _errorText = "";

    public WatchFilter Result => new()
    {
        Name = Name.Trim(),
        Keywords = Keywords.Trim(),
        Tell = Tell,
        Local = Local,
        Broadcast = Broadcast,
        Request = Request,
    };

    public event Action<bool>? CloseRequested;

    /// <summary>Back to the built-in values for this filter's name (MSR), or an empty filter for custom ones.</summary>
    [RelayCommand]
    private void ResetToDefaults()
    {
        var d = new WatchFilter { Name = Name.Trim() }.DefaultsFor();
        Name = d.Name;
        Keywords = d.Keywords;
        Tell = d.Tell;
        Local = d.Local;
        Broadcast = d.Broadcast;
        Request = d.Request;
        ErrorText = "";
    }

    [RelayCommand]
    private void Save()
    {
        var name = Name.Trim();
        ErrorText =
            name.Length == 0 ? "Enter a display name." :
            _otherNames.Contains(name) ? "Another filter already uses that name." : "";
        if (ErrorText.Length == 0)
            CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
