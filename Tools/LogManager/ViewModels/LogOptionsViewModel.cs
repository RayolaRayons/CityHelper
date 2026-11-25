using System;
using System.Collections.Generic;
using System.Linq;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.Tools.LogManager.ViewModels;

public sealed partial class ChannelToggle : ObservableObject
{
    public ChannelToggle(LogChannel channel, bool isChecked)
    {
        Channel = channel;
        DisplayName = LogChannels.DisplayName(channel);
        _isChecked = isChecked;
    }

    public LogChannel Channel { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>Working copy of the Log Manager options; nothing is written until Save.</summary>
public sealed partial class LogOptionsViewModel : OptionsSectionViewModel
{
    private readonly LogManagerSettings _settings;

    public LogOptionsViewModel(LogManagerSettings settings)
    {
        _settings = settings;
        _keepCopies = settings.KeepCopies;
        _copiesFolder = settings.CopiesFolder;
        _selectedSystemFix = settings.SystemChannelFix;
        _validateOrphanedDailies = settings.ValidateOrphanedDailies;
        _selectedMergeMode = settings.MergeMode;
        Channels = LogChannels.All.Select(c => new ChannelToggle(c, settings.IncludedChannels.Contains(c))).ToList();
    }

    public override string Title => "Log Manager";

    public IReadOnlyList<ChannelToggle> Channels { get; }

    [ObservableProperty]
    private bool _keepCopies;

    [ObservableProperty]
    private string _copiesFolder = "";

    public IReadOnlyList<string> SystemFixModes => LogManagerSettings.SystemFixModes;

    [ObservableProperty]
    private string _selectedSystemFix;

    [ObservableProperty]
    private bool _validateOrphanedDailies;

    public IReadOnlyList<string> MergeModes => LogManagerSettings.MergeModes;

    [ObservableProperty]
    private string _selectedMergeMode;

    public override string? Save()
    {
        _settings.KeepCopies = KeepCopies;
        _settings.CopiesFolder = CopiesFolder.Trim();
        _settings.SystemChannelFix = SelectedSystemFix;
        _settings.ValidateOrphanedDailies = ValidateOrphanedDailies;
        _settings.MergeMode = SelectedMergeMode;
        _settings.IncludedChannels = Channels.Where(c => c.IsChecked).Select(c => c.Channel).ToHashSet();
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            return "Could not write CityHelper.ini:\n" + ex.Message;
        }
        return null;
    }
}
