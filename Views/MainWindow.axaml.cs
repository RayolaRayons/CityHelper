using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using CityHelper.Services;

namespace CityHelper.Views;

public partial class MainWindow : Window
{
    // The window opens 50% larger than the old 1100 x 700 until a size has been saved
    private const double DefaultWidth = 1650;
    private const double DefaultHeight = 1050;

    public MainWindow()
    {
        InitializeComponent();

        var settings = AppSettings.Load();
        Width = settings.WindowWidth > 0 ? Math.Max(settings.WindowWidth, MinWidth) : DefaultWidth;
        Height = settings.WindowHeight > 0 ? Math.Max(settings.WindowHeight, MinHeight) : DefaultHeight;
        if (settings.WindowMaximized)
            WindowState = WindowState.Maximized;

        Opened += (_, _) => FitToScreen();
        Closing += (_, _) => SaveSize();
    }

    /// <summary>A saved or default size larger than the screen it opens on is reduced to fit, and the window is re-centered.</summary>
    private void FitToScreen()
    {
        if (WindowState != WindowState.Normal || Screens.ScreenFromWindow(this) is not { } screen)
            return;

        var area = screen.WorkingArea;
        double maxWidth = area.Width / screen.Scaling, maxHeight = area.Height / screen.Scaling;
        if (Width <= maxWidth && Height <= maxHeight)
            return;

        Width = Math.Min(Width, maxWidth);
        Height = Math.Min(Height, maxHeight);
        Position = new PixelPoint(
            area.X + (int)((area.Width - Width * screen.Scaling) / 2),
            area.Y + (int)((area.Height - Height * screen.Scaling) / 2));
    }

    /// <summary>Remembers the size (the normal size, not the maximized one) and whether the window was maximized.</summary>
    private void SaveSize()
    {
        try
        {
            var settings = AppSettings.Load();
            settings.WindowMaximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                settings.WindowWidth = ClientSize.Width;
                settings.WindowHeight = ClientSize.Height;
            }
            settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // remembering the size is a convenience
        }
    }
}
