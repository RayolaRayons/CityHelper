using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CityHelper.Tools.ChatWatcher;

/// <summary>
/// Follows the newest chatlog*.txt in a folder and returns the lines added since the last poll. The first file
/// is read from its current end (old chat doesn't flood the list); a file that appears later (a new day) is read
/// from its start.
/// </summary>
public sealed class LogTailer
{
    private static readonly TimeSpan SwitchCheckInterval = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, long> _positions = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastSwitchCheck = DateTime.MinValue;

    public string? Directory { get; private set; }
    public string? CurrentFile { get; private set; }

    /// <summary>
    /// The character logged in, from the last "Welcome to City of Heroes, &lt;Name&gt;!" line (null if none was found).
    /// It is looked up when a log file is picked and updated whenever a new greeting is read.
    /// </summary>
    public string? CurrentCharacter { get; private set; }

    // How many of the newest logs are searched for the greeting when the newest has none (e.g. a new day's file)
    private const int MaxFilesScanned = 5;

    public void SetDirectory(string? directory)
    {
        Directory = directory;
        CurrentFile = null;
        CurrentCharacter = null;
        _positions.Clear();
        _lastSwitchCheck = DateTime.MinValue;
    }

    /// <summary>Forces a look for a newer log file on the next poll.</summary>
    public void CheckForNewerFile() => _lastSwitchCheck = DateTime.MinValue;

    /// <summary>New complete lines since the last call (empty if none, or if the folder or file is unavailable).</summary>
    public List<string> Poll()
    {
        var lines = new List<string>();
        if (Directory is null || !System.IO.Directory.Exists(Directory))
            return lines;

        try
        {
            if (DateTime.UtcNow - _lastSwitchCheck >= SwitchCheckInterval)
            {
                _lastSwitchCheck = DateTime.UtcNow;
                var files = new DirectoryInfo(Directory).EnumerateFiles("chatlog*.txt", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(f => f.LastWriteTimeUtc).ToList();
                var newest = files.FirstOrDefault();
                if (newest != null && !newest.FullName.Equals(CurrentFile, StringComparison.OrdinalIgnoreCase))
                {
                    bool first = CurrentFile is null;
                    CurrentFile = newest.FullName;
                    if (!_positions.ContainsKey(CurrentFile))
                        _positions[CurrentFile] = first ? newest.Length : 0;
                    DetectCharacter(files);
                }
            }

            if (CurrentFile is null || !File.Exists(CurrentFile))
                return lines;

            long length = new FileInfo(CurrentFile).Length;
            long position = _positions.GetValueOrDefault(CurrentFile);
            if (length < position)
                position = 0;                                   // truncated / restarted
            if (length == position)
            {
                _positions[CurrentFile] = position;
                return lines;
            }

            // The game keeps the file open for writing, so share it
            using var stream = new FileStream(CurrentFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(position, SeekOrigin.Begin);
            var buffer = new byte[length - position];
            int read = stream.Read(buffer, 0, buffer.Length);

            // Only consume whole lines; a half-written last line waits for the next poll
            int lastNewline = Array.LastIndexOf(buffer, (byte)'\n', read - 1);
            if (lastNewline < 0)
                return lines;

            _positions[CurrentFile] = position + lastNewline + 1;
            var text = Encoding.UTF8.GetString(buffer, 0, lastNewline + 1);
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (trimmed.Trim().Length == 0)
                    continue;
                lines.Add(trimmed);
                if (ChatLogParser.ParseWelcome(trimmed) is { } name)
                    CurrentCharacter = name;        // someone logged in while we were watching
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file is busy or just rotated; try again next poll
        }
        return lines;
    }

    /// <summary>Takes the character from the last greeting in the newest log that has one. Keeps the previous one if none is found.</summary>
    private void DetectCharacter(IEnumerable<FileInfo> filesNewestFirst)
    {
        try
        {
            foreach (var file in filesNewestFirst.Take(MaxFilesScanned))
            {
                if (LastGreeting(file.FullName) is { } name)
                {
                    CurrentCharacter = name;
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable right now; the greeting is also picked up from new lines
        }
    }

    private static string? LastGreeting(string path)
    {
        string? last = null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            // Cheap test first: most lines are not the greeting
            if (line.Contains("Welcome to City of Heroes,", StringComparison.Ordinal) && ChatLogParser.ParseWelcome(line) is { } name)
                last = name;
        }
        return last;
    }
}
