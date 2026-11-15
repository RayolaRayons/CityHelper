using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Pfim;

namespace CityHelper.Tools.PiggViewer.Core;

/// <summary>A decoded texture: 32-bit BGRA pixels, plus how the entry exports as separate files.</summary>
public sealed class TextureData
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required byte[] Bgra { get; init; }       // Width * Height * 4, rows top to bottom
    public required bool HasAlpha { get; init; }
    public required string Version { get; init; }    // "TEX" (v1) or "TX2" (v2)
    public required string SourceName { get; init; } // name of the embedded image, e.g. "ground.tga"
    public required IReadOnlyList<ExportSegment> Segments { get; init; }
}

/// <summary>
/// Reads the game's .texture container: a 32-byte header, the original file name, (v2) a small mip header, then a
/// complete .tga / .dds image. Layout copied from the game's text.h as used by Piglet.
/// </summary>
public static class TextureReader
{
    private const int HeaderSize = 32;

    /// <summary>Returns null (with a reason) if the data is not a texture this reader understands.</summary>
    public static TextureData? Read(byte[] blob, out string? error)
    {
        error = null;
        if (blob.Length <= HeaderSize)
        {
            error = "Too small to be a texture.";
            return null;
        }

        int fileSize = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(4));
        int width = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(8));
        int height = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(12));
        bool alpha = blob[28] != 0;

        string? version =
            blob[29] == 'T' && blob[30] == 'E' && blob[31] == 'X' ? "TEX" :
            blob[29] == 'T' && blob[30] == 'X' && blob[31] == '2' ? "TX2" : null;
        if (version is null)
        {
            error = "Unknown texture version.";
            return null;
        }

        int nameEnd = Array.IndexOf(blob, (byte)0, HeaderSize);
        if (nameEnd < 0)
        {
            error = "Texture header has no file name.";
            return null;
        }
        var path = Encoding.UTF8.GetString(blob, HeaderSize, nameEnd - HeaderSize);
        int headerSize = HeaderSize + (nameEnd - HeaderSize) + 1;

        if (fileSize <= 0 || fileSize > blob.Length - headerSize)
        {
            error = "Texture data size is invalid.";
            return null;
        }
        int textureOffset = blob.Length - fileSize;

        var textureName = path[(path.LastIndexOf('/') + 1)..];
        var stem = Path.GetFileNameWithoutExtension(textureName);
        var segments = new List<ExportSegment>
        {
            new(stem + ".hdr", 0, headerSize),
            new(textureName, textureOffset, fileSize),
        };
        if (version == "TX2" && textureOffset - headerSize > 0)
            segments.Add(new ExportSegment(stem + ".mip", headerSize, textureOffset - headerSize));

        IImage image;
        try
        {
            image = Pfimage.FromStream(new MemoryStream(blob, textureOffset, fileSize));
        }
        catch (Exception ex)
        {
            error = "Could not decode the image: " + ex.Message;
            return null;
        }

        var bgra = ToBgra(image, alpha, out error);
        if (bgra is null)
            return null;

        return new TextureData
        {
            Width = image.Width,
            Height = image.Height,
            Bgra = bgra,
            HasAlpha = alpha,
            Version = version,
            SourceName = textureName,
            Segments = segments,
        };
    }

    private static byte[]? ToBgra(IImage image, bool useAlpha, out string? error)
    {
        error = null;
        int w = image.Width, h = image.Height;
        var result = new byte[w * h * 4];

        for (int y = 0; y < h; y++)
        {
            int src = y * image.Stride;
            int dst = y * w * 4;
            for (int x = 0; x < w; x++, dst += 4)
            {
                byte b, g, r, a = 255;
                switch (image.Format)
                {
                    case ImageFormat.Rgba32:
                        b = image.Data[src]; g = image.Data[src + 1]; r = image.Data[src + 2]; a = image.Data[src + 3];
                        src += 4;
                        break;
                    case ImageFormat.Rgb24:
                        b = image.Data[src]; g = image.Data[src + 1]; r = image.Data[src + 2];
                        src += 3;
                        break;
                    case ImageFormat.Rgb8:
                        b = g = r = image.Data[src++];
                        break;
                    case ImageFormat.R5g6b5:
                    {
                        int v = image.Data[src] | (image.Data[src + 1] << 8);
                        r = (byte)(((v >> 11) & 0x1F) * 255 / 31);
                        g = (byte)(((v >> 5) & 0x3F) * 255 / 63);
                        b = (byte)((v & 0x1F) * 255 / 31);
                        src += 2;
                        break;
                    }
                    case ImageFormat.R5g5b5a1:
                    {
                        int v = image.Data[src] | (image.Data[src + 1] << 8);
                        r = (byte)(((v >> 10) & 0x1F) * 255 / 31);
                        g = (byte)(((v >> 5) & 0x1F) * 255 / 31);
                        b = (byte)((v & 0x1F) * 255 / 31);
                        a = (v & 0x8000) != 0 ? (byte)255 : (byte)0;
                        src += 2;
                        break;
                    }
                    default:
                        error = $"Image format {image.Format} is not supported.";
                        return null;
                }
                result[dst] = b;
                result[dst + 1] = g;
                result[dst + 2] = r;
                result[dst + 3] = useAlpha ? a : (byte)255;      // the game only blends when the header says alpha
            }
        }
        return result;
    }
}
