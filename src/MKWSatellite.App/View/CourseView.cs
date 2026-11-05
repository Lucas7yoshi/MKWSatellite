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
    /// <summary>Bullet Bill route flags: no-finish stretches (<c>IsForbidKillerFinish</c>) and releasable points.</summary>
    public bool BulletZones = true;
    /// <summary>A dot on every path node (lap path, AI routes, other rails), to see what can be clicked.</summary>
    public bool Nodes = true;
    public bool ItemBoxes = true, Coins = true, Boosts = true, StartGrid = true;
    public bool RespawnAreas = true, RecoveryPoints = true, ValleyAreas = true, HighSpeedAreas = true, BoostAreas = true, RuleAreas = true, LinkAreas = false, OtherAreas = false;
    public bool Hazards = true, Fences = false, Audience = false, Scenery = false;
    public bool LayerRace = true, LayerWorld = true, LayerCourseFix = true;
    /// <summary>Colour the lap path by height instead of a flat colour.</summary>
    public bool HeightColors = true;
    /// <summary>Draw 3D models for visible objects; course geometry (<c>Stage_*</c> actors) has its own toggle.</summary>
    public bool Models = true, StageModels = true;
    /// <summary>Draw the 2D markers for objects that are shown as models.</summary>
    public bool MarkersOnModels = true;
    /// <summary>Multiplier for line widths and marker sizes.</summary>
    public float LineScale = 1f;
}

/// <summary>
/// Builds and draws the course overlay: paths, gates, areas and object markers, for the top-down camera (flat, in X/Z)
/// or the 3D camera (lines are camera-facing ribbons, gates are upright rectangles, areas are boxes). Sizes are given
/// in screen pixels, so geometry is rebuilt when the camera changes.
/// </summary>
public sealed class CourseView(PrimitiveBatch batch)
{
    public CourseScene? Course { get; private set; }
    public ViewLayers Layers { get; } = new();
    public Pickable? Selected { get; set; }
    public Pickable? Hovered { get; private set; }

    /// <summary>Set by the app: editor dummy shape of an actor type (<c>Cube_c</c>, <c>Cube_t</c>, <c>Sphere</c>...), if known.</summary>
    public Func<string, string?>? DummyShape { get; set; }
    /// <summary>Set by the app: whether an object's model is currently drawn (its 2D marker can then be hidden).</summary>
    public Func<CourseObject, bool>? HasModel { get; set; }

    private Matrix4x4 _builtView;
    private bool _dirty = true;
    private float _minY, _maxY;
    private readonly List<(Pickable Item, float RadiusPx)> _pickables = [];
    private readonly Dictionary<CourseRail, List<(int Segment, float From, float To)>> _lookAhead = new(ReferenceEqualityComparer.Instance);

    // Camera used for the current build.
    private IViewCamera _cam = null!;
    private OrbitCamera? _orbit;

