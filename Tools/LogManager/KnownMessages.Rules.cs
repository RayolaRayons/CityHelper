using System.Linq;
using System.Text.RegularExpressions;

namespace CityHelper.Tools.LogManager;

internal static partial class KnownMessages
{
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // Added by hand. Checked before the lists generated from remove_color_tags.py, so they win where both match.

    /// <summary>Consignment House (auction) activity, including enhancements received from it ("Name: Accuracy/Damage").</summary>
    private static readonly string[] AuctionPatterns =
    {
        @"You bid .*",
        @"You have purchased .*",
        @"You got .*",
        @"You put .* in the Consignment House.*",
        @"You received [^:(]+:[^()]*/.*",
        @".*Consignment House.*",
    };

    /// <summary>Rewards: salvage, recipes, incarnate threads...</summary>
    private static readonly string[] RewardPatterns =
    {
        @"You received .*",
    };

    private static readonly string[] ExtraCombatPatterns =
    {
        @"You have defeated .*",
        @"You knock .* off their feet.*",
        @"Target is unaffected by .*",
        @"You use your .* Inspiration.*",
        @"You have been temporarily .*",
        @"Your .* (has|have) been (reduced|increased|lowered|decreased|enhanced|boosted).*",
    };

    private static readonly Regex AuctionRe = Build(AuctionPatterns);
    private static readonly Regex RewardRe = Build(RewardPatterns);
    private static readonly Regex CombatRe = Build(ExtraCombatPatterns.Concat(CombatPatterns).ToArray());
    private static readonly Regex SystemRe = Build(SystemPatterns);

    private static Regex Build(string[] patterns) =>
        new("^(?:" + string.Join("|", patterns.Select(p => "(?:" + p + ")")) + ")", Options);

    /// <summary>The type of an unbracketed line (text after the timestamp), or null if it is not a known message.</summary>
    public static LogChannel? Identify(string rest)
    {
        if (AuctionRe.IsMatch(rest))
            return LogChannel.Auction;
        if (RewardRe.IsMatch(rest))
            return LogChannel.Reward;
        if (CombatRe.IsMatch(rest))
            return LogChannel.Combat;
        if (SystemRe.IsMatch(rest))
            return LogChannel.System;
        return null;
    }
}
