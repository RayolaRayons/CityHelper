using System;
using System.Collections.Generic;

namespace CityHelper.Tools.PiggViewer.Core;

/// <summary>
/// Small block-compression encoder (BC1 / BC3) and box-filter mip generation. Only used for the low-resolution
/// preview mip levels that a .texture carries next to its image, where simple bounding-box endpoints are plenty.
/// Pixels are 32-bit BGRA, rows top to bottom.
/// </summary>
public static class DxtEncoder
{
    /// <summary>Halves the image (2x2 box filter; a dimension that is already 1 stays 1).</summary>
    public static byte[] Downsample(byte[] bgra, int width, int height, out int newWidth, out int newHeight)
    {
        newWidth = Math.Max(1, width / 2);
        newHeight = Math.Max(1, height / 2);
        var result = new byte[newWidth * newHeight * 4];

        for (int y = 0; y < newHeight; y++)
        {
            for (int x = 0; x < newWidth; x++)
            {
                int x0 = Math.Min(x * 2, width - 1), x1 = Math.Min(x * 2 + 1, width - 1);
                int y0 = Math.Min(y * 2, height - 1), y1 = Math.Min(y * 2 + 1, height - 1);
                for (int c = 0; c < 4; c++)
                {
                    int sum = bgra[(y0 * width + x0) * 4 + c] + bgra[(y0 * width + x1) * 4 + c]
                            + bgra[(y1 * width + x0) * 4 + c] + bgra[(y1 * width + x1) * 4 + c];
                    result[(y * newWidth + x) * 4 + c] = (byte)((sum + 2) / 4);
                }
            }
        }
        return result;
    }

    /// <summary>Encodes one level as DXT1 (8 bytes per 4x4 block) or DXT5 (16 bytes per block).</summary>
    public static byte[] Encode(byte[] bgra, int width, int height, bool dxt5)
    {
        int bw = Math.Max(1, (width + 3) / 4), bh = Math.Max(1, (height + 3) / 4);
        var output = new byte[bw * bh * (dxt5 ? 16 : 8)];
        var block = new byte[16 * 4];
        int position = 0;

        for (int by = 0; by < bh; by++)
        {
            for (int bx = 0; bx < bw; bx++)
            {
                // Gather the 4x4 block, repeating edge pixels when the image isn't a multiple of 4
                for (int py = 0; py < 4; py++)
                    for (int px = 0; px < 4; px++)
                    {
                        int sx = Math.Min(bx * 4 + px, width - 1), sy = Math.Min(by * 4 + py, height - 1);
                        Array.Copy(bgra, (sy * width + sx) * 4, block, (py * 4 + px) * 4, 4);
                    }

                if (dxt5)
                {
                    EncodeAlpha(block, output, position);
                    position += 8;
                }
                EncodeColor(block, output, position, allowPunchThrough: !dxt5);
                position += 8;
            }
        }
        return output;
    }

    private static void EncodeAlpha(byte[] block, byte[] output, int at)
    {
        int min = 255, max = 0;
        for (int i = 0; i < 16; i++)
        {
            int a = block[i * 4 + 3];
            min = Math.Min(min, a);
            max = Math.Max(max, a);
        }
        output[at] = (byte)max;          // a0 > a1 selects the 8-value interpolation
        output[at + 1] = (byte)min;
        if (max == min)
            return;                      // flat alpha: all indices 0

        var palette = new int[8];
        palette[0] = max;
        palette[1] = min;
        for (int i = 1; i <= 6; i++)
            palette[i + 1] = ((7 - i) * max + i * min) / 7;

        ulong bits = 0;
        for (int i = 0; i < 16; i++)
        {
            int a = block[i * 4 + 3], best = 0, bestError = int.MaxValue;
            for (int p = 0; p < 8; p++)
            {
                int error = Math.Abs(a - palette[p]);
                if (error < bestError) { bestError = error; best = p; }
            }
            bits |= (ulong)best << (3 * i);
        }
        for (int i = 0; i < 6; i++)
            output[at + 2 + i] = (byte)(bits >> (8 * i));
    }

