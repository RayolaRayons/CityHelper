using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.Tools.ConfigSync.ViewModels;

public enum SyncDirection
{
    None,           // nothing to do (identical, or nothing exists)
    LocalToSync,    // local is newer (or only local exists)
    SyncToLocal,    // sync folder is newer (or only the sync folder has it)
}

/// <summary>One item in the grid: local side, sync-folder side, and the way a sync would go.</summary>
public sealed partial class SyncRowViewModel : ViewModelBase
{
    private readonly Func<SyncRowViewModel, Task> _sync;
    private bool _busy;

    public SyncRowViewModel(SyncItemDef item, IReadOnlyList<string> localPaths, string? localExclude,
                            IReadOnlyList<string>? syncPaths, string? syncExclude, Func<SyncRowViewModel, Task> sync)
    {
        Item = item;
        LocalPaths = localPaths;
        LocalExclude = localExclude;
        SyncPaths = syncPaths;
        SyncExclude = syncExclude;
        _sync = sync;
    }

    /// <summary>Log files are merged (two-way, by timestamp) instead of copied one way.</summary>
    public bool IsMerge { get; init; }
    private bool _mergeDiffers;

    public IReadOnlyList<string> LocalPaths { get; }
    public string? LocalExclude { get; }
    public IReadOnlyList<string>? SyncPaths { get; }
    public string? SyncExclude { get; }

    public SyncItemDef Item { get; }
    public string Name => Item.DisplayName;
    public string LocalPath => string.Join("\n", LocalPaths);
    public string? SyncPath => SyncPaths is null ? null : string.Join("\n", SyncPaths);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocalText))]
    private bool _isLoaded;

    public DateTime? LocalTime { get; private set; }
    public DateTime? SyncTime { get; private set; }

    public SyncDirection Direction { get; private set; }

    public bool LocalIsNewer => Direction == SyncDirection.LocalToSync && LocalTime != null && SyncTime != null;
    public bool SyncIsNewer => Direction == SyncDirection.SyncToLocal && LocalTime != null && SyncTime != null;
    public string ArrowText => IsMerge ? (_mergeDiffers ? "\u21C4" : "=") : Direction switch
    {
        SyncDirection.LocalToSync => "→",
        SyncDirection.SyncToLocal => "←",
        _ => "=",
    };
    public bool CanSync => (IsMerge ? _mergeDiffers : Direction != SyncDirection.None) && SyncPath != null && !_busy;

    /// <summary>For merge rows: whether the two folders differ in any way a merge would change.</summary>
    public void SetMergeDiffers(bool differs)
    {
        _mergeDiffers = differs;
        OnPropertyChanged(nameof(ArrowText));
        OnPropertyChanged(nameof(CanSync));
        SyncCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Disables the arrow while a sync is running.</summary>
    public void SetBusy(bool busy)
    {
        _busy = busy;
        OnPropertyChanged(nameof(CanSync));
        SyncCommand.NotifyCanExecuteChanged();
    }

    public string LocalText => Describe(LocalTime);
    public string SyncText => SyncPath is null ? "No sync folder set" : Describe(SyncTime);

    private string Describe(DateTime? t) =>
        !IsLoaded ? "Checking..." : t is null ? "Not found" : "Modified " + t.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>Applies the file timestamps (UTC) and works out which side is newer.</summary>
    public void SetTimes(DateTime? local, DateTime? sync)
    {
        LocalTime = local;
        SyncTime = sync;
        Direction =
            local is null && sync is null ? SyncDirection.None :
            sync is null ? SyncDirection.LocalToSync :
            local is null ? SyncDirection.SyncToLocal :
            // Compare to the second: filesystems differ in timestamp precision
            Truncate(local.Value) > Truncate(sync.Value) ? SyncDirection.LocalToSync :
            Truncate(local.Value) < Truncate(sync.Value) ? SyncDirection.SyncToLocal :
            SyncDirection.None;
        IsLoaded = true;

        OnPropertyChanged(nameof(LocalText));
        OnPropertyChanged(nameof(SyncText));
        OnPropertyChanged(nameof(LocalIsNewer));
        OnPropertyChanged(nameof(SyncIsNewer));
        OnPropertyChanged(nameof(ArrowText));
        OnPropertyChanged(nameof(CanSync));
        SyncCommand.NotifyCanExecuteChanged();
    }

    private static DateTime Truncate(DateTime d) => new(d.Ticks - d.Ticks % TimeSpan.TicksPerSecond, d.Kind);

    [RelayCommand(CanExecute = nameof(CanSync))]
    private Task Sync() => _sync(this);
}
