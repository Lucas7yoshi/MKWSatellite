using System.Buffers.Binary;
using System.Text;

namespace MKWSatellite.Formats.Textures;

/// <summary>Switch GPU texture format: high byte = layout, low byte = numeric type (as stored in BNTX).</summary>
public readonly record struct TextureFormat(uint Raw)
{
    public int Layout => (int)(Raw >> 8) & 0xFF;
    public int Type => (int)Raw & 0xFF;
    public bool IsSrgb => Type == 0x06;

    /// <summary>Block size in pixels and bytes per block (1×1 for uncompressed). Null if unknown.</summary>
    public (int W, int H, int Bytes)? Block => Layout switch
    {
        0x02 => (1, 1, 1),            // R8
        0x07 => (1, 1, 2),            // R5G6B5
        0x09 => (1, 1, 2),            // R8G8
        0x0A => (1, 1, 2),            // R16
        0x0B => (1, 1, 4),            // R8G8B8A8
        0x0C => (1, 1, 4),            // B8G8R8A8
        0x12 => (1, 1, 4),            // R16G16 (used for height maps)
        0x15 => (1, 1, 8),            // R16G16B16A16
        0x1A or 0x1B or 0x1C or 0x1D or 0x1E or 0x1F or 0x20 => (4, 4, Layout is 0x1A or 0x1D ? 8 : 16), // BC1..BC7
        >= 0x2D and <= 0x3A => AstcBlocks[Layout - 0x2D],
        _ => null,
    };

    public bool IsAstc => Layout is >= 0x2D and <= 0x3A;

    private static readonly (int, int, int)[] AstcBlocks =
    [
        (4, 4, 16), (5, 4, 16), (5, 5, 16), (6, 5, 16), (6, 6, 16), (8, 5, 16), (8, 6, 16), (8, 8, 16),
        (10, 5, 16), (10, 6, 16), (10, 8, 16), (10, 10, 16), (12, 10, 16), (12, 12, 16),
    ];

    public override string ToString() => Layout switch
    {
        0x02 => "R8", 0x07 => "R5G6B5", 0x09 => "R8G8", 0x0A => "R16", 0x0B => "R8G8B8A8", 0x0C => "B8G8R8A8",
        0x12 => "R16G16", 0x15 => "R16G16B16A16",
        0x1A => "BC1", 0x1B => "BC2", 0x1C => "BC3", 0x1D => "BC4", 0x1E => "BC5", 0x1F => "BC6H", 0x20 => "BC7",
        _ when IsAstc => $"ASTC{Block!.Value.W}x{Block!.Value.H}",
        _ => $"0x{Layout:X2}",
    } + Type switch { 0x01 => "_UNORM", 0x02 => "_SNORM", 0x05 => "_FLOAT", 0x06 => "_SRGB", 0x0A => "_UFLOAT", _ => $"_{Type:X2}" };
}

