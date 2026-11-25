using CityHelper.Tools.LogManager;
using Xunit;

namespace CityHelper.Tests;

public class LogOperationsTests
{
    [Theory]
    [InlineData("chatlog 2026-11-03.txt", false)]
    [InlineData("chatlog 2026-11-03 (2).txt", false)]
    [InlineData("chatlogWeekly 2026-11-02.txt", true)]
    [InlineData("chatlogMonthly 2026-11-01.txt", true)]
    [InlineData("chatlogMerged 2026-11-03.txt", true)]
    public void Only_logs_made_by_a_merge_count_as_merged(string name, bool merged) => Assert.Equal(merged, LogOperations.IsMergedLog(name));

    [Fact]
    public void InsertChannels_adds_channels_and_leaves_emotes_and_bracketed_lines_alone()
    {
        using var t = new TempDir();
        var file = t.Write("logs/chatlog 2026-11-18.txt",
            "2026-11-18 20:54:57 You have defeated Bombardier",
            "2026-11-18 20:48:25 You received Respite.",
            "2026-11-18 22:17:04 You bid 5,000,000 total on 10 Chronal Skip.",
            "2026-11-18 20:00:00 Psyber waves.",
            "2026-11-18 20:00:01 [Local] Psyber: hi");

        var result = LogOperations.InsertChannels(t.Dir("logs"), file, backup: false);

        Assert.Empty(result.Errors);
        Assert.Equal(new[]
        {
            "2026-11-18 20:54:57 [Combat] You have defeated Bombardier",
            "2026-11-18 20:48:25 [Reward] You received Respite.",
            "2026-11-18 22:17:04 [Auction] You bid 5,000,000 total on 10 Chronal Skip.",
            "2026-11-18 20:00:00 Psyber waves.",
            "2026-11-18 20:00:01 [Local] Psyber: hi",
        }, File.ReadAllLines(file));
    }

    [Fact]
    public void InsertChannels_retags_lines_that_were_marked_System_but_are_something_else()
    {
        using var t = new TempDir();
        var file = t.Write("logs/chatlog 2026-11-18.txt",
            "2026-11-18 20:54:57 [System] You have defeated Bombardier",
            "2026-11-18 20:54:58 [System] Team invite sent!");

        LogOperations.InsertChannels(t.Dir("logs"), file, backup: false);

        Assert.Equal(new[]
        {
            "2026-11-18 20:54:57 [Combat] You have defeated Bombardier",
            "2026-11-18 20:54:58 [System] Team invite sent!",
        }, File.ReadAllLines(file));
    }

    [Fact]
    public void FixTimestampedSystemMessages_replaces_a_time_used_as_channel_and_keeps_a_timestamped_copy()
    {
        using var t = new TempDir();
        var file = t.Write("logs/chatlog 2026-11-06.txt",
            "2026-11-06 05:53:10 [05:53] You can claim 105 tickets from Architect Entertainment.",
            "2026-11-06 05:53:11 [Local] A: hi");
        var copies = t.Dir("copies");

        var result = LogOperations.FixTimestampedSystemMessages(t.Dir("logs"), file, backup: true, copiesFolder: copies);

        Assert.Empty(result.Errors);
        Assert.Equal("2026-11-06 05:53:10 [System] You can claim 105 tickets from Architect Entertainment.", File.ReadLines(file).First());
        var copy = Assert.Single(Directory.GetFiles(copies));
        Assert.Matches(@"chatlog 2026-11-06_\d{8}-\d{6}\.txt$", copy);
        Assert.Contains("[05:53]", File.ReadAllText(copy));            // the copy is the original
    }

    [Fact]
    public void FixTimestampedSystemMessages_without_copies_changes_the_file_only()
    {
        using var t = new TempDir();
        var file = t.Write("logs/chatlog 2026-11-06.txt", "2026-11-06 05:53:10 [05:53] hello");

        LogOperations.FixTimestampedSystemMessages(t.Dir("logs"), file, backup: false);

        Assert.False(Directory.Exists(Path.Combine(t.Path, "logs", "Archive")));
        Assert.Contains("[System]", File.ReadAllText(file));
    }

