using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CityHelper.Services;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.Tools.ConfigSync.ViewModels;

/// <summary>
/// Lists every enabled sync item for the selected installation (environment) and account:
/// local files on the left, the sync folder on the right. The sync actions are placeholders for now.
/// </summary>
public sealed partial class ConfigSyncViewModel : ViewModelBase
{
    private readonly AppState _state;
    private int _version;
    private int _stampsVersion = -1;                                  // the _version whose scan produced _lastStamps
    private List<(DateTime? Local, DateTime? Sync)> _lastStamps = new();
    private readonly DispatcherTimer _watchTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _checking;

    public ConfigSyncViewModel(AppState state)
    {
        _state = state;
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.SelectedInstallation) or nameof(AppState.SelectedAccount))
                _ = RefreshAsync();
        };
        _watchTimer.Tick += async (_, _) => await CheckForChangesAsync();
        _ = RefreshAsync();
    }

    // --- Noticing changes (the game keeps writing its logs while this is open) ---

    /// <summary>Starts looking for changed files every few seconds. The view calls this while it is on screen.</summary>
    public void StartWatching()
    {
        _watchTimer.Start();
        _ = CheckForChangesAsync();
    }

    public void StopWatching() => _watchTimer.Stop();

    /// <summary>Compares the files' current write times with those of the last scan and re-scans if any differ.</summary>
    private async Task CheckForChangesAsync()
    {
        // Not while a scan or sync is running, or when the last scan's result is not in yet
        if (_checking || IsBusy || Rows.Count == 0 || _stampsVersion != _version)
            return;

        _checking = true;
        try
        {
            int version = _version;
            var rows = Rows.ToList();
            var stamps = await Task.Run(() => rows.Select(r => (
                Local: FileStamp.Latest(r.LocalPaths, r.LocalExclude, r.Item.FilePattern, r.Item.Recursive),
                Sync: r.SyncPaths is null ? (DateTime?)null : FileStamp.Latest(r.SyncPaths, r.SyncExclude, r.Item.FilePattern, r.Item.Recursive))).ToList());

            if (version == _version && !stamps.SequenceEqual(_lastStamps))
                await RefreshAsync();
        }
        finally
        {
            _checking = false;
        }
    }

    public ObservableCollection<SyncRowViewModel> Rows { get; } = new();

    [ObservableProperty]
    private string _environmentText = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncAllCommand))]
    private bool _hasWork;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncAllCommand))]
    private bool _isBusy;

    private ConfigSyncSettings LoadSettings() => LoadSettings(_state);

    private static ConfigSyncSettings LoadSettings(AppState state) =>
        ConfigSyncSettings.Load(
            state.Installations.Select(i => i.Name).ToList(),
            state.Installations.FirstOrDefault(i => i.IsDefault)?.Name);

    [RelayCommand]
    public async Task RefreshAsync()
    {
        int version = ++_version;
        Rows.Clear();
        HasWork = false;

        if (_state.SelectedInstallation is not { } inst || _state.SelectedAccount is not { } account)
        {
            EnvironmentText = "";
            StatusText = "Select an installation and account.";
            return;
        }

        var settings = LoadSettings();
        var env = settings.Effective(inst.Name);
        var gameEnv = settings.Find(inst.Name)?.GameEnvironment ?? SyncItems.DefaultGameEnvironment;
        var backup = string.IsNullOrWhiteSpace(env.BackupFolder) ? null : env.BackupFolder;
        EnvironmentText = $"Sync folder: {backup ?? "(not set)"}";

        foreach (var item in SyncItems.All.Where(i => env.EnabledItems.Contains(i.Key)))
        {
            var rels = item.RelativePaths(account.Name, gameEnv);
            var exclude = item.RelativeExclude(account.Name, gameEnv);
            Rows.Add(new SyncRowViewModel(item,
                rels.Select(r => Path.Combine(inst.Path, r)).ToList(),
                exclude is null ? null : Path.Combine(inst.Path, exclude),
                backup is null ? null : rels.Select(r => Path.Combine(backup, r)).ToList(),
                backup is null || exclude is null ? null : Path.Combine(backup, exclude),
                OnRowSync) { IsMerge = (item.Key == "logfiles" && env.MergeLogfiles) || (item.Key == "private_logs" && env.MergePrivateLogs),
                    ValidateOrphans = settings.ValidateOrphanedLogs && item.Key == "logfiles" });
        }

        StatusText = Rows.Count == 0 ? "No items are enabled for this environment (see Options)."
                   : backup is null ? "No sync folder is set for this environment (see Options)."
                   : "";

        // File system scans can be slow (logs, mods), so keep them off the UI thread
        var rows = Rows.ToList();
        var times = await Task.Run(() => rows.Select(r => (Local: FileStamp.Latest(r.LocalPaths, r.LocalExclude, r.Item.FilePattern, r.Item.Recursive),
                                                          Sync: r.SyncPaths is null ? null : FileStamp.Latest(r.SyncPaths, r.SyncExclude, r.Item.FilePattern, r.Item.Recursive),
                                                          Differs: r.IsMerge && r.SyncPaths is not null && LogMerger.HasDifferences(r.LocalPaths[0], r.SyncPaths[0], r.ValidateOrphans))).ToList());
        if (version != _version)
            return;     // selection changed while scanning

        _lastStamps = times.Select(t => (t.Local, t.Sync)).ToList();
        _stampsVersion = version;

        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].SetTimes(times[i].Local, times[i].Sync);
            if (rows[i].IsMerge)
                rows[i].SetMergeDiffers(times[i].Differs);
        }
        HasWork = backup is not null && rows.Any(r => r.CanSync);
    }

    /// <summary>Creates the Options window section for this tool (no view model needed, so nothing is scanned).</summary>
    public static ConfigSyncOptionsViewModel CreateOptions(AppState state) =>
        new(state.Installations.Select(i => i.Name).ToList(), LoadSettings(state), state);

    // --- Syncing ---

    private bool CanSyncAll => HasWork && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSyncAll))]
    private Task SyncAllAsync() => SyncRowsAsync(Rows.Where(r => r.CanSync).ToList());

    private Task OnRowSync(SyncRowViewModel row) => SyncRowsAsync(new[] { row });

    /// <summary>Copies each row's newer side over to the other, off the UI thread, then re-scans.</summary>
    private async Task SyncRowsAsync(IReadOnlyList<SyncRowViewModel> rows)
    {
        if (IsBusy || rows.Count == 0)
            return;

        IsBusy = true;
        foreach (var r in Rows) r.SetBusy(true);
        StatusText = rows.Count == 1 ? $"Syncing {rows[0].Name}..." : $"Syncing {rows.Count} items...";

        var total = await Task.Run(() =>
        {
            var sum = new CopyResult();
            foreach (var row in rows)
                sum.Add(CopyRow(row));
            return sum;
        });

        IsBusy = false;
        await RefreshAsync();     // also clears the busy state (the rows are rebuilt)

        var summary = $"Synced {(rows.Count == 1 ? rows[0].Name : rows.Count + " items")}: " +
                      $"{total.Copied} file(s) copied, {(total.Merged > 0 ? total.Merged + " merged, " : "")}{total.UpToDate} already up to date.";
        if (total.Errors.Count > 0)
            summary += $" {total.Errors.Count} error(s): " + string.Join("; ", total.Errors.Take(3)) +
                       (total.Errors.Count > 3 ? "; ..." : "");
        StatusText = summary;
    }

    private static CopyResult CopyRow(SyncRowViewModel row) => ConfigSyncRunner.CopyRow(row);
}

