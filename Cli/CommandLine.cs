using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using CityHelper.Models;
using CityHelper.Services;
using CityHelper.Tools.Backup;
using CityHelper.Tools.ConfigSync;
using CityHelper.Tools.LogManager;

namespace CityHelper.Cli;

/// <summary>
/// Command line use: run some functions with no window (for scheduled tasks and scripts).
/// Commands (any number, run in the order given): /ConfigSync /MergeLog /FixChannels /FixSystem /CleanupLog /Backup.
/// Options: /installation:Name /account:Name /file:Path /type:weekly|monthly /force.
/// Exit code: 0 done, 1 a problem was reported, 2 bad command line.
/// </summary>
public static class CommandLine
{
    private static readonly string[] Commands = { "configsync", "mergelog", "fixchannels", "fixsystem", "cleanuplog", "backup" };
    private static readonly string[] OptionNames = { "installation", "account", "file", "type", "force" };

    public const string Usage =
@"CityHelper command line (runs without opening the window)

  CityHelper.exe /Command [/Command ...] [options]

Commands, run in the order given:
  /ConfigSync     Config Sync's copy process: newer side of each item is copied across, logs are merged
  /MergeLog       Merge logs: the daily logs into weekly or monthly logs, per Options > Log Manager (Merge type;
                  with System channel fix set to At merge, the files are fixed first)
  /FixChannels    Insert channels: add [Combat] [System] [Reward] [Auction] to lines that have none
  /FixSystem      Fix system channels: a timestamp used as a channel, e.g. [05:53], becomes [System]
  /CleanupLog     Clean up logs with the saved settings (channels to keep, keep copies)
  /Backup         Create a fresh backup in the configured backup location

Options:
  /installation:Name   Installation to use (default: your default installation)
  /account:Name        Account to use (default: the first account)
  /file:Path           FixChannels, FixSystem, CleanupLog: only this log file (default: every log of the account)
  /type:weekly|monthly MergeLog: overrides the merge type of the options
  /force               CleanupLog: skip the safety check for large removals (more than 20% or 5000 lines)

A result summary is shown in the console it was started from and added to CityHelper-cli.log
in the settings folder. Exit code: 0 done, 1 a problem was reported, 2 bad command line.";

    /// <summary>Runs the command line if it has commands; returns the exit code, or null to start the window as usual.</summary>
    public static int? TryRun(string[] args)
    {
        var parsed = Parse(args);
        if (parsed.Commands.Count == 0 && !parsed.Help && parsed.Unknown.Count == 0)
            return null;                                   // no command line use: open the window

        var output = Output.Open();
        try
        {
            if (parsed.Help)
            {
                output.Line(Usage);
                return 0;
            }
            if (parsed.Unknown.Count > 0 || parsed.Commands.Count == 0)
            {
                output.Line("Unknown option: " + string.Join(" ", parsed.Unknown.DefaultIfEmpty("(none)")));
                output.Line("");
                output.Line(Usage);
                return 2;
            }

            var state = new AppState();
            if (!Select(state, parsed.Options, output))
                return 1;

            int exit = 0;
            foreach (var command in parsed.Commands)
            {
                output.Line($"== /{command} ({state.SelectedInstallation?.Name}, {state.SelectedAccount?.Name}) ==");
                try
                {
                    exit = Math.Max(exit, Run(command, state, parsed.Options, output));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    output.Line("Failed: " + ex.Message);
                    exit = 1;
                }
            }
            output.Line(exit == 0 ? "Done." : "Finished with problems.");
            return exit;
        }
        finally
        {
            output.Close();
        }
    }

    // ------------------------------------------------------------ parsing

    private sealed record Parsed(List<string> Commands, Dictionary<string, string> Options, List<string> Unknown, bool Help);

    private static Parsed Parse(string[] args)
    {
        var commands = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var unknown = new List<string>();
        bool help = false;

        foreach (var arg in args)
        {
            if (arg.Length < 2 || arg[0] is not ('/' or '-'))
            {
                if (commands.Count > 0 || options.Count > 0)
                    unknown.Add(arg);                      // stray text after a command
                continue;
            }

            var body = arg.TrimStart('/', '-');
            int sep = body.IndexOfAny(new[] { ':', '=' });
            var name = (sep < 0 ? body : body[..sep]).ToLowerInvariant();
            var value = sep < 0 ? "" : body[(sep + 1)..];

            if (name is "help" or "?" or "h")
                help = true;
            else if (Commands.Contains(name))
                commands.Add(name);
            else if (OptionNames.Contains(name))
                options[name] = value;
            else if (arg[0] == '/')
                unknown.Add(arg);                          // a "/something" that is not ours; "-x" style args are left to the app
        }
        return new Parsed(commands, options, unknown, help);
    }

    // ------------------------------------------------------------ choosing installation and account

