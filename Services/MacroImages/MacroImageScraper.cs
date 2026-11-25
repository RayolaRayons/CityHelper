using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CityHelper.Services.MacroImages;

public sealed record MacroImageProgress(string Message, int Done, int Total);

public sealed record MacroImageSyncResult(int Entries, int Downloaded, int AlreadyCached, int Failed, bool Cancelled, string? Error);

/// <summary>
/// Reads the wiki's "Macro image (Slash Command)" page: every table row has a picture and a texture name, under
/// the page's headings (Alignments, Archetypes, ..., Powers with its A - C, D - E... groups). The list is saved
/// and the pictures downloaded into the cache. Running it again updates the list and fetches only missing pictures.
/// </summary>
public static class MacroImageScraper
{
    private const string Site = "https://homecoming.wiki";
    private const int Parallel = 6;

    // Headings (h2 = section, h3 = group) and tables, in page order
    private static readonly Regex Token = new(@"<h([23])[^>]*>(.*?)</h\1>|<table[^>]*>(.*?)</table>",
        RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Row = new(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Cell = new(@"<td[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ImageSrc = new(@"<img[^>]*?\ssrc=""([^""]+)""", RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>The list as the page gives it: section, group (blank when the section has no sub-headings), name and picture.</summary>
    public static List<MacroImageEntry> Parse(string html)
    {
        var entries = new List<MacroImageEntry>();
        string section = "", group = "";
        foreach (Match m in Token.Matches(html))
        {
            if (m.Groups[1].Success)
            {
                var title = WebUtility.HtmlDecode(Tags.Replace(m.Groups[2].Value, "")).Trim();
                if (m.Groups[1].Value == "2") { section = title; group = ""; }
                else group = title;
                continue;
            }

            foreach (Match row in Row.Matches(m.Groups[3].Value))
            {
                var cells = Cell.Matches(row.Groups[1].Value);
                if (cells.Count < 2)
                    continue;
                var src = ImageSrc.Match(cells[0].Groups[1].Value);
                var name = WebUtility.HtmlDecode(Tags.Replace(cells[1].Groups[1].Value, "")).Trim();
                if (!src.Success || name.Length == 0 || section.Length == 0)
                    continue;

                var url = WebUtility.HtmlDecode(src.Groups[1].Value);
                if (url.StartsWith("/"))
                    url = Site + url;
                entries.Add(new MacroImageEntry(section, group, name, FileNameFor(url), url));
            }
        }
        return entries;
    }

    /// <summary>The picture's own file name from its address, made safe for the file system.</summary>
    private static string FileNameFor(string url)
    {
        var last = Uri.UnescapeDataString(url.Split('?')[0].TrimEnd('/').Split('/')[^1]);
        foreach (var c in Path.GetInvalidFileNameChars())
            last = last.Replace(c, '_');
        return last;
    }

    public static async Task<MacroImageSyncResult> SyncAsync(IProgress<MacroImageProgress>? progress, CancellationToken cancel)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1";
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"CityHelper/{version} (macro image cache)");

            progress?.Report(new MacroImageProgress("Reading the wiki page...", 0, 0));
            var html = await http.GetStringAsync(MacroImageCache.SourceUrl, cancel);
            var entries = Parse(html);
            if (entries.Count == 0)
                return new MacroImageSyncResult(0, 0, 0, 0, false, "The wiki page had no macro image tables (its layout may have changed).");

            // The list is saved first, so it is usable even if the picture download is stopped
            Directory.CreateDirectory(MacroImageCache.ImageDirectory);
            MacroImageCache.Save(new MacroImageDatabase { Source = MacroImageCache.SourceUrl, Updated = DateTime.Now, Entries = entries });

            var todo = entries.GroupBy(e => e.File, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .Where(e => !IsCached(e)).ToList();
            int total = todo.Count, done = 0, failed = 0, downloaded = 0;
            int distinct = entries.Select(e => e.File).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            progress?.Report(new MacroImageProgress($"{entries.Count:N0} images listed; {total:N0} to download...", 0, total));

            using var gate = new SemaphoreSlim(Parallel);
            var tasks = todo.Select(async entry =>
            {
                await gate.WaitAsync(cancel);
                try
                {
                    bool ok = await DownloadAsync(http, entry, cancel);
                    if (ok) Interlocked.Increment(ref downloaded); else Interlocked.Increment(ref failed);
                }
                finally
                {
                    gate.Release();
                    int n = Interlocked.Increment(ref done);
                    progress?.Report(new MacroImageProgress($"Downloading images ({n:N0} of {total:N0})...", n, total));
                }
            }).ToList();
            await Task.WhenAll(tasks);

            return new MacroImageSyncResult(entries.Count, downloaded, distinct - total, failed, false, null);
        }
        catch (OperationCanceledException)
        {
            return new MacroImageSyncResult(MacroImageCache.Load()?.Entries.Count ?? 0, 0, 0, 0, true, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            return new MacroImageSyncResult(0, 0, 0, 0, false, ex.Message);
        }
    }

    private static bool IsCached(MacroImageEntry entry)
    {
        var info = new FileInfo(MacroImageCache.ImagePath(entry));
        return info.Exists && info.Length > 0;
    }

    private static async Task<bool> DownloadAsync(HttpClient http, MacroImageEntry entry, CancellationToken cancel)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var bytes = await http.GetByteArrayAsync(entry.Url, cancel);
                var path = MacroImageCache.ImagePath(entry);
                var temp = path + ".part";
                await File.WriteAllBytesAsync(temp, bytes, cancel);
                File.Move(temp, path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !cancel.IsCancellationRequested)
            {
                // one more try
            }
        }
        return false;
    }
}
