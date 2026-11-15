using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CityHelper.ViewModels;

namespace CityHelper.Views;

public partial class InstallationEditWindow : Window
{
    private readonly HashSet<string> _otherNames = new(StringComparer.OrdinalIgnoreCase);

    public InstallationEditWindow()
    {
        InitializeComponent();
    }

    public InstallationEditWindow(InstallationEdit? existing, IEnumerable<string> otherNames) : this()
    {
        foreach (var n in otherNames)
            _otherNames.Add(n);
        Title = existing is null ? "Add Installation" : "Edit Installation";
        this.FindControl<TextBox>("NameBox")!.Text = existing?.Name ?? "";
        this.FindControl<TextBox>("PathBox")!.Text = existing?.Path ?? "";
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var pathBox = this.FindControl<TextBox>("PathBox")!;
        IStorageFolder? start = null;
        if (!string.IsNullOrWhiteSpace(pathBox.Text) && Directory.Exists(pathBox.Text))
            start = await StorageProvider.TryGetFolderFromPathAsync(pathBox.Text);
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select City of Heroes folder",
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            pathBox.Text = path;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var name = this.FindControl<TextBox>("NameBox")!.Text?.Trim() ?? "";
        var path = this.FindControl<TextBox>("PathBox")!.Text?.Trim() ?? "";
        var error = this.FindControl<TextBlock>("ErrorText")!;

        string? problem =
            name.Length == 0 ? "Enter a display name." :
            path.Length == 0 ? "Enter or browse to a folder." :
            _otherNames.Contains(name) ? "Another installation already uses that display name." : null;

        error.IsVisible = problem != null;
        error.Text = problem;
        if (problem == null)
            Close(new InstallationEdit(name, path));
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
