using System.Buffers.Binary;

namespace MKWSatellite.Formats.Textures;

/// <summary>
/// ASTC LDR decoder (2D, all block sizes), written from the Khronos Data Format Specification's ASTC chapter:
/// block modes, void-extent blocks, partitions with the spec's hash, integer sequence encoding (trits/quints),
/// colour/weight unquantisation, LDR endpoint modes, weight infill and dual planes. HDR blocks/modes decode to
/// magenta, which is also the spec's error colour. Mario Kart World's albedo textures are almost all ASTC.
/// </summary>
public static class AstcDecoder
{
    private static readonly byte[] Error = [255, 0, 255, 255];

    public static byte[] Decode(byte[] src, int width, int height, int blockW, int blockH, bool srgb)
    {
        var bx = (width + blockW - 1) / blockW;
        var by = (height + blockH - 1) / blockH;
        var dst = new byte[width * height * 4];
        Parallel.For(0, by, y =>
        {
            Span<byte> texels = stackalloc byte[blockW * blockH * 4];
            for (var x = 0; x < bx; x++)
            {
                var off = (y * bx + x) * 16;
                if (off + 16 > src.Length) return;
                DecodeBlock(src.AsSpan(off, 16), blockW, blockH, srgb, texels);
                for (var ty = 0; ty < blockH; ty++)
                {
                    var py = y * blockH + ty;
                    if (py >= height) break;
                    var px0 = x * blockW;
                    var n = Math.Min(blockW, width - px0);
                    texels.Slice(ty * blockW * 4, n * 4).CopyTo(dst.AsSpan((py * width + px0) * 4, n * 4));
                }
            }
        });
        return dst;
    }

    /// <summary>Decodes one 16-byte block into <paramref name="rgba"/> (blockW × blockH × 4 bytes).</summary>
    public static void DecodeBlock(ReadOnlySpan<byte> block, int blockW, int blockH, bool srgb, Span<byte> rgba)
    {
        if (!TryDecodeBlock(block, blockW, blockH, srgb, rgba))
            for (var i = 0; i < blockW * blockH; i++) Error.CopyTo(rgba[(i * 4)..]);
    }

