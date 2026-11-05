using System.CommandLine;
using MKWSatellite.Formats.Configuration;
using MKWSatellite.Formats.Course;
using MKWSatellite.Formats.IO;

var romfsOption = new Option<string[]>("--romfs")
{
    Description = $"Extracted romfs directory (repeat for base then update). Defaults to ${SatelliteSettings.EnvVar} or {SatelliteSettings.FileName}.",
    Recursive = true,
    AllowMultipleArgumentsPerToken = true,
};

var root = new RootCommand("MKWSatellite command-line tools");
root.Options.Add(romfsOption);

// info: what dump is configured and which content version it is
var info = new Command("info", "Show the configured romfs layers and detected resource version.");
info.SetAction(parse =>
{
    var fs = OpenFs(parse.GetValue(romfsOption));
    if (fs is null) return 1;
    for (var i = 0; i < fs.Layers.Count; i++) Console.WriteLine($"layer {i}: {fs.Layers[i]}");
    var v = fs.GetResourceVersion();
    Console.WriteLine($"resource version: {(v is null ? "unknown" : $"Product.{v} ({v / 100}.{v / 10 % 10}.{v % 10})")}");
    Console.WriteLine($"Banc scenes: {fs.ListFiles(BancScene.RomfsDirectory, "*.bcett.byml.zs").Count}");
    return 0;
});
root.Subcommands.Add(info);

// banc: summarise one placement scene
var bancName = new Argument<string>("name") { Description = "File name in romfs/Banc, e.g. \"Race_Cinema_A_00[Game03].bcett.byml.zs\"." };
var banc = new Command("banc", "Summarise a Banc placement scene (actors and rails by type).") { bancName };
banc.SetAction(parse =>
{
    var fs = OpenFs(parse.GetValue(romfsOption));
    if (fs is null) return 1;
    var scene = BancScene.Parse(fs.Read($"{BancScene.RomfsDirectory}/{parse.GetValue(bancName)}"));
    Console.WriteLine($"{scene.FilePath}: {scene.Actors.Count} actors, {scene.Rails.Count} rails");
    foreach (var g in scene.Actors.GroupBy(a => a.Type).OrderByDescending(g => g.Count()))
        Console.WriteLine($"  {g.Count(),5}  {g.Key}");
    foreach (var g in scene.Rails.GroupBy(r => r.Name).OrderByDescending(g => g.Count()))
        Console.WriteLine($"  {g.Count(),5}  rail {g.Key} ({g.Sum(r => r.Points.Count)} points)");
    return 0;
});
root.Subcommands.Add(banc);

