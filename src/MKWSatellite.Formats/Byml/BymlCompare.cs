namespace MKWSatellite.Formats.Byml;

/// <summary>Structural comparison of BYML trees (types and values, floats by bit pattern).</summary>
public static class BymlCompare
{
    /// <summary>A description of the first difference, or null if the trees are equal.</summary>
    public static string? FirstDifference(object? a, object? b, string path = "")
    {
        switch (a, b)
        {
            case (null, null): return null;
            case (BymlDict x, BymlDict y):
                if (x.Count != y.Count) return $"{path}: {x.Count} vs {y.Count} keys";
                foreach (var (k, v) in x)
                {
                    if (!y.TryGetValue(k, out var w)) return $"{path}.{k}: missing";
                    if (FirstDifference(v, w, $"{path}.{k}") is { } d) return d;
                }
                return null;
            case (BymlArray x, BymlArray y):
                if (x.Count != y.Count) return $"{path}: {x.Count} vs {y.Count} items";
                for (var i = 0; i < x.Count; i++)
                    if (FirstDifference(x[i], y[i], $"{path}[{i}]") is { } d) return d;
                return null;
            case (BymlHashMap x, BymlHashMap y):
                if (x.Count != y.Count) return $"{path}: {x.Count} vs {y.Count} entries";
                foreach (var (k, v) in x)
                {
                    if (!y.TryGetValue(k, out var w)) return $"{path}[0x{k:X}]: missing";
                    if (FirstDifference(v, w, $"{path}[0x{k:X}]") is { } d) return d;
                }
                return null;
            case (float x, float y):
                return BitConverter.SingleToInt32Bits(x) == BitConverter.SingleToInt32Bits(y) ? null : $"{path}: {x} vs {y}";
            case (double x, double y):
                return BitConverter.DoubleToInt64Bits(x) == BitConverter.DoubleToInt64Bits(y) ? null : $"{path}: {x} vs {y}";
            case (byte[] x, byte[] y):
                return x.AsSpan().SequenceEqual(y) ? null : $"{path}: binary differs";
            default:
                if (a?.GetType() != b?.GetType()) return $"{path}: {a?.GetType().Name} vs {b?.GetType().Name}";
                return Equals(a, b) ? null : $"{path}: {a} vs {b}";
        }
    }
}