    public void SetCourse(CourseScene course, TopDownCamera camera)
    {
        Course = course;
        Selected = null;
        _lookAhead.Clear();
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

    public void Draw(IViewCamera camera)
    {
        if (Course is null) return;
        var vp = camera.ViewProjection;
        if (_dirty || vp != _builtView || !ReferenceEquals(camera, _cam))
        {
            _cam = camera;
            _orbit = camera as OrbitCamera;
            Build();
            _builtView = vp;
            _dirty = false;
        }
        batch.Draw(vp);
    }

    /// <summary>Course geometry: the actors that carry a course's stage model (e.g. <c>Stage_Cinema_A_00</c>).</summary>
    public static bool IsStageModel(CourseObject o) => o.Type.StartsWith("Stage_", StringComparison.Ordinal);

    /// <summary>Whether an object's model should be drawn (areas and system objects have none).</summary>
    public bool ModelVisible(CourseObject o) =>
        Layers.Models && LayerOn(o.Layer) && !o.IsArea && (IsStageModel(o) ? Layers.StageModels : CategoryOn(o.Category));

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

    /// <summary>World units per screen pixel at a point (constant top-down; grows with distance in 3D), times the line scale.</summary>
    private float Upp(Vector3 p) =>
        (_orbit is { } o
            ? MathF.Max(Vector3.Distance(o.Eye, p), 0.5f) * 2 * MathF.Tan(o.FieldOfView / 2) / MathF.Max(1, o.ViewportSize.Y)
            : _cam.UnitsPerPixel) * Layers.LineScale;

    private void Build()
    {
        var c = Course!;
        batch.Clear();
        _pickables.Clear();
        var is3D = _orbit is not null;

        // 1. Areas: filled footprint top-down, boxes in 3D.
        foreach (var o in c.Objects.Where(o => o.IsArea && IsVisible(o)))
        {
            var col = Palette.ForCategory(o.Category);
            if (is3D) AreaBox(o, col);
            else
            {
                var outline = AreaFootprint(o);
                for (var i = 1; i + 1 < outline.Length; i++) batch.Triangle(outline[0], outline[i], outline[i + 1], Palette.WithAlpha(col, 0x38));
                for (var i = 0; i < outline.Length; i++) Line(outline[i], outline[(i + 1) % outline.Length], 1.5f, Palette.WithAlpha(col, 0xE0));
            }
            _pickables.Add((new PickedObject(o), 0));
        }

        // 2. Rails
        foreach (var r in c.Rails.Where(r => LayerOn(r.Layer)))
        {
            var isLap = r.Kind == "LapPathGraph";
            var isRoute = r.Kind == "RoutePathGraph";
            if (isLap ? !Layers.LapPath : isRoute ? !Layers.AiRoutes : !Layers.OtherRails) continue;
            if (!isLap && !isRoute && !Palette.ShowRail(r.Kind)) continue;
            var px = isLap ? 4f : isRoute ? 2f : 2.5f;
            var routeColor = isRoute ? Palette.ForRoute(r.Rail.Points.Count > 0 ? r.Rail.Points[0].Dynamic : null) : 0;
            var pts = r.Rail.Points;
            if (isRoute && Layers.BulletZones)
            {
                // Computed by the game from the route's shape (BulletBillRules): drawn first, flags on top.
                if (!_lookAhead.TryGetValue(r, out var spans)) _lookAhead[r] = spans = BulletBillRules.LookAheadSpans(pts, r.Rail.IsClosed);
                foreach (var (k, from, to) in spans)
                {
                    var a = pts[k].Translation;
                    var b = pts[k + 1 < pts.Count ? k + 1 : 0].Translation;
                    Line(Vector3.Lerp(a, b, from), Vector3.Lerp(a, b, to), 10, Palette.BulletLookAhead);
                }
                // The game keeps the IsForbidKillerFinish of the last route point the Bullet Bill passed until it
                // reaches the next one (RouteFollower_Update), so a flagged point covers the stretch after it.
                for (var i = 0; i < pts.Count; i++)
                {
                    if (pts[i].Dynamic?.Get<bool>("IsForbidKillerFinish") != true) continue;
                    var next = i + 1 < pts.Count ? pts[i + 1] : r.Rail.IsClosed && pts.Count > 2 ? pts[0] : null;
                    if (next is not null) Line(pts[i].Translation, next.Translation, 10, Palette.BulletNoFinish);
                    foreach (var dst in pts[i].LinkTargets)
                        if (c.PointsByHash.TryGetValue(dst, out var q)) Line(pts[i].Translation, q.Translation, 10, Palette.BulletNoFinish);
                    if (next is null && pts[i].LinkTargets.Count == 0) Dot(pts[i].Translation, 6, Palette.BulletNoFinish);
                }
            }
            void Seg(Vector3 a, Vector3 b)
            {
                var col = isLap ? (Layers.HeightColors ? Palette.Height((a.Y + b.Y) / 2, _minY, _maxY) : Palette.ForRail(r.Kind))
                    : isRoute ? routeColor : Palette.ForRail(r.Kind);
                Line(a, b, px, col);
                if (isRoute && Vector3.Distance(a, b) > 30 * Upp((a + b) / 2)) Chevron(a, b, col); // direction of travel
            }
            for (var i = 1; i < pts.Count; i++) Seg(pts[i - 1].Translation, pts[i].Translation);
            if (r.Rail.IsClosed && pts.Count > 2) Seg(pts[^1].Translation, pts[0].Translation);
            foreach (var p in pts)
                foreach (var dst in p.LinkTargets)
                    if (c.PointsByHash.TryGetValue(dst, out var q))
                        Seg(p.Translation, q.Translation);
            if (!isLap)
                for (var i = 0; i < pts.Count; i++)
                {
                    _pickables.Add((new PickedRailPoint(r, i), 4));
                    if (isRoute && Layers.BulletZones && pts[i].Dynamic?.Get<bool>("IsKillerReleasable") == true)
                        Dot(pts[i].Translation, 4.5f, Palette.BulletReleasable);
                    else if (Layers.Nodes)
                        Dot(pts[i].Translation, 2.5f, isRoute ? routeColor : Palette.ForRail(r.Kind));
                }
        }

        // 3. Lap gates: a line between the bottom corners top-down, the full upright rectangle in 3D.
        if (Layers.LapPath)
            foreach (var lp in c.LapPoints)
            {
                var isCp = lp.CheckPoint is not null || lp.IsStart || lp.IsGoal || lp.IsLapCheck;
                _pickables.Add((new PickedLapPoint(lp), 6));
                if (Layers.Nodes)
                    Dot(lp.Position, 3f, Layers.HeightColors ? Palette.Height(lp.Position.Y, _minY, _maxY) : Palette.ForRail("LapPathGraph"));
                if (isCp ? !Layers.CheckpointGates : !Layers.AllGates) continue;
                var g = lp.GateCorners();
                var col = lp.IsStart || lp.IsGoal ? Palette.StartGate : lp.IsLapCheck ? Palette.LapCheckGate : isCp ? Palette.CheckpointGate : Palette.Gate;
                if (is3D)
                {
                    batch.Quad(g[0], g[1], g[2], g[3], Palette.WithAlpha(col, isCp ? (byte)0x50 : (byte)0x28));
                    for (var i = 0; i < 4; i++) Line(g[i], g[(i + 1) % 4], isCp ? 3f : 1.5f, col);
                }
                else
                {
                    Line(g[0], g[1], isCp ? 5f : 2.5f, col);
                    Disc(lp.Position, 3f * Upp(lp.Position), col, 8);
                }
            }

        // 4. Object markers
        foreach (var o in c.Objects.Where(o => !o.IsArea && IsVisible(o)))
        {
            _pickables.Add((new PickedObject(o), 6));
            if (!Layers.MarkersOnModels && ModelVisible(o) && HasModel?.Invoke(o) == true) continue;
            var col = Palette.ForCategory(o.Category);
            var p = o.Position;
            var fwd = Vector3.TransformNormal(Vector3.UnitZ, o.Rotation);
            var u = Upp(p);
            switch (o.Category)
            {
                case ObjectCategory.ItemBox:
                    Square(p, MathF.Max(2.5f, 5 * u), col);
                    break;
                case ObjectCategory.Coin:
                    Disc(p, MathF.Max(0.8f, 2.5f * u), col, 8);
                    break;
                case ObjectCategory.BoostRing:
                    // Ring facing is not consistent with local +Z in the data, so no arrow.
                    RingMarker(p, MathF.Max(4f, 6 * u), 2, col);
                    break;
                case ObjectCategory.DashPanel or ObjectCategory.GlideRamp or ObjectCategory.JumpRamp:
                    Arrow(p, fwd, MathF.Max(6f, 11 * u), col);
                    break;
                case ObjectCategory.StartPosition or ObjectCategory.LapGate:
                    Arrow(p, fwd, MathF.Max(3f, 7 * u), col);
                    break;
                case ObjectCategory.RecoveryPoint:
                    RingMarker(p, MathF.Max(3f, 6 * u), 2, col);
                    Disc(p, MathF.Max(1f, 2 * u), col, 8);
                    break;
                case ObjectCategory.Hazard:
                    var s = MathF.Max(3f, 5 * u);
                    Line(p + new Vector3(-s, 0, -s), p + new Vector3(s, 0, s), 1.5f, col);
                    Line(p + new Vector3(-s, 0, s), p + new Vector3(s, 0, -s), 1.5f, col);
                    break;
                default:
                    Disc(p, MathF.Max(0.6f, 1.8f * u), col, 6);
                    break;
            }
        }

        // 5. Selection highlight
        if (Selected is PickedObject { Object.IsArea: true } sa && is3D) AreaBox(sa.Object, Palette.Selection, highlight: true);

        batch.Upload();
    }

    // --- areas ---

    /// <summary>
    /// The area's box in local units: X/Z span ±0.5 (times Scale); Y spans 0..1 for <c>Cube_t</c>-style editor shapes and
    /// ±0.5 for centred ones (<c>*_c</c>, plain <c>Cube</c>/<c>Sphere</c>). The 1-unit base size and the bottom origin
    /// of <c>Cube_t</c> are inferred from the data (rescue areas have the lap path 0.3-0.7 of their height above the
    /// origin), not traced in code. Plain <c>Cube</c> being centred is a guess.
    /// </summary>
    private (float Y0, float Y1, bool Round) AreaShape(CourseObject o)
    {
        var shape = DummyShape?.Invoke(o.Type) ?? o.Primitive;
        var round = shape.StartsWith("Sphere", StringComparison.Ordinal) || shape.StartsWith("Cylinder", StringComparison.Ordinal) || shape == "Capsule";
        var centred = shape.EndsWith("_c", StringComparison.Ordinal) || shape is "Cube" or "Sphere" or "Box";
        return centred ? (-0.5f, 0.5f, round) : (0f, 1f, round);
    }

    private static Matrix4x4 AreaMatrix(CourseObject o) => Matrix4x4.CreateScale(o.Actor.Scale) * o.Rotation * Matrix4x4.CreateTranslation(o.Position);

    private void AreaBox(CourseObject o, uint col, bool highlight = false)
    {
        var (y0, y1, round) = AreaShape(o);
        var m = AreaMatrix(o);
        var n = round ? 20 : 4;
        Vector3 P(int i, float y)
        {
            var a = round ? i * MathF.Tau / n : i * MathF.PI / 2 + MathF.PI / 4;
            var r = round ? 0.5f : MathF.Sqrt(0.5f);
            return Vector3.Transform(new Vector3(MathF.Cos(a) * r, y, MathF.Sin(a) * r), m);
        }
        var fill = Palette.WithAlpha(col, highlight ? (byte)0x30 : (byte)0x1C);
        var edge = Palette.WithAlpha(col, 0xE0);
        var w = highlight ? 3f : 1.5f;
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            batch.Quad(P(i, y0), P(j, y0), P(j, y1), P(i, y1), fill);
            Line(P(i, y0), P(j, y0), w, edge);
            Line(P(i, y1), P(j, y1), w, edge);
            if (!round || i % 5 == 0) Line(P(i, y0), P(i, y1), w, edge);
        }
        for (var i = 1; i + 1 < n; i++) batch.Triangle(P(0, y1), P(i, y1), P(i + 1, y1), fill);
    }

