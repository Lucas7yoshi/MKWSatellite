using System.Numerics;
using MKWSatellite.Formats.Course;
using MKWSatellite.Formats.IO;

namespace MKWSatellite.Formats.Phive;

/// <summary>A collision file to read for a course, with the transform for plain shapes.</summary>
public sealed record CollisionSource(string Path, bool IsCompound, Matrix4x4 World, string Label);

/// <summary>
/// Which collision files make up a course (see docs/formats/collision.md):
/// <list type="bullet">
/// <item>Open-world courses: the static compounds <c>Phive/StaticCompoundBody/World_Game03/World_Game03_XXZZ_ij</c>
/// (250-unit cells, i = X, j = Z) covering the course. They include the stage model's collision, the merged world
/// geometry and fixed objects. Plus the terrain collision <c>Phive/Terrain/Game03_Proc/X##_Z##_ij.bphsh</c> (local to
/// the cell centre, absolute heights).</item>
/// <item>Off-world courses (<c>_Fix</c> layer): each <c>Stage_*</c> actor's own shapes (ShapeParam <c>PhshMesh</c>),
/// placed with the actor's transform.</item>
/// <item>Both: the race scene's compound <c>Phive/StaticCompoundBody/Course/&lt;race scene&gt;.bphsc</c> (race-only
/// objects plus respawn / valley / no-jump-action volumes).</item>
/// </list>
/// </summary>
public static class CourseCollision
{
    public const float Cell = 250f, TileSize = 1000f;
    private const string Suffix = ".Nin_Ounce_NVN2";

    public static List<CollisionSource> SourcesFor(GameFileSystem fs, CourseScene course, Func<string, IEnumerable<string>> stageShapes, float margin = 150)
    {
        var list = new List<CollisionSource>();
        var raceName = course.Name.Split('[')[0].Replace(".bcett.byml.zs", "");
        Add(fs, list, $"Phive/StaticCompoundBody/Course/{raceName}{Suffix}.bphsc.zs", true, Matrix4x4.Identity, "race scene");

        if (course.StaticLayerSource.StartsWith("World_Game03", StringComparison.Ordinal))
        {
            var (min, max) = course.PathBounds;
            var x0 = (int)MathF.Floor((min.X - margin) / Cell + 32); var x1 = (int)MathF.Floor((max.X + margin) / Cell + 32);
            var z0 = (int)MathF.Floor((min.Z - margin) / Cell + 32); var z1 = (int)MathF.Floor((max.Z + margin) / Cell + 32);
            for (var cx = Math.Max(0, x0); cx <= Math.Min(63, x1); cx++)
                for (var cz = Math.Max(0, z0); cz <= Math.Min(63, z1); cz++)
                {
                    int tx = cx / 4, tz = cz / 4, i = cx % 4, j = cz % 4;
                    Add(fs, list, $"Phive/StaticCompoundBody/World_Game03/World_Game03_{tx:00}{tz:00}_{i}{j}{Suffix}.bphsc.zs", true,
                        Matrix4x4.Identity, $"world {tx:00}{tz:00}_{i}{j}");
                    var centre = new Vector3((cx - 32 + 0.5f) * Cell, 0, (cz - 32 + 0.5f) * Cell);
                    Add(fs, list, $"Phive/Terrain/Game03_Proc/X{tx:00}_Z{tz:00}_{i}{j}{Suffix}.bphsh.zs", false,
                        Matrix4x4.CreateTranslation(centre), $"terrain X{tx:00}_Z{tz:00}_{i}{j}");
                }
        }
        else
        {
            foreach (var o in course.Objects.Where(o => o.Layer == CourseLayer.CourseFix && o.Type.StartsWith("Stage_", StringComparison.Ordinal)))
                foreach (var work in stageShapes(o.Type))
                {
                    var world = Matrix4x4.CreateScale(o.Actor.Scale) * CourseMath.EulerXyz(o.Actor.Rotate) * Matrix4x4.CreateTranslation(o.Position);
                    Add(fs, list, ShapePath(work), false, world, o.Type);
                }
        }
        return list;
    }

