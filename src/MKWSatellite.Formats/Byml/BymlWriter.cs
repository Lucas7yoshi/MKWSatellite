using System.Buffers.Binary;
using System.Text;

namespace MKWSatellite.Formats.Byml;

/// <summary>
/// Writes the object tree produced by <see cref="BymlReader"/> back to little-endian BYML (v2+). Value types are kept
/// as the reader returned them (int stays int, uint stays uint, float stays float), so an unchanged tree reads back
/// equal. The byte layout is not Nintendo's exactly: containers are written depth-first after the string tables,
/// 64-bit values and binary blobs after the containers, and identical subtrees are not shared.
/// </summary>
public static class BymlWriter
{
    private const byte TString = 0xA0, TBinary = 0xA1, TArray = 0xC0, TDict = 0xC1, THash32 = 0x20, THash64 = 0x21, TStringTable = 0xC2,
        TBool = 0xD0, TInt = 0xD1, TFloat = 0xD2, TUInt = 0xD3, TInt64 = 0xD4, TUInt64 = 0xD5, TDouble = 0xD6, TNull = 0xFF;

    /// <summary>Serialises <paramref name="root"/> (a <see cref="BymlDict"/>, <see cref="BymlArray"/> or hash map).</summary>
    public static byte[] Write(object root, ushort version = 7)
    {
        if (root is not (BymlDict or BymlArray or BymlHashMap)) throw new ArgumentException("BYML root must be a container.", nameof(root));
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        var strings = new SortedSet<string>(StringComparer.Ordinal);
        Collect(root, keys, strings);
        var keyList = keys.ToArray();
        var stringList = strings.ToArray();
        var keyIndex = keyList.Select((k, i) => (k, i)).ToDictionary(t => t.k, t => t.i, StringComparer.Ordinal);
        var stringIndex = stringList.Select((s, i) => (s, i)).ToDictionary(t => t.s, t => t.i, StringComparer.Ordinal);

        var w = new Writer();
        w.Bytes("YB"u8);
        w.U16(version);
        var keyTableField = w.Reserve();
        var stringTableField = w.Reserve();
        var rootField = w.Reserve();

        if (keyList.Length > 0) { w.Patch(keyTableField, w.Position); WriteStringTable(w, keyList); }
        if (stringList.Length > 0) { w.Patch(stringTableField, w.Position); WriteStringTable(w, stringList); }

        // Containers depth-first; scalar payloads that live outside the node (64-bit, binary) are queued and written last.
        var deferred = new List<(int Field, object Value)>();
        w.Align(4);
        w.Patch(rootField, w.Position);
        WriteContainer(w, root, keyIndex, stringIndex, deferred);
        foreach (var (field, value) in deferred)
        {
            switch (value)
            {
                case byte[] bin:
                    w.Align(4);
                    w.Patch(field, w.Position);
                    w.U32((uint)bin.Length);
                    w.Bytes(bin);
                    break;
                default:
                    w.Align(8);
                    w.Patch(field, w.Position);
                    w.U64(value switch
                    {
                        long l => (ulong)l,
                        ulong u => u,
                        double d => (ulong)BitConverter.DoubleToInt64Bits(d),
                        _ => throw new InvalidOperationException(),
                    });
                    break;
            }
        }
        w.Align(4);
        return w.ToArray();
    }

    /// <summary>Pads to the next power of two with zeros, like the shipped Banc files (decompressed size).</summary>
    public static byte[] PadToPowerOfTwo(byte[] data)
    {
        var size = 1;
        while (size < data.Length) size <<= 1;
        if (size == data.Length) return data;
        var padded = new byte[size];
        data.CopyTo(padded, 0);
        return padded;
    }

    private static void Collect(object? node, SortedSet<string> keys, SortedSet<string> strings)
    {
        switch (node)
        {
            case string s: strings.Add(s); break;
            case BymlDict d:
                foreach (var (k, v) in d) { keys.Add(k); Collect(v, keys, strings); }
                break;
            case BymlArray a:
                foreach (var v in a) Collect(v, keys, strings);
                break;
            case BymlHashMap m:
                foreach (var v in m.Values) Collect(v, keys, strings);
                break;
        }
    }

