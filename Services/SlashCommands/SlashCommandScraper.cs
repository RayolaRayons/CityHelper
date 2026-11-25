using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CityHelper.Services.SlashCommands;

public sealed record SlashCommandParseResult(List<SlashCommand> Commands, int SkippedNonWorking);

/// <summary>
/// Reads the wiki's "List of Slash Commands" main page. Every row of its letter tables has the command (and its syntax)
/// on the left and the description on the right. Rows that only say "Alias for /x" become aliases of x, and
/// "Alias: /y" / "(Also: e)" notes in a description add aliases too. Only the "Working Slash Commands" section is
/// kept; the non-functional and unknown ones are counted and left out.
/// </summary>
public static class SlashCommandScraper
{
    public const string PageUrl = "https://homecoming.wiki/wiki/List_of_Slash_Commands";
    private const string Site = "https://homecoming.wiki";
    private const RegexOptions Opt = RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.IgnoreCase;

    private static readonly Regex Heading = new(@"<h2[^>]*>(.*?)</h2>", Opt);
    private static readonly Regex Row = new(@"<tr[^>]*>(.*?)</tr>", Opt);
    private static readonly Regex Cell = new(@"<td[^>]*>(.*?)</td>", Opt);
    private static readonly Regex Bold = new(@"<b>(.*?)</b>", Opt);
    private static readonly Regex Href = new(@"href=""([^""]+)""", Opt);
    private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AliasFor = new(@"^Alias for\s+/?([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AliasNote = new(@"\s*Alias(?:es)?:\s*((?:/[A-Za-z0-9_]+(?:\s*(?:,|and)\s*)*)+)\.?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AlsoNote = new(@"\s*\(Also:\s*([^)]*)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex KeyAtEnd = new(
        @"\s*\(\s*((?:(?:Ctrl|Shift|Alt)\s*[-+]\s*)*(?:[A-Z]|F\d{1,2}|Tab|Enter|Esc|Space|Home|End|PageUp|PageDown|Insert|Delete|Backspace))\s*\)\.?\s*$",
        RegexOptions.Compiled);
    private static readonly Regex KeyBound = new(@"bound to the (\w+) key \(default\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Inline tags (links, bold) are dropped without leaving a space, so "/<a>fsaa</a>" reads "/fsaa"; a line break is a space
    private static readonly Regex LineBreak = new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static string Text(string html) =>
        Regex.Replace(WebUtility.HtmlDecode(Tags.Replace(LineBreak.Replace(html, " "), "")).Replace(' ', ' '), @"\s+", " ").Trim();

    // ------------------------------------------------------------------ the page

    public static SlashCommandParseResult Parse(string html)
    {
        var sections = Heading.Matches(html).Select(m => (m.Index, Title: Text(m.Groups[1].Value))).ToList();
        string SectionAt(int position) => sections.LastOrDefault(s => s.Index <= position).Title ?? "";

        var commands = new List<SlashCommand>();
        var byName = new Dictionary<string, SlashCommand>(StringComparer.OrdinalIgnoreCase);
        var aliasRows = new List<(string Alias, string Target, string Description, string Url)>();
        int skipped = 0;

        foreach (Match row in Row.Matches(html))
        {
            var cells = Cell.Matches(row.Groups[1].Value);
            if (cells.Count != 2)
                continue;
            var left = cells[0].Groups[1].Value;
            var bold = Bold.Match(left);
            if (!bold.Success)
                continue;

            var name = Text(bold.Groups[1].Value).TrimStart('/').Trim();
            if (name.Length == 0 || name.Contains(' '))
                continue;
            if (!SectionAt(row.Index).StartsWith("Working", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            var description = Text(cells[1].Groups[1].Value);
            var syntax = Text(left[(bold.Index + bold.Length)..]);
            var url = UrlFor(name, Href.Match(bold.Groups[1].Value) is { Success: true } h ? h.Groups[1].Value : null);

            if (AliasFor.Match(description) is { Success: true } alias)
            {
                aliasRows.Add((name, alias.Groups[1].Value, description, url));
                continue;
            }
            Add(commands, byName, Build(name, syntax, description, url));
        }

        // Rows that were only an alias belong to the command they point at (or stay as a command if it isn't listed)
        foreach (var (alias, target, description, url) in aliasRows)
        {
            if (byName.TryGetValue(target, out var command))
                AddAlias(command, alias);
            else
                Add(commands, byName, Build(alias, "", description, url));
        }
        return new SlashCommandParseResult(commands, skipped);
    }

    /// <summary>Adds a command; a name the page lists twice is kept once, with whatever the second row adds.</summary>
    private static void Add(List<SlashCommand> commands, Dictionary<string, SlashCommand> byName, SlashCommand command)
    {
        if (!byName.TryGetValue(command.Name, out var first))
        {
            commands.Add(command);
            byName[command.Name] = command;
            return;
        }
        foreach (var alias in command.Aliases)
            AddAlias(first, alias);
        if (first.Description.Length == 0) first.Description = command.Description;
        first.DefaultKey ??= command.DefaultKey;
        if (first.RawSyntax is null) { first.RawSyntax = command.RawSyntax; first.Params = command.Params; }
    }

    private static string UrlFor(string name, string? href)
    {
        if (!string.IsNullOrEmpty(href) && href.StartsWith("/wiki/") && !href.Contains("redlink"))
            return Site + WebUtility.HtmlDecode(href);
        var title = char.ToUpperInvariant(name[0]) + name[1..];
        return $"{Site}/wiki/{title}_(Slash_Command)";
    }

    private static void AddAlias(SlashCommand command, string alias)
    {
        alias = alias.Trim().TrimStart('/');
        if (alias.Length > 0 && !alias.Equals(command.Name, StringComparison.OrdinalIgnoreCase)
            && !command.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
            command.Aliases.Add(alias);
    }

    // ------------------------------------------------------------------ one command

    internal static SlashCommand Build(string name, string syntax, string description, string url)
    {
        var command = new SlashCommand { Name = name, WikiUrl = url, RawSyntax = syntax.Length == 0 ? null : syntax };

        foreach (Match m in AliasNote.Matches(description))
            foreach (Match a in Regex.Matches(m.Groups[1].Value, @"/([A-Za-z0-9_]+)"))
                AddAlias(command, a.Groups[1].Value);
        description = AliasNote.Replace(description, "");

        if (AlsoNote.Match(description) is { Success: true } also)
        {
            foreach (var part in Regex.Split(also.Groups[1].Value, @"\s*(?:,|\bor\b|\band\b)\s*"))
                if (Regex.IsMatch(part.Trim(), @"^[A-Za-z0-9_]+$"))
                    AddAlias(command, part);
            description = AlsoNote.Replace(description, "");
        }

        if (KeyBound.Match(description) is { Success: true } bound)
            command.DefaultKey = bound.Groups[1].Value;
        if (KeyAtEnd.Match(description) is { Success: true } key)
        {
            command.DefaultKey ??= key.Groups[1].Value;
            description = KeyAtEnd.Replace(description, "");
        }

        command.Description = StripSlashes(description.Trim());
        command.Params = ParseSyntax(syntax, command.Description);
        command.Toggle = SlashCommandPageParser.ToggleFor(command);
        return command;
    }

    // ------------------------------------------------------------------ the syntax text

    private static readonly Regex Token = new(@"\[[^\]]*\]|<[^>]*>|[^\s\[\]<>]+", RegexOptions.Compiled);
    private static readonly Regex Range = new(@"^(-?\d+(?:\.\d+)?)\s*-\s*(-?\d+(?:\.\d+)?)$", RegexOptions.Compiled);
    private static readonly Regex Unit = new(@"\b(degrees|seconds|minutes|feet|pixels|percent)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly HashSet<string> Words = new(StringComparer.OrdinalIgnoreCase) { "or", "and", "to", "the", "a", "an" };

    private static string TypeOf(string word) => word.ToLowerInvariant() switch
    {
        "int" or "integer" or "number" or "count" or "amount" => "int",
        "float" or "decimal" => "float",
        _ => "string",
    };

    /// <summary>
    /// Turns the syntax text into parameters: [a-b] is an optional number range, [word] an optional parameter,
    /// [a|b] a choice, &lt;int&gt; a typed value, and a bare word a required parameter. Text that is not a parameter
    /// ("+, ++, or") is left out; the full text is always kept in rawSyntax.
    /// </summary>
    internal static List<SlashCommandParam> ParseSyntax(string syntax, string description)
    {
        var list = new List<SlashCommandParam>();
        foreach (Match m in Token.Matches(syntax))
        {
            var token = m.Value;
            if (token[0] == '[')
            {
                var inner = token[1..^1].Trim();
                var range = Range.Match(inner);
                if (range.Success)
                {
                    double min = double.Parse(range.Groups[1].Value, CultureInfo.InvariantCulture);
                    double max = double.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture);
                    bool whole = !range.Groups[1].Value.Contains('.') && !range.Groups[2].Value.Contains('.');
                    var unit = Unit.Match(description);
                    list.Add(new SlashCommandParam
                    {
                        Name = unit.Success ? unit.Value.ToLowerInvariant() : "value",
                        Type = whole ? "int" : "float", Min = min, Max = max, Optional = true,
                    });
                }
                else if (inner.Contains('|'))
                {
                    var values = inner.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
                    list.Add(new SlashCommandParam { Name = "option", Type = "enum", Optional = true, Values = values });
                }
                else if (Regex.IsMatch(inner, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                    list.Add(new SlashCommandParam { Name = inner, Type = TypeOf(inner), Optional = true });
            }
            else if (token[0] == '<')
            {
                var inner = token[1..^1].Trim();
                if (inner.Length > 0)
                    list.Add(new SlashCommandParam { Name = "arg" + (list.Count + 1), Type = TypeOf(inner) });
            }
            else if (Regex.IsMatch(token, @"^[A-Za-z_][A-Za-z0-9_]*$") && !Words.Contains(token))
                list.Add(new SlashCommandParam { Name = token, Type = TypeOf(token) });
        }
        return list;
    }

    private static readonly Regex SlashBeforeCommand = new(@"(?<![\w/])/(?=[+\-]*[A-Za-z_])", RegexOptions.Compiled);

    /// <summary>
    /// The saved text never has the slash in front of a command ("see /camreset" is saved as "see camreset"): every command is
    /// assumed to have it. A slash inside a word or a link (and/or, https://) is left alone.
    /// </summary>
    internal static string StripSlashes(string text) => SlashBeforeCommand.Replace(text, "");

    /// <summary>The lines of a preformatted block, decoded.</summary>
    internal static IEnumerable<string> DecodeLines(string html) =>
        WebUtility.HtmlDecode(Tags.Replace(html, "")).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);

    // ------------------------------------------------------------------ fetching

    /// <summary>
    /// Downloads the list page and parses it, then reads each command's own page for its details, numbers and examples.
    /// Only those pages are read (one level below the list); a page that can't be read leaves that command with the list's
    /// information.
    /// </summary>
    public static async Task<SlashCommandParseResult> FetchAsync(IProgress<(string Message, int Done, int Total)>? progress, CancellationToken cancel)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1";
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"CityHelper/{version} (slash command cache)");

        progress?.Report(("Reading the List of Slash Commands...", 0, 0));
        var parsed = Parse(await http.GetStringAsync(PageUrl, cancel));

        // One page at a time with a pause between them: the wiki (behind Cloudflare) answers "429 Too Many Requests" to anything faster
        int total = parsed.Commands.Count, done = 0;
        var pace = new Pace(PageDelay);
        foreach (var command in parsed.Commands)
        {
            cancel.ThrowIfCancellationRequested();
            var html = await GetPageAsync(http, command.WikiUrl, pace, cancel);
            if (html != null)
                SlashCommandPageParser.Apply(command, html);       // else: no page for this command, it keeps what the list says
            done++;
            progress?.Report(($"Reading the command pages ({done:N0} of {total:N0})...", done, total));
            await Task.Delay(pace.Delay, cancel);
        }
        return parsed;
    }

    /// <summary>The pause between requests: it grows when the wiki says "too many requests" and shrinks back while all goes well.</summary>
    private sealed class Pace
    {
        private readonly TimeSpan _floor;
        public Pace(TimeSpan floor) { _floor = floor; Delay = floor; }
        public TimeSpan Delay { get; private set; }
        public void Slower() => Delay = TimeSpan.FromMilliseconds(Math.Min(Delay.TotalMilliseconds * 2, 8000));
        public void Faster() => Delay = TimeSpan.FromMilliseconds(Math.Max(_floor.TotalMilliseconds, Delay.TotalMilliseconds * 0.9));
    }

    /// <summary>The pause between two page requests.</summary>
    public static TimeSpan PageDelay { get; set; } = TimeSpan.FromMilliseconds(600);

    /// <summary>The page's html, or null if there is none (404...). A "too many requests" answer is waited out, then retried.</summary>
    private static async Task<string?> GetPageAsync(HttpClient http, string url, Pace pace, CancellationToken cancel)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            using var response = await http.GetAsync(url, cancel);
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                pace.Slower();
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5 * (attempt + 1));
                await Task.Delay(wait > TimeSpan.FromMinutes(2) ? TimeSpan.FromMinutes(2) : wait, cancel);
                continue;
            }
            pace.Faster();
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancel) : null;
        }
        return null;
    }

    /// <summary>The list page only (no command pages).</summary>
    public static async Task<SlashCommandParseResult> FetchListAsync(CancellationToken cancel)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CityHelper (slash command cache)");
        return Parse(await http.GetStringAsync(PageUrl, cancel));
    }
}
