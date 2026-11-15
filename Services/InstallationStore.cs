using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CityHelper.Models;

namespace CityHelper.Services;

/// <summary>
/// Reads/writes the installation list to installations.ini in the application directory.
/// One [InstallationN] section per entry, in list order; exactly one carries default = true.
/// </summary>
public static class InstallationStore
{
    public static string DefaultPath => System.IO.Path.Combine(AppContext.BaseDirectory, "installations.ini");

    /// <summary>Returns null if the file doesn't exist.</summary>
    public static List<Installation>? Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            return null;

        var result = new List<Installation>();
        string? name = null, folder = null;
        bool isDefault = false, inEntry = false;

        void Flush()
        {
            if (inEntry && !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(folder))
                result.Add(new Installation(name.Trim(), folder.Trim(), isDefault));
            name = folder = null;
            isDefault = false;
        }

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
                continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                Flush();
                inEntry = line[1..^1].StartsWith("Installation", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            int eq = line.IndexOf('=');
            if (!inEntry || eq < 0)
                continue;
            var key = line[..eq].Trim().ToLowerInvariant();
            var value = line[(eq + 1)..].Trim();
            switch (key)
            {
                case "name": name = value; break;
                case "path": folder = value; break;
                case "default": isDefault = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
            }
        }
        Flush();
        return Normalize(result);
    }

    public static void Save(IReadOnlyList<Installation> items, string? path = null)
    {
        var sb = new StringBuilder();
        int i = 1;
        foreach (var item in Normalize(new List<Installation>(items)))
        {
            sb.Append("[Installation").Append(i++).Append("]\n");
            sb.Append("name = ").Append(item.Name).Append('\n');
            sb.Append("path = ").Append(item.Path).Append('\n');
            sb.Append("default = ").Append(item.IsDefault ? "true" : "false").Append("\n\n");
        }
        File.WriteAllText(path ?? DefaultPath, sb.ToString());
    }

    /// <summary>Exactly one default: the first flagged entry, otherwise the top item.</summary>
    public static List<Installation> Normalize(List<Installation> items)
    {
        int def = items.FindIndex(x => x.IsDefault);
        if (def < 0) def = 0;
        for (int i = 0; i < items.Count; i++)
            if (items[i].IsDefault != (i == def))
                items[i] = items[i] with { IsDefault = i == def };
        return items;
    }
}
