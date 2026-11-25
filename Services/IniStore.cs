using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CityHelper.Services;

/// <summary>
/// The one ini file for all of CityHelper's options and settings (CityHelper.ini in the application directory).
/// Each part (a tool, the installation list...) owns the sections whose name starts with its prefix, for example
/// [LogManager.Options] or [ConfigSync.Environment1]; a part with a single unnamed section uses just [Prefix].
/// Parts read and write their own text in their familiar layout (with the prefix stripped), so they never see
/// each other's sections. A part with no sections yet is read from its old stand-alone ini file, if one exists,
/// which migrates existing settings the first time the part saves.
/// </summary>
public static class IniStore
{
    public const string FileName = "CityHelper.ini";

    /// <summary>Marker file next to the exe that keeps settings beside the exe (portable / standalone use).</summary>
    public const string PortableMarker = "portable.txt";

    /// <summary>
    /// Where settings live. Standalone: beside the exe, when a portable.txt (or an existing CityHelper.ini) is there.
    /// Installed (for example under Program Files, which isn't writable): %APPDATA%\CityHelper.
    /// </summary>
    public static string DataDirectory { get; } = ChooseDataDirectory();

    public static string FilePath => Path.Combine(DataDirectory, FileName);

    private static string ChooseDataDirectory()
    {
        var exeDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, PortableMarker)) || File.Exists(Path.Combine(exeDir, FileName)))
            return exeDir;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return string.IsNullOrEmpty(appData) ? exeDir : Path.Combine(appData, "CityHelper");
    }

    /// <summary>
    /// The lines of the part's sections, headers without the prefix; null if there are none (and no legacy file).
    /// An explicit <paramref name="path"/> bypasses the shared file and reads that file as it is.
    /// </summary>
    public static string[]? ReadLines(string prefix, string legacyFileName, string? path = null)
    {
        if (path != null)
            return File.Exists(path) ? File.ReadAllLines(path) : null;

        if (File.Exists(FilePath))
        {
            var mine = new List<string>();
            bool inMine = false, found = false;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                if (TryHeader(line, out var name))
                {
                    inMine = Owns(prefix, name);
                    if (inMine)
                    {
                        found = true;
                        if (name.Length > prefix.Length)      // "[Prefix.Section]" becomes "[Section]"; "[Prefix]" has no header
                            mine.Add("[" + name[(prefix.Length + 1)..] + "]");
                    }
                    continue;
                }
                if (inMine)
                    mine.Add(line);
            }
            if (found)
                return mine.ToArray();
        }

        var legacy = Path.Combine(AppContext.BaseDirectory, legacyFileName);
        return File.Exists(legacy) ? File.ReadAllLines(legacy) : null;
    }

    /// <summary>Replaces the part's sections in the shared file with <paramref name="text"/> (in the part's own layout).</summary>
    public static void WriteText(string prefix, string text, string? path = null)
    {
        if (path != null)
        {
            if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } folder)
                Directory.CreateDirectory(folder);
            File.WriteAllText(path, text);
            return;
        }

        // Header lines get the prefix; text before the first header becomes the part's plain "[Prefix]" section
        var mine = new List<string>();
        bool headed = false;
        foreach (var line in text.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (TryHeader(line, out var name))
            {
                headed = true;
                mine.Add("[" + prefix + "." + name + "]");
            }
            else
            {
                if (!headed && line.Trim().Length > 0)
                {
                    mine.Add("[" + prefix + "]");
                    headed = true;
                }
                mine.Add(line);
            }
        }
        while (mine.Count > 0 && mine[^1].Trim().Length == 0)
            mine.RemoveAt(mine.Count - 1);

        // Keep every other part's sections as they are; this part's block goes where its old one was
        var result = new List<string>();
        bool inMine = false, inserted = false;
        if (File.Exists(FilePath))
        {
            foreach (var line in File.ReadAllLines(FilePath))
            {
                if (TryHeader(line, out var name))
                {
                    inMine = Owns(prefix, name);
                    if (inMine && !inserted)
                    {
                        AddBlock(result, mine);
                        inserted = true;
                    }
                }
                if (!inMine)
                    result.Add(line);
            }
        }
        if (!inserted)
            AddBlock(result, mine);

        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(FilePath, string.Join("\r\n", result).TrimEnd() + "\r\n", new UTF8Encoding(false));
    }

    private static void AddBlock(List<string> result, List<string> block)
    {
        if (block.Count == 0)
            return;
        if (result.Count > 0 && result[^1].Trim().Length != 0)
            result.Add("");
        result.AddRange(block);
        result.Add("");
    }

    private static bool Owns(string prefix, string sectionName) =>
        sectionName.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
        sectionName.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase);

    private static bool TryHeader(string line, out string name)
    {
        var t = line.Trim();
        if (t.Length >= 2 && t[0] == '[' && t[^1] == ']')
        {
            name = t[1..^1].Trim();
            return true;
        }
        name = "";
        return false;
    }
}
