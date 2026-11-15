using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CityHelper.Tools.PiggViewer.Core;

public enum DdsFormat
{
    Dxt1,
    Dxt3,
    Dxt5,
    Uncompressed,
    Other,       // DX10 header, BC4/BC5/BC7, YUV ...
}

/// <summary>The few things about a .dds file the texture packer needs.</summary>
public sealed record DdsInfo(int Width, int Height, int MipLevels, DdsFormat Format, int DataOffset)
{
    public const int HeaderSize = 128;       // "DDS " + the 124 byte header

    /// <summary>Size in bytes of one level of a block-compressed image (4x4 blocks).</summary>
    public static int LevelSize(DdsFormat format, int width, int height) =>
        Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * (format == DdsFormat.Dxt1 ? 8 : 16);

    public static DdsInfo Parse(byte[] dds)
    {
        if (dds.Length < HeaderSize || dds[0] != 'D' || dds[1] != 'D' || dds[2] != 'S' || dds[3] != ' ')
            throw new InvalidDataException("Not a .dds file.");
        if (BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(4)) != 124)
            throw new InvalidDataException("Unexpected .dds header size.");

        int height = BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(12));
        int width = BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(16));
        int mips = Math.Max(1, BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(28)));
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
            throw new InvalidDataException("Invalid .dds dimensions.");

        int pixelFormatFlags = BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(80));
        var fourCc = Encoding.ASCII.GetString(dds, 84, 4);
        DdsFormat format;
        if ((pixelFormatFlags & 0x4) != 0)            // DDPF_FOURCC
            format = fourCc switch
            {
                "DXT1" => DdsFormat.Dxt1,
                "DXT3" => DdsFormat.Dxt3,
                "DXT5" => DdsFormat.Dxt5,
                _ => DdsFormat.Other,
            };
        else
            format = (pixelFormatFlags & 0x41) != 0 ? DdsFormat.Uncompressed : DdsFormat.Other;   // RGB / ALPHAPIXELS

        return new DdsInfo(width, height, mips, format, HeaderSize);
    }

    /// <summary>Builds a .dds from block-compressed levels (largest first), for tools and tests.</summary>
    public static byte[] Build(DdsFormat format, int width, int height, IReadOnlyList<byte[]> levels)
    {
        if (format is not (DdsFormat.Dxt1 or DdsFormat.Dxt3 or DdsFormat.Dxt5))
            throw new ArgumentException("Only DXT1 / DXT3 / DXT5 can be built.");

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("DDS "));
        w.Write(124);
        w.Write(0x000A1007);                 // CAPS | HEIGHT | WIDTH | PIXELFORMAT | MIPMAPCOUNT | LINEARSIZE
        w.Write(height);
        w.Write(width);
        w.Write(levels[0].Length);
        w.Write(0);
        w.Write(levels.Count);
        for (int i = 0; i < 11; i++) w.Write(0);
        w.Write(32);                         // pixel format
        w.Write(0x4);                        // FOURCC
        w.Write(Encoding.ASCII.GetBytes(format switch { DdsFormat.Dxt1 => "DXT1", DdsFormat.Dxt3 => "DXT3", _ => "DXT5" }));
        for (int i = 0; i < 5; i++) w.Write(0);
        w.Write(levels.Count > 1 ? 0x401008 : 0x1000);   // TEXTURE (+ COMPLEX | MIPMAP)
        for (int i = 0; i < 4; i++) w.Write(0);
        foreach (var level in levels)
            w.Write(level);
        return ms.ToArray();
    }
}
