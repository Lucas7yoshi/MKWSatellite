using System.Buffers.Binary;
using System.Text;

namespace MKWSatellite.Formats.Phive;

/// <summary>A type from a Havok tagfile's reflection data (TNA1/TBDY).</summary>
public sealed class HavokType(int index)
{
    public int Index { get; } = index;
    public string Name { get; internal set; } = "";
    public List<(string Name, object? Value)> Parameters { get; } = [];
    public HavokType? Parent { get; internal set; }
    public int Format { get; internal set; }
    public HavokType? SubType { get; internal set; }
    public int Size { get; internal set; }
    public List<(string Name, int Flags, int Offset, HavokType? Type)> Members { get; } = [];

    /// <summary>Offset of a field, searching base types too; -1 if absent.</summary>
    public int Offset(string field)
    {
        for (var t = this; t is not null; t = t.Parent)
            foreach (var m in t.Members)
                if (m.Name == field) return m.Offset;
        return -1;
    }

    public override string ToString() => Name;
}

/// <summary>An object listed in the tagfile's ITEM table: its type and position in the file.</summary>
public readonly record struct HavokItem(HavokType? Type, int Flags, int Offset, int Count);

/// <summary>
/// Havok "TAG0" tagfile (SDK 2023.2 in Mario Kart World), as embedded in Phive files. Objects are stored in place in
/// the DATA chunk with relative pointers (<c>hkRelPtr</c>, <c>hkRelArrayView</c>), so after reading the reflection
/// (type names, field offsets) and the ITEM table, shapes can be read straight from the bytes. See
/// docs/formats/collision.md.
/// </summary>
public sealed class HavokTagfile
{
    public byte[] Data { get; }
    public int DataStart { get; }
    public IReadOnlyList<HavokType> Types { get; }
    public IReadOnlyList<HavokItem> Items { get; }
    private readonly Dictionary<int, HavokType> _typeAt = [];

    public HavokTagfile(byte[] d, int start)
    {
        Data = d;
        var (tag0, tag0End) = FindChunk(d, start, d.Length, "TAG0");
        var sections = new Dictionary<string, (int Start, int End)>();
        void Walk(int a, int e)
        {
            foreach (var (tag, flags, s, end) in Chunks(d, a, e))
            {
                sections[tag] = (s, end);
                if (tag is "TYPE" or "INDX") Walk(s, end);
            }
        }
        Walk(tag0, tag0End);
        DataStart = sections["DATA"].Start;
        var typeStrings = Strings(d, sections["TST1"]);
        var fieldStrings = Strings(d, sections["FST1"]);

        var (p, end2) = sections["TNA1"];
        var count = VarInt(d, ref p);
        var types = new HavokType[count];
        for (var i = 1; i < count; i++) types[i] = new HavokType(i);
        for (var i = 1; i < count; i++)
        {
            var t = types[i];
            t.Name = typeStrings[VarInt(d, ref p)];
            var np = VarInt(d, ref p);
            for (var k = 0; k < np; k++)
            {
                var name = typeStrings[VarInt(d, ref p)];
                var v = VarInt(d, ref p);
                t.Parameters.Add((name, name.StartsWith('t') ? (v > 0 ? types[v] : null) : v));
            }
        }

        (p, var end) = sections["TBDY"];
        while (p < end)
        {
            var ti = VarInt(d, ref p);
            if (ti == 0) continue;
            var t = types[ti];
            var parent = VarInt(d, ref p);
            t.Parent = parent > 0 ? types[parent] : null;
            var opt = VarInt(d, ref p);
            if ((opt & 1) != 0) t.Format = VarInt(d, ref p);
            if ((opt & 2) != 0) { var s = VarInt(d, ref p); t.SubType = s > 0 ? types[s] : null; }
            if ((opt & 4) != 0) VarInt(d, ref p); // version
            if ((opt & 8) != 0) { t.Size = VarInt(d, ref p); VarInt(d, ref p); } // size, alignment
            if ((opt & 0x10) != 0) VarInt(d, ref p); // flags
            if ((opt & 0x20) != 0)
            {
                var n = VarInt(d, ref p);
                for (var k = 0; k < n; k++)
                {
                    var name = fieldStrings[VarInt(d, ref p)];
                    var flags = VarInt(d, ref p);
                    if ((flags & 0x80) != 0) VarInt(d, ref p); // extra value on some members (seen in world compounds)
                    var off = VarInt(d, ref p);
                    var ty = VarInt(d, ref p);
                    t.Members.Add((name, flags, off, ty > 0 ? types[ty] : null));
                }
            }
            if ((opt & 0x40) != 0)
            {
                var n = VarInt(d, ref p);
                for (var k = 0; k < n; k++) { VarInt(d, ref p); VarInt(d, ref p); }
            }
            if ((opt & 0x80) != 0) VarInt(d, ref p);
            if ((opt & ~0xFF) != 0) throw new InvalidDataException($"Havok type body option {opt:X}");
        }
        Types = types;

        var items = new List<HavokItem>();
        (p, end) = sections["ITEM"];
        for (; p + 12 <= end; p += 12)
        {
            var f = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p));
            var off = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p + 4));
            var cnt = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p + 8));
            var ti = (int)(f & 0xFFFFFF);
            var item = new HavokItem(ti > 0 ? types[ti] : null, (int)(f >> 24), DataStart + off, cnt);
            items.Add(item);
            if (item.Type is { } it && cnt > 0) _typeAt.TryAdd(item.Offset, it);
        }
        Items = items;
    }

    /// <summary>The type of the object stored at an absolute file offset, if it is an ITEM.</summary>
    public HavokType? TypeAt(int offset) => _typeAt.GetValueOrDefault(offset);

    public HavokType? Type(string name) => Types.FirstOrDefault(t => t?.Name == name);

    public IEnumerable<int> ObjectsOfType(string name) =>
        Items.Where(i => i.Type?.Name == name && i.Count > 0).Select(i => i.Offset);

    private static (int Start, int End) FindChunk(byte[] d, int a, int e, string tag)
    {
        foreach (var (t, _, s, end) in Chunks(d, a, e))
            if (t == tag) return (s, end);
        throw new InvalidDataException($"Havok tagfile: no {tag} chunk at 0x{a:X}");
    }

    private static IEnumerable<(string Tag, int Flags, int Start, int End)> Chunks(byte[] d, int off, int end)
    {
        while (off + 8 <= end)
        {
            var h = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(off));
            var size = (int)(h & 0x3FFFFFFF);
            if (size < 8) yield break;
            yield return (Encoding.ASCII.GetString(d, off + 4, 4), (int)(h >> 30), off + 8, off + size);
            off += size;
        }
    }

    private static string[] Strings(byte[] d, (int Start, int End) s) =>
        Encoding.Latin1.GetString(d, s.Start, s.End - s.Start).Split('\0');

    /// <summary>Havok's variable-length integer: the top bits of the first byte give the length.</summary>
    public static int VarInt(byte[] d, ref int p)
    {
        int b = d[p];
        if ((b & 0x80) == 0) { p += 1; return b; }
        if ((b & 0xC0) == 0x80) { var v = ((b & 0x3F) << 8) | d[p + 1]; p += 2; return v; }
        if ((b & 0xE0) == 0xC0) { var v = ((b & 0x1F) << 16) | (d[p + 1] << 8) | d[p + 2]; p += 3; return v; }
        if ((b & 0xF8) == 0xE0) { var v = ((b & 0x07) << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3]; p += 4; return v; }
        throw new InvalidDataException($"Havok varint 0x{b:X2} at 0x{p:X}");
    }
}
