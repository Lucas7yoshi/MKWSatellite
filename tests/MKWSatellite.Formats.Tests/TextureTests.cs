using System.Text;
using MKWSatellite.Formats.Archives;
using MKWSatellite.Formats.Textures;

namespace MKWSatellite.Formats.Tests;

public class TextureTests
{
    [Fact]
    public void AstcVoidExtentBlockIsOneColour()
    {
        // Void-extent block: mode bits 0x1FC, extent coordinates all ones, colour as UNORM16 RGBA in bits 64..127.
        var block = new byte[16];
        BitConverter.GetBytes(0xFFFFFFFFFFFFFDFCUL).CopyTo(block, 0);
        BitConverter.GetBytes((ushort)0x1234).CopyTo(block, 8);  // R
        BitConverter.GetBytes((ushort)0xAB00).CopyTo(block, 10); // G
        BitConverter.GetBytes((ushort)0x0000).CopyTo(block, 12); // B
        BitConverter.GetBytes((ushort)0xFFFF).CopyTo(block, 14); // A
        var rgba = AstcDecoder.Decode(block, 8, 8, 8, 8, srgb: false);
        for (var i = 0; i < 64; i++)
            Assert.Equal(new byte[] { 0x12, 0xAB, 0x00, 0xFF }, rgba.AsSpan(i * 4, 4).ToArray());
    }

    [Fact]
    public void AstcReservedBlockModeDecodesToErrorColour()
    {
        var rgba = AstcDecoder.Decode(new byte[16], 4, 4, 4, 4, srgb: false); // mode 0 is reserved
        Assert.Equal(new byte[] { 255, 0, 255, 255 }, rgba.AsSpan(0, 4).ToArray());
    }

    [Fact]
    public void Bc1SolidBlock()
    {
        // c0 = c1 = pure red (RGB565 0xF800), all indices 0.
        var block = new byte[] { 0x00, 0xF8, 0x00, 0xF8, 0, 0, 0, 0 };
        var rgba = BcDecoder.Decode(block, 4, 4, BcDecoder.Kind.Bc1);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, rgba.AsSpan(60, 4).ToArray());
    }

    [Fact]
    public void BlockLinearPlacesElementsByGobLayout()
    {
        // One GOB-wide (64 bytes), 8-row surface of 1-byte elements, block height 1 GOB: dst[y*64+x] = src[GobOffset(x,y)].
        var src = new byte[512];
        for (var i = 0; i < src.Length; i++) src[i] = (byte)(i * 7);
        var dst = BlockLinear.Deswizzle(src, 64, 8, 1, 0);
        static int Gob(int x, int y) => (x % 16) + (y % 4) * 16 + ((x / 16) % 2) * 64 + ((y / 4) % 2) * 128 + ((x / 32) % 2) * 256;
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 64; x++)
                Assert.Equal(src[Gob(x, y)], dst[y * 64 + x]);
    }

    [Fact]
    public void SarcReadsNamedFiles()
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        // header (0x14), SFAT (0x0C + 16), SFNT (8 + names), data
        var name = "a/b.txt\0"u8.ToArray(); // 8 bytes, already 4-aligned
        var dataOffset = 0x14 + 0x0C + 16 + 8 + name.Length;
        w.Write("SARC"u8.ToArray()); w.Write((ushort)0x14); w.Write((byte)0xFF); w.Write((byte)0xFE);
        w.Write(0); w.Write(dataOffset); w.Write(0x0100); // file size (unused), data offset, version
        w.Write("SFAT"u8.ToArray()); w.Write((ushort)0x0C); w.Write((ushort)1); w.Write(0x65);
        w.Write(0u); w.Write(0x01000000u); w.Write(0); w.Write(5); // hash, attr (named, offset 0), start, end
        w.Write("SFNT"u8.ToArray()); w.Write((ushort)8); w.Write((ushort)0);
        w.Write(name);
        w.Write(Encoding.ASCII.GetBytes("hello"));
        var sarc = new Sarc(ms.ToArray());
        Assert.True(sarc.TryGet("a/b.txt", out var file));
        Assert.Equal("hello", Encoding.ASCII.GetString(file.Span));
    }
}
