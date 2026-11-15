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
        _selectedPreset = settings.Preset;
        _backupMergedFiles = settings.BackupMergedFiles;
        Channels = LogChannels.All.Select(c => new ChannelToggle(c, settings.IncludedChannels.Contains(c))).ToList();
    }

    public override string Title => "Log Manager";

    public IReadOnlyList<string> Presets => LogManagerSettings.Presets;
    public IReadOnlyList<ChannelToggle> Channels { get; }

    [ObservableProperty]
    private string _selectedPreset;

    [ObservableProperty]
    private bool _backupMergedFiles;

    partial void OnSelectedPresetChanged(string value)
    {
        // RP selects exactly its channels; None leaves the checkboxes as they are
        if (value == LogManagerSettings.PresetRp)
            foreach (var c in Channels)
                c.IsChecked = LogChannels.RpPreset.Contains(c.Channel);
    }

    public override string? Save()
    {
        _settings.Preset = SelectedPreset;
        _settings.BackupMergedFiles = BackupMergedFiles;
        _settings.IncludedChannels = Channels.Where(c => c.IsChecked).Select(c => c.Channel).ToHashSet();
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            return "Could not write logmanager.ini:\n" + ex.Message;
        }
        return null;
    }
}
