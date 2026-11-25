using System.Text.Json;
using CityHelper.Services.SlashCommands;
using Xunit;

namespace CityHelper.Tests;

public class SlashCommandPageTests
{
    private const string NoParticlesPage = """
        <div id="mw-content-text" class="mw-body-content"><div class="mw-content-ltr mw-parser-output">
        <div class="mw-heading mw-heading2"><h2 id="Slash_command">Slash command</h2></div>
        <div style="margin: 0.2em 0 1em 1.6em"><b>/<a class="mw-selflink selflink">noparticles</a></b> <i>[0-1]</i></div>
        <p>Turn off particle graphics.
        </p>
        <dl><dd><b>0</b> = Turn ON the particle system</dd>
        <dd><b>1</b> = The OFF the particle system</dd></dl>
        <ul><li>Default = 0.</li>
        <li>When used without an argument, this command will tell you the state. For example, <code>/noparticles</code></li>
        <li>Also consider reducing the <a href="/wiki/Bloomweight_(Slash_Command)">Bloom Amount</a> (see /bloomweight).</li></ul>
        <div class="mw-heading mw-heading2"><h2 id="Example">Example</h2></div>
        <pre>/noparticles 0
        </pre>
        <div role="navigation" class="navbox"><dl><dd><b>/<a href="/wiki/Other_(Slash_Command)">other_command</a></b> navigation links that are not part of the page</dd></dl></div>
        """;

    private static SlashCommand Command(string name, string syntax, string description = "") =>
        SlashCommandScraper.Build(name, syntax, description, "https://homecoming.wiki/wiki/X_(Slash_Command)");

    [Fact]
    public void A_command_page_adds_details_examples_numbers_and_the_default()
    {
        var c = Command("noparticles", "[0-1]", "Turn off particle graphics.");

        SlashCommandPageParser.Apply(c, NoParticlesPage);

        Assert.StartsWith("Turn off particle graphics.", c.Details);
        Assert.DoesNotContain("navigation links", c.Details);                    // the link boxes at the bottom are not page text
        Assert.Equal(new[] { "noparticles 0" }, c.Examples);                     // no leading slash
        var p = Assert.Single(c.Params);
        Assert.Equal(0d, p.Default);
        Assert.Equal(new[] { (0d, "Turn ON the particle system"), (1d, "The OFF the particle system") },
                     p.Numbers!.Select(n => (n.Value, n.Meaning)));
    }

    [Fact]
    public void A_command_that_takes_0_or_1_gets_the_toggle_form_without_a_slash()
    {
        var c = Command("noparticles", "[0-1]");

        SlashCommandPageParser.Apply(c, NoParticlesPage);

        Assert.Equal("++noparticles", c.Toggle);
    }

    [Fact]
    public void The_saved_text_has_no_slash_in_front_of_a_command()
    {
        var c = Command("noparticles", "[0-1]", "Like the old /hide command.");
        SlashCommandPageParser.Apply(c, NoParticlesPage);

        Assert.Equal("Like the old hide command.", c.Description);
        Assert.Contains("For example, noparticles", c.Details);
        Assert.Contains("(see bloomweight)", c.Details);
        Assert.DoesNotContain("/noparticles", c.Details);
    }

    [Theory]
    [InlineData("Up/Down and and/or stay", "Up/Down and and/or stay")]
    [InlineData("see https://homecoming.wiki/x", "see https://homecoming.wiki/x")]
    [InlineData("use /camreset or /+zoomin now", "use camreset or +zoomin now")]
    public void Only_a_slash_that_starts_a_command_is_removed(string text, string expected) =>
        Assert.Equal(expected, SlashCommandScraper.StripSlashes(text));

