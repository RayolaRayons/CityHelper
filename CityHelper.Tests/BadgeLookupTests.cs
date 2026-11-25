using CityHelper.Services.Badges;
using CityHelper.Tools.BadgeLookup.ViewModels;
using Xunit;

namespace CityHelper.Tests;

public class BadgeLookupTests
{
    private static BadgeLookupViewModel Lookup() => new(new[]
    {
        new Badge { InternalName = "A", HeroName = "Adamant", VillainName = "Ironman", Category = "Exploration", Description = "Found it." },
        new Badge { InternalName = "B", HeroName = "Brawler", VillainName = "Brawler", Category = "Combat", Description = "Hit things." },
        new Badge { InternalName = "C", HeroName = "Cartographer", VillainName = "Cartographer", Category = "Exploration", Description = "Maps." },
    });

    [Fact]
    public void The_list_built_into_the_program_has_the_badges_and_categories()
    {
        var all = BadgeCache.LoadBundled();

        Assert.True(all.Count > 1500, $"only {all.Count} badges");
        Assert.True(all.Select(b => b.Category).Distinct().Count() >= 10);
    }

    [Fact]
    public void Categories_list_all_with_counts_and_filter_the_badges()
    {
        var vm = Lookup();

        Assert.Equal(3, vm.Categories.Count);
        Assert.Equal(3, vm.Badges.Count);

        vm.SelectedCategory = vm.Categories.Single(c => c.Name == "Exploration");

        Assert.Equal(new[] { "Adamant / Ironman", "Cartographer" }, vm.Badges.Select(b => b.Title));
        Assert.Equal("2 of 3 badges", vm.CountText);
    }

    [Fact]
    public void Search_finds_either_name_or_the_description_within_the_category()
    {
        var vm = Lookup();

        vm.Search = "ironman";
        Assert.Equal("A", vm.Badges.Single().InternalName);

        vm.Search = "hit things";
        Assert.Equal("B", vm.Badges.Single().InternalName);

        vm.SelectedCategory = vm.Categories.Single(c => c.Name == "Exploration");
        Assert.Empty(vm.Badges);
    }
}
