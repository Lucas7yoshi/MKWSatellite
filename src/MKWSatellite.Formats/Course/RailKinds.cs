namespace MKWSatellite.Formats.Course;

/// <summary>What a kind of rail is for, as far as we know, and how sure we are.</summary>
/// <param name="Label">Short name for the UI.</param>
/// <param name="Description">One or two sentences.</param>
/// <param name="Basis">How the meaning was established (traced in code, or read from data / names).</param>
public sealed record RailKindInfo(string Label, string Description, string Basis);

/// <summary>
/// The rail kinds (<c>GraphRailDefGParam</c>, listed in <c>RSDB/RailDefine</c>) and what they do. See
/// docs/formats/rails.md for the evidence behind each entry.
/// </summary>
public static class RailKinds
{
    public const string Lap = "LapPathGraph", Route = "RoutePathGraph", Grind = "RailSlidePath", FenceGrind = "ColRailSlidePath";

    public static readonly IReadOnlyDictionary<string, RailKindInfo> Known = new Dictionary<string, RailKindInfo>
    {
        [Lap] = new("Lap path", "Race progress: lap gates, checkpoints, start/goal.", "traced (progress, gates)"),
        [Route] = new("AI route", "Paths CPU racers, Bullet Bill and item AI follow; flags per node.", "traced in part (Bullet Bill)"),
        [Grind] = new("Grind rail",
            "A rail karts (and shells) can grind on. Placed by hand or generated from track pieces and wires; " +
            "RailSlideType says which.", "data + code: counted with ColRailSlidePath as rail-slide rails"),
        [FenceGrind] = new("Fence grind rail",
            "A grind rail along the top of a fence or guardrail; its Parent is that actor, so it belongs to the object " +
            "(and is generated from it).", "data: Parent = fence/guardrail actor; code counts it with RailSlidePath"),
        ["GuideGraphRail"] = new("Object path",
            "A path a moving object follows: snowboarding Shy Guys, eagles, ski lifts, generated actors. The object " +
            "links to a node; VelocityScale on nodes changes its speed.", "data: actors link to these nodes"),
        ["GlidePath"] = new("Glide path",
            "Guides gliding, e.g. after a cannon (IsCannon) or a glide ramp; Ascend sets climb.", "names only (unverified)"),
        ["GravityPath"] = new("Gravity path", "Anti-gravity / wall section guide.", "names only (unverified)"),
        ["GravityCameraPath"] = new("Gravity camera path", "Camera guide for gravity sections.", "names only (unverified)"),
        ["PullPath"] = new("Pull path", "Pulls racers along with a Power per node (currents, wind).", "names only (unverified)"),
        ["WaterFlowPath"] = new("Water flow", "River current direction.", "names only (unverified)"),
        ["WaterCascadePath"] = new("Waterfall", "Waterfall line.", "names only (unverified)"),
        ["WhirlpoolPath"] = new("Whirlpool", "Whirlpool path.", "names only (unverified)"),
        ["CitizenPath"] = new("Pedestrian path", "Walking path for town NPCs.", "names only (unverified)"),
        ["ParkingPath"] = new("Parking path", "Traffic parking manoeuvre path.", "names only (unverified)"),
        ["OffroadPath"] = new("Off-road path", "Off-road traffic/NPC path.", "names only (unverified)"),
        ["RidePath"] = new("Ride path", "Path for rideable movers.", "names only (unverified)"),
    };

    public static RailKindInfo Info(string kind) =>
        Known.TryGetValue(kind, out var i) ? i : new(kind, "Unknown rail kind.", "not looked at");

    public static bool IsGrind(string kind) => kind is Grind or FenceGrind;

    /// <summary>
    /// <c>Dynamic.RailSlideType</c> on a <c>RailSlidePath</c> (read at rail setup, 0x71005F7FB0, hash 0xE747C8D1).
    /// What the game does differently per type is not traced; labels come from which prefabs use each value.
    /// </summary>
    public static string GrindTypeLabel(int type) => type switch
    {
        0 => "placed (railings, pipes, edges)",
        1 => "track-piece edge (generated)",
        3 => "wire / power line (generated)",
        4 => "wire, second kind (generated)",
        5 => "placed in Resort / Lagoon City ground",
        _ => $"type {type}",
    };
}
