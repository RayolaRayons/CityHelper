using System;
using System.Collections.Generic;
using System.IO;

namespace CityHelper.Tools.ConfigSync;

public sealed class CopyResult
{
    public int Copied { get; set; }
    public int Merged { get; set; }
    public int UpToDate { get; set; }
    public List<string> Errors { get; } = new();

    public void Add(CopyResult other)
    {
        Copied += other.Copied;
        Merged += other.Merged;
        UpToDate += other.UpToDate;
        Errors.AddRange(other.Errors);
    }
}

/// <summary>
/// Copies files and folders one way. Never deletes anything, and never overwrites a file that is not older
/// than the source (so a newer file on the destination side survives). Last-write times are preserved.
/// </summary>
public static class FileCopier
{
    // Filesystems differ in timestamp precision, so differences under this are "the same"
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Copies a file, or a folder's files (optionally only those matching <paramref name="pattern"/>, and optionally
    /// without subfolders), skipping <paramref name="excludedFolder"/>.
    /// </summary>
    public static CopyResult Copy(string source, string destination, string? excludedFolder = null,
                                  string? pattern = null, bool recursive = true)
    {
        var result = new CopyResult();
        try
        {
            if (File.Exists(source))
            {
                CopyFile(source, destination, result);
            }
            else if (Directory.Exists(source))
            {
                string? skip = excludedFolder is null ? null
                    : Path.GetFullPath(excludedFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

                Directory.CreateDirectory(destination);
                var options = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true };
                foreach (var file in new DirectoryInfo(source).EnumerateFiles(pattern ?? "*", options))
                {
                    if (skip != null && file.FullName.StartsWith(skip, StringComparison.OrdinalIgnoreCase))
                        continue;
                    CopyFile(file.FullName, Path.Combine(destination, Path.GetRelativePath(source, file.FullName)), result);
                }
            }
            // Source missing: nothing to copy
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            result.Errors.Add($"{source}: {ex.Message}");
        }
        return result;
    }

    private static void CopyFile(string source, string destination, CopyResult result)
    {
        try
        {
            if (File.Exists(destination) &&
                SharedFile.LastWriteUtc(source) - SharedFile.LastWriteUtc(destination) <= Tolerance)
            {
                result.UpToDate++;
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            SharedFile.Copy(source, destination);   // works on a log the game has open; keeps the source's last-write time
            result.Copied++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Errors.Add($"{destination}: {ex.Message}");
        }
    }
}
