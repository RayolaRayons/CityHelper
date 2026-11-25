using CityHelper.Tools.ConfigSync;
using Xunit;

namespace CityHelper.Tests;

/// <summary>Config Sync's two-way log merge. In the "ini" collection: it reads Log Manager's options (the copies folder).</summary>
[Collection("ini")]
public class LogMergerTests
{
    [Fact]
    public void A_log_on_one_side_only_is_copied_across()
    {
        using var t = new TempDir();
        t.Write("local/chatlog 2026-11-01.txt", "2026-11-01 10:00:00 [Local] A: hi");
        t.Write("sync/chatlog 2026-11-02.txt", "2026-11-02 10:00:00 [Local] B: yo");

        var result = LogMerger.Merge(t.Dir("local"), t.Dir("sync"));

        Assert.Equal(2, result.Copied);
        Assert.True(File.Exists(Path.Combine(t.Path, "sync", "chatlog 2026-11-01.txt")));
        Assert.True(File.Exists(Path.Combine(t.Path, "local", "chatlog 2026-11-02.txt")));
    }

    [Fact]
    public void The_same_log_with_different_content_is_merged_into_both_sides()
    {
        using var t = new TempDir();
        t.Write("local/chatlog 2026-11-01.txt", "2026-11-01 10:00:00 [Local] A: one");
        t.Write("sync/chatlog 2026-11-01.txt", "2026-11-01 10:00:05 [Local] B: two");

        var result = LogMerger.Merge(t.Dir("local"), t.Dir("sync"));

        Assert.Equal(1, result.Merged);
        var expected = new[] { "2026-11-01 10:00:00 [Local] A: one", "2026-11-01 10:00:05 [Local] B: two" };
        Assert.Equal(expected, File.ReadAllLines(Path.Combine(t.Path, "local", "chatlog 2026-11-01.txt")));
        Assert.Equal(expected, File.ReadAllLines(Path.Combine(t.Path, "sync", "chatlog 2026-11-01.txt")));
    }

    [Fact]
    public void A_log_that_Log_Manager_archived_is_not_copied_back()
    {
        using var t = new TempDir();
        t.Write("local/Archive/chatlog 2026-11-12.txt", "2026-11-12 10:00:00 [Local] A: hi");
        t.Write("sync/chatlog 2026-11-12.txt", "2026-11-12 10:00:00 [Local] A: hi");           // same name and size as the archived one

        var result = LogMerger.Merge(t.Dir("local"), t.Dir("sync"));

        Assert.Equal(0, result.Copied);
        Assert.False(File.Exists(Path.Combine(t.Path, "local", "chatlog 2026-11-12.txt")));
    }

    [Fact]
    public void An_archived_log_with_a_different_size_is_not_taken_for_the_same_one()
    {
        using var t = new TempDir();
        t.Write("local/Archive/chatlog 2026-11-12.txt", "2026-11-12 10:00:00 [Local] A: hi");
        t.Write("sync/chatlog 2026-11-12.txt", "2026-11-12 10:00:00 [Local] A: hi", "2026-11-12 10:00:05 [Local] A: more");

        LogMerger.Merge(t.Dir("local"), t.Dir("sync"));

        Assert.True(File.Exists(Path.Combine(t.Path, "local", "chatlog 2026-11-12.txt")));
    }

    [Fact]
    public void With_orphan_validation_a_sync_only_day_inside_a_weekly_log_is_not_copied_back()
    {
        using var t = new TempDir();
        t.Write("local/chatlogWeekly 2026-11-09.txt", "2026-11-10 10:00:00 [Local] A: hi", "2026-11-11 10:00:00 [Local] B: yo");
        t.Write("sync/chatlog 2026-11-10.txt", "2026-11-10 10:00:00 [Local] A: hi");

        var result = LogMerger.Merge(t.Dir("local"), t.Dir("sync"), validateOrphans: true);

        Assert.False(File.Exists(Path.Combine(t.Path, "local", "chatlog 2026-11-10.txt")));
        Assert.True(File.Exists(Path.Combine(t.Path, "sync", "chatlogWeekly 2026-11-09.txt")));       // the weekly log itself is backed up
        Assert.Equal(1, result.Copied);
    }

    [Fact]
    public void Without_orphan_validation_the_same_day_is_copied_back()
    {
        using var t = new TempDir();
        t.Write("local/chatlogWeekly 2026-11-09.txt", "2026-11-10 10:00:00 [Local] A: hi");
        t.Write("sync/chatlog 2026-11-10.txt", "2026-11-10 10:00:00 [Local] A: hi");

        LogMerger.Merge(t.Dir("local"), t.Dir("sync"), validateOrphans: false);

        Assert.True(File.Exists(Path.Combine(t.Path, "local", "chatlog 2026-11-10.txt")));
    }
}
