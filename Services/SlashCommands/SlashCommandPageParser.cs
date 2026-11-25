using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CityHelper.Services.SlashCommands;

/// <summary>
/// Reads one command's own wiki page (one level below the list; no links on it are followed): the explanation, the
/// examples, and the numbers the command's parameter can take ("0 = off", "Default = 5"). Page text is added to the
/// command; names, aliases, the toggle and the examples are kept without the leading slash.
/// </summary>
public static class SlashCommandPageParser
{
    private const RegexOptions Opt = RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.IgnoreCase;

    // Headings, paragraphs, definition and list items, and preformatted examples, in page order
    private static readonly Regex Block = new(@"<h2[^>]*>(.*?)</h2>|<p>(.*?)</p>|<dd>(.*?)</dd>|<li>(.*?)</li>|<pre[^>]*>(.*?)</pre>", Opt);
    private static readonly Regex Number = new(@"-?\d+(?:\.\d+)?", RegexOptions.Compiled);

    private static readonly Regex ValueThenMeaning = new(@"^(-?\d+(?:\.\d+)?)\s*(?:=|:|\u2013|\u2014|-)\s+(\S.*)$", RegexOptions.Compiled);
    private static readonly Regex LabelEqualsNumber = new(@"([A-Za-z][A-Za-z '\-]{0,40}?)\s*=\s*(-?\d+(?:\.\d+)?)(?!\d)(?!\.\d)", RegexOptions.Compiled);
    private static readonly Regex DefaultIs = new(@"\bDefaults?\b[^.\d=:\-]{0,20}?(?:\bto\b|\bis\b|=|:)\s*(-?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Limit = new(@"\b(maximum|minimum|limited to|limit of)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NumberList = new(@"\b(?:settings|values|options|levels)\b[^.\d]{0,25}?\b(?:are|include)\b[^\d.]{0,12}((?:-?\d+(?:\.\d+)?(?:\s*,\s*(?:and\s+)?|\s+and\s+)?)+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MeantAsNumber = new(@"\bmeasured in (?:feet|degrees|seconds|minutes|pixels|percent|meters|units)\b|\bany number\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RangeText = new(@"\b(?:from|between|range of)\s+(-?\d+(?:\.\d+)?)\s*(?:to|and|-)\s*(-?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Adds what the command's page says to the command.</summary>
    public static void Apply(SlashCommand command, string html)
    {
        // The page's own text only: from the content to the first navigation box (the link boxes at the bottom)
        int start = html.IndexOf("id=\"mw-content-text\"", StringComparison.Ordinal);
        if (start < 0)
            return;
        var content = html[start..];
        int nav = content.IndexOf("<div role=\"navigation\"", StringComparison.Ordinal);
        if (nav > 0)
            content = content[..nav];

        var lines = new List<string>();
        var examples = new List<string>();
        bool inExamples = false;

        foreach (Match m in Block.Matches(content))
        {
            if (m.Groups[1].Success)
            {
                var heading = SlashCommandScraper.Text(m.Groups[1].Value);
                inExamples = heading.StartsWith("Example", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (m.Groups[5].Success)
            {
                foreach (var raw in SlashCommandScraper.DecodeLines(m.Groups[5].Value))
                {
                    var example = raw.Trim().TrimStart('/').Trim();
                    if (example.Length > 0 && !examples.Contains(example))
                        examples.Add(example);
                }
                continue;
            }

            var text = SlashCommandScraper.Text(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value);
            if (!inExamples && text.Length > 0)
                lines.Add(SlashCommandScraper.StripSlashes(text));
        }

        command.Details = lines.Count == 0 ? null : string.Join("\n", lines);
        command.Examples = examples.Count == 0 ? null : examples;
        ReadNumbers(command, lines);
        command.Toggle = ToggleFor(command);
    }

    // ------------------------------------------------------------------ the numbers

    /// <summary>Finds "0 = off" style values, "Default = 5" and a stated range, and gives them to the command's number parameter.</summary>
    internal static void ReadNumbers(SlashCommand command, IEnumerable<string> lines)
    {
        lines = lines.ToList();

        // A single parameter the page measures in feet, degrees... or calls "any number" is a number, whatever the syntax text said
        if (command.Params.Count == 1 && command.Params[0].Type == "string" && lines.Any(l => MeantAsNumber.IsMatch(l)))
            command.Params[0].Type = "float";

        var numeric = command.Params.Where(p => p.Type is "int" or "float").ToList();
        if (numeric.Count == 0)
            return;

        foreach (var line in lines)
        {
            // Which parameter the line is about: the one it names, otherwise the first number parameter
            var target = numeric.FirstOrDefault(p => numeric.Count > 1 && Regex.IsMatch(line, $@"\b{Regex.Escape(p.Name)}\b", RegexOptions.IgnoreCase)) ?? numeric[0];

            var found = new List<(double Value, string Meaning)>();
            if (ValueThenMeaning.Match(line) is { Success: true } vm)
                found.Add((Parse(vm.Groups[1].Value), vm.Groups[2].Value.Trim().TrimEnd('.')));
            else
                foreach (Match m in LabelEqualsNumber.Matches(line))
                    found.Add((Parse(m.Groups[2].Value), m.Groups[1].Value.Trim()));

            // "Typical settings are 0, 2, 4, and 8" and "limited to a maximum of 120 feet"
            if (NumberList.Match(line) is { Success: true } list)
                foreach (Match n in Number.Matches(list.Groups[1].Value))
                    found.Add((Parse(n.Value), "typical setting"));
            foreach (var sentence in Regex.Split(line, @"(?<=[.!?])\s+"))
                if (Limit.IsMatch(sentence) && !Regex.IsMatch(sentence, @"\bIssue\b", RegexOptions.IgnoreCase) && Number.Match(sentence) is { Success: true } first)
                    found.Add((Parse(first.Value), sentence.Trim().TrimEnd('.')));

            foreach (var (value, meaning) in found)
            {
                if (meaning.Equals("default", StringComparison.OrdinalIgnoreCase))
                {
                    target.Default ??= value;            // "Default = 0." is the default, not another number
                    continue;
                }
                if ((target.Numbers?.Count ?? 0) >= 20)
                    continue;
                target.Numbers ??= new List<SlashCommandNumber>();
                if (!target.Numbers.Any(n => n.Value == value && n.Meaning.Equals(meaning, StringComparison.OrdinalIgnoreCase)))
                    target.Numbers.Add(new SlashCommandNumber { Value = value, Meaning = meaning });
                if (meaning.StartsWith("default", StringComparison.OrdinalIgnoreCase))
                    target.Default ??= value;
            }

            if (DefaultIs.Match(line) is { Success: true } d)
                target.Default ??= Parse(d.Groups[1].Value);

            if (RangeText.Match(line) is { Success: true } r && target.Min is null && target.Max is null)
            {
                target.Min = Parse(r.Groups[1].Value);
                target.Max = Parse(r.Groups[2].Value);
            }
        }
    }

    private static double Parse(string text) => double.Parse(text, CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ 0 or 1

    /// <summary>
    /// The toggle form (++name) for a command whose one parameter is 0 or 1: it flips between the two. Commands whose
    /// syntax already uses + signs (zoom) are not toggles.
    /// </summary>
    public static string? ToggleFor(SlashCommand command)
    {
        if (command.Params.Count != 1 || (command.RawSyntax?.Contains('+') ?? false))
            return null;
        var p = command.Params[0];

        bool zeroOrOne =
            p is { Type: "int", Min: 0, Max: 1 }
            || (p.Values is { Count: > 0 } v && v.OrderBy(x => x).SequenceEqual(new[] { "0", "1" }))
            || (p.Numbers is { Count: > 0 } n && n.Select(x => x.Value).Distinct().OrderBy(x => x).SequenceEqual(new[] { 0d, 1d }));
        return zeroOrOne ? "++" + command.Name : null;
    }
}