    private static bool TryDecodeBlock(ReadOnlySpan<byte> block, int blockW, int blockH, bool srgb, Span<byte> rgba)
    {
        var lo = BinaryPrimitives.ReadUInt64LittleEndian(block);
        var hi = BinaryPrimitives.ReadUInt64LittleEndian(block[8..]);
        var bits = new UInt128(hi, lo);
        var mode = (int)(lo & 0x7FF);
        var texelCount = blockW * blockH;

        // Void extent: one constant colour (UNORM16 per channel in bits 64..127).
        if ((mode & 0x1FF) == 0x1FC)
        {
            if ((mode & 0x200) != 0) return false; // HDR
            Span<byte> c = [(byte)(hi >> 8), (byte)(hi >> 24), (byte)(hi >> 40), (byte)(hi >> 56)];
            for (var i = 0; i < texelCount; i++) c.CopyTo(rgba[(i * 4)..]);
            return true;
        }

        if (!DecodeBlockMode(mode, out var gw, out var gh, out var dualPlane, out var weightLevels)) return false;
        if (gw > blockW || gh > blockH) return false;
        var partitions = (int)((lo >> 11) & 3) + 1;
        var weightCount = gw * gh * (dualPlane ? 2 : 1);
        if (weightCount > 64) return false;
        var weightBits = IseBitCount(weightLevels, weightCount);
        if (weightBits is < 24 or > 96) return false;
        if (partitions == 4 && dualPlane) return false;

        // Colour endpoint modes.
        Span<int> cem = stackalloc int[4];
        var belowWeights = 128 - weightBits;
        var extraCemBits = 0;
        var partitionSeed = 0;
        int colorStart;
        if (partitions == 1)
        {
            cem[0] = Bits(bits, 13, 4);
            colorStart = 17;
        }
        else
        {
            partitionSeed = Bits(bits, 13, 10);
            colorStart = 29;
            var selector = Bits(bits, 23, 2);
            if (selector == 0)
            {
                var c = Bits(bits, 25, 4);
                for (var i = 0; i < partitions; i++) cem[i] = c;
            }
            else
            {
                extraCemBits = 3 * partitions - 4;
                var enc = Bits(bits, 23, 6) | (Bits(bits, belowWeights - extraCemBits, extraCemBits) << 6);
                var baseClass = (enc & 3) - 1;
                var bitPos = 2;
                Span<int> cls = stackalloc int[4];
                for (var i = 0; i < partitions; i++) cls[i] = (enc >> bitPos++) & 1;
                for (var i = 0; i < partitions; i++) { cem[i] = ((baseClass + cls[i]) << 2) | ((enc >> bitPos) & 3); bitPos += 2; }
            }
        }
        var colorEnd = belowWeights - extraCemBits - (dualPlane ? 2 : 0);
        var ccs = dualPlane ? Bits(bits, colorEnd, 2) : -1;

        var colorValueCount = 0;
        for (var i = 0; i < partitions; i++)
        {
            if (cem[i] is 2 or 3 or 7 or 11 or 14 or 15) return false; // HDR endpoint modes
            colorValueCount += 2 * ((cem[i] >> 2) + 1);
        }
        if (colorValueCount > 18) return false;
        var colorBitsAvailable = colorEnd - colorStart;
        var colorLevels = 0;
        for (var i = ColorLevels.Length - 1; i >= 0; i--)
            if (IseBitCount(ColorLevels[i], colorValueCount) <= colorBitsAvailable) { colorLevels = ColorLevels[i]; break; }
        if (colorLevels < 6) return false;

        // Colour values → endpoints.
        Span<int> colorValues = stackalloc int[18];
        DecodeIse(bits, colorStart, colorValueCount, colorLevels, colorValues);
        for (var i = 0; i < colorValueCount; i++) colorValues[i] = UnquantizeColor(colorValues[i], colorLevels);
        Span<int> endpoints = stackalloc int[4 * 8]; // per partition: e0 RGBA, e1 RGBA
        var vi = 0;
        for (var p = 0; p < partitions; p++)
        {
            var n = 2 * ((cem[p] >> 2) + 1);
            DecodeEndpoints(cem[p], colorValues.Slice(vi, n), endpoints.Slice(p * 8, 8));
            vi += n;
        }

        // Weights: stored bit-reversed from the top of the block.
        var reversed = Reverse(bits);
        Span<int> weights = stackalloc int[64];
        DecodeIse(reversed, 0, weightCount, weightLevels, weights);
        for (var i = 0; i < weightCount; i++) weights[i] = UnquantizeWeight(weights[i], weightLevels);

        var planes = dualPlane ? 2 : 1;
        var ds = (1024 + blockW / 2) / Math.Max(1, blockW - 1);
        var dt = (1024 + blockH / 2) / Math.Max(1, blockH - 1);
        var smallBlock = texelCount < 31;
        Span<int> texelWeight = stackalloc int[2];
        for (var t = 0; t < blockH; t++)
        for (var s = 0; s < blockW; s++)
        {
            for (var plane = 0; plane < planes; plane++)
            {
                if (gw == blockW && gh == blockH)
                {
                    texelWeight[plane] = weights[(t * gw + s) * planes + plane];
                    continue;
                }
                var gs = (ds * s * (gw - 1) + 32) >> 6;
                var gt = (dt * t * (gh - 1) + 32) >> 6;
                var js = gs >> 4; var fs = gs & 0xF;
                var jt = gt >> 4; var ft = gt & 0xF;
                var w11 = (fs * ft + 8) >> 4;
                var w10 = ft - w11;
                var w01 = fs - w11;
                var w00 = 16 - fs - ft + w11;
                texelWeight[plane] = (GridWeight(weights, js, jt, gw, gh, planes, plane) * w00
                    + GridWeight(weights, js + 1, jt, gw, gh, planes, plane) * w01
                    + GridWeight(weights, js, jt + 1, gw, gh, planes, plane) * w10
                    + GridWeight(weights, js + 1, jt + 1, gw, gh, planes, plane) * w11 + 8) >> 4;
            }

            var part = partitions == 1 ? 0 : SelectPartition(partitionSeed, s, t, 0, partitions, smallBlock);
            var e = endpoints.Slice(part * 8, 8);
            var o = (t * blockW + s) * 4;
            for (var c = 0; c < 4; c++)
            {
                var w = c == ccs ? texelWeight[1] : texelWeight[0];
                int c0 = e[c], c1 = e[4 + c];
                c0 = srgb ? (c0 << 8) | 0x80 : (c0 << 8) | c0;
                c1 = srgb ? (c1 << 8) | 0x80 : (c1 << 8) | c1;
                var v = (c0 * (64 - w) + c1 * w + 32) >> 6;
                rgba[o + c] = (byte)(v >> 8);
            }
        }
        return true;
    }

