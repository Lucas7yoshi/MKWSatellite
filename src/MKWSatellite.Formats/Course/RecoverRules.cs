namespace MKWSatellite.Formats.Course;

/// <summary>
/// Where Lakitu puts you back on the AI route graph, and whether that spot gives the respawn boost.
/// Traced in <c>Rescue_FindRecoverPointOnPath</c> (<c>0x7100D01670</c>), route point fields parsed in
/// <c>RoutePoint_ParseDynamicParams</c> (<c>0x7100AD45F0</c>):
/// <list type="bullet">
/// <item><c>Dynamic.RecoverType</c> (route point +932/+936, default 0): the search starts at the route point nearest
/// the player (among those linked to the last valid lap node) and repeats, up to 100 steps: 1 = move to a previous
/// point, 2 = move to a next point, 0 (and 5, which no branch handles) = respawn here. 3 re-picks the route point
/// nearest the player among those linked to the current lap node (normally the same point, and the walk then accepts
/// the start); 4 asks a recover override (<c>sub_710298C340</c>) and then stops. This model treats 3 and 4 as "respawn
/// here" (<b>approximate</b>). With several previous/next points the game picks the one nearest along the lap graph;
/// this model takes the first.</item>
/// <item><c>Dynamic.IsRecoverDash</c> (flag word +944 bit 0x8000): read from the point the search ends on and returned
/// as the search's "recover dash" result. That this result is what gives the boost after landing is
/// <b>unverified</b> (its consumer was not traced).</item>
/// </list>
/// </summary>
public sealed class RecoverRules
{
    public const int MaxSteps = 100;

    /// <summary>A route point: its rail and index.</summary>
    public readonly record struct Node(CourseRail Rail, int Index)
    {
        public BancRailPoint Point => Rail.Rail.Points[Index];
        public int RecoverType => Point.Dynamic?.TryGetValue("RecoverType", out var v) == true && v is IConvertible c ? c.ToInt32(null) : 0;
        public bool IsRecoverDash => Point.Dynamic?.Get<bool>("IsRecoverDash") == true;
    }

    private readonly Dictionary<BancRailPoint, Node> _nodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BancRailPoint, List<Node>> _prev = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BancRailPoint, Node?> _landing = new(ReferenceEqualityComparer.Instance);

    public RecoverRules(CourseScene course)
    {
        var routes = course.Rails.Where(r => r.Kind == RailKinds.Route).ToList();
        foreach (var r in routes)
            for (var i = 0; i < r.Rail.Points.Count; i++) _nodes[r.Rail.Points[i]] = new Node(r, i);
        foreach (var n in _nodes.Values) _byHash.TryAdd(n.Point.Hash, n);
        foreach (var n in _nodes.Values)
            foreach (var next in Next(n))
            {
                if (!_prev.TryGetValue(next.Point, out var list)) _prev[next.Point] = list = [];
                list.Add(n);
            }
    }

    /// <summary>Next points: the following point on the rail (wrapping when closed), then linked points on other routes.</summary>
    public IEnumerable<Node> Next(Node n)
    {
        var pts = n.Rail.Rail.Points;
        if (n.Index + 1 < pts.Count) yield return new Node(n.Rail, n.Index + 1);
        else if (n.Rail.Rail.IsClosed && pts.Count > 2) yield return new Node(n.Rail, 0);
        foreach (var h in n.Point.LinkTargets)
            if (_byHash.TryGetValue(h, out var t)) yield return t;
    }

    private readonly Dictionary<ulong, Node> _byHash = [];

    public IReadOnlyList<Node> Prev(Node n) => _prev.TryGetValue(n.Point, out var l) ? l : [];

    public Node? NodeOf(BancRailPoint p) => _nodes.TryGetValue(p, out var n) ? n : null;

    /// <summary>
    /// Where a respawn that starts at <paramref name="start"/> ends, following <c>RecoverType</c> 1/2. When the walk
    /// runs past <see cref="MaxSteps"/> (a 1/2 loop): the game then falls back to the starting point, which is returned.
    /// </summary>
    public Node Landing(Node start)
    {
        if (_landing.TryGetValue(start.Point, out var cached)) return cached!.Value;
        Node? result = null;
        var n = start;
        for (var step = 0; step <= MaxSteps; step++)
        {
            var type = n.RecoverType;
            Node? to = type switch
            {
                1 => Prev(n) is { Count: > 0 } p ? p[0] : Next(n).Cast<Node?>().FirstOrDefault(),
                2 => Next(n).Cast<Node?>().FirstOrDefault(),
                _ => null,
            };
            if (to is not { } t) { result = n; break; }
            n = t;
        }
        result ??= start;
        _landing[start.Point] = result;
        return result.Value;
    }

    /// <summary>All route points marked <c>IsRecoverDash</c>.</summary>
    public IEnumerable<Node> DashPoints => _nodes.Values.Where(n => n.IsRecoverDash);
}
