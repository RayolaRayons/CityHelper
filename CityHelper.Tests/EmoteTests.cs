using CityHelper.Services.Emotes;
using CityHelper.Services.SlashCommands;
using CityHelper.Tools.CommandLookup.ViewModels;
using Xunit;

namespace CityHelper.Tests;

public class EmoteTests
{
    // Two tables with different columns, a repeated heading row, hidden placeholder text, and an emote listed twice
    private const string Page = """
        <h2>Normal Emotes</h2>
        <table class="wikitable"><tbody>
        <tr><th>Emote</th><th>Aliases</th><th>Flying?</th><th>Sound?</th><th>Loops?</th><th>Description</th><th>Issue Release</th></tr>
        <tr><td><a href="/wiki/Emote_Afraid">Afraid</a></td><td>Cower, Fear, Scared</td><td>Flying</td><td>silent</td><td>Loops</td><td>Cower in fear, hold stance.</td><td></td></tr>
        <tr><td><a href="/wiki/Emote_Akimbo">Akimbo</a></td><td><span style="visibility:hidden">zzzzz</span></td><td>No</td><td>silent</td><td>Loops</td><td>Stands with fists on hips. See /e afraid.</td><td></td></tr>
        <tr><th>Emote</th><th>Aliases</th><th>Flying?</th><th>Sound?</th><th>Loops?</th><th>Description</th><th>Issue Release</th></tr>
        <tr><td><a href="/wiki/Emote_Bow">Bow</a></td><td>Sorry</td><td>No</td><td>silent</td><td></td><td>Chinese/Japanese style bow.</td><td></td></tr>
        </tbody></table>
        <h2>Costume Change Emotes</h2>
        <table class="wikitable"><tbody>
        <tr><th>Emote</th><th>Aliases</th><th>Flying?</th><th>Sound?</th><th>Description</th><th>Issue Release</th></tr>
        <tr><td>Afraid</td><td>Terrified</td><td>No</td><td>silent</td><td>Changes costume.</td><td></td></tr>
        <tr><td>Hero</td><td></td><td>No</td><td>silent</td><td>Changes to the hero costume.</td><td></td></tr>
        </tbody></table>
        <table><tr><th>Something</th><th>Else</th></tr><tr><td>not</td><td>an emote table</td></tr></table>
        """;

    [Fact]
    public void Each_emote_table_gives_the_emote_aliases_and_description()
    {
        var emotes = EmoteScraper.Parse(Page);

        Assert.Equal(new[] { "Afraid", "Akimbo", "Bow", "Hero" }, emotes.Select(e => e.Name));
        var afraid = emotes[0];
        Assert.Equal("Cower, Fear, Scared, Terrified".Split(", "), afraid.Aliases);       // the second table adds an alias
        Assert.Equal("Cower in fear, hold stance.", afraid.Description);                  // the first description is kept
        Assert.Empty(emotes[1].Aliases);                                                  // hidden placeholder text is not an alias
    }

    [Fact]
    public void A_table_without_a_description_column_or_an_emote_column_is_ignored()
    {
        var emotes = EmoteScraper.Parse(Page);

        Assert.DoesNotContain(emotes, e => e.Name == "not");
    }

    [Fact]
    public void Saved_text_has_no_slash_in_front_of_a_command_but_keeps_other_slashes()
    {
        var emotes = EmoteScraper.Parse(Page);

        Assert.Equal("Stands with fists on hips. See e afraid.", emotes.Single(e => e.Name == "Akimbo").Description);
        Assert.Equal("Chinese/Japanese style bow.", emotes.Single(e => e.Name == "Bow").Description);
    }

