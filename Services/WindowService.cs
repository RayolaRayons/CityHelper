using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CityHelper.Tools;
using CityHelper.ViewModels;
using CityHelper.Views;

namespace CityHelper.Services;

/// <summary>Opens the shell's modal windows over the main window.</summary>
public sealed class WindowService
{
    private readonly AppState _state;

    private readonly IReadOnlyList<ITool> _tools;

    public WindowService(AppState state, IReadOnlyList<ITool> tools)
    {
        _state = state;
        _tools = tools;
    }

    /// <summary>One scrollable window: installations first, then every tool's options.</summary>
    public async Task ShowOptionsAsync()
    {
        if ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not { } owner)
            return;

        var sections = new List<OptionsSectionViewModel> { new InstallationsViewModel(_state) };
        sections.AddRange(_tools.Select(t => t.CreateOptionsSection()).OfType<OptionsSectionViewModel>());
        await new OptionsWindow(new OptionsViewModel(sections)).ShowDialog(owner);
    }

    public async Task ShowAboutAsync()
    {
        if ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not { } owner)
            return;
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
        await new MessageBox("About CityHelper", $"CityHelper {version}\n\nCity of Heroes helper tools.\n\nIncludes PopMenu Hero, created 2026 by RayolaRayons.").ShowDialog(owner);
    }

    public void OpenUrl(string url)
    {
        if ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is { } owner)
            _ = owner.Launcher.LaunchUriAsync(new System.Uri(url));
    }
}
