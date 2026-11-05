using System.Buffers.Binary;
using System.Text;

namespace MKWSatellite.Formats.Archives;

/// <summary>
/// SARC archive reader (little-endian, as in Mario Kart World). Header → SFAT (file entries: name hash, name offset
/// in 4-byte units in the low 24 bits of the attribute word, data start/end) → SFNT (names) → data.
/// </summary>
public sealed class Sarc
{
    private readonly byte[] _data;
    private readonly Dictionary<string, (int Start, int End)> _files = new(StringComparer.Ordinal);

    public Sarc(byte[] data)
    {
        if (data.Length < 0x14 || Encoding.ASCII.GetString(data, 0, 4) != "SARC") throw new InvalidDataException("Not a SARC archive");
        if (data[6] != 0xFF || data[7] != 0xFE) throw new NotSupportedException("Big-endian SARC not supported");
        _data = data;
        var d = data.AsSpan();
        var headerSize = BinaryPrimitives.ReadUInt16LittleEndian(d[4..]);
        var dataOffset = BinaryPrimitives.ReadInt32LittleEndian(d[0x0C..]);
        var sfat = headerSize;
        if (Encoding.ASCII.GetString(data, sfat, 4) != "SFAT") throw new InvalidDataException("Expected SFAT");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(d[(sfat + 6)..]);
        var sfnt = sfat + 12 + count * 16;
        if (Encoding.ASCII.GetString(data, sfnt, 4) != "SFNT") throw new InvalidDataException("Expected SFNT");
        var names = sfnt + 8;
        for (var i = 0; i < count; i++)
        {
            var e = sfat + 12 + i * 16;
            var attr = BinaryPrimitives.ReadUInt32LittleEndian(d[(e + 4)..]);
            var start = BinaryPrimitives.ReadInt32LittleEndian(d[(e + 8)..]);
            var end = BinaryPrimitives.ReadInt32LittleEndian(d[(e + 12)..]);
            if ((attr >> 24) == 0) continue; // unnamed entry (high byte = name present / collision index)
            var nameStart = names + (int)(attr & 0xFFFFFF) * 4;
            var nameEnd = Array.IndexOf(data, (byte)0, nameStart);
            _files[Encoding.UTF8.GetString(data, nameStart, nameEnd - nameStart)] = (dataOffset + start, dataOffset + end);
        }
    }

    public IEnumerable<string> FileNames => _files.Keys;

    public int Count => _files.Count;

    public bool TryGet(string name, out ReadOnlyMemory<byte> file)
    {
        if (_files.TryGetValue(name, out var r)) { file = _data.AsMemory(r.Start, r.End - r.Start); return true; }
        file = default;
        return false;
    }
}