    [Fact]
    public void Cleanup_removes_unchecked_channels_and_PreviewCleanup_predicts_it()
    {
        using var t = new TempDir();
        var file = t.Write("logs/chatlog 2026-11-18.txt",
            "2026-11-18 10:00:00 [Local] A: hi",
            "2026-11-18 10:00:01 Head Splitter is recharged.",
            "2026-11-18 10:00:02 You received Respite.");
        var keep = new HashSet<LogChannel> { LogChannel.Local };

        Assert.Equal((2, 3), LogOperations.PreviewCleanup(file, keep));
        var result = LogOperations.Cleanup(t.Dir("logs"), new[] { file }, keep, backup: true, backupFolder: t.Dir("bk"));

        Assert.Empty(result.Errors);
        Assert.Equal(new[] { "2026-11-18 10:00:00 [Local] A: hi" }, File.ReadAllLines(file));
        Assert.Single(Directory.GetFiles(t.Path + "/bk"));
    }

    [Fact]
    public void Merge_weekly_combines_the_days_and_moves_the_sources_to_the_copies_folder()
    {
        using var t = new TempDir();
        var a = t.Write("logs/chatlog 2026-11-03.txt", "2026-11-03 10:00:00 [Local] A: one");
        var b = t.Write("logs/chatlog 2026-11-04.txt", "2026-11-04 10:00:00 [Local] B: two");
        var copies = t.Dir("copies");

        var result = LogOperations.Merge(t.Dir("logs"), new[] { a, b }, MergeType.Weekly, backup: true, copiesFolder: copies);

        Assert.Empty(result.Errors);
        var merged = Path.Combine(t.Path, "logs", "chatlogWeekly 2026-11-02.txt");        // the Monday of that week
        Assert.Equal(new[] { "2026-11-03 10:00:00 [Local] A: one", "2026-11-04 10:00:00 [Local] B: two" }, File.ReadAllLines(merged));
        Assert.False(File.Exists(a));
        Assert.Equal(2, Directory.GetFiles(copies).Length);
    }

    [Fact]
    public void Merge_monthly_takes_a_single_day_into_a_new_monthly_log()
    {
        using var t = new TempDir();
        var day = t.Write("logs/chatlog 2026-11-18.txt", "2026-11-18 10:00:00 [Local] A: hi");

        var result = LogOperations.Merge(t.Dir("logs"), new[] { day }, MergeType.Monthly, backup: true);

        Assert.Empty(result.Errors);
        Assert.Equal(new[] { "2026-11-18 10:00:00 [Local] A: hi" }, File.ReadAllLines(Path.Combine(t.Path, "logs", "chatlogMonthly 2026-11-01.txt")));
        Assert.False(File.Exists(day));
        Assert.True(File.Exists(Path.Combine(t.Path, "logs", "Archive", "chatlog 2026-11-18.txt")));
    }

