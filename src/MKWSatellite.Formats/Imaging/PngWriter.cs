using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace MKWSatellite.Formats.Imaging;

/// <summary>Minimal RGBA8 PNG encoder (no dependency): viewport screenshots and texture dumps.</summary>
public static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <param name="rgba">Pixels, row-major, top row first, 4 bytes per pixel.</param>
    public static void Write(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        using var file = File.Create(path);
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // colour type RGBA
        WriteChunk(file, "IHDR", ihdr);

        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            var stride = width * 4;
            for (var y = 0; y < height; y++)
            {
                z.WriteByte(0); // filter: none
                z.Write(rgba.Slice(y * stride, stride));
            }
        }
        WriteChunk(file, "IDAT", raw.ToArray());
        WriteChunk(file, "IEND", []);
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
