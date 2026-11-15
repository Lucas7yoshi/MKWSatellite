namespace MKWSatellite.Formats.Phive;

/// <summary>
/// The kart performance stat group a ground surface uses: the <c>TopSpeed*</c> / <c>Handling*</c> columns of
/// <c>System/CombinationDataTableData/PerformanceParamTableData</c>. Traced in the 1.8 executable
/// (<c>sub_710065B660</c> at 0x710065BAD8; docs/formats/collision.md, "Performance surfaces").
/// </summary>
public enum PerformanceSurface
{
    /// <summary>Not ground (walls, volumes, poles, pickups...).</summary>
    None,
    /// <summary><c>TopSpeedTarmac</c> / <c>HandlingTarmac</c>: hard surfaces (by SubMaterial).</summary>
    Tarmac,
    /// <summary><c>TopSpeedGravel</c> / <c>HandlingGravel</c>: every other SubMaterial (grass, soil, sand, snow, ice...).</summary>
    Gravel,
    /// <summary><c>TopSpeedWater</c> / <c>HandlingWater</c>: set by the kart's water state, not by a triangle.</summary>
    Water,
    /// <summary><c>TopSpeedDirt</c> / <c>HandlingDirt</c>: LowDirt / Dirt / HighDirt. Both columns are empty in 1.8.</summary>
    Dirt,
}

public static class PerformanceSurfaces
{
    /// <summary>One line on where the mapping comes from, for the UI.</summary>
    public const string Source = "Traced in the 1.8 code; Water is a liquid probe, drawn on Water material (unverified)";

    /// <summary>
    /// SubMaterials that select Tarmac (bit = SubMaterialCollection index, mask 0x27C05E0F0 in the code): Asphalt,
    /// Concrete, Stone, Wood, Iron, Kanaami, Glass, Carpet, Rainbow, Vinyl, Plastic, Biscuit, Candy, ChocoChip, Bone.
    /// </summary>
    public const ulong TarmacSubMaterialMask = 0x27C05E0F0;

    /// <summary>Gameplay materials that are not ground a kart drives on, shown as <see cref="PerformanceSurface.None"/>.</summary>
    private static readonly HashSet<string> NotGround =
    [
        "Undefined", "Vehicle", "Object", "Wall", "LowWall", "BoundWall", "AirWall", "HPWall", "MobVehicleWall", "Valley",
        "Rescue", "Fall", "Pole", "Pull", "Coin", "ItemObj", "Block", "Audience", "NoBoundNoFrictionObject",
        "NoFrictionObject", "HighBoundObject",
    ];

    /// <summary>Stat group for a collision material.</summary>
    public static PerformanceSurface Of(CollisionMaterial m, PhiveNames names) => Of(names.Material(m.Material), m.SubMaterial);

    /// <summary>
    /// Stat group by gameplay material name and SubMaterial index, in the game's order (after rail grind and wall
    /// ride): water state, then dirt (LowDirt / Dirt / HighDirt set the dirt rate), then the SubMaterial mask.
    /// The game takes water from the kart's depth in a water surface; here the <c>Water</c> material stands in for it.
    /// </summary>
    public static PerformanceSurface Of(string material, int subMaterial)
    {
        if (NotGround.Contains(material)) return PerformanceSurface.None;
        if (material == "Water") return PerformanceSurface.Water;
        if (material is "LowDirt" or "Dirt" or "HighDirt") return PerformanceSurface.Dirt;
        return subMaterial is >= 0 and <= 33 && (TarmacSubMaterialMask >> subMaterial & 1) != 0
            ? PerformanceSurface.Tarmac : PerformanceSurface.Gravel;
    }
}
