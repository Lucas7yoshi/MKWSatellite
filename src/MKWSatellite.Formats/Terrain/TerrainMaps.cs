using System.Buffers.Binary;
using System.Text;
using MKWSatellite.Formats.Compression;
using MKWSatellite.Formats.IO;
using MKWSatellite.Formats.Textures;

namespace MKWSatellite.Formats.Terrain;

/// <summary>
/// Per-area terrain maps (<c>TerrainArc/&lt;scene&gt;/&lt;area&gt;.hght</c>, <c>.mate</c>, <c>.color_mul2.extm</c>, ...).
/// Each file is a <c>TARB</c> wrapper (u16 header size at 0x0A, u32 decompressed size at 0x0C) around one zstd frame.
/// The payload is a 0x34-byte surface header, then the surface; the last u32 of the header is the surface size and the
/// surface is the last that many bytes (height maps carry another block before it, not needed for drawing).
/// Surfaces are 260×260 texels, block-linear with a block height of one GOB. A tile's 256 cells span the area edge:
/// texel i sits at <c>min + i·size/256</c>, so texels 0..256 cover the area and 257..259 overlap the next one.
/// </summary>
public static class TerrainMaps
{
    public const int Resolution = 260;
    public const int Cells = 256;

    /// <summary>The surface bytes of a TARB file (still block-linear).</summary>
    public static ReadOnlySpan<byte> Surface(byte[] payload)
    {
        var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0x30));
        return payload.AsSpan(payload.Length - size, size);
    }

    public static byte[] Unwrap(byte[] file)
    {
        if (file.Length < 0x14 || Encoding.ASCII.GetString(file, 0, 4) != "TARB") throw new InvalidDataException("Not a TARB file");
        var headerSize = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x0A));
        return Zstd.Decompress(file.AsSpan(headerSize));
    }

    /// <summary>Height map: 260×260 u16, row = Z, column = X; world height = value / 65535 × scene max height.</summary>
    public static ushort[] Heights(byte[] file)
    {
        var lin = BlockLinear.Deswizzle(Surface(Unwrap(file)), Resolution, Resolution, 2, 0);
        var h = new ushort[Resolution * Resolution];
        Buffer.BlockCopy(lin, 0, h, 0, Math.Min(lin.Length, h.Length * 2));
        return h;
    }

    /// <summary>Material map: 260×260 RGBA8 = (material A, material B, blend A→B 0..255, unused).</summary>
    public static byte[] Materials(byte[] file) => BlockLinear.Deswizzle(Surface(Unwrap(file)), Resolution, Resolution, 4, 0);

    /// <summary>A BC3 colour map (<c>albedo_far</c>, <c>color_mul2</c>) decoded to 260×260 RGBA8.</summary>
    public static byte[] Bc3(byte[] file)
    {
        const int blocks = Resolution / 4;
        var lin = BlockLinear.Deswizzle(Surface(Unwrap(file)), blocks, blocks, 16, 0);
        return BcDecoder.Decode(lin, Resolution, Resolution, BcDecoder.Kind.Bc3);
    }

    /// <summary>Water map texels per edge: one per 4 height cells (65 = 64 cells + 1).</summary>
    public const int WaterResolution = 65;

    /// <summary>
    /// Water map (<c>water.extm</c>): 65×65 × 4 u16. Channel 0 = water surface height (same scale as the height map),
    /// channels 1/2 sit at about 0.5 (flow direction, unverified), channel 3 is a small type value (unverified). There is
    /// water wherever the surface is above the terrain. Returned as RGBA16.
    /// </summary>
    public static ushort[] Water(byte[] file)
    {
        var lin = BlockLinear.Deswizzle(Surface(Unwrap(file)), WaterResolution, WaterResolution, 8, 0);
        var w = new ushort[WaterResolution * WaterResolution * 4];
        Buffer.BlockCopy(lin, 0, w, 0, Math.Min(lin.Length, w.Length * 2));
        return w;
    }

    /// <summary>romfs path of one map of an area, e.g. <c>TerrainArc/Game03_Proc/6700003C4A.hght</c>.</summary>
    public static string PathOf(string scene, string area, string extension) => $"TerrainArc/{scene}/{area}.{extension}";

    /// <summary>Reads a file directly (no zstd auto-detection; TARB files start with their own magic).</summary>
    public static byte[]? TryRead(GameFileSystem fs, string path) => fs.Resolve(path) is { } p ? File.ReadAllBytes(p) : null;
}
