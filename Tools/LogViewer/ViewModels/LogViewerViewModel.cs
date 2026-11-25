using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls.Primitives;
using CityHelper.Services;
using CityHelper.Tools.LogManager;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.Tools.LogViewer.ViewModels;

/// <summary>How the rows are drawn; replaced as a whole whenever a display option changes.</summary>
public sealed record DisplayOptions(
    bool ShowDates, bool ShowTimes, bool ShowChannels, bool ShowCharacters,
    ColorCodeMode ColorCodes, bool ChannelColors, bool WordWrap, Regex? Highlight,
    IReadOnlySet<LogChannel>? CleanupKeeps = null)
{
    /// <summary>True when cleanup marking is on and Log Manager's cleanup would remove this entry.</summary>
    public bool IsRemovedByCleanup(LogLine line) => CleanupKeeps != null && !CleanupKeeps.Contains(line.CleanupChannel);
}

public sealed partial class FilterItemViewModel : ObservableObject
{
    private readonly Action _changed;

    public FilterItemViewModel(string name, int count, Action changed)
    {
        Name = name;
        Count = count;
        _changed = changed;
        _isChecked = true;
    }

    public string Name { get; }
    public int Count { get; }
    public string Display => $"{Name}  ({Count:N0})";

    [ObservableProperty]
    private bool _isChecked;

    partial void OnIsCheckedChanged(bool value) => _changed();
}

public sealed record LogFileItem(string Name, string Path)
{
    public override string ToString() => Name;
}

/// <summary>
/// Shows one chat log of the selected installation / account with the channel and character filters
/// and the date / time / channel / character toggles.
/// </summary>
public sealed partial class LogViewerViewModel : ViewModelBase, IStatusSource
{
    private readonly AppState _state;
    private LogViewerSettings _settings = LogViewerSettings.Load();
    private ParsedLog? _log;
    private int _version;
    private bool _suspendFilter;
    private bool _loading;

