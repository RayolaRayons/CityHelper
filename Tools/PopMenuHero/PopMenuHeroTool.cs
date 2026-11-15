using System;
using System.Collections.Generic;
using CityHelper.Ribbon;
using CityHelper.Services;
using CityHelper.Tools.PopMenuHero.Services;
using CityHelper.Tools.PopMenuHero.ViewModels;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.Tools.PopMenuHero;

/// <summary>PopMenu Hero: .mnu popmenu editor, ported from the standalone PopMenuHero app.</summary>
public sealed class PopMenuHeroTool : ITool
{
    private readonly Lazy<PopMenuHeroViewModel> _vm;

    public PopMenuHeroTool(AppState state)
    {
        _vm = new(() => new PopMenuHeroViewModel(new DialogService(), state));
    }

    public string Name => "PopmenuHero";

    public ViewModelBase CreateViewModel(AppState state) => _vm.Value;

    public System.Windows.Input.ICommand? SaveCommand => _vm.Value.SaveCommand;

    /// <summary>Document commands on the File tab. Save is the File tab's global Save (see SaveCommand).</summary>
    public IReadOnlyList<RibbonGroup> FileGroups
    {
        get
        {
            var vm = _vm.Value;
            return new[]
            {
                new RibbonGroup("Document", new[]
                {
                    new RibbonCommand("New", vm.NewCommand),
                    new RibbonCommand("Open", vm.OpenCommand),
                    new RibbonCommand("Save As", vm.SaveAsCommand),
                    new RibbonCommand("Import", vm.ImportMenuCommand),
                }),
            };
        }
    }

    public IReadOnlyList<RibbonGroup> RibbonGroups
    {
        get
        {
            var vm = _vm.Value;
            return new[]
            {
                new RibbonGroup("Edit", new[]
                {
                    new RibbonCommand("Undo", vm.UndoCommand),
                    new RibbonCommand("Copy", vm.CopyCommand),
                    new RibbonCommand("Paste", vm.PasteCommand),
                    new RibbonCommand("Delete", vm.DeleteSelectedCommand),
                }),
                new RibbonGroup("View", new[]
                {
                    new RibbonCommand("Expand All", vm.ExpandAllCommand),
                    new RibbonCommand("Collapse Root", vm.CollapseToRootCommand),
                    new RibbonCommand("Collapse All", vm.CollapseAllCommand),
                    new RibbonCommand("MNU Preview", vm.OpenPreviewCommand),
                }),
            };
        }
    }

    public IReadOnlyList<RibbonGroup> InformationGroups
    {
        get
        {
            var vm = _vm.Value;
            RibbonCommand Link(string label, string url) =>
                new(label, new RelayCommand(() => vm.Dialogs.OpenUrl(url)));

            return new[]
            {
                new RibbonGroup("Links", new[]
                {
                    Link("Homecoming Forum", "https://forums.homecomingservers.com/topic/63727-popmenu-hero-gui-editor-for-popmenus/"),
                    Link("City of Data 2.0", "https://cod.uberguy.net"),
                    Link("PopMenu Wiki", "https://homecoming.wiki/wiki/Popmenu_(Slash_Command)"),
                    Link("PopMenus: What, Why, How", "https://forums.homecomingservers.com/topic/6701-popmenus-the-what-why-and-how/"),
                }),
            };
        }
    }
}
