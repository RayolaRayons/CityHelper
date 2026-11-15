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
    public static LogOperationResult Merge(string logDir, IReadOnlyList<string> files, MergeType type, bool backup)
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
            if (sources.Count < 2)
            {
                result.Messages.Add($"{label}: skipped (only one file)");
                continue;
            }
            try
            {
                MergeGroup(logDir, outputName, sources, backup);
                result.Messages.Add($"{label}: merged {sources.Count} files into {outputName}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Errors.Add($"{label}: {ex.Message}");
            }
        }
        return result;
    }

    private static void MergeGroup(string logDir, string outputName, List<string> sources, bool backup)
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
                    MoveToArchive(logDir, source);
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

    /// <summary>
    /// Trims each file in place, dropping the entries whose channel is not in <paramref name="keep"/>. With
    /// <paramref name="backup"/> the untouched original is copied to the Archive subfolder first.
    /// </summary>
    public static LogOperationResult Cleanup(string logDir, IReadOnlyList<string> files, IReadOnlySet<LogChannel> keep, bool backup)
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
                    CopyToArchive(logDir, file);

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

    private static string ArchivePath(string logDir, string file)
    {
        var dir = Path.Combine(logDir, ArchiveFolder);
        Directory.CreateDirectory(dir);

        var name = Path.GetFileNameWithoutExtension(file);
        var ext = Path.GetExtension(file);
        var target = Path.Combine(dir, name + ext);
        for (int n = 2; File.Exists(target); n++)       // never overwrite something already archived
            target = Path.Combine(dir, $"{name} ({n}){ext}");
        return target;
    }

    private static void MoveToArchive(string logDir, string file) => File.Move(file, ArchivePath(logDir, file));

    private static void CopyToArchive(string logDir, string file) => File.Copy(file, ArchivePath(logDir, file));
}
