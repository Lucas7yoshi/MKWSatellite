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

    /// <summary>Plain YAML: dicts as maps, hash maps with 0x-hex keys, arrays as block sequences, binary as hex.</summary>
    public static void WriteYaml(TextWriter w, object? node, string indent = "")
    {
        static bool Container(object? v) => v is BymlDict { Count: > 0 } or BymlArray { Count: > 0 } or BymlHashMap { Count: > 0 };
        void Entry(string key, object? v)
        {
            if (Container(v)) { w.WriteLine($"{indent}{key}:"); WriteYaml(w, v, indent + "  "); }
            else w.WriteLine($"{indent}{key}: {YamlScalar(v)}");
        }
        switch (node)
        {
            case BymlDict d:
                foreach (var (k, v) in d) Entry(YamlKey(k), v);
                break;
            case BymlHashMap h:
                foreach (var (k, v) in h) Entry($"0x{k:X16}", v);
                break;
            case BymlArray a:
                foreach (var v in a)
                {
                    if (!Container(v)) { w.WriteLine($"{indent}- {YamlScalar(v)}"); continue; }
                    // First line of a nested block goes after the dash.
                    var sub = new StringWriter();
                    WriteYaml(sub, v, indent + "  ");
                    var text = sub.ToString();
                    w.Write($"{indent}- {text[(indent.Length + 2)..]}");
                }
                break;
            default:
                w.WriteLine(indent + YamlScalar(node));
                break;
        }
    }

    private static string YamlKey(string k) =>
        k.Length > 0 && k.All(c => char.IsLetterOrDigit(c) || c is '_' or '.' or '-' or '/') && !char.IsDigit(k[0]) ? k : YamlString(k);

    private static string YamlString(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    private static string YamlScalar(object? v) => v switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        string s => YamlString(s),
        float f => float.IsFinite(f) ? f.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : float.IsNaN(f) ? ".nan" : f > 0 ? ".inf" : "-.inf",
        double d => double.IsFinite(d) ? d.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : double.IsNaN(d) ? ".nan" : d > 0 ? ".inf" : "-.inf",
        byte[] b => "!!binary " + Convert.ToBase64String(b),
        BymlDict => "{}",
        BymlArray => "[]",
        BymlHashMap => "{}",
        IFormattable x => x.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

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
