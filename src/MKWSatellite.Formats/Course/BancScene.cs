using System.Numerics;
using MKWSatellite.Formats.Byml;

namespace MKWSatellite.Formats.Course;

/// <summary>
/// A placement scene from <c>romfs/Banc/*.bcett.byml.zs</c>: placed actors and graph rails (lap path,
/// AI routes, guide rails...). The original BYML node is kept on every object so nothing is lost
/// and saving back can be added later.
/// </summary>
public sealed class BancScene
{
    public const string RomfsDirectory = "Banc";

    public required string FilePath { get; init; }
    public required IReadOnlyList<BancActor> Actors { get; init; }
    public required IReadOnlyList<BancRail> Rails { get; init; }
    public required BymlDict Raw { get; init; }
    /// <summary>BYML version of the file (7 in 1.8), kept for saving.</summary>
    public ushort BymlVersion { get; init; } = 7;

    public static BancScene Parse(byte[] decompressedByml)
    {
        var reader = BymlReader.Parse(decompressedByml);
        var root = reader.Root as BymlDict ?? throw new InvalidDataException("Banc root is not a dictionary.");
        return FromRoot(root, reader.Version);
    }

    /// <summary>Wraps a (possibly edited) BYML tree. Actors and rails keep references into <paramref name="root"/>.</summary>
    public static BancScene FromRoot(BymlDict root, ushort bymlVersion = 7)
    {
        return new BancScene
        {
            BymlVersion = bymlVersion,
            FilePath = root.Get<string>("FilePath") ?? "",
            Actors = BancScene.Items(root.Array("Actors")).OfType<BymlDict>().Select(BancActor.From).ToList(),
            Rails = BancScene.Items(root.Array("Rails")).OfType<BymlDict>().Select(BancRail.From).ToList(),
            Raw = root,
        };
    }

    internal static IEnumerable<object?> Items(BymlArray? a) => a ?? Enumerable.Empty<object?>();

    internal static Vector3 Vec3(BymlArray? a) =>
        a is { Count: >= 3 } ? new Vector3(Convert.ToSingle(a[0]), Convert.ToSingle(a[1]), Convert.ToSingle(a[2])) : default;

    /// <summary>"Work/Gyml/ActorGParam/CmnObj_Coin000_A_00.gear__ActorGParam.gyml" -> "CmnObj_Coin000_A_00".</summary>
    internal static string ShortName(string? gyaml) =>
        gyaml is null ? "" : Path.GetFileName(gyaml).Split('.')[0];
}

public sealed record BancActor(string Type, string Name, ulong Hash, Vector3 Translate, Vector3 Rotate, Vector3 Scale, BymlDict? Dynamic, BymlDict Raw)
{
    internal static BancActor From(BymlDict d) => new(
        BancScene.ShortName(d.Get<string>("Gyaml")),
        d.Get<string>("Name") ?? "",
        d.Get<ulong>("Hash"),
        BancScene.Vec3(d.Array("Translate")),
        BancScene.Vec3(d.Array("Rotate")),
        d.Array("Scale") is { } s ? BancScene.Vec3(s) : Vector3.One,
        d.Dict("Dynamic"),
        d);
}

public sealed record BancRail(string Name, ulong Hash, bool IsClosed, IReadOnlyList<BancRailPoint> Points, BymlDict Raw)
{
    internal static BancRail From(BymlDict d) => new(
        d.Get<string>("Name") ?? "",
        d.Get<ulong>("Hash"),
        d.Get<bool>("IsClosed"),
        BancScene.Items(d.Array("Points")).OfType<BymlDict>().Select(BancRailPoint.From).ToList(),
        d);
}

/// <summary>A rail node. Links point at other nodes by <see cref="Hash"/> (graph edges, possibly across rails).</summary>
public sealed record BancRailPoint(ulong Hash, Vector3 Translation, Vector3 Rotate, BymlDict? Dynamic, IReadOnlyList<ulong> LinkTargets, BymlDict Raw)
{
    internal static BancRailPoint From(BymlDict d) => new(
        d.Get<ulong>("Hash"),
        BancScene.Vec3(d.Array("Translation")),
        BancScene.Vec3(d.Array("Rotate")),
        d.Dict("Dynamic"),
        BancScene.Items(d.Array("Links")).OfType<BymlDict>().Select(l => l.Get<ulong>("Dst")).ToList(),
        d);
}
