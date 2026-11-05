using System.Buffers.Binary;
using System.Text;

namespace MKWSatellite.Formats.Byml;

/// <summary>
/// Minimal read-only BYML reader (v2+, little or big endian).
/// Produces a plain object tree: <see cref="BymlDict"/>, <see cref="BymlArray"/>, string, bool, int, uint,
/// long, ulong, float, double, byte[] or null.
/// TODO: hash-map node types (0x20/0x21) and other newer node types are not handled yet.
/// </summary>
public sealed class BymlReader
{
    private const byte TString = 0xA0, TBinary = 0xA1, TArray = 0xC0, TDict = 0xC1, TStringTable = 0xC2,
        TBool = 0xD0, TInt = 0xD1, TFloat = 0xD2, TUInt = 0xD3, TInt64 = 0xD4, TUInt64 = 0xD5, TDouble = 0xD6, TNull = 0xFF;

    private readonly byte[] _d;
    private readonly bool _le;
    private readonly string[] _keys;
    private readonly string[] _strings;

    public ushort Version { get; }
    public object? Root { get; }

    private BymlReader(byte[] data)
    {
        _d = data;
        _le = data[0] == (byte)'Y' && data[1] == (byte)'B';
        if (!_le && !(data[0] == (byte)'B' && data[1] == (byte)'Y'))
            throw new InvalidDataException("Not a BYML file (bad magic).");
        Version = U16(2);
        var keyTable = U32(4);
        var stringTable = U32(8);
        var root = U32(12);
        _keys = keyTable != 0 ? ReadStringTable(keyTable) : [];
        _strings = stringTable != 0 ? ReadStringTable(stringTable) : [];
        Root = root != 0 ? ReadContainer(root, _d[root]) : null;
    }

    /// <summary>Parses decompressed BYML bytes.</summary>
    public static BymlReader Parse(byte[] data) => new(data);

    private ushort U16(int o) => _le ? BinaryPrimitives.ReadUInt16LittleEndian(_d.AsSpan(o)) : BinaryPrimitives.ReadUInt16BigEndian(_d.AsSpan(o));
    private int U32(int o) => (int)(_le ? BinaryPrimitives.ReadUInt32LittleEndian(_d.AsSpan(o)) : BinaryPrimitives.ReadUInt32BigEndian(_d.AsSpan(o)));
    private uint UInt(int o) => _le ? BinaryPrimitives.ReadUInt32LittleEndian(_d.AsSpan(o)) : BinaryPrimitives.ReadUInt32BigEndian(_d.AsSpan(o));
    private ulong U64(int o) => _le ? BinaryPrimitives.ReadUInt64LittleEndian(_d.AsSpan(o)) : BinaryPrimitives.ReadUInt64BigEndian(_d.AsSpan(o));
    private int U24(int o) => _le ? _d[o] | _d[o + 1] << 8 | _d[o + 2] << 16 : _d[o] << 16 | _d[o + 1] << 8 | _d[o + 2];

    private string[] ReadStringTable(int o)
    {
        if (_d[o] != TStringTable) throw new InvalidDataException($"Expected string table at 0x{o:X}.");
        var n = U24(o + 1);
        var result = new string[n];
        for (var i = 0; i < n; i++)
        {
            var start = o + U32(o + 4 + 4 * i);
            var end = Array.IndexOf(_d, (byte)0, start);
            result[i] = Encoding.UTF8.GetString(_d, start, end - start);
        }
        return result;
    }

    private object? ReadValue(byte type, int valueOffset)
    {
        switch (type)
        {
            case TString: return _strings[U32(valueOffset)];
            case TBinary:
            {
                var at = U32(valueOffset);
                return _d.AsSpan(at + 4, U32(at)).ToArray();
            }
            case TArray or TDict: return ReadContainer(U32(valueOffset), type);
            case TBool: return UInt(valueOffset) != 0;
            case TInt: return (int)UInt(valueOffset);
            case TFloat: return BitConverter.Int32BitsToSingle((int)UInt(valueOffset));
            case TUInt: return UInt(valueOffset);
            case TInt64: return (long)U64(U32(valueOffset));
            case TUInt64: return U64(U32(valueOffset));
            case TDouble: return BitConverter.Int64BitsToDouble((long)U64(U32(valueOffset)));
            case TNull: return null;
            default: throw new NotSupportedException($"BYML node type 0x{type:X2} at 0x{valueOffset:X} is not supported yet.");
        }
    }

    private object ReadContainer(int o, byte expectedType)
    {
        var type = _d[o];
        if (type != expectedType) throw new InvalidDataException($"Node at 0x{o:X} is 0x{type:X2}, expected 0x{expectedType:X2}.");
        var n = U24(o + 1);
        if (type == TArray)
        {
            var values = o + 4 + ((n + 3) & ~3);
            var arr = new BymlArray(n);
            for (var i = 0; i < n; i++) arr.Add(ReadValue(_d[o + 4 + i], values + 4 * i));
            return arr;
        }
        if (type == TDict)
        {
            var dict = new BymlDict(n);
            for (var i = 0; i < n; i++)
            {
                var e = o + 4 + 8 * i;
                dict[_keys[U24(e)]] = ReadValue(_d[e + 3], e + 4);
            }
            return dict;
        }
        throw new NotSupportedException($"BYML container type 0x{type:X2} at 0x{o:X} is not supported yet.");
    }
}

/// <summary>A BYML dictionary node. BYML stores keys sorted, so original authoring order is not available.</summary>
public sealed class BymlDict(int capacity) : Dictionary<string, object?>(capacity, StringComparer.Ordinal)
{
    public T? Get<T>(string key) => TryGetValue(key, out var v) && v is T t ? t : default;
    public BymlDict? Dict(string key) => Get<BymlDict>(key);
    public BymlArray? Array(string key) => Get<BymlArray>(key);
}

/// <summary>A BYML array node.</summary>
public sealed class BymlArray(int capacity) : List<object?>(capacity);