internal static class FileStamp
{
    /// <summary>Newest write time (UTC) across the given files/folders; null if none of them exist.</summary>
    public static DateTime? Latest(IEnumerable<string> paths, string? excludedFolder = null, string? pattern = null, bool recursive = true)
    {
        DateTime? newest = null;
        foreach (var path in paths)
            if (Latest(path, excludedFolder, pattern, recursive) is { } t && (newest is null || t > newest))
                newest = t;
        return newest;
    }

    /// <summary>
    /// Last write time (UTC) of a file, or of the newest file under a folder (skipping the excluded folder);
    /// null if it doesn't exist.
    /// </summary>
    public static DateTime? Latest(string path, string? excludedFolder = null, string? pattern = null, bool recursive = true)
    {
        try
        {
            if (File.Exists(path))
                return SharedFile.LastWriteUtc(path);
            if (!Directory.Exists(path))
                return null;

            string? skip = excludedFolder is null ? null
                : Path.GetFullPath(excludedFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            DateTime? newest = null;
            var options = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true };
            foreach (var file in new DirectoryInfo(path).EnumerateFiles(pattern ?? "*", options))
            {
                if (skip != null && file.FullName.StartsWith(skip, StringComparison.OrdinalIgnoreCase))
                    continue;
                var stamp = SharedFile.LastWriteUtc(file.FullName);
                if (newest is null || stamp > newest)
                    newest = stamp;
            }
            // A pattern-limited folder with no matching files counts as having nothing to sync
            return newest ?? (pattern is null ? Directory.GetLastWriteTimeUtc(path) : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
