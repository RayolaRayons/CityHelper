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
public sealed partial class LogViewerViewModel : ViewModelBase
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
    [ObservableProperty] private string _statusText = "";

    [ObservableProperty] private bool _showDates;
    [ObservableProperty] private bool _showTimes = true;
    [ObservableProperty] private bool _showChannels = true;
    [ObservableProperty] private bool _showCharacters = true;

    /// <summary>Marks in red the lines Log Manager's Cleanup Logfile would remove (its channel options decide).</summary>
    [ObservableProperty] private bool _showCleanup;

    /// <summary>Text to highlight in the lines; several words or phrases can be separated by commas. Not saved.</summary>
    [ObservableProperty] private string _highlightText = "";

    partial void OnHighlightTextChanged(string value)
    {
        if (!_loading && Display != null)
            RefreshDisplay();
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
    }

    private void SaveSettings()
    {
        try { _settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Could not write logviewer.ini: " + ex.Message;
        }
    }

    // ---- folder and file list ----

    private void LoadFolder()
    {
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
                         .OrderByDescending(f => f.LastWriteTimeUtc))
                Files.Add(new LogFileItem(f.Name, f.FullName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Could not read the logs folder: " + ex.Message;
            return;
        }

        StatusText = Files.Count == 0 ? "No log files found." : "";
        SelectedFile = Files.FirstOrDefault();          // newest first
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
            ChannelItems.Add(new FilterItemViewModel(name, count, ApplyFilters));
        foreach (var (name, count) in parsed.CharacterCounts)
            CharacterItems.Add(new FilterItemViewModel(name, count, ApplyFilters));
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

    [RelayCommand]
    private void Refresh()
    {
        var current = SelectedFile?.Path;
        LoadFolder();
        if (current != null && Files.FirstOrDefault(f => f.Path.Equals(current, StringComparison.OrdinalIgnoreCase)) is { } same)
            SelectedFile = same;                        // stay on the file that was open
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
