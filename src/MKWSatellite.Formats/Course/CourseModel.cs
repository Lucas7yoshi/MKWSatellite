using System.Numerics;
using MKWSatellite.Formats.Byml;

namespace MKWSatellite.Formats.Course;

/// <summary>Which scene layer an object came from. See docs/formats/scene-layers.md.</summary>
public enum CourseLayer
{
    /// <summary>The race scene (<c>Race_*.bcett</c>): lap path, items, respawns, start grid.</summary>
    Race,
    /// <summary>Base open-world tiles (<c>World_Game03/*</c>), clipped to the course bounds: course furniture.</summary>
    World,
    /// <summary>An off-world course's own static layer (<c>&lt;Course&gt;_00_Fix</c>, e.g. Rainbow Road).</summary>
    CourseFix,
}

/// <summary>What an object is, for display. Derived from the actor type name; see <see cref="CourseObjectClassifier"/>.</summary>
public enum ObjectCategory
{
    ItemBox,
    Coin,
    DashPanel,
    GlideRamp,
    BoostRing,
    JumpRamp,
    StartPosition,
    LapGate,
    RespawnArea,
    /// <summary>Point-like respawn/recovery markers (priority Lakitu recovery point, respawn point).</summary>
    RecoveryPoint,
    ValleyArea,
    HighSpeedArea,
    /// <summary>Invisible boost/glide triggers (DashArea, GlideArea) and the Bullet-Bill-only glide area.</summary>
    BoostArea,
    /// <summary>Gameplay rule areas: no reverse driving, no jump action, item destroying, high dirt, no entry...</summary>
    RuleArea,
    /// <summary>Areas that spawn or delete linked actors (CreateLinkArea, Area_DeleteLinkedActor).</summary>
    LinkArea,
    OtherArea,
    Hazard,
    Fence,
    Audience,
    Other,
}

/// <summary>A link from another object to this one (e.g. a CreateLinkArea that spawns it).</summary>
public sealed record IncomingLink(CourseObject Source, string LinkName);

/// <summary>A placed actor in a course, tagged with its layer, category and actor-database row.</summary>
public sealed record CourseObject(BancActor Actor, CourseLayer Layer, ObjectCategory Category, ActorInfo? Info)
{
    public string Type => Actor.Type;

    /// <summary>Links from other objects in the course that target this one. Filled by <see cref="CourseLoader"/>.</summary>
    public List<IncomingLink> IncomingLinks { get; } = [];

    /// <summary>Outgoing link targets by hash (<c>Links[].Dst</c>), with link names.</summary>
    public IEnumerable<(ulong Dst, string Name)> OutgoingLinks =>
        BancScene.Items(Actor.Raw.Array("Links")).OfType<BymlDict>().Select(l => (l.Get<ulong>("Dst"), l.Get<string>("Name") ?? ""));

    /// <summary>Editor primitive for areas: Cube (default), Sphere, Cylinder...</summary>
    public string Primitive => Actor.Raw.Get<string>("PrimitiveType") ?? "Cube";
    public Vector3 Position => Actor.Translate;

    /// <summary>Rotation from the actor's Euler angles (radians, applied X then Y then Z; same convention as rail points).</summary>
    public Matrix4x4 Rotation => CourseMath.EulerXyz(Actor.Rotate);

    /// <summary>True for box-shaped areas (editor <c>PrimitiveType</c> Cube/Box).</summary>
    public bool IsArea => Category is ObjectCategory.RespawnArea or ObjectCategory.ValleyArea or ObjectCategory.HighSpeedArea
        or ObjectCategory.BoostArea or ObjectCategory.RuleArea or ObjectCategory.LinkArea or ObjectCategory.OtherArea;
}

/// <summary>A rail (path) with its layer.</summary>
public sealed record CourseRail(BancRail Rail, CourseLayer Layer)
{
    public string Kind => Rail.Name;
    public RailKindInfo Info => RailKinds.Info(Kind);

    /// <summary>Rail-level parameters (e.g. <c>RailSlideType</c>, <c>IsAngleLimit</c> on grind rails).</summary>
    public BymlDict? Dynamic => Rail.Raw.Dict("Dynamic");

    /// <summary>Editor prefab the rail came from, e.g. <c>Houdini/Auto_Game03_Wire_RailslidePath</c> (generated).</summary>
    public string? ParentPrefab => Rail.Raw.Get<string>("ParentPrefab")?.Replace(".bcett.json", "");

