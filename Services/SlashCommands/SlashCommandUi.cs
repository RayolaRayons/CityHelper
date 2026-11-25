using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using CityHelper.Views;

namespace CityHelper.Services.SlashCommands;

/// <summary>The dialogs around reparsing the wiki's slash command list (Options > System).</summary>
public static class SlashCommandUi
{
    /// <summary>Reads the wiki page again with a progress box, saves the new list, and reports the outcome.</summary>
    public static async Task ReparseAsync(Window owner)
    {
        var result = await ProgressWindow.RunAsync(owner, "Slash Commands", (progress, cancel) =>
        {
            return SlashCommandCache.ReparseAsync(progress, cancel);
        });

        string message =
            result.Cancelled ? "Stopped; the slash command list was not changed."
            : result.Error != null ? "The slash commands could not be reparsed:\n" + result.Error
            : $"Reparsed the wiki page: {result.Commands:N0} working commands saved"
              + (result.Skipped > 0 ? $" ({result.Skipped:N0} non-functional or unknown ones left out)." : ".");
        await new MessageBox("Slash Commands", message).ShowDialog(owner);
    }
}
