using CityHelper.Tools.LogManager;
using Xunit;

namespace CityHelper.Tests;

public class ConsolidatedLogsTests
{
    [Fact]
    public void A_day_that_is_fully_contained_is_found()
    {
        using var t = new TempDir();
        var daily = t.Write("logs/chatlog 2026-11-10.txt", "2026-11-10 10:00:00 [Local] A: hi");
        t.Write("logs/chatlogMonthly 2026-11-01.txt", "2026-11-10 10:00:00 [Local] A: hi", "2026-11-20 10:00:00 [Local] B: later");

        Assert.True(new ConsolidatedLogs(t.Dir("logs")).Contains(daily));
    }

    [Fact]
    public void An_entry_missing_from_the_consolidated_log_means_the_day_is_not_contained()
    {
        using var t = new TempDir();
        var daily = t.Write("logs/chatlog 2026-11-10.txt", "2026-11-10 10:00:00 [Local] A: hi", "2026-11-10 10:00:01 [Local] A: extra");
        t.Write("logs/chatlogWeekly 2026-11-09.txt", "2026-11-10 10:00:00 [Local] A: hi");

        Assert.False(new ConsolidatedLogs(t.Dir("logs")).Contains(daily));
    }

    [Fact]
    public void A_repeated_entry_must_be_repeated_in_the_consolidated_log()
    {
        using var t = new TempDir();
        var daily = t.Write("logs/chatlog 2026-11-10.txt", "2026-11-10 10:00:00 [Local] A: hi", "2026-11-10 10:00:00 [Local] A: hi");
        t.Write("logs/chatlogWeekly 2026-11-09.txt", "2026-11-10 10:00:00 [Local] A: hi");

        Assert.False(new ConsolidatedLogs(t.Dir("logs")).Contains(daily));
    }

    [Fact]
    public void A_consolidated_log_of_another_period_does_not_count()
    {
        using var t = new TempDir();
        var daily = t.Write("logs/chatlog 2026-11-10.txt", "2026-11-10 10:00:00 [Local] A: hi");
        t.Write("logs/chatlogWeekly 2026-11-16.txt", "2026-11-10 10:00:00 [Local] A: hi");         // week of the 16th: does not cover the 10th

        Assert.False(new ConsolidatedLogs(t.Dir("logs")).Contains(daily));
    }

    [Fact]
    public void An_empty_daily_log_is_not_proof_of_anything()
    {
        using var t = new TempDir();
        var daily = t.Write("logs/chatlog 2026-11-10.txt");
        t.Write("logs/chatlogWeekly 2026-11-09.txt", "2026-11-10 10:00:00 [Local] A: hi");

        Assert.False(new ConsolidatedLogs(t.Dir("logs")).Contains(daily));
    }
}
