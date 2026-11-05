using System.Numerics;
using ImGuiNET;
using MKWSatellite.App.Rendering;
using MKWSatellite.Formats.Course;

namespace MKWSatellite.App.View;

/// <summary>Something under the cursor: an object or a lap-path node.</summary>
public abstract record Pickable(Vector3 Position);
public sealed record PickedObject(CourseObject Object) : Pickable(Object.Position);
public sealed record PickedLapPoint(LapPoint Point) : Pickable(Point.Position);
public sealed record PickedRailPoint(CourseRail Rail, int Index) : Pickable(Rail.Rail.Points[Index].Translation);

/// <summary>Display toggles. Each maps to a group of things drawn.</summary>
public sealed class ViewLayers
{
    public bool LapPath = true, CheckpointGates = true, AllGates = false, CheckpointLabels = true;
    public bool AiRoutes = true, OtherRails = true;
    public bool ItemBoxes = true, Coins = true, Boosts = true, StartGrid = true;
    public bool RespawnAreas = true, RecoveryPoints = true, ValleyAreas = true, HighSpeedAreas = true, BoostAreas = true, RuleAreas = true, LinkAreas = false, OtherAreas = false;
    public bool Hazards = true, Fences = false, Audience = false, Scenery = false;
    public bool LayerRace = true, LayerWorld = true, LayerCourseFix = true;
    /// <summary>Colour the lap path by height instead of a flat colour.</summary>
    public bool HeightColors = true;
}

/// <summary>
/// Builds and draws the top-down 2D course view: paths, gates, areas and object markers.
/// Markers have a minimum on-screen size, so geometry is rebuilt whenever the zoom changes.
/// </summary>
public sealed class CourseView(PrimitiveBatch batch)
{
    public CourseScene? Course { get; private set; }
    public ViewLayers Layers { get; } = new();
    public Pickable? Selected { get; set; }
    public Pickable? Hovered { get; private set; }

    private float _builtZoom = -1;
    private bool _dirty = true;
    private float _minY, _maxY;
    private readonly List<(Pickable Item, float Radius)> _pickables = [];

    public void SetCourse(CourseScene course, TopDownCamera camera)
    {
        Course = course;
        Selected = null;
        var lap = course.LapPoints.Select(p => p.Position).ToList();
        var pts = lap.Count > 0 ? lap : course.Objects.Select(o => o.Position).ToList();
        _minY = pts.Count > 0 ? pts.Min(p => p.Y) : 0;
        _maxY = pts.Count > 0 ? pts.Max(p => p.Y) : 1;
        FrameCourse(camera);
        _dirty = true;
    }

    public void FrameCourse(TopDownCamera camera)
    {
        if (Course is null) return;
        var (min, max) = Course.PathBounds;
        camera.Frame(new Vector2(min.X, min.Z), new Vector2(max.X, max.Z));
    }

    public void Invalidate() => _dirty = true;

    public void Draw(TopDownCamera camera)
    {
        if (Course is null) return;
        if (_dirty || MathF.Abs(camera.PixelsPerUnit - _builtZoom) > _builtZoom * 0.02f)
        {
            Build(camera.UnitsPerPixel);
            _builtZoom = camera.PixelsPerUnit;
            _dirty = false;
        }
        batch.Draw(camera.ViewProjection);
    }

    private bool LayerOn(CourseLayer l) => l switch
    {
        CourseLayer.Race => Layers.LayerRace,
        CourseLayer.World => Layers.LayerWorld,
        _ => Layers.LayerCourseFix,
    };

