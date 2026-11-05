using ZstdSharp;

namespace MKWSatellite.Formats.Compression;

/// <summary>Zstandard helpers for the game's <c>*.zs</c> files.</summary>
public static class Zstd
{
    private static readonly byte[] Magic = [0x28, 0xB5, 0x2F, 0xFD];

    public static bool IsCompressed(ReadOnlySpan<byte> data) => data.Length >= 4 && data[..4].SequenceEqual(Magic);

    /// <summary>Decompresses a zstd frame. Files that need a trained dictionary are not supported yet.</summary>
    public static byte[] Decompress(ReadOnlySpan<byte> data)
    {
        using var d = new Decompressor();
        return d.Unwrap(data).ToArray();
    }

    /// <summary>Compresses to a single zstd frame (no dictionary), as the game's <c>.zs</c> files are.</summary>
    public static byte[] Compress(ReadOnlySpan<byte> data, int level = 19)
    {
        using var c = new Compressor(level);
        return c.Wrap(data).ToArray();
    }

    /// <summary>Returns the data decompressed if it is a zstd frame, otherwise unchanged.</summary>
    public static byte[] DecompressIfNeeded(byte[] data) => IsCompressed(data) ? Decompress(data) : data;
}
