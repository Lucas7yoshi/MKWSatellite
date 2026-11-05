namespace MKWSatellite.Formats.Course;

/// <summary>
/// Maps actor types to display categories, using the developers' label from <see cref="ActorDatabase"/> first
/// (e.g. any label containing ダッシュ板 "dash board" is a dash panel, whatever the type name) and the type name as a
/// fallback. Categories drive colours and toggles only; gameplay behaviour still needs checking per type in code.
/// </summary>
public static class CourseObjectClassifier
{
    /// <summary>
    /// System/area types that matter for runners, with an English gloss of the developer label (our translation).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (ObjectCategory Category, string Gloss)> SystemTypes =
        new Dictionary<string, (ObjectCategory, string)>(StringComparer.Ordinal)
        {
            ["SystemObj_RescueArea_00"] = (ObjectCategory.RespawnArea, "Rescue area (RESQArea)"),
            ["SystemObj_RescueAreaMovable_00"] = (ObjectCategory.RespawnArea, "Rescue area (RESQArea), for route-change objects"),
            ["SystemObj_RespawnArea_00"] = (ObjectCategory.RespawnArea, "Area that designates a respawn point"),
            ["SystemObj_RespawnPoint_00"] = (ObjectCategory.RecoveryPoint, "Respawn point"),
            ["SystemObj_PrioRecoverPoint"] = (ObjectCategory.RecoveryPoint, "Priority Lakitu recovery point"),
            ["SystemObj_ValleyArea_00"] = (ObjectCategory.ValleyArea, "Valley area"),
            ["SystemObj_ValleyAreaMovable_00"] = (ObjectCategory.ValleyArea, "Valley area, for route-change objects"),
            ["SystemObj_HighSpeedArea_00"] = (ObjectCategory.HighSpeedArea, "High-speed area"),
            ["SystemObj_DashArea_00"] = (ObjectCategory.BoostArea, "Dash area (hitbox only, no visible panel)"),
            ["SystemObj_GlideArea_00"] = (ObjectCategory.BoostArea, "Glide area (hitbox only, no visible ramp)"),
            ["SystemObj_GlideAreaKiller_00"] = (ObjectCategory.BoostArea, "Glide area that only affects Bullet Bill"),
            ["SystemObj_ForbidReverseArea_00"] = (ObjectCategory.RuleArea, "Strictly no reverse driving area"),
            ["SystemObj_NoJAArea_00"] = (ObjectCategory.RuleArea, "Jump action forbidden area"),
            ["SystemObj_NoEntryArea_00"] = (ObjectCategory.RuleArea, "No entry area"),
            ["SystemObj_NoJugemAppearArea_00"] = (ObjectCategory.RuleArea, "No Lakitu appearance area (recovery still happens)"),
            ["SystemObj_HighDirtArea_00"] = (ObjectCategory.RuleArea, "High dirt area (HDIRTArea)"),
            ["SystemObj_FallLavaArea_00"] = (ObjectCategory.RuleArea, "Lava fall area"),
            ["SystemObj_WorldEndArea_00"] = (ObjectCategory.RuleArea, "World end area"),
            ["SystemObj_RideKeepOutArea_00"] = (ObjectCategory.RuleArea, "Ride keep-out area (wall)"),
            ["Area_ItemCrasher"] = (ObjectCategory.RuleArea, "Item destroying area (box)"),
            ["Area_DeleteLinkedActor_00"] = (ObjectCategory.LinkArea, "Area that deletes its link targets (for Rainbow)"),
            ["SystemObj_CreateLinkArea_00"] = (ObjectCategory.LinkArea, "Spawn-link area: its CreateLink targets appear when a racer enters, staggered by distance"),
            ["SystemObj_RainbowEnterPoint_00"] = (ObjectCategory.RecoveryPoint, "Rainbow entry designation (links to a lap path point)"),
            ["SystemObj_RainbowWarpPoint_00"] = (ObjectCategory.RecoveryPoint, "Rainbow warp destination (links to a lap path point)"),
            ["StartPosition"] = (ObjectCategory.StartPosition, "Start position"),
        };

    public static ObjectCategory Classify(string type, ActorInfo? info = null)
    {
        if (SystemTypes.TryGetValue(type, out var known)) return known.Category;

        var label = info?.Label ?? "";
        bool L(string s) => label.Contains(s, StringComparison.Ordinal);
        bool N(string s) => type.Contains(s, StringComparison.Ordinal);

        // Effects (lights, particles) are never gameplay objects, whatever their label mentions.
        if (info?.Category == "EffectObj" || type.StartsWith("EffectObj_", StringComparison.Ordinal)) return ObjectCategory.Other;

        // Label-based (developer descriptions), most specific first.
        if (L("アイテムボックス")) return ObjectCategory.ItemBox;                 // item box
        if (L("コイン") && !L("バトル")) return ObjectCategory.Coin;                // coin
        if (L("ダッシュ板") || L("ダッシュジャンプ台")) return ObjectCategory.DashPanel; // dash board / dash jump ramp
        if (L("グライド") && (L("板") || L("台"))) return ObjectCategory.GlideRamp;  // glide board / glide ramp
        if (L("加速リング") || L("星リング")) return ObjectCategory.BoostRing;         // acceleration ring / star ring
        if (L("ジャンプ台")) return ObjectCategory.JumpRamp;                        // jump ramp
        if (L("ラップゲート") || L("中間ゲート") || L("ゴールゲート")) return ObjectCategory.LapGate;

        // Name-based fallback (no database, or types the labels above don't cover).
        if (N("ItemBox")) return ObjectCategory.ItemBox;
        if (N("Coin") && !N("CoinBattle")) return ObjectCategory.Coin;
        if (N("DashBoard") || N("DashRamp") || N("DashPlate")) return ObjectCategory.DashPanel;
        if (N("GlideBoard") || N("GlideRamp") || N("GlidePlate")) return ObjectCategory.GlideRamp;
        if (N("AccelRing") || N("StarRing")) return ObjectCategory.BoostRing;
        if (N("JumpRamp") || N("JumpGap") || N("JumpFloor") || N("JumpLog") || N("JumpSoil")) return ObjectCategory.JumpRamp;
        if (N("LapGate") || N("GoalGate")) return ObjectCategory.LapGate;
        if (type.StartsWith("Area_", StringComparison.Ordinal) || (type.StartsWith("SystemObj_", StringComparison.Ordinal) && N("Area")))
            return ObjectCategory.OtherArea;
        if (N("Fence") || N("Barrier") || N("Barricad") || N("Pylon") || N("Buoy") || N("Bollard") || N("Guardrail"))
            return ObjectCategory.Fence;
        if (N("Audience") || N("Cheering") || N("CheerStick")) return ObjectCategory.Audience;
        if (N("Mobcar") || N("Mobship") || N("Mobplane") || N("Packun") || N("Pukupuku") || N("Dossun")
            || N("Teresa") || N("Cow000") || N("Bull000") || N("FireSnake") || N("Flapper")
            || N("Geyser") || N("Poo000") || N("Tokotoko") || N("Patadon") || N("Poihana") || N("Penguin000")
            || N("SnowBoarder") || N("Forklift") || N("TRex") || N("Triceratops") || N("Teren"))
            return ObjectCategory.Hazard;
        return ObjectCategory.Other;
    }

    /// <summary>English gloss for known system types, else null (show the Japanese label).</summary>
    public static string? Gloss(string type) => SystemTypes.TryGetValue(type, out var k) ? k.Gloss : null;
}
