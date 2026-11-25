using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CityHelper.Models;

namespace CityHelper.Services;

/// <summary>Looks for regular City of Heroes installations (Homecoming, Rebirth...) so setup can offer them.</summary>
public static class InstallationDetector
{
    private static readonly string[] NameHints = { "homecoming", "rebirth", "city of heroes", "cityofheroes", "paragon" };

    private static readonly string[] ExeNames =
    {
        Path.Combine("bin", "win64", "CityOfHeroes.exe"),
        Path.Combine("bin", "CityOfHeroes.exe"),
        "CityOfHeroes.exe",
    };

    /// <summary>A folder looks like a game installation if it has the game executable or an accounts folder.</summary>
    public static bool LooksLikeInstallation(string dir) =>
        Directory.Exists(dir) &&
        (ExeNames.Any(e => File.Exists(Path.Combine(dir, e))) || Directory.Exists(Path.Combine(dir, "accounts")));

    public static List<Installation> Detect()
    {
        var found = new List<string>();

        void Consider(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            try
            {
                dir = Path.GetFullPath(dir.Trim().Trim('"')).TrimEnd('\\', '/');
                if (LooksLikeInstallation(dir) && !found.Contains(dir, StringComparer.OrdinalIgnoreCase))
                    found.Add(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // an unreadable or odd path is simply not an installation
            }
        }

        // Folders whose subfolders are candidates (e.g. C:\Games\Homecoming, C:\Games\Rebirth)
        void ConsiderChildren(string? parent, bool onlyHinted)
        {
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent)) return;
            try
            {
                foreach (var child in Directory.EnumerateDirectories(parent))
                {
                    var name = Path.GetFileName(child);
                    if (!onlyHinted || NameHints.Any(h => name.Contains(h, StringComparison.OrdinalIgnoreCase)))
                        Consider(child);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // unreadable folder: nothing to offer from it
            }
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var root = drive.RootDirectory.FullName;
            ConsiderChildren(Path.Combine(root, "Games"), onlyHinted: false);
            ConsiderChildren(root, onlyHinted: true);
            ConsiderChildren(Path.Combine(root, "Program Files"), onlyHinted: true);
            ConsiderChildren(Path.Combine(root, "Program Files (x86)"), onlyHinted: true);
        }
        ConsiderChildren(Path.Combine(local, "Programs"), onlyHinted: true);
        ConsiderChildren(local, onlyHinted: true);
        ConsiderChildren(Path.Combine(profile, "Games"), onlyHinted: false);

        foreach (var dir in InstallLocationsFromRegistry())
            Consider(dir);

        return found.Select(ToInstallation).ToList();
    }

    /// <summary>Name for a found folder: "Homecoming" for a Homecoming folder, else the folder's own name.</summary>
    public static Installation ToInstallation(string dir)
    {
        var folder = Path.GetFileName(dir.TrimEnd('\\', '/'));
        var name = string.IsNullOrWhiteSpace(folder) ? dir : folder;
        if (folder.Equals("homecoming", StringComparison.OrdinalIgnoreCase))
            name = "Homecoming";
        return new Installation(name, dir);
    }

    private static IEnumerable<string> InstallLocationsFromRegistry()
    {
        var result = new List<string>();
        if (!OperatingSystem.IsWindows())
            return result;

        string[] roots =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        };
        try
        {
            foreach (var hive in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
                foreach (var root in roots)
                {
                    using var key = hive.OpenSubKey(root);
                    if (key is null) continue;
                    foreach (var sub in key.GetSubKeyNames())
                    {
                        using var app = key.OpenSubKey(sub);
                        var display = app?.GetValue("DisplayName") as string;
                        if (display is null || !NameHints.Any(h => display.Contains(h, StringComparison.OrdinalIgnoreCase)))
                            continue;
                        if (app!.GetValue("InstallLocation") is string { Length: > 0 } location)
                            result.Add(location);
                    }
                }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // no registry access: the folder scan above still applies
        }
        return result;
    }
}
