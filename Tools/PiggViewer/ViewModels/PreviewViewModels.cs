using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CityHelper.Tools.PiggViewer.Core;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.Tools.PiggViewer.ViewModels;

/// <summary>What the lower pane shows for the selected file. The view is found by name (TexturePreviewView ...).</summary>
public abstract partial class PreviewViewModel : ViewModelBase
{
    private readonly Func<Task> _export;

    protected PreviewViewModel(Func<Task> export) => _export = export;

    [RelayCommand]
    private Task Export() => _export();
}

public sealed partial class TexturePreviewViewModel : PreviewViewModel
{
    private const double ZoomStep = 0.95;      // per mouse wheel notch, as in the original

    public TexturePreviewViewModel(TextureData texture, Func<Task> export) : base(export)
    {
        Texture = texture;
        Bitmap = CreateBitmap(texture);
        InfoText = $"{texture.Width} x {texture.Height}   {texture.Version}   {texture.SourceName}" +
                   (texture.HasAlpha ? "   alpha" : "");
    }

    public TextureData Texture { get; }
    public WriteableBitmap Bitmap { get; }
    public string InfoText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayWidth), nameof(DisplayHeight))]
    private double _scale = 1.0;

    public double DisplayWidth => Texture.Width * Scale;
    public double DisplayHeight => Texture.Height * Scale;

    public void ZoomIn() => Scale = Math.Min(Scale / ZoomStep, 32);
    public void ZoomOut() => Scale = Math.Max(Scale * ZoomStep, 0.02);

    private static WriteableBitmap CreateBitmap(TextureData texture)
    {
        var bitmap = new WriteableBitmap(new PixelSize(texture.Width, texture.Height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var frame = bitmap.Lock();
        int rowBytes = texture.Width * 4;
        for (int y = 0; y < texture.Height; y++)
            System.Runtime.InteropServices.Marshal.Copy(texture.Bgra, y * rowBytes, frame.Address + y * frame.RowBytes, rowBytes);
        return bitmap;
    }
}

public sealed class TextPreviewViewModel : PreviewViewModel
{
    public TextPreviewViewModel(string text, string note, Func<Task> export) : base(export)
    {
        Text = text;
        Note = note;
    }

    public string Text { get; }
    public string Note { get; }
}

/// <summary>Hex dump of a file the viewer has no better view for.</summary>
public sealed class BinaryPreviewViewModel : PreviewViewModel
{
    public BinaryPreviewViewModel(string hex, string note, Func<Task> export) : base(export)
    {
        Hex = hex;
        Note = note;
    }

    public string Hex { get; }
    public string Note { get; }
}

public sealed class MessagePreviewViewModel : PreviewViewModel
{
    public MessagePreviewViewModel(string message, Func<Task> export) : base(export) => Message = message;

    public string Message { get; }
}