    private static int GridWeight(ReadOnlySpan<int> weights, int x, int y, int gw, int gh, int planes, int plane) =>
        weights[(Math.Min(y, gh - 1) * gw + Math.Min(x, gw - 1)) * planes + plane];

    // ---- block mode ----

    private static bool DecodeBlockMode(int mode, out int w, out int h, out bool dual, out int levels)
    {
        w = h = levels = 0;
        dual = false;
        int r, hp;
        if ((mode & 3) != 0)
        {
            r = ((mode >> 4) & 1) | ((mode & 3) << 1);
            var a = (mode >> 5) & 3;
            var b = (mode >> 7) & 3;
            switch ((mode >> 2) & 3)
            {
                case 0: w = b + 4; h = a + 2; break;
                case 1: w = b + 8; h = a + 2; break;
                case 2: w = a + 2; h = b + 8; break;
                default:
                    if ((mode & 0x100) == 0) { w = a + 2; h = (b & 1) + 6; }
                    else { w = (b & 1) + 2; h = a + 2; }
                    break;
            }
            hp = (mode >> 9) & 1;
            dual = ((mode >> 10) & 1) != 0;
        }
        else
        {
            if ((mode & 0xF) == 0) return false; // reserved
            r = ((mode >> 4) & 1) | (((mode >> 2) & 3) << 1);
            var a = (mode >> 5) & 3;
            hp = (mode >> 9) & 1;
            dual = ((mode >> 10) & 1) != 0;
            switch ((mode >> 7) & 3)
            {
                case 0: w = 12; h = a + 2; break;
                case 1: w = a + 2; h = 12; break;
                case 2:
                    w = a + 6; h = ((mode >> 9) & 3) + 6;
                    hp = 0; dual = false;
                    break;
                default:
                    if (a == 0) { w = 6; h = 10; }
                    else if (a == 1) { w = 10; h = 6; }
                    else return false;
                    break;
            }
        }
        if (r < 2) return false;
        levels = hp == 0
            ? r switch { 2 => 2, 3 => 3, 4 => 4, 5 => 5, 6 => 6, _ => 8 }
            : r switch { 2 => 10, 3 => 12, 4 => 16, 5 => 20, 6 => 24, _ => 32 };
        return true;
    }

    // ---- integer sequence encoding ----

    private static readonly int[] ColorLevels = [2, 3, 4, 5, 6, 8, 10, 12, 16, 20, 24, 32, 40, 48, 64, 80, 96, 128, 160, 192, 256];

    /// <summary>(bits per value, 1 = trit / 2 = quint / 0 = plain) for a number of quantisation levels.</summary>
    private static (int Bits, int Kind) Encoding(int levels) => levels switch
    {
        2 => (1, 0), 3 => (0, 1), 4 => (2, 0), 5 => (0, 2), 6 => (1, 1), 8 => (3, 0), 10 => (1, 2), 12 => (2, 1),
        16 => (4, 0), 20 => (2, 2), 24 => (3, 1), 32 => (5, 0), 40 => (3, 2), 48 => (4, 1), 64 => (6, 0),
        80 => (4, 2), 96 => (5, 1), 128 => (7, 0), 160 => (5, 2), 192 => (6, 1), 256 => (8, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(levels)),
    };

