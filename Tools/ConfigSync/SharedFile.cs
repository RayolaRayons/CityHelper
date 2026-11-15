using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CityHelper.Tools.ConfigSync;

/// <summary>
/// File access that works while City of Heroes has a file open and is still writing it (the chat logs).
/// Plain File.ReadAll* refuse such files, and directory listings report a stale size / write time for them
/// until the game closes them, which is why a log that was just updated looked unchanged.
/// </summary>
internal static class SharedFile
{
    private const FileShare AllowOthers = FileShare.ReadWrite | FileShare.Delete;

    public static byte[] ReadAllBytes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, AllowOthers);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    public static string ReadAllText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, AllowOthers);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static string[] ReadAllLines(string path)
    {
        var lines = new List<string>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, AllowOthers);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
            lines.Add(line);
        return lines.ToArray();
    }

    /// <summary>Copies a file that may be open elsewhere, keeping its (current) last write time.</summary>
    public static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var time = LastWriteUtc(source);
        using (var from = new FileStream(source, FileMode.Open, FileAccess.Read, AllowOthers))
        using (var to = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
            from.CopyTo(to);
        File.SetLastWriteTimeUtc(destination, time);
    }

    /// <summary>
    /// Last write time (UTC) as the file system has it right now. Asking through an open handle (no read access, so
    /// it works even when the game has the file locked) gives the live value; the directory entry only catches up
    /// when the game closes the file.
    /// </summary>
    public static DateTime LastWriteUtc(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using var handle = CreateFileW(path, FileReadAttributes, 7 /* share read | write | delete */, IntPtr.Zero,
                                           OpenExisting, BackupSemantics, IntPtr.Zero);
            if (!handle.IsInvalid && GetFileTime(handle, IntPtr.Zero, IntPtr.Zero, out long write) && write > 0)
                return DateTime.FromFileTimeUtc(write);
        }
        return File.GetLastWriteTimeUtc(path);
    }

    private const uint FileReadAttributes = 0x0080;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
                                                     uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileTime(SafeFileHandle file, IntPtr creation, IntPtr lastAccess, out long lastWrite);
}
