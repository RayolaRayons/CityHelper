using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using CityHelper.Services;
using CityHelper.Services.MacroImages;
using CityHelper.ViewModels;
using CityHelper.Views;

namespace CityHelper;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var appSettings = AppSettings.Load();
        ThemeService.Apply(appSettings.Theme);          // before any window is created

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (appSettings.SetupCompleted)
                desktop.MainWindow = NewMainWindow();
            else
            {
                // First run: setup comes first; the main window opens when it closes
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var setup = new SetupWindow();
                setup.Closed += (_, _) =>
                {
                    ShowMainWindow(desktop);
                    desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                };
                desktop.MainWindow = setup;
            }
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static MainWindow NewMainWindow()
    {
        var window = new MainWindow { DataContext = new MainViewModel() };
        // First run (or nothing cached yet): offer to load the macro images
        window.Opened += async (_, _) => await MacroImageUi.PromptFirstRunAsync(window);
        return window;
    }

    private static void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var window = NewMainWindow();
        desktop.MainWindow = window;
        window.Show();
    }
}
