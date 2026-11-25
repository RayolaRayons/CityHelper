using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CityHelper.Services.SlashCommands;

public sealed record SlashCommandReparseResult(int Commands, int Skipped, string? Error, bool Cancelled = false);

/// <summary>
/// The slash command list as JSON. A copy made from the wiki is built into the program (Assets\SlashCommands.json),
/// so nothing has to be parsed at run time. Reparsing the wiki page (Options > System) saves a newer copy in
/// Cache\SlashCommands beside the settings, which is used from then on.
/// </summary>
public static class SlashCommandCache
{
    private const string ResourceName = "CityHelper.SlashCommands.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Directory => Path.Combine(IniStore.DataDirectory, "Cache", "SlashCommands");
    public static string FilePath => Path.Combine(Directory, "slashcommands.json");

    /// <summary>The commands: the reparsed copy if there is one, otherwise the one built into the program.</summary>
    public static List<SlashCommand> Load()
    {
        if (File.Exists(FilePath) && Read(File.ReadAllText(FilePath)) is { Count: > 0 } cached)
            return cached;
        return LoadBundled();
    }

    public static List<SlashCommand> LoadBundled()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is null)
            return new List<SlashCommand>();
        using var reader = new StreamReader(stream);
        return Read(reader.ReadToEnd()) ?? new List<SlashCommand>();
    }

    private static List<SlashCommand>? Read(string json)
    {
        try { return JsonSerializer.Deserialize<List<SlashCommand>>(json, Json); }
        catch (JsonException) { return null; }
    }

    public static string Serialize(IEnumerable<SlashCommand> commands) => JsonSerializer.Serialize(commands, Json);

    /// <summary>"620 commands (reparsed from the wiki 2026-11-24 17:30)" or "... (built into the program)".</summary>
    public static string Describe()
    {
        var count = Load().Count;
        return File.Exists(FilePath)
            ? $"{count:N0} commands (reparsed from the wiki {File.GetLastWriteTime(FilePath):yyyy-MM-dd HH:mm})."
            : $"{count:N0} commands (the copy built into the program).";
    }

    /// <summary>Reads the wiki list and every command page again and saves the result as the new cache.</summary>
    public static async Task<SlashCommandReparseResult> ReparseAsync(IProgress<(string Message, int Done, int Total)>? progress, CancellationToken cancel)
    {
        try
        {
            var parsed = await SlashCommandScraper.FetchAsync(progress, cancel);
            if (parsed.Commands.Count == 0)
                return new SlashCommandReparseResult(0, 0, "The wiki page had no command tables (its layout may have changed); the cache was not changed.");

            System.IO.Directory.CreateDirectory(Directory);
            var temp = FilePath + ".tmp";
            await File.WriteAllTextAsync(temp, Serialize(parsed.Commands), cancel);
            File.Move(temp, FilePath, overwrite: true);
            return new SlashCommandReparseResult(parsed.Commands.Count, parsed.SkippedNonWorking, null);
        }
        catch (OperationCanceledException)
        {
            return new SlashCommandReparseResult(0, 0, null, Cancelled: true);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or IOException or UnauthorizedAccessException)
        {
            return new SlashCommandReparseResult(0, 0, ex.Message);
        }
    }
}