    private static bool Select(AppState state, Dictionary<string, string> options, Output output)
    {
        if (state.Installations.Count == 0)
        {
            output.Line("No installation is set up yet. Start CityHelper and add one first.");
            return false;
        }
        if (options.TryGetValue("installation", out var name) && name.Length > 0)
        {
            var found = state.Installations.FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                output.Line($"No installation named \"{name}\". Known: " + string.Join(", ", state.Installations.Select(i => i.Name)));
                return false;
            }
            state.SelectedInstallation = found;
        }
        if (options.TryGetValue("account", out var account) && account.Length > 0)
        {
            var found = state.Accounts.FirstOrDefault(a => a.Name.Equals(account, StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                output.Line($"No account named \"{account}\". Known: " + string.Join(", ", state.Accounts.Select(a => a.Name)));
                return false;
            }
            state.SelectedAccount = found;
        }
        if (state.SelectedInstallation is null || state.SelectedAccount is null)
        {
            output.Line("The installation has no accounts yet (accounts\\<name> folders)." );
            return false;
        }
        return true;
    }

    // ------------------------------------------------------------ the commands

    private static int Run(string command, AppState state, Dictionary<string, string> options, Output output) => command switch
    {
        "configsync" => RunConfigSync(state, output),
        "mergelog" => RunMergeLog(state, options, output),
        "fixchannels" => RunOnLogs(state, options, output, (dir, file, s) => LogOperations.InsertChannels(dir, file, s.KeepCopies, s.CopiesFolder)),
        "fixsystem" => RunOnLogs(state, options, output, (dir, file, s) => LogOperations.FixTimestampedSystemMessages(dir, file, s.KeepCopies, s.CopiesFolder)),
        "cleanuplog" => RunCleanup(state, options, output),
        "backup" => RunBackup(state, output),
        _ => 2,
    };

    private static string LogDirectory(AppState state) =>
        Path.Combine(state.SelectedInstallation!.Path, "accounts", state.SelectedAccount!.Name, "logs");