    public LogViewerViewModel(AppState state)
    {
        _state = state;
        _loading = true;
        ShowDates = _settings.ShowDates;
        ShowTimes = _settings.ShowTimes;
        ShowChannels = _settings.ShowChannels;
        ShowCharacters = _settings.ShowCharacters;
        ShowCleanup = _settings.ShowCleanup;
        _loading = false;
        Display = BuildDisplay();

        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.SelectedInstallation) or nameof(AppState.SelectedAccount))
                LoadFolder();
        };
        LoadFolder();
    }

    public ObservableCollection<LogFileItem> Files { get; } = new();
    public ObservableCollection<FilterItemViewModel> ChannelItems { get; } = new();
    public ObservableCollection<FilterItemViewModel> CharacterItems { get; } = new();

    [ObservableProperty] private LogFileItem? _selectedFile;
    [ObservableProperty] private IReadOnlyList<LogLine> _visibleLines = Array.Empty<LogLine>();
    [ObservableProperty] private DisplayOptions _display = null!;
    [ObservableProperty] private ScrollBarVisibility _horizontalScroll = ScrollBarVisibility.Disabled;
    [ObservableProperty] private string _folderText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBarText))]
    private string _statusText = "";

    /// <summary>"12 results" / "Result 3 of 12" for the text in the find box; empty when there is no text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBarText))]
    private string _resultsText = "";

    /// <summary>Shown at the right of the main status bar: the results first, then the line counts.</summary>
    public string StatusBarText => ResultsText.Length == 0 ? StatusText : ResultsText + "   |   " + StatusText;

    [ObservableProperty] private bool _showDates;
    [ObservableProperty] private bool _showTimes = true;
    [ObservableProperty] private bool _showChannels = true;
    [ObservableProperty] private bool _showCharacters = true;

    /// <summary>Marks in red the lines Log Manager's Cleanup Logfile would remove (its channel options decide).</summary>
    [ObservableProperty] private bool _showCleanup;

    public const string ModeHighlight = "Highlight";
    public const string ModeSearch = "Search";
    public IReadOnlyList<string> FindModes { get; } = new[] { ModeHighlight, ModeSearch };

    /// <summary>Highlight marks the text in the lines; Search also steps through the lines that contain it.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextResultCommand), nameof(PreviousResultCommand))]
    private string _findMode = ModeHighlight;

    /// <summary>Text to highlight or search for; several words or phrases can be separated by commas. Not saved.</summary>
    [ObservableProperty] private string _highlightText = "";

    /// <summary>Raised to scroll to (and select) the current search result.</summary>
    public event Action<LogLine>? ResultRequested;

    private List<LogLine> _results = new();
    private int _currentResult = -1;

    partial void OnHighlightTextChanged(string value)
    {
        if (!_loading && Display != null)
            RefreshDisplay();
    }

    partial void OnFindModeChanged(string value)
    {
        _currentResult = -1;
        UpdateResults();
    }

    private bool SearchActive => FindMode == ModeSearch && _results.Count > 0;

    /// <summary>The lines of the current view that contain the find text (the text as it is drawn: character and message).</summary>
    private void UpdateResults()
    {
        var pattern = BuildHighlight();
        _results = pattern is null
            ? new List<LogLine>()
            : VisibleLines.Where(l => pattern.IsMatch(SearchableText(l))).ToList();
        if (_currentResult >= _results.Count)
            _currentResult = -1;
        UpdateResultsText();
        NextResultCommand.NotifyCanExecuteChanged();
        PreviousResultCommand.NotifyCanExecuteChanged();
    }

    private string SearchableText(LogLine line) =>
        (line.ShowCharacterPrefix && line.Character.Length > 0 ? line.CharacterDisplay + ": " : "")
        + LogLineFormatter.MessageText(line, Display?.ColorCodes ?? ColorCodeMode.Hide);

    private void UpdateResultsText()
    {
        if (HighlightText.Trim().Length == 0)
            ResultsText = "";
        else if (_currentResult >= 0)
            ResultsText = $"Result {_currentResult + 1:N0} of {_results.Count:N0}";
        else
            ResultsText = _results.Count == 1 ? "1 result" : $"{_results.Count:N0} results";
    }

    [RelayCommand(CanExecute = nameof(SearchActive))]
    private void NextResult() => StepResult(+1);

    [RelayCommand(CanExecute = nameof(SearchActive))]
    private void PreviousResult() => StepResult(-1);

    private void StepResult(int direction)
    {
        if (_results.Count == 0)
            return;
        _currentResult = _currentResult < 0
            ? (direction > 0 ? 0 : _results.Count - 1)
            : (_currentResult + direction + _results.Count) % _results.Count;
        UpdateResultsText();
        ResultRequested?.Invoke(_results[_currentResult]);
    }

    /// <summary>Case-insensitive match for any of the comma-separated terms; null when the box is empty.</summary>
    private Regex? BuildHighlight()
    {
        var terms = HighlightText.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim()).Where(t => t.Length > 0).Select(Regex.Escape).ToList();
        return terms.Count == 0 ? null : new Regex(string.Join("|", terms), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    partial void OnShowDatesChanged(bool value) => ToggleChanged();
    partial void OnShowTimesChanged(bool value) => ToggleChanged();
    partial void OnShowChannelsChanged(bool value) => ToggleChanged();
    partial void OnShowCharactersChanged(bool value) => ToggleChanged();
    partial void OnShowCleanupChanged(bool value) => ToggleChanged();

    private void ToggleChanged()
    {
        if (_loading)
            return;
        _settings.ShowDates = ShowDates;
        _settings.ShowTimes = ShowTimes;
        _settings.ShowChannels = ShowChannels;
        _settings.ShowCharacters = ShowCharacters;
        _settings.ShowCleanup = ShowCleanup;
        SaveSettings();
        RefreshDisplay();
        UpdateStatus();
    }

    // Read each time the display is rebuilt, so changes made in the Log Manager options are picked up
    private DisplayOptions BuildDisplay() => new(
        ShowDates, ShowTimes, ShowChannels, ShowCharacters,
        _settings.ColorCodes, _settings.UseChannelColors, _settings.WordWrap, BuildHighlight(),
        ShowCleanup ? LogManagerSettings.Load().IncludedChannels : null);

    /// <summary>Swaps in new display options and re-creates the rows so they pick them up.</summary>
    private void RefreshDisplay()
    {
        Display = BuildDisplay();
        HorizontalScroll = _settings.WordWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        VisibleLines = VisibleLines.ToArray();      // new list instance: the list re-realizes its rows
        UpdateResults();
    }

    private void SaveSettings()
    {
        try { _settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Could not write CityHelper.ini: " + ex.Message;
        }
    }

    // ---- folder and file list ----

    // The channel and character filters that were switched off before a refresh, restored once the file is read again
    private (HashSet<string> Channels, HashSet<string> Characters)? _keepFilters;

    private (HashSet<string> Channels, HashSet<string> Characters) CaptureUncheckedFilters() => (
        ChannelItems.Where(c => !c.IsChecked).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
        CharacterItems.Where(c => !c.IsChecked).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase));

    /// <summary>Reads the folder again. With <paramref name="keepPath"/> that file stays selected if it still exists.</summary>
    private void LoadFolder(string? keepPath = null)
    {
        if (keepPath is null)
            _keepFilters = null;
        Files.Clear();
        SelectedFile = null;
        ClearLog();

        if (_state.SelectedInstallation is not { } inst || _state.SelectedAccount is not { } account)
        {
            FolderText = "";
            StatusText = "Select an installation and account.";
            return;
        }

        var dir = Path.Combine(inst.Path, "accounts", account.Name, "logs");
        FolderText = dir;
        if (!Directory.Exists(dir))
        {
            StatusText = "This account has no logs folder.";
            return;
        }

        try
        {
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*.txt", SearchOption.TopDirectoryOnly)
                         .Where(f => f.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(f => LogOperations.DateOf(f.FullName))     // the date in the name, newest first
                         .ThenByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase))
                Files.Add(new LogFileItem(f.Name, f.FullName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Could not read the logs folder: " + ex.Message;
            return;
        }

        StatusText = Files.Count == 0 ? "No log files found." : "";
        var target = (keepPath is null ? null : Files.FirstOrDefault(f => f.Path.Equals(keepPath, StringComparison.OrdinalIgnoreCase)))
                     ?? Files.FirstOrDefault();         // newest first
        if (target is null || !target.Path.Equals(keepPath, StringComparison.OrdinalIgnoreCase))
            _keepFilters = null;                        // a different file: its filters start fresh
        SelectedFile = target;
    }

    partial void OnSelectedFileChanged(LogFileItem? value) => _ = LoadFileAsync(value);

    private void ClearLog()
    {
        _log = null;
        _suspendFilter = true;
        ChannelItems.Clear();
        CharacterItems.Clear();
        _suspendFilter = false;
        VisibleLines = Array.Empty<LogLine>();
    }

    private async Task LoadFileAsync(LogFileItem? file)
    {
        int version = ++_version;
        var keep = _keepFilters;
        _keepFilters = null;
        ClearLog();
        if (file is null)
            return;

        StatusText = $"Loading {file.Name}...";
        ParsedLog? parsed = null;
        string? error = null;
        try
        {
            parsed = await Task.Run(() => LogFileParser.Parse(file.Path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
        }
        if (version != _version)
            return;                                     // another file was picked meanwhile

        if (parsed is null)
        {
            StatusText = "Could not read the file: " + error;
            return;
        }

        _log = parsed;
        _suspendFilter = true;
        foreach (var (name, count) in parsed.ChannelCounts)
            ChannelItems.Add(new FilterItemViewModel(name, count, ApplyFilters) { IsChecked = keep is null || !keep.Value.Channels.Contains(name) });
        foreach (var (name, count) in parsed.CharacterCounts)
            CharacterItems.Add(new FilterItemViewModel(name, count, ApplyFilters) { IsChecked = keep is null || !keep.Value.Characters.Contains(name) });
        _suspendFilter = false;

        ApplyFilters();
    }

    // ---- filtering ----

    private void ApplyFilters()
    {
        if (_suspendFilter || _log is null)
            return;

        var channels = ChannelItems.Where(c => c.IsChecked).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var characters = CharacterItems.Where(c => c.IsChecked).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        VisibleLines = _log.Lines.Where(l => channels.Contains(l.ChannelKey) && characters.Contains(l.CharacterKey)).ToArray();
        _currentResult = -1;
        UpdateResults();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_log is null)
            return;
        StatusText = $"{VisibleLines.Count:N0} of {_log.Lines.Count:N0} lines";
        if (ShowCleanup && Display.CleanupKeeps != null)
        {
            // Counted over the whole file, in physical lines, like the "removed N line(s)" message of the cleanup
            int removed = _log.Lines.Where(Display.IsRemovedByCleanup).Sum(l => l.PhysicalLines);
            StatusText += $"   |   Cleanup would remove {removed:N0} line(s) (shown in red)";
        }
    }

    private void SetAll(ObservableCollection<FilterItemViewModel> items, bool isChecked)
    {
        _suspendFilter = true;                          // one re-filter at the end, not one per item
        foreach (var item in items)
            item.IsChecked = isChecked;
        _suspendFilter = false;
        ApplyFilters();
    }

    [RelayCommand] private void CheckAllChannels() => SetAll(ChannelItems, true);
    [RelayCommand] private void UncheckAllChannels() => SetAll(ChannelItems, false);
    [RelayCommand] private void CheckAllCharacters() => SetAll(CharacterItems, true);
    [RelayCommand] private void UncheckAllCharacters() => SetAll(CharacterItems, false);

    /// <summary>Rewrites "[05:53]"-style channels in the displayed log as "[System]" (needs the Log Manager option).</summary>
    [RelayCommand]
    private async Task FixSystemAsync()
    {
        var file = SelectedFile;
        if (file is null)
        {
            StatusText = "Pick a log file first.";
            return;
        }
        var settings = LogManagerSettings.Load();

        await RunOnFileAsync(file, "Fixing...",
            (dir, path) => LogOperations.FixTimestampedSystemMessages(dir, path, settings.KeepCopies, settings.CopiesFolder));
    }

    /// <summary>Adds [Combat] / [System] to the displayed log's lines that have no channel but are known messages.</summary>
    [RelayCommand]
    private async Task InsertChannelsAsync()
    {
        if (SelectedFile is not { } file)
        {
            StatusText = "Pick a log file first.";
            return;
        }
        var settings = LogManagerSettings.Load();
        await RunOnFileAsync(file, "Adding channels...",
            (dir, path) => LogOperations.InsertChannels(dir, path, settings.KeepCopies, settings.CopiesFolder));
    }

    /// <summary>Removes the entries of the channels left unchecked in Options > Log Manager from the displayed log.</summary>
    [RelayCommand]
    private async Task CleanupLogfileAsync()
    {
        if (SelectedFile is not { } file)
        {
            StatusText = "Pick a log file first.";
            return;
        }
        var settings = LogManagerSettings.Load();

        // Safety: removing a lot of a log needs a confirmation (more than 20% of its lines or 5000 lines, whichever is fewer)
        StatusText = "Checking...";
        (int Removed, int Total) preview;
        try
        {
            preview = await Task.Run(() => LogOperations.PreviewCleanup(file.Path, settings.IncludedChannels));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Could not read the file: " + ex.Message;
            return;
        }
        if (preview.Removed == 0)
        {
            UpdateStatus();
            StatusText = $"{file.Name}: nothing to remove   |   " + StatusText;
            return;
        }

        double limit = Math.Min(preview.Total * 0.2, 5000);
        if (preview.Removed > limit)
        {
            double percent = preview.Total == 0 ? 0 : 100.0 * preview.Removed / preview.Total;
            var message = $"Cleanup would remove {preview.Removed:N0} of {preview.Total:N0} lines ({percent:0}%) from {file.Name}.\n\n"
                        + (settings.KeepCopies ? "A copy is kept first.\n\n" : "No copy is kept (see Options > Log Manager).\n\n")
                        + "Continue?";
            if (Confirm is null || !await Confirm(message))
            {
                UpdateStatus();
                StatusText = "Cleanup cancelled.   |   " + StatusText;
                return;
            }
        }

        await RunOnFileAsync(file, "Cleaning up...",
            (dir, path) => LogOperations.Cleanup(dir, new[] { path }, settings.IncludedChannels, settings.KeepCopies,
                                                  settings.CopiesFolder));
    }

    /// <summary>Asks the person to confirm (Yes / No); set by the view. Without it a confirmation counts as "No".</summary>
    public Func<string, Task<bool>>? Confirm { get; set; }

    /// <summary>Runs a rewrite of the displayed file off the UI thread, then shows the rewritten file and what was done.</summary>
    private async Task RunOnFileAsync(LogFileItem file, string busyText, Func<string, string, LogOperationResult> operation)
    {
        var dir = Path.GetDirectoryName(file.Path)!;
        StatusText = busyText;
        var result = await Task.Run(() => operation(dir, file.Path));
        await LoadFileAsync(file);                      // show the rewritten file
        var parts = new List<string>(result.Messages);
        if (result.Errors.Count > 0)
            parts.Add("Errors: " + string.Join("; ", result.Errors));
        StatusText = string.Join("   |   ", parts) + "   |   " + StatusText;
    }

    /// <summary>
    /// Reads the folder and the open log file from disk again: the same file stays open, the channel and character
    /// filters stay as they were, and the find box's search runs again on the new content (F5).
    /// </summary>
    [RelayCommand]
    private void Refresh()
    {
        var current = SelectedFile?.Path;
        _keepFilters = current is null ? null : CaptureUncheckedFilters();
        LoadFolder(current);
    }

    /// <summary>Called after the Options window saved: picks up what was written.</summary>
    public void ReloadOptions()
    {
        // The toggles on the toolbar are kept as they are
        var saved = LogViewerSettings.Load();
        _settings.ColorCodes = saved.ColorCodes;
        _settings.UseChannelColors = saved.UseChannelColors;
        _settings.WordWrap = saved.WordWrap;
        RefreshDisplay();     // also re-reads the Log Manager channels used for the cleanup marking
        UpdateStatus();
    }
}