    private static int IseBitCount(int levels, int count)
    {
        var (b, kind) = Encoding(levels);
        return kind switch
        {
            1 => (8 * count + 4) / 5 + count * b,
            2 => (7 * count + 2) / 3 + count * b,
            _ => count * b,
        };
    }

    private static void DecodeIse(UInt128 bits, int start, int count, int levels, Span<int> result)
    {
        var (b, kind) = Encoding(levels);
        var end = start + IseBitCount(levels, count);
        var pos = start;
        int Read(int n)
        {
            // Bits past the end of the sequence read as zero (they belong to other fields).
            var avail = Math.Clamp(end - pos, 0, n);
            var v = avail > 0 ? Bits(bits, pos, avail) : 0;
            pos += n;
            return v;
        }

        if (kind == 0)
        {
            for (var i = 0; i < count; i++) result[i] = Read(b);
            return;
        }
        var group = kind == 1 ? 5 : 3;
        Span<int> m = stackalloc int[5];
        for (var i = 0; i < count; i += group)
        {
            int packed;
            if (kind == 1)
            {
                m[0] = Read(b); packed = Read(2);
                m[1] = Read(b); packed |= Read(2) << 2;
                m[2] = Read(b); packed |= Read(1) << 4;
                m[3] = Read(b); packed |= Read(2) << 5;
                m[4] = Read(b); packed |= Read(1) << 7;
            }
            else
            {
                m[0] = Read(b); packed = Read(3);
                m[1] = Read(b); packed |= Read(2) << 3;
                m[2] = Read(b); packed |= Read(2) << 5;
            }
            for (var j = 0; j < group && i + j < count; j++)
            {
                var hiPart = kind == 1 ? TritTable[packed * 5 + j] : QuintTable[packed * 3 + j];
                result[i + j] = (hiPart << b) | m[j];
            }
        }
    }

    private static readonly byte[] TritTable = BuildTrits();
    private static readonly byte[] QuintTable = BuildQuints();

    private static byte[] BuildTrits()
    {
        var table = new byte[256 * 5];
        for (var t = 0; t < 256; t++)
        {
            int Bit(int v, int i) => (v >> i) & 1;
            int c, t4, t3, t2, t1, t0;
            if (((t >> 2) & 7) == 7)
            {
                c = (((t >> 5) & 7) << 2) | (t & 3);
                t4 = 2; t3 = 2;
            }
            else
            {
                c = t & 0x1F;
                if (((t >> 5) & 3) == 3) { t4 = 2; t3 = Bit(t, 7); }
                else { t4 = Bit(t, 7); t3 = (t >> 5) & 3; }
            }
            if ((c & 3) == 3) { t2 = 2; t1 = Bit(c, 4); t0 = (Bit(c, 3) << 1) | (Bit(c, 2) & ~Bit(c, 3) & 1); }
            else if (((c >> 2) & 3) == 3) { t2 = 2; t1 = 2; t0 = c & 3; }
            else { t2 = Bit(c, 4); t1 = (c >> 2) & 3; t0 = (Bit(c, 1) << 1) | (Bit(c, 0) & ~Bit(c, 1) & 1); }
            table[t * 5] = (byte)t0; table[t * 5 + 1] = (byte)t1; table[t * 5 + 2] = (byte)t2;
            table[t * 5 + 3] = (byte)t3; table[t * 5 + 4] = (byte)t4;
        }
        return table;
    }

