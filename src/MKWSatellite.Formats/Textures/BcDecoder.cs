using System.Buffers.Binary;

namespace MKWSatellite.Formats.Textures;

/// <summary>BC1–BC5 (S3TC / RGTC) block decoders, written from the public format descriptions.</summary>
public static class BcDecoder
{
    public enum Kind { Bc1, Bc2, Bc3, Bc4, Bc4Signed, Bc5, Bc5Signed }

    public static byte[] Decode(byte[] src, int width, int height, Kind kind)
    {
        var bytesPerBlock = kind is Kind.Bc1 or Kind.Bc4 or Kind.Bc4Signed ? 8 : 16;
        var bw = (width + 3) / 4;
        var bh = (height + 3) / 4;
        var dst = new byte[width * height * 4];
        Parallel.For(0, bh, by =>
        {
            Span<byte> block = stackalloc byte[16 * 4];
            for (var bx = 0; bx < bw; bx++)
            {
                var off = (by * bw + bx) * bytesPerBlock;
                if (off + bytesPerBlock > src.Length) return;
                var s = src.AsSpan(off, bytesPerBlock);
                switch (kind)
                {
                    case Kind.Bc1: Color(s, block, true); break;
                    case Kind.Bc2: Color(s[8..], block, false); ExplicitAlpha(s, block); break;
                    case Kind.Bc3: Color(s[8..], block, false); Channel(s, block, 3, false); break;
                    case Kind.Bc4 or Kind.Bc4Signed:
                        Channel(s, block, 0, kind == Kind.Bc4Signed);
                        for (var i = 0; i < 16; i++) { block[i * 4 + 1] = block[i * 4 + 2] = block[i * 4]; block[i * 4 + 3] = 255; }
                        break;
                    default: // BC5: red/green, blue left at 0 (normal maps reconstruct Z in the shader)
                        Channel(s, block, 0, kind == Kind.Bc5Signed);
                        Channel(s[8..], block, 1, kind == Kind.Bc5Signed);
                        for (var i = 0; i < 16; i++) { block[i * 4 + 2] = 0; block[i * 4 + 3] = 255; }
                        break;
                }
                Store(block, dst, width, height, bx * 4, by * 4);
            }
        });
        return dst;
    }

    private static void Store(ReadOnlySpan<byte> block, byte[] dst, int width, int height, int x0, int y0)
    {
        for (var y = 0; y < 4 && y0 + y < height; y++)
        for (var x = 0; x < 4 && x0 + x < width; x++)
            block.Slice((y * 4 + x) * 4, 4).CopyTo(dst.AsSpan(((y0 + y) * width + x0 + x) * 4, 4));
    }

    private static void Color(ReadOnlySpan<byte> s, Span<byte> block, bool allowPunchThrough)
    {
        var c0 = BinaryPrimitives.ReadUInt16LittleEndian(s);
        var c1 = BinaryPrimitives.ReadUInt16LittleEndian(s[2..]);
        var idx = BinaryPrimitives.ReadUInt32LittleEndian(s[4..]);
        Span<int> pal = stackalloc int[16];
        Rgb565(c0, pal[..4]);
        Rgb565(c1, pal[4..8]);
        if (c0 > c1 || !allowPunchThrough)
        {
            for (var k = 0; k < 3; k++)
            {
                pal[8 + k] = (2 * pal[k] + pal[4 + k]) / 3;
                pal[12 + k] = (pal[k] + 2 * pal[4 + k]) / 3;
            }
            pal[11] = pal[15] = 255;
        }
        else
        {
            for (var k = 0; k < 3; k++) { pal[8 + k] = (pal[k] + pal[4 + k]) / 2; pal[12 + k] = 0; }
            pal[11] = 255; pal[15] = 0;
        }
        for (var i = 0; i < 16; i++)
        {
            var p = (int)(idx >> (i * 2)) & 3;
            for (var k = 0; k < 4; k++) block[i * 4 + k] = (byte)pal[p * 4 + k];
        }
    }

    private static void Rgb565(ushort c, Span<int> o)
    {
        o[0] = ((c >> 11) & 31) * 255 / 31;
        o[1] = ((c >> 5) & 63) * 255 / 63;
        o[2] = (c & 31) * 255 / 31;
        o[3] = 255;
    }

    private static void ExplicitAlpha(ReadOnlySpan<byte> s, Span<byte> block)
    {
        var a = BinaryPrimitives.ReadUInt64LittleEndian(s);
        for (var i = 0; i < 16; i++) block[i * 4 + 3] = (byte)(((a >> (i * 4)) & 15) * 17);
    }

    /// <summary>BC4-style 8-byte single channel block into channel <paramref name="ch"/>.</summary>
    private static void Channel(ReadOnlySpan<byte> s, Span<byte> block, int ch, bool signed)
    {
        Span<int> pal = stackalloc int[8];
        int a0, a1;
        if (signed) { a0 = Math.Max((sbyte)s[0], (sbyte)-127) + 127; a1 = Math.Max((sbyte)s[1], (sbyte)-127) + 127; a0 = a0 * 255 / 254; a1 = a1 * 255 / 254; }
        else { a0 = s[0]; a1 = s[1]; }
        pal[0] = a0; pal[1] = a1;
        if (a0 > a1) for (var k = 1; k < 7; k++) pal[k + 1] = ((7 - k) * a0 + k * a1) / 7;
        else
        {
            for (var k = 1; k < 5; k++) pal[k + 1] = ((5 - k) * a0 + k * a1) / 5;
            pal[6] = 0; pal[7] = 255;
        }
        ulong bits = 0;
        for (var k = 0; k < 6; k++) bits |= (ulong)s[2 + k] << (8 * k);
        for (var i = 0; i < 16; i++) block[i * 4 + ch] = (byte)pal[(int)(bits >> (i * 3)) & 7];
    }
}
