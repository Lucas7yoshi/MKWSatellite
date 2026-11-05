using System.Numerics;

namespace MKWSatellite.Formats.Course;

/// <summary>
/// An area's volume in its local space, before the actor's Scale/Rotate/Translate: an axis-aligned box from
/// <see cref="Min"/> to <see cref="Max"/> (or the cylinder/sphere inscribed in it when <see cref="Round"/>).
/// </summary>
/// <param name="FromPhysics">
/// True when it comes from the actor's Phive shape (the collision the game tests against); false when it is a fallback
/// from the editor placeholder (<c>BancDummyShape</c>), which is a guess.
/// </param>
public sealed record AreaShape(Vector3 Min, Vector3 Max, bool Round, bool FromPhysics, string Description)
{
    /// <summary>
    /// Fallback from the editor placeholder name when the actor has no Phive shape. <c>Cube_t</c> is the shape every
    /// physics-backed <c>Cube_t</c> area uses (box centre (0,-1,0), half-extents 1: hangs down from the origin);
    /// <c>_c</c> and plain shapes are taken as centred with half-extents 1 (most physics-backed <c>Cube_c</c> areas;
    /// plain <c>Cube</c> varies in the data, so this is a guess).
    /// </summary>
    public static AreaShape FromDummy(string? dummy)
    {
        var name = dummy ?? "Cube_c";
        var round = name.StartsWith("Sphere", StringComparison.Ordinal) || name.StartsWith("Cylinder", StringComparison.Ordinal) || name.StartsWith("Capsule", StringComparison.Ordinal);
        return name.EndsWith("_t", StringComparison.Ordinal)
            ? new AreaShape(new Vector3(-1, -2, -1), new Vector3(1, 0, 1), round, false, $"{name} (editor shape, hangs below origin; guessed)")
            : new AreaShape(new Vector3(-1), new Vector3(1), round, false, $"{name} (editor shape, centred; guessed)");
    }
}