    /// <summary>The files to work on: the /file option, or every log directly in the account's logs folder.</summary>
    private static List<string> LogFiles(AppState state, Dictionary<string, string> options, Output output)
    {
        if (options.TryGetValue("file", out var file) && file.Length > 0)
            return File.Exists(file) ? new List<string> { file } : Missing(output, file);

        var dir = LogDirectory(state);
        if (!Directory.Exists(dir))
            return Missing(output, dir);
        return Directory.EnumerateFiles(dir, "*.txt", SearchOption.TopDirectoryOnly)
            .Where(f => Path.GetExtension(f).Equals(".txt", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> Missing(Output output, string path)
    {
        output.Line("Not found: " + path);
        output.Problem = true;
        return new List<string>();
    }

    private static int Finish(Output output, LogOperationResult result)
    {
        foreach (var m in result.Messages) output.Line("  " + m);
        foreach (var e in result.Errors) output.Line("  ERROR " + e);
        return result.Errors.Count > 0 || output.Problem ? 1 : 0;
    }

    private static int RunConfigSync(AppState state, Output output)
    {
        var (items, problem) = ConfigSyncRunner.Run(state);
        if (problem != null)
        {
            output.Line(problem);
            return 1;
        }
        int errors = 0;
        foreach (var (item, result) in items)
        {
            output.Line($"  {item}: {result.Copied} copied" + (result.Merged > 0 ? $", {result.Merged} merged" : "") + $", {result.UpToDate} up to date");
            foreach (var e in result.Errors) { output.Line("    ERROR " + e); errors++; }
        }
        return errors > 0 ? 1 : 0;
    }

    private static int RunMergeLog(AppState state, Dictionary<string, string> options, Output output)
    {
        var settings = LogManagerSettings.Load();
        var type = options.TryGetValue("type", out var t) ? t.ToLowerInvariant() switch
        {
            "weekly" => MergeType.Weekly,
            "monthly" => MergeType.Monthly,
            _ => settings.MergeType,
        } : settings.MergeType;

        if (type == MergeType.Selected)
        {
            output.Line("The merge type is \"No merge\" (Options > Log Manager), so nothing was merged. Use /type:weekly or /type:monthly, or choose one in the options.");
            return 0;
        }

        var dir = LogDirectory(state);
        if (!Directory.Exists(dir))
        {
            output.Line("Not found: " + dir);
            return 1;
        }

        var daily = Directory.EnumerateFiles(dir, "*.txt", SearchOption.TopDirectoryOnly)
            .Where(f => LogOperations.IsDailyLog(Path.GetFileName(f))).ToList();
        var sum = new LogOperationResult();
        if (settings.FixAtMergeEnabled)
            foreach (var file in daily)
            {
                var fix = LogOperations.FixTimestampedSystemMessages(dir, file, settings.KeepCopies, settings.CopiesFolder);
                sum.Messages.AddRange(fix.Messages.Where(m => m.Contains(": fixed ")));
                sum.Errors.AddRange(fix.Errors);
            }

        var merged = LogOperations.Merge(dir, daily, type, settings.KeepCopies, settings.CopiesFolder);
        sum.Messages.AddRange(merged.Messages);
        sum.Errors.AddRange(merged.Errors);

        if (settings.ValidateOrphanedDailies)
        {
            var orphans = LogOperations.ArchiveContainedDailies(dir, settings.CopiesFolder);
            sum.Messages.AddRange(orphans.Messages);
            sum.Errors.AddRange(orphans.Errors);
        }
        if (sum.Messages.Count == 0 && sum.Errors.Count == 0)
            sum.Messages.Add("No daily logs to merge.");
        return Finish(output, sum);
    }

    private static int RunOnLogs(AppState state, Dictionary<string, string> options, Output output,
                                 Func<string, string, LogManagerSettings, LogOperationResult> operation)
    {
        var settings = LogManagerSettings.Load();
        var sum = new LogOperationResult();
        foreach (var file in LogFiles(state, options, output))
        {
            var one = operation(Path.GetDirectoryName(file)!, file, settings);
            // Files that needed nothing are not listed one by one
            sum.Messages.AddRange(one.Messages.Where(m => !m.Contains("no lines need") && !m.Contains("no timestamped")));
            sum.Errors.AddRange(one.Errors);
        }
        if (sum.Messages.Count == 0 && sum.Errors.Count == 0)
            sum.Messages.Add("Nothing needed changing.");
        return Finish(output, sum);
    }

    private static int RunCleanup(AppState state, Dictionary<string, string> options, Output output)
    {
        var settings = LogManagerSettings.Load();
        bool force = options.ContainsKey("force");
        var sum = new LogOperationResult();
        foreach (var file in LogFiles(state, options, output))
        {
            if (LogOperations.IsOpenElsewhere(file))
            {
                sum.Messages.Add($"{Path.GetFileName(file)}: skipped (open in the game)");
                continue;
            }
            var (removed, total) = LogOperations.PreviewCleanup(file, settings.IncludedChannels);
            if (removed == 0)
                continue;

            // The window asks before a big removal; here there is nobody to ask, so such a file is left alone unless /force
            if (!force && removed > Math.Min(total * 0.2, 5000))
            {
                sum.Messages.Add($"{Path.GetFileName(file)}: skipped, cleanup would remove {removed:N0} of {total:N0} lines (use /force, or clean it up in the Log Viewer)");
                continue;
            }
            var one = LogOperations.Cleanup(Path.GetDirectoryName(file)!, new[] { file }, settings.IncludedChannels,
                                            settings.KeepCopies, settings.CopiesFolder);
            sum.Messages.AddRange(one.Messages);
            sum.Errors.AddRange(one.Errors);
        }
        if (sum.Messages.Count == 0 && sum.Errors.Count == 0)
            sum.Messages.Add("Nothing to remove.");
        return Finish(output, sum);
    }

    private static int RunBackup(AppState state, Output output)
    {
        var inst = state.SelectedInstallation!;
        var account = state.SelectedAccount!;
        var settings = BackupSettings.Load(state);
        var names = state.Installations.Select(i => i.Name).ToList();
        var sync = ConfigSyncSettings.Load(names, state.Installations.FirstOrDefault(i => i.IsDefault)?.Name);
        var env = sync.Find(inst.Name)?.GameEnvironment ?? SyncItems.DefaultGameEnvironment;

        var result = BackupService.Create(inst.Path, inst.Name, account.Name, env, settings.Items, settings.EffectiveLocation(state),
                                          null, System.Threading.CancellationToken.None);
        if (result.Problem != null)
        {
            output.Line("  Backup failed: " + result.Problem);
            return 1;
        }
        output.Line($"  Saved {result.Path} ({result.Files:N0} files)");
        foreach (var e in result.Errors) output.Line("  could not read: " + e);
        return result.Errors.Count > 0 ? 1 : 0;
    }

    // ------------------------------------------------------------ output: the console it was started from, and a log file

    private sealed class Output
    {
        private TextWriter? _console;
        private StreamWriter? _file;
        public bool Problem { get; set; }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(int processId);
        [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        public static Output Open()
        {
            var o = new Output();
            try
            {
                // The program is a window application: use the console of whoever started it, if there is one
                if (OperatingSystem.IsWindows() && AttachConsole(-1))
                {
                    var std = GetStdHandle(-11);
                    if (std == IntPtr.Zero || std == new IntPtr(-1))
                    {
                        var conout = CreateFile("CONOUT$", 0x40000000, 2, IntPtr.Zero, 3, 0, IntPtr.Zero);   // GENERIC_WRITE, FILE_SHARE_WRITE, OPEN_EXISTING
                        if (conout != new IntPtr(-1))
                            o._console = new StreamWriter(new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(conout, true), FileAccess.Write), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
                    }
                }
                o._console ??= new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                o._console = null;
            }
            try
            {
                Directory.CreateDirectory(IniStore.DataDirectory);
                o._file = new StreamWriter(Path.Combine(IniStore.DataDirectory, "CityHelper-cli.log"), append: true) { AutoFlush = true };
                o._file.WriteLine($"----- {DateTime.Now:yyyy-MM-dd HH:mm:ss} -----");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                o._file = null;
            }
            return o;
        }

        public void Line(string text)
        {
            try { _console?.WriteLine(text); } catch (IOException) { }
            try { _file?.WriteLine(text); } catch (IOException) { }
        }

        public void Close()
        {
            _console?.Flush();
            _file?.Dispose();
        }
    }
}
