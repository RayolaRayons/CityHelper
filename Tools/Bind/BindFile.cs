using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CityHelper.Tools.Bind;

/// <summary>One line of a binds file: the key (combinations joined by +) and the command.</summary>
public sealed record BindEntry(string Key, string Command);

/// <summary>Reads and writes binds files: each line is "key command...". The original is backed up before the first overwrite.</summary>
public static class BindFile
{
    public const string BackupFolderName = "backup";

    public static List<BindEntry> Load(string path)
    {
        var entries = new List<BindEntry>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            int split = line.IndexOfAny(new[] { ' ', '\t' });
            entries.Add(split < 0
                ? new BindEntry(line, "")
                : new BindEntry(line[..split], line[(split + 1)..].Trim()));
        }
        return entries;
    }

    /// <summary>Copies the existing file to a "backup" subfolder next to it, unless an original is already kept there.</summary>
    public static void Backup(string path)
    {
        if (!File.Exists(path))
            return;
        var dir = Path.Combine(Path.GetDirectoryName(path)!, BackupFolderName);
        var target = Path.Combine(dir, Path.GetFileName(path));
        if (File.Exists(target))
            return;     // keep the very first (original) version
        Directory.CreateDirectory(dir);
        File.Copy(path, target);
    }

    public static void Save(string path, IEnumerable<BindEntry> entries)
    {
        Backup(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sb = new StringBuilder();
        foreach (var e in entries)
            sb.Append(e.Command.Length == 0 ? e.Key : e.Key + " " + e.Command).Append("\r\n");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>Names of the files directly in a binds folder (the backup subfolder is not included).</summary>
    public static List<string> ListFiles(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.txt").Select(f => Path.GetFileName(f)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();
}