    [Fact]
    public void Merge_monthly_adds_a_day_to_the_existing_monthly_log_and_keeps_the_old_one_as_a_copy()
    {
        using var t = new TempDir();
        var monthly = t.Write("logs/chatlogMonthly 2026-11-01.txt", "2026-11-02 10:00:00 [Local] A: early");
        var day = t.Write("logs/chatlog 2026-11-18.txt", "2026-11-18 10:00:00 [Local] B: later");

        var result = LogOperations.Merge(t.Dir("logs"), new[] { day }, MergeType.Monthly, backup: true);

        Assert.Empty(result.Errors);
        Assert.Contains("into the existing", string.Join(" ", result.Messages));
        Assert.Equal(new[] { "2026-11-02 10:00:00 [Local] A: early", "2026-11-18 10:00:00 [Local] B: later" }, File.ReadAllLines(monthly));
        Assert.False(File.Exists(day));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(t.Path, "logs", "Archive")).Length);       // the day and the monthly log as it was
    }

    [Fact]
    public void Merge_weekly_puts_each_day_in_its_own_weeks_log()
    {
        using var t = new TempDir();
        var a = t.Write("logs/chatlog 2026-11-03.txt", "2026-11-03 10:00:00 [Local] A: one");
        var b = t.Write("logs/chatlog 2026-11-10.txt", "2026-11-10 10:00:00 [Local] B: two");

        LogOperations.Merge(t.Dir("logs"), new[] { a, b }, MergeType.Weekly, backup: false);

        Assert.True(File.Exists(Path.Combine(t.Path, "logs", "chatlogWeekly 2026-11-02.txt")));
        Assert.True(File.Exists(Path.Combine(t.Path, "logs", "chatlogWeekly 2026-11-09.txt")));
    }

    [Fact]
    public void Merge_selected_still_needs_two_files()
    {
        using var t = new TempDir();
        var a = t.Write("logs/chatlog 2026-11-03.txt", "2026-11-03 10:00:00 [Local] A: one");

        var result = LogOperations.Merge(t.Dir("logs"), new[] { a }, MergeType.Selected, backup: false);

        Assert.Contains("only one file", string.Join(" ", result.Messages));
        Assert.True(File.Exists(a));
    }

    [Fact]
    public void Merge_without_copies_leaves_the_source_files()
    {
        using var t = new TempDir();
        var a = t.Write("logs/chatlog 2026-11-03.txt", "2026-11-03 10:00:00 [Local] A: one");
        var b = t.Write("logs/chatlog 2026-11-04.txt", "2026-11-04 10:00:00 [Local] B: two");

        LogOperations.Merge(t.Dir("logs"), new[] { a, b }, MergeType.Weekly, backup: false);

        Assert.True(File.Exists(a));
        Assert.True(File.Exists(Path.Combine(t.Path, "logs", "chatlogWeekly 2026-11-02.txt")));
    }

    [Fact]
    public void Merge_does_not_double_the_same_entry_found_in_two_files()
    {
        using var t = new TempDir();
        var a = t.Write("logs/chatlog 2026-11-03.txt", "2026-11-03 10:00:00 [Local] A: same", "2026-11-03 10:00:01 [Local] A: only a");
        var b = t.Write("logs/chatlog 2026-11-04.txt", "2026-11-03 10:00:00 [Local] A: same", "2026-11-04 10:00:00 [Local] B: only b");

        LogOperations.Merge(t.Dir("logs"), new[] { a, b }, MergeType.Weekly, backup: false);

        var lines = File.ReadAllLines(Path.Combine(t.Path, "logs", "chatlogWeekly 2026-11-02.txt"));
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void ArchiveContainedDailies_moves_only_the_days_fully_inside_a_consolidated_log()
    {
        using var t = new TempDir();
        t.Write("logs/chatlog 2026-11-10.txt", "2026-11-10 10:00:00 [Local] A: hi", "2026-11-10 10:00:05 [Local] B: yo");
        t.Write("logs/chatlog 2026-11-11.txt", "2026-11-11 10:00:00 [Local] A: not merged anywhere");
        t.Write("logs/chatlogWeekly 2026-11-09.txt",
            "2026-11-10 10:00:00 [Local] A: hi", "2026-11-10 10:00:05 [Local] B: yo", "2026-11-12 09:00:00 [Local] C: later");

        var result = LogOperations.ArchiveContainedDailies(t.Dir("logs"));

        Assert.Empty(result.Errors);
        Assert.False(File.Exists(Path.Combine(t.Path, "logs", "chatlog 2026-11-10.txt")));
        Assert.True(File.Exists(Path.Combine(t.Path, "logs", "Archive", "chatlog 2026-11-10.txt")));
        Assert.True(File.Exists(Path.Combine(t.Path, "logs", "chatlog 2026-11-11.txt")));
    }
}
