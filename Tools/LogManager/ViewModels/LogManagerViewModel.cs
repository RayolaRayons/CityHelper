using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CityHelper.Services;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.Tools.LogManager.ViewModels;

public sealed partial class LogFileViewModel : ObservableObject
{
    public LogFileViewModel(FileInfo file)
    {
        Path = file.FullName;
        Name = file.Name;
        SizeText = FormatSize(file.Length);
        ModifiedText = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
        Date = LogOperations.DateOf(file.FullName);
        IsDaily = LogOperations.IsDailyLog(file.Name);
        IsMerged = LogOperations.IsMergedLog(file.Name);
        IsInUse = LogOperations.IsOpenElsewhere(file.FullName);
    }

    public string Path { get; }
    public string Name { get; }

    /// <summary>The date in the file name (the file's date if it has none); the list is ordered by it.</summary>
    public DateTime Date { get; }

    /// <summary>A single day's log as written by the game (not a merged file).</summary>
    public bool IsDaily { get; }

    /// <summary>A log made by a merge (weekly, monthly or merged).</summary>
    public bool IsMerged { get; }

    /// <summary>The game has the file open right now, so merging and cleanup leave it alone.</summary>
    public bool IsInUse { get; }

    public string DisplayName => IsInUse ? Name + "   (open in the game)" : Name;
    public string SizeText { get; }
    public string ModifiedText { get; }

    [ObservableProperty]
    private bool _isChecked;

    // A log the game has open can't be checked (merging and cleanup leave it alone)
    partial void OnIsCheckedChanged(bool value)
    {
        if (value && IsInUse)
            IsChecked = false;
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):0.0} MB" :
        bytes >= 1 << 10 ? $"{bytes / (double)(1 << 10):0.#} KB" : $"{bytes} B";
}

/// <summary>Lists the selected account's log files and merges / trims the checked ones.</summary>
public sealed partial class LogManagerViewModel : ViewModelBase
{
    private readonly AppState _state;
    private int _version;

