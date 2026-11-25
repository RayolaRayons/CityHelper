using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CityHelper.Services.SlashCommands;

namespace CityHelper.Services.Emotes;

/// <summary>
/// Reads the wiki's Emotes page. Every emote table (normal, boombox, unlockable, thematic and costume change emotes)
/// has an Emote, an Aliases and a Description column; the columns are found by their headings, since the tables differ.
/// Only those three things are kept. An emote listed in more than one table is kept once.
/// </summary>
public static class EmoteScraper
{
    public const string PageUrl = "https://homecoming.wiki/wiki/Emotes";
    private const RegexOptions Opt = RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.IgnoreCase;

    private static readonly Regex Table = new(@"<table[^>]*>(.*?)</table>", Opt);
    private static readonly Regex Row = new(@"<tr[^>]*>(.*?)</tr>", Opt);
    private static readonly Regex HeaderCell = new(@"<th[^>]*>(.*?)</th>", Opt);
    private static readonly Regex Cell = new(@"<td[^>]*>(.*?)</td>", Opt);
    private static readonly Regex Hidden = new(@"<span style=""visibility:hidden"">.*?</span>", Opt);

    private static string Text(string html) => SlashCommandScraper.Text(Hidden.Replace(html, ""));

    public static List<Emote> Parse(string html)
    {
        var emotes = new List<Emote>();
        var byName = new Dictionary<string, Emote>(StringComparer.OrdinalIgnoreCase);

        foreach (Match table in Table.Matches(html))
        {
            int nameColumn = -1, aliasColumn = -1, descriptionColumn = -1;
            foreach (Match row in Row.Matches(table.Groups[1].Value))
            {
                var headers = HeaderCell.Matches(row.Groups[1].Value);
                if (headers.Count > 0 && !row.Groups[1].Value.Contains("<td", StringComparison.OrdinalIgnoreCase))
                {
                    // A heading row (a table may repeat it further down): find the columns we want
                    var names = headers.Select(h => Text(h.Groups[1].Value)).ToList();
                    nameColumn = names.FindIndex(n => n.Equals("Emote", StringComparison.OrdinalIgnoreCase));
                    aliasColumn = names.FindIndex(n => n.StartsWith("Alias", StringComparison.OrdinalIgnoreCase));
                    descriptionColumn = names.FindIndex(n => n.StartsWith("Description", StringComparison.OrdinalIgnoreCase));
                    continue;
                }
                if (nameColumn < 0 || descriptionColumn < 0)
                    continue;                                   // a table that is not an emote table

                var cells = Cell.Matches(row.Groups[1].Value);
                if (cells.Count <= Math.Max(nameColumn, descriptionColumn))
                    continue;

                var name = Text(cells[nameColumn].Groups[1].Value).TrimStart('/').Trim();
                if (name.Length == 0)
                    continue;
                var aliases = aliasColumn >= 0 && aliasColumn < cells.Count
                    ? Text(cells[aliasColumn].Groups[1].Value).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                          .Select(a => a.TrimStart('/').Trim()).Where(a => a.Length > 0).ToList()
                    : new List<string>();
                var description = SlashCommandScraper.StripSlashes(Text(cells[descriptionColumn].Groups[1].Value));

                if (byName.TryGetValue(name, out var existing))
                {
                    foreach (var alias in aliases.Where(a => !existing.Aliases.Contains(a, StringComparer.OrdinalIgnoreCase)))
                        existing.Aliases.Add(alias);
                    if (existing.Description.Length == 0)
                        existing.Description = description;
                    continue;
                }
                var emote = new Emote { Name = name, Aliases = aliases, Description = description };
                emotes.Add(emote);
                byName[name] = emote;
            }
        }
        return emotes;
    }

    /// <summary>Downloads the Emotes page and parses it (one request).</summary>
    public static async Task<List<Emote>> FetchAsync(CancellationToken cancel)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1";
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"CityHelper/{version} (emote cache)");
        return Parse(await http.GetStringAsync(PageUrl, cancel));
    }
}
