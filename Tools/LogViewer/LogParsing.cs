using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CityHelper.Tools.LogManager;

namespace CityHelper.Tools.LogViewer;

/// <summary>One log entry, split into the parts the viewer can show, hide and filter on.</summary>
public sealed class LogLine
{
    public const string NoCharacter = "(none)";

    public int Number { get; init; }
    public string Date { get; init; } = "";
    public string Time { get; init; } = "";

    /// <summary>The bracketed channel as written in the log ("Local", "Looking For Group", "CU Social"); empty for unbracketed lines.</summary>
    public string Channel { get; init; } = "";

    /// <summary>What the channel filter lists: the bracketed channel, or Combat / System / Emote / Unknown for unbracketed lines.</summary>
    public string ChannelKey { get; init; } = "";

    public string Character { get; set; } = "";

    /// <summary>True for "Name: message" lines. Emotes and system text keep their full wording in the message.</summary>
    public bool ShowCharacterPrefix { get; init; }

    /// <summary>An outgoing tell ("--> Name"): shown with an arrow, but the arrow is not part of the character's name.</summary>
    public bool IsOutgoing { get; init; }

    /// <summary>The speaker as drawn in the line: the name, with the arrow in front for an outgoing tell.</summary>
    public string CharacterDisplay => IsOutgoing ? "→ " + Character : Character;

    /// <summary>Message text with its color tags still in place; continuation lines are joined with a newline.</summary>
    public string Message { get; set; } = "";

    public string CharacterKey => Character.Length == 0 ? NoCharacter : Character;

    /// <summary>The entry's first line exactly as written in the log (empty for stray text before the first timestamp).</summary>
    public string FirstLine { get; init; } = "";

    private LogChannel? _cleanupChannel;

    /// <summary>The channel Log Manager's cleanup sorts this entry into (it keeps or removes whole channels).</summary>
    public LogChannel CleanupChannel => _cleanupChannel ??= Date.Length == 0 ? LogChannel.Unknown : LogClassifier.Classify(FirstLine);

    /// <summary>Number of physical lines in the log this entry covers (the first plus continuation lines).</summary>
    public int PhysicalLines => 1 + Message.Count(c => c == '\n');
}

public sealed class ParsedLog
{
    public List<LogLine> Lines { get; } = new();
    public SortedDictionary<string, int> ChannelCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public SortedDictionary<string, int> CharacterCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Splits a chat log into entries: "yyyy-MM-dd HH:mm:ss [Channel] Name: message" for channel chat, "Name: message"
/// style speaker detection per channel, and unbracketed lines classified as combat, system or emote (emotes get
/// their character from the names seen speaking elsewhere in the file).
/// </summary>
public static class LogFileParser
{
    private static readonly Regex LineRe = new(@"^(\d{4}-\d{2}-\d{2}) (\d{2}:\d{2}:\d{2}) (.*)$", RegexOptions.Compiled);
    private static readonly Regex ChannelRe = new(@"^\[([^\]]*)\]\s?(.*)$", RegexOptions.Compiled);
    private static readonly Regex IncomingTellRe = new(@"^:([^:]+):\s?(.*)$", RegexOptions.Compiled);
    private static readonly Regex OutgoingTellRe = new(@"^-->\s*([^:]+):\s?(.*)$", RegexOptions.Compiled);
    private static readonly Regex LeadingGroupRe = new(@"^\s*\([^)]*\)", RegexOptions.Compiled);

    // Game-generated channels: no speaker, the whole rest is the message
    private static readonly HashSet<string> SpeakerlessChannels = new(StringComparer.OrdinalIgnoreCase)
        { "system", "combat", "reward", "auction", "ppd", "fdpc", "nemesis", "vanguard", "caption", "council", "rikti" };

    public static ParsedLog Parse(string path)
    {
        var result = new ParsedLog();
        LogLine? last = null;
        int number = 0;

        // The game may still have the file open for writing
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? text;
        while ((text = reader.ReadLine()) != null)
        {
            number++;
            var m = LineRe.Match(text);
            if (!m.Success)
            {
                // Continuation of the previous entry (or stray text at the top of the file)
                if (last != null)
                    last.Message += "\n" + text;
                else
                {
                    last = new LogLine { Number = number, ChannelKey = "Unknown", Message = text };
                    result.Lines.Add(last);
                }
                continue;
            }

            last = ParseEntry(number, m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, text);
            result.Lines.Add(last);
        }

        AssignEmoteCharacters(result.Lines);

        foreach (var line in result.Lines)
        {
            result.ChannelCounts[line.ChannelKey] = result.ChannelCounts.GetValueOrDefault(line.ChannelKey) + 1;
            result.CharacterCounts[line.CharacterKey] = result.CharacterCounts.GetValueOrDefault(line.CharacterKey) + 1;
        }
        return result;
    }

