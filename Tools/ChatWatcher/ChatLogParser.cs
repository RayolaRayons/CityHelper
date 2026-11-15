using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CityHelper.Tools.ChatWatcher;

public sealed record ChatRequest(DateTime Timestamp, string Name, string Channel, string Message);

/// <summary>
/// Parses chat log lines like "2026-11-05 23:31:52 [Local] Name: text" (and "[Tell] :Name: text" for incoming tells)
/// and decides whether they match the watched channels and keywords.
/// </summary>
public static class ChatLogParser
{
    public static readonly IReadOnlyList<string> Channels = new[] { "Tell", "Local", "Broadcast", "Request" };

    // Markup embedded in chat text, e.g. <color #ff0101> or </color>
    private static readonly Regex TagRe = new(@"<[^<>\r\n]*>", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s{2,}", RegexOptions.Compiled);

    private static readonly Regex LineRe = new(
        @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) \[(?<ch>Tell|Local|Broadcast|Request)\] (?<rest>.*)$",
        RegexOptions.Compiled);
    private static readonly Regex TellRe = new(@"^:(?<name>[^:]+):\s*(?<msg>.*)$", RegexOptions.Compiled);
    private static readonly Regex SpeakerRe = new(@"^(?<name>[^:]+):\s*(?<msg>.*)$", RegexOptions.Compiled);

    // Written when a character logs in: "2026-11-11 17:41:30 Welcome to City of Heroes, <Name>!"
    private static readonly Regex WelcomeRe = new(
        @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} Welcome to City of Heroes, (?<name>.+?)!\s*$", RegexOptions.Compiled);

    /// <summary>The character name if the line is the login greeting, otherwise null.</summary>
    public static string? ParseWelcome(string rawLine)
    {
        var m = WelcomeRe.Match(StripTags(rawLine));
        return m.Success ? m.Groups["name"].Value.Trim() : null;
    }

    public static string StripTags(string text) => Spaces.Replace(TagRe.Replace(text, ""), " ").Trim();

    /// <summary>Splits the comma / semicolon / newline separated keyword text.</summary>
    public static List<string> SplitKeywords(string text) =>
        text.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim()).Where(k => k.Length > 0).ToList();

    /// <summary>Whole-word, case-insensitive for plain words; substring for anything with punctuation or spaces.</summary>
    public static List<Regex> CompileKeywords(IEnumerable<string> keywords) =>
        keywords.Select(k => Regex.IsMatch(k, @"^[A-Za-z0-9_]+$")
                ? new Regex(@"\b" + Regex.Escape(k) + @"\b", RegexOptions.IgnoreCase)
                : new Regex(Regex.Escape(k), RegexOptions.IgnoreCase)).ToList();

    /// <summary>
    /// Returns the request if the line is from a watched channel and (when keywords are given) its message
    /// matches one of them. With no keywords every message in the watched channels matches.
    /// </summary>
    public static ChatRequest? Parse(string rawLine, IReadOnlySet<string> watchedChannels, IReadOnlyList<Regex> keywords)
    {
        var m = LineRe.Match(StripTags(rawLine));
        if (!m.Success)
            return null;

        var channel = m.Groups["ch"].Value;
        if (!watchedChannels.Contains(channel))
            return null;

        var rest = m.Groups["rest"].Value;
        if (rest.StartsWith("-->"))
            return null;                                   // our own outgoing tell

        var speaker = (channel == "Tell" ? TellRe : SpeakerRe).Match(rest);
        if (!speaker.Success)
            return null;

        var message = speaker.Groups["msg"].Value.Trim();
        if (keywords.Count > 0 && !keywords.Any(k => k.IsMatch(message)))
            return null;

        return new ChatRequest(
            DateTime.ParseExact(m.Groups["ts"].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            speaker.Groups["name"].Value.Trim(), channel, message);
    }
}
