using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CityHelper.Tools.Bind;

namespace CityHelper.Tools.PlayerNotes;

/// <summary>One player: global name, star rating, the characters seen and free notes (real line breaks).</summary>
public sealed record PlayerNote(string Name, int Stars, string Characters, string Notes);

/// <summary>
/// playernotes.txt: one line per player, <c>"globalname" "stars" "characters" "notes"</c>. Notes use a literal \n for
/// line breaks and (null) when empty. Lines that do not match are kept as they are.
/// </summary>
public sealed class PlayerNotesFile
{
    private const string NullText = "(null)";
    private static readonly Regex LineRegex = new(@"^\s*""([^""]*)""\s+""(\d+)""\s+""([^""]*)""\s+""([^""]*)""\s*$", RegexOptions.Compiled);

    public List<PlayerNote> Notes { get; } = new();

    /// <summary>Lines that could not be parsed; written back unchanged after the players.</summary>
    public List<string> Unparsed { get; } = new();

    public static PlayerNotesFile Load(string path)
    {
        var file = new PlayerNotesFile();
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Trim().Length == 0)
                continue;
            var m = LineRegex.Match(line);
            if (!m.Success)
            {
                file.Unparsed.Add(line);
                continue;
            }
            var notes = m.Groups[4].Value;
            notes = notes == NullText ? "" : notes.Replace("\\n", "\n");
            file.Notes.Add(new PlayerNote(m.Groups[1].Value, int.Parse(m.Groups[2].Value), m.Groups[3].Value, notes));
        }
        return file;
    }

    public void Save(string path)
    {
        BindFile.Backup(path);      // keeps the original in a "backup" subfolder
        var sb = new StringBuilder();
        foreach (var n in Notes)
        {
            var notes = n.Notes.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\n");
            sb.Append('"').Append(Clean(n.Name)).Append("\" \"").Append(n.Stars).Append("\" \"")
              .Append(Clean(n.Characters)).Append("\" \"").Append(notes.Length == 0 ? NullText : Clean(notes)).Append("\"\r\n");
        }
        foreach (var line in Unparsed)
            sb.Append(line).Append("\r\n");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    // A quote inside a field would break the line format
    private static string Clean(string s) => s.Replace('"', '\'').Trim();
}