    /// <summary>Top-down outline (convex polygon) of an area at its origin height.</summary>
    public static Vector3[] AreaFootprint(CourseObject o)
    {
        var m = o.Rotation;
        var s = o.Actor.Scale / 2;
        Vector3 P(float x, float z) => o.Position + Vector3.TransformNormal(new Vector3(x * s.X, 0, z * s.Z), m);
        if (o.Primitive is "Sphere" or "Cylinder" or "Capsule")
            return Enumerable.Range(0, 24).Select(i => P(MathF.Cos(i * MathF.Tau / 24), MathF.Sin(i * MathF.Tau / 24))).ToArray();
        return [P(-1, -1), P(1, -1), P(1, 1), P(-1, 1)];
    }

    /// <summary>Ray parameter of the hit on an area's box, or null if missed (slab test in the box's local space).</summary>
    private float? RayHitArea(CourseObject o, Vector3 origin, Vector3 dir)
    {
        if (!Matrix4x4.Invert(AreaMatrix(o), out var inv)) return null;
        var lo = Vector3.Transform(origin, inv);
        var ld = Vector3.TransformNormal(dir, inv);
        var (y0, y1, _) = AreaShape(o);
        Span<float> mn = [-0.5f, y0, -0.5f];
        Span<float> mx = [0.5f, y1, 0.5f];
        Span<float> oo = [lo.X, lo.Y, lo.Z];
        Span<float> dd = [ld.X, ld.Y, ld.Z];
        float t0 = 0, t1 = float.MaxValue;
        for (var k = 0; k < 3; k++)
        {
            if (MathF.Abs(dd[k]) < 1e-12f) { if (oo[k] < mn[k] || oo[k] > mx[k]) return null; continue; }
            var a = (mn[k] - oo[k]) / dd[k];
            var b = (mx[k] - oo[k]) / dd[k];
            if (a > b) (a, b) = (b, a);
            t0 = MathF.Max(t0, a);
            t1 = MathF.Min(t1, b);
            if (t0 > t1) return null;
        }
        return t0; // same parameter as the world ray, since the transform is affine
    }

