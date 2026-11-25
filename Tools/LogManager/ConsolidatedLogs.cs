using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CityHelper.Tools.ConfigSync;

namespace CityHelper.Tools.LogManager;

/// <summary>
/// The weekly and monthly logs of a logs folder ("chatlogWeekly 2026-11-09.txt", "chatlogMonthly 2026-11-01.txt"),
/// used to tell whether a daily log is an orphan: every entry of it already exists in a consolidated log whose date
/// range covers the day, so it was evidently merged elsewhere and only needs to go to the Archive folder.
/// </summary>
public sealed class ConsolidatedLogs
{
    private static readonly Regex Name = new(@"^chatlog(Weekly|Monthly)[ _-]*(\d{4}-\d{2}-\d{2})\.txt$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private sealed record Candidate(string Path, DateTime Start, DateTime End);

    private readonly List<Candidate> _candidates = new();
    private readonly Dictionary<string, Dictionary<string, int>> _entryCounts = new(StringComparer.OrdinalIgnoreCase);

    public ConsolidatedLogs(string logDir)
    {
        if (!Directory.Exists(logDir))
            return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(logDir, "chatlog*.txt", SearchOption.TopDirectoryOnly))
            {
                var m = Name.Match(System.IO.Path.GetFileName(file));
                if (!m.Success || !DateTime.TryParseExact(m.Groups[2].Value, "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var start))
                    continue;
                bool weekly = m.Groups[1].Value.Equals("Weekly", StringComparison.OrdinalIgnoreCase);
                var end = weekly ? start.AddDays(6) : new DateTime(start.Year, start.Month, 1).AddMonths(1).AddDays(-1);
                _candidates.Add(new Candidate(file, start, end));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // an unreadable folder has no consolidated logs we know of
        }
    }

    public bool Any => _candidates.Count > 0;

    /// <summary>True if every entry of the daily log is in the weekly / monthly logs whose dates overlap the day.</summary>
    public bool Contains(string dailyPath)
    {
        try
        {
            var day = LogOperations.DateOf(dailyPath).Date;
            var covering = _candidates.Where(c => day >= c.Start && day <= c.End).ToList();
            if (covering.Count == 0)
                return false;

            var entries = LogMerger.Parse(SharedFile.ReadAllLines(dailyPath));
            if (entries.Count == 0)
                return false;                       // nothing to compare: not proof that anything was merged

            // Multiset of the consolidated entries, so an entry repeated in the daily log must be repeated there too
            var available = new Dictionary<string, int>();
            foreach (var c in covering)
                foreach (var (key, count) in Counts(c.Path))
                    available[key] = Math.Max(available.GetValueOrDefault(key), count);

            var needed = new Dictionary<string, int>();
            foreach (var e in entries)
            {
                var key = e.Key;
                needed[key] = needed.GetValueOrDefault(key) + 1;
                if (needed[key] > available.GetValueOrDefault(key))
                    return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private Dictionary<string, int> Counts(string path)
    {
        if (_entryCounts.TryGetValue(path, out var cached))
            return cached;
        var counts = new Dictionary<string, int>();
        foreach (var e in LogMerger.Parse(SharedFile.ReadAllLines(path)))
            counts[e.Key] = counts.GetValueOrDefault(e.Key) + 1;
        return _entryCounts[path] = counts;
    }
}
