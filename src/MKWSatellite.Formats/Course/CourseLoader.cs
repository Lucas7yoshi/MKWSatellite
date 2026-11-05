using System.Numerics;
using System.Text.RegularExpressions;
using MKWSatellite.Formats.IO;

namespace MKWSatellite.Formats.Course;

/// <summary>
/// Builds a <see cref="CourseScene"/> from a race scene plus its static layer, following the verified layering in
/// docs/formats/scene-layers.md: race scene + (<c>&lt;Course&gt;_00_Fix</c> if it exists, otherwise base-world tiles
/// clipped to the race paths' 3D bounds). Never uses the free-roam <c>-Free</c> world.
/// World tiles are parsed once and cached, so loading a second course is fast.
/// </summary>
public sealed partial class CourseLoader(GameFileSystem fs)
{
    public const string WorldDirectory = "Banc/World_Game03";

    /// <summary>Margin around the race paths when picking world objects (units). Y margin keeps ground scenery out of sky courses.</summary>
    public Vector3 WorldMargin { get; set; } = new(250, 250, 250);

    private List<BancScene>? _worldTiles;
    private ActorDatabase? _database;

    /// <summary>The game's actor table (labels/categories), loaded once.</summary>
    public ActorDatabase Database => _database ??= ActorDatabase.Load(fs);

    private CourseObject Make(BancActor a, CourseLayer layer)
    {
        var info = Database.Get(a.Type);
        return new CourseObject(a, layer, CourseObjectClassifier.Classify(a.Type, info), info);
    }

    /// <summary>Race scene file names (in <c>Banc/</c>), e.g. <c>Race_Cinema_A_00[Game03].bcett.byml.zs</c>.</summary>
    /// <summary>Race scenes, then the Intermission (<c>InterStage_*</c>) and Rally (<c>LongRally_*</c>) connecting routes.</summary>
    public IReadOnlyList<string> ListRaceScenes() =>
        [.. fs.ListFiles(BancScene.RomfsDirectory, "Race_*.bcett.byml.zs"),
         .. fs.ListFiles(BancScene.RomfsDirectory, "InterStage*.bcett.byml.zs"),
         .. fs.ListFiles(BancScene.RomfsDirectory, "LongRally*.bcett.byml.zs")];

    /// <summary>"Race_Cinema_A_00[Game03].bcett.byml.zs" -> "Cinema".</summary>
    public static string CourseNameOf(string raceFile) => CourseNameRegex().Match(raceFile) is { Success: true } m ? m.Groups[1].Value : raceFile;

    [GeneratedRegex(@"^Race_([A-Za-z0-9]+)_")]
    private static partial Regex CourseNameRegex();

    /// <summary>
    /// Rebuilds a course after its race scene was edited: race-layer objects, rails and lap points come from
    /// <paramref name="race"/>; the static layer (world tiles or _Fix) is reused as is.
    /// </summary>
    public CourseScene RebuildRace(CourseScene course, BancScene race)
    {
        var objects = race.Actors.Select(a => Make(a, CourseLayer.Race)).ToList();
        var byHash = objects.GroupBy(o => o.Actor.Hash).ToDictionary(g => g.Key, g => g.First());
        foreach (var o in objects)
            foreach (var (dst, name) in o.OutgoingLinks)
                if (byHash.TryGetValue(dst, out var target)) target.IncomingLinks.Add(new IncomingLink(o, name));
        objects.AddRange(course.Objects.Where(o => o.Layer != CourseLayer.Race));
        var rails = race.Rails.Select(r => new CourseRail(r, CourseLayer.Race)).ToList();
        rails.AddRange(course.Rails.Where(r => r.Layer != CourseLayer.Race));
        var lap = rails.Where(r => r.Kind == "LapPathGraph" && r.Layer == CourseLayer.Race)
            .SelectMany(r => r.Rail.Points.Select((p, i) => new LapPoint(p, r, i))).ToList();
        return new CourseScene
        {
            Name = course.Name,
            RaceScene = race,
            StaticLayerSource = course.StaticLayerSource,
            Objects = objects,
            Rails = rails,
            LapPoints = lap,
            PathBounds = course.PathBounds,
            Database = course.Database,
        };
    }