    private static void WriteStringTable(Writer w, string[] list)
    {
        var start = w.Position;
        w.U8(TStringTable);
        w.U24(list.Length);
        var offsetsAt = w.Position;
        for (var i = 0; i <= list.Length; i++) w.U32(0);
        for (var i = 0; i < list.Length; i++)
        {
            w.PatchAt(offsetsAt + 4 * i, (uint)(w.Position - start));
            w.Bytes(Encoding.UTF8.GetBytes(list[i]));
            w.U8(0);
        }
        w.PatchAt(offsetsAt + 4 * list.Length, (uint)(w.Position - start)); // end offset
        w.Align(4);
    }

    private static byte TypeOf(object? v) => v switch
    {
        null => TNull,
        string => TString,
        byte[] => TBinary,
        BymlArray => TArray,
        BymlDict => TDict,
        BymlHashMap m => m.Keys.Any(k => k > uint.MaxValue) ? THash64 : THash32,
        bool => TBool,
        int => TInt,
        float => TFloat,
        uint => TUInt,
        long => TInt64,
        ulong => TUInt64,
        double => TDouble,
        _ => throw new NotSupportedException($"Cannot write {v.GetType().Name} to BYML."),
    };

    private static void WriteContainer(Writer w, object node, Dictionary<string, int> keyIndex, Dictionary<string, int> stringIndex,
        List<(int, object)> deferred)
    {
        // Children containers are written after this node; their offset fields are patched then.
        var children = new List<(int Field, object Node)>();
        void Value(object? v)
        {
            var field = w.Position;
            switch (v)
            {
                case null: w.U32(0); break;
                case string s: w.U32((uint)stringIndex[s]); break;
                case bool b: w.U32(b ? 1u : 0u); break;
                case int i: w.U32((uint)i); break;
                case uint u: w.U32(u); break;
                case float f: w.U32((uint)BitConverter.SingleToInt32Bits(f)); break;
                case BymlArray or BymlDict or BymlHashMap: w.U32(0); children.Add((field, v)); break;
                case byte[] or long or ulong or double: w.U32(0); deferred.Add((field, v)); break;
                default: throw new NotSupportedException($"Cannot write {v.GetType().Name} to BYML.");
            }
        }

        switch (node)
        {
            case BymlArray a:
                w.U8(TArray);
                w.U24(a.Count);
                foreach (var v in a) w.U8(TypeOf(v));
                w.Align(4);
                foreach (var v in a) Value(v);
                break;
            case BymlDict d:
                w.U8(TDict);
                w.U24(d.Count);
                foreach (var (k, v) in d.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    w.U24(keyIndex[k]);
                    w.U8(TypeOf(v));
                    Value(v);
                }
                break;
            case BymlHashMap m:
                var wide = TypeOf(m) == THash64;
                w.U8(wide ? THash64 : THash32);
                w.U24(m.Count);
                var sorted = m.OrderBy(kv => kv.Key).ToList();
                foreach (var (k, v) in sorted)
                {
                    if (wide) w.U64(k); else w.U32((uint)k);
                    Value(v);
                }
                foreach (var (_, v) in sorted) w.U8(TypeOf(v));
                w.Align(4);
                break;
        }

        foreach (var (field, child) in children)
        {
            w.Align(4);
            w.Patch(field, w.Position);
            WriteContainer(w, child, keyIndex, stringIndex, deferred);
        }
    }

    private sealed class Writer
    {
        private readonly MemoryStream _s = new();
        private readonly byte[] _b = new byte[8];

        public int Position => (int)_s.Position;
        public void U8(byte v) => _s.WriteByte(v);
        public void U16(ushort v) { BinaryPrimitives.WriteUInt16LittleEndian(_b, v); _s.Write(_b, 0, 2); }
        public void U24(int v) { _s.WriteByte((byte)v); _s.WriteByte((byte)(v >> 8)); _s.WriteByte((byte)(v >> 16)); }
        public void U32(uint v) { BinaryPrimitives.WriteUInt32LittleEndian(_b, v); _s.Write(_b, 0, 4); }
        public void U64(ulong v) { BinaryPrimitives.WriteUInt64LittleEndian(_b, v); _s.Write(_b, 0, 8); }
        public void Bytes(ReadOnlySpan<byte> v) => _s.Write(v);
        public int Reserve() { var p = Position; U32(0); return p; }
        public void Align(int a) { while (_s.Position % a != 0) _s.WriteByte(0); }
        public void Patch(int field, int value) => PatchAt(field, (uint)value);

        public void PatchAt(int at, uint value)
        {
            var end = _s.Position;
            _s.Position = at;
            U32(value);
            _s.Position = end;
        }

        public byte[] ToArray() => _s.ToArray();
    }
}
