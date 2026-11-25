using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CityHelper.Services;

namespace CityHelper.Tools.LogManager;

/// <summary>Log Manager options, stored in the [LogManager.*] sections of CityHelper.ini.</summary>
public sealed class LogManagerSettings
{
    public const string MergeNone = "No merge";
    public const string MergeWeekly = "Weekly";
    public const string MergeMonthly = "Monthly";
    public static readonly IReadOnlyList<string> MergeModes = new[] { MergeNone, MergeWeekly, MergeMonthly };

    /// <summary>
    /// What the Merge button does. No merge: the files you check are combined into one. Weekly / Monthly: the daily
    /// logs (selected for you when nothing is checked) are combined into one file per week / month.
    /// </summary>
    public string MergeMode { get; set; } = MergeNone;

    public MergeType MergeType => MergeMode switch
    {
        MergeWeekly => MergeType.Weekly,
        MergeMonthly => MergeType.Monthly,
        _ => MergeType.Selected,
    };

    public const string FixManual = "Manual";
    public const string FixAtMerge = "At merge";
    public static readonly IReadOnlyList<string> SystemFixModes = new[] { FixManual, FixAtMerge };

    /// <summary>
    /// The System channel fix (a timestamp used as a channel, e.g. [05:53], becomes [System]). The Fix System button
    /// and /FixSystem are always available; At merge also does it by itself, on the files being merged, when a merge starts.
    /// </summary>
    public string SystemChannelFix { get; set; } = FixManual;

    public bool FixAtMergeEnabled => SystemChannelFix == FixAtMerge;

    /// <summary>
    /// Keep a copy of every log this program changes (merged sources, System channel fixes, inserted channels,
    /// cleanups): they go to <see cref="CopiesFolder"/>. When off, changes are made without a copy.
    /// </summary>
    public bool KeepCopies { get; set; } = true;

    /// <summary>Where copies and archived logs go; empty means the Archive folder inside the logs folder.</summary>
    public string CopiesFolder { get; set; } = "";

    /// <summary>Move daily logs that are already fully inside a weekly / monthly log to the Archive folder.</summary>
    public bool ValidateOrphanedDailies { get; set; }

    /// <summary>Channels to keep; cleanup removes the others. All selected initially.</summary>
    public HashSet<LogChannel> IncludedChannels { get; set; } = new(LogChannels.All);

    public static LogManagerSettings Load(string? path = null)
    {
        var settings = new LogManagerSettings();
        var lines = IniStore.ReadLines("LogManager", "logmanager.ini", path);
        if (lines is null)
            return settings;

        var channels = new Dictionary<string, bool>();
        bool sawKeepCopies = false, sawCopiesFolder = false, sawFix = false;
        bool? olderMerged = null, olderCleanup = null, olderAutoFix = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            int eq = line.IndexOf('=');
            if (line.Length == 0 || line[0] is '#' or ';' or '[' || eq < 0)
                continue;
            var key = line[..eq].Trim().ToLowerInvariant();
            var value = line[(eq + 1)..].Trim();
            switch (key)
            {
                case "preset": break;               // older files: the RP / None preset no longer exists
                case "keep_copies":
                    settings.KeepCopies = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    sawKeepCopies = true;
                    break;
                case "copies_folder":
                    settings.CopiesFolder = value;
                    sawCopiesFolder = true;
                    break;
                case "system_channel_fix":
                    settings.SystemChannelFix = SystemFixModes.FirstOrDefault(m => m.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? FixManual;
                    sawFix = true;
                    break;
                // Older files: three backup-style options and two System channel options, now one of each
                case "backup_merged_files": olderMerged = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                case "backup_on_cleanup": olderCleanup = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                case "cleanup_backup_folder":
                    if (!sawCopiesFolder) settings.CopiesFolder = value;
                    break;
                case "auto_fix_system_channel": olderAutoFix = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                case "fix_timestamped_system_messages": break;      // the button is always available now
                case "merge_mode":
                    settings.MergeMode = MergeModes.FirstOrDefault(m => m.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? MergeNone;
                    break;
                case "validate_orphaned_dailies":
                    settings.ValidateOrphanedDailies = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
                default:
                    channels[key] = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }

        if (!sawKeepCopies && (olderMerged.HasValue || olderCleanup.HasValue))
            settings.KeepCopies = (olderMerged ?? true) || (olderCleanup ?? true);
        if (!sawFix && olderAutoFix == true)
            settings.SystemChannelFix = FixAtMerge;

        // Channels missing from the file count as selected
        settings.IncludedChannels = new HashSet<LogChannel>(
            LogChannels.All.Where(c => channels.GetValueOrDefault(LogChannels.Key(c), true)));
        return settings;
    }

    public void Save(string? path = null)
    {
        var sb = new StringBuilder();
        sb.Append("[Options]\n");
        sb.Append("keep_copies = ").Append(KeepCopies ? "true" : "false").Append('\n');
        sb.Append("copies_folder = ").Append(CopiesFolder).Append('\n');
        sb.Append("system_channel_fix = ").Append(SystemChannelFix).Append('\n');
        sb.Append("validate_orphaned_dailies = ").Append(ValidateOrphanedDailies ? "true" : "false").Append('\n');
        sb.Append("merge_mode = ").Append(MergeMode).Append("\n\n");
        sb.Append("[Channels]\n");
        foreach (var c in LogChannels.All)
            sb.Append(LogChannels.Key(c)).Append(" = ").Append(IncludedChannels.Contains(c) ? "true" : "false").Append('\n');
        IniStore.WriteText("LogManager", sb.ToString(), path);
    }
}