    private static void EncodeColor(byte[] block, byte[] output, int at, bool allowPunchThrough)
    {
        bool punch = false;
        if (allowPunchThrough)
            for (int i = 0; i < 16; i++)
                if (block[i * 4 + 3] < 128) { punch = true; break; }

        // Bounding box of the opaque pixels
        int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0, opaque = 0;
        for (int i = 0; i < 16; i++)
        {
            if (punch && block[i * 4 + 3] < 128) continue;
            int b = block[i * 4], g = block[i * 4 + 1], r = block[i * 4 + 2];
            minR = Math.Min(minR, r); maxR = Math.Max(maxR, r);
            minG = Math.Min(minG, g); maxG = Math.Max(maxG, g);
            minB = Math.Min(minB, b); maxB = Math.Max(maxB, b);
            opaque++;
        }
        if (opaque == 0)
        {
            // Entirely transparent: all index 3
            output[at + 4] = output[at + 5] = output[at + 6] = output[at + 7] = 0xFF;
            return;
        }

        // Pull the endpoints in a little: the extremes are rarely the best fit for the interior pixels
        int insetR = (maxR - minR) >> 4, insetG = (maxG - minG) >> 4, insetB = (maxB - minB) >> 4;
        int hiR = maxR - insetR, hiG = maxG - insetG, hiB = maxB - insetB;
        int loR = minR + insetR, loG = minG + insetG, loB = minB + insetB;

        // Keep the endpoints along the block's diagonal direction
        if (DiagonalSign(block, punch) < 0)
            (loG, hiG) = (hiG, loG);

        int c0 = Pack565(hiR, hiG, hiB);
        int c1 = Pack565(loR, loG, loB);

        if (!punch)
        {
            if (c0 < c1) (c0, c1) = (c1, c0);                   // c0 > c1: four-color mode
            if (c0 == c1)
            {
                // Flat block: any index works, but c0 == c1 means three-color mode, which is fine for opaque pixels
                output[at] = (byte)c0; output[at + 1] = (byte)(c0 >> 8);
                output[at + 2] = (byte)c1; output[at + 3] = (byte)(c1 >> 8);
                output[at + 4] = output[at + 5] = output[at + 6] = output[at + 7] = 0;
                return;
            }
        }
        else if (c0 > c1)
        {
            (c0, c1) = (c1, c0);                                // c0 <= c1: three-color mode with transparency
        }

        output[at] = (byte)c0; output[at + 1] = (byte)(c0 >> 8);
        output[at + 2] = (byte)c1; output[at + 3] = (byte)(c1 >> 8);

        var palette = new int[4][];
        palette[0] = Unpack565(c0);
        palette[1] = Unpack565(c1);
        if (!punch)
        {
            palette[2] = Mix(palette[0], palette[1], 2, 1, 3);
            palette[3] = Mix(palette[0], palette[1], 1, 2, 3);
        }
        else
        {
            palette[2] = Mix(palette[0], palette[1], 1, 1, 2);
            palette[3] = new[] { 0, 0, 0 };
        }

        uint indices = 0;
        for (int i = 0; i < 16; i++)
        {
            int index;
            if (punch && block[i * 4 + 3] < 128)
                index = 3;
            else
            {
                int b = block[i * 4], g = block[i * 4 + 1], r = block[i * 4 + 2];
                index = 0;
                int bestError = int.MaxValue;
                for (int p = 0; p < (punch ? 3 : 4); p++)
                {
                    int dr = r - palette[p][0], dg = g - palette[p][1], db = b - palette[p][2];
                    int error = dr * dr + dg * dg + db * db;
                    if (error < bestError) { bestError = error; index = p; }
                }
            }
            indices |= (uint)index << (2 * i);
        }
        for (int i = 0; i < 4; i++)
            output[at + 4 + i] = (byte)(indices >> (8 * i));
    }

    /// <summary>+1 if brighter pixels also have more of the other channels (R/B follow G), -1 otherwise.</summary>
    private static int DiagonalSign(byte[] block, bool punch)
    {
        long cx = 0, cy = 0, n = 0;
        long mr = 0, mg = 0, mb = 0;
        for (int i = 0; i < 16; i++)
        {
            if (punch && block[i * 4 + 3] < 128) continue;
            mb += block[i * 4]; mg += block[i * 4 + 1]; mr += block[i * 4 + 2]; n++;
        }
        if (n == 0) return 1;
        mb /= n; mg /= n; mr /= n;
        for (int i = 0; i < 16; i++)
        {
            if (punch && block[i * 4 + 3] < 128) continue;
            cx += (block[i * 4 + 2] - mr) * (block[i * 4 + 1] - mg);
            cy += (block[i * 4] - mb) * (block[i * 4 + 1] - mg);
        }
        return cx + cy < 0 ? -1 : 1;
    }

    private static int[] Mix(int[] a, int[] b, int wa, int wb, int div) =>
        new[] { (a[0] * wa + b[0] * wb) / div, (a[1] * wa + b[1] * wb) / div, (a[2] * wa + b[2] * wb) / div };

    private static int Pack565(int r, int g, int b) =>
        ((Math.Clamp(r, 0, 255) * 31 + 127) / 255 << 11) | ((Math.Clamp(g, 0, 255) * 63 + 127) / 255 << 5) | ((Math.Clamp(b, 0, 255) * 31 + 127) / 255);

    private static int[] Unpack565(int c)
    {
        int r = (c >> 11) & 31, g = (c >> 5) & 63, b = c & 31;
        return new[] { (r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2) };
    }
}
