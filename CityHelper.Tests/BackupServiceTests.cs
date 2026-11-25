using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CityHelper.Tools.Backup;
using Xunit;

namespace CityHelper.Tests;

public class BackupServiceTests
{
    private static string Install(TempDir t)
    {
        t.Write("Homecoming/Binds/a.txt", "bind a");
        t.Write("Homecoming/binds.txt", "root binds");
        t.Write("Homecoming/settings/live/keybinds.txt", "keys");
        t.Write("Homecoming/accounts/Unleet/playernotes.txt", "notes");
        t.Write("Homecoming/accounts/Unleet/logs/chatlog 2026-11-01.txt", "log");
        t.Write("Homecoming/accounts/Unleet/logs/private/p.txt", "private chat");
        t.Write("Homecoming/data/texts/English/menus/m.mnu", "menu");
        return Path.Combine(t.Path, "Homecoming");
    }

    private static readonly string[] Keys = { "binds", "player_notes", "logfiles", "private_logs", "popmenus" };

    [Fact]
    public void Create_zips_the_chosen_items_with_a_manifest_and_List_finds_it()
    {
        using var t = new TempDir();
        var install = Install(t);
        var location = Path.Combine(t.Path, "Backup");

        var result = BackupService.Create(install, "Homecoming", "Unleet", "live", Keys, location, null, CancellationToken.None);

        Assert.Null(result.Problem);
        Assert.Equal(7, result.Files);
        var list = BackupService.List(location);
        var info = Assert.Single(list);
        Assert.Equal("Homecoming", info.Installation);
        Assert.Equal("Unleet", info.Account);
        Assert.Equal(3, info.Items.First(i => i.Key == "binds").Files);          // Binds folder, binds.txt, keybinds.txt
        Assert.Equal(1, info.Items.First(i => i.Key == "logfiles").Files);       // private chat is its own item
        Assert.Equal(1, info.Items.First(i => i.Key == "private_logs").Files);
    }

    [Fact]
    public void Restore_puts_back_only_the_chosen_items()
    {
        using var t = new TempDir();
        var install = Install(t);
        var location = Path.Combine(t.Path, "Backup");
        var zip = BackupService.Create(install, "Homecoming", "Unleet", "live", Keys, location, null, CancellationToken.None).Path!;

        File.WriteAllText(Path.Combine(install, "Binds", "a.txt"), "CHANGED");
        File.WriteAllText(Path.Combine(install, "accounts", "Unleet", "playernotes.txt"), "CHANGED");
        File.WriteAllText(Path.Combine(install, "data", "texts", "English", "menus", "m.mnu"), "CHANGED");

        var restored = BackupService.Restore(zip, install, new[] { "binds", "player_notes" }, null, CancellationToken.None);

        Assert.Empty(restored.Errors);
        Assert.Equal(4, restored.Files);
        Assert.Equal("bind a", File.ReadAllText(Path.Combine(install, "Binds", "a.txt")).Trim());
        Assert.Equal("notes", File.ReadAllText(Path.Combine(install, "accounts", "Unleet", "playernotes.txt")).Trim());
        Assert.Equal("CHANGED", File.ReadAllText(Path.Combine(install, "data", "texts", "English", "menus", "m.mnu")));     // not chosen
    }

    [Fact]
    public void Create_with_no_items_or_no_files_reports_a_problem_and_makes_no_zip()
    {
        using var t = new TempDir();
        var install = Install(t);
        var location = Path.Combine(t.Path, "Backup");

        Assert.NotNull(BackupService.Create(install, "H", "Unleet", "live", Array.Empty<string>(), location, null, CancellationToken.None).Problem);
        Assert.NotNull(BackupService.Create(install, "H", "Nobody", "live", new[] { "player_notes" }, location, null, CancellationToken.None).Problem);
        Assert.Empty(BackupService.List(location));
    }

    [Fact]
    public void A_cancelled_backup_leaves_no_file_behind()
    {
        using var t = new TempDir();
        var install = Install(t);
        var location = Path.Combine(t.Path, "Backup");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var result = BackupService.Create(install, "H", "Unleet", "live", Keys, location, null, cancelled.Token);

        Assert.True(result.Cancelled);
        Assert.Empty(Directory.GetFiles(location));
    }

    [Fact]
    public void List_ignores_zips_without_a_manifest()
    {
        using var t = new TempDir();
        var location = t.Dir("Backup");
        using (var zip = ZipFile.Open(Path.Combine(location, "Backup_not-ours.zip"), ZipArchiveMode.Create))
            zip.CreateEntry("readme.txt");

        Assert.Empty(BackupService.List(location));
    }

    [Fact]
    public void Restore_refuses_an_entry_that_would_leave_the_installation_folder()
    {
        using var t = new TempDir();
        var install = t.Dir("Homecoming");
        var zipPath = Path.Combine(t.Path, "Backup_evil.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var manifest = new BackupManifest { CreatedUtc = DateTime.UtcNow, Installation = "H", Account = "A" };
            using (var w = new StreamWriter(zip.CreateEntry("manifest.json").Open(), new UTF8Encoding(false)))
                w.Write(JsonSerializer.Serialize(manifest));
            using var evil = new StreamWriter(zip.CreateEntry("files/binds/../../evil.txt").Open());
            evil.Write("gotcha");
        }

        var result = BackupService.Restore(zipPath, install, new[] { "binds" }, null, CancellationToken.None);

        Assert.Equal(0, result.Files);
        Assert.NotEmpty(result.Errors);
        Assert.False(File.Exists(Path.Combine(t.Path, "evil.txt")));
    }
}
