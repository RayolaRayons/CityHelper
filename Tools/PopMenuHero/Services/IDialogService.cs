using System.Threading.Tasks;

namespace CityHelper.Tools.PopMenuHero.Services;

public enum MessageKind
{
    Info,
    Warning,
    Error,
}

/// <summary>UI interactions the view models need, implemented by the main window.</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message);
    Task ShowMessageAsync(string title, string message, MessageKind kind = MessageKind.Info);
    Task<string?> PickOpenFileAsync(string title, string? startDirectory);
    Task<string?> PickSaveFileAsync(string title, string? startDirectory, string? suggestedFileName);
    Task<string?> PickFolderAsync(string title, string? startDirectory);
    void OpenUrl(string url);
    /// <summary>Shows (or brings forward) the non-modal MNU preview window.</summary>
    void ShowPreview(CityHelper.Tools.PopMenuHero.ViewModels.MnuPreviewViewModel preview);
    void CloseWindow();
}
