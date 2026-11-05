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
    public static readonly uint Gate = Rgb(150, 150, 120, 0xA0);
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

    public static uint ForRoute(BymlDict? d)
    {
        if (d?.Get<bool>("IsOnlyKillerInItem") == true) return RouteBulletBill;
        var prio = d is not null && d.TryGetValue("RoutePriority", out var v) && v is int i ? i : 0;
        return prio < 0 ? RouteLowPriority : prio > 0 ? RoutePreferred : RouteNormal;
    }

    /// <summary>Rails worth drawing under "Other rails"; editor/audience helper rails are skipped.</summary>
    public static bool ShowRail(string kind) => kind is "GuideGraphRail" or "GravityPath" or "GlidePath" or "WaterFlowPath" or "PullPath";

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
}
