using System.Buffers.Binary;
using System.Text;

namespace MKWSatellite.Formats.Text;

/// <summary>
/// Minimal MSBT (<c>MsgStdBn</c>) reader: label → text from the LBL1 and TXT2 sections. Text is UTF-16; inline control
/// tags (<c>0x0E</c> group/type/params, <c>0x0F</c> closing tags) are dropped, so only the plain text is kept.
/// </summary>
public static class Msbt
{
    public static Dictionary<string, string> Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x20 || !data[..8].SequenceEqual("MsgStdBn"u8)) throw new InvalidDataException("not an MSBT file");
        var little = data[8] == 0xFF && data[9] == 0xFE;
        var encoding = data[0x0C]; // 0 = UTF-8, 1 = UTF-16, 2 = UTF-32
        if (encoding != 1) throw new InvalidDataException($"MSBT encoding {encoding} not supported");
        uint U32(ReadOnlySpan<byte> s) => little ? BinaryPrimitives.ReadUInt32LittleEndian(s) : BinaryPrimitives.ReadUInt32BigEndian(s);
        ushort U16(ReadOnlySpan<byte> s) => little ? BinaryPrimitives.ReadUInt16LittleEndian(s) : BinaryPrimitives.ReadUInt16BigEndian(s);

        var sections = U16(data[0x0E..]);
        var labels = new Dictionary<string, int>(StringComparer.Ordinal);
        var texts = new List<string>();
        var off = 0x20;
        for (var n = 0; n < sections && off + 16 <= data.Length; n++)
        {
            var magic = data.Slice(off, 4);
            var size = (int)U32(data[(off + 4)..]);
            var body = data.Slice(off + 16, size);
            if (magic.SequenceEqual("LBL1"u8))
            {
                var groups = (int)U32(body);
                for (var g = 0; g < groups; g++)
                {
                    var count = (int)U32(body[(4 + g * 8)..]);
                    var p = (int)U32(body[(8 + g * 8)..]);
                    for (var i = 0; i < count; i++)
                    {
                        int len = body[p];
                        var name = Encoding.ASCII.GetString(body.Slice(p + 1, len));
                        labels[name] = (int)U32(body[(p + 1 + len)..]);
                        p += 1 + len + 4;
                    }
                }
            }
            else if (magic.SequenceEqual("TXT2"u8))
            {
                var count = (int)U32(body);
                for (var i = 0; i < count; i++)
                {
                    var start = (int)U32(body[(4 + i * 4)..]);
                    var end = i + 1 < count ? (int)U32(body[(8 + i * 4)..]) : size;
                    texts.Add(Decode(body[start..end], U16));
                }
            }
            off += 16 + ((size + 15) & ~15);
        }
        return labels.Where(kv => kv.Value < texts.Count).ToDictionary(kv => kv.Key, kv => texts[kv.Value], StringComparer.Ordinal);
    }

    private delegate ushort ReadU16(ReadOnlySpan<byte> s);

    private static string Decode(ReadOnlySpan<byte> s, ReadU16 u16)
    {
        var sb = new StringBuilder(s.Length / 2);
        for (var i = 0; i + 1 < s.Length; )
        {
            var c = u16(s[i..]);
            if (c == 0) break;
            if (c == 0x0E && i + 8 <= s.Length) { i += 8 + u16(s[(i + 6)..]); continue; } // tag: group, type, param size, params
            if (c == 0x0F && i + 6 <= s.Length) { i += 6; continue; } // closing tag: group, type
            sb.Append((char)c);
            i += 2;
        }
        return sb.ToString();
    }
}
