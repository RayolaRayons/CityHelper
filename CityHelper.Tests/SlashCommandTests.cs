using System.Text.Json;
using CityHelper.Services.SlashCommands;
using Xunit;

namespace CityHelper.Tests;

public class SlashCommandTests
{
    private static string Page(string rows, string section = "Working Slash Commands") =>
        $"<h2 id=\"x\">{section}</h2><table><tbody>{rows}</tbody></table>";

    private static string Row(string nameHtml, string descriptionHtml) =>
        $"<tr><td>{nameHtml}</td><td>{descriptionHtml}\n</td></tr>";

    private const string AngleSnap =
        "<b>/<a href=\"/wiki/Angle_snap_(Slash_Command)\" title=\"x\">angle_snap</a></b><i> [0-359]</i>";

    [Fact]
    public void A_row_becomes_a_command_in_the_agreed_format()
    {
        var page = Page(Row(AngleSnap, "Sets the angle for snap drag rotation in degrees while editing a supergroup base. Default snap = 5, disable snap = 0"));

        var result = SlashCommandScraper.Parse(page);

        var c = Assert.Single(result.Commands);
        Assert.Equal("angle_snap", c.Name);
        Assert.Empty(c.Aliases);
        Assert.StartsWith("Sets the angle for snap drag rotation", c.Description);
        Assert.Null(c.DefaultKey);
        Assert.Equal("https://homecoming.wiki/wiki/Angle_snap_(Slash_Command)", c.WikiUrl);
        Assert.Equal("[0-359]", c.RawSyntax);
        Assert.Equal("wiki", c.Source);
        var p = Assert.Single(c.Params);
        Assert.Equal(("degrees", "int", 0d, 359d, true), (p.Name, p.Type, p.Min, p.Max, p.Optional));
    }

    [Fact]
    public void The_json_has_the_agreed_field_names_and_an_explicit_null_default_key()
    {
        var c = SlashCommandScraper.Parse(Page(Row(AngleSnap, "Sets the angle in degrees."))).Commands.Single();

        var json = SlashCommandCache.Serialize(new[] { c });

        using var doc = JsonDocument.Parse(json);
        var item = doc.RootElement[0];
        Assert.Equal(new[] { "name", "aliases", "description", "defaultKey", "wikiUrl", "params", "rawSyntax", "source" },
                     item.EnumerateObject().Select(x => x.Name));
        Assert.Equal(JsonValueKind.Null, item.GetProperty("defaultKey").ValueKind);
        Assert.Equal(new[] { "name", "type", "min", "max", "optional" }, item.GetProperty("params")[0].EnumerateObject().Select(x => x.Name));
    }

    [Fact]
    public void A_row_that_only_says_Alias_for_becomes_an_alias_of_that_command()
    {
        var page = Page(
            Row("<b>/<a href=\"/wiki/Arena_(Slash_Command)\">arena</a></b><i> message</i>", "Sends the message on the Arena channel.") +
            Row("<b>/<a href=\"/wiki/Ac_(Slash_Command)\" class=\"mw-redirect\">ac</a></b><i> message</i>", "Alias for <b>/<a href=\"/wiki/Arena_(Slash_Command)\">arena</a></b>"));

        var result = SlashCommandScraper.Parse(page);

        var c = Assert.Single(result.Commands);
        Assert.Equal("arena", c.Name);
        Assert.Equal(new[] { "ac" }, c.Aliases);
    }

    [Fact]
    public void Alias_notes_in_a_description_add_aliases_and_leave_the_description_clean()
    {
        var page = Page(Row("<b>/<a href=\"/wiki/Broadcast_(Slash_Command)\">broadcast</a></b><i> message</i>",
            "Send message to Broadcast channel. Aliases: <b>/<a href=\"/wiki/B_(Slash_Command)\">b</a></b>, <b>/<a href=\"/wiki/Y_(Slash_Command)\">y</a></b>, <b>/<a href=\"/wiki/Yell_(Slash_Command)\">yell</a></b>"));

        var c = SlashCommandScraper.Parse(page).Commands.Single();

        Assert.Equal(new[] { "b", "y", "yell" }, c.Aliases);
        Assert.Equal("Send message to Broadcast channel.", c.Description);
    }

    [Fact]
    public void A_single_Alias_note_is_taken_out_of_the_description_too()
    {
        var page = Page(Row("<b>/<a href=\"/wiki/Antialiasing_(Slash_Command)\">antialiasing</a></b><i> number</i>",
            "Sets the amount of full screen antialiasing. Alias: <b>/<a href=\"/wiki/Fsaa_(Slash_Command)\">fsaa</a></b>"));

        var c = SlashCommandScraper.Parse(page).Commands.Single();

        Assert.Equal(new[] { "fsaa" }, c.Aliases);
        Assert.Equal("Sets the amount of full screen antialiasing.", c.Description);
    }