    public CourseScene Load(string raceFile, IProgress<string>? progress = null)
    {
        var race = BancScene.Parse(fs.Read($"{BancScene.RomfsDirectory}/{raceFile}"));
        var rails = race.Rails.Select(r => new CourseRail(r, CourseLayer.Race)).ToList();
        var objects = race.Actors.Select(a => Make(a, CourseLayer.Race)).ToList();

        var pathPoints = race.Rails.Where(r => r.Name is "LapPathGraph" or "RoutePathGraph").SelectMany(r => r.Points).Select(p => p.Translation).ToList();
        if (pathPoints.Count == 0) pathPoints = race.Actors.Select(a => a.Translate).ToList();
        var bounds = pathPoints.Count == 0
            ? (Vector3.Zero, Vector3.Zero)
            : (pathPoints.Aggregate(Vector3.Min), pathPoints.Aggregate(Vector3.Max));

        string staticSource;
        var course = CourseNameOf(raceFile);
        var fixPath = $"{BancScene.RomfsDirectory}/{course}_00_Fix.bcett.byml.zs";
        if (fs.Exists(fixPath))
        {
            progress?.Report($"Loading {course}_00_Fix");
            var fix = BancScene.Parse(fs.Read(fixPath));
            objects.AddRange(fix.Actors.Select(a => Make(a, CourseLayer.CourseFix)));
            rails.AddRange(fix.Rails.Select(r => new CourseRail(r, CourseLayer.CourseFix)));
            staticSource = $"{course}_00_Fix";
        }
        else
        {
            var min = bounds.Item1 - WorldMargin;
            var max = bounds.Item2 + WorldMargin;
            bool Inside(Vector3 p) => p.X >= min.X && p.Y >= min.Y && p.Z >= min.Z && p.X <= max.X && p.Y <= max.Y && p.Z <= max.Z;
            var tilesUsed = 0;
            foreach (var tile in WorldTiles(progress))
            {
                var used = false;
                foreach (var a in tile.Actors.Where(a => Inside(a.Translate)))
                {
                    objects.Add(Make(a, CourseLayer.World));
                    used = true;
                }
                // World rails (glide/gravity/water paths) are kept when any point is inside the course bounds.
                foreach (var r in tile.Rails.Where(r => r.Points.Any(p => Inside(p.Translation))))
                {
                    rails.Add(new CourseRail(r, CourseLayer.World));
                    used = true;
                }
                if (used) tilesUsed++;
            }
            staticSource = $"World_Game03 ({tilesUsed} tiles)";
        }

        // Resolve links between objects (links never cross scene layers in the shipped data, but resolve globally anyway).
        var byHash = objects.GroupBy(o => o.Actor.Hash).ToDictionary(g => g.Key, g => g.First());
        foreach (var o in objects)
            foreach (var (dst, name) in o.OutgoingLinks)
                if (byHash.TryGetValue(dst, out var target)) target.IncomingLinks.Add(new IncomingLink(o, name));

        var lap = rails.Where(r => r.Kind == "LapPathGraph" && r.Layer == CourseLayer.Race)
            .SelectMany(r => r.Rail.Points.Select((p, i) => new LapPoint(p, r, i))).ToList();

        return new CourseScene
        {
            Name = raceFile.Replace(".bcett.byml.zs", ""),
            RaceScene = race,
            StaticLayerSource = staticSource,
            Objects = objects,
            Rails = rails,
            LapPoints = lap,
            PathBounds = bounds,
            Database = Database,
        };
    }

    private List<BancScene> WorldTiles(IProgress<string>? progress)
    {
        if (_worldTiles is not null) return _worldTiles;
        var files = fs.ListFiles(WorldDirectory, "*.bcett.byml.zs");
        var tiles = new BancScene[files.Count];
        var done = 0;
        Parallel.For(0, files.Count, i =>
        {
            tiles[i] = BancScene.Parse(fs.Read($"{WorldDirectory}/{files[i]}"));
            var n = Interlocked.Increment(ref done);
            if (n % 20 == 0) progress?.Report($"World tiles {n}/{files.Count}");
        });
        return _worldTiles = tiles.ToList();
    }
}
