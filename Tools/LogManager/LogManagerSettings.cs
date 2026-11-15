using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CityHelper.Tools.LogManager;

/// <summary>Log Manager options, stored in logmanager.ini in the application directory.</summary>
public sealed class LogManagerSettings
{
    public const string PresetNone = "None";
    public const string PresetRp = "RP";
    public static readonly IReadOnlyList<string> Presets = new[] { PresetNone, PresetRp };

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "logmanager.ini");

    public string Preset { get; set; } = PresetNone;

    /// <summary>Move merged source files (and the originals of cleaned files) into an Archive subfolder.</summary>
    public bool BackupMergedFiles { get; set; } = true;

    /// <summary>Channels to keep; cleanup removes the others. All selected initially.</summary>
    public HashSet<LogChannel> IncludedChannels { get; set; } = new(LogChannels.All);

    public static LogManagerSettings Load(string? path = null)
    {
        var settings = new LogManagerSettings();
        path ??= DefaultPath;
        if (!File.Exists(path))
            return settings;

        var channels = new Dictionary<string, bool>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            int eq = line.IndexOf('=');
            if (line.Length == 0 || line[0] is '#' or ';' or '[' || eq < 0)
                continue;
            var key = line[..eq].Trim().ToLowerInvariant();
            var value = line[(eq + 1)..].Trim();
            switch (key)
            {
                case "preset":
                    settings.Preset = Presets.FirstOrDefault(p => p.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? PresetNone;
                    break;
                case "backup_merged_files":
                    settings.BackupMergedFiles = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
                default:
                    channels[key] = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }

        // Channels missing from the file count as selected
        settings.IncludedChannels = new HashSet<LogChannel>(
            LogChannels.All.Where(c => channels.GetValueOrDefault(LogChannels.Key(c), true)));
        return settings;
    }

    public void Save(string? path = null)
    {
        var sb = new StringBuilder();
        sb.Append("[Options]\n");
        sb.Append("preset = ").Append(Preset).Append('\n');
        sb.Append("backup_merged_files = ").Append(BackupMergedFiles ? "true" : "false").Append("\n\n");
        sb.Append("[Channels]\n");
        foreach (var c in LogChannels.All)
            sb.Append(LogChannels.Key(c)).Append(" = ").Append(IncludedChannels.Contains(c) ? "true" : "false").Append('\n');
        File.WriteAllText(path ?? DefaultPath, sb.ToString());
    }
}
