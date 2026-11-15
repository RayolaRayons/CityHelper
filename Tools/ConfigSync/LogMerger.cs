using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CityHelper.Tools.ConfigSync;

/// <summary>
/// Two-way merge of the .txt log files in two folders. Files present on one side only are copied across; files
/// with the same name but different content are merged line by line, ordered by the "yyyy-MM-dd HH:mm:ss"
/// timestamp each entry starts with, and the merged result is written to both sides.
/// </summary>
public static class LogMerger
{
    private static readonly Regex Stamp = new(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})(?!\d)", RegexOptions.Compiled);

    internal sealed class Entry
    {
        public DateTime Time { get; init; }
        public List<string> Lines { get; } = new();
        public string Key => string.Join("\n", Lines);
    }

    /// <summary>True if a file exists on one side only, or exists on both with different content.</summary>
    public static bool HasDifferences(string localDir, string syncDir)
    {
        var local = Files(localDir);
        var sync = Files(syncDir);
        if (!local.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(sync.Keys))
            return true;
        return local.Any(kv => !SameContent(kv.Value, sync[kv.Key]));
    }

    public static CopyResult Merge(string localDir, string syncDir)
    {
        var result = new CopyResult();
        try
        {
            Directory.CreateDirectory(localDir);
            Directory.CreateDirectory(syncDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Errors.Add(ex.Message);
            return result;
        }

        var local = Files(localDir);
        var sync = Files(syncDir);
        foreach (var name in local.Keys.Union(sync.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var hasLocal = local.TryGetValue(name, out var lp);
            var hasSync = sync.TryGetValue(name, out var sp);
            try
            {
                if (hasLocal && !hasSync)
                {
                    File.Copy(lp!, Path.Combine(syncDir, name), overwrite: false);
                    result.Copied++;
                }
                else if (hasSync && !hasLocal)
                {
                    File.Copy(sp!, Path.Combine(localDir, name), overwrite: false);
                    result.Copied++;
                }
                else if (SameContent(lp!, sp!))
                {
                    result.UpToDate++;
                }
                else
                {
                    MergeFiles(lp!, sp!);
                    result.Merged++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Errors.Add($"{name}: {ex.Message}");
            }
        }
        return result;
    }

    private static void MergeFiles(string localPath, string syncPath)
    {
        var merged = UnionSorted(new[] { Parse(SharedFile.ReadAllLines(localPath)), Parse(SharedFile.ReadAllLines(syncPath)) });
        var lines = merged.SelectMany(e => e.Lines);
        var newline = SharedFile.ReadAllText(localPath).Contains("\r\n") ? "\r\n" : Environment.NewLine;
        var text = string.Join(newline, lines) + newline;

        var utf8 = new UTF8Encoding(false);
        File.WriteAllText(localPath, text, utf8);
        File.WriteAllText(syncPath, text, utf8);

        // Same timestamp on both so the two sides don't look different afterwards
        var now = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(localPath, now);
        File.SetLastWriteTimeUtc(syncPath, now);
    }

    /// <summary>Groups lines into entries: a line starting with a timestamp begins one; other lines continue the previous entry.</summary>
    /// <summary>
    /// Multiset union of the sources, ordered by timestamp. An identical entry is kept as many times as the source
    /// with the most copies has it, so repeated lines inside one file survive but the same line in two files is not
    /// doubled. The sort is stable: entries with the same timestamp keep source order, then line order.
    /// </summary>
    internal static List<Entry> UnionSorted(IEnumerable<List<Entry>> sources)
    {
        var added = new Dictionary<string, int>();
        var all = new List<Entry>();
        foreach (var source in sources)
        {
            var seen = new Dictionary<string, int>();
            foreach (var e in source)
            {
                var key = e.Key;
                seen[key] = seen.GetValueOrDefault(key) + 1;
                if (seen[key] > added.GetValueOrDefault(key))
                {
                    added[key] = seen[key];
                    all.Add(e);
                }
            }
        }
        return all.OrderBy(e => e.Time).ToList();
    }

    internal static List<Entry> Parse(IEnumerable<string> lines)
    {
        var entries = new List<Entry>();
        Entry? current = null;
        foreach (var line in lines)
        {
            var m = Stamp.Match(line);
            if (m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var time))
            {
                current = new Entry { Time = time };
                entries.Add(current);
            }
            else if (current is null)
            {
                // Lines before the first timestamp stay at the top
                current = new Entry { Time = DateTime.MinValue };
                entries.Add(current);
            }
            current.Lines.Add(line);
        }
        return entries;
    }

    private static Dictionary<string, string> Files(string dir)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(dir))
            return map;
        foreach (var f in Directory.EnumerateFiles(dir, "*.txt", SearchOption.TopDirectoryOnly))
            if (Path.GetExtension(f).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                map[Path.GetFileName(f)] = f;
        return map;
    }

    private static bool SameContent(string a, string b)
    {
        try
        {
            // Read through shared access: FileInfo.Length is stale and File.ReadAllBytes fails for a log the game has open
            return SharedFile.ReadAllBytes(a).AsSpan().SequenceEqual(SharedFile.ReadAllBytes(b));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
