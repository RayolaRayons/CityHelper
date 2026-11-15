using System;
using System.IO;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CityHelper.Tools.LogViewer.ViewModels;

/// <summary>Working copy of the Log Viewer options; nothing is written until Save.</summary>
public sealed partial class LogViewerOptionsViewModel : OptionsSectionViewModel
{
    private readonly LogViewerSettings _settings;

    public LogViewerOptionsViewModel(LogViewerSettings settings)
    {
        _settings = settings;
        _colorModeIndex = (int)settings.ColorCodes;
        _channelColorIndex = settings.UseChannelColors ? 1 : 0;
        _wordWrap = settings.WordWrap;
    }

    public override string Title => "Log Viewer";

    /// <summary>0 = hide the tags, 1 = show the raw tags inline.</summary>
    [ObservableProperty] private int _colorModeIndex;

    /// <summary>0 = off, 1 = use the default channel colors.</summary>
    [ObservableProperty] private int _channelColorIndex;

    [ObservableProperty] private bool _wordWrap;

    public override string? Save()
    {
        _settings.ColorCodes = (ColorCodeMode)Math.Clamp(ColorModeIndex, 0, 1);
        _settings.UseChannelColors = ChannelColorIndex == 1;
        _settings.WordWrap = WordWrap;
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Could not write logviewer.ini:\n" + ex.Message;
        }
        return null;
    }
}
