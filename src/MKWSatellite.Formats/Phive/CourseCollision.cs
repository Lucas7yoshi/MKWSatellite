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
                    // Work/Phive/Shape/Dcc/<name>.phsh -> Phive/Shape/Dcc/<name>.Nin_Ounce_NVN2.bphsh.zs
                    var rel = work.StartsWith("Work/", StringComparison.Ordinal) ? work[5..] : work;
                    var path = Path.ChangeExtension(rel, null) + Suffix + ".bphsh.zs";
                    var world = Matrix4x4.CreateScale(o.Actor.Scale) * CourseMath.EulerXyz(o.Actor.Rotate) * Matrix4x4.CreateTranslation(o.Position);
                    Add(fs, list, path.Replace('\\', '/'), false, world, o.Type);
                }
        }
        return list;
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