    /// <summary>Hash of the actor the rail belongs to (fence/guardrail for <c>ColRailSlidePath</c>), if any.</summary>
    public ulong? ParentActor => Rail.Raw.TryGetValue("Parent", out var v) && v is ulong h ? h : null;

    /// <summary><c>RailSlideType</c> of a grind rail (0 when absent).</summary>
    public int GrindType => Dynamic?.TryGetValue("RailSlideType", out var v) == true && v is int i ? i : 0;

    /// <summary>Polyline length through the nodes (closing segment included for closed rails).</summary>
    public float Length
    {
        get
        {
            var p = Rail.Points;
            var l = 0f;
            for (var i = 1; i < p.Count; i++) l += Vector3.Distance(p[i - 1].Translation, p[i].Translation);
            if (Rail.IsClosed && p.Count > 2) l += Vector3.Distance(p[^1].Translation, p[0].Translation);
            return l;
        }
    }
}

/// <summary>
/// A lap-path node with its progress gate. Traced in 1.8: the point setup (0x71007993A0) stores <c>RectSize</c> as the
/// gate's half-width/half-height, and 0x71007EE5D0 moves the gate centre to node + <c>RectOffset.X</c> * right +
/// <c>RectOffset.Y</c> * up (rows 0/1 of the <c>Rotate</c> matrix). So the gate spans offset ± size on each axis.
/// Progress (0x7100AAD950 → <c>RailPoint_IsInSegmentVolume</c> 0x71007F34D0) finds the segment whose two gate planes
/// (through the centres, facing local Z) you are between, then checks you are within the gates' width/height,
/// interpolated along the segment. See docs/formats/banc.md.
/// </summary>
public sealed record LapPoint(BancRailPoint Point, CourseRail Rail, int Index)
{
    public Vector3 Position => Point.Translation;
    public Vector2 RectSize => CourseMath.Vec2(Point.Raw.Array("RectSize"));
    public Vector2 RectOffset => CourseMath.Vec2(Point.Raw.Array("RectOffset"));

    /// <summary><c>Dynamic.CheckPoint</c>: checkpoint number, if this node is one. Meaning beyond the name is unverified.</summary>
    public float? CheckPoint => Point.Dynamic?.TryGetValue("CheckPoint", out var v) == true && v is float f ? f : null;
    public bool IsStart => Point.Dynamic?.Get<bool>("IsStart") == true;
    public bool IsGoal => Point.Dynamic?.Get<bool>("IsGoal") == true;
    public bool IsLapCheck => Point.Dynamic?.Get<bool>("IsLapCheck") == true;
    public int? ChangeId => Point.Dynamic?.TryGetValue("ChangeId", out var v) == true && v is int i ? i : null;

    /// <summary>
    /// <c>Dynamic.LerpType</c> (lap point +0x530, read by 0x7102990F20): which sides of the progress zone run straight
    /// from this gate's edge to the next gate's edge instead of following the interpolated frame. 0 = neither
    /// (default), 1 = both, 2 = the +right side, 3 = the -right side. A side is straight only when both gates of the
    /// segment ask for it (traced in <c>RailPoint_IsInSegmentVolume</c>).
    /// </summary>
    public int LerpType => Point.Dynamic?.TryGetValue("LerpType", out var v) == true && v is IConvertible c ? c.ToInt32(null) : 0;

    /// <summary>The gate's frame: centre, right/up/forward rows of <c>Rotate</c> and the half-extents.</summary>
    public (Vector3 Centre, Quaternion Rotation, float HalfWidth, float HalfHeight) GateFrame()
    {
        var m = CourseMath.EulerXyz(Point.Rotate);
        return (GateCentre, Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m)), RectSize.X, RectSize.Y);
    }

    /// <summary>Gate centre: the node moved by <c>RectOffset</c> along the point's local X (right) and Y (up).</summary>
    public Vector3 GateCentre
    {
        get
        {
            var m = CourseMath.EulerXyz(Point.Rotate);
            return Position + Vector3.TransformNormal(Vector3.UnitX, m) * RectOffset.X + Vector3.TransformNormal(Vector3.UnitY, m) * RectOffset.Y;
        }
    }

    /// <summary>The four gate corners in world space (bottom-left, bottom-right, top-right, top-left in local X/Y).</summary>
    public Vector3[] GateCorners()
    {
        var m = CourseMath.EulerXyz(Point.Rotate);
        var x = Vector3.TransformNormal(Vector3.UnitX, m);
        var y = Vector3.TransformNormal(Vector3.UnitY, m);
        var c = GateCentre;
        var hx = x * RectSize.X;
        var hy = y * RectSize.Y;
        return [c - hx - hy, c + hx - hy, c + hx + hy, c - hx + hy];
    }
}

