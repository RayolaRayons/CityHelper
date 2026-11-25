using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using CityHelper.Services;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.Tools.PlayerNotes.ViewModels;

/// <summary>One editable line of the grid.</summary>
public sealed partial class PlayerNoteRowViewModel : ObservableObject
{
    private readonly Action _changed;
    private readonly Action<PlayerNoteRowViewModel> _remove;

    public PlayerNoteRowViewModel(PlayerNote note, Action changed, Action<PlayerNoteRowViewModel> remove)
    {
        _name = note.Name;
        _stars = Math.Clamp(note.Stars, 0, 5);
        _characters = note.Characters;
        _notes = note.Notes;
        _changed = changed;
        _remove = remove;
    }

    [ObservableProperty] private string _name;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Star1On), nameof(Star2On), nameof(Star3On), nameof(Star4On), nameof(Star5On))]
    private int _stars;

    [ObservableProperty] private string _characters;
    [ObservableProperty] private string _notes;

    partial void OnNameChanged(string value)
    {
        // Global names are written without the @ (typed or pasted as "@name")
        if (value != null && value.TrimStart().StartsWith('@'))
        {
            Name = value.TrimStart().TrimStart('@');
            return;                                     // the change to the cleaned name calls this again
        }
        _changed();
    }
    partial void OnStarsChanged(int value) => _changed();
    partial void OnCharactersChanged(string value) => _changed();
    partial void OnNotesChanged(string value) => _changed();

    // Which of the five stars are lit
    public bool Star1On => Stars >= 1;
    public bool Star2On => Stars >= 2;
    public bool Star3On => Stars >= 3;
    public bool Star4On => Stars >= 4;
    public bool Star5On => Stars >= 5;

    /// <summary>Clicking star n rates n stars; clicking the star that is already the rating clears it.</summary>
    [RelayCommand]
    private void SetStars(string? star)
    {
        if (!int.TryParse(star, out var n))
            return;
        Stars = Stars == n ? 0 : Math.Clamp(n, 0, 5);
    }

    public PlayerNote ToNote() =>
        new(Name ?? "", Math.Clamp(Stars, 0, 5), Characters ?? "", Notes ?? "");

    /// <summary>True if the filter is part of the global name, of any one of the comma separated character names, or of the notes.</summary>
    public bool Matches(string filter) =>
        (Name ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        (Characters ?? "").Split(',').Any(c => c.Trim().Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
        (Notes ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void Remove() => _remove(this);
}

/// <summary>Edits accounts\&lt;account&gt;\playernotes.txt of the selected installation.</summary>
public sealed partial class PlayerNotesViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly List<PlayerNoteRowViewModel> _all = new();
    private List<string> _unparsed = new();
    private string? _path;          // the file the rows were loaded from (so pending edits go to the right place)

    public PlayerNotesViewModel(AppState state)
    {
        _state = state;
        _saveTimer.Tick += (_, _) => OnSaveTimer();
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.SelectedInstallation) or nameof(AppState.SelectedAccount))
                Load();
        };
        Load();
    }

    /// <summary>The rows currently shown (all of them, or those matching the filter).</summary>
    public ObservableCollection<PlayerNoteRowViewModel> Rows { get; } = new();

    [ObservableProperty]
    private string _filter = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddRowCommand))]
    private bool _canEdit;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    partial void OnFilterChanged(string value) => ApplyFilter();

    // --- Sorting (display only; the file keeps its own order) ---

    private enum SortColumn { None, Name, Stars }

    private SortColumn _sortColumn = SortColumn.None;
    private bool _sortDescending;

    public string NameHeader => "Global Name" + SortArrow(SortColumn.Name);
    public string StarsHeader => "Stars" + SortArrow(SortColumn.Stars);

    private string SortArrow(SortColumn column) =>
        _sortColumn != column ? "" : _sortDescending ? "  ▼" : "  ▲";

    [RelayCommand]
    private void SortByName() => SortBy(SortColumn.Name, descendingFirst: false);

    [RelayCommand]
    private void SortByStars() => SortBy(SortColumn.Stars, descendingFirst: true);

    /// <summary>A second click on the same header reverses the order.</summary>
    private void SortBy(SortColumn column, bool descendingFirst)
    {
        if (_sortColumn == column)
            _sortDescending = !_sortDescending;
        else
        {
            _sortColumn = column;
            _sortDescending = descendingFirst;
        }
        OnPropertyChanged(nameof(NameHeader));
        OnPropertyChanged(nameof(StarsHeader));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var f = Filter.Trim();
        IEnumerable<PlayerNoteRowViewModel> rows = f.Length == 0 ? _all : _all.Where(r => r.Matches(f));

        // Edits do not re-sort the list (a row would jump away while it is being edited); sorting happens here
        rows = _sortColumn switch
        {
            SortColumn.Name => Order(rows, r => r.Name ?? "", null),
            SortColumn.Stars => Order(rows, r => r.Stars, r => r.Name ?? ""),
            _ => rows,
        };

        Rows.Clear();
        foreach (var r in rows)
            Rows.Add(r);
    }

    private IEnumerable<PlayerNoteRowViewModel> Order<TKey>(IEnumerable<PlayerNoteRowViewModel> rows,
        Func<PlayerNoteRowViewModel, TKey> key, Func<PlayerNoteRowViewModel, string>? then)
    {
        IEnumerable<PlayerNoteRowViewModel> ordered = typeof(TKey) == typeof(string)
            ? (_sortDescending
                ? rows.OrderByDescending(r => (string)(object)key(r)!, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(r => (string)(object)key(r)!, StringComparer.OrdinalIgnoreCase))
            : (_sortDescending ? rows.OrderByDescending(key) : rows.OrderBy(key));
        // Ties (the same stars) fall back to the name, ascending
        return then is null ? ordered : ((IOrderedEnumerable<PlayerNoteRowViewModel>)ordered).ThenBy(then, StringComparer.OrdinalIgnoreCase);
    }

    // --- Automatic saving ---

    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };

    /// <summary>Every edit is written to the file shortly after the last keystroke / click.</summary>
    private void MarkDirty()
    {
        IsDirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnSaveTimer()
    {
        _saveTimer.Stop();
        if (IsDirty && _path != null)
            TrySave();
    }

    private PlayerNoteRowViewModel NewRow(PlayerNote note) =>
        new(note, MarkDirty, row =>
        {
            _all.Remove(row);
            Rows.Remove(row);
            MarkDirty();
            UpdateStatus();
        });

    /// <summary>Re-reads the file. Unsaved changes are discarded when called as the Reload command.</summary>
    [RelayCommand]
    public void Reload()
    {
        _saveTimer.Stop();
        IsDirty = false;
        Load();
    }

    private void Load()
    {
        // Edits belong to the file they were made in, so write them before moving to another
        _saveTimer.Stop();
        if (IsDirty && _path != null)
            TrySave();

        _all.Clear();
        _unparsed = new List<string>();
        Rows.Clear();
        IsDirty = false;
        _path = null;
        CanEdit = false;

        if (_state.SelectedInstallation is not { } inst || _state.SelectedAccount is not { } account)
        {
            StatusText = "Select an installation and account.";
            return;
        }

        _path = Path.Combine(inst.Path, "accounts", account.Name, "playernotes.txt");
        CanEdit = true;
        if (!File.Exists(_path))
        {
            StatusText = $"No playernotes.txt for {account.Name} yet; it is created when you save.";
            return;
        }

        try
        {
            var file = PlayerNotesFile.Load(_path);
            foreach (var n in file.Notes)
                _all.Add(NewRow(n));
            _unparsed = file.Unparsed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Could not read playernotes.txt: " + ex.Message;
            CanEdit = false;
            return;
        }

        ApplyFilter();
        UpdateStatus();
    }

    private void UpdateStatus() =>
        StatusText = $"{_all.Count} players" + (_unparsed.Count > 0 ? $"  ({_unparsed.Count} unreadable lines are kept as they are)" : "");

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddRow()
    {
        Filter = "";                                    // so the new line is visible
        var row = NewRow(new PlayerNote("", 0, "", ""));
        _all.Insert(0, row);
        Rows.Insert(0, row);
        MarkDirty();
        UpdateStatus();
    }

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Save() => TrySave();

    private bool TrySave()
    {
        if (_path is null)
            return false;

        var file = new PlayerNotesFile();
        file.Notes.AddRange(_all.Where(r => (r.Name ?? "").Trim().Length > 0).Select(r => r.ToNote()));
        file.Unparsed.AddRange(_unparsed);
        try
        {
            file.Save(_path);
            IsDirty = false;
            StatusText = "Saved (original kept in the backup folder).  " + file.Notes.Count + " players.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Could not save playernotes.txt: " + ex.Message;
            return false;
        }
    }
}
