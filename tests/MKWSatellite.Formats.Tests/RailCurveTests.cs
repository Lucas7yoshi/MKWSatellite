using System.Numerics;
using MKWSatellite.Formats.Byml;
using MKWSatellite.Formats.Course;

namespace MKWSatellite.Formats.Tests;

/// <summary>Segment shapes as traced in 1.8 (RailSegment_SetCurveType 0x7100980E00, RailSegment_Evaluate 0x710070AC60).</summary>
public class RailCurveTests
{
    private static BancRailPoint Node(Vector3 pos, Vector3? c0 = null, Vector3? c1 = null)
    {
        var raw = new BymlDict(2);
        if (c0 is { } a) raw["Control0"] = new BymlArray(3) { a.X, a.Y, a.Z };
        if (c1 is { } b) raw["Control1"] = new BymlArray(3) { b.X, b.Y, b.Z };
        return new BancRailPoint(0, pos, Vector3.Zero, null, [], raw);
    }

    [Fact]
    public void MissingHandles_AreTheNodePosition_StraightSegment()
    {
        var a = Node(new Vector3(0, 0, 0));
        var b = Node(new Vector3(10, 0, 0), c0: new Vector3(10.0005f, 0, 0)); // within the game's 0.001 tolerance
        Assert.Equal(a.Translation, a.Control1);
        Assert.Equal(RailCurve.Shape.Line, RailCurve.ShapeOf(a, b));
        Assert.Equal(new Vector3(5, 0, 0), RailCurve.Evaluate(a, b, 0.5f));
    }

    [Fact]
    public void ShapeUsesStartControl1AndEndControl0()
    {
        var p0 = new Vector3(0, 0, 0);
        var p1 = new Vector3(10, 0, 0);
        // The start node's Control0 and the end node's Control1 belong to the neighbouring segments.
        Assert.Equal(RailCurve.Shape.Line, RailCurve.ShapeOf(Node(p0, c0: new Vector3(-5, 0, 0)), Node(p1, c1: new Vector3(15, 0, 0))));
        Assert.Equal(RailCurve.Shape.StartHandle, RailCurve.ShapeOf(Node(p0, c1: new Vector3(0, 0, 5)), Node(p1)));
        Assert.Equal(RailCurve.Shape.EndHandle, RailCurve.ShapeOf(Node(p0), Node(p1, c0: new Vector3(10, 0, 5))));
        Assert.Equal(RailCurve.Shape.Cubic, RailCurve.ShapeOf(Node(p0, c1: new Vector3(0, 0, 5)), Node(p1, c0: new Vector3(10, 0, 5))));
    }

    [Fact]
    public void BezierMidpoints()
    {
        var a = Node(Vector3.Zero, c1: new Vector3(0, 0, 8));
        var b = Node(new Vector3(10, 0, 0), c0: new Vector3(10, 0, 8));
        // Cubic: 1/8 P0 + 3/8 C0 + 3/8 C1 + 1/8 P1.
        Assert.True(Vector3.Distance(RailCurve.Evaluate(a, b, 0.5f), new Vector3(5, 0, 6)) < 1e-4f);
        // Quadratic through the start handle only: 1/4 P0 + 1/2 C + 1/4 P1.
        var c = Node(new Vector3(10, 0, 0));
        Assert.True(Vector3.Distance(RailCurve.Evaluate(a, c, 0.5f), new Vector3(2.5f, 0, 4)) < 1e-4f);
        var pts = new List<Vector3>();
        RailCurve.Sample(a, b, pts);
        Assert.Equal(a.Translation, pts[0]);
        Assert.Equal(b.Translation, pts[^1]);
    }
}
