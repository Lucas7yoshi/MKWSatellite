using System.Numerics;

namespace MKWSatellite.Formats.Course;

/// <summary>
/// Where a Bullet Bill ("Killer") is not allowed to end on an AI route (<c>RoutePathGraph</c>), from the 1.8 route
/// follower (<c>RouteFollower_Update</c>, 0x71006C24D0) and its look-ahead builder (0x7100BEFA80). See
/// docs/formats/banc.md. When the item would end, the game:
/// <list type="number">
/// <item>ends it anyway after about 840 frames;</item>
/// <item>keeps going if the last point passed has <c>IsForbidKillerFinish</c> (until the next point);</item>
/// <item>keeps going if the current turn value is above <see cref="NearTurn"/>;</item>
/// <item>looks at the next route points: keeps going if one within <see cref="NearAhead"/> units turns more than
/// <see cref="NearTurn"/> or has <c>ItemType</c> 1 or 2, or one within <see cref="FarAhead"/> turns more than
/// <see cref="FarTurn"/>; ends once the look-ahead passes <see cref="FarAhead"/> units.</item>
/// </list>
/// A point's turn is the heading change from it to the next point (the angle between the two points' forward axes
/// around the first one's up axis, from <c>Rotate</c>) in degrees, divided by the distance between them.
/// </summary>
public static class BulletBillRules
{
    public const float NearAhead = 30f, FarAhead = 50f, NearTurn = 0.45f, FarTurn = 2f;
    public const int TimeoutFrames = 840;

    /// <summary>Turn (degrees per unit) of the segment from point <c>i</c> to the next; 0 for the last point of an open rail.</summary>
    public static float[] TurnRates(IReadOnlyList<BancRailPoint> pts, bool closed)
    {
        var t = new float[pts.Count];
        for (var i = 0; i < pts.Count; i++)
        {
            if (Next(i, pts.Count, closed) is not { } n) continue;
            var len = Vector3.Distance(pts[i].Translation, pts[n].Translation);
            if (len < 1e-6f) continue;
            var r1 = CourseMath.EulerXyz(pts[i].Rotate);
            var f1 = Vector3.TransformNormal(Vector3.UnitZ, r1);
            var u1 = Vector3.TransformNormal(Vector3.UnitY, r1);
            var f2 = Vector3.TransformNormal(Vector3.UnitZ, CourseMath.EulerXyz(pts[n].Rotate));
            var sin = Vector3.Dot(u1, Vector3.Cross(f2, f1));
            var cos = Vector3.Dot(f1, f2) - Vector3.Dot(f2, u1) * Vector3.Dot(f1, u1);
            t[i] = MathF.Atan2(sin, cos) * (180f / MathF.PI) / len;
        }
        return t;
    }

    /// <summary>
    /// For each segment <c>i</c> (point <c>i</c> to the next), the parts (fractions 0..1 along it) where the look-ahead
    /// stops a Bullet Bill from ending. Follows the rail only (not links to other rails).
    /// </summary>
    public static List<(int Segment, float From, float To)> LookAheadSpans(IReadOnlyList<BancRailPoint> pts, bool closed)
    {
        var spans = new List<(int, float, float)>();
        var turn = TurnRates(pts, closed);
        var n = pts.Count;
        for (var k = 0; k < n; k++)
        {
            if (Next(k, n, closed) is not { } j0) continue;
            var len = Vector3.Distance(pts[k].Translation, pts[j0].Translation);
            if (len < 1e-6f) continue;
            var parts = new List<(float From, float To)>();
            // Distance from fraction f along this segment to point j = (1 - f) * len + s, s = path length j0..j.
            int? j = j0;
            for (float s = 0; j is { } jj && s < FarAhead; )
            {
                var abs = MathF.Abs(turn[jj]);
                var item = pts[jj].Dynamic?.TryGetValue("ItemType", out var v) == true && v is int it && it is 1 or 2;
                if (abs > NearTurn || item) parts.Add((1 - (NearAhead - s) / len, 1));
                if (abs > FarTurn) parts.Add((1 - (FarAhead - s) / len, 1 - (NearAhead - s) / len));
                if (Next(jj, n, closed) is not { } nx || nx == j0) break;
                s += Vector3.Distance(pts[jj].Translation, pts[nx].Translation);
                j = nx;
            }
            foreach (var (from, to) in Merge(parts)) spans.Add((k, from, to));
        }
        return spans;
    }

    private static int? Next(int i, int count, bool closed) =>
        i + 1 < count ? i + 1 : closed && count > 2 ? 0 : null;

    private static IEnumerable<(float, float)> Merge(List<(float From, float To)> parts)
    {
        var clipped = parts.Select(p => (From: Math.Clamp(p.From, 0, 1), To: Math.Clamp(p.To, 0, 1)))
            .Where(p => p.To > p.From).OrderBy(p => p.From).ToList();
        for (var i = 0; i < clipped.Count; )
        {
            var (from, to) = clipped[i++];
            while (i < clipped.Count && clipped[i].From <= to) to = MathF.Max(to, clipped[i++].To);
            yield return (from, to);
        }
    }
}
