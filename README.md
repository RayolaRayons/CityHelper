# CityHelper

Helper tools for City of Heroes (Homecoming and other servers). .NET 9, Avalonia 12, CommunityToolkit.Mvvm.
Windows is the supported platform (the Scheduler and Chat Watcher use Windows features).

## Layout of the repository
| Folder | What it is |
|---|---|
| `CityHelper.csproj`, `Views/`, `ViewModels/`, `Services/`, `Tools/`, `Cli/` | the application (a library; the window and all tools) |
| `CityHelper.Desktop/` | the executable: starts the window, or runs the command line (`Cli/CommandLine.cs`) |
| `CityHelper.Tests/` | xUnit tests for the code that changes files (logs, backups, settings, merge, scheduler) |
| `build/` | `build.ps1` (build, test, package) and `CityHelper.iss` (installer script) |
| `Directory.Build.props` | package versions and the **version of the program** (the one place it is kept) |

## The window
Left: the installation and account selectors and the list of sections, in two groups with a divider:
Log Viewer, PopmenuHero, Player Notes, Character Ordering, ChatWatcher, Bind | Config Sync, Log Manager, PIGG Viewer, Scheduler, Backup, then **Options**.
A section may have a menu bar above its page (`ITool.FileGroups` / `RibbonGroups`). Options holds System (theme, macro image cache), Installations, and a page per tool (the two log tools are grouped under Logs).

## Settings
One file, `CityHelper.ini`. Each part owns the sections that start with its name (`[LogManager.Options]`, `[ConfigSync.Environment1]`, `[Backup.Items]`...); see `Services/IniStore.cs`.
- Standalone: beside the program when `portable.txt` (or an existing `CityHelper.ini`) is there.
- Installed: `%APPDATA%\CityHelper`.
The macro image cache (`Cache\MacroImages`), backups (default: `Backup` inside Config Sync's sync folder) and `CityHelper-cli.log` live next to / under the same settings folder.

## Adding a tool
1. A view model deriving from `ViewModelBase` (it takes `AppState` to read the selected installation and account).
2. A view `FooView` next to it; `ViewLocator` pairs `FooViewModel` with `FooView` by name, so no registration is needed.
3. A `FooTool : ITool` (name, `CreateViewModel`, optional `CreateOptionsSection`, `SaveCommand`, `RefreshCommand`, `InformationGroups`).
4. Register it in `Tools/ToolRegistry.cs` and give it a place in the order lists there.

## Building and releasing
`powershell -File build\build.ps1` builds Release, runs the tests (a failing test stops the build), publishes a self-contained single file and makes, in `dist\`:
- `CityHelper-<version>-portable-win-x64.zip`: standalone; its `portable.txt` keeps settings beside the exe.
- `CityHelper-<version>-setup-win-x64.exe`: installer (needs Inno Setup 6 or 7; skipped with a warning if missing).

The version is set from the script, so nothing is edited by hand:
`build\build.ps1 -Version 20261124` sets the shown version; add `-BumpMinor` to also raise the internal assembly version's minor number (2026.15.0 becomes 2026.16.0).
Other switches: `-SkipTests`, `-SkipInstaller`, `-Runtime`.

## Tests
`dotnet test CityHelper.Tests`. The tests use temporary folders and keep their settings beside the test program, never in your profile.

## Command line
`CityHelper.exe /Command [/Command ...] [options]` runs functions without opening the window (for scheduled tasks and scripts). Commands run in the order given:
- `/ConfigSync` Config Sync's copy process (newer side copied across, logs merged)
- `/MergeLog` merge the daily logs into weekly / monthly logs (Options > Logs > Log Manager > Merge type; `/type:weekly|monthly` overrides). With *System channel fix: At merge* the files are fixed first.
- `/FixChannels` insert channels (`[Combat] [System] [Reward] [Auction]`) into lines that have none
- `/FixSystem` a timestamp used as a channel, e.g. `[05:53]`, becomes `[System]`
- `/CleanupLog` log cleanup with the saved settings (a file that would lose more than 20% / 5000 lines is skipped unless `/force`)
- `/Backup` a fresh backup in the configured backup location

Options: `/installation:Name`, `/account:Name` (default: the default installation, its first account), `/file:Path` (FixChannels, FixSystem, CleanupLog: one log only), `/help`.
Results go to the console it was started from and to `CityHelper-cli.log` in the settings folder. Exit code 0 = done, 1 = a problem was reported, 2 = bad command line.
The **Scheduler** section creates Windows Task Scheduler tasks (folder `\CityHelper`) that run these commands.

## Log Manager options in short
- *Keep copies of changed logs* (+ *Copies folder*, default `Archive` inside the logs folder): merged sources are moved there; fixes, inserted channels and cleanups leave a copy with the date and time in its name.
- *System channel fix*: Manual (button, `/FixSystem`) or At merge.
- *Merge type*: No merge, Weekly or Monthly. **Merge** always combines the checked files into one; Weekly / Monthly add a **Merge Weekly / Merge Monthly** button for the daily logs (and set what `/MergeLog` does): each checked log, even a single day, is merged into the log of its week / month, which is created if it is not there yet. **Select Daily Logs** checks every log that is not a merged log.

## Slash command list
`Assets/SlashCommands.json` holds the commands of the wiki's [List of Slash Commands](https://homecoming.wiki/wiki/List_of_Slash_Commands) (working commands only), plus what each command's own page says (one level below the list; no other links are followed): `name`, `aliases`, `description`, `defaultKey`, `wikiUrl`, `params` (`name`, `type`, `min`, `max`, `optional`, and when the page gives them `default` and `numbers`: value and meaning), `rawSyntax`, `toggle` (`++name`, for commands that take 0 or 1), `details`, `examples` and `source`. Saved names, aliases, toggles, examples and text never carry the leading slash (every command is assumed to have it); the Command Lookup adds it when showing. It is built into the program (`Services/SlashCommands`, `SlashCommandCache.Load()`), so nothing is parsed at run time. **Options > System > Reparse Slash Commands** reads the wiki page again and keeps the result in `Cache\SlashCommands` beside the settings, which then takes over. To refresh the built-in copy, reparse and copy that cache file over `Assets/SlashCommands.json`.

## Macro image cache
`Assets/MacroImages.zip` (the macro image list `macroimages.json` and its 3,280 pictures, from the wiki's Macro image page) is built into the program. The first time the icon lookup (or the start-up check) needs it and there is no `Cache\MacroImages` beside the settings, it is unpacked there (`MacroImageCache.SeedIfMissing()`), so nothing is downloaded and no question is asked. **Options > System > Set Up / Sync Macro Images** still updates a cache from the wiki. To refresh the built-in copy, sync, then zip the `Cache\MacroImages` folder (`macroimages.json` and `Images/`) over `Assets/MacroImages.zip`.

## Emote list
`Assets/Emotes.json` holds the emotes of the wiki's [Emotes](https://homecoming.wiki/wiki/Emotes) page: `name`, `aliases` and `description` (333 emotes). It is built into the program like the slash command list (`Services/Emotes`, `EmoteCache.Load()`). **Options > System > Reparse Emotes** reads the page again and keeps the result in the `Cache/Emotes` folder beside the settings. **Command Lookup** shows the two lists as an accordion, **Commands** and **Emotes**, one open at a time, with one search box for both.
