using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CityHelper.Services;

namespace CityHelper.Tools.ConfigSync;

/// <summary>Settings of one environment (an installation) as stored in CityHelper.ini.</summary>
public sealed class EnvironmentSettings
{
    public EnvironmentSettings(string name) => Name = name;

    public string Name { get; set; }
    public bool UseDefault { get; set; }
    public string BackupFolder { get; set; } = "";

    /// <summary>Folder under settings this installation uses (live, beta, ...). Never inherited.</summary>
    public string GameEnvironment { get; set; } = SyncItems.DefaultGameEnvironment;

    public bool MergeLogfiles { get; set; }
    public bool MergePrivateLogs { get; set; }
    public HashSet<string> EnabledItems { get; set; } = new(StringComparer.Ordinal);

    public EnvironmentSettings Clone() => new(Name)
    {
        UseDefault = UseDefault,
        BackupFolder = BackupFolder,
        GameEnvironment = GameEnvironment,
        MergeLogfiles = MergeLogfiles,
        MergePrivateLogs = MergePrivateLogs,
        EnabledItems = new HashSet<string>(EnabledItems, StringComparer.Ordinal),
    };

    /// <summary>Copies the inheritable values (not the name, use-default flag or game environment).</summary>
    public void CopyValuesFrom(EnvironmentSettings other)
    {
        BackupFolder = other.BackupFolder;
        MergeLogfiles = other.MergeLogfiles;
        MergePrivateLogs = other.MergePrivateLogs;
        EnabledItems = new HashSet<string>(other.EnabledItems, StringComparer.Ordinal);
    }
}

/// <summary>All Config Sync settings, persisted to the [ConfigSync.*] sections of CityHelper.ini.</summary>
public sealed class ConfigSyncSettings
{
    public string DefaultEnvironment { get; set; } = "";

    /// <summary>
    /// When merging logs, a daily log that is already fully inside a weekly / monthly log (locally) is archived
    /// instead of being kept, and one that exists only in the sync folder is not copied back.
    /// </summary>
    public bool ValidateOrphanedLogs { get; set; }
    public List<EnvironmentSettings> Environments { get; } = new();

    public EnvironmentSettings? Find(string name) =>
        Environments.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The settings that actually apply to an environment: its own, or the default's if it inherits.</summary>
    public EnvironmentSettings Effective(string name)
    {
        var env = Find(name) ?? NewEnvironment(name, isDefault: false);
        if (env.UseDefault && !env.Name.Equals(DefaultEnvironment, StringComparison.OrdinalIgnoreCase)
            && Find(DefaultEnvironment) is { } def)
            return def;
        return env;
    }

    /// <summary>Fresh settings: the default environment starts with everything enabled; others inherit from it.</summary>
    public static EnvironmentSettings NewEnvironment(string name, bool isDefault) => new(name)
    {
        UseDefault = !isDefault,
        EnabledItems = new HashSet<string>(SyncItems.All.Select(i => i.Key), StringComparer.Ordinal),
    };

    /// <summary>Loads the file, then makes sure every given environment exists and a valid default is set.</summary>
    public static ConfigSyncSettings Load(IReadOnlyList<string> environmentNames, string? defaultSuggestion, string? path = null)
    {
        var settings = new ConfigSyncSettings();
        var lines = IniStore.ReadLines("ConfigSync", "configsync.ini", path);
        if (lines != null)
            settings.Parse(lines);

        settings.Environments.RemoveAll(e => !environmentNames.Contains(e.Name, StringComparer.OrdinalIgnoreCase));
        if (settings.Find(settings.DefaultEnvironment) is null)
            settings.DefaultEnvironment = environmentNames.Contains(defaultSuggestion ?? "") ? defaultSuggestion!
                                          : environmentNames.FirstOrDefault() ?? "";
        foreach (var name in environmentNames)
            if (settings.Find(name) is null)
                settings.Environments.Add(NewEnvironment(name, name.Equals(settings.DefaultEnvironment, StringComparison.OrdinalIgnoreCase)));

        // The default never inherits
        if (settings.Find(settings.DefaultEnvironment) is { } def)
            def.UseDefault = false;
        return settings;
    }

    public void Save(string? path = null)
    {
        var sb = new StringBuilder();
        sb.Append("[General]\n");
        sb.Append("default_environment = ").Append(DefaultEnvironment).Append('\n');
        sb.Append("validate_orphaned_logs = ").Append(Bool(ValidateOrphanedLogs)).Append("\n\n");

        int i = 1;
        foreach (var env in Environments)
        {
            sb.Append("[Environment").Append(i++).Append("]\n");
            sb.Append("name = ").Append(env.Name).Append('\n');
            sb.Append("use_default = ").Append(Bool(env.UseDefault)).Append('\n');
            sb.Append("backup_folder = ").Append(env.BackupFolder).Append('\n');
            sb.Append("game_environment = ").Append(env.GameEnvironment).Append('\n');
            sb.Append("merge_logfiles = ").Append(Bool(env.MergeLogfiles)).Append('\n');
            sb.Append("merge_private_logs = ").Append(Bool(env.MergePrivateLogs)).Append('\n');
            foreach (var item in SyncItems.All)
                sb.Append(item.Key).Append(" = ").Append(Bool(env.EnabledItems.Contains(item.Key))).Append('\n');
            sb.Append('\n');
        }
        IniStore.WriteText("ConfigSync", sb.ToString(), path);
    }

    private static string Bool(bool b) => b ? "true" : "false";

    private void Parse(IEnumerable<string> lines)
    {
        EnvironmentSettings? current = null;
        bool inGeneral = false;
        var items = SyncItems.All.Select(i => i.Key).ToHashSet();

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
                continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                var section = line[1..^1];
                inGeneral = section.Equals("General", StringComparison.OrdinalIgnoreCase);
                current = null;
                if (section.StartsWith("Environment", StringComparison.OrdinalIgnoreCase))
                {
                    current = new EnvironmentSettings("");
                    Environments.Add(current);
                }
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq < 0)
                continue;
            var key = line[..eq].Trim().ToLowerInvariant();
            var value = line[(eq + 1)..].Trim();
            bool on = value.Equals("true", StringComparison.OrdinalIgnoreCase);

            if (inGeneral && key == "default_environment")
                DefaultEnvironment = value;
            else if (inGeneral && key == "validate_orphaned_logs")
                ValidateOrphanedLogs = on;
            else if (current != null)
            {
                switch (key)
                {
                    case "name": current.Name = value; break;
                    case "use_default": current.UseDefault = on; break;
                    case "backup_folder": current.BackupFolder = value; break;
                    case "game_environment": current.GameEnvironment = value.ToLowerInvariant(); break;
                    case "merge_logfiles": current.MergeLogfiles = on; break;
                    case "merge_private_logs":
                    case "merge_private_notes":   // older name of this setting
                        current.MergePrivateLogs = on; break;
                    default:
                        if (items.Contains(key) && on)
                            current.EnabledItems.Add(key);
                        break;
                }
            }
        }
        Environments.RemoveAll(e => e.Name.Length == 0);
    }
}