    [Fact]
    public void The_json_has_just_the_three_fields()
    {
        var json = EmoteCache.Serialize(EmoteScraper.Parse(Page).Take(1));

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(new[] { "name", "aliases", "description" }, doc.RootElement[0].EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void The_list_built_into_the_program_has_the_emotes()
    {
        var all = EmoteCache.LoadBundled();

        Assert.True(all.Count > 300, $"only {all.Count} emotes");
        Assert.Contains("Cower", all.Single(e => e.Name == "Afraid").Aliases);
        Assert.All(all, e =>
        {
            Assert.False(e.Name.StartsWith('/'), e.Name);
            Assert.All(e.Aliases, a => Assert.False(a.StartsWith('/'), e.Name + ": " + a));
        });
    }
}

public class CommandLookupAccordionTests
{
    private static CommandLookupViewModel Lookup() => new(
        new[]
        {
            new SlashCommand { Name = "arena", Description = "Arena chat." },
            new SlashCommand { Name = "wave", Description = "Waves." },
        },
        new[]
        {
            new Emote { Name = "Afraid", Aliases = { "Cower", "Fear" }, Description = "Cower in fear, hold stance." },
            new Emote { Name = "Wave", Aliases = { "Hello" }, Description = "Waves hello." },
            new Emote { Name = "Bow", Description = "Takes a bow." },
        });

    [Fact]
    public void Commands_is_open_to_begin_with_and_the_headers_show_the_counts()
    {
        var vm = Lookup();

        Assert.True(vm.IsCommandsOpen);
        Assert.False(vm.IsEmotesOpen);
        Assert.Contains("Commands", vm.CommandsHeader);
        Assert.Contains("(2)", vm.CommandsHeader);
        Assert.Contains("(3)", vm.EmotesHeader);
        Assert.StartsWith("▼", vm.CommandsHeader);
        Assert.StartsWith("▶", vm.EmotesHeader);
    }

    [Fact]
    public void Opening_one_section_closes_the_other()
    {
        var vm = Lookup();

        vm.ToggleEmotesCommand.Execute(null);
        Assert.True(vm.IsEmotesOpen);
        Assert.False(vm.IsCommandsOpen);

        vm.ToggleCommandsCommand.Execute(null);
        Assert.True(vm.IsCommandsOpen);
        Assert.False(vm.IsEmotesOpen);

        vm.ToggleCommandsCommand.Execute(null);                 // an open section can be closed: both are shut
        Assert.False(vm.IsCommandsOpen);
        Assert.False(vm.IsEmotesOpen);
    }

    [Fact]
    public void The_detail_pane_follows_the_open_section()
    {
        var vm = Lookup();
        Assert.True(vm.ShowCommandDetail);
        Assert.False(vm.ShowEmoteDetail);

        vm.IsEmotesOpen = true;

        Assert.False(vm.ShowCommandDetail);
        Assert.True(vm.ShowEmoteDetail);
        Assert.Equal("Afraid", vm.SelectedEmote!.Name);
    }

    [Fact]
    public void Search_filters_both_sections_and_the_status_follows_the_open_one()
    {
        var vm = Lookup();

        vm.Search = "wave";

        Assert.Equal(new[] { "wave" }, vm.Commands.Select(c => c.ListName));
        Assert.Equal(new[] { "Wave" }, vm.Emotes.Select(e => e.Name));
        Assert.Equal("1 of 2 commands", vm.CountText);
        vm.IsEmotesOpen = true;
        Assert.Equal("1 of 3 emotes", vm.CountText);
        Assert.Equal(vm.CountText, vm.StatusBarText);
    }

    [Fact]
    public void Emotes_are_found_by_alias_or_description_and_use_the_chat_line_form()
    {
        var vm = Lookup();

        vm.Search = "cower";
        Assert.Equal("Afraid", vm.Emotes.Single().Name);
        Assert.Equal("/e Afraid", vm.Emotes.Single().UseLine);
        Assert.Equal("Cower, Fear", vm.Emotes.Single().AliasText);

        vm.Search = "takes a bow";
        Assert.Equal("Bow", vm.Emotes.Single().Name);
        Assert.False(vm.Emotes.Single().HasAliases);
    }
}
