using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CityHelper.Tools.ConfigSync;

namespace CityHelper.Tools.LogManager;

public enum MergeType
{
    Selected,
    Weekly,
    Monthly,
}

public sealed class LogOperationResult
{
    public List<string> Messages { get; } = new();
    public List<string> Errors { get; } = new();
}

/// <summary>Merge and cleanup of the chat log files in an account's logs folder.</summary>
public static class LogOperations
{
    public const string ArchiveFolder = "Archive";

    private static readonly Regex DateInName = new(@"(\d{4}-\d{2}-\d{2})", RegexOptions.Compiled);
    private static readonly UTF8Encoding Utf8 = new(false);
    private const string Newline = "\r\n";

    // ------------------------------------------------------- which files

    // A log the game writes for one day: "chatlog 2026-11-13.txt". The merged files ("chatlogWeekly 2026-11-09.txt") don't match.
    private static readonly Regex DailyName = new(@"^chatlog[ _-]*\d{4}-\d{2}-\d{2}\.txt$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool IsDailyLog(string fileName) => DailyName.IsMatch(fileName);

    // The logs this program makes by merging: chatlogWeekly, chatlogMonthly (and chatlogMerged for a merge of chosen files)
    private static readonly Regex MergedName = new(@"^chatlog(Weekly|Monthly|Merged)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>True for a log made by a merge (weekly, monthly or merged); any other log counts as a daily log.</summary>
    public static bool IsMergedLog(string fileName) => MergedName.IsMatch(fileName);

    /// <summary>
    /// True if another program (City of Heroes, while it is logging to the file) has the file open. The check is an
    /// attempt to open it exclusively, which fails while anyone else holds it.
    /// </summary>
    public static bool IsOpenElsewhere(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;        // sharing violation
        }
        catch (UnauthorizedAccessException)
        {
            return false;       // not "in use": a permission problem is reported when the merge tries to write
        }
    }

    // ---------------------------------------------------------------- merge

    /// <summary>
    /// Merges the given files: all of them into one (Selected), or grouped by the week / month of the date in their
    /// file names. Entries are ordered by timestamp and de-duplicated. With <paramref name="backup"/> the source files
    /// are moved into the Archive subfolder afterwards.
    /// </summary>
    public static LogOperationResult Merge(string logDir, IReadOnlyList<string> files, MergeType type, bool backup, string? copiesFolder = null)
    {
        var result = new LogOperationResult();
        if (files.Count == 0)
        {
            result.Errors.Add("No files are checked.");
            return result;
        }

        // A log the game still has open is left alone (it is still being written, and can't be moved)
        var busy = files.Where(IsOpenElsewhere).ToList();
        if (busy.Count > 0)
        {
            result.Messages.Add("Skipped (open in the game): " + string.Join(", ", busy.Select(Path.GetFileName)));
            files = files.Except(busy, StringComparer.OrdinalIgnoreCase).ToList();
            if (files.Count == 0)
                return result;
        }

        var dated = files.Select(f => (Path: f, Date: DateOf(f))).OrderBy(x => x.Date).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();

        IEnumerable<(string OutputName, List<string> Sources, string Label)> groups = type switch
        {
            MergeType.Selected => new[] { ($"chatlogMerged {dated[0].Date:yyyy-MM-dd}.txt", dated.Select(d => d.Path).ToList(), "Selected files") },
            MergeType.Weekly => dated.GroupBy(d => StartOfWeek(d.Date)).Select(g =>
                ($"chatlogWeekly {g.Key:yyyy-MM-dd}.txt", g.Select(d => d.Path).ToList(), $"Week of {g.Key:yyyy-MM-dd}")),
            _ => dated.GroupBy(d => new DateTime(d.Date.Year, d.Date.Month, 1)).Select(g =>
                ($"chatlogMonthly {g.Key:yyyy-MM-dd}.txt", g.Select(d => d.Path).ToList(), g.Key.ToString("MMMM yyyy"))),
        };

        foreach (var (outputName, sources, label) in groups)
        {
            // Checked files are combined into one file, so that needs two. A week / month can take a single day: it is
            // merged into that period's log, which is created if it isn't there yet.
            if (type == MergeType.Selected && sources.Count < 2)
            {
                result.Messages.Add($"{label}: skipped (only one file)");
                continue;
            }
            var output = Path.Combine(logDir, outputName);
            if (sources.Count == 1 && SamePath(sources[0], output))
            {
                result.Messages.Add($"{label}: {outputName} is already the log for this period");
                continue;
            }
            try
            {
                bool existed = File.Exists(output);
                MergeGroup(logDir, outputName, sources, backup, copiesFolder);
                var count = sources.Count(f => !SamePath(f, output));
                result.Messages.Add($"{label}: merged {count} file(s) " + (existed ? $"into the existing {outputName}" : $"into {outputName}"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Errors.Add($"{label}: {ex.Message}");
            }
        }
        return result;
    }

    private static void MergeGroup(string logDir, string outputName, List<string> sources, bool backup, string? copiesFolder)
    {
        var output = Path.Combine(logDir, outputName);

        // An existing file with the target name that wasn't picked is merged in too, never overwritten blindly
        var all = new List<string>(sources);
        if (File.Exists(output) && !all.Any(s => SamePath(s, output)))
            all.Add(output);

        var merged = LogMerger.UnionSorted(all.Select(f => LogMerger.Parse(File.ReadAllLines(f))));
        var text = string.Join(Newline, merged.SelectMany(e => e.Lines)) + Newline;
        var stamp = all.Max(File.GetLastWriteTimeUtc);

        // Write first, so nothing is moved or replaced unless the merged file is safely on disk
        var temp = output + ".merging";
        File.WriteAllText(temp, text, Utf8);
        try
        {
            if (backup)
                foreach (var source in all)
                    MoveToArchive(logDir, source, copiesFolder);
            File.Move(temp, output, overwrite: true);
            File.SetLastWriteTimeUtc(output, stamp);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    // -------------------------------------------------------------- cleanup

    /// <summary>How many lines cleanup would remove from the file, and how many it has (physical lines).</summary>
    public static (int Removed, int Total) PreviewCleanup(string file, IReadOnlySet<LogChannel> keep)
    {
        var entries = LogMerger.Parse(File.ReadAllLines(file));
        int total = entries.Sum(e => e.Lines.Count);
        int kept = entries.Where(e => keep.Contains(LogClassifier.Classify(e))).Sum(e => e.Lines.Count);
        return (total - kept, total);
    }

    /// <summary>Copies the file to the backup folder (the Archive folder when none is set) with the date and time added to its name.</summary>
    private static void BackupWithTimestamp(string logDir, string file, string? folder)
    {
        var dir = CopiesDirectory(logDir, folder);
        Directory.CreateDirectory(dir);

        var stem = Path.GetFileNameWithoutExtension(file) + "_" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var ext = Path.GetExtension(file);
        var target = Path.Combine(dir, stem + ext);
        for (int n = 2; File.Exists(target); n++)
            target = Path.Combine(dir, $"{stem} ({n}){ext}");
        File.Copy(file, target);
    }

    /// <summary>
    /// Trims each file in place, dropping the entries whose channel is not in <paramref name="keep"/>. With
    /// <paramref name="backup"/> the untouched original is copied to the Archive subfolder first.
    /// </summary>
    public static LogOperationResult Cleanup(string logDir, IReadOnlyList<string> files, IReadOnlySet<LogChannel> keep, bool backup,
        string? backupFolder = null)
    {
        var result = new LogOperationResult();
        if (files.Count == 0)
        {
            result.Errors.Add("No files are checked.");
            return result;
        }

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (IsOpenElsewhere(file))
            {
                result.Messages.Add($"{name}: skipped (open in the game)");
                continue;
            }
            try
            {
                var entries = LogMerger.Parse(File.ReadAllLines(file));
                var kept = entries.Where(e => keep.Contains(LogClassifier.Classify(e))).ToList();
                int removed = entries.Sum(e => e.Lines.Count) - kept.Sum(e => e.Lines.Count);
                if (removed == 0)
                {
                    result.Messages.Add($"{name}: nothing to remove");
                    continue;
                }

                var stamp = File.GetLastWriteTimeUtc(file);
                if (backup)
                    BackupWithTimestamp(logDir, file, backupFolder);

                var text = kept.Count == 0 ? "" : string.Join(Newline, kept.SelectMany(e => e.Lines)) + Newline;
                File.WriteAllText(file, text, Utf8);
                File.SetLastWriteTimeUtc(file, stamp);     // keep its place in date order
                result.Messages.Add($"{name}: removed {removed:N0} line(s)");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Errors.Add($"{name}: {ex.Message}");
            }
        }
        return result;
    }

    // ------------------------------------------------- timestamped system messages

    // "2026-11-06 05:53:10 [05:53] You can claim ..." : the channel is a time of day, not a name
    private static readonly Regex TimestampChannel = new(
        @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} )\[\d{1,2}:\d{2}(?::\d{2})?\]", RegexOptions.Compiled);

    /// <summary>
    /// Rewrites every line whose channel is a timestamp so the channel reads "[System]". With
    /// <paramref name="backup"/> the untouched original is copied to the Archive subfolder first.
    /// </summary>
    public static LogOperationResult FixTimestampedSystemMessages(string logDir, string file, bool backup, string? copiesFolder = null)
    {
        var result = new LogOperationResult();
        var name = Path.GetFileName(file);
        if (IsOpenElsewhere(file))
        {
            result.Messages.Add($"{name}: skipped (open in the game)");
            return result;
        }
        try
        {
            var lines = File.ReadAllLines(file);
            int changed = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var fixedLine = TimestampChannel.Replace(lines[i], "$1[System]");
                if (fixedLine != lines[i])
                {
                    lines[i] = fixedLine;
                    changed++;
                }
            }
            if (changed == 0)
            {
                result.Messages.Add($"{name}: no timestamped system messages found");
                return result;
            }

            var stamp = File.GetLastWriteTimeUtc(file);
            if (backup)
                BackupWithTimestamp(logDir, file, copiesFolder);
            File.WriteAllText(file, string.Join(Newline, lines) + Newline, Utf8);
            File.SetLastWriteTimeUtc(file, stamp);
            result.Messages.Add($"{name}: fixed {changed:N0} line(s)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Errors.Add($"{name}: {ex.Message}");
        }
        return result;
    }

    // ----------------------------------------------------- insert channels

    // "2026-11-06 05:53:10 Parry is recharged." : no [channel] at all
    private static readonly Regex UnbracketedLine = new(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) (?!\[)(.+)$", RegexOptions.Compiled);
    private static readonly Regex SystemTaggedLine = new(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) \[System\] (.+)$", RegexOptions.Compiled);

    /// <summary>
    /// Adds "[Combat]", "[System]", "[Reward]" or "[Auction]" to the lines that have no channel and that the known message
    /// patterns (remove_color_tags.py and the extra rules) identify as one of those types; lines already marked [System]
    /// that are really one of the others are re-marked. Emotes and unrecognised lines are left alone.
    /// </summary>
    public static LogOperationResult InsertChannels(string logDir, string file, bool backup, string? copiesFolder = null)
    {
        var result = new LogOperationResult();
        var name = Path.GetFileName(file);
        if (IsOpenElsewhere(file))
        {
            result.Messages.Add($"{name}: skipped (open in the game)");
            return result;
        }
        try
        {
            var lines = File.ReadAllLines(file);
            int changed = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var m = UnbracketedLine.Match(lines[i]);
                if (m.Success)
                {
                    var channel = LogClassifier.Classify(lines[i]);
                    if (channel is not (LogChannel.Combat or LogChannel.System or LogChannel.Reward or LogChannel.Auction))
                        continue;
                    lines[i] = $"{m.Groups[1].Value} [{LogChannels.TagName(channel)}] {m.Groups[2].Value}";
                    changed++;
                    continue;
                }

                // A line given [System] earlier that the patterns now know to be Combat / Reward / Auction
                var tagged = SystemTaggedLine.Match(lines[i]);
                if (tagged.Success && KnownMessages.Identify(tagged.Groups[2].Value) is { } known && known != LogChannel.System)
                {
                    lines[i] = $"{tagged.Groups[1].Value} [{LogChannels.TagName(known)}] {tagged.Groups[2].Value}";
                    changed++;
                }
            }
            if (changed == 0)
            {
                result.Messages.Add($"{name}: no lines need a channel");
                return result;
            }

            var stamp = File.GetLastWriteTimeUtc(file);
            if (backup)
                BackupWithTimestamp(logDir, file, copiesFolder);
            File.WriteAllText(file, string.Join(Newline, lines) + Newline, Utf8);
            File.SetLastWriteTimeUtc(file, stamp);
            result.Messages.Add($"{name}: set the channel of {changed:N0} line(s)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Errors.Add($"{name}: {ex.Message}");
        }
        return result;
    }

    // --------------------------------------------------- orphaned daily logs

    /// <summary>
    /// Moves the daily logs whose entries are all in a weekly or monthly log covering their date into the Archive
    /// folder: they were evidently merged elsewhere and only came back (for example from a sync folder).
    /// </summary>
    public static LogOperationResult ArchiveContainedDailies(string logDir, string? copiesFolder = null)
    {
        var result = new LogOperationResult();
        if (!Directory.Exists(logDir))
            return result;

        var consolidated = new ConsolidatedLogs(logDir);
        if (!consolidated.Any)
            return result;

        string[] dailies;
        try
        {
            dailies = Directory.EnumerateFiles(logDir, "*.txt", SearchOption.TopDirectoryOnly)
                .Where(f => IsDailyLog(Path.GetFileName(f))).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Errors.Add(ex.Message);
            return result;
        }

        foreach (var daily in dailies)
        {
            if (IsOpenElsewhere(daily) || !consolidated.Contains(daily))
                continue;
            try
            {
                MoveToArchive(logDir, daily, copiesFolder);
                result.Messages.Add($"{Path.GetFileName(daily)}: already in a weekly / monthly log, moved to {ArchiveFolder}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Errors.Add($"{Path.GetFileName(daily)}: {ex.Message}");
            }
        }
        return result;
    }

    // -------------------------------------------------------------- helpers

    /// <summary>Date from the file name ("chatlog 2026-01-16.txt"), else the file's last-write date.</summary>
    public static DateTime DateOf(string path)
    {
        var m = DateInName.Match(Path.GetFileName(path));
        if (m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d))
            return d;
        return File.GetLastWriteTime(path).Date;
    }

    /// <summary>Monday of the week containing the date.</summary>
    private static DateTime StartOfWeek(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>The folder for archived logs and copies: the configured one, or Archive inside the logs folder.</summary>
    public static string CopiesDirectory(string logDir, string? copiesFolder) =>
        string.IsNullOrWhiteSpace(copiesFolder) ? Path.Combine(logDir, ArchiveFolder) : copiesFolder;

    private static string ArchivePath(string logDir, string file, string? copiesFolder)
    {
        var dir = CopiesDirectory(logDir, copiesFolder);
        Directory.CreateDirectory(dir);

        var name = Path.GetFileNameWithoutExtension(file);
        var ext = Path.GetExtension(file);
        var target = Path.Combine(dir, name + ext);
        for (int n = 2; File.Exists(target); n++)       // never overwrite something already archived
            target = Path.Combine(dir, $"{name} ({n}){ext}");
        return target;
    }

    private static void MoveToArchive(string logDir, string file, string? copiesFolder) =>
        File.Move(file, ArchivePath(logDir, file, copiesFolder));
}
