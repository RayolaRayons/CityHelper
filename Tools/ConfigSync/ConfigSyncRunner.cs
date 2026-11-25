using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CityHelper.Services;
using CityHelper.Tools.ConfigSync.ViewModels;

namespace CityHelper.Tools.ConfigSync;

/// <summary>
/// Config Sync's copy process without the window: works out which side of each enabled item is newer and copies it
/// across (logs are merged), exactly as Sync All does. Used by the command line.
/// </summary>
public static class ConfigSyncRunner
{
    /// <summary>Copies one row in the direction its timestamps point (or merges a logs row).</summary>
    internal static CopyResult CopyRow(SyncRowViewModel row)
    {
        var result = new CopyResult();
        if (row.SyncPaths is null)
            return result;
        if (row.IsMerge)
            return LogMerger.Merge(row.LocalPaths[0], row.SyncPaths[0], row.ValidateOrphans);
        if (row.Direction == SyncDirection.None)
            return result;

        bool toSync = row.Direction == SyncDirection.LocalToSync;
        var from = toSync ? row.LocalPaths : row.SyncPaths;
        var to = toSync ? row.SyncPaths : row.LocalPaths;
        var exclude = toSync ? row.LocalExclude : row.SyncExclude;

        for (int i = 0; i < from.Count; i++)
            result.Add(FileCopier.Copy(from[i], to[i], exclude, row.Item.FilePattern, row.Item.Recursive));
        return result;
    }

    /// <summary>Syncs every enabled item of the selected installation and account. Returns what happened per item, or a problem.</summary>
    public static (List<(string Item, CopyResult Result)> Items, string? Problem) Run(AppState state)
    {
        var items = new List<(string, CopyResult)>();
        if (state.SelectedInstallation is not { } inst || state.SelectedAccount is not { } account)
            return (items, "Select an installation and account first.");

        var names = state.Installations.Select(i => i.Name).ToList();
        var settings = ConfigSyncSettings.Load(names, state.Installations.FirstOrDefault(i => i.IsDefault)?.Name);
        var env = settings.Effective(inst.Name);
        var gameEnv = settings.Find(inst.Name)?.GameEnvironment ?? SyncItems.DefaultGameEnvironment;
        if (string.IsNullOrWhiteSpace(env.BackupFolder))
            return (items, $"No sync folder is set for \"{inst.Name}\" (Options > Config Sync).");
        var backup = env.BackupFolder;

        foreach (var item in SyncItems.All.Where(i => env.EnabledItems.Contains(i.Key)))
        {
            var rels = item.RelativePaths(account.Name, gameEnv);
            var exclude = item.RelativeExclude(account.Name, gameEnv);
            var local = rels.Select(r => Path.Combine(inst.Path, r)).ToList();
            var localExclude = exclude is null ? null : Path.Combine(inst.Path, exclude);
            var sync = rels.Select(r => Path.Combine(backup, r)).ToList();
            var syncExclude = exclude is null ? null : Path.Combine(backup, exclude);

            var row = new SyncRowViewModel(item, local, localExclude, sync, syncExclude, _ => System.Threading.Tasks.Task.CompletedTask)
            {
                IsMerge = (item.Key == "logfiles" && env.MergeLogfiles) || (item.Key == "private_logs" && env.MergePrivateLogs),
                ValidateOrphans = settings.ValidateOrphanedLogs && item.Key == "logfiles",
            };

            row.SetTimes(FileStamp.Latest(local, localExclude, item.FilePattern, item.Recursive),
                         FileStamp.Latest(sync, syncExclude, item.FilePattern, item.Recursive));
            if (row.IsMerge)
                row.SetMergeDiffers(LogMerger.HasDifferences(local[0], sync[0], row.ValidateOrphans));
            if (!row.CanSync)
            {
                items.Add((item.DisplayName, new CopyResult { UpToDate = 1 }));
                continue;
            }
            items.Add((item.DisplayName, CopyRow(row)));
        }
        return (items, null);
    }
}
