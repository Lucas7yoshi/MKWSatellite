using System.Numerics;
using MKWSatellite.Formats.Byml;
using MKWSatellite.Formats.Course;

namespace MKWSatellite.App.View;

/// <summary>Colours, as 0xAABBGGRR (the byte order PrimitiveBatch and ImGui both use).</summary>
public static class Palette
{
    public static uint Rgb(byte r, byte g, byte b, byte a = 0xFF) => (uint)(a << 24 | b << 16 | g << 8 | r);
    public static uint WithAlpha(uint c, byte a) => (c & 0x00FFFFFF) | (uint)a << 24;
    public static Vector4 ToVector(uint c) => new((c & 0xFF) / 255f, (c >> 8 & 0xFF) / 255f, (c >> 16 & 0xFF) / 255f, (c >> 24) / 255f);

    public static readonly uint Background = Rgb(14, 16, 24);
    public static readonly uint Selection = Rgb(255, 255, 255);
    public static readonly uint CheckpointGate = Rgb(255, 214, 10);
    public static readonly uint StartGate = Rgb(255, 255, 255);
    public static readonly uint LapCheckGate = Rgb(255, 120, 200);
    public static readonly uint Gate = Rgb(255, 145, 40);
    public static readonly uint Ruler = Rgb(120, 255, 160);

    public static uint ForCategory(ObjectCategory c) => c switch
    {
        ObjectCategory.ItemBox => Rgb(255, 196, 40),
        ObjectCategory.Coin => Rgb(250, 230, 90),
        ObjectCategory.DashPanel => Rgb(255, 120, 30),
        ObjectCategory.GlideRamp => Rgb(70, 210, 255),
        ObjectCategory.BoostRing => Rgb(255, 90, 230),
        ObjectCategory.JumpRamp => Rgb(180, 140, 255),
        ObjectCategory.StartPosition => Rgb(235, 235, 235),
        ObjectCategory.LapGate => Rgb(235, 235, 235),
        ObjectCategory.RespawnArea => Rgb(60, 220, 110),
        ObjectCategory.ValleyArea => Rgb(230, 60, 60),
        ObjectCategory.RecoveryPoint => Rgb(150, 255, 150),
        ObjectCategory.BoostArea => Rgb(255, 150, 40),
        ObjectCategory.RuleArea => Rgb(200, 90, 255),
        ObjectCategory.LinkArea => Rgb(255, 255, 120),
        ObjectCategory.HighSpeedArea => Rgb(40, 220, 220),
        ObjectCategory.OtherArea => Rgb(120, 120, 160),
        ObjectCategory.Hazard => Rgb(255, 70, 70),
        ObjectCategory.Fence => Rgb(130, 110, 90),
        ObjectCategory.Audience => Rgb(90, 90, 110),
        _ => Rgb(110, 110, 120),
    };

    public static uint ForRail(string kind) => kind switch
    {
        "LapPathGraph" => Rgb(255, 214, 10),
        "RoutePathGraph" => Rgb(80, 130, 255, 0xB0),
        "GuideGraphRail" => Rgb(120, 255, 120, 0xC0),
        "GravityPath" => Rgb(200, 120, 255, 0xC0),
        "GlidePath" => Rgb(70, 210, 255, 0xD0),
        "WaterFlowPath" => Rgb(40, 140, 255, 0xC0),
        _ => Rgb(160, 160, 160, 0x90),
    };

    // AI route colours by the flags on a route rail's first point (meanings from the key names; not traced in code).
    public static readonly uint RouteNormal = Rgb(70, 125, 255);
    public static readonly uint RoutePreferred = Rgb(60, 230, 255);
    public static readonly uint RouteLowPriority = Rgb(170, 110, 255);
    public static readonly uint RouteBulletBill = Rgb(255, 150, 90);
    /// <summary>Route stretch after an <c>IsForbidKillerFinish</c> point: a Bullet Bill can't end here.</summary>
    public static readonly uint BulletNoFinish = Rgb(255, 40, 60, 0x90);
    /// <summary>Route stretch where the look-ahead (sharp turn or ItemType ahead) stops a Bullet Bill ending.</summary>
    public static readonly uint BulletLookAhead = Rgb(255, 150, 30, 0x80);
    /// <summary>Lap progress zone between gates.</summary>
    public static readonly uint ProgressZone = Rgb(80, 190, 255);
    /// <summary>Route point with <c>IsKillerReleasable</c>.</summary>
    public static readonly uint BulletReleasable = Rgb(90, 255, 120);
    public static readonly uint Hover = Rgb(255, 255, 255, 0xC0);

    public static uint ForRoute(BymlDict? d)
    {
        if (d?.Get<bool>("IsOnlyKillerInItem") == true) return RouteBulletBill;
        var prio = d is not null && d.TryGetValue("RoutePriority", out var v) && v is int i ? i : 0;
        return prio < 0 ? RouteLowPriority : prio > 0 ? RoutePreferred : RouteNormal;
    }