// roundtrip: check the BYML writer reproduces every scene (read -> write -> read gives an equal tree)
var rtFilter = new Option<string>("--filter") { Description = "Only scenes whose file name contains this text." };
var rtDir = new Option<string>("--dir") { Description = "romfs directory.", DefaultValueFactory = _ => BancScene.RomfsDirectory };
var roundtrip = new Command("roundtrip", "Read, re-write and re-read BYML scenes; report any difference.") { rtFilter, rtDir };
roundtrip.SetAction(parse =>
{
    var fs = OpenFs(parse.GetValue(romfsOption));
    if (fs is null) return 1;
    var dir = parse.GetValue(rtDir)!;
    var filter = parse.GetValue(rtFilter) ?? "";
    var files = fs.ListFiles(dir, "*.byml.zs").Where(f => f.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    var bad = 0;
    long origTotal = 0, newTotal = 0;
    foreach (var f in files)
    {
        var data = fs.Read($"{dir}/{f}");
        var r = MKWSatellite.Formats.Byml.BymlReader.Parse(data);
        var written = MKWSatellite.Formats.Byml.BymlWriter.Write(r.Root!, r.Version);
        var back = MKWSatellite.Formats.Byml.BymlReader.Parse(written);
        var used = data.Length - data.Reverse().TakeWhile(b => b == 0).Count();
        origTotal += used; newTotal += written.Length;
        if (MKWSatellite.Formats.Byml.BymlCompare.FirstDifference(r.Root, back.Root) is { } diff)
        {
            bad++;
            Console.WriteLine($"DIFF {f}: {diff}");
        }
        else if (MKWSatellite.Formats.Byml.BymlWriter.PadToPowerOfTwo(written).Length > data.Length)
            Console.WriteLine($"grows past original padded size: {f} ({written.Length} > {data.Length})");
    }
    Console.WriteLine($"{files.Count} files, {bad} different; data {origTotal} -> {newTotal} bytes");
    return bad == 0 ? 0 : 2;
});
root.Subcommands.Add(roundtrip);

// scan: parse every Banc scene, report failures and a census of actor types, rail kinds and Dynamic keys
var scanFilter = new Option<string>("--filter") { Description = "Only scenes whose file name contains this text (e.g. Race_)." };
var scanOut = new Option<string?>("--out") { Description = "Write the full census to this text file." };
var scanDir = new Option<string>("--dir") { Description = "romfs directory to scan.", DefaultValueFactory = _ => BancScene.RomfsDirectory };
var scan = new Command("scan", "Parse all Banc scenes and print a census of what they contain.") { scanFilter, scanOut, scanDir };
scan.SetAction(parse =>
{
    var fs = OpenFs(parse.GetValue(romfsOption));
    if (fs is null) return 1;
    var filter = parse.GetValue(scanFilter) ?? "";
    var dir = parse.GetValue(scanDir)!;
    var files = fs.ListFiles(dir, "*.bcett.byml.zs").Where(f => f.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    var census = new SceneCensus();
    var failures = new List<string>();
    foreach (var f in files)
    {
        try { census.Add(f, BancScene.Parse(fs.Read($"{dir}/{f}"))); }
        catch (Exception e) { failures.Add($"{f}: {e.GetType().Name}: {e.Message}"); }
    }
    Console.WriteLine($"{files.Count} scenes, {failures.Count} failed");
    foreach (var f in failures) Console.WriteLine($"  FAIL {f}");
    var report = census.Report();
    if (parse.GetValue(scanOut) is { } outPath) { File.WriteAllText(outPath, report); Console.WriteLine($"census written to {outPath}"); }
    else Console.Write(report);
    return failures.Count == 0 ? 0 : 2;
});
root.Subcommands.Add(scan);

// dump: print any (optionally zstd-compressed) BYML file as an indented tree
var dumpPath = new Argument<string>("path") { Description = "romfs-relative path, e.g. Banc/PathAdditionalData/Race_Cinema_A_00.byml" };
var dumpDepth = new Option<int>("--depth") { Description = "Maximum depth to print.", DefaultValueFactory = _ => 6 };
var dumpItems = new Option<int>("--items") { Description = "Maximum array items to print per array.", DefaultValueFactory = _ => 4 };
var dump = new Command("dump", "Print a BYML file as an indented tree.") { dumpPath, dumpDepth, dumpItems };
dump.SetAction(parse =>
{
    var fs = OpenFs(parse.GetValue(romfsOption));
    if (fs is null) return 1;
    var byml = MKWSatellite.Formats.Byml.BymlReader.Parse(fs.Read(parse.GetValue(dumpPath)!));
    Console.WriteLine($"BYML v{byml.Version}");
    BymlText.Write(Console.Out, byml.Root, parse.GetValue(dumpDepth), parse.GetValue(dumpItems));
    return 0;
});
root.Subcommands.Add(dump);

// course: assemble a race course from its layers and summarise it
var courseName = new Argument<string>("race") { Description = "Race scene file in romfs/Banc, e.g. \"Race_Cinema_A_00[Game03].bcett.byml.zs\" (or a unique substring)." };
var courseCmd = new Command("course", "Assemble a race course (race scene + static layer) and summarise it.") { courseName };
courseCmd.SetAction(parse =>
{
    var fs = OpenFs(parse.GetValue(romfsOption));
    if (fs is null) return 1;
    var loader = new CourseLoader(fs);
    var arg = parse.GetValue(courseName)!;
    var matches = loader.ListRaceScenes().Where(f => f.Contains(arg, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1) { Console.Error.WriteLine($"{matches.Count} race scenes match \"{arg}\": {string.Join(", ", matches)}"); return 1; }
    var c = loader.Load(matches[0], new Progress<string>(s => Console.Error.WriteLine(s)));
    Console.WriteLine($"{c.Name}: static layer {c.StaticLayerSource}");
    Console.WriteLine($"path bounds {c.PathBounds.Min} .. {c.PathBounds.Max}");
    foreach (var g in c.Objects.GroupBy(o => (o.Layer, o.Category)).OrderBy(g => g.Key.Layer).ThenBy(g => g.Key.Category))
        Console.WriteLine($"  {g.Key.Layer,-9} {g.Key.Category,-14} {g.Count(),5}  e.g. {string.Join(", ", g.Select(o => o.Type).Distinct().Take(3))}");
    foreach (var g in c.Rails.GroupBy(r => (r.Layer, r.Kind)).OrderBy(g => g.Key.Layer))
        Console.WriteLine($"  {g.Key.Layer,-9} rail {g.Key.Kind,-18} {g.Count(),4} rails, {g.Sum(r => r.Rail.Points.Count)} points");

    // Gate orientation check: angle between each gate's local Z (its normal) and the direction to the next lap point.
    var angles = new List<double>();
    foreach (var lp in c.LapPoints)
    {
        var pts = lp.Rail.Rail.Points;
        if (lp.Index + 1 >= pts.Count) continue;
        var dir = System.Numerics.Vector3.Normalize(pts[lp.Index + 1].Translation - lp.Position);
        var n = System.Numerics.Vector3.TransformNormal(System.Numerics.Vector3.UnitZ, CourseMath.EulerXyz(lp.Point.Rotate));
        angles.Add(Math.Acos(Math.Clamp(System.Numerics.Vector3.Dot(dir, n), -1, 1)) * 180 / Math.PI);
    }
    angles.Sort();
    if (angles.Count > 0)
        Console.WriteLine($"lap gate normal vs path direction: median {angles[angles.Count / 2]:0.0} deg, p90 {angles[angles.Count * 9 / 10]:0.0} deg, n={angles.Count}");
    // Same check for boost objects: local +Z vs the direction of the nearest lap segment.
    var lapSegs = c.LapPoints.Where(l => l.Index + 1 < l.Rail.Rail.Points.Count)
        .Select(l => (A: l.Position, B: l.Rail.Rail.Points[l.Index + 1].Translation)).ToList();
    foreach (var o in c.Objects.Where(o => o.Category is ObjectCategory.DashPanel or ObjectCategory.GlideRamp or ObjectCategory.BoostRing or ObjectCategory.StartPosition))
    {
        if (lapSegs.Count == 0) break;
        var seg = lapSegs.MinBy(s => System.Numerics.Vector3.Distance((s.A + s.B) / 2, o.Position));
        var dir = System.Numerics.Vector3.Normalize(seg.B - seg.A);
        var fwd = System.Numerics.Vector3.TransformNormal(System.Numerics.Vector3.UnitZ, o.Rotation);
        var ang = Math.Acos(Math.Clamp(System.Numerics.Vector3.Dot(dir, fwd), -1, 1)) * 180 / Math.PI;
        var dist = System.Numerics.Vector3.Distance((seg.A + seg.B) / 2, o.Position);
        if (o.Category != ObjectCategory.StartPosition || o.Actor.Dynamic?.Get<int>("StartPositionId") == 1)
            Console.WriteLine($"  facing {o.Category,-13} {o.Type,-36} {ang,6:0.0} deg from lap direction (lap segment {dist:0} u away)");
    }
    var cps = c.LapPoints.Where(l => l.CheckPoint is not null).ToList();
    Console.WriteLine($"lap points {c.LapPoints.Count}, with CheckPoint {cps.Count}, start {c.LapPoints.Count(l => l.IsStart)}, goal {c.LapPoints.Count(l => l.IsGoal)}, lapcheck {c.LapPoints.Count(l => l.IsLapCheck)}");
    return 0;
});
root.Subcommands.Add(courseCmd);

// tex: decode a BNTX texture to PNG (mip 0, first layer)
var texPath = new Argument<string>("path") { Description = "romfs-relative BNTX, e.g. Tex/Stage_Cinema_A_RoofExterior_Alb.bntx.zs" };
var texOut = new Argument<string>("out") { Description = "Output PNG path." };
var texMip = new Option<int>("--mip") { Description = "Mip level to decode.", DefaultValueFactory = _ => 0 };
var texCmd = new Command("tex", "Decode a BNTX texture to PNG.") { texPath, texOut, texMip };
texCmd.SetAction(parse =>
{
    var fs = OpenFs(parse.GetValue(romfsOption));
    if (fs is null) return 1;
    foreach (var t in MKWSatellite.Formats.Textures.Bntx.Parse(fs.Read(parse.GetValue(texPath)!)))
    {
        Console.WriteLine($"{t.Name}: {t.Width}x{t.Height} {t.Format} mips {t.MipCount} array {t.ArrayLength} tile {t.TileMode} bh 2^{t.BlockHeightLog2}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var level = parse.GetValue(texMip);
        var rgba = t.DecodeMip(level);
        Console.WriteLine($"  decoded in {sw.ElapsedMilliseconds} ms");
        if (rgba is null) { Console.Error.WriteLine("  format not supported"); return 2; }
        MKWSatellite.Formats.Imaging.PngWriter.Write(parse.GetValue(texOut)!, t.MipWidth(level), t.MipHeight(level), rgba);
        break;
    }
    return 0;
});
root.Subcommands.Add(texCmd);

// model: resolve an actor type (or take a romfs path) and summarise its BFRES
var modelArg = new Argument<string>("actor-or-path") { Description = "Actor type (e.g. Stage_Cinema_A_00) or romfs path (Model/....bfres.zs)." };
var modelCmd = new Command("model", "Summarise the model an actor uses (shapes, materials, textures).") { modelArg };
modelCmd.SetAction(parse =>
{
    var fs = OpenFs(parse.GetValue(romfsOption));
    if (fs is null) return 1;
    var arg = parse.GetValue(modelArg)!;
    var path = arg.Contains('/') ? arg : new MKWSatellite.Formats.Models.ActorModelResolver(fs).ModelPath(arg);
    if (path is null) { Console.Error.WriteLine($"{arg}: no model"); return 1; }
    Console.WriteLine(path);
    foreach (var m in MKWSatellite.Formats.Models.Bfres.Parse(fs.Read(path)))
    {
        var tris = m.Shapes.Sum(s => s.Indices.Length / 3);
        var verts = m.Shapes.SelectMany(s => s.Positions).ToList();
        Console.WriteLine($"{m.Name}: {m.Bones.Count} bones, {m.Shapes.Count} shapes, {m.Materials.Count} materials, {tris} triangles");
        if (verts.Count > 0) Console.WriteLine($"  bounds {verts.Aggregate(System.Numerics.Vector3.Min)} .. {verts.Aggregate(System.Numerics.Vector3.Max)}");
        foreach (var s in m.Shapes.Take(12))
            Console.WriteLine($"  shape {s.Name}: {s.Positions.Length} verts, {s.Indices.Length / 3} tris, skin {s.SkinCount}, uv0 {(s.Uv0 is null ? "no" : "yes")}, material {m.Materials[s.MaterialIndex].Name} albedo {m.Materials[s.MaterialIndex].Albedo ?? "-"}");
        var missing = m.Materials.Select(x => x.Albedo).OfType<string>().Distinct().Where(t => !fs.Exists($"Tex/{t}.bntx.zs")).ToList();
        Console.WriteLine($"  albedo textures: {m.Materials.Select(x => x.Albedo).OfType<string>().Distinct().Count()}, missing files: {missing.Count} {string.Join(", ", missing.Take(5))}");
    }
    return 0;
});
root.Subcommands.Add(modelCmd);

return root.Parse(args).Invoke();

static GameFileSystem? OpenFs(string[]? romfs)
{
    var settings = SatelliteSettings.Load(romfs);
    if (!settings.IsConfigured)
    {
        Console.Error.WriteLine($"No romfs configured. Pass --romfs <dir>, set {SatelliteSettings.EnvVar}, or create {SatelliteSettings.FileName} (see mkwsat.local.example.json).");
        return null;
    }
    return new GameFileSystem(settings.Romfs);
}