    private static byte[] BuildQuints()
    {
        var table = new byte[128 * 3];
        for (var q = 0; q < 128; q++)
        {
            int Bit(int v, int i) => (v >> i) & 1;
            int q2, q1, q0;
            if (((q >> 1) & 3) == 3 && ((q >> 5) & 3) == 0)
            {
                q2 = (Bit(q, 0) << 2) | ((Bit(q, 4) & ~Bit(q, 0) & 1) << 1) | (Bit(q, 3) & ~Bit(q, 0) & 1);
                q1 = 4; q0 = 4;
            }
            else
            {
                int c;
                if (((q >> 1) & 3) == 3) { q2 = 4; c = (((q >> 3) & 3) << 3) | ((~(q >> 5) & 3) << 1) | (q & 1); }
                else { q2 = (q >> 5) & 3; c = q & 0x1F; }
                if ((c & 7) == 5) { q1 = 4; q0 = (c >> 3) & 3; }
                else { q1 = (c >> 3) & 3; q0 = c & 7; }
            }
            table[q * 3] = (byte)q0; table[q * 3 + 1] = (byte)q1; table[q * 3 + 2] = (byte)q2;
        }
        return table;
    }

    // ---- unquantisation ----

    private static int UnquantizeColor(int v, int levels)
    {
        var (b, kind) = Encoding(levels);
        if (kind == 0) return Replicate(v, b, 8);
        var m = v & ((1 << b) - 1);
        var d = v >> b;
        var a = (m & 1) != 0 ? 0x1FF : 0;
        int bb, c;
        if (kind == 1)
        {
            switch (b)
            {
                case 1: c = 204; bb = 0; break;
                case 2: { c = 93; var x = (m >> 1) & 1; bb = (x << 8) | (x << 4) | (x << 2) | (x << 1); break; }
                case 3: { c = 44; var x = (m >> 1) & 3; bb = (x << 7) | (x << 2) | x; break; }
                case 4: { c = 22; var x = (m >> 1) & 7; bb = (x << 6) | x; break; }
                case 5: { c = 11; var x = (m >> 1) & 0xF; bb = (x << 5) | (x >> 2); break; }
                default: { c = 5; var x = (m >> 1) & 0x1F; bb = (x << 4) | (x >> 4); break; }
            }
        }
        else
        {
            switch (b)
            {
                case 1: c = 113; bb = 0; break;
                case 2: { c = 54; var x = (m >> 1) & 1; bb = (x << 8) | (x << 3) | (x << 2); break; }
                case 3: { c = 26; var x = (m >> 1) & 3; bb = (x << 7) | (x << 1) | (x >> 1); break; }
                case 4: { c = 13; var x = (m >> 1) & 7; bb = (x << 6) | (x >> 1); break; }
                default: { c = 6; var x = (m >> 1) & 0xF; bb = (x << 5) | (x >> 3); break; }
            }
        }
        var t = d * c + bb;
        t ^= a;
        return (a & 0x80) | (t >> 2);
    }

    private static int UnquantizeWeight(int v, int levels)
    {
        var (b, kind) = Encoding(levels);
        int r;
        if (kind == 0) r = Replicate(v, b, 6);
        else if (b == 0)
        {
            r = kind == 1 ? v switch { 0 => 0, 1 => 32, _ => 63 } : v switch { 0 => 0, 1 => 16, 2 => 32, 3 => 47, _ => 63 };
        }
        else
        {
            var m = v & ((1 << b) - 1);
            var d = v >> b;
            var a = (m & 1) != 0 ? 0x7F : 0;
            int bb, c;
            if (kind == 1)
            {
                switch (b)
                {
                    case 1: c = 50; bb = 0; break;
                    case 2: { c = 23; var x = (m >> 1) & 1; bb = (x << 6) | (x << 2) | x; break; }
                    default: { c = 11; var x = (m >> 1) & 3; bb = (x << 5) | x; break; }
                }
            }
            else
            {
                switch (b)
                {
                    case 1: c = 28; bb = 0; break;
                    default: { c = 13; var x = (m >> 1) & 1; bb = (x << 6) | (x << 1); break; }
                }
            }
            var t = d * c + bb;
            t ^= a;
            r = (a & 0x20) | (t >> 2);
        }
        return r > 32 ? r + 1 : r;
    }

    private static int Replicate(int v, int fromBits, int toBits)
    {
        if (fromBits == 0) return 0;
        var r = 0;
        var filled = 0;
        while (filled < toBits)
        {
            r = (r << fromBits) | v;
            filled += fromBits;
        }
        return r >> (filled - toBits);
    }

