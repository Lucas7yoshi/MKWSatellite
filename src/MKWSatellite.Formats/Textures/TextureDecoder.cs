namespace MKWSatellite.Formats.Textures;

/// <summary>Decodes linear (already deswizzled) texture data to RGBA8. All decoders are managed C#.</summary>
public static class TextureDecoder
{
    /// <summary>Returns RGBA8 pixels (top row first), or null if the format isn't supported yet (BC6H, BC7).</summary>
    /// <remarks>sRGB data is returned as stored (still sRGB-encoded); the renderer decides how to sample it.</remarks>
    public static byte[]? ToRgba8(byte[] linear, int width, int height, TextureFormat format)
    {
        if (format.IsAstc)
        {
            var b = format.Block!.Value;
            return AstcDecoder.Decode(linear, width, height, b.W, b.H, format.IsSrgb);
        }
        return format.Layout switch
        {
            0x1A => BcDecoder.Decode(linear, width, height, BcDecoder.Kind.Bc1),
            0x1B => BcDecoder.Decode(linear, width, height, BcDecoder.Kind.Bc2),
            0x1C => BcDecoder.Decode(linear, width, height, BcDecoder.Kind.Bc3),
            0x1D => BcDecoder.Decode(linear, width, height, format.Type == 0x02 ? BcDecoder.Kind.Bc4Signed : BcDecoder.Kind.Bc4),
            0x1E => BcDecoder.Decode(linear, width, height, format.Type == 0x02 ? BcDecoder.Kind.Bc5Signed : BcDecoder.Kind.Bc5),
            0x0B => Copy(linear, width * height * 4),
            0x0C => Bgra(linear, width * height),
            0x02 => Expand(linear, width * height, 1, (s, i, o) => { o[0] = o[1] = o[2] = s[i]; o[3] = 255; }),
            0x09 => Expand(linear, width * height, 2, (s, i, o) => { o[0] = s[i]; o[1] = s[i + 1]; o[2] = 0; o[3] = 255; }),
            0x0A => Expand(linear, width * height, 2, (s, i, o) => { o[0] = o[1] = o[2] = s[i + 1]; o[3] = 255; }),
            0x07 => Expand(linear, width * height, 2, (s, i, o) =>
            {
                var v = s[i] | (s[i + 1] << 8);
                o[0] = (byte)(((v >> 11) & 31) * 255 / 31); o[1] = (byte)(((v >> 5) & 63) * 255 / 63); o[2] = (byte)((v & 31) * 255 / 31); o[3] = 255;
            }),
            _ => null,
        };
    }

    private delegate void PixelFn(byte[] src, int index, Span<byte> rgba);

    private static byte[] Copy(byte[] src, int n)
    {
        var dst = new byte[n];
        src.AsSpan(0, Math.Min(n, src.Length)).CopyTo(dst);
        return dst;
    }

    private static byte[] Bgra(byte[] src, int pixels)
    {
        var dst = Copy(src, pixels * 4);
        for (var i = 0; i < dst.Length; i += 4) (dst[i], dst[i + 2]) = (dst[i + 2], dst[i]);
        return dst;
    }

    private static byte[] Expand(byte[] src, int pixels, int bpp, PixelFn fn)
    {
        var dst = new byte[pixels * 4];
        for (var p = 0; p < pixels && (p + 1) * bpp <= src.Length; p++) fn(src, p * bpp, dst.AsSpan(p * 4, 4));
        return dst;
    }
}