    /// <summary>Rails drawn under "Other paths": everything but the lap path, AI routes and grind rails.</summary>
    public static bool ShowRail(string kind) => kind is not (RailKinds.Lap or RailKinds.Route) && !RailKinds.IsGrind(kind);

    // Grind rails by RailSlideType / kind.
    public static readonly uint GrindPlaced = Rgb(255, 120, 200);
    public static readonly uint GrindTrack = Rgb(255, 60, 140);
    public static readonly uint GrindWire = Rgb(255, 235, 90);
    public static readonly uint GrindOther = Rgb(190, 120, 255);
    public static readonly uint GrindFence = Rgb(230, 160, 110, 0xD0);

    public static uint ForGrind(CourseRail r) => r.Kind == RailKinds.FenceGrind ? GrindFence : r.GrindType switch
    {
        0 => GrindPlaced,
        1 => GrindTrack,
        3 or 4 => GrindWire,
        _ => GrindOther,
    };

    /// <summary>Height ramp: low = blue, mid = green/yellow, high = red.</summary>
    public static uint Height(float y, float min, float max)
    {
        var t = max - min < 1e-3f ? 0.5f : Math.Clamp((y - min) / (max - min), 0, 1);
        // blue -> cyan -> green -> yellow -> red
        Vector3[] stops = [new(0.2f, 0.4f, 1f), new(0.1f, 0.9f, 1f), new(0.2f, 1f, 0.3f), new(1f, 0.9f, 0.1f), new(1f, 0.25f, 0.2f)];
        var f = t * (stops.Length - 1);
        var i = Math.Min((int)f, stops.Length - 2);
        var c = Vector3.Lerp(stops[i], stops[i + 1], f - i);
        return Rgb((byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255));
    }

    /// <summary>Collision colour by gameplay material (PhiveConfig MaterialCollection name).</summary>
    public static uint ForCollisionMaterial(string m) => m switch
    {
        "Road" => Rgb(150, 152, 165),
        "LowDirt" => Rgb(185, 160, 105),
        "Dirt" => Rgb(160, 115, 60),
        "HighDirt" => Rgb(115, 75, 35),
        "Bumpy" => Rgb(205, 175, 70),
        "Slip" => Rgb(130, 205, 255),
        "HighSlip" => Rgb(60, 140, 255),
        "Wall" => Rgb(205, 85, 70),
        "LowWall" => Rgb(230, 145, 105),
        "BoundWall" => Rgb(240, 50, 50),
        "HPWall" => Rgb(255, 80, 170),
        "MobVehicleWall" => Rgb(190, 70, 120),
        "AirWall" => Rgb(235, 235, 255),
        "Dash" => Rgb(255, 140, 0),
        "DashGlide" => Rgb(255, 190, 0),
        "Glide" => Rgb(0, 225, 190),
        "KillerGlide" => Rgb(255, 95, 30),
        "Trampoline" => Rgb(225, 90, 255),
        "Gravity" => Rgb(150, 80, 255),
        "Water" => Rgb(40, 110, 235),
        "MuddyWater" => Rgb(120, 95, 55),
        "Shallows" => Rgb(90, 175, 230),
        "Lava" => Rgb(255, 60, 0),
        "Fall" => Rgb(40, 40, 40),
        "Valley" => Rgb(110, 20, 150),
        "Rescue" => Rgb(255, 0, 255),
        "Pole" => Rgb(240, 220, 80),
        "Pull" => Rgb(200, 200, 0),
        "Coin" or "ItemObj" or "Block" => Rgb(255, 220, 0),
        "Audience" => Rgb(120, 120, 120),
        _ => Rgb(175, 175, 175),
    };

    /// <summary>Collision colour by surface (SubMaterialCollection name); unknown names get a stable hashed colour.</summary>
    public static uint ForCollisionSurface(string s) => s switch
    {
        "Asphalt" => Rgb(85, 88, 98),
        "Concrete" => Rgb(170, 170, 165),
        "Stone" => Rgb(135, 125, 110),
        "Wood" => Rgb(170, 120, 70),
        "Kusa" => Rgb(80, 170, 60),
        "Soil" => Rgb(140, 95, 55),
        "Sand" => Rgb(225, 200, 130),
        "Snow" => Rgb(240, 245, 255),
        "Ice" => Rgb(150, 220, 255),
        "Iron" => Rgb(110, 135, 165),
        "Kanaami" => Rgb(150, 160, 120),
        "Glass" => Rgb(170, 230, 230),
        "Carpet" => Rgb(190, 60, 70),
        "Cloud" => Rgb(250, 250, 250),
        "Rainbow" => Rgb(255, 120, 230),
        "AirWall" => Rgb(235, 235, 255),
        "Mud" => Rgb(100, 75, 45),
        "Lava" => Rgb(255, 60, 0),
        _ => Hashed(s),
    };

    private static uint Hashed(string s)
    {
        var h = 0u;
        foreach (var ch in s) h = h * 31 + ch;
        return Rgb((byte)(90 + h % 150), (byte)(90 + h / 150 % 150), (byte)(90 + h / 22500 % 150));
    }
}
