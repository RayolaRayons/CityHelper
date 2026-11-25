using CityHelper.Tools.LogManager;
using Xunit;

namespace CityHelper.Tests;

public class LogClassifierTests
{
    private static LogChannel Classify(string textAfterTimestamp) => LogClassifier.Classify("2026-11-18 20:50:00 " + textAfterTimestamp);

    [Theory]
    [InlineData("Head Splitter is still recharging.")]
    [InlineData("Parry is recharged.")]
    [InlineData("Now readying Hack instead of Parry.")]
    [InlineData("HIT Dark Ring Mistress! Your Hack power had a 78.53% chance to hit, you rolled a 6.34.")]
    [InlineData("Dark Ring Mistress MISSES! Subdue power had a 36.76% chance to hit, but rolled a 40.30.")]
    [InlineData("Porche HITS you! Superior Gauntleted Fist power was autohit.")]
    [InlineData("You have defeated Bombardier")]
    [InlineData("You knock Mamba Blade off their feet with your Might of the Empire: Damage/Recharge/Chance for Knockdown!")]
    [InlineData("Target is unaffected by Head Splitter.")]
    [InlineData("You use your Rejuvenate Inspiration, and gain a moderate amount of Endurance and Health.")]
    [InlineData("You have been temporarily blinded and cannot attack unless something strikes you!")]
    [InlineData("Your chance to hit and Defenses have been reduced by ShadeNet Agent's Weaken combo!")]
    public void Combat_lines_are_combat(string text) => Assert.Equal(LogChannel.Combat, Classify(text));

    [Theory]
    [InlineData("You received Invention: Defense Buff (Recipe).")]
    [InlineData("You received Respite.")]
    [InlineData("You received Incarnate Thread.")]
    [InlineData("You received Vanguard Merit.")]
    [InlineData("You received 1 units of Reward Merit.")]
    [InlineData("You received Invention: Range (Recipe).")]
    public void Received_lines_are_rewards(string text) => Assert.Equal(LogChannel.Reward, Classify(text));

    [Theory]
    [InlineData("You bid 5,000,000 total on 10 Chronal Skip.")]
    [InlineData("You have purchased Chronal Skip.")]
    [InlineData("You got 10 Enhancement Converter.")]
    [InlineData("You received Experienced Marksman: Accuracy/Damage.")]     // an enhancement, not a reward
    [InlineData("You put Panacea: Chance for +Hit Points/Endurance in the Consignment House.")]
    [InlineData("You paid 700,000 to the Consignment House.")]
    public void Consignment_house_lines_are_auction(string text) => Assert.Equal(LogChannel.Auction, Classify(text));

    [Theory]
    [InlineData("Team invite sent!")]
    [InlineData("Passcode accepted for the door.")]
    [InlineData("You have 3 unclaimed respecs available. Type /respec in the chat window to begin respecing your character.")]
    public void Game_messages_are_system(string text) => Assert.Equal(LogChannel.System, Classify(text));

    [Fact]
    public void Emotes_are_not_given_a_channel() => Assert.Equal(LogChannel.Emotes, Classify("Psyber nods his head clearly listening to you."));

    [Theory]
    [InlineData("[Local] Psyber: hello", LogChannel.Local)]
    [InlineData("[Team] Psyber: hello", LogChannel.Team)]
    [InlineData("[Combat] Head Splitter is recharged.", LogChannel.Combat)]
    [InlineData("[Reward] You received Respite.", LogChannel.Reward)]
    [InlineData("[Auction] You bid 5 total on 1 X.", LogChannel.Auction)]
    [InlineData("[Afterhours] Someone: hi", LogChannel.CustomChannels)]
    public void A_bracketed_channel_names_the_channel(string text, LogChannel expected) => Assert.Equal(expected, Classify(text));
}
