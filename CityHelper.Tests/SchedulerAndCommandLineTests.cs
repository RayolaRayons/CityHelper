using CityHelper.Cli;
using CityHelper.Tools.Scheduler;
using Xunit;

namespace CityHelper.Tests;

public class SchedulerTests
{
    [Fact]
    public void The_arguments_name_the_commands_options_installation_and_account()
    {
        var spec = new ScheduleSpec { Switches = { "Backup", "MergeLog" }, Force = true };

        Assert.Equal("/MergeLog /Backup /force /installation:\"Home coming\" /account:\"Unleet\"",
                     spec.Arguments("Home coming", "Unleet"));
    }

    [Fact]
    public void A_task_definition_reads_back_as_the_same_schedule()
    {
        var spec = new ScheduleSpec
        {
            Name = "City of Heroes - Test - Unleet", Switches = { "ConfigSync", "CleanupLog" }, Force = true,
            Weekly = true, Days = { DayOfWeek.Monday, DayOfWeek.Friday }, StartTime = new TimeSpan(2, 30, 0),
        };

        var xml = typeof(SchedulerService).GetMethod("BuildXml", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                      .Invoke(null, new object[] { spec, "Test", "Unleet" }) as string;
        var back = SchedulerService.ParseXml(xml!);

        Assert.NotNull(back);
        Assert.Equal(spec.Name, back!.Name);
        Assert.Equal(new[] { "ConfigSync", "CleanupLog" }.OrderBy(x => x), back.Switches.OrderBy(x => x));
        Assert.True(back.Force);
        Assert.True(back.Weekly);
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Friday }, back.Days.OrderBy(d => d));
        Assert.Equal(new TimeSpan(2, 30, 0), back.StartTime);
        Assert.Equal("Weekly Mon,Fri at 02:30", back.When);
    }

    [Fact]
    public void A_daily_schedule_has_no_days()
    {
        var spec = new ScheduleSpec { Name = "n", Switches = { "Backup" }, StartTime = new TimeSpan(21, 5, 0) };
        var xml = (string)typeof(SchedulerService).GetMethod("BuildXml", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                      .Invoke(null, new object[] { spec, "T", "A" })!;

        var back = SchedulerService.ParseXml(xml)!;

        Assert.False(back.Weekly);
        Assert.Equal("Daily at 21:05", back.When);
    }

    [Fact]
    public void Task_names_are_per_installation_and_account_and_have_no_spaces()
    {
        var name = SchedulerService.TaskNameFor("My Install", "Un leet", "City of Heroes - A - B");

        Assert.StartsWith(SchedulerService.Prefix("My Install", "Un leet"), name);
        Assert.DoesNotContain(' ', name);
    }

    [Fact]
    public void Unreadable_xml_gives_no_schedule() => Assert.Null(SchedulerService.ParseXml("<not xml"));
}

public class CommandLineTests
{
    [Fact]
    public void No_arguments_means_open_the_window() => Assert.Null(CommandLine.TryRun(Array.Empty<string>()));

    [Fact]
    public void An_unknown_slash_option_is_a_bad_command_line() => Assert.Equal(2, CommandLine.TryRun(new[] { "/definitely-not-a-command" }));

    [Fact]
    public void Help_succeeds() => Assert.Equal(0, CommandLine.TryRun(new[] { "/help" }));
}
