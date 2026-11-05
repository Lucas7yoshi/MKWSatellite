using System.Text;
using MKWSatellite.Formats.Byml;
using MKWSatellite.Formats.Course;

/// <summary>Accumulates what keys and types appear across many Banc scenes, for format discovery.</summary>
internal sealed class SceneCensus
{
    private sealed class Bucket
    {
        public int Count;
        public readonly HashSet<string> Scenes = [];
        public readonly SortedDictionary<string, KeyStat> Keys = new(StringComparer.Ordinal);
    }

    private sealed class KeyStat
    {
        public int Count;
        public readonly SortedSet<string> Types = [];
        public readonly SortedSet<string> Samples = new(StringComparer.Ordinal);
    }

    private readonly SortedDictionary<string, Bucket> _rootKeys = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, Bucket> _actors = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, Bucket> _rails = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, Bucket> _railPoints = new(StringComparer.Ordinal);
    private int _scenes;

    public void Add(string file, BancScene scene)
    {
        _scenes++;
        var root = Get(_rootKeys, "(root)", file);
        Collect(root, scene.Raw, "", 1);
        foreach (var a in scene.Actors)
            Collect(Get(_actors, a.Type, file), a.Raw, "", 3, skip: k => k is "Hash" or "SRTHash" or "InstanceId" or "Gyaml" or "Translate");
        foreach (var r in scene.Rails)
        {
            Collect(Get(_rails, r.Name, file), r.Raw, "", 1, skip: k => k is "Points" or "Hash");
            var pts = Get(_railPoints, r.Name, file);
            foreach (var p in r.Points)
                Collect(pts, p.Raw, "", 3, skip: k => k is "Hash" or "Parent" or "Translation" or "Src" or "Dst");
        }
    }

    private static Bucket Get(SortedDictionary<string, Bucket> d, string key, string file)
    {
        if (!d.TryGetValue(key, out var b)) d[key] = b = new Bucket();
        b.Count++;
        b.Scenes.Add(file);
        return b;
    }

    private static void Collect(Bucket b, BymlDict dict, string prefix, int depth, Func<string, bool>? skip = null)
    {
        foreach (var (k, v) in dict)
        {
            if (skip?.Invoke(k) == true) continue;
            var path = prefix + k;
            if (!b.Keys.TryGetValue(path, out var s)) b.Keys[path] = s = new KeyStat();
            s.Count++;
            s.Types.Add(TypeName(v));
            if (v is BymlDict child && depth > 1) Collect(b, child, path + ".", depth - 1, skip);
            else if (v is BymlArray arr && depth > 1 && arr.Count > 0 && arr[0] is BymlDict)
                foreach (var e in arr.OfType<BymlDict>()) Collect(b, e, path + "[].", depth - 1, skip);
            else if (s.Samples.Count < 8) s.Samples.Add(Format(v));
        }
    }

    private static string TypeName(object? v) => v switch
    {
        null => "null",
        BymlDict => "dict",
        BymlArray a => $"array[{a.Count}]",
        _ => v.GetType().Name.ToLowerInvariant(),
    };

    private static string Format(object? v) => v switch
    {
        null => "null",
        BymlArray a when a.Count <= 4 && a.All(x => x is not BymlDict and not BymlArray) => "[" + string.Join(", ", a.Select(Format)) + "]",
        BymlArray a => $"[{a.Count} items]",
        BymlDict d => $"{{{d.Count} keys}}",
        float f => f.ToString("0.###"),
        string s => s.Length > 60 ? s[..60] + "…" : s,
        _ => v.ToString() ?? "",
    };

    public string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Banc census over {_scenes} scenes");
        Section(sb, "Root keys", _rootKeys, showScenes: false);
        Section(sb, "Rails", _rails, showScenes: true);
        Section(sb, "Rail points (by rail kind)", _railPoints, showScenes: false);
        Section(sb, "Actors", _actors, showScenes: true);
        return sb.ToString();
    }

    private void Section(StringBuilder sb, string title, SortedDictionary<string, Bucket> d, bool showScenes)
    {
        sb.AppendLine().AppendLine($"## {title} ({d.Count})");
        foreach (var (name, b) in d.OrderByDescending(x => x.Value.Scenes.Count).ThenBy(x => x.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"{name}  x{b.Count} in {b.Scenes.Count} scenes" +
                          (showScenes && b.Scenes.Count <= 3 ? $"  ({string.Join(", ", b.Scenes)})" : ""));
            foreach (var (k, s) in b.Keys)
            {
                sb.AppendLine($"    {k}: {string.Join("|", s.Types)} x{s.Count}  e.g. {string.Join("; ", s.Samples)}");
            }
        }
    }
}