    private bool CategoryOn(ObjectCategory c) => c switch
    {
        ObjectCategory.ItemBox => Layers.ItemBoxes,
        ObjectCategory.Coin => Layers.Coins,
        ObjectCategory.DashPanel or ObjectCategory.GlideRamp or ObjectCategory.BoostRing or ObjectCategory.JumpRamp => Layers.Boosts,
        ObjectCategory.StartPosition or ObjectCategory.LapGate => Layers.StartGrid,
        ObjectCategory.RespawnArea => Layers.RespawnAreas,
        ObjectCategory.ValleyArea => Layers.ValleyAreas,
        ObjectCategory.RecoveryPoint => Layers.RecoveryPoints,
        ObjectCategory.BoostArea => Layers.BoostAreas,
        ObjectCategory.RuleArea => Layers.RuleAreas,
        ObjectCategory.LinkArea => Layers.LinkAreas,
        ObjectCategory.HighSpeedArea => Layers.HighSpeedAreas,
        ObjectCategory.OtherArea => Layers.OtherAreas,
        ObjectCategory.Hazard => Layers.Hazards,
        ObjectCategory.Fence => Layers.Fences,
        ObjectCategory.Audience => Layers.Audience,
        _ => Layers.Scenery,
    };

    public bool IsVisible(CourseObject o) => LayerOn(o.Layer) && CategoryOn(o.Category);

    private void Build(float upp)
    {
        var c = Course!;
        batch.Clear();
        _pickables.Clear();

        // 1. Areas (filled, underneath everything)
        foreach (var o in c.Objects.Where(o => o.IsArea && IsVisible(o)))
        {
            var outline = AreaFootprint(o);
            var col = Palette.ForCategory(o.Category);
            for (var i = 1; i + 1 < outline.Length; i++) batch.Triangle(outline[0], outline[i], outline[i + 1], Palette.WithAlpha(col, 0x38));
            for (var i = 0; i < outline.Length; i++) ThickLine(outline[i], outline[(i + 1) % outline.Length], 1.2f * upp, Palette.WithAlpha(col, 0xC0));
            _pickables.Add((new PickedObject(o), 0));
        }

        // 2. Rails
        foreach (var r in c.Rails.Where(r => LayerOn(r.Layer)))
        {
            var isLap = r.Kind == "LapPathGraph";
            var isRoute = r.Kind == "RoutePathGraph";
            if (isLap ? !Layers.LapPath : isRoute ? !Layers.AiRoutes : !Layers.OtherRails) continue;
            if (!isLap && !isRoute && !Palette.ShowRail(r.Kind)) continue;
            var width = (isLap ? 3.5f : isRoute ? 1.2f : 2f) * upp;
            var pts = r.Rail.Points;
            void Seg(int i, Vector3 a, Vector3 b)
            {
                var col = isLap && Layers.HeightColors ? Palette.Height((a.Y + b.Y) / 2, _minY, _maxY) : Palette.ForRail(r.Kind);
                ThickLine(a, b, width, col);
            }
            for (var i = 1; i < pts.Count; i++) Seg(i, pts[i - 1].Translation, pts[i].Translation);
            if (r.Rail.IsClosed && pts.Count > 2) Seg(0, pts[^1].Translation, pts[0].Translation);
            foreach (var p in pts)
                foreach (var dst in p.LinkTargets)
                    if (c.PointsByHash.TryGetValue(dst, out var q))
                        Seg(-1, p.Translation, q.Translation);
            if (!isLap)
                for (var i = 0; i < pts.Count; i++) _pickables.Add((new PickedRailPoint(r, i), 4 * upp));
        }

        // 3. Lap gates (vertical rectangles: from above they are a line between the bottom corners)
        if (Layers.LapPath)
            foreach (var lp in c.LapPoints)
            {
                var isCp = lp.CheckPoint is not null || lp.IsStart || lp.IsGoal || lp.IsLapCheck;
                if (isCp ? !Layers.CheckpointGates : !Layers.AllGates)
                {
                    _pickables.Add((new PickedLapPoint(lp), 5 * upp));
                    continue;
                }
                var g = lp.GateCorners();
                var col = lp.IsStart || lp.IsGoal ? Palette.StartGate : lp.IsLapCheck ? Palette.LapCheckGate : isCp ? Palette.CheckpointGate : Palette.Gate;
                ThickLine(g[0], g[1], (isCp ? 2.5f : 1.2f) * upp, col);
                Disc(lp.Position, 2.5f * upp, col, 8);
                _pickables.Add((new PickedLapPoint(lp), 6 * upp));
            }

        // 4. Object markers
        foreach (var o in c.Objects.Where(o => !o.IsArea && IsVisible(o)))
        {
            var col = Palette.ForCategory(o.Category);
            var p = o.Position;
            var fwd = Vector3.TransformNormal(Vector3.UnitZ, o.Rotation);
            var px = MathF.Max(upp, 0.0001f);
            switch (o.Category)
            {
                case ObjectCategory.ItemBox:
                    Square(p, MathF.Max(2.5f, 5 * px), col);
                    break;
                case ObjectCategory.Coin:
                    Disc(p, MathF.Max(0.8f, 2.5f * px), col, 8);
                    break;
                case ObjectCategory.BoostRing:
                    // Ring facing is not consistent with local +Z in the data, so no arrow.
                    Ring(p, MathF.Max(4f, 6 * px), 2 * px, col);
                    break;
                case ObjectCategory.DashPanel or ObjectCategory.GlideRamp or ObjectCategory.JumpRamp:
                    Arrow(p, fwd, MathF.Max(6f, 11 * px), col);
                    break;
                case ObjectCategory.StartPosition or ObjectCategory.LapGate:
                    Arrow(p, fwd, MathF.Max(3f, 7 * px), col);
                    break;
                case ObjectCategory.RecoveryPoint:
                    Ring(p, MathF.Max(3f, 6 * px), 2 * px, col);
                    Disc(p, MathF.Max(1f, 2 * px), col, 8);
                    break;
                case ObjectCategory.Hazard:
                    var s = MathF.Max(3f, 5 * px);
                    ThickLine(p + new Vector3(-s, 0, -s), p + new Vector3(s, 0, s), 1.5f * px, col);
                    ThickLine(p + new Vector3(-s, 0, s), p + new Vector3(s, 0, -s), 1.5f * px, col);
                    break;
                default:
                    Disc(p, MathF.Max(0.6f, 1.8f * px), col, 6);
                    break;
            }
            _pickables.Add((new PickedObject(o), 6 * px));
        }

        // 5. Selection highlight
        if (Selected is not null) Ring(Selected.Position, 9 * upp, 1.5f * upp, Palette.Selection);

        batch.Upload();
    }