    public LogManagerViewModel(AppState state)
    {
        _state = state;
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.SelectedInstallation) or nameof(AppState.SelectedAccount))
                Refresh(keepChecks: false);
        };
        RefreshOptions();
        Refresh(keepChecks: false);
    }

    public ObservableCollection<LogFileViewModel> Files { get; } = new();

    /// <summary>"Merge Weekly" / "Merge Monthly" for the merge type chosen in Options; the button is hidden for "No merge".</summary>
    [ObservableProperty] private string _periodMergeText = "";
    [ObservableProperty] private bool _hasPeriodMerge;

    /// <summary>Reads the merge type from the options again (at start, and after the options were saved).</summary>
    public void RefreshOptions()
    {
        var type = LogManagerSettings.Load().MergeType;
        HasPeriodMerge = type != MergeType.Selected;
        PeriodMergeText = type switch { MergeType.Weekly => "Merge Weekly", MergeType.Monthly => "Merge Monthly", _ => "" };
    }

    [ObservableProperty]
    private string _folderText = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MergeCommand), nameof(MergePeriodCommand), nameof(InsertChannelsCommand), nameof(FixSystemCommand), nameof(CleanupLogfileCommand))]
    private bool _isBusy;

    private string? LogDirectory =>
        _state.SelectedInstallation is { } inst && _state.SelectedAccount is { } account
            ? Path.Combine(inst.Path, "accounts", account.Name, "logs")
            : null;

    /// <summary>Re-reads the folder. Checked files stay checked on a manual refresh.</summary>
    [RelayCommand]
    private void Refresh() => Refresh(keepChecks: true);

    /// <summary>
    /// The background upkeep switched on in Options: archives the daily logs that are already inside a weekly / monthly
    /// log. Re-lists the folder if anything changed. (The automatic System channel fix is not done here: it is done
    /// when a merge is started.)
    /// </summary>
    private async Task MaintainAsync(string dir, LogManagerSettings settings)
    {
        int version = _version;
        var result = await Task.Run(() =>
        {
            var sum = new LogOperationResult();
            if (settings.ValidateOrphanedDailies)
            {
                var orphans = LogOperations.ArchiveContainedDailies(dir, settings.CopiesFolder);
                sum.Messages.AddRange(orphans.Messages);
                sum.Errors.AddRange(orphans.Errors);
            }
            return sum;
        });
        if (version != _version || result.Messages.Count == 0 && result.Errors.Count == 0)
            return;
        Refresh(keepChecks: true, validate: false);
        StatusText = Summarize(result);
    }

    private void Refresh(bool keepChecks, bool validate = true)
    {
        _version++;
        var previouslyChecked = keepChecks
            ? Files.Where(f => f.IsChecked).Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>();
        Files.Clear();

        var dir = LogDirectory;
        if (dir is null)
        {
            FolderText = "";
            StatusText = "Select an installation and account.";
            return;
        }

        FolderText = dir;
        if (!Directory.Exists(dir))
        {
            StatusText = "This account has no logs folder.";
            return;
        }

        try
        {
            // Only the .txt logs directly in the folder (private chat and the Archive subfolder are separate)
            // In sequence by the date in the file name, newest at the top, so merged files sit among the days they cover
            var infos = new DirectoryInfo(dir).EnumerateFiles("*.txt", SearchOption.TopDirectoryOnly)
                .Where(f => f.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                .Select(f => new LogFileViewModel(f) { IsChecked = previouslyChecked.Contains(f.Name) })
                .OrderByDescending(f => f.Date).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var file in infos)
                Files.Add(file);
            StatusText = Files.Count == 0 ? "No log files found." : $"{Files.Count} log file(s).";
            var settings = LogManagerSettings.Load();
            if (validate && settings.ValidateOrphanedDailies)
                _ = MaintainAsync(dir, settings);

        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Could not read the logs folder: " + ex.Message;
        }
    }

    /// <summary>Select Daily Logs: checks every log that is not a merged log (weekly, monthly or merged), except any the game has open.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private void SelectDailyLogs() => SelectDailies(setStatus: true);

    private void SelectDailies(bool setStatus)
    {
        int open = 0, selected = 0;
        foreach (var f in Files)
        {
            f.IsChecked = !f.IsMerged && !f.IsInUse;
            if (f.IsChecked) selected++;
            if (!f.IsMerged && f.IsInUse) open++;
        }
        if (setStatus)
            StatusText = $"Selected {selected} daily log(s)" + (open > 0 ? $"; {open} open in the game can't be selected." : ".");
    }

    private bool CanRun => !IsBusy;

    /// <summary>Merge: the checked files are combined into one file.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task MergeAsync() => RunMergeAsync(MergeType.Selected);

    /// <summary>
    /// Merge Weekly / Merge Monthly (the merge type in Options): the checked logs, or all the daily logs if none are
    /// checked, are combined into one file per week / month.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task MergePeriodAsync()
    {
        var type = LogManagerSettings.Load().MergeType;
        if (type == MergeType.Selected)
        {
            StatusText = "Choose Weekly or Monthly as the merge type under Options > Logs > Log Manager first.";
            return Task.CompletedTask;
        }
        if (!Files.Any(f => f.IsChecked))
            SelectDailies(setStatus: false);
        return RunMergeAsync(type);
    }

    private async Task RunMergeAsync(MergeType type)
    {
        var dir = LogDirectory;
        var settings = LogManagerSettings.Load();

        var checkedFiles = Files.Where(f => f.IsChecked).Select(f => f.Path).ToList();
        if (dir is null || checkedFiles.Count == 0)
        {
            StatusText = type == MergeType.Selected ? "Check the log files to merge first." : "There are no daily logs to merge.";
            return;
        }
        if (type == MergeType.Selected && checkedFiles.Count < 2)
        {
            StatusText = "Check at least two files to merge.";
            return;
        }

        IsBusy = true;
        StatusText = "Merging...";
        var result = await Task.Run(() =>
        {
            var fixes = new LogOperationResult();
            if (settings.FixAtMergeEnabled)
                foreach (var file in checkedFiles)
                {
                    var one = LogOperations.FixTimestampedSystemMessages(dir, file, settings.KeepCopies, settings.CopiesFolder);
                    fixes.Messages.AddRange(one.Messages.Where(m => m.Contains(": fixed ")));
                    fixes.Errors.AddRange(one.Errors);
                }
            var merged = LogOperations.Merge(dir, checkedFiles, type, settings.KeepCopies, settings.CopiesFolder);
            merged.Messages.InsertRange(0, fixes.Messages);
            merged.Errors.InsertRange(0, fixes.Errors);
            return merged;
        });
        IsBusy = false;

        Refresh(keepChecks: false);
        StatusText = Summarize(result);
    }

    /// <summary>Runs a rewrite on each checked file (off the UI thread) and lists what it did.</summary>
    private async Task RunOnCheckedAsync(string noneChecked, string busyText,
                                         Func<string, string, LogManagerSettings, LogOperationResult> operation)
    {
        var dir = LogDirectory;
        var checkedFiles = Files.Where(f => f.IsChecked).Select(f => f.Path).ToList();
        if (dir is null || checkedFiles.Count == 0)
        {
            StatusText = noneChecked;
            return;
        }

        var settings = LogManagerSettings.Load();
        IsBusy = true;
        StatusText = busyText;
        var result = await Task.Run(() =>
        {
            var sum = new LogOperationResult();
            foreach (var file in checkedFiles)
            {
                var one = operation(dir, file, settings);
                sum.Messages.AddRange(one.Messages);
                sum.Errors.AddRange(one.Errors);
            }
            return sum;
        });
        IsBusy = false;

        Refresh(keepChecks: true, validate: false);
        StatusText = Summarize(result);
    }

    /// <summary>Adds [Combat] / [System] / [Reward] / [Auction] to the lines of the checked files that have no channel but are known messages.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task InsertChannelsAsync() =>
        RunOnCheckedAsync("Check the log files to add channels to first.", "Adding channels...",
            (dir, file, s) => LogOperations.InsertChannels(dir, file, s.KeepCopies, s.CopiesFolder));

    /// <summary>A timestamp used as a channel, e.g. [05:53], becomes [System] in the checked files.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task FixSystemAsync() =>
        RunOnCheckedAsync("Check the log files to fix first.", "Fixing...",
            (dir, file, s) => LogOperations.FixTimestampedSystemMessages(dir, file, s.KeepCopies, s.CopiesFolder));

    /// <summary>Asks the person to confirm (Yes / No); set by the view. Without it a confirmation counts as "No".</summary>
    public Func<string, Task<bool>>? Confirm { get; set; }

    /// <summary>
    /// Removes the channels left unchecked in Options from the checked files. A file that would lose more than 20% of its
    /// lines, or more than 5000, (whichever is fewer) needs a confirmation, as in the Log Viewer.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CleanupLogfileAsync()
    {
        var dir = LogDirectory;
        var checkedFiles = Files.Where(f => f.IsChecked).Select(f => f.Path).ToList();
        if (dir is null || checkedFiles.Count == 0)
        {
            StatusText = "Check the log files to clean up first.";
            return;
        }

        var settings = LogManagerSettings.Load();
        IsBusy = true;
        StatusText = "Checking...";
        List<(string Path, int Removed, int Total)> previews;
        try
        {
            previews = await Task.Run(() => checkedFiles.Select(f =>
            {
                var (removed, total) = LogOperations.PreviewCleanup(f, settings.IncludedChannels);
                return (f, removed, total);
            }).ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            IsBusy = false;
            StatusText = "Could not read a file: " + ex.Message;
            return;
        }

        var changing = previews.Where(p => p.Removed > 0).ToList();
        if (changing.Count == 0)
        {
            IsBusy = false;
            StatusText = "Nothing to remove from the checked files.";
            return;
        }

        var large = changing.Where(p => p.Removed > Math.Min(p.Total * 0.2, 5000)).ToList();
        if (large.Count > 0)
        {
            var list = string.Join("\n", large.Take(8).Select(p =>
                $"{Path.GetFileName(p.Path)}: {p.Removed:N0} of {p.Total:N0} lines ({100.0 * p.Removed / Math.Max(1, p.Total):0}%)"));
            var message = $"Cleanup would remove a large part of {large.Count} file(s):\n\n{list}" + (large.Count > 8 ? "\n..." : "")
                        + "\n\n" + (settings.KeepCopies ? "A copy is kept first." : "No copy is kept (see Options > Log Manager).")
                        + "\n\nContinue with the cleanup?";
            if (Confirm is null || !await Confirm(message))
            {
                IsBusy = false;
                StatusText = "Cleanup cancelled.";
                return;
            }
        }

        StatusText = "Cleaning up...";
        var result = await Task.Run(() => LogOperations.Cleanup(dir, changing.Select(p => p.Path).ToList(), settings.IncludedChannels,
                                                                settings.KeepCopies, settings.CopiesFolder));
        IsBusy = false;
        Refresh(keepChecks: true, validate: false);
        StatusText = Summarize(result);
    }

    private static string Summarize(LogOperationResult r)
    {
        var parts = new List<string>(r.Messages);
        if (r.Errors.Count > 0)
            parts.Add("Errors: " + string.Join("; ", r.Errors));
        return string.Join("   |   ", parts);
    }
}
