using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using CityHelper.Views;

namespace CityHelper.Services.MacroImages;

/// <summary>The dialogs around the macro image cache: the first-run question, the download with progress, the result.</summary>
public static class MacroImageUi
{
    /// <summary>Downloads (or updates) the cache with a progress box; reports the outcome. Returns true if a list is cached afterwards.</summary>
    public static async Task<bool> SyncAsync(Window owner)
    {
        var result = await ProgressWindow.RunAsync(owner, "Macro Images", (progress, cancel) =>
            MacroImageScraper.SyncAsync(new Progress<MacroImageProgress>(p => progress.Report((p.Message, p.Done, p.Total))), cancel));

        string message =
            result.Error != null ? "The macro images could not be loaded:\n" + result.Error :
            result.Cancelled ? "Stopped. " + MacroImageCache.Describe() + "\nRun the sync again from Options > System to finish downloading."
            : $"Macro images are up to date: {result.Entries:N0} images listed, {result.Downloaded:N0} downloaded"
              + (result.Failed > 0 ? $", {result.Failed:N0} could not be downloaded (run the sync again to retry)." : ".");
        await new MessageBox("Macro Images", message).ShowDialog(owner);
        return MacroImageCache.IsConfigured;
    }

    /// <summary>True if the list is available; if not, asks whether to download it now and does so on Yes.</summary>
    public static async Task<bool> EnsureAsync(Window owner)
    {
        await Task.Run(MacroImageCache.SeedIfMissing);       // the copy built into the program, if there is no cache yet
        if (MacroImageCache.IsConfigured)
            return true;
        bool yes = await new ConfirmBox("Macro Images",
            "The macro image list (icon pictures and texture names) has not been loaded yet.\n\n" +
            "Load it now from the Homecoming wiki? It is saved in a local cache.").ShowDialog<bool>(owner);
        return yes && await SyncAsync(owner);
    }

    /// <summary>At start: if nothing is cached and the person hasn't said no before, ask once whether to load it.</summary>
    public static async Task PromptFirstRunAsync(Window owner)
    {
        await Task.Run(MacroImageCache.SeedIfMissing);       // the built-in copy makes the question unnecessary
        if (MacroImageCache.IsConfigured)
            return;
        var settings = AppSettings.Load();
        if (settings.MacroImagePromptDismissed)
            return;

        bool yes = await new ConfirmBox("Macro Images",
            "CityHelper can load the macro image list (icon pictures and texture names) from the Homecoming wiki into a local cache. " +
            "PopMenu Hero uses it to pick icons.\n\nLoad it now?\n\n" +
            "If you choose No you won't be asked again; you can do it any time under Options > System.").ShowDialog<bool>(owner);

        if (yes)
        {
            await SyncAsync(owner);
            return;
        }
        try
        {
            settings.MacroImagePromptDismissed = true;
            settings.Save();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // asked again next time
        }
    }
}
