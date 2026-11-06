using System.Numerics;
using MKWSatellite.Formats.Byml;
using MKWSatellite.Formats.Course;

namespace MKWSatellite.Formats.Tests;

public class LapZoneTests
{
    private static LapPoint Gate(Vector3 pos, float yaw, float halfWidth, float halfHeight, int lerpType = 0, float offsetX = 0)
    {
        var raw = new BymlDict(2)
        {
            ["RectSize"] = new BymlArray(2) { halfWidth, halfHeight },
            ["RectOffset"] = new BymlArray(2) { offsetX, 0f },
        };
        BymlDict? dyn = null;
        if (lerpType != 0) dyn = new BymlDict(1) { ["LerpType"] = lerpType };
        return new LapPoint(new BancRailPoint(0, pos, new Vector3(0, yaw, 0), dyn, [], raw), null!, 0);
    }

    /// <summary>
    /// Port of RailPoint_IsInSegmentVolume (1.8 0x71007F34D0) with the t of RailPoint_FindSegmentContaining
    /// (0x7100AAD950), margin 0, no upper bound. Written from the decompiled code, independently of LapZone.
    /// </summary>
    private static bool GameInZone(LapPoint a, LapPoint b, Vector3 p)
    {
        var ma = CourseMath.EulerXyz(a.Point.Rotate);
        var mb = CourseMath.EulerXyz(b.Point.Rotate);
        Vector3 Row(Matrix4x4 m, Vector3 axis) => Vector3.TransformNormal(axis, m);
        var ca = a.GateCentre;
        var cb = b.GateCentre;
        var da = Vector3.Dot(Row(ma, Vector3.UnitZ), p - ca);
        var db = Vector3.Dot(Row(mb, Vector3.UnitZ), p - cb);
        if (da - db <= 0) return false; // the game skips the segment (0x7100AAD950)
        var t = da / (da - db);
        if (t < 0 || t > 1) return false;

        var q = Quaternion.Slerp(Quaternion.CreateFromRotationMatrix(ma), Quaternion.CreateFromRotationMatrix(mb), t);
        var right = Vector3.Transform(Vector3.UnitX, q);
        var up = Vector3.Transform(Vector3.UnitY, q);
        var c = Vector3.Lerp(ca, cb, t);
        var d = p - c;
        var lat = Vector3.Dot(right, d);
        var side = lat < 0 ? -1 : 1;
        bool Flag(int mode) => mode == 1 || (side < 0 ? mode == 3 : mode == 2);
        var lateralOk = false;
        var straightDone = false;
        if (Flag(a.LerpType) && Flag(b.LerpType))
        {
            var ea = ca + side * a.RectSize.X * Row(ma, Vector3.UnitX);
            var eb = cb + side * b.RectSize.X * Row(mb, Vector3.UnitX);
            var n = Vector3.Cross(eb - ea, up);
            if (n.Length() > 0)
            {
                n = Vector3.Normalize(n);
                if (side * Vector3.Dot(n, Row(ma, Vector3.UnitX)) < 0) n = -n;
                lateralOk = Vector3.Dot(n, p - ea) <= 0;
                straightDone = true;
            }
        }
        if (!straightDone) lateralOk = MathF.Abs(lat) <= a.RectSize.X + (b.RectSize.X - a.RectSize.X) * t;
        if (!lateralOk) return false;
        return Vector3.Dot(up, d) >= -(a.RectSize.Y + (b.RectSize.Y - a.RectSize.Y) * t);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(2, 0)] // only one gate asks: both sides stay curved
    public void SectionBottomCornersLieOnTheGameBoundary(int lerpA, int lerpB)
    {
        // A 70 degree bend with different gate widths and an offset centre.
        var a = Gate(new Vector3(0, 10, 0), 0f, 30, 20, lerpA, offsetX: 5);
        var b = Gate(new Vector3(40, 14, 60), 70f * MathF.PI / 180f, 45, 25, lerpB);
        var sections = LapZone.Sections(a, b);
        Assert.True(sections.Count > 2);
        for (var k = 1; k < sections.Count - 1; k++)
        {
            var s = sections[k];
            var mid = (s[0] + s[1] + s[2] + s[3]) / 4;
            foreach (var corner in new[] { s[0], s[1] })
            {
                var inside = Vector3.Lerp(corner, mid, 0.01f);
                var outside = Vector3.Lerp(corner, mid, -0.01f);
                Assert.True(GameInZone(a, b, inside), $"section {k}: {inside} should be inside");
                Assert.False(GameInZone(a, b, outside), $"section {k}: {outside} should be outside");
            }
        }
    }