/// <summary>A race course assembled from its scene layers.</summary>
public sealed class CourseScene
{
    public required string Name { get; init; }
    public required BancScene RaceScene { get; init; }
    /// <summary>Static layer source description, e.g. "World_Game03 (37 tiles)" or "Rainbow_00_Fix".</summary>
    public required string StaticLayerSource { get; init; }
    public required IReadOnlyList<CourseObject> Objects { get; init; }
    public required IReadOnlyList<CourseRail> Rails { get; init; }
    public required IReadOnlyList<LapPoint> LapPoints { get; init; }
    public required ActorDatabase Database { get; init; }
    /// <summary>3D bounds of the race's lap and route paths (the area the course occupies).</summary>
    public required (Vector3 Min, Vector3 Max) PathBounds { get; init; }

    /// <summary>All rail points by hash, for following <c>Links</c> across rails.</summary>
    public IReadOnlyDictionary<ulong, BancRailPoint> PointsByHash => _pointsByHash ??=
        Rails.SelectMany(r => r.Rail.Points).GroupBy(p => p.Hash).ToDictionary(g => g.Key, g => g.First());
    private Dictionary<ulong, BancRailPoint>? _pointsByHash;

    /// <summary>Objects by actor hash (for rail <c>Parent</c> and link lookups).</summary>
    public IReadOnlyDictionary<ulong, CourseObject> ObjectsByHash => _objectsByHash ??=
        Objects.GroupBy(o => o.Actor.Hash).ToDictionary(g => g.Key, g => g.First());
    private Dictionary<ulong, CourseObject>? _objectsByHash;

    /// <summary>
    /// Objects that link to a node of each rail (e.g. a snowboarding Shy Guy to the <c>GuideGraphRail</c> it rides),
    /// with the node index and link name.
    /// </summary>
    public IReadOnlyDictionary<CourseRail, List<(CourseObject Object, int Node, string Link)>> RailUsers => _railUsers ??= BuildRailUsers();
    private Dictionary<CourseRail, List<(CourseObject, int, string)>>? _railUsers;

    private Dictionary<CourseRail, List<(CourseObject, int, string)>> BuildRailUsers()
    {
        var node = new Dictionary<ulong, (CourseRail, int)>();
        foreach (var r in Rails)
            for (var i = 0; i < r.Rail.Points.Count; i++) node.TryAdd(r.Rail.Points[i].Hash, (r, i));
        var users = new Dictionary<CourseRail, List<(CourseObject, int, string)>>(ReferenceEqualityComparer.Instance);
        foreach (var o in Objects)
            foreach (var (dst, name) in o.OutgoingLinks)
                if (node.TryGetValue(dst, out var t))
                {
                    if (!users.TryGetValue(t.Item1, out var l)) users[t.Item1] = l = [];
                    l.Add((o, t.Item2, name));
                }
        return users;
    }
}

public static class CourseMath
{
    /// <summary>
    /// Rotation matrix for game Euler angles (radians): R = Rz * Ry * Rx (column-vector convention), i.e. rotate about
    /// X first. Matches the up-vector computation in RailGraph_ParseRail. Returned as a System.Numerics row-vector
    /// matrix, so use <see cref="Vector3.TransformNormal(Vector3, Matrix4x4)"/>.
    /// </summary>
    public static Matrix4x4 EulerXyz(Vector3 r) =>
        Matrix4x4.CreateRotationX(r.X) * Matrix4x4.CreateRotationY(r.Y) * Matrix4x4.CreateRotationZ(r.Z);

    public static Vector2 Vec2(BymlArray? a) =>
        a is { Count: >= 2 } ? new Vector2(Convert.ToSingle(a[0]), Convert.ToSingle(a[1])) : default;
}