    /// <summary>
    /// The 250-unit cells of another world compound set covering the course (open-world courses only), e.g.
    /// <c>World_Game03_Water</c> or <c>World_Game03_ModelWater</c>: <c>Phive/StaticCompoundBody/&lt;set&gt;/&lt;set&gt;_XXZZ_ij</c>.
    /// </summary>
    public static List<CollisionSource> WorldSetSourcesFor(GameFileSystem fs, CourseScene course, string set, float margin = 150)
    {
        var list = new List<CollisionSource>();
        if (!course.StaticLayerSource.StartsWith("World_Game03", StringComparison.Ordinal)) return list;
        var (min, max) = course.PathBounds;
        var x0 = (int)MathF.Floor((min.X - margin) / Cell + 32); var x1 = (int)MathF.Floor((max.X + margin) / Cell + 32);
        var z0 = (int)MathF.Floor((min.Z - margin) / Cell + 32); var z1 = (int)MathF.Floor((max.Z + margin) / Cell + 32);
        for (var cx = Math.Max(0, x0); cx <= Math.Min(63, x1); cx++)
            for (var cz = Math.Max(0, z0); cz <= Math.Min(63, z1); cz++)
            {
                int tx = cx / 4, tz = cz / 4, i = cx % 4, j = cz % 4;
                Add(fs, list, $"Phive/StaticCompoundBody/{set}/{set}_{tx:00}{tz:00}_{i}{j}{Suffix}.bphsc.zs", true,
                    Matrix4x4.Identity, $"{set} {tx:00}{tz:00}_{i}{j}");
            }
        return list;
    }

    /// <summary>Phive shape path for a ShapeParam <c>PhshMeshPath</c> (<c>Work/Phive/Shape/Dcc/X.phsh</c>).</summary>
    public static string ShapePath(string work)
    {
        var rel = work.StartsWith("Work/", StringComparison.Ordinal) ? work[5..] : work;
        return (Path.ChangeExtension(rel, null) + Suffix + ".bphsh.zs").Replace('\\', '/');
    }

    /// <summary>
    /// Adds placed objects' own collision meshes (ActorGParam PhysicsRef → ShapeParam <c>PhshMesh</c>) to
    /// <paramref name="mesh"/>, placed with each object's transform. Static objects are already baked into the
    /// compounds: an instance whose vertices are at least <paramref name="bakedShare"/> already present (within
    /// 0.25 units) is skipped. What is left is mostly moving or spawned objects (traffic, Rainbow Road's gates),
    /// drawn where the scene places them. Returns the number of instances added.
    /// </summary>
    public static int AddObjects(GameFileSystem fs, CourseScene course, CollisionMesh mesh, Func<string, IReadOnlyList<string>> meshPaths,
        List<string>? errors = null, float bakedShare = 0.9f)
    {
        var snap = new HashSet<(int, int, int)>(mesh.Positions.Count);
        foreach (var v in mesh.Positions) snap.Add(Key(v));
        var added = 0;
        foreach (var o in course.Objects)
        {
            if (o.Type.StartsWith("Stage_", StringComparison.Ordinal)) continue;
            var paths = meshPaths(o.Type);
            if (paths.Count == 0) continue;
            var world = Matrix4x4.CreateScale(o.Actor.Scale) * CourseMath.EulerXyz(o.Actor.Rotate) * Matrix4x4.CreateTranslation(o.Position);
            var om = new CollisionMesh();
            foreach (var w in paths)
            {
                var path = ShapePath(w);
                if (!fs.Exists(path)) continue;
                try { PhiveCollision.ReadShape(fs.Read(path), world, om); }
                catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentOutOfRangeException or KeyNotFoundException)
                {
                    errors?.Add($"{path} ({o.Type}): {e.Message}");
                }
            }
            if (om.TriangleCount == 0) continue;
            var baked = om.Positions.Count(v => Near(snap, v));
            if (baked >= bakedShare * om.Positions.Count) continue;
            mesh.CurrentObject = mesh.Objects.Count;
            mesh.Objects.Add(o.Type);
            mesh.Append(om);
            mesh.CurrentObject = -1;
            added++;
        }
        return added;
    }

    private static (int, int, int) Key(Vector3 v) => ((int)MathF.Round(v.X * 4), (int)MathF.Round(v.Y * 4), (int)MathF.Round(v.Z * 4));

    private static bool Near(HashSet<(int, int, int)> snap, Vector3 v)
    {
        var (x, y, z) = Key(v);
        for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
                for (var dz = -1; dz <= 1; dz++)
                    if (snap.Contains((x + dx, y + dy, z + dz))) return true;
        return false;
    }

    private static void Add(GameFileSystem fs, List<CollisionSource> list, string path, bool compound, Matrix4x4 world, string label)
    {
        if (fs.Exists(path)) list.Add(new(path, compound, world, label));
    }

    /// <summary>Reads all sources into one mesh; errors are collected, not thrown.</summary>
    public static CollisionMesh Load(GameFileSystem fs, IEnumerable<CollisionSource> sources, List<string>? errors = null)
    {
        var mesh = new CollisionMesh();
        foreach (var s in sources)
        {
            try
            {
                var d = fs.Read(s.Path);
                if (s.IsCompound) PhiveCollision.ReadCompound(d, mesh);
                else PhiveCollision.ReadShape(d, s.World, mesh);
            }
            catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentOutOfRangeException or KeyNotFoundException)
            {
                errors?.Add($"{s.Path}: {e.Message}");
            }
        }
        return mesh;
    }
}
