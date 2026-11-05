namespace MKWSatellite.Formats.Textures;

/// <summary>
/// Tegra "block linear" surface layout used by Switch GPU textures (BNTX tile mode 0). Memory is split into GOBs
/// (groups of bytes) of 64 bytes × 8 rows = 512 bytes; GOBs are stacked vertically in "blocks" of
/// <c>2^blockHeightLog2</c> GOBs, and blocks run left to right, then top to bottom. Inside a GOB the 64×8 bytes are
/// interleaved in 16-byte × 2-row sectors. Written from the publicly documented layout (Tegra X1 TRM, "Block Linear").
/// </summary>
public static class BlockLinear
{
    private const int GobWidthBytes = 64;
    private const int GobHeight = 8;
    private const int GobSize = GobWidthBytes * GobHeight;

    /// <summary>Byte offset of (xBytes, y) inside a GOB, as Mario Kart World's textures are laid out.</summary>
    /// <remarks>
    /// Differs from the Switch 1 (Maxwell) GOB, whose sectors are 16 bytes × 2 rows
    /// (<c>x/32*256 + y%8/2*64 + x%32/16*32 + y%2*16 + x%16</c>). Here sectors are 16 bytes × 4 rows: address bits from
    /// low to high are x0..x3, y0, y1, x4, y2, x5. Found by searching all bit orders for the smoothest decoded image
    /// (BC1 and ASTC textures agree), then confirmed visually.
    /// </remarks>
    private static int GobOffset(int x, int y) =>
        (x % 16) + (y % 4) * 16 + ((x / 16) % 2) * 64 + ((y / 4) % 2) * 128 + ((x / 32) % 2) * 256;

    /// <summary>
    /// Converts a block-linear surface to a linear (row-major) one.
    /// </summary>
    /// <param name="width">Width in elements (pixels, or compressed blocks).</param>
    /// <param name="height">Height in elements.</param>
    /// <param name="bytesPerElement">Bytes per pixel or per compressed block.</param>
    /// <param name="blockHeightLog2">log2 of the block height in GOBs (BNTX stores it per texture, for mip 0).</param>
    public static byte[] Deswizzle(ReadOnlySpan<byte> src, int width, int height, int bytesPerElement, int blockHeightLog2)
    {
        var dst = new byte[width * height * bytesPerElement];
        var blockHeight = 1 << blockHeightLog2;
        var rowBytes = width * bytesPerElement;
        var widthInGobs = (rowBytes + GobWidthBytes - 1) / GobWidthBytes;
        var blockRows = GobHeight * blockHeight;          // rows per block
        var blockSize = GobSize * blockHeight;            // bytes per block (one GOB wide)

        for (var y = 0; y < height; y++)
        {
            var blockRowBase = (y / blockRows) * blockSize * widthInGobs + ((y % blockRows) / GobHeight) * GobSize;
            var dstRow = y * rowBytes;
            for (var x = 0; x < rowBytes; x += 16)
            {
                // 16-byte runs are contiguous in both layouts (bytes per element is 1..16 and divides 16 for all formats used).
                var n = Math.Min(16, rowBytes - x);
                var srcOff = blockRowBase + (x / GobWidthBytes) * blockSize + GobOffset(x, y);
                if (srcOff + n > src.Length) continue; // truncated data: leave black rather than throw
                src.Slice(srcOff, n).CopyTo(dst.AsSpan(dstRow + x, n));
            }
        }
        return dst;
    }

    /// <summary>
    /// Block height (log2, in GOBs) that the hardware uses for a mip level, given mip 0's value. Smaller mips shrink the
    /// block height while it would exceed half the mip's height in GOBs.
    /// </summary>
    public static int MipBlockHeightLog2(int blockHeightLog2, int heightInElements)
    {
        var bh = blockHeightLog2;
        var gobsHigh = (heightInElements + GobHeight - 1) / GobHeight;
        while (bh > 0 && (1 << (bh - 1)) >= gobsHigh) bh--;
        return bh;
    }
}