    [Fact]
    public void Also_notes_add_aliases()
    {
        var c = SlashCommandScraper.Parse(Page(Row("<b>/<a href=\"/wiki/Emote_(Slash_Command)\">emote</a></b><i> emote</i>",
            "Emotes a text string (Also: e or em)"))).Commands.Single();

        Assert.Equal(new[] { "e", "em" }, c.Aliases);
        Assert.Equal("Emotes a text string", c.Description);
    }

    [Theory]
    [InlineData("Cycles through 8 standard angles. (F2)", "F2", "Cycles through 8 standard angles.")]
    [InlineData("Reverse your last action. (Ctrl-Z)", "Ctrl-Z", "Reverse your last action.")]
    [InlineData("Select next object. (Shift+Tab)", "Shift+Tab", "Select next object.")]
    [InlineData("Camreset is bound to the PageDown key (default) to reset the camera.", "PageDown", "Camreset is bound to the PageDown key (default) to reset the camera.")]
    public void A_default_key_in_the_description_becomes_defaultKey(string description, string key, string cleaned)
    {
        var c = SlashCommandScraper.Parse(Page(Row("<b>/<a href=\"/wiki/X_(Slash_Command)\">x_cmd</a></b>", description))).Commands.Single();

        Assert.Equal(key, c.DefaultKey);
        Assert.Equal(cleaned, c.Description);
    }

    [Fact]
    public void Only_the_working_section_is_kept()
    {
        var page = Page(Row("<b>/<a href=\"/wiki/A\">works</a></b>", "Yes."))
                 + Page(Row("<b>/<a href=\"/wiki/B\">gone</a></b>", "Removed."), "Non-functional Slash Commands");

        var result = SlashCommandScraper.Parse(page);

        Assert.Equal("works", Assert.Single(result.Commands).Name);
        Assert.Equal(1, result.SkippedNonWorking);
    }

    [Fact]
    public void A_name_listed_twice_is_kept_once()
    {
        var page = Page(Row("<b>/<a href=\"/wiki/A\">dup</a></b>", "First.") + Row("<b>/<a href=\"/wiki/A\">dup</a></b>", "Second."));

        Assert.Single(SlashCommandScraper.Parse(page).Commands);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("message", 1)]
    [InlineData("slotName powername", 2)]
    [InlineData("+, ++, or [0-1]", 1)]
    public void The_syntax_text_gives_the_parameters(string syntax, int count) =>
        Assert.Equal(count, SlashCommandScraper.ParseSyntax(syntax, "").Count);

    [Fact]
    public void Syntax_forms_give_types_ranges_and_choices()
    {
        var typed = SlashCommandScraper.ParseSyntax("number [name] [on|off] <int>", "");

        Assert.Equal(("number", "int", false), (typed[0].Name, typed[0].Type, typed[0].Optional));
        Assert.Equal(("name", "string", true), (typed[1].Name, typed[1].Type, typed[1].Optional));
        Assert.Equal("enum", typed[2].Type);
        Assert.Equal(new[] { "on", "off" }, typed[2].Values);
        Assert.Equal(("arg4", "int"), (typed[3].Name, typed[3].Type));
    }

    [Fact]
    public void The_list_built_into_the_program_has_the_commands_and_the_agreed_example()
    {
        var all = SlashCommandCache.LoadBundled();

        Assert.True(all.Count > 400, $"only {all.Count} commands");
        var angle = all.Single(c => c.Name == "angle_snap");
        Assert.Equal("[0-359]", angle.RawSyntax);
        Assert.Equal("https://homecoming.wiki/wiki/Angle_snap_(Slash_Command)", angle.WikiUrl);
        Assert.Equal("degrees", angle.Params.Single().Name);
        Assert.Contains("ah", all.Single(c => c.Name == "auctionhouse").Aliases);
        Assert.All(all, c => Assert.Equal("wiki", c.Source));
    }
}

public class MacroImageSeedTests
{
    [Fact]
    public void The_built_in_copy_unpacks_into_the_cache_and_is_complete()
    {
        // The test program keeps its data beside itself (portable.txt), so this never touches a real cache
        if (Directory.Exists(CityHelper.Services.MacroImages.MacroImageCache.Directory))
            Directory.Delete(CityHelper.Services.MacroImages.MacroImageCache.Directory, true);

        Assert.True(CityHelper.Services.MacroImages.MacroImageCache.SeedIfMissing());

        Assert.True(CityHelper.Services.MacroImages.MacroImageCache.IsConfigured);
        var db = CityHelper.Services.MacroImages.MacroImageCache.Load()!;
        Assert.True(db.Entries.Count > 3000, $"only {db.Entries.Count} entries");
        Assert.All(db.Entries, e => Assert.True(File.Exists(CityHelper.Services.MacroImages.MacroImageCache.ImagePath(e)), e.Name));
        Assert.False(CityHelper.Services.MacroImages.MacroImageCache.SeedIfMissing());       // an existing cache is left alone
    }
}