    // ---- endpoints ----

    private static void DecodeEndpoints(int mode, ReadOnlySpan<int> v, Span<int> e)
    {
        void Set(Span<int> o, int r, int g, int b, int a) { o[0] = Clamp(r); o[1] = Clamp(g); o[2] = Clamp(b); o[3] = Clamp(a); }
        var e0 = e[..4];
        var e1 = e[4..];
        switch (mode)
        {
            case 0: Set(e0, v[0], v[0], v[0], 255); Set(e1, v[1], v[1], v[1], 255); break;
            case 1:
            {
                var l0 = (v[0] >> 2) | (v[1] & 0xC0);
                var l1 = Math.Min(l0 + (v[1] & 0x3F), 255);
                Set(e0, l0, l0, l0, 255); Set(e1, l1, l1, l1, 255);
                break;
            }
            case 4: Set(e0, v[0], v[0], v[0], v[2]); Set(e1, v[1], v[1], v[1], v[3]); break;
            case 5:
            {
                var (b0, a0) = BitTransferSigned(v[1], v[0]);
                var (b2, a2) = BitTransferSigned(v[3], v[2]);
                Set(e0, a0, a0, a0, a2); Set(e1, a0 + b0, a0 + b0, a0 + b0, a2 + b2);
                break;
            }
            case 6:
                Set(e0, (v[0] * v[3]) >> 8, (v[1] * v[3]) >> 8, (v[2] * v[3]) >> 8, 255);
                Set(e1, v[0], v[1], v[2], 255);
                break;
            case 8 or 12:
            {
                int a0 = mode == 12 ? v[6] : 255, a1 = mode == 12 ? v[7] : 255;
                if (v[1] + v[3] + v[5] >= v[0] + v[2] + v[4]) { Set(e0, v[0], v[2], v[4], a0); Set(e1, v[1], v[3], v[5], a1); }
                else { BlueContract(e0, v[1], v[3], v[5], a1); BlueContract(e1, v[0], v[2], v[4], a0); }
                break;
            }
            case 9 or 13:
            {
                var (b0, a0) = BitTransferSigned(v[1], v[0]);
                var (b1, a1) = BitTransferSigned(v[3], v[2]);
                var (b2, a2) = BitTransferSigned(v[5], v[4]);
                int al0 = 255, al1 = 255;
                if (mode == 13) { var (b3, a3) = BitTransferSigned(v[7], v[6]); al0 = a3; al1 = a3 + b3; }
                if (b0 + b1 + b2 >= 0) { Set(e0, a0, a1, a2, al0); Set(e1, a0 + b0, a1 + b1, a2 + b2, al1); }
                else { BlueContract(e0, a0 + b0, a1 + b1, a2 + b2, al1); BlueContract(e1, a0, a1, a2, al0); }
                break;
            }
            case 10:
                Set(e0, (v[0] * v[3]) >> 8, (v[1] * v[3]) >> 8, (v[2] * v[3]) >> 8, v[4]);
                Set(e1, v[0], v[1], v[2], v[5]);
                break;
            default: // HDR modes are rejected earlier
                Set(e0, 255, 0, 255, 255); Set(e1, 255, 0, 255, 255);
                break;
        }
    }

    /// <summary>The spec's bit_transfer_signed(a = offset value, b = base value): returns (signed offset, base).</summary>
    private static (int Offset, int Base) BitTransferSigned(int offset, int baseValue)
    {
        baseValue >>= 1;
        baseValue |= offset & 0x80;
        offset >>= 1;
        offset &= 0x3F;
        if ((offset & 0x20) != 0) offset -= 0x40;
        return (offset, baseValue);
    }

    private static void BlueContract(Span<int> o, int r, int g, int b, int a)
    {
        o[0] = Clamp((r + b) >> 1); o[1] = Clamp((g + b) >> 1); o[2] = Clamp(b); o[3] = Clamp(a);
    }

    private static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

    // ---- partitions ----

