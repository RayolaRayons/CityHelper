using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CityHelper.Tools.Scheduler;

/// <summary>The command line functions a schedule can run.</summary>
public sealed record ScheduleCommand(string Switch, string Label);

/// <summary>What a scheduled task does and when: the command line parameters and the start time.</summary>
public sealed class ScheduleSpec
{
    public static readonly IReadOnlyList<ScheduleCommand> Commands = new[]
    {
        new ScheduleCommand("ConfigSync", "Config Sync (copy)"),
        new ScheduleCommand("FixSystem", "Fix system channels"),
        new ScheduleCommand("FixChannels", "Insert channels"),
        new ScheduleCommand("MergeLog", "Merge logs"),
        new ScheduleCommand("CleanupLog", "Clean up logs"),
        new ScheduleCommand("Backup", "Backup"),
    };

    public string Name { get; set; } = "";
    public List<string> Switches { get; set; } = new();
    public bool Force { get; set; }
    public bool Weekly { get; set; }
    public List<DayOfWeek> Days { get; set; } = new();
    public TimeSpan StartTime { get; set; } = new(3, 0, 0);

    public string Summary => string.Join(", ", Commands.Where(c => Switches.Contains(c.Switch, StringComparer.OrdinalIgnoreCase)).Select(c => c.Label));

    public string When => (Weekly ? "Weekly " + string.Join(",", Days.OrderBy(d => ((int)d + 6) % 7).Select(d => d.ToString()[..3])) : "Daily")
                          + " at " + StartTime.ToString(@"hh\:mm");

    /// <summary>The command line the task runs (after the program): /Backup /installation:"X" /account:"Y".</summary>
    public string Arguments(string installation, string account)
    {
        var parts = Commands.Where(c => Switches.Contains(c.Switch, StringComparer.OrdinalIgnoreCase)).Select(c => "/" + c.Switch).ToList();
        if (Force) parts.Add("/force");
        parts.Add($"/installation:\"{installation}\"");
        parts.Add($"/account:\"{account}\"");
        return string.Join(" ", parts);
    }
}

/// <summary>A CityHelper task as the Windows Task Scheduler has it.</summary>
public sealed record ScheduledTask(string TaskName, string DisplayName, ScheduleSpec Spec, string NextRun, string LastRun, string LastResult, string Status);

/// <summary>
/// Creates, lists, runs and deletes the schedules as tasks in the Windows Task Scheduler (folder \CityHelper),
/// through schtasks.exe. Each task starts this program with the command line switches (/Backup ...), which run
/// without a window.
/// </summary>
public static class SchedulerService
{
    public const string Folder = @"\CityHelper\";

    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>Task name prefix for an installation and account, so the grid shows only the current ones.</summary>
    public static string Prefix(string installation, string account) => Folder + Safe(installation) + "_" + Safe(account) + "_";

    public static string TaskNameFor(string installation, string account, string name) => Prefix(installation, account) + Safe(name);

    private static string Safe(string text)
    {
        foreach (var c in Path.GetInvalidFileNameChars().Concat(new[] { '\\', '/' }))
            text = text.Replace(c, '_');
        return text.Trim().Replace(' ', '-');
    }

    // ------------------------------------------------------------ schtasks

    private static (int Exit, string Output, string Error) Run(string arguments)
    {
        var info = new ProcessStartInfo("schtasks.exe", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using var process = Process.Start(info)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error.Result);
    }

