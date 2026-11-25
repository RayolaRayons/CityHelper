using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace CityHelper.Services.MacroImages;

/// <summary>One macro image of the wiki's list: where it is listed, its texture name (what /macro_image and Icon take) and its picture.</summary>
public sealed record MacroImageEntry(string Section, string Group, string Name, string File, string Url);

/// <summary>The local copy of the macro image list: a database (json) and the pictures, under Cache\MacroImages.</summary>
public sealed class MacroImageDatabase
{
    public string Source { get; set; } = "";
    public DateTime Updated { get; set; }
    public List<MacroImageEntry> Entries { get; set; } = new();
}

/// <summary>Where the cache lives and how it is read and written.</summary>
public static class MacroImageCache
{
    public const string SourceUrl = "https://homecoming.wiki/wiki/Macro_image_(Slash_Command)";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Cache\MacroImages beside the settings file (the program folder when standalone, the user profile when installed).</summary>
    public static string Directory => Path.Combine(IniStore.DataDirectory, "Cache", "MacroImages");

    public static string ImageDirectory => Path.Combine(Directory, "Images");

    public static string DatabasePath => Path.Combine(Directory, "macroimages.json");

    private const string BuiltInResource = "CityHelper.MacroImages.zip";
    private static readonly object SeedLock = new();

    /// <summary>
    /// A copy of the list and its pictures is built into the program. If there is no cache yet, it is unpacked into
    /// Cache\MacroImages, so the lookup works at once and nothing has to be downloaded (Sync still updates it).
    /// Returns true if it unpacked something. A cache that exists is never touched.
    /// </summary>
    public static bool SeedIfMissing()
    {
        lock (SeedLock)
        {
            if (IsConfigured)
                return false;
            using var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(BuiltInResource);
            if (stream is null)
                return false;

            try
            {
                // Unpack beside the target and move it into place, so a stopped unpack leaves no half cache behind
                var temp = Directory + ".seeding";
                if (System.IO.Directory.Exists(temp))
                    System.IO.Directory.Delete(temp, true);
                System.IO.Directory.CreateDirectory(temp);

                var root = Path.GetFullPath(temp).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read))
                    foreach (var entry in zip.Entries.Where(e => e.Name.Length > 0))
                    {
                        var target = Path.GetFullPath(Path.Combine(temp, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            continue;
                        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        entry.ExtractToFile(target, overwrite: true);
                    }

                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(Directory)!);
                if (System.IO.Directory.Exists(Directory))
                    System.IO.Directory.Delete(Directory, true);          // a folder without a list: only leftovers
                System.IO.Directory.Move(temp, Directory);
                return IsConfigured;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return false;                                              // the person is simply asked to download instead
            }
        }
    }

    /// <summary>True once a list has been downloaded (or unpacked from the copy built into the program).</summary>
    public static bool IsConfigured => System.IO.File.Exists(DatabasePath);

    public static string ImagePath(MacroImageEntry entry) => Path.Combine(ImageDirectory, entry.File);

    /// <summary>The cached list, or null if there is none (or it can't be read).</summary>
    public static MacroImageDatabase? Load()
    {
        try
        {
            return IsConfigured
                ? JsonSerializer.Deserialize<MacroImageDatabase>(System.IO.File.ReadAllText(DatabasePath), Json)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Save(MacroImageDatabase database)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var temp = DatabasePath + ".tmp";
        System.IO.File.WriteAllText(temp, JsonSerializer.Serialize(database, Json));
        System.IO.File.Move(temp, DatabasePath, overwrite: true);
    }

    /// <summary>"3,278 images, updated 2026-11-20 17:45", or a note that nothing is cached yet.</summary>
    public static string Describe()
    {
        var db = Load();
        if (db is null)
            return "No macro images are cached yet.";
        int pictures = db.Entries.Count(e => System.IO.File.Exists(ImagePath(e)));
        return $"{db.Entries.Count:N0} images listed, {pictures:N0} downloaded, updated {db.Updated:yyyy-MM-dd HH:mm}.";
    }
}
