using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CityHelper.Services;

namespace CityHelper.Tools.ChatWatcher;

/// <summary>A named set of keywords plus the channels it watches.</summary>
public sealed class WatchFilter
{
    public const string MsrName = "MSR";

    public string Name { get; set; } = "";

    /// <summary>Comma separated; whole-word, case-insensitive matching (see ChatLogParser).</summary>
    public string Keywords { get; set; } = "";

    public bool Tell { get; set; }
    public bool Local { get; set; }
    public bool Broadcast { get; set; }
    public bool Request { get; set; }

    public WatchFilter Clone() => (WatchFilter)MemberwiseClone();

    public override string ToString() => Name;

    /// <summary>The keywords and channels the old CoH Invite Queue script used.</summary>
    public static WatchFilter Msr() => new()
    {
        Name = MsrName,
        Keywords = "MSR, invite, Me, join, is there room, aliens, raid",
        Tell = true,
        Local = true,
        Broadcast = true,
        Request = false,
    };

    /// <summary>What "reset to defaults" gives: the built-in values for MSR, otherwise an empty filter.</summary>
    public WatchFilter DefaultsFor() =>
        Name.Equals(MsrName, StringComparison.OrdinalIgnoreCase) ? Msr() : new WatchFilter { Name = Name };
}

/// <summary>Chat Watcher settings, stored in the [ChatWatcher.*] sections of CityHelper.ini.</summary>
public sealed class ChatWatcherSettings
{
    public List<WatchFilter> Filters { get; } = new();

    // What the pane itself currently shows (saved as you change it)
    public string SelectedFilter { get; set; } = WatchFilter.MsrName;
    public string Keywords { get; set; } = "";
    public bool WatchTell { get; set; } = true;
    public bool WatchLocal { get; set; } = true;
    public bool WatchBroadcast { get; set; } = true;
    public bool WatchRequest { get; set; }
    public bool SendDirectly { get; set; }
    public bool Monitoring { get; set; }

    // Options window
    public int SwitchDelayMs { get; set; } = 150;     // after switching to the game window
    public int PasteDelayMs { get; set; } = 400;      // after Enter opens the chat bar, before pasting
    public int SendDelayMs { get; set; } = 650;       // after pasting, before the final Enter
    public bool PlaySound { get; set; } = true;

    public static ChatWatcherSettings Load(string? path = null)
    {
        var s = new ChatWatcherSettings();
        var lines = IniStore.ReadLines("ChatWatcher", "chatwatcher.ini", path);
        bool loadedKeywords = false;

        if (lines != null)
        {
            string section = "";
            WatchFilter? filter = null;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] is '#' or ';')
                    continue;
                if (line[0] == '[' && line[^1] == ']')
                {
                    section = line[1..^1];
                    filter = null;
                    if (section.StartsWith("Filter", StringComparison.OrdinalIgnoreCase))
                    {
                        filter = new WatchFilter();
                        s.Filters.Add(filter);
                    }
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq < 0)
                    continue;
                var key = line[..eq].Trim().ToLowerInvariant();
                var value = line[(eq + 1)..].Trim();
                bool on = value.Equals("true", StringComparison.OrdinalIgnoreCase);

                if (filter != null)
                {
                    switch (key)
                    {
                        case "name": filter.Name = value; break;
                        case "keywords": filter.Keywords = value; break;
                        case "tell": filter.Tell = on; break;
                        case "local": filter.Local = on; break;
                        case "broadcast": filter.Broadcast = on; break;
                        case "request": filter.Request = on; break;
                    }
                }
                else if (section.Equals("Current", StringComparison.OrdinalIgnoreCase))
                {
                    switch (key)
                    {
                        case "filter": s.SelectedFilter = value; break;
                        case "keywords": s.Keywords = value; loadedKeywords = true; break;
                        case "tell": s.WatchTell = on; break;
                        case "local": s.WatchLocal = on; break;
                        case "broadcast": s.WatchBroadcast = on; break;
                        case "request": s.WatchRequest = on; break;
                        case "send_directly": s.SendDirectly = on; break;
                        case "monitoring": s.Monitoring = on; break;
                    }
                }
                else if (section.Equals("General", StringComparison.OrdinalIgnoreCase))
                {
                    switch (key)
                    {
                        case "switch_delay_ms": s.SwitchDelayMs = ParseMs(value, s.SwitchDelayMs); break;
                        case "paste_delay_ms": s.PasteDelayMs = ParseMs(value, s.PasteDelayMs); break;
                        case "send_delay_ms": s.SendDelayMs = ParseMs(value, s.SendDelayMs); break;
                        case "play_sound": s.PlaySound = on; break;
                    }
                }
            }
            s.Filters.RemoveAll(f => f.Name.Length == 0);
        }

        // First run: the MSR filter from the old invite-queue script
        if (s.Filters.Count == 0)
            s.Filters.Add(WatchFilter.Msr());

        if (!loadedKeywords)
        {
            var start = s.Filters.FirstOrDefault(f => f.Name.Equals(s.SelectedFilter, StringComparison.OrdinalIgnoreCase)) ?? s.Filters[0];
            s.SelectedFilter = start.Name;
            s.ApplyFilter(start);
        }
        return s;
    }

    /// <summary>Copies a filter's keywords and channels into the current pane state.</summary>
    public void ApplyFilter(WatchFilter f)
    {
        SelectedFilter = f.Name;
        Keywords = f.Keywords;
        WatchTell = f.Tell;
        WatchLocal = f.Local;
        WatchBroadcast = f.Broadcast;
        WatchRequest = f.Request;
    }

    public void Save(string? path = null)
    {
        var sb = new StringBuilder();
        sb.Append("[General]\n");
        sb.Append("switch_delay_ms = ").Append(SwitchDelayMs).Append('\n');
        sb.Append("paste_delay_ms = ").Append(PasteDelayMs).Append('\n');
        sb.Append("send_delay_ms = ").Append(SendDelayMs).Append('\n');
        sb.Append("play_sound = ").Append(Bool(PlaySound)).Append("\n\n");

        sb.Append("[Current]\n");
        sb.Append("filter = ").Append(SelectedFilter).Append('\n');
        sb.Append("keywords = ").Append(Keywords).Append('\n');
        sb.Append("tell = ").Append(Bool(WatchTell)).Append('\n');
        sb.Append("local = ").Append(Bool(WatchLocal)).Append('\n');
        sb.Append("broadcast = ").Append(Bool(WatchBroadcast)).Append('\n');
        sb.Append("request = ").Append(Bool(WatchRequest)).Append('\n');
        sb.Append("send_directly = ").Append(Bool(SendDirectly)).Append("\n\n");

        int i = 1;
        foreach (var f in Filters)
        {
            sb.Append("[Filter").Append(i++).Append("]\n");
            sb.Append("name = ").Append(f.Name).Append('\n');
            sb.Append("keywords = ").Append(f.Keywords).Append('\n');
            sb.Append("tell = ").Append(Bool(f.Tell)).Append('\n');
            sb.Append("local = ").Append(Bool(f.Local)).Append('\n');
            sb.Append("broadcast = ").Append(Bool(f.Broadcast)).Append('\n');
            sb.Append("request = ").Append(Bool(f.Request)).Append("\n\n");
        }
        IniStore.WriteText("ChatWatcher", sb.ToString(), path);
    }

    private static string Bool(bool b) => b ? "true" : "false";

    private static int ParseMs(string value, int fallback) =>
        int.TryParse(value, out var n) && n >= 0 ? n : fallback;
}
