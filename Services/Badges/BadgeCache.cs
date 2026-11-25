using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace CityHelper.Services.Badges;

public sealed record BadgeImportResult(int Badges, string? Error);

/// <summary>
/// The badge list (badges-all.json). A copy is built into the program. A newer badges-all.json can be imported
/// (Options > System); it is kept in Cache\Badges beside the settings and used from then on.
/// </summary>
public static class BadgeCache
{
    private const string ResourceName = "CityHelper.Badges.json";
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static string Directory => Path.Combine(IniStore.DataDirectory, "Cache", "Badges");
    public static string FilePath => Path.Combine(Directory, "badges-all.json");

    /// <summary>The imported list if there is one, otherwise the one built into the program.</summary>
    public static List<Badge> Load()
    {
        if (File.Exists(FilePath) && Read(File.ReadAllText(FilePath)) is { Count: > 0 } imported)
            return imported;
        return LoadBundled();
    }

    public static List<Badge> LoadBundled()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is null)
            return new List<Badge>();
        using var reader = new StreamReader(stream);
        return Read(reader.ReadToEnd()) ?? new List<Badge>();
    }

    /// <summary>The badges in the json text, or null if it isn't a badge list.</summary>
    public static List<Badge>? Read(string json)
    {
        try
        {
            var badges = JsonSerializer.Deserialize<List<Badge>>(json, Json);
            return badges is { Count: > 0 } && badges.All(b => b.InternalName.Length > 0 || b.HeroName.Length > 0) ? badges : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Describe()
    {
        var count = Load().Count;
        return File.Exists(FilePath)
            ? $"{count:N0} badges (imported {File.GetLastWriteTime(FilePath):yyyy-MM-dd HH:mm})."
            : $"{count:N0} badges (the copy built into the program).";
    }

    public const string DownloadUrl = "https://hcvault.cityofheroes.dev/badges/export.json";

    /// <summary>Raised after a new list has been kept, so an open Badge Lookup can read it.</summary>
    public static event Action? Updated;

    /// <summary>Downloads the current list from the Homecoming vault (one request) and keeps it as the list to use.</summary>
    public static async System.Threading.Tasks.Task<BadgeImportResult> DownloadAsync()
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CityHelper");
            var json = await http.GetStringAsync(DownloadUrl);
            var badges = Read(json);
            if (badges is null)
                return new BadgeImportResult(0, "The download was not a badge list.");
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, json);
            Updated?.Invoke();
            return new BadgeImportResult(badges.Count, null);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return new BadgeImportResult(0, ex.Message);
        }
    }

    /// <summary>Checks that the file is a badge list and keeps a copy of it as the list to use.</summary>
    public static BadgeImportResult Import(string path)
    {
        try
        {
            var badges = Read(File.ReadAllText(path));
            if (badges is null)
                return new BadgeImportResult(0, "That file is not a badge list (badges-all.json).");
            System.IO.Directory.CreateDirectory(Directory);
            File.Copy(path, FilePath, overwrite: true);
            return new BadgeImportResult(badges.Count, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new BadgeImportResult(0, ex.Message);
        }
    }
}
