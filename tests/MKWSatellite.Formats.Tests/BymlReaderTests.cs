using System.Text;
using MKWSatellite.Formats.Byml;

namespace MKWSatellite.Formats.Tests;

public class BymlReaderTests
{
    /// <summary>Builds a tiny little-endian BYML v7: { "a": 5 (int), "b": "hi", "c": [1.5f, true] }.</summary>
    private static byte[] BuildSample()
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        void U24(int v) { w.Write((byte)v); w.Write((byte)(v >> 8)); w.Write((byte)(v >> 16)); }
        void Align() { while (ms.Position % 4 != 0) w.Write((byte)0); }
        void StringTable(params string[] s)
        {
            w.Write((byte)0xC2); U24(s.Length);
            var offs = new List<int>();
            var pos = 4 + 4 * (s.Length + 1);
            foreach (var x in s) { offs.Add(pos); pos += Encoding.UTF8.GetByteCount(x) + 1; }
            offs.Add(pos);
            foreach (var o in offs) w.Write(o);
            foreach (var x in s) { w.Write(Encoding.UTF8.GetBytes(x)); w.Write((byte)0); }
            Align();
        }

        w.Write("YB"u8.ToArray()); w.Write((ushort)7);
        w.Write(0x10); w.Write(0); w.Write(0); // key table, string table, root (patched below)
        StringTable("a", "b", "c");
        var strTable = (int)ms.Position;
        StringTable("hi");
        var array = (int)ms.Position;
        w.Write((byte)0xC0); U24(2); w.Write((byte)0xD2); w.Write((byte)0xD0); Align();
        w.Write(BitConverter.SingleToInt32Bits(1.5f)); w.Write(1);
        var dict = (int)ms.Position;
        w.Write((byte)0xC1); U24(3);
        U24(0); w.Write((byte)0xD1); w.Write(5);
        U24(1); w.Write((byte)0xA0); w.Write(0);
        U24(2); w.Write((byte)0xC0); w.Write(array);
        ms.Position = 8; w.Write(strTable); w.Write(dict);
        return ms.ToArray();
    }

    [Fact]
    public void ParsesDictArrayAndScalars()
    {
        var byml = BymlReader.Parse(BuildSample());
        Assert.Equal(7, byml.Version);
        var root = Assert.IsType<BymlDict>(byml.Root);
        Assert.Equal(5, root.Get<int>("a"));
        Assert.Equal("hi", root.Get<string>("b"));
        var c = root.Array("c")!;
        Assert.Equal(1.5f, c[0]);
        Assert.Equal(true, c[1]);
    }

    [Fact]
    public void RejectsBadMagic() =>
        Assert.Throws<InvalidDataException>(() => BymlReader.Parse(new byte[16]));
}