/// <summary>One texture from a BNTX file.</summary>
public sealed class BntxTexture
{
    public required string Name { get; init; }
    public required TextureFormat Format { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Depth { get; init; }
    public required int ArrayLength { get; init; }
    public required int MipCount { get; init; }
    public required int TileMode { get; init; }
    public required int BlockHeightLog2 { get; init; }
    /// <summary>Raw (swizzled) image data; <see cref="MipOffsets"/> are relative to its start.</summary>
    public required ReadOnlyMemory<byte> Data { get; init; }
    public required long[] MipOffsets { get; init; }
    /// <summary>Bytes between array layers (each layer holds a full mip chain); 0 for single textures.</summary>
    public long LayerStride { get; init; }

    public int MipWidth(int level) => Math.Max(1, Width >> level);
    public int MipHeight(int level) => Math.Max(1, Height >> level);

    /// <summary>Smallest mip level whose larger side is at most <paramref name="maxSize"/> (0 if none is needed).</summary>
    public int LevelForMaxSize(int maxSize)
    {
        var level = 0;
        while (level + 1 < MipCount && Math.Max(MipWidth(level), MipHeight(level)) > maxSize) level++;
        return level;
    }

    /// <summary>One mip level of an array layer, deswizzled to linear blocks (still compressed if the format is).</summary>
    public byte[] GetLinearMip(int level = 0, int layer = 0)
    {
        var b = Format.Block ?? throw new NotSupportedException($"Texture format {Format} not supported");
        level = Math.Clamp(level, 0, MipOffsets.Length - 1);
        var bw = (MipWidth(level) + b.W - 1) / b.W;
        var bh = (MipHeight(level) + b.H - 1) / b.H;
        var data = Data.Span[(int)(MipOffsets[level] + Math.Clamp(layer, 0, Math.Max(0, ArrayLength - 1)) * LayerStride)..];
        if (TileMode != 0)
        {
            // Pitch linear: rows are tightly packed in what we've seen; copy as is.
            return data[..Math.Min(data.Length, bw * bh * b.Bytes)].ToArray();
        }
        return BlockLinear.Deswizzle(data, bw, bh, b.Bytes, BlockLinear.MipBlockHeightLog2(BlockHeightLog2, bh));
    }

    /// <summary>A mip level decoded to RGBA8 (top row first). Returns null for formats we can't decode yet.</summary>
    public byte[]? DecodeMip(int level = 0, int layer = 0)
    {
        level = Math.Clamp(level, 0, MipOffsets.Length - 1);
        return TextureDecoder.ToRgba8(GetLinearMip(level, layer), MipWidth(level), MipHeight(level), Format);
    }
}

/// <summary>
/// BNTX texture container (version 4.1 in Mario Kart World; platform tag "Ounc" instead of the Switch 1 "NX  ").
/// Layout read from the files: header → platform block (texture count, texture pointer array, BRTD data block) → one
/// <c>BRTI</c> info block per texture with dimensions, format, block height and a mip offset table (absolute offsets).
/// </summary>
public static class Bntx
{
    public static IReadOnlyList<BntxTexture> Parse(byte[] file)
    {
        if (file.Length < 0x40 || Encoding.ASCII.GetString(file, 0, 4) != "BNTX")
            throw new InvalidDataException("Not a BNTX file");
        var d = file.AsSpan();
        var count = BinaryPrimitives.ReadInt32LittleEndian(d[0x24..]);
        var infoArray = (int)BinaryPrimitives.ReadInt64LittleEndian(d[0x28..]);
        var result = new List<BntxTexture>(count);
        for (var i = 0; i < count; i++)
        {
            var o = (int)BinaryPrimitives.ReadInt64LittleEndian(d[(infoArray + i * 8)..]);
            if (Encoding.ASCII.GetString(file, o, 4) != "BRTI") throw new InvalidDataException("Expected BRTI block");
            var tileMode = BinaryPrimitives.ReadUInt16LittleEndian(d[(o + 0x12)..]);
            var mips = BinaryPrimitives.ReadUInt16LittleEndian(d[(o + 0x16)..]);
            var format = BinaryPrimitives.ReadUInt32LittleEndian(d[(o + 0x1C)..]);
            var width = BinaryPrimitives.ReadInt32LittleEndian(d[(o + 0x24)..]);
            var height = BinaryPrimitives.ReadInt32LittleEndian(d[(o + 0x28)..]);
            var depth = BinaryPrimitives.ReadInt32LittleEndian(d[(o + 0x2C)..]);
            var arrayLen = BinaryPrimitives.ReadInt32LittleEndian(d[(o + 0x30)..]);
            var bhLog2 = BinaryPrimitives.ReadInt32LittleEndian(d[(o + 0x34)..]);
            var imageSize = BinaryPrimitives.ReadInt32LittleEndian(d[(o + 0x50)..]);
            var namePtr = (int)BinaryPrimitives.ReadInt64LittleEndian(d[(o + 0x60)..]);
            var mipPtr = (int)BinaryPrimitives.ReadInt64LittleEndian(d[(o + 0x70)..]);

            var mipAbs = new long[Math.Max(1, (int)mips)];
            for (var m = 0; m < mipAbs.Length; m++) mipAbs[m] = BinaryPrimitives.ReadInt64LittleEndian(d[(mipPtr + m * 8)..]);
            var start = mipAbs[0];
            var available = (int)(file.Length - start);
            var len = imageSize > 0 ? Math.Min(imageSize, available) : available;
            result.Add(new BntxTexture
            {
                Name = ReadString(d, namePtr),
                Format = new TextureFormat(format),
                Width = width, Height = height, Depth = depth, ArrayLength = arrayLen,
                MipCount = mips, TileMode = tileMode, BlockHeightLog2 = bhLog2,
                Data = file.AsMemory((int)start, len),
                MipOffsets = mipAbs.Select(m => m - start).ToArray(),
                LayerStride = arrayLen > 1 && imageSize > 0 ? imageSize / arrayLen : 0,
            });
        }
        return result;
    }

    private static string ReadString(ReadOnlySpan<byte> d, int p)
    {
        if (p <= 0 || p + 2 > d.Length) return "";
        var n = BinaryPrimitives.ReadUInt16LittleEndian(d[p..]);
        return Encoding.UTF8.GetString(d.Slice(p + 2, n));
    }
}