    private static int SelectPartition(int seed, int x, int y, int z, int partitionCount, bool smallBlock)
    {
        if (smallBlock) { x <<= 1; y <<= 1; z <<= 1; }
        seed += (partitionCount - 1) * 1024;
        var rnum = Hash52((uint)seed);
        int s1 = (int)(rnum & 0xF), s2 = (int)((rnum >> 4) & 0xF), s3 = (int)((rnum >> 8) & 0xF), s4 = (int)((rnum >> 12) & 0xF);
        int s5 = (int)((rnum >> 16) & 0xF), s6 = (int)((rnum >> 20) & 0xF), s7 = (int)((rnum >> 24) & 0xF), s8 = (int)((rnum >> 28) & 0xF);
        int s9 = (int)((rnum >> 18) & 0xF), s10 = (int)((rnum >> 22) & 0xF), s11 = (int)((rnum >> 26) & 0xF);
        var s12 = (int)(((rnum >> 30) | (rnum << 2)) & 0xF);
        s1 *= s1; s2 *= s2; s3 *= s3; s4 *= s4; s5 *= s5; s6 *= s6; s7 *= s7; s8 *= s8; s9 *= s9; s10 *= s10; s11 *= s11; s12 *= s12;
        int sh1, sh2;
        if ((seed & 1) != 0) { sh1 = (seed & 2) != 0 ? 4 : 5; sh2 = partitionCount == 3 ? 6 : 5; }
        else { sh1 = partitionCount == 3 ? 6 : 5; sh2 = (seed & 2) != 0 ? 4 : 5; }
        var sh3 = (seed & 0x10) != 0 ? sh1 : sh2;
        s1 >>= sh1; s2 >>= sh2; s3 >>= sh1; s4 >>= sh2; s5 >>= sh1; s6 >>= sh2; s7 >>= sh1; s8 >>= sh2;
        s9 >>= sh3; s10 >>= sh3; s11 >>= sh3; s12 >>= sh3;
        var a = (int)((s1 * x + s2 * y + s11 * z + (rnum >> 14)) & 0x3F);
        var b = (int)((s3 * x + s4 * y + s12 * z + (rnum >> 10)) & 0x3F);
        var c = (int)((s5 * x + s6 * y + s9 * z + (rnum >> 6)) & 0x3F);
        var d = (int)((s7 * x + s8 * y + s10 * z + (rnum >> 2)) & 0x3F);
        if (partitionCount < 4) d = 0;
        if (partitionCount < 3) c = 0;
        if (a >= b && a >= c && a >= d) return 0;
        if (b >= c && b >= d) return 1;
        return c >= d ? 2 : 3;
    }

    private static uint Hash52(uint p)
    {
        p ^= p >> 15; p -= p << 17; p += p << 7; p += p << 4;
        p ^= p >> 5; p += p << 16; p ^= p >> 7; p ^= p >> 3;
        p ^= p << 6; p ^= p >> 17;
        return p;
    }

    // ---- bit helpers ----

    private static int Bits(UInt128 v, int start, int count) =>
        count == 0 || start >= 128 || start < 0 ? 0 : (int)(ulong)((v >> start) & (UInt128)((1UL << count) - 1));

    private static UInt128 Reverse(UInt128 v) =>
        new(ReverseBits((ulong)v), ReverseBits((ulong)(v >> 64)));

    private static ulong ReverseBits(ulong x)
    {
        x = ((x >> 1) & 0x5555555555555555UL) | ((x & 0x5555555555555555UL) << 1);
        x = ((x >> 2) & 0x3333333333333333UL) | ((x & 0x3333333333333333UL) << 2);
        x = ((x >> 4) & 0x0F0F0F0F0F0F0F0FUL) | ((x & 0x0F0F0F0F0F0F0F0FUL) << 4);
        x = ((x >> 8) & 0x00FF00FF00FF00FFUL) | ((x & 0x00FF00FF00FF00FFUL) << 8);
        x = ((x >> 16) & 0x0000FFFF0000FFFFUL) | ((x & 0x0000FFFF0000FFFFUL) << 16);
        return (x >> 32) | (x << 32);
    }
}
