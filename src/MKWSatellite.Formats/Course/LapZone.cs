using System.Numerics;

namespace MKWSatellite.Formats.Course;

/// <summary>
/// Shape of the lap progress zone between two gates, following <c>RailPoint_IsInSegmentVolume</c> (1.8 0x71007F34D0).
/// A position p is on segment a→b at t = d_a / (d_a - d_b) (d = signed distance to each gate plane), so the points
/// with a given t form the plane (1-t)·F_a·(p - c_a) + t·F_b·(p - c_b) = 0. At that t the game lerps the centre and
/// half-extents and slerps the gate rotation, then requires |R(t)·(p - c(t))| ≤ w(t) and U(t)·(p - c(t)) ≥ -h(t).
/// On a bend the slerped sides bow outward past the straight lines between the gate corners, unless
/// <see cref="LapPoint.LerpType"/> makes a side straight. Each cross-section is found by intersecting the t-plane with
/// the side and floor/ceiling planes.
/// </summary>
public static class LapZone
{
    /// <summary>
    /// Cross-sections of the zone from gate a (t = 0) to gate b (t = 1), each as four corners in the order of
    /// <see cref="LapPoint.GateCorners"/> (bottom-left, bottom-right, top-right, top-left). The top is drawn at +h for
    /// reference; the lap tracker does not bound the zone from above.
    /// </summary>
    public static List<Vector3[]> Sections(LapPoint a, LapPoint b, float maxStepDegrees = 3f, int maxSteps = 32)
    {
        var fa = a.GateFrame();
        var fb = b.GateFrame();
        var dot = MathF.Abs(Quaternion.Dot(fa.Rotation, fb.Rotation));
        var angle = 2f * MathF.Acos(Math.Clamp(dot, 0f, 1f)) * (180f / MathF.PI);
        var steps = Math.Clamp((int)MathF.Ceiling(angle / maxStepDegrees), 1, maxSteps);
        var ma = a.LerpType;
        var mb = b.LerpType;
        var straightPlus = SideStraight(ma, +1) && SideStraight(mb, +1);
        var straightMinus = SideStraight(ma, -1) && SideStraight(mb, -1);

        var ga = a.GateCorners();
        var gb = b.GateCorners();
        var result = new List<Vector3[]>(steps + 1);
        for (var i = 0; i <= steps; i++)
        {
            var t = (float)i / steps;
            result.Add(Section(fa, fb, t, straightMinus, straightPlus) ?? Lerp(ga, gb, t));
        }
        return result;
    }

    /// <summary>True when <paramref name="lerpType"/> makes the given side (+1 = +right, -1 = -right) straight.</summary>
    public static bool SideStraight(int lerpType, int side) => lerpType == 1 || (side > 0 ? lerpType == 2 : lerpType == 3);

    private static Vector3[]? Section(
        (Vector3 Centre, Quaternion Rotation, float HalfWidth, float HalfHeight) a,
        (Vector3 Centre, Quaternion Rotation, float HalfWidth, float HalfHeight) b,
        float t, bool straightMinus, bool straightPlus)
    {
        var fwdA = Vector3.Transform(Vector3.UnitZ, a.Rotation);
        var fwdB = Vector3.Transform(Vector3.UnitZ, b.Rotation);
        var rightA = Vector3.Transform(Vector3.UnitX, a.Rotation);
        var rightB = Vector3.Transform(Vector3.UnitX, b.Rotation);
        var q = Quaternion.Slerp(a.Rotation, b.Rotation, t);
        var right = Vector3.Transform(Vector3.UnitX, q);
        var up = Vector3.Transform(Vector3.UnitY, q);
        var c = Vector3.Lerp(a.Centre, b.Centre, t);
        var w = a.HalfWidth + (b.HalfWidth - a.HalfWidth) * t;
        var h = a.HalfHeight + (b.HalfHeight - a.HalfHeight) * t;

        // Points at parameter t.
        var nT = (1 - t) * fwdA + t * fwdB;
        var dT = (1 - t) * Vector3.Dot(fwdA, a.Centre) + t * Vector3.Dot(fwdB, b.Centre);

        (Vector3 N, float D) Side(int s, bool straight)
        {
            if (straight)
            {
                // Plane through both gates' edges containing the interpolated up axis, facing outward.
                var ea = a.Centre + s * a.HalfWidth * rightA;
                var eb = b.Centre + s * b.HalfWidth * rightB;
                var n = Vector3.Cross(eb - ea, up);
                if (n.LengthSquared() > 1e-12f)
                {
                    n = Vector3.Normalize(n);
                    if (Vector3.Dot(n, s * rightA) < 0) n = -n;
                    return (n, Vector3.Dot(n, ea));
                }
            }
            var ns = s * right;
            return (ns, Vector3.Dot(ns, c) + w);
        }

        var (nl, dl) = Side(-1, straightMinus);
        var (nr, dr) = Side(+1, straightPlus);
        var dBottom = Vector3.Dot(up, c) - h;
        var dTop = Vector3.Dot(up, c) + h;

        var bl = Intersect(nT, dT, nl, dl, up, dBottom);
        var br = Intersect(nT, dT, nr, dr, up, dBottom);
        var tr = Intersect(nT, dT, nr, dr, up, dTop);
        var tl = Intersect(nT, dT, nl, dl, up, dTop);
        if (bl is null || br is null || tr is null || tl is null) return null;
        return [bl.Value, br.Value, tr.Value, tl.Value];
    }

    /// <summary>Point on three planes n·p = d, or null when they do not meet in a single point.</summary>
    private static Vector3? Intersect(Vector3 n1, float d1, Vector3 n2, float d2, Vector3 n3, float d3)
    {
        var c23 = Vector3.Cross(n2, n3);
        var det = Vector3.Dot(n1, c23);
        if (MathF.Abs(det) < 1e-6f * n1.Length() * n2.Length() * n3.Length()) return null;
        return (d1 * c23 + d2 * Vector3.Cross(n3, n1) + d3 * Vector3.Cross(n1, n2)) / det;
    }

    private static Vector3[] Lerp(Vector3[] a, Vector3[] b, float t) =>
        [Vector3.Lerp(a[0], b[0], t), Vector3.Lerp(a[1], b[1], t), Vector3.Lerp(a[2], b[2], t), Vector3.Lerp(a[3], b[3], t)];
}