    /// <summary>The CityHelper tasks of this installation and account.</summary>
    public static List<ScheduledTask> List(string installation, string account)
    {
        var result = new List<ScheduledTask>();
        if (!IsSupported)
            return result;

        var prefix = Prefix(installation, account);
        var names = new List<string>();
        var (code, output, _) = Run("/Query /FO CSV /NH");
        if (code != 0)
            return result;
        foreach (var line in output.Split('\n'))
        {
            var fields = SplitCsv(line);
            if (fields.Count > 0 && fields[0].StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !names.Contains(fields[0]))
                names.Add(fields[0]);
        }

        foreach (var name in names)
            if (Describe(name, prefix) is { } task)
                result.Add(task);
        return result.OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static ScheduledTask? Describe(string taskName, string prefix)
    {
        var xml = Run($"/Query /TN \"{taskName}\" /XML");
        var spec = xml.Exit == 0 ? ParseXml(xml.Output) : null;
        if (spec is null)
            return null;
        // The name as typed is kept in the task's documentation text; the task name itself has no spaces
        var display = spec.Name.Length > 0 ? spec.Name : taskName[prefix.Length..].Replace('-', ' ');
        spec.Name = display;

        // Next run, status and the last run's result (the columns of the verbose list are in a fixed order)
        string next = "", last = "", result = "", status = "";
        var verbose = Run($"/Query /TN \"{taskName}\" /FO CSV /V /NH");
        if (verbose.Exit == 0)
        {
            var f = SplitCsv(verbose.Output.Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "");
            if (f.Count > 6)
            {
                next = f[2]; status = f[3]; last = f[5];
                // 267011 is the Task Scheduler's "has not run yet" (and its last run date is 1999)
                result = f[6] == "0" ? "OK" : f[6] == "267011" ? "Not run yet" : "Code " + f[6];
                if (last.Contains("1999"))
                    last = "";
            }
        }
        return new ScheduledTask(taskName, display, spec, next, last, result, status);
    }

    /// <summary>Creates the task, or replaces it if it exists.</summary>
    public static string? Save(string installation, string account, ScheduleSpec spec, string? previousTaskName)
    {
        if (!IsSupported)
            return "Scheduling needs Windows.";
        var taskName = TaskNameFor(installation, account, spec.Name);
        var xmlPath = Path.Combine(Path.GetTempPath(), "cityhelper_task_" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            File.WriteAllText(xmlPath, BuildXml(spec, installation, account), Encoding.Unicode);
            var (code, _, error) = Run($"/Create /TN \"{taskName}\" /XML \"{xmlPath}\" /F");
            if (code != 0)
                return "The Task Scheduler refused the task: " + error.Trim();
        }
        finally
        {
            try { File.Delete(xmlPath); } catch (IOException) { }
        }

        // Renamed: the old task goes
        if (previousTaskName != null && !previousTaskName.Equals(taskName, StringComparison.OrdinalIgnoreCase))
            Delete(previousTaskName);
        return null;
    }

    public static string? Delete(string taskName)
    {
        var (code, _, error) = Run($"/Delete /TN \"{taskName}\" /F");
        return code == 0 ? null : error.Trim();
    }

    public static string? RunNow(string taskName)
    {
        var (code, _, error) = Run($"/Run /TN \"{taskName}\"");
        return code == 0 ? null : error.Trim();
    }

    public static void OpenTaskScheduler() =>
        Process.Start(new ProcessStartInfo("taskschd.msc") { UseShellExecute = true });

    // ------------------------------------------------------------ the task definition

    internal static string BuildXml(ScheduleSpec spec, string installation, string account)
    {
        string exe = Environment.ProcessPath ?? "CityHelper.exe";
        string start = DateTime.Today.Add(spec.StartTime).ToString("yyyy-MM-dd'T'HH:mm:ss");
        string schedule = spec.Weekly
            ? "<ScheduleByWeek><DaysOfWeek>" + string.Concat(spec.Days.Select(d => $"<{d}/>")) + "</DaysOfWeek><WeeksInterval>1</WeeksInterval></ScheduleByWeek>"
            : "<ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>";
        string E(string s) => System.Security.SecurityElement.Escape(s) ?? "";

        return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>CityHelper: {E(spec.Summary)} ({E(installation)}, {E(account)})</Description><Documentation>{E(spec.Name)}</Documentation></RegistrationInfo>
  <Triggers>
    <CalendarTrigger>
      <StartBoundary>{start}</StartBoundary>
      <Enabled>true</Enabled>
      {schedule}
    </CalendarTrigger>
  </Triggers>
  <Principals><Principal id=""Author""><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <StartWhenAvailable>true</StartWhenAvailable>
    <ExecutionTimeLimit>PT2H</ExecutionTimeLimit>
    <Enabled>true</Enabled>
  </Settings>
  <Actions Context=""Author""><Exec><Command>{E(exe)}</Command><Arguments>{E(spec.Arguments(installation, account))}</Arguments></Exec></Actions>
</Task>";
    }

    /// <summary>Reads a task definition back into the parameters and the start time.</summary>
    internal static ScheduleSpec? ParseXml(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml.TrimStart('﻿', ' ', '\r', '\n'));
            XElement? Find(XElement parent, string name) => parent.Descendants().FirstOrDefault(e => e.Name.LocalName == name);

            var spec = new ScheduleSpec { Name = Find(doc.Root!, "Documentation")?.Value ?? "" };
            var args = Find(doc.Root!, "Arguments")?.Value ?? "";
            foreach (var command in ScheduleSpec.Commands)
                if (Regex.IsMatch(args, @"(^|\s)/" + command.Switch + @"(\s|$)", RegexOptions.IgnoreCase))
                    spec.Switches.Add(command.Switch);
            spec.Force = Regex.IsMatch(args, @"(^|\s)/force(\s|$)", RegexOptions.IgnoreCase);

            if (Find(doc.Root!, "StartBoundary")?.Value is { } boundary && DateTime.TryParse(boundary, out var when))
                spec.StartTime = when.TimeOfDay;
            if (Find(doc.Root!, "ScheduleByWeek") is { } week)
            {
                spec.Weekly = true;
                foreach (var day in Find(week, "DaysOfWeek")?.Elements() ?? Enumerable.Empty<XElement>())
                    if (Enum.TryParse<DayOfWeek>(day.Name.LocalName, out var d))
                        spec.Days.Add(d);
            }
            return spec;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        foreach (Match m in Regex.Matches(line.Trim(), "\"((?:[^\"]|\"\")*)\"|([^,]+)"))
            fields.Add(m.Groups[1].Success ? m.Groups[1].Value.Replace("\"\"", "\"") : m.Groups[2].Value);
        return fields;
    }
}
