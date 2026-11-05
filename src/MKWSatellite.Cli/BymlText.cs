using MKWSatellite.Formats.Byml;

/// <summary>Indented text rendering of a BYML tree for exploration.</summary>
internal static class BymlText
{
    public static void Write(TextWriter w, object? node, int maxDepth, int maxItems, string indent = "", int depth = 0)
    {
        switch (node)
        {
            case BymlDict d:
                foreach (var (k, v) in d)
                {
                    if (v is BymlDict or BymlArray or BymlHashMap && !IsScalarArray(v))
                    {
                        w.WriteLine($"{indent}{k}: {Summary(v)}");
                        if (depth < maxDepth) Write(w, v, maxDepth, maxItems, indent + "  ", depth + 1);
                    }
                    else w.WriteLine($"{indent}{k}: {Scalar(v)}");
                }
                break;
            case BymlHashMap h:
                var shown = 0;
                foreach (var (k, v) in h)
                {
                    if (shown++ >= maxItems) { w.WriteLine($"{indent}... {h.Count - maxItems} more"); break; }
                    if (v is BymlDict or BymlArray or BymlHashMap && !IsScalarArray(v))
                    {
                        w.WriteLine($"{indent}#{k:X16}: {Summary(v)}");
                        if (depth < maxDepth) Write(w, v, maxDepth, maxItems, indent + "  ", depth + 1);
                    }
                    else w.WriteLine($"{indent}#{k:X16}: {Scalar(v)}");
                }
                break;
            case BymlArray a:
                for (var i = 0; i < a.Count && i < maxItems; i++)
                {
                    var v = a[i];
                    if (v is BymlDict or BymlArray or BymlHashMap && !IsScalarArray(v))
                    {
                        w.WriteLine($"{indent}[{i}] {Summary(v)}");
                        if (depth < maxDepth) Write(w, v, maxDepth, maxItems, indent + "  ", depth + 1);
                    }
                    else w.WriteLine($"{indent}[{i}] {Scalar(v)}");
                }
                if (a.Count > maxItems) w.WriteLine($"{indent}... {a.Count - maxItems} more");
                break;
            default:
                w.WriteLine(indent + Scalar(node));
                break;
        }
    }

    private static bool IsScalarArray(object? v) => v is BymlArray a && a.Count <= 8 && a.All(x => x is not BymlDict and not BymlArray and not BymlHashMap);

    private static string Summary(object? v) => v switch
    {
        BymlDict d => $"{{{d.Count}}}",
        BymlArray a => $"[{a.Count}]",
        BymlHashMap h => $"#{{{h.Count}}}",
        _ => "",
    };

    private static string Scalar(object? v) => v switch
    {
        null => "null",
        string s => $"\"{s}\"",
        float f => f.ToString("R"),
        byte[] b => $"<{b.Length} bytes>",
        BymlArray a => "[" + string.Join(", ", a.Select(Scalar)) + "]",
        _ => v.ToString() ?? "",
    };
}
