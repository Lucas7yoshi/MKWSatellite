using System.Buffers.Binary;
using System.Text;
using MKWSatellite.Formats.Phive;

namespace MKWSatellite.Formats.Tests;

public class HavokTagfileTests
{
    [Theory]
    [InlineData(new byte[] { 0x05 }, 5, 1)]
    [InlineData(new byte[] { 0x80, 0xA0 }, 0xA0, 2)]
    [InlineData(new byte[] { 0x81, 0x02 }, 0x102, 2)]
    [InlineData(new byte[] { 0xC1, 0x02, 0x03 }, 0x10203, 3)]
    [InlineData(new byte[] { 0xE1, 0x02, 0x03, 0x04 }, 0x1020304, 4)]
    public void VarIntLengthsFromTopBits(byte[] bytes, int value, int length)
    {
        var p = 0;
        Assert.Equal(value, HavokTagfile.VarInt(bytes, ref p));
        Assert.Equal(length, p);
    }

    private static byte[] Chunk(string tag, params byte[][] body)
    {
        var inner = body.SelectMany(b => b).ToArray();
        var d = new byte[8 + inner.Length];
        BinaryPrimitives.WriteUInt32BigEndian(d, (uint)d.Length | 0x40000000);
        Encoding.ASCII.GetBytes(tag).CopyTo(d, 4);
        inner.CopyTo(d, 8);
        return d;
    }

    /// <summary>A minimal tagfile: one type with one member, one ITEM pointing at the start of DATA.</summary>
    [Fact]
    public void ReadsTypesMembersAndItems()
    {
        var data = new byte[160];
        var item = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(item.AsSpan(12), 1); // type 1
        BinaryPrimitives.WriteUInt32LittleEndian(item.AsSpan(20), 1); // count 1, offset 0
        var file = Chunk("TAG0",
            Chunk("SDKV", Encoding.ASCII.GetBytes("20230200")),
            Chunk("DATA", data),
            Chunk("TYPE",
                Chunk("TST1", Encoding.ASCII.GetBytes("hknpMeshShape\0")),
                Chunk("TNA1", [2, 0, 0]),
                Chunk("FST1", Encoding.ASCII.GetBytes("vertexConversionUtil\0")),
                // type 1: parent 0, options size|members, size 160 align 16, 1 member (name 0, flags 0, offset 0x50, type 0)
                Chunk("TBDY", [1, 0, 0x28, 0x80, 160, 16, 1, 0, 0, 0x50, 0])),
            Chunk("INDX", Chunk("ITEM", item)));

        var tf = new HavokTagfile(file, 0);
        var t = tf.Type("hknpMeshShape");
        Assert.NotNull(t);
        Assert.Equal(160, t!.Size);
        Assert.Equal(0x50, t.Offset("vertexConversionUtil"));
        Assert.Equal(-1, t.Offset("missing"));
        Assert.Same(t, tf.TypeAt(tf.DataStart));
        Assert.Equal([tf.DataStart], tf.ObjectsOfType("hknpMeshShape"));
    }
}