    [Fact]
    public void EndSectionsAreTheGates()
    {
        var a = Gate(new Vector3(0, 0, 0), 0f, 30, 20);
        var b = Gate(new Vector3(40, 0, 60), 1.2f, 45, 25);
        var s = LapZone.Sections(a, b);
        var ga = a.GateCorners();
        var gb = b.GateCorners();
        for (var i = 0; i < 4; i++)
        {
            Assert.True(Vector3.Distance(s[0][i], ga[i]) < 1e-3f, $"{s[0][i]} vs {ga[i]}");
            Assert.True(Vector3.Distance(s[^1][i], gb[i]) < 1e-3f, $"{s[^1][i]} vs {gb[i]}");
        }
    }

    /// <summary>
    /// Hairpin: the inner gate edges reach past the line where the two gate planes cross. The game skips the segment
    /// there (d_a - d_b &lt;= 0 in RailPoint_FindSegmentContaining), so no drawn corner may lie beyond that line, and the
    /// port must reject points just past it.
    /// </summary>
    [Fact]
    public void HairpinCornersStopWhereTheGatePlanesCross()
    {
        var a = Gate(new Vector3(0, 0, 0), 0f, 30, 10);
        var b = Gate(new Vector3(20, 0, 10), 1.6f, 30, 10);
        var fa = Vector3.TransformNormal(Vector3.UnitZ, CourseMath.EulerXyz(a.Point.Rotate));
        var fb = Vector3.TransformNormal(Vector3.UnitZ, CourseMath.EulerXyz(b.Point.Rotate));
        float Den(Vector3 p) => Vector3.Dot(fa, p - a.GateCentre) - Vector3.Dot(fb, p - b.GateCentre);
        var clipped = 0;
        foreach (var s in LapZone.Sections(a, b))
            foreach (var c in s)
            {
                Assert.True(Den(c) > -1e-2f, $"corner {c} is past the crossing line (d_a - d_b = {Den(c)})");
                if (MathF.Abs(Den(c)) >= 1e-2f) continue;
                clipped++;
                Assert.False(LapZone.Contains(a, b, c - 0.5f * Vector3.Normalize(fa - fb)), "point past the crossing line accepted");
                Assert.False(GameInZone(a, b, c - 0.5f * Vector3.Normalize(fa - fb)), "point past the crossing line accepted (test port)");
            }
        Assert.True(clipped > 0, "expected the hairpin to clip some corners");
        Assert.True(GameInZone(a, b, new Vector3(5, 1, 5)) == LapZone.Contains(a, b, new Vector3(5, 1, 5)));
    }

    [Fact]
    public void BendBowsOutwardUnlessLerpTypeMakesItStraight()
    {
        static float MidWidth(int lerp)
        {
            var a = Gate(new Vector3(0, 0, 0), 0f, 30, 20, lerp);
            var b = Gate(new Vector3(40, 0, 60), 1.2f, 30, 20, lerp);
            var s = LapZone.Sections(a, b);
            var m = s[s.Count / 2];
            return Vector3.Distance(m[0], m[1]);
        }
        var chord = MidWidth(1);
        Assert.True(MidWidth(0) > chord * 1.05f, $"curved {MidWidth(0)} vs straight {chord}");
    }
}
