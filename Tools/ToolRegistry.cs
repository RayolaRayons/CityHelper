using System;
using System.Collections.Generic;
using System.Linq;
using CityHelper.Services;

namespace CityHelper.Tools;

/// <summary>The single place where tools are registered. One line per tool.</summary>
public static class ToolRegistry
{
    // The navigation order: the everyday tools, a divider, then the management tools (Options is added last by the main view model).
    // A tool that is not named here goes at the end of the second group.
    private static readonly string[] FirstGroup = { "Log Viewer", "PopmenuHero", "Player Notes", "Character Ordering", "ChatWatcher", "Bind", "Command Lookup", "Badge Lookup", "Icon Lookup" };
    private static readonly string[] SecondGroup = { "Config Sync", "Log Manager", "PIGG Viewer", "Scheduler", "Backup" };

    /// <summary>All tools in navigation order, with a divider between the two groups.</summary>
    public static IReadOnlyList<ITool> CreateAll(AppState state)
    {
        var all = Register(state).ToList();
        int Rank(ITool t, string[] group) => Array.FindIndex(group, n => n.Equals(t.Name, StringComparison.OrdinalIgnoreCase));

        var first = all.Where(t => Rank(t, FirstGroup) >= 0).OrderBy(t => Rank(t, FirstGroup));
        var second = all.Where(t => Rank(t, FirstGroup) < 0)
                        .OrderBy(t => Rank(t, SecondGroup) >= 0 ? Rank(t, SecondGroup) : int.MaxValue)
                        .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase);
        return first.Append(new SeparatorTool()).Concat(second).ToList();
    }

    private static ITool[] Register(AppState state) => new ITool[]
    {
        new PopMenuHero.PopMenuHeroTool(state),
        new Bind.BindTool(state),
        new PlayerNotes.PlayerNotesTool(state),
        new CharacterOrdering.CharacterOrderingTool(state),
        new LogViewer.LogViewerTool(state),
        new PiggViewer.PiggViewerTool(state),
        new ConfigSync.ConfigSyncTool(state),
        new Backup.BackupTool(state),
        new Scheduler.SchedulerTool(state),
        new CommandLookup.CommandLookupTool(),
        new BadgeLookup.BadgeLookupTool(),
        new IconLookup.IconLookupTool(),
        new LogManager.LogManagerTool(state),
        new ChatWatcher.ChatWatcherTool(state),
    };
}
