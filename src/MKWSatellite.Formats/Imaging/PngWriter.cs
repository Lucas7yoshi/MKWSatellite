using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace MKWSatellite.Formats.Imaging;

/// <summary>
/// RGBA8 PNG encoder (no dependency): viewport screenshots and texture dumps. Writes RGB when every pixel is opaque,
/// picks a filter per row (smallest sum of absolute filtered bytes, the usual heuristic) and uses zlib's smallest
/// setting.
/// </summary>
public static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <param name="rgba">Pixels, row-major, top row first, 4 bytes per pixel.</param>
    public static void Write(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        var opaque = true;
        for (var i = 3; i < rgba.Length; i += 4) if (rgba[i] != 255) { opaque = false; break; }
        var bpp = opaque ? 3 : 4;
        var stride = width * bpp;

        // Packed pixels (RGB when opaque).
        var px = new byte[stride * height];
        if (opaque) for (int s = 0, d = 0; s < width * height * 4; s += 4, d += 3) { px[d] = rgba[s]; px[d + 1] = rgba[s + 1]; px[d + 2] = rgba[s + 2]; }
        else rgba[..px.Length].CopyTo(px);

        // Filter each row independently (rows only read the unfiltered row above).
        var filtered = new byte[(stride + 1) * height];
        Parallel.For(0, height, y => FilterRow(px, filtered, y, stride, bpp));

        using var file = File.Create(path);
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;                     // bit depth
        ihdr[9] = (byte)(opaque ? 2 : 6); // colour type RGB / RGBA
        WriteChunk(file, "IHDR", ihdr);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.SmallestSize, leaveOpen: true)) z.Write(filtered);
        WriteChunk(file, "IDAT", raw.ToArray());
        WriteChunk(file, "IEND", []);
    }

    private static void FilterRow(byte[] px, byte[] outBuf, int y, int stride, int bpp)
    {
        var cur = px.AsSpan(y * stride, stride);
        var prev = y > 0 ? px.AsSpan((y - 1) * stride, stride) : new byte[stride];
        var best = new byte[stride];
        var tmp = new byte[stride];
        var bestType = 0;
        var bestScore = long.MaxValue;
        for (var type = 0; type < 5; type++)
        {
            long score = 0;
            for (var i = 0; i < stride; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0, x = cur[i];
                var f = type switch
                {
                    0 => x,
                    1 => x - a,
                    2 => x - b,
                    3 => x - ((a + b) >> 1),
                    _ => x - Paeth(a, b, c),
                };
                var v = (byte)f;
                tmp[i] = v;
                score += v < 128 ? v : 256 - v;
            }
            if (score < bestScore) { bestScore = score; bestType = type; (best, tmp) = (tmp, best); }
        }
        var o = y * (stride + 1);
        outBuf[o] = (byte)bestType;
        best.CopyTo(outBuf, o + 1);
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = Crc(Crc(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(len, crc);
        s.Write(len);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }
}