    // --- primitives ---

    /// <summary>A line <paramref name="px"/> screen pixels wide: flat in X/Z top-down, a camera-facing ribbon in 3D.</summary>
    private void Line(Vector3 a, Vector3 b, float px, uint color)
    {
        if (_orbit is { } o)
        {
            var d = b - a;
            if (d.LengthSquared() < 1e-8f) return;
            var mid = (a + b) / 2;
            var side = Vector3.Cross(d, o.Eye - mid);
            if (side.LengthSquared() < 1e-12f) return;
            side = Vector3.Normalize(side);
            var ha = side * (px * Upp(a) / 2);
            var hb = side * (px * Upp(b) / 2);
            batch.Quad(a - ha, b - hb, b + hb, a + ha, color);
            return;
        }
        var d2 = new Vector2(b.X - a.X, b.Z - a.Z);
        if (d2.LengthSquared() < 1e-8f) return;
        var n = Vector2.Normalize(new Vector2(-d2.Y, d2.X)) * (px * Upp(a) / 2);
        var off = new Vector3(n.X, 0, n.Y);
        batch.Quad(a - off, b - off, b + off, a + off, color);
    }

    /// <summary>Small arrowhead at the middle of a segment, pointing from a to b.</summary>
    private void Chevron(Vector3 a, Vector3 b, uint color)
    {
        var mid = (a + b) / 2;
        var u = Upp(mid);
        var dir = Vector3.Normalize(b - a);
        var side = _orbit is { } o ? Vector3.Cross(dir, o.Eye - mid) : Vector3.Cross(dir, Vector3.UnitY);
        if (side.LengthSquared() < 1e-12f) return;
        side = Vector3.Normalize(side);
        var len = 8 * u;
        Line(mid + dir * len / 2, mid - dir * len / 2 + side * len * 0.6f, 2f, color);
        Line(mid + dir * len / 2, mid - dir * len / 2 - side * len * 0.6f, 2f, color);
    }

