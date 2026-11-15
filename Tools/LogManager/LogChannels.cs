using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CityHelper.Tools.ConfigSync;

namespace CityHelper.Tools.LogManager;

public enum LogChannel
{
    PetMessages,
    CustomChannels,
    Combat,
    Broadcast,
    Coalition,
    LookingForGroup,
    Request,
    Supergroup,
    Team,
    Tell,
    Help,
    League,
    Local,
    Emotes,
    Npc,
    System,
    AeTickets,
    Unknown,
}

public static class LogChannels
{
    public static readonly IReadOnlyList<LogChannel> All = Enum.GetValues<LogChannel>();

    /// <summary>Channels the RP preset keeps.</summary>
    public static readonly IReadOnlySet<LogChannel> RpPreset = new HashSet<LogChannel>
    {
        LogChannel.Broadcast, LogChannel.Coalition, LogChannel.LookingForGroup, LogChannel.Request,
        LogChannel.Supergroup, LogChannel.Team, LogChannel.Tell, LogChannel.League, LogChannel.Local, LogChannel.Emotes,
    };

    public static string DisplayName(LogChannel c) => c switch
    {
        LogChannel.PetMessages => "Pet messages",
        LogChannel.CustomChannels => "Custom Channels",
        LogChannel.LookingForGroup => "Looking for group",
        LogChannel.Npc => "NPC",
        LogChannel.AeTickets => "AE Tickets",
        _ => c.ToString(),
    };

    /// <summary>Stable key used in the ini file.</summary>
    public static string Key(LogChannel c) => c.ToString().ToLowerInvariant();
}

/// <summary>
/// Decides which channel a log entry belongs to from its first line, e.g.
/// "2026-11-06 00:00:05 [Local] Name: text" (bracketed channel) or "2026-11-06 00:00:08 You gain 868 experience."
/// All the guesses about unbracketed lines (combat / system / emote) live here.
/// </summary>
public static class LogClassifier
{
    private const int TimestampLength = 19;   // "yyyy-MM-dd HH:mm:ss"

    private static readonly Regex Tag = new(@"^\[([^\]]*)\]", RegexOptions.Compiled);
    private static readonly Regex TimeLikeTag = new(@"^\d{1,2}:\d{2}$", RegexOptions.Compiled);
    private static readonly Regex AeTicketsText = new(@"^\s*You can claim \d+ tickets? from Architect Entertainment",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Game-generated news / zone event channels
    private static readonly HashSet<string> SystemTags = new(StringComparer.OrdinalIgnoreCase)
        { "system", "ppd", "fdpc", "nemesis", "vanguard", "caption", "council", "rikti" };

    private static readonly Regex CombatLine = new(
        @"^(You (hit|miss|gain|heal|stun|defeated|activated|resist|are (hit|healed|stunned|held|slept|immobilized|knocked|terrorized|confused|feared|blinded)|were)\b" +
        @"|Your combat improves" +
        @"|.+ (hits|misses|has defeated|heals|stuns|is defeated|was defeated|resists) )",
        RegexOptions.Compiled);

    private static readonly Regex SystemLine = new(
        @"^(You |Your |The |Dragging |Welcome|Server|Zone|Warning|Attention|Notice|Congratulations)",
        RegexOptions.Compiled);

    public static LogChannel Classify(string entryFirstLine)
    {
        if (entryFirstLine.Length <= TimestampLength + 1)
            return LogChannel.Unknown;
        var rest = entryFirstLine[(TimestampLength + 1)..];

        var m = Tag.Match(rest);
        if (m.Success)
        {
            var tag = m.Groups[1].Value.Trim();

            // "[05:53] You can claim 105 tickets from Architect Entertainment." (the tag is a time, not a channel)
            if (TimeLikeTag.IsMatch(tag) && AeTicketsText.IsMatch(rest[m.Length..]))
                return LogChannel.AeTickets;

            return tag.ToLowerInvariant() switch
            {
                "local" => LogChannel.Local,
                "league" => LogChannel.League,
                "looking for group" or "lfg" => LogChannel.LookingForGroup,
                "broadcast" => LogChannel.Broadcast,
                "help" => LogChannel.Help,
                "team" => LogChannel.Team,
                "request" => LogChannel.Request,
                "tell" => LogChannel.Tell,
                "supergroup" or "sg" => LogChannel.Supergroup,
                "coalition" => LogChannel.Coalition,
                "npc" => LogChannel.Npc,
                "pet" => LogChannel.PetMessages,
                "emote" => LogChannel.Emotes,
                _ when SystemTags.Contains(tag) => LogChannel.System,
                _ when TimeLikeTag.IsMatch(tag) => LogChannel.Unknown,
                _ => LogChannel.CustomChannels,   // General, supergroup-named, player channels...
            };
        }

        if (CombatLine.IsMatch(rest))
            return LogChannel.Combat;
        if (SystemLine.IsMatch(rest))
            return LogChannel.System;
        return LogChannel.Emotes;               // "Name does something"
    }

    internal static LogChannel Classify(LogMerger.Entry entry) =>
        entry.Time == DateTime.MinValue || entry.Lines.Count == 0 ? LogChannel.Unknown : Classify(entry.Lines[0]);
}