    /// <summary>
    /// Top-down outline (convex polygon) of an area: a unit cube, or for Sphere/Cylinder primitives a unit-diameter
    /// circle, scaled by Scale, rotated, at Translate. The 1-unit base size is inferred from the data (e.g. Rainbow's
    /// delete areas have scales in the hundreds to thousands), not verified in code.
    /// </summary>
    public static Vector3[] AreaFootprint(CourseObject o)
    {
        var m = o.Rotation;
        var s = o.Actor.Scale / 2;
        Vector3 P(float x, float z) => o.Position + Vector3.TransformNormal(new Vector3(x * s.X, 0, z * s.Z), m);
        if (o.Primitive is "Sphere" or "Cylinder" or "Capsule")
            return Enumerable.Range(0, 24).Select(i => P(MathF.Cos(i * MathF.Tau / 24), MathF.Sin(i * MathF.Tau / 24))).ToArray();
        return [P(-1, -1), P(1, -1), P(1, 1), P(-1, 1)];
    }

    // --- primitives (all flat in X/Z) ---

    private void ThickLine(Vector3 a, Vector3 b, float width, uint color)
    {
        var d = new Vector2(b.X - a.X, b.Z - a.Z);
        if (d.LengthSquared() < 1e-8f) return;
        var n = Vector2.Normalize(new Vector2(-d.Y, d.X)) * (width / 2);
        var o = new Vector3(n.X, 0, n.Y);
        batch.Quad(a - o, b - o, b + o, a + o, color);
    }

    private void Disc(Vector3 c, float r, uint color, int segments)
    {
        for (var i = 0; i < segments; i++)
        {
            var a0 = i * MathF.Tau / segments;
            var a1 = (i + 1) * MathF.Tau / segments;
            batch.Triangle(c, c + new Vector3(MathF.Cos(a0) * r, 0, MathF.Sin(a0) * r), c + new Vector3(MathF.Cos(a1) * r, 0, MathF.Sin(a1) * r), color);
        }
    }

