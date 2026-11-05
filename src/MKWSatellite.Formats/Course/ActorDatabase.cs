using System.Text.RegularExpressions;
using MKWSatellite.Formats.Byml;
using MKWSatellite.Formats.IO;

namespace MKWSatellite.Formats.Course;

/// <summary>One row of the game's actor database: category and the developers' (Japanese) label.</summary>
public sealed record ActorInfo(string Type, string Category, string Label);

/// <summary>
/// The game's actor table, <c>RSDB/Actor.Product.NNN.rstbl.byml.zs</c> (4,826 rows in 1.8): every actor type with a
/// category (StaticObj, PropObj, SystemObj, ...) and a descriptive Japanese label written by the developers, e.g.
/// <c>Area_DeleteLinkedActor_00</c> = 「リンク先を削除するエリア（レインボー用）」. The labels are the most reliable
/// statement of what a type is for, so classification is based on them.
/// </summary>
public sealed partial class ActorDatabase
{
    private readonly Dictionary<string, ActorInfo> _rows;

    private ActorDatabase(Dictionary<string, ActorInfo> rows) => _rows = rows;

    public static ActorDatabase Empty { get; } = new([]);

    public int Count => _rows.Count;

    public ActorInfo? Get(string type) => _rows.GetValueOrDefault(type);

    /// <summary>Loads the newest <c>Actor.Product.*</c> table found in RSDB, or an empty database if there is none.</summary>
    public static ActorDatabase Load(GameFileSystem fs)
    {
        var file = fs.ListFiles("RSDB", "Actor.Product.*.rstbl.byml.zs")
            .OrderByDescending(f => VersionRegex().Match(f) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0)
            .FirstOrDefault();
        return file is null ? Empty : Parse(fs.Read($"RSDB/{file}"));
    }

    public static ActorDatabase Parse(byte[] byml)
    {
        var rows = new Dictionary<string, ActorInfo>(StringComparer.Ordinal);
        if (BymlReader.Parse(byml).Root is BymlArray arr)
            foreach (var r in arr.OfType<BymlDict>())
            {
                // __RowId: "Work/Gyml/ActorGParam/<Type>.gear__ActorGParam.gyml"
                var type = BancScene.ShortName(r.Get<string>("__RowId"));
                if (type.Length > 0) rows[type] = new ActorInfo(type, r.Get<string>("Category") ?? "", r.Get<string>("Label") ?? "");
            }
        return new ActorDatabase(rows);
    }

    [GeneratedRegex(@"\.Product\.(\d+)\.")]
    private static partial Regex VersionRegex();
}
