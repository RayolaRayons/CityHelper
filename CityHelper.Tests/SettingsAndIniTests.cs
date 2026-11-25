using CityHelper.Services;
using CityHelper.Tools.LogManager;
using Xunit;

namespace CityHelper.Tests;

/// <summary>Reading and writing settings, including the one shared CityHelper.ini.</summary>
[Collection("ini")]
public class SettingsAndIniTests
{
    [Fact]
    public void Each_part_keeps_to_its_own_sections_and_replaces_only_those()
    {
        var a = "TestA" + Guid.NewGuid().ToString("N")[..6];
        var b = "TestB" + Guid.NewGuid().ToString("N")[..6];
        try
        {
            IniStore.WriteText(a, "[Options]\nname = one\n\n[Items]\nx = true\n");
            IniStore.WriteText(b, "[Options]\nname = two\n");
            IniStore.WriteText(a, "[Options]\nname = changed\n");                  // rewrites A, leaves B

            Assert.Contains("name = changed", string.Join("\n", IniStore.ReadLines(a, "")!));
            Assert.DoesNotContain("x = true", string.Join("\n", IniStore.ReadLines(a, "")!));
            Assert.Contains("name = two", string.Join("\n", IniStore.ReadLines(b, "")!));
            Assert.Contains($"[{a}.Options]", File.ReadAllText(IniStore.FilePath));
        }
        finally
        {
            IniStore.WriteText(a, "");
            IniStore.WriteText(b, "");
        }
    }

    [Fact]
    public void Text_before_the_first_header_becomes_the_parts_plain_section()
    {
        var p = "TestC" + Guid.NewGuid().ToString("N")[..6];
        try
        {
            IniStore.WriteText(p, "key = value\n");
            Assert.Equal(new[] { "key = value" }, IniStore.ReadLines(p, "")!.Where(l => l.Trim().Length > 0).ToArray());
            Assert.Contains($"[{p}]", File.ReadAllText(IniStore.FilePath));
        }
        finally { IniStore.WriteText(p, ""); }
    }

    [Fact]
    public void Part_that_was_never_written_reads_as_null()
    {
        Assert.Null(IniStore.ReadLines("NeverWritten" + Guid.NewGuid().ToString("N")[..6], "does-not-exist.ini"));
    }

    [Fact]
    public void Older_Log_Manager_files_are_folded_into_the_new_options()
    {
        using var t = new TempDir();
        var path = t.Write("old.ini",
            "[Options]", "preset = RP", "backup_merged_files = false", "backup_on_cleanup = false",
            "cleanup_backup_folder = D:\\Copies", "auto_fix_system_channel = true", "fix_timestamped_system_messages = false", "merge_mode = Weekly");

        var s = LogManagerSettings.Load(path);

        Assert.False(s.KeepCopies);                                   // both older backup options were off
        Assert.Equal("D:\\Copies", s.CopiesFolder);
        Assert.Equal(LogManagerSettings.FixAtMerge, s.SystemChannelFix);
        Assert.True(s.FixAtMergeEnabled);
        Assert.Equal(MergeType.Weekly, s.MergeType);
    }

    [Fact]
    public void Either_older_backup_option_on_keeps_copies()
    {
        using var t = new TempDir();
        var path = t.Write("old.ini", "[Options]", "backup_merged_files = false", "backup_on_cleanup = true");

        Assert.True(LogManagerSettings.Load(path).KeepCopies);
    }

    [Fact]
    public void Defaults_keep_copies_fix_by_hand_and_do_not_merge()
    {
        var s = new LogManagerSettings();

        Assert.True(s.KeepCopies);
        Assert.Equal(LogManagerSettings.FixManual, s.SystemChannelFix);
        Assert.Equal(MergeType.Selected, s.MergeType);                // "No merge"
        Assert.Equal("", s.CopiesFolder);
    }

    [Fact]
    public void Log_Manager_settings_round_trip_through_a_file()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "lm.ini");
        var s = new LogManagerSettings
        {
            KeepCopies = false, CopiesFolder = "E:\\Keep", SystemChannelFix = LogManagerSettings.FixAtMerge,
            MergeMode = LogManagerSettings.MergeMonthly, ValidateOrphanedDailies = true,
            IncludedChannels = new HashSet<LogChannel> { LogChannel.Local, LogChannel.Reward },
        };
        s.Save(path);

        var back = LogManagerSettings.Load(path);

        Assert.False(back.KeepCopies);
        Assert.Equal("E:\\Keep", back.CopiesFolder);
        Assert.True(back.FixAtMergeEnabled);
        Assert.Equal(MergeType.Monthly, back.MergeType);
        Assert.True(back.ValidateOrphanedDailies);
        Assert.Equal(new HashSet<LogChannel> { LogChannel.Local, LogChannel.Reward }, back.IncludedChannels);
    }
}
