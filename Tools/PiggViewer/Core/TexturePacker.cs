using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Pfim;

namespace CityHelper.Tools.PiggViewer.Core;

/// <summary>
/// Puts a .texture back together from its parts (header + file name, optional mip block, .dds image), so a texture can
/// be exported, edited in an image editor and packed again.
///
/// Layout: 32-byte header | file name + NUL | mip block (optional) | .dds image.
/// Header fields rewritten here: offset 0 = size of everything before the image, offset 4 = size of the image,
/// offsets 8 / 12 = width / height. Name, flags, fade values, alpha and the version tag are kept as they were.
/// The mip block is a 16-byte header (struct size 16, width, height, GL format) followed by the smallest mip levels,
/// stored as DXT1 or DXT5; it is regenerated from the new image because the game's copy is encoded separately.
/// </summary>
public static class TexturePacker
{
    private const int HeaderSize = 32;
    private const int MipHeaderSize = 16;
    private const int PreloadMaxSize = 8;               // the mip block starts at the first level no bigger than 8 texels
    private const int GlDxt1 = 0x83F1;
    private const int GlDxt5 = 0x83F3;                  // also used for DXT3 images, as in the game's own textures

    /// <summary>Everything in a texture before the image: the header with the name, and the mip block if any.</summary>
    public sealed record Parts(byte[] Header, byte[]? MipBlock, byte[] Image);

    public static Parts Split(byte[] texture)
    {
        if (texture.Length <= HeaderSize)
            throw new InvalidDataException("Too small to be a texture.");
        int imageSize = BinaryPrimitives.ReadInt32LittleEndian(texture.AsSpan(4));
        int nameEnd = Array.IndexOf(texture, (byte)0, HeaderSize);
        if (nameEnd < 0 || imageSize <= 0 || imageSize > texture.Length - (nameEnd + 1))
            throw new InvalidDataException("Texture header is not valid.");

        int headerLength = nameEnd + 1;
        int imageOffset = texture.Length - imageSize;
        return new Parts(
            texture.AsSpan(0, headerLength).ToArray(),
            imageOffset > headerLength ? texture.AsSpan(headerLength, imageOffset - headerLength).ToArray() : null,
            texture.AsSpan(imageOffset).ToArray());
    }

    /// <summary>
    /// Replaces the image of an existing texture. An unchanged image gives back the original bytes exactly. Otherwise the
    /// size fields are updated and, if the texture had a mip block, it is regenerated from the new image.
    /// </summary>
    public static byte[] ReplaceImage(byte[] originalTexture, byte[] newImage)
    {
        var parts = Split(originalTexture);
        if (newImage.AsSpan().SequenceEqual(parts.Image))
            return originalTexture;

        // Keep the stored width / height unless the image changed size (a few textures store sizes that differ from the .dds)
        var original = DdsInfo.Parse(parts.Image);
        var replacement = DdsInfo.Parse(newImage);
        bool sameSize = original.Width == replacement.Width && original.Height == replacement.Height;
        return Pack(parts.Header, parts.MipBlock, newImage, regenerateMip: true, updateSize: !sameSize);
    }

    /// <summary>
    /// Builds a texture from exported parts. <paramref name="mipBlock"/> is the exported .mip (null if the texture had
    /// none): it is kept as is, or rebuilt from <paramref name="image"/> when <paramref name="regenerateMip"/> is set.
    /// </summary>
    public static byte[] Pack(byte[] header, byte[]? mipBlock, byte[] image, bool regenerateMip = true, bool updateSize = true)
    {
        if (header.Length <= HeaderSize || header[^1] != 0)
            throw new InvalidDataException("The header file is not a texture header (it should end with the file name).");
        var info = DdsInfo.Parse(image);

        if (mipBlock != null && regenerateMip)
            mipBlock = BuildMipBlock(image) ?? throw new InvalidDataException(
                $"The image format ({info.Format}) can't be used for the mip block; save the image as DXT1, DXT3 or DXT5.");

        int mipLength = mipBlock?.Length ?? 0;
        var result = new byte[header.Length + mipLength + image.Length];
        header.CopyTo(result, 0);
        mipBlock?.CopyTo(result, header.Length);
        image.CopyTo(result, header.Length + mipLength);

        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0), header.Length + mipLength);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), image.Length);
        if (updateSize)
        {
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8), info.Width);
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(12), info.Height);
        }
        return result;
    }

    /// <summary>
    /// Builds the mip block for a DXT image: the levels from the first one no larger than 8 texels down to 1x1, re-encoded.
    /// Returns null if the image isn't DXT1 / DXT3 / DXT5.
    /// </summary>
    public static byte[]? BuildMipBlock(byte[] dds)
    {
        var info = DdsInfo.Parse(dds);
        if (info.Format is not (DdsFormat.Dxt1 or DdsFormat.Dxt3 or DdsFormat.Dxt5))
            return null;

        var image = Pfimage.FromStream(new MemoryStream(dds));
        if (image.Format != ImageFormat.Rgba32)
            return null;
        var level = ToTightBgra(image);
        int width = image.Width, height = image.Height;

        // Walk down to the first preload level
        while (Math.Max(width, height) > PreloadMaxSize)
            level = DxtEncoder.Downsample(level, width, height, out width, out height);

        bool dxt5 = info.Format != DdsFormat.Dxt1;
        int preloadWidth = width, preloadHeight = height;
        var data = new List<byte[]>();
        while (true)
        {
            data.Add(DxtEncoder.Encode(level, width, height, dxt5));
            if (width == 1 && height == 1)
                break;
            level = DxtEncoder.Downsample(level, width, height, out width, out height);
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(MipHeaderSize);
        w.Write(preloadWidth);
        w.Write(preloadHeight);
        w.Write(dxt5 ? GlDxt5 : GlDxt1);
        foreach (var d in data)
            w.Write(d);
        return ms.ToArray();
    }

    private static byte[] ToTightBgra(IImage image)
    {
        var result = new byte[image.Width * image.Height * 4];
        for (int y = 0; y < image.Height; y++)
            Buffer.BlockCopy(image.Data, y * image.Stride, result, y * image.Width * 4, image.Width * 4);
        return result;
    }
}