    private static LogLine ParseEntry(int number, string date, string time, string rest, string fullLine)
    {
        // "[05:53] You can claim 105 tickets from Architect Entertainment." starts with a time, not a channel
        if (LogClassifier.Classify(fullLine) == LogChannel.AeTickets)
            return new LogLine { Number = number, Date = date, Time = time, ChannelKey = "AE Tickets", Message = rest, FirstLine = fullLine };

        var channel = ChannelRe.Match(rest);
        if (!channel.Success)
        {
            // No bracket: combat, system text or an emote
            var key = LogClassifier.Classify(fullLine) switch
            {
                LogChannel.Combat => "Combat",
                LogChannel.System => "System",
                LogChannel.Reward => "Reward",
                LogChannel.Auction => "Auction",
                LogChannel.Emotes => "Emote",
                _ => "Unknown",
            };
            return new LogLine { Number = number, Date = date, Time = time, ChannelKey = key, Message = rest, FirstLine = fullLine };
        }

        var tag = channel.Groups[1].Value;
        var body = channel.Groups[2].Value;
        var (character, message, hasSpeaker, outgoing) = SplitSpeaker(tag, body);

        return new LogLine
        {
            Number = number,
            Date = date,
            Time = time,
            Channel = tag,
            ChannelKey = tag.Length == 0 ? "Unknown" : tag,
            Character = character,
            ShowCharacterPrefix = hasSpeaker,
            IsOutgoing = outgoing,
            Message = message,
            FirstLine = fullLine,
        };
    }

    private static (string Character, string Message, bool HasSpeaker, bool Outgoing) SplitSpeaker(string tag, string body)
    {
        if (SpeakerlessChannels.Contains(tag))
            return ("", body, false, false);

        // Custom channel MOTD / handle lines look like "(null): Name: text"
        if (body.StartsWith("(null): ", StringComparison.Ordinal))
            body = body[8..];

        if (tag.Equals("Tell", StringComparison.OrdinalIgnoreCase))
        {
            var incoming = IncomingTellRe.Match(body);
            if (incoming.Success)
                return (incoming.Groups[1].Value.Trim(), incoming.Groups[2].Value, true, false);
            var outgoing = OutgoingTellRe.Match(body);
            if (outgoing.Success)
                return (outgoing.Groups[1].Value.Trim(), outgoing.Groups[2].Value, true, true);
            return ("", body, false, false);
        }

        // "Name: message" where the colon is followed by a space (names never contain markup)
        int colon = body.IndexOf(": ", StringComparison.Ordinal);
        if (colon <= 0 || colon > 60 || body[0] == '<')
            return ("", body, false, false);

        var speaker = body[..colon];
        if (speaker.Contains('<') || speaker.Contains('>'))
            return ("", body, false, false);

        // "(Supergroup Name)Character" prefix on coalition / pet lines
        speaker = LeadingGroupRe.Replace(speaker, "").Trim();
        return speaker.Length == 0 ? ("", body, false, false) : (speaker, body[(colon + 2)..], true, false);
    }

    /// <summary>
    /// Emotes read "Name does something". The name isn't delimited, so match the start of the text against the names
    /// that spoke elsewhere in the file (longest match wins) and use that as the character.
    /// </summary>
    private static void AssignEmoteCharacters(List<LogLine> lines)
    {
        var byFirstWord = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in lines.Where(l => l.ShowCharacterPrefix && l.Character.Length > 0)
                     .Select(l => l.Character).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var first = FirstWord(name);
            if (!byFirstWord.TryGetValue(first, out var list))
                byFirstWord[first] = list = new List<string>();
            list.Add(name);
        }
        foreach (var list in byFirstWord.Values)
            list.Sort((a, b) => b.Length.CompareTo(a.Length));

        foreach (var line in lines.Where(l => l.ChannelKey == "Emote"))
        {
            if (!byFirstWord.TryGetValue(FirstWord(line.Message), out var candidates))
                continue;
            foreach (var name in candidates)
            {
                if (line.Message.StartsWith(name, StringComparison.OrdinalIgnoreCase) &&
                    (line.Message.Length == name.Length || line.Message[name.Length] is ' ' or '\''))
                {
                    line.Character = name;
                    break;
                }
            }
        }
    }

    private static string FirstWord(string text)
    {
        int end = text.IndexOfAny(new[] { ' ', '\'' });
        return end < 0 ? text : text[..end];
    }
}
