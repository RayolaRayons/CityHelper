using CityHelper.Services.SlashCommands;
using CityHelper.Tools.CommandLookup.ViewModels;
using Xunit;

namespace CityHelper.Tests;

public class CommandLookupTests
{
    private static CommandLookupViewModel Lookup() => new(new[]
    {
        new SlashCommand { Name = "broadcast", Aliases = { "b", "y", "yell" }, Description = "Send message to Broadcast channel.", RawSyntax = "message",
                           Params = { new SlashCommandParam { Name = "message", Type = "string" } } },
        new SlashCommand { Name = "arena", Description = "Sends the message on the Arena channel.", RawSyntax = "message" },
        new SlashCommand { Name = "angle_snap", Description = "Sets the angle in degrees.", RawSyntax = "[0-359]", DefaultKey = "F2",
                           Params = { new SlashCommandParam { Name = "degrees", Type = "int", Min = 0, Max = 359, Optional = true } } },
    });

    [Fact]
    public void All_commands_are_listed_by_name_to_begin_with()
    {
        var vm = Lookup();

        Assert.Equal(new[] { "/angle_snap", "/arena", "/broadcast" }, vm.Commands.Select(c => c.Title));
        Assert.Equal("3 commands", vm.CountText);
        Assert.Equal("/angle_snap", vm.Selected!.Title);
    }

    [Fact]
    public void Search_finds_names_aliases_and_descriptions_and_ignores_a_leading_slash()
    {
        var vm = Lookup();

        vm.Search = "/yell";
        Assert.Equal(new[] { "/broadcast" }, vm.Commands.Select(c => c.Title));

        vm.Search = "degrees";
        Assert.Equal(new[] { "/angle_snap" }, vm.Commands.Select(c => c.Title));

        vm.Search = "ARENA";
        Assert.Equal("/arena", vm.Commands.First().Title);       // the command named arena comes before ones that only mention it
        Assert.Equal("1 of 3 commands", vm.CountText);
    }

    [Fact]
    public void A_name_match_is_listed_before_a_description_match()
    {
        var vm = new CommandLookupViewModel(new[]
        {
            new SlashCommand { Name = "alpha", Description = "Sends a message to the arena." },
            new SlashCommand { Name = "arena", Description = "Arena chat." },
        });

        vm.Search = "arena";

        Assert.Equal(new[] { "/arena", "/alpha" }, vm.Commands.Select(c => c.Title));
    }

    [Fact]
    public void The_details_show_syntax_key_aliases_and_parameters()
    {
        var vm = Lookup();

        vm.Search = "angle";
        var row = vm.Selected!;
        Assert.Equal("/angle_snap [0-359]", row.SyntaxLine);
        Assert.Equal("F2", row.KeyText);
        Assert.False(row.HasAliases);
        var p = Assert.Single(row.Params);
        Assert.Equal(("degrees", "int", "0 to 359", "optional"), (p.Name, p.Type, p.Range, p.Requirement));

        vm.Search = "yell";
        Assert.Equal("/b, /y, /yell", vm.Selected!.AliasText);
        Assert.False(vm.Selected.HasKey);
    }

    [Fact]
    public void A_search_with_no_match_leaves_nothing_selected()
    {
        var vm = Lookup();

        vm.Search = "zzz-no-such";

        Assert.Empty(vm.Commands);
        Assert.False(vm.HasSelection);
    }

    [Fact]
    public void The_status_bar_shows_the_count()
    {
        var vm = Lookup();
        vm.Search = "message";

        Assert.Equal(vm.CountText, vm.StatusBarText);
    }
}