    private void Ring(Vector3 c, float r, float width, uint color)
    {
        const int n = 24;
        for (var i = 0; i < n; i++)
        {
            var a0 = i * MathF.Tau / n;
            var a1 = (i + 1) * MathF.Tau / n;
            ThickLine(c + new Vector3(MathF.Cos(a0) * r, 0, MathF.Sin(a0) * r), c + new Vector3(MathF.Cos(a1) * r, 0, MathF.Sin(a1) * r), width, color);
        }
    }

    private void Square(Vector3 c, float half, uint color) =>
        batch.Quad(c + new Vector3(-half, 0, -half), c + new Vector3(half, 0, -half), c + new Vector3(half, 0, half), c + new Vector3(-half, 0, half), color);

    /// <summary>Arrow pointing along the object's forward (local +Z) projected onto X/Z.</summary>
    private void Arrow(Vector3 c, Vector3 forward, float length, uint color)
    {
        var f = new Vector2(forward.X, forward.Z);
        f = f.LengthSquared() < 1e-6f ? new Vector2(0, -1) : Vector2.Normalize(f);
        var side = new Vector2(-f.Y, f.X);
        Vector3 P(Vector2 v) => c + new Vector3(v.X, 0, v.Y);
        var tip = f * length * 0.6f;
        var back = -f * length * 0.4f;
        batch.Triangle(P(tip), P(back + side * length * 0.38f), P(back * 0.3f), color);
        batch.Triangle(P(tip), P(back * 0.3f), P(back - side * length * 0.38f), color);
    }

    // --- picking and labels ---

    public Pickable? PickAt(TopDownCamera camera, Vector2 screen)
    {
        var world = camera.ScreenToWorld(screen);
        Pickable? best = null;
        var bestD = float.MaxValue;
        foreach (var (item, radius) in _pickables)
        {
            float d;
            if (item is PickedObject { Object.IsArea: true } a)
                d = PointInQuad(world, AreaFootprint(a.Object)) ? 0.5f * (8 * camera.UnitsPerPixel) : float.MaxValue; // areas lose to point objects
            else
                d = Vector2.Distance(world, new Vector2(item.Position.X, item.Position.Z)) - radius;
            var limit = 8 * camera.UnitsPerPixel;
            if (d < limit && d < bestD) { best = item; bestD = d; }
        }
        return best;
    }

    public void UpdateHover(TopDownCamera camera, Vector2 mouse, bool mouseInViewport) =>
        Hovered = mouseInViewport ? PickAt(camera, mouse) : null;

    private static bool PointInQuad(Vector2 p, Vector3[] q)
    {
        var inside = false;
        for (int i = 0, j = q.Length - 1; i < q.Length; j = i++)
        {
            var (xi, zi, xj, zj) = (q[i].X, q[i].Z, q[j].X, q[j].Z);
            if (zi > p.Y != zj > p.Y && p.X < (xj - xi) * (p.Y - zi) / (zj - zi) + xi) inside = !inside;
        }
        return inside;
    }

    /// <summary>Checkpoint numbers, drawn with ImGui on top of the GL view.</summary>
    public void DrawLabels(TopDownCamera camera, ImDrawListPtr dl)
    {
        if (Course is null || !Layers.LapPath || !Layers.CheckpointLabels) return;
        foreach (var lp in Course.LapPoints)
        {
            string? text = lp.IsStart ? "START" : lp.IsGoal ? "GOAL" : lp.CheckPoint is { } cp ? cp.ToString("0") : null;
            if (lp.IsLapCheck) text = (text is null ? "" : text + " ") + "LAP";
            if (text is null) continue;
            var s = camera.WorldToScreen(lp.Position) + new Vector2(6, -16);
            dl.AddText(s + new Vector2(1, 1), 0xFF000000, text);
            dl.AddText(s, 0xFFFFFFFF, text);
        }
    }
}
