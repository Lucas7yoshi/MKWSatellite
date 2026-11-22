using System.Numerics;

namespace MKWSatellite.Formats.Course;

/// <summary>
/// How the game turns rail nodes into a curve (traced in 1.8, all rail kinds share it; docs/formats/rails.md "Curves").
/// <list type="bullet">
/// <item><c>RailGraph_ParseRail</c> (0x710029AD70) reads each node's <c>Translation</c>, <c>Control0</c> and
/// <c>Control1</c> (a missing handle is the node position) and <c>RailBody_AddPoint</c> (0x710029A9A0) chains them into
/// segments: node i → node i+1, plus last → first on a closed rail.</item>
/// <item><c>RailSegment_SetCurveType</c> (0x7100980E00) gives each segment a type from its two inner handles: the start
/// node's <c>Control1</c> and the end node's <c>Control0</c>. A handle counts when any axis differs from its node by
/// more than 0.001. Neither: straight line; one: quadratic Bezier through that handle; both: cubic Bezier.</item>
/// <item>The evaluator (0x710070AC60, inlined in the grind-rail search <c>RailSlideSearcher_FindNearest</c>) and
/// <c>RailSegment_TangentAt</c> (0x7102A95660) use those curves.</item>
/// </list>
/// </summary>
public static class RailCurve
{
    /// <summary>Segment shape: bit 0 = start handle used, bit 1 = end handle used (the game's own values).</summary>
    [Flags]
    public enum Shape : byte { Line = 0, StartHandle = 1, EndHandle = 2, Cubic = 3 }

    private const float HandleEpsilon = 0.001f;

    private static bool Differs(Vector3 a, Vector3 b) =>
        MathF.Abs(a.X - b.X) > HandleEpsilon || MathF.Abs(a.Y - b.Y) > HandleEpsilon || MathF.Abs(a.Z - b.Z) > HandleEpsilon;

    public static Shape ShapeOf(BancRailPoint from, BancRailPoint to) =>
        (Differs(from.Translation, from.Control1) ? Shape.StartHandle : 0) | (Differs(to.Translation, to.Control0) ? Shape.EndHandle : 0);

    /// <summary>Point at <paramref name="t"/> (0..1) on the segment from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static Vector3 Evaluate(BancRailPoint from, BancRailPoint to, float t) => Evaluate(from, to, ShapeOf(from, to), t);

    public static Vector3 Evaluate(BancRailPoint from, BancRailPoint to, Shape shape, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        var p0 = from.Translation;
        var p1 = to.Translation;
        var u = 1 - t;
        switch (shape)
        {
            case Shape.Cubic:
                return u * u * u * p0 + 3 * u * u * t * from.Control1 + 3 * u * t * t * to.Control0 + t * t * t * p1;
            case Shape.StartHandle:
            case Shape.EndHandle:
                var c = shape == Shape.StartHandle ? from.Control1 : to.Control0;
                return u * u * p0 + 2 * u * t * c + t * t * p1;
            default:
                return Vector3.Lerp(p0, p1, t);
        }
    }

    /// <summary>
    /// Points along a segment, both ends included: just the two nodes for a straight one, otherwise enough samples
    /// that each chord is at most ~1 unit (8..64), which is finer than the game's own 9..16-step debug draw.
    /// </summary>
    public static void Sample(BancRailPoint from, BancRailPoint to, List<Vector3> into)
    {
        var shape = ShapeOf(from, to);
        into.Add(from.Translation);
        if (shape != Shape.Line)
        {
            // Length of the control polygon bounds the curve length.
            var c0 = shape.HasFlag(Shape.StartHandle) ? from.Control1 : from.Translation;
            var c1 = shape.HasFlag(Shape.EndHandle) ? to.Control0 : to.Translation;
            var hull = Vector3.Distance(from.Translation, c0) + Vector3.Distance(c0, c1) + Vector3.Distance(c1, to.Translation);
            var n = Math.Clamp((int)MathF.Ceiling(hull), 8, 64);
            for (var k = 1; k < n; k++) into.Add(Evaluate(from, to, shape, k / (float)n));
        }
        into.Add(to.Translation);
    }

    /// <summary>The rail's segments in order (closing segment last for a closed rail with more than two nodes).</summary>
    public static IEnumerable<(int From, int To)> Segments(BancRail rail)
    {
        var n = rail.Points.Count;
        for (var i = 1; i < n; i++) yield return (i - 1, i);
        if (rail.IsClosed && n > 2) yield return (n - 1, 0);
    }

    /// <summary>Length along the curves (sampled).</summary>
    public static float Length(BancRail rail)
    {
        var l = 0f;
        var buf = new List<Vector3>();
        foreach (var (a, b) in Segments(rail))
        {
            buf.Clear();
            Sample(rail.Points[a], rail.Points[b], buf);
            for (var k = 1; k < buf.Count; k++) l += Vector3.Distance(buf[k - 1], buf[k]);
        }
        return l;
    }

    /// <summary>True when any segment of the rail is curved.</summary>
    public static bool IsCurved(BancRail rail) => Segments(rail).Any(s => ShapeOf(rail.Points[s.From], rail.Points[s.To]) != Shape.Line);
}
