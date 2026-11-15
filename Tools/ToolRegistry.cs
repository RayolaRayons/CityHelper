using System.Collections.Generic;
using System.Linq;
using CityHelper.Services;

namespace CityHelper.Tools;

/// <summary>The single place where tools are registered. One line per tool.</summary>
public static class ToolRegistry
{
    /// <summary>All tools, with finished ones listed first (registration order is kept within each group).</summary>
    public static IReadOnlyList<ITool> CreateAll(AppState state) => Register(state).OrderByDescending(t => t.IsComplete).ToList();

    private static ITool[] Register(AppState state) => new ITool[]
    {
        new PopMenuHero.PopMenuHeroTool(state),
        new Bind.BindTool(state),
        new PlayerNotes.PlayerNotesTool(state),
        new CharacterOrdering.CharacterOrderingTool(state),
        new LogViewer.LogViewerTool(state),
        new PlaceholderTool("Texture Mod generator"),
        new PiggViewer.PiggViewerTool(state),
        new ConfigSync.ConfigSyncTool(state),
        new LogManager.LogManagerTool(state),
        new ChatWatcher.ChatWatcherTool(state),
    };
}
