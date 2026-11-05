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
}

/// <summary>
/// A lap-path node with its gate rectangle. The game reads <c>RectSize</c>/<c>RectOffset</c> for every rail point
/// (RailGraph_ParseRail, 1.8 0x710029AD70) into a rectangle in the point's local X/Y plane, so the gate faces local Z.
/// The rectangle spans (offset - size) / 2 .. (offset + size) / 2 on each axis, i.e. its centre is at offset / 2.
/// Inferred, not traced: it is the only reading under which every Airship Fortress gate contains the road at its node
/// (nodes measured 0-0.3 units above the stage model) while fitting under the covered section's 11-19 unit ceiling,
/// and it matches gates widened to one side (offset = growth). See docs/formats/banc.md.
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

    /// <summary>The four gate corners in world space (bottom-left, bottom-right, top-right, top-left in local X/Y).</summary>
    public Vector3[] GateCorners()
    {
        var m = CourseMath.EulerXyz(Point.Rotate);
        var x = Vector3.TransformNormal(Vector3.UnitX, m);
        var y = Vector3.TransformNormal(Vector3.UnitY, m);
        var c = Position + x * (RectOffset.X / 2) + y * (RectOffset.Y / 2);
        var hx = x * (RectSize.X / 2);
        var hy = y * (RectSize.Y / 2);
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

    internal static Vector2 Vec2(BymlArray? a) =>
        a is { Count: >= 2 } ? new Vector2(Convert.ToSingle(a[0]), Convert.ToSingle(a[1])) : default;
}
