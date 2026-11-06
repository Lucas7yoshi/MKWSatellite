using System.Numerics;
using MKWSatellite.Formats.Byml;
using MKWSatellite.Formats.Course;

namespace MKWSatellite.Formats.Tests;

public class BulletBillRulesTests
{
    private static BancRailPoint P(float x, float z, float yawDeg) =>
        new(0, new Vector3(x, 0, z), new Vector3(0, yawDeg * MathF.PI / 180f, 0), null, [], new BymlDict(0));

    [Fact]
    public void StraightRouteHasNoSpans()
    {
        var pts = Enumerable.Range(0, 10).Select(i => P(0, i * 20, 0)).ToList();
        Assert.All(BulletBillRules.TurnRates(pts, false), t => Assert.Equal(0, t, 4));
        Assert.Empty(BulletBillRules.LookAheadSpans(pts, false));
    }

    [Fact]
    public void TurnIsDegreesPerUnit()
    {
        // 30 degree heading change over 15 units = 2 deg/unit; sign follows the up axis (left vs right).
        var t = BulletBillRules.TurnRates([P(0, 0, 0), P(0, 15, 30)], false);
        Assert.Equal(2f, MathF.Abs(t[0]), 3);
        Assert.Equal(0f, t[1]);
    }

    [Fact]
    public void TightCircleIsFullyCovered()
    {
        // 12 points on a radius-30 circle: ~15.5 units and 30 degrees per segment, about 1.9 deg/unit.
        var pts = Enumerable.Range(0, 12).Select(i =>
        {
            var a = i * MathF.Tau / 12;
            return P(30 * MathF.Sin(a), 30 * MathF.Cos(a), a * 180 / MathF.PI + 90);
        }).ToList();
        var spans = BulletBillRules.LookAheadSpans(pts, true);
        Assert.Equal(12, spans.Count);
        Assert.All(spans, s => { Assert.Equal(0, s.From, 3); Assert.Equal(1, s.To, 3); });
    }

    [Fact]
    public void LeadInBeforeASharpBendIsFiftyUnits()
    {
        // Straight 10-unit segments, then a sharp bend at point 10 (90 degrees over 10 units = 9 deg/unit, > FarTurn).
        var pts = Enumerable.Range(0, 11).Select(i => P(0, i * 10, 0)).Append(P(10, 100, 90)).ToList();
        var spans = BulletBillRules.LookAheadSpans(pts, false);
        // Within 50 units of point 10: segments 5..9 covered completely, nothing before.
        Assert.Equal([5, 6, 7, 8, 9], spans.Select(s => s.Segment).ToArray());
        Assert.All(spans, s => { Assert.Equal(0, s.From, 3); Assert.Equal(1, s.To, 3); });
    }

    [Fact]
    public void LookAheadReasonNamesTheTurningPointOnly()
    {
        var pts = Enumerable.Range(0, 11).Select(i => P(0, i * 10, 0)).Append(P(10, 100, 90)).ToList();
        Assert.Null(BulletBillRules.LookAheadReason(pts, false, 5));
        var why = BulletBillRules.LookAheadReason(pts, false, 10);
        Assert.NotNull(why);
        Assert.Contains("within 50 units", why);
    }
}
