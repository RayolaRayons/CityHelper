using System;
using System.IO;
using System.Text;

namespace CityHelper.Tools.PiggViewer;

/// <summary>Remembers the folders last used by the file dialogs, in piggviewer.ini in the application directory.</summary>
public sealed class PiggViewerSettings
{
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "piggviewer.ini");

    public string LastArchiveFolder { get; set; } = "";
    public string LastOpenFolder { get; set; } = "";
    public string LastExportFolder { get; set; } = "";
    public string LastNewArchiveSource { get; set; } = "";
    public string LastNewArchiveFolder { get; set; } = "";
    public string LastTextureFolder { get; set; } = "";

    public static PiggViewerSettings Load()
    {
        var s = new PiggViewerSettings();
        if (!File.Exists(DefaultPath))
            return s;
        foreach (var raw in File.ReadAllLines(DefaultPath))
        {
            var line = raw.Trim();
            int eq = line.IndexOf('=');
            if (line.Length == 0 || line[0] is '#' or ';' or '[' || eq < 0)
                continue;
            var value = line[(eq + 1)..].Trim();
            switch (line[..eq].Trim().ToLowerInvariant())
            {
                case "last_archive_folder": s.LastArchiveFolder = value; break;
                case "last_open_folder": s.LastOpenFolder = value; break;
                case "last_export_folder": s.LastExportFolder = value; break;
                case "last_new_archive_source": s.LastNewArchiveSource = value; break;
                case "last_new_archive_folder": s.LastNewArchiveFolder = value; break;
                case "last_texture_folder": s.LastTextureFolder = value; break;
            }
        }
        return s;
    }

    public void Save()
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append("[Folders]\n");
            sb.Append("last_archive_folder = ").Append(LastArchiveFolder).Append('\n');
            sb.Append("last_open_folder = ").Append(LastOpenFolder).Append('\n');
            sb.Append("last_export_folder = ").Append(LastExportFolder).Append('\n');
            sb.Append("last_new_archive_source = ").Append(LastNewArchiveSource).Append('\n');
            sb.Append("last_new_archive_folder = ").Append(LastNewArchiveFolder).Append('\n');
            File.WriteAllText(DefaultPath, sb.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Remembering folders is a convenience; a read-only install folder shouldn't break the viewer
        }
    }
}
