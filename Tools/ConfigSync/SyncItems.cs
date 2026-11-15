using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CityHelper.Tools.ConfigSync;

/// <summary>
/// One kind of thing that can be synced. Paths are relative to the installation folder (and mirrored under the
/// sync folder). {account} = the selected account's folder, {env} = the game environment folder under settings.
/// An item may cover several paths; a folder item may exclude one nested subfolder that is its own item,
/// and may be limited to files matching a pattern (e.g. *.txt), optionally without descending into subfolders.
/// </summary>
public sealed record SyncItemDef(string Key, string DisplayName, string[] PathTemplates, string? ExcludeTemplate = null,
                                 string? FilePattern = null, bool Recursive = true)
{
    public IReadOnlyList<string> RelativePaths(string account, string env) =>
        PathTemplates.Select(t => Expand(t, account, env)).ToList();

    public string? RelativeExclude(string account, string env) =>
        ExcludeTemplate is null ? null : Expand(ExcludeTemplate, account, env);

    private static string Expand(string template, string account, string env) =>
        template.Replace("{account}", account).Replace("{env}", env).Replace('/', Path.DirectorySeparatorChar);
}

/// <summary>The syncable items and where they live. This table is the single place those paths are defined.</summary>
public static class SyncItems
{
    /// <summary>Folder names valid under settings in an installation.</summary>
    public static readonly IReadOnlyList<string> GameEnvironments =
        new[] { "live", "beta", "closedbeta", "experimental", "launcher" };

    public const string DefaultGameEnvironment = "live";

    public static readonly IReadOnlyList<SyncItemDef> All = new[]
    {
        new SyncItemDef("mods", "Mods", new[] { "assets/mods" }),
        // Only the .txt files directly in logs; private chat history (logs\private) is its own item
        new SyncItemDef("logfiles", "Logfiles", new[] { "accounts/{account}/logs" }, FilePattern: "*.txt", Recursive: false),
        new SyncItemDef("private_logs", "Private Logs", new[] { "accounts/{account}/logs/private" }),
        new SyncItemDef("player_notes", "Player Notes", new[] { "accounts/{account}/playernotes.txt" }),
        new SyncItemDef("popmenus", "Popmenus", new[] { "data/texts/English/menus" }),
        new SyncItemDef("chat_options", "Chat Options", new[] { "settings/{env}/chat.txt" }),
        new SyncItemDef("client_options", "Client Options", new[] { "settings/{env}/client.json" }),
        new SyncItemDef("player_slots", "Player Slots", new[] { "accounts/{account}/playerslot.txt" }),
        new SyncItemDef("binds", "Binds", new[] { "Binds", "binds.txt", "settings/{env}/keybinds.txt" }),
        new SyncItemDef("costume_files", "Costume Files", new[] { "costumes" }),
        new SyncItemDef("architect", "Architect Files", new[] { "architect" }),
    };
}
