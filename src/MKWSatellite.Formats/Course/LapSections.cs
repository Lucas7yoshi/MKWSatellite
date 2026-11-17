using System.Numerics;

namespace MKWSatellite.Formats.Course;

/// <summary>
/// Splits the lap path into checkpoint sections for display. The lap graph (<see cref="LapZone.Segments"/>) is walked
/// breadth-first from the <c>IsStart</c> nodes; each node belongs to the last <c>CheckPoint</c> node before it on the
/// way there. Passing an <c>IsLapCheck</c> node starts a new part, because intermission courses number their
/// checkpoints again after it (e.g. 1-3 on the approach, then 1-6 around the lap). So a section is a (part, number)
/// pair, numbered in the order the walk reaches them; parallel branches with the same number share a section. Nodes the
/// walk does not reach take the section of the nearest node it does. A viewing aid, not game logic: what
/// <c>IsLapCheck</c> does to the checkpoint count in game is <b>unverified</b>.
/// </summary>
public sealed class LapSections
{
    /// <summary>One section: its <see cref="Part"/> (lap checks passed before it) and checkpoint number (null before the first).</summary>
    public sealed record Section(int Part, int? CheckPoint);

    private readonly Dictionary<LapPoint, int> _section = new(ReferenceEqualityComparer.Instance);
    private readonly Graph _graph;

    public IReadOnlyList<Section> All => _graph.Sections;

    /// <summary>Highest section index (sections are 0..Max), or -1 if the course has no checkpoints.</summary>
    public int Max { get; }

    public LapSections(CourseScene course)
    {
        var nodes = course.LapPoints;
        var index = new Dictionary<LapPoint, int>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < nodes.Count; i++) index[nodes[i]] = i;
        var edges = LapZone.Segments(course).Select(e => (index[e.A], index[e.B])).ToList();
        _graph = Build(nodes.Count, edges, i => nodes[i].IsStart, i => nodes[i].IsLapCheck,
            i => nodes[i].CheckPoint is { } c ? (int)MathF.Round(c) : null,
            (a, b) => Vector3.DistanceSquared(nodes[a].Position, nodes[b].Position));
        Max = nodes.Any(l => l.CheckPoint is not null) ? _graph.Sections.Count - 1 : -1;
        for (var i = 0; i < nodes.Count; i++)
            if (_graph.Of[i] >= 0) _section[nodes[i]] = _graph.Of[i];
    }

    public int? Of(LapPoint p) => _section.TryGetValue(p, out var s) ? s : null;

    /// <summary>Short name of a section: "CP 3", "Start" or "Lap check", with the part when there are several.</summary>
    public string Label(int s)
    {
        if (s < 0 || s >= All.Count) return "?";
        var x = All[s];
        var name = x.CheckPoint is { } c ? $"CP {c}" : x.Part == 0 ? "Start" : "Lap check";
        return All.Any(y => y.Part > 0) ? $"{name} (part {x.Part + 1})" : name;
    }

    /// <summary>
    /// Whether section <paramref name="s"/> is at most <paramref name="before"/> sections before or
    /// <paramref name="after"/> after <paramref name="centre"/>, counting steps along the lap graph.
    /// </summary>
    public bool InWindow(int s, int centre, int before, int after) => _graph.InWindow(s, centre, before, after);

    /// <summary>Sections of a lap graph given as node indices (see <see cref="LapSections"/>); public for tests.</summary>
    public static Graph Build(int count, IReadOnlyList<(int A, int B)> edges, Func<int, bool> isStart, Func<int, bool> isLapCheck,
        Func<int, int?> checkPoint, Func<int, int, float> distanceSquared)
    {
        var next = new List<int>[count];
        var hasPrev = new bool[count];
        for (var i = 0; i < count; i++) next[i] = [];
        foreach (var (a, b) in edges) { next[a].Add(b); hasPrev[b] = true; }

        var roots = Enumerable.Range(0, count).Where(isStart).ToList();
        if (roots.Count == 0) roots = Enumerable.Range(0, count).Where(i => !hasPrev[i]).ToList();
        if (roots.Count == 0 && count > 0) roots = [Enumerable.Range(0, count).FirstOrDefault(i => checkPoint(i) is not null)];

        var keys = new List<Section>();
        var keyIndex = new Dictionary<Section, int>();
        var of = Enumerable.Repeat(-1, count).ToArray();
        var queue = new Queue<(int Node, int Part, int? Cp)>(roots.Select(r => (r, 0, (int?)null)));
        while (queue.Count > 0)
        {
            var (n, part, cp) = queue.Dequeue();
            if (of[n] >= 0) continue;
            if (isLapCheck(n)) { part++; cp = null; }
            if (checkPoint(n) is { } c) cp = c;
            var key = new Section(part, cp);
            if (!keyIndex.TryGetValue(key, out var k)) { keyIndex[key] = k = keys.Count; keys.Add(key); }
            of[n] = k;
            foreach (var m in next[n]) if (of[m] < 0) queue.Enqueue((m, part, cp));
        }

        var reached = Enumerable.Range(0, count).Where(i => of[i] >= 0).ToList();
        if (reached.Count > 0)
        {
            var fill = Enumerable.Range(0, count).Where(i => of[i] < 0)
                .Select(i => (i, reached.MinBy(r => distanceSquared(r, i)))).ToList();
            foreach (var (i, r) in fill) of[i] = of[r];
        }

        var fwd = new HashSet<int>[keys.Count];
        var back = new HashSet<int>[keys.Count];
        for (var i = 0; i < keys.Count; i++) { fwd[i] = []; back[i] = []; }
        foreach (var (a, b) in edges)
            if (of[a] >= 0 && of[b] >= 0 && of[a] != of[b]) { fwd[of[a]].Add(of[b]); back[of[b]].Add(of[a]); }
        return new Graph(keys, of, fwd, back);
    }

    public sealed class Graph(IReadOnlyList<Section> sections, int[] of, HashSet<int>[] fwd, HashSet<int>[] back)
    {
        public IReadOnlyList<Section> Sections => sections;
        /// <summary>Section of each node (-1 if none).</summary>
        public IReadOnlyList<int> Of => of;

        private (int Centre, int Before, int After, HashSet<int> Shown)? _cache;

        public bool InWindow(int s, int centre, int before, int after)
        {
            if (_cache is not { } c || c.Centre != centre || c.Before != before || c.After != after)
            {
                var shown = new HashSet<int> { centre };
                if (centre >= 0 && centre < sections.Count)
                {
                    Walk(fwd, centre, after, shown);
                    Walk(back, centre, before, shown);
                }
                _cache = c = (centre, before, after, shown);
            }
            return c.Shown.Contains(s);
        }

        private static void Walk(HashSet<int>[] links, int from, int steps, HashSet<int> into)
        {
            var frontier = new List<int> { from };
            var seen = new HashSet<int> { from };
            for (var d = 0; d < steps && frontier.Count > 0; d++)
            {
                var nextFrontier = new List<int>();
                foreach (var s in frontier)
                    foreach (var t in links[s])
                        if (seen.Add(t)) { into.Add(t); nextFrontier.Add(t); }
                frontier = nextFrontier;
            }
        }
    }
}
