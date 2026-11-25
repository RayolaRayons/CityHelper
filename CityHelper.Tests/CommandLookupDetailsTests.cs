using CityHelper.Services.SlashCommands;
using CityHelper.Tools.CommandLookup.ViewModels;
using Xunit;

namespace CityHelper.Tests;

public class CommandLookupDetailsTests
{
    [Fact]
    public void The_list_shows_names_and_aliases_without_the_slash_while_the_heading_keeps_it()
    {
        var vm = new CommandLookupViewModel(new[]
        {
            new SlashCommand { Name = "antialiasing", Aliases = { "fsaa", "aa" }, Description = "Sets antialiasing." },
        });

        var row = vm.Commands.Single();

        Assert.Equal("antialiasing", row.ListName);
        Assert.Equal("fsaa, aa", row.ListAliases);
        Assert.Equal("/antialiasing", row.Title);
        Assert.Equal("/fsaa, /aa", row.AliasText);
    }

    [Fact]
    public void The_details_show_the_toggle_the_page_text_examples_and_numbers()
    {
        var vm = new CommandLookupViewModel(new[]
        {
            new SlashCommand
            {
                Name = "noparticles", Description = "Turn off particle graphics.", RawSyntax = "[0-1]", Toggle = "++noparticles",
                Details = "Turn off particle graphics.\nDefault = 0.", Examples = new List<string> { "noparticles 0" },
                Params =
                {
                    new SlashCommandParam
                    {
                        Name = "value", Type = "int", Min = 0, Max = 1, Optional = true, Default = 0,
                        Numbers = new List<SlashCommandNumber> { new() { Value = 0, Meaning = "Turn ON the particle system" } },
                    },
                },
            },
        });

        var row = vm.Selected!;

        Assert.Equal("/++noparticles", row.ToggleText);                    // the lookup shows the slash that the saved data leaves out
        Assert.True(row.HasDetails);
        Assert.Equal(new[] { "/noparticles 0" }, row.Examples);
        var p = row.Params.Single();
        Assert.Equal("0", p.DefaultText);
        Assert.Equal(new[] { "0 = Turn ON the particle system" }, p.Numbers);

        vm.Search = "default = 0";                                         // the page text is searched too
        Assert.Single(vm.Commands);
    }
}
