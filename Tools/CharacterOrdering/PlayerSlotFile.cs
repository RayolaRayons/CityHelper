using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CityHelper.Tools.CharacterOrdering;

/// <summary>One parsed playerslot.txt line: "account" "server" "name" "position".</summary>
public sealed class SlotEntry
{
    public SlotEntry(string account, string server, string name, int position)
    {
        Account = account;
        Server = server;
        Name = name;
        Position = position;
    }

    public string Account { get; }
    public string Server { get; }
    public string Name { get; }
    public int Position { get; set; }

    public string ToLine() => $"\"{Account}\" \"{Server}\" \"{Name}\" \"{Position}\"";
}

/// <summary>
/// playerslot.txt. Lines that don't parse are kept verbatim so saving never drops data,
/// and entry order within the file is preserved (only Position values change).
/// </summary>
public sealed class PlayerSlotFile
{
    private static readonly Regex LineRegex =
        new("^\\s*\"([^\"]*)\"\\s+\"([^\"]*)\"\\s+\"([^\"]*)\"\\s+\"(\\d+)\"\\s*$", RegexOptions.Compiled);

    private sealed record Line(string? Raw, SlotEntry? Entry);

    private readonly List<Line> _lines = new();
    private string _newline = "\r\n";
    private bool _trailingNewline = true;

    public IEnumerable<SlotEntry> Entries => _lines.Where(l => l.Entry != null).Select(l => l.Entry!);

    public static PlayerSlotFile Load(string path)
    {
        var text = File.ReadAllText(path);
        var file = new PlayerSlotFile
        {
            _newline = text.Contains("\r\n") ? "\r\n" : text.Contains('\n') ? "\n" : "\r\n",
            _trailingNewline = text.EndsWith('\n') || text.Length == 0,
        };

        var rows = text.Replace("\r\n", "\n").Split('\n');
        int count = file._trailingNewline && rows.Length > 0 && rows[^1].Length == 0 ? rows.Length - 1 : rows.Length;
        for (int i = 0; i < count; i++)
        {
            var m = LineRegex.Match(rows[i]);
            if (m.Success && int.TryParse(m.Groups[4].Value, out int pos))
                file._lines.Add(new Line(null, new SlotEntry(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, pos)));
            else
                file._lines.Add(new Line(rows[i], null));
        }
        return file;
    }

    public void Save(string path)
    {
        var text = string.Join(_newline, _lines.Select(l => l.Entry?.ToLine() ?? l.Raw));
        if (_trailingNewline && _lines.Count > 0)
            text += _newline;
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }
}