    [Fact]
    public void Numbers_are_found_for_labelled_values_typical_settings_and_limits()
    {
        var angle = Command("angle_snap", "[0-359]");
        SlashCommandPageParser.ReadNumbers(angle, new[] { "Default snap = 5, disable snap = 0" });
        Assert.Equal(5d, angle.Params[0].Default);
        Assert.Equal(new[] { 5d, 0d }, angle.Params[0].Numbers!.Select(n => n.Value));

        var aa = Command("antialiasing", "number");
        SlashCommandPageParser.ReadNumbers(aa, new[] { "Available options depend on your card, but typical settings are 0, 2, 4, and 8." });
        Assert.Equal(new[] { 0d, 2d, 4d, 8d }, aa.Params[0].Numbers!.Select(n => n.Value));

        var cam = Command("camdist", "distance");
        SlashCommandPageParser.ReadNumbers(cam, new[] { "Camdist is measured in feet.", "The camera is limited to a maximum of 120 feet away." });
        Assert.Equal("float", cam.Params[0].Type);                              // measured in feet: a number
        Assert.Equal(120d, Assert.Single(cam.Params[0].Numbers!).Value);
    }

    [Fact]
    public void Issue_numbers_are_not_taken_for_parameter_values()
    {
        var c = Command("room_clip", "[0-1]");

        SlashCommandPageParser.ReadNumbers(c, new[] { "This command was first introduced by Homecoming in Issue 25." });

        Assert.Null(c.Params[0].Numbers);
    }

    [Theory]
    [InlineData("[0-1]", true)]
    [InlineData("[0-359]", false)]
    [InlineData("+, ++, or [0-1]", false)]              // the zoom commands use + and ++ themselves
    [InlineData("message", false)]
    [InlineData("[0-1] [0-1]", false)]                  // two parameters: no single toggle
    [InlineData("", false)]
    public void Toggles_exist_only_for_a_single_0_or_1_parameter(string syntax, bool toggle) =>
        Assert.Equal(toggle, Command("example", syntax).Toggle == "++example");

    [Fact]
    public void A_parameter_whose_page_lists_exactly_0_and_1_is_a_toggle_too()
    {
        var c = Command("fullscreen_like", "number");

        SlashCommandPageParser.ReadNumbers(c, new[] { "0 = windowed", "1 = full screen" });

        Assert.Equal("++fullscreen_like", SlashCommandPageParser.ToggleFor(c));
    }

    [Fact]
    public void The_new_fields_are_written_only_when_there_is_something_to_write()
    {
        var plain = Command("afk", "message", "Away.");
        var toggled = Command("noparticles", "[0-1]", "Off.");
        SlashCommandPageParser.Apply(toggled, NoParticlesPage);

        using var a = JsonDocument.Parse(SlashCommandCache.Serialize(new[] { plain }));
        using var b = JsonDocument.Parse(SlashCommandCache.Serialize(new[] { toggled }));

        Assert.False(a.RootElement[0].TryGetProperty("toggle", out _));
        Assert.False(a.RootElement[0].TryGetProperty("details", out _));
        Assert.Equal("++noparticles", b.RootElement[0].GetProperty("toggle").GetString());
        Assert.Equal(JsonValueKind.String, b.RootElement[0].GetProperty("details").ValueKind);
        Assert.Equal("wiki", b.RootElement[0].GetProperty("source").GetString());
    }

    [Fact]
    public void Nothing_in_the_built_in_list_starts_with_a_slash()
    {
        var all = SlashCommandCache.LoadBundled();

        Assert.All(all, c =>
        {
            Assert.False(c.Name.StartsWith('/'), c.Name);
            Assert.All(c.Aliases, a => Assert.False(a.StartsWith('/'), c.Name + ": " + a));
            Assert.False(c.Toggle?.StartsWith('/') ?? false, c.Name);
            Assert.All(c.Examples ?? new List<string>(), e => Assert.False(e.StartsWith('/'), c.Name + ": " + e));
            Assert.False(c.RawSyntax?.StartsWith('/') ?? false, c.Name);
        });
    }
}
