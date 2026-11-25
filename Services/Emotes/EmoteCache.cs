using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CityHelper.Views;
using Avalonia.Controls;

namespace CityHelper.Services.Emotes;

public sealed record EmoteReparseResult(int Emotes, string? Error, bool Cancelled = false);

/// <summary>
/// The emote list as JSON. A copy made from the wiki is built into the program (Assets\Emotes.json); reparsing the page
/// (Options > System) saves a newer copy in Cache\Emotes beside the settings, which is used from then on.
/// </summary>
public static class EmoteCache
{
    private const string ResourceName = "CityHelper.Emotes.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Directory => Path.Combine(IniStore.DataDirectory, "Cache", "Emotes");
    public static string FilePath => Path.Combine(Directory, "emotes.json");

    public static List<Emote> Load()
    {
        if (File.Exists(FilePath) && Read(File.ReadAllText(FilePath)) is { Count: > 0 } cached)
            return cached;
        return LoadBundled();
    }

    public static List<Emote> LoadBundled()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is null)
            return new List<Emote>();
        using var reader = new StreamReader(stream);
        return Read(reader.ReadToEnd()) ?? new List<Emote>();
    }

    private static List<Emote>? Read(string json)
    {
        try { return JsonSerializer.Deserialize<List<Emote>>(json, Json); }
        catch (JsonException) { return null; }
    }

    public static string Serialize(IEnumerable<Emote> emotes) => JsonSerializer.Serialize(emotes, Json);

    public static string Describe()
    {
        var count = Load().Count;
        return File.Exists(FilePath)
            ? $"{count:N0} emotes (reparsed from the wiki {File.GetLastWriteTime(FilePath):yyyy-MM-dd HH:mm})."
            : $"{count:N0} emotes (the copy built into the program).";
    }

    /// <summary>Reads the wiki's Emotes page again and saves the result as the new cache.</summary>
    public static async Task<EmoteReparseResult> ReparseAsync(CancellationToken cancel)
    {
        try
        {
            var emotes = await EmoteScraper.FetchAsync(cancel);
            if (emotes.Count == 0)
                return new EmoteReparseResult(0, "The wiki page had no emote tables (its layout may have changed); the cache was not changed.");

            System.IO.Directory.CreateDirectory(Directory);
            var temp = FilePath + ".tmp";
            await File.WriteAllTextAsync(temp, Serialize(emotes), cancel);
            File.Move(temp, FilePath, overwrite: true);
            return new EmoteReparseResult(emotes.Count, null);
        }
        catch (OperationCanceledException)
        {
            return new EmoteReparseResult(0, null, Cancelled: true);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or IOException or UnauthorizedAccessException)
        {
            return new EmoteReparseResult(0, ex.Message);
        }
    }

    /// <summary>Reparse with a progress box and a message at the end.</summary>
    public static async Task ReparseWithDialogsAsync(Window owner)
    {
        var result = await ProgressWindow.RunAsync(owner, "Emotes", (progress, cancel) =>
        {
            progress.Report(("Reading the wiki's Emotes page...", 0, 0));
            return ReparseAsync(cancel);
        });

        string message =
            result.Cancelled ? "Stopped; the emote list was not changed."
            : result.Error != null ? "The emotes could not be reparsed:\n" + result.Error
            : $"Reparsed the wiki page: {result.Emotes:N0} emotes saved.";
        await new MessageBox("Emotes", message).ShowDialog(owner);
    }
}