    /// <summary>
    /// A round dot of <paramref name="px"/> screen pixels radius with a dark outline: facing the camera in 3D, flat
    /// in X/Z top-down.
    /// </summary>
    private void Dot(Vector3 c, float px, uint color)
    {
        var u = Upp(c);
        Vector3 right = Vector3.UnitX, up = Vector3.UnitZ;
        if (_orbit is { } o)
        {
            var f = c - o.Eye;
            if (f.LengthSquared() < 1e-8f) return;
            f = Vector3.Normalize(f);
            right = Vector3.Cross(f, Vector3.UnitY);
            right = right.LengthSquared() < 1e-8f ? Vector3.UnitX : Vector3.Normalize(right);
            up = Vector3.Cross(right, f);
        }
        void Fill(float r, uint col)
        {
            const int n = 10;
            for (var i = 0; i < n; i++)
            {
                var a0 = i * MathF.Tau / n;
                var a1 = (i + 1) * MathF.Tau / n;
                batch.Triangle(c, c + (right * MathF.Cos(a0) + up * MathF.Sin(a0)) * r, c + (right * MathF.Cos(a1) + up * MathF.Sin(a1)) * r, col);
            }
        }
        Fill((px + 1.2f) * u, Palette.WithAlpha(0xFF000000, (byte)Math.Min(0xC0, color >> 24)));
        Fill(px * u, color);
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

    private void RingMarker(Vector3 c, float r, float px, uint color)
    {
        const int n = 24;
        for (var i = 0; i < n; i++)
        {
            var a0 = i * MathF.Tau / n;
            var a1 = (i + 1) * MathF.Tau / n;
            Line(c + new Vector3(MathF.Cos(a0) * r, 0, MathF.Sin(a0) * r), c + new Vector3(MathF.Cos(a1) * r, 0, MathF.Sin(a1) * r), px, color);
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

    public Pickable? PickAt(IViewCamera view, Vector2 screen)
    {
        if (view is OrbitCamera orbit) return PickOnScreen(orbit, screen);
        var camera = (TopDownCamera)view;
        var world = camera.ScreenToWorld(screen);
        Pickable? best = null;
        var bestD = float.MaxValue;
        var limit = PickRadiusPx * camera.UnitsPerPixel;
        foreach (var (item, radiusPx) in _pickables)
        {
            float d;
            if (item is PickedObject { Object.IsArea: true } a)
                d = PointInQuad(world, AreaFootprint(a.Object)) ? 0.5f * limit : float.MaxValue; // areas lose to point objects
            else
                d = Vector2.Distance(world, new Vector2(item.Position.X, item.Position.Z)) - radiusPx * camera.UnitsPerPixel;
            if (d < limit && d < bestD) { best = item; bestD = d; }
        }
        return best;
    }

    /// <summary>How close (screen pixels) the cursor must be to a point to pick it; follows the line/marker size.</summary>
    private float PickRadiusPx => 14f * MathF.Max(1f, Layers.LineScale);

    /// <summary>3D picking: the nearest projected point within the pick radius wins; otherwise the nearest area box under the cursor.</summary>
    private Pickable? PickOnScreen(OrbitCamera camera, Vector2 screen)
    {
        Pickable? best = null;
        var bestD = PickRadiusPx;
        foreach (var (item, _) in _pickables)
        {
            if (item is PickedObject { Object.IsArea: true }) continue;
            if (camera.Project(item.Position) is not { } p) continue;
            var d = Vector2.Distance(p, screen);
            if (d < bestD) { best = item; bestD = d; }
        }
        if (best is not null) return best;

        var (origin, dir) = camera.Ray(screen);
        var bestT = float.MaxValue;
        foreach (var (item, _) in _pickables)
        {
            if (item is not PickedObject { Object.IsArea: true } a) continue;
            if (RayHitArea(a.Object, origin, dir) is { } t && t < bestT) { bestT = t; best = item; }
        }
        return best;
    }

    public void UpdateHover(IViewCamera camera, Vector2 mouse, bool mouseInViewport) =>
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

    /// <summary>Screen-space rings for the hovered and selected points, drawn with ImGui on top of the GL view.</summary>
    public void DrawHighlights(IViewCamera camera, ImDrawListPtr dl)
    {
        var r = 9f * MathF.Max(1f, Layers.LineScale);
        if (Hovered is { } h && h != Selected && h is not PickedObject { Object.IsArea: true } && camera.Project(h.Position) is { } hp)
        {
            dl.AddCircle(hp, r, 0xA0000000, 24, 4);
            dl.AddCircle(hp, r, Palette.Hover, 24, 2);
        }
        if (Selected is { } s && s is not PickedObject { Object.IsArea: true } && camera.Project(s.Position) is { } sp)
        {
            dl.AddCircle(sp, r * 1.3f, 0xC0000000, 24, 5);
            dl.AddCircle(sp, r * 1.3f, Palette.Selection, 24, 2.5f);
            dl.AddCircleFilled(sp, 2.5f * MathF.Max(1f, Layers.LineScale), Palette.Selection);
        }
    }

    /// <summary>Checkpoint numbers, drawn with ImGui on top of the GL view.</summary>
    public void DrawLabels(IViewCamera camera, ImDrawListPtr dl)
    {
        if (Course is null || !Layers.LapPath || !Layers.CheckpointLabels) return;
        foreach (var lp in Course.LapPoints)
        {
            string? text = lp.IsStart ? "START" : lp.IsGoal ? "GOAL" : lp.CheckPoint is { } cp ? cp.ToString("0") : null;
            if (lp.IsLapCheck) text = (text is null ? "" : text + " ") + "LAP";
            if (text is null) continue;
            // In 3D, label the top of the gate so it sits above the road.
            var g = lp.GateCorners();
            var anchor = camera is OrbitCamera ? (g[2] + g[3]) / 2 : lp.Position;
            if (camera.Project(anchor) is not { } screen) continue;
            var s = screen + new Vector2(6, -16);
            dl.AddText(s + new Vector2(1, 1), 0xFF000000, text);
            dl.AddText(s, 0xFFFFFFFF, text);
        }
    }
}
