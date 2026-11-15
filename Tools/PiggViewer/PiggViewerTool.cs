using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CityHelper.Ribbon;
using CityHelper.Services;
using CityHelper.Tools.PiggViewer.ViewModels;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.Tools.PiggViewer;

/// <summary>Browses, exports and creates .pigg archives. A C# rewrite of Piglet, the OuroDev pigg file editor.</summary>
public sealed class PiggViewerTool : ITool
{
    private readonly Lazy<PiggViewerViewModel> _vm;

    public PiggViewerTool(AppState state) => _vm = new(() => new PiggViewerViewModel(state));

    public string Name => "PIGG Viewer";

    public ViewModelBase CreateViewModel(AppState state) => _vm.Value;

    public IReadOnlyList<RibbonGroup> FileGroups => new[]
    {
        new RibbonGroup("Archive", new[]
        {
            new RibbonCommand("Open Archive", _vm.Value.OpenArchiveCommand),
            new RibbonCommand("Open Folder", _vm.Value.OpenFolderCommand),
            new RibbonCommand("New From Folder", _vm.Value.NewFromFolderCommand),
            new RibbonCommand("Close", _vm.Value.CloseCommand),
        }),
        new RibbonGroup("Installation", new[]
        {
            new RibbonCommand("Open Assets", _vm.Value.OpenAssetsCommand),
            new RibbonCommand("Open Mods", _vm.Value.OpenModsCommand),
        }),
    };

    public IReadOnlyList<RibbonGroup> RibbonGroups => new[]
    {
        new RibbonGroup("Export", new[]
        {
            new RibbonCommand("Export Files", _vm.Value.ExportFilesCommand),
            new RibbonCommand("Export Folder", _vm.Value.ExportDirectoryCommand),
        }),
        new RibbonGroup("Texture", new[]
        {
            new RibbonCommand("Pack Texture", _vm.Value.PackTextureCommand),
        }),
    };

    // Piglet's own help links
    public IReadOnlyList<RibbonGroup> InformationGroups => new[]
    {
        new RibbonGroup("Piglet", new[]
        {
            Link("OuroDev Wiki", "https://ourowiki.ouro-comdev.com"),
            Link("OuroDev Git", "https://git.ourodev.com"),
            Link("Bug Tracker", "https://bugs.ourodev.com"),
        }),
    };

    private static RibbonCommand Link(string label, string url) => new(label, new RelayCommand(() =>
    {
        if ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is { } owner)
            _ = owner.Launcher.LaunchUriAsync(new Uri(url));
    }));
}
