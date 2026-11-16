using System.Text.RegularExpressions;
using MKWSatellite.Formats.Archives;
using MKWSatellite.Formats.Byml;
using MKWSatellite.Formats.IO;

namespace MKWSatellite.Formats.Text;

/// <summary>
/// In-game (English) names for scene files, from <c>Mals/USen/MsgMain.msarc</c> (falls back to <c>EUen</c>):
/// <list type="bullet">
/// <item>Races: <c>Course.msbt</c> label <c>Name_Race_&lt;Course&gt;_&lt;Variant&gt;_00</c>. Only one variant per course has a
/// label (e.g. <c>WestCity_D</c> = Crown City); the others get the course name plus their variant letter.</item>
/// <item>Course areas (for Intermission and Rally routes, <c>&lt;A&gt;To&lt;B&gt;</c>): <c>CourseArea.msbt</c>
/// <c>Name_&lt;Course&gt;</c> or <c>Name_&lt;Course&gt;_00</c>.</item>
/// <item>Battle stages: <c>BattleCourse.msbt</c>, numeric labels linked to stages by
/// <c>Gyml/BattleCourseSelectUIInfo/*.bgyml</c> (<c>UITextureBaseName</c> → <c>CourseMessageLabel</c>).</item>
/// <item>Missions: <c>Mission.msbt</c>, label = the scene name without <c>_Fix</c> (the mission's description).</item>
/// </list>
/// </summary>
public sealed partial class CourseNames
{
    private readonly Dictionary<string, string> _course = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _area = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _battle = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _mission = new(StringComparer.Ordinal);

    public int Count => _course.Count + _battle.Count + _mission.Count;

    public static CourseNames Load(GameFileSystem fs)
    {
        var names = new CourseNames();
        var path = new[] { "Mals/USen/MsgMain.msarc", "Mals/EUen/MsgMain.msarc" }.FirstOrDefault(fs.Exists);
        if (path is null) return names;
        var sarc = new Sarc(fs.Read(path));
        Dictionary<string, string> Msg(string file) =>
            sarc.TryGet(file, out var b) ? Msbt.Read(b.Span) : new Dictionary<string, string>();

        foreach (var (k, v) in Msg("Course.msbt"))
            if (RaceLabel().Match(k) is { Success: true } m) names._course[m.Groups[1].Value + "_" + m.Groups[2].Value] = v;
        foreach (var (k, v) in Msg("CourseArea.msbt"))
            if (k.StartsWith("Name_", StringComparison.Ordinal)) names._area[k[5..].Replace("_00", "")] = v;
        foreach (var (k, v) in Msg("Mission.msbt")) names._mission[k] = v;

        var battle = Msg("BattleCourse.msbt");
        foreach (var (stage, label) in BattleLabels(fs))
            if (battle.TryGetValue(label, out var n)) names._battle[stage] = n;
        return names;
    }

    /// <summary>Stage base name (e.g. <c>Volcano_00</c>) → BattleCourse message label, from the battle course select tables.</summary>
    private static IEnumerable<(string Stage, string Label)> BattleLabels(GameFileSystem fs)
    {
        const string dir = "System/GymlArchive";
        var archive = fs.ListFiles(dir, "general_format.*.sarc.zs").LastOrDefault();
        if (archive is null) yield break;
        var sarc = new Sarc(fs.Read($"{dir}/{archive}"));
        foreach (var file in sarc.FileNames.Where(f => f.StartsWith("Gyml/BattleCourseSelectUIInfo/", StringComparison.Ordinal)))
        {
            if (!sarc.TryGet(file, out var b) || BymlReader.Parse(b.ToArray()).Root is not BymlDict root) continue;
            foreach (var info in root.Array("CourseInfo")?.OfType<BymlDict>() ?? [])
                if (info.Get<string>("UITextureBaseName") is { } stage && info.Get<string>("CourseMessageLabel") is { } label)
                    yield return (stage, label);
        }
    }

    [GeneratedRegex(@"^Name_Race_([A-Za-z0-9]+)_([A-Z])_00$")]
    private static partial Regex RaceLabel();

    [GeneratedRegex(@"^Race_([A-Za-z0-9]+)_([A-Z])_(\d\d)")]
    private static partial Regex RaceFile();

    [GeneratedRegex(@"^(?:InterStageEx\d|LongRally)_([A-Za-z0-9]+?)(?:_([A-Z]))?_\d\d")]
    private static partial Regex RouteFile();

    [GeneratedRegex(@"^(?:Balloon|Coin|Bomb)Battle_(.+)\.bcett")]
    private static partial Regex BattleFile();

    private readonly Dictionary<string, string?> _cache = new(StringComparer.Ordinal);

    /// <summary>The in-game name for a scene file (<c>Banc/</c> file name), or null if none is known. Cached.</summary>
    public string? Of(string sceneFile)
    {
        lock (_cache)
        {
            if (!_cache.TryGetValue(sceneFile, out var name)) _cache[sceneFile] = name = Lookup(sceneFile);
            return name;
        }
    }

    private string? Lookup(string sceneFile)
    {
        if (RaceFile().Match(sceneFile) is { Success: true } race)
        {
            var (course, variant, number) = (race.Groups[1].Value, race.Groups[2].Value, race.Groups[3].Value);
            if (number == "00" && _course.TryGetValue($"{course}_{variant}", out var exact)) return exact;
            var any = _course.FirstOrDefault(kv => kv.Key.StartsWith(course + "_", StringComparison.Ordinal)).Value
                      ?? Area(course);
            return any is null ? null : $"{any} ({variant}{(number == "00" ? "" : " " + number)})";
        }
        if (RouteFile().Match(sceneFile) is { Success: true } route)
        {
            // "<A>To<B>": try each "To" so names that contain it (ToadFactory) still split right.
            var pair = route.Groups[1].Value;
            for (var i = pair.IndexOf("To", 1, StringComparison.Ordinal); i > 0; i = pair.IndexOf("To", i + 1, StringComparison.Ordinal))
                if (Area(pair[..i]) is { } a && Area(pair[(i + 2)..]) is { } b)
                    return $"{a} → {b}" + (route.Groups[2].Value is { Length: > 0 } v and not "A" ? $" ({v})" : "");
            return null;
        }
        if (BattleFile().Match(sceneFile) is { Success: true } battle)
        {
            var mode = sceneFile[..sceneFile.IndexOf("Battle_", StringComparison.Ordinal)];
            return _battle.TryGetValue(battle.Groups[1].Value, out var n) ? $"{n} ({mode})" : null;
        }
        if (sceneFile.StartsWith("Mission_", StringComparison.Ordinal))
        {
            var key = sceneFile.Replace(".bcett.byml.zs", "").Replace("_Fix", "");
            return _mission.TryGetValue(key, out var n) ? $"{key[8..]}: {n}" : null;
        }
        return null;
    }

    private string? Area(string course) => _area.TryGetValue(course, out var n) ? n
        : _course.FirstOrDefault(kv => kv.Key.StartsWith(course + "_", StringComparison.Ordinal)).Value;
}
