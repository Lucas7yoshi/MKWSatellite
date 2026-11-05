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

// ground: measure how far lap nodes and areas sit above the course geometry (Stage models, as the viewer places them)
var groundCourse = new Argument<string>("course") { Description = "Race scene name filter, e.g. AirshipFortress_A." };
var groundCmd = new Command("ground", "Vertical distance from lap nodes and areas to the course models below/above them.") { groundCourse };
groundCmd.SetAction(parse =>
{
    var fs = OpenFs(parse.GetValue(romfsOption));
    if (fs is null) return 1;
    var loader = new CourseLoader(fs);
    var file = loader.ListRaceScenes().FirstOrDefault(f => f.Contains(parse.GetValue(groundCourse)!, StringComparison.OrdinalIgnoreCase));
    if (file is null) { Console.Error.WriteLine("no such course"); return 1; }
    var course = loader.Load(file);
    var resolver = new MKWSatellite.Formats.Models.ActorModelResolver(fs);
    var tris = new List<(System.Numerics.Vector3 A, System.Numerics.Vector3 B, System.Numerics.Vector3 C)>();
    foreach (var o in course.Objects.Where(o => o.Type.StartsWith("Stage_", StringComparison.Ordinal)))
    {
        if (resolver.ModelPath(o.Type) is not { } path) continue;
        var world = System.Numerics.Matrix4x4.CreateScale(o.Actor.Scale) * o.Rotation * System.Numerics.Matrix4x4.CreateTranslation(o.Position);
        foreach (var m in MKWSatellite.Formats.Models.Bfres.Parse(fs.Read(path)))
            foreach (var sh in m.Shapes)
                for (var i = 0; i + 2 < sh.Indices.Length; i += 3)
                    tris.Add((System.Numerics.Vector3.Transform(sh.Positions[sh.Indices[i]], world),
                              System.Numerics.Vector3.Transform(sh.Positions[sh.Indices[i + 1]], world),
                              System.Numerics.Vector3.Transform(sh.Positions[sh.Indices[i + 2]], world)));
        Console.WriteLine($"model {o.Type} at {o.Position}");
    }
    Console.WriteLine($"{tris.Count} triangles");
    // Heights of all surfaces crossing the vertical line through (x, z).
    List<float> Hits(float x, float z)
    {
        var hs = new List<float>();
        foreach (var (a, b, c) in tris)
        {
            var d = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
            if (MathF.Abs(d) < 1e-9f) continue;
            var l1 = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / d;
            var l2 = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / d;
            var l3 = 1 - l1 - l2;
            if (l1 < 0 || l2 < 0 || l3 < 0) continue;
            hs.Add(l1 * a.Y + l2 * b.Y + l3 * c.Y);
        }
        return hs;
    }
    string Rel(float y, List<float> hs)
    {
        var below = hs.Where(h => h <= y + 0.01f).DefaultIfEmpty(float.NaN).Max();
        var above = hs.Where(h => h > y + 0.01f).DefaultIfEmpty(float.NaN).Min();
        return $"surface below {y - below,7:0.00}  above {above - y,7:0.00}";
    }
    foreach (var lp in course.LapPoints)
    {
        var p = lp.Position;
        Console.WriteLine($"lap {lp.Index,3} y {p.Y,8:0.0} size {lp.RectSize.X,5:0.#}x{lp.RectSize.Y,5:0.#} off {lp.RectOffset.X,6:0.##},{lp.RectOffset.Y,6:0.##}  {Rel(p.Y, Hits(p.X, p.Z))}");
    }
    foreach (var o in course.Objects.Where(o => o.Category is ObjectCategory.HighSpeedArea or ObjectCategory.RespawnArea or ObjectCategory.ValleyArea or ObjectCategory.BoostArea))
    {
        var p = o.Position;
        Console.WriteLine($"{o.Type,-36} y {p.Y,8:0.0} scaleY {o.Actor.Scale.Y,7:0.#}  {Rel(p.Y, Hits(p.X, p.Z))}");
    }
    return 0;
});
root.Subcommands.Add(groundCmd);

// areafit: how much of the lap path each area contains, under the physics shape vs the old editor-shape guess
var fitCourse = new Argument<string>("course") { Description = "Race scene name filter." };
var fitCmd = new Command("areafit", "Fraction of lap-path samples inside each area (physics box vs old 1-unit guess).") { fitCourse };
fitCmd.SetAction(parse =>
{
    var fs = OpenFs(parse.GetValue(romfsOption));
    if (fs is null) return 1;
    var loader = new CourseLoader(fs);
    var file = loader.ListRaceScenes().FirstOrDefault(f => f.Contains(parse.GetValue(fitCourse)!, StringComparison.OrdinalIgnoreCase));
    if (file is null) { Console.Error.WriteLine("no such course"); return 1; }
    var course = loader.Load(file);
    var resolver = new MKWSatellite.Formats.Models.ActorModelResolver(fs);
    var samples = new List<System.Numerics.Vector3>();
    foreach (var rail in course.LapPoints.GroupBy(l => l.Rail))
    {
        var pts = rail.ToList();
        for (var i = 0; i + 1 < pts.Count; i++)
            for (var k = 0; k < 8; k++) samples.Add(System.Numerics.Vector3.Lerp(pts[i].Position, pts[i + 1].Position, k / 8f));
    }
    var oldShape = new AreaShape(new System.Numerics.Vector3(-0.5f, 0, -0.5f), new System.Numerics.Vector3(0.5f, 1, 0.5f), false, false, "old");
    int Inside(CourseObject o, AreaShape sh)
    {
        var m = System.Numerics.Matrix4x4.CreateScale(o.Actor.Scale) * o.Rotation * System.Numerics.Matrix4x4.CreateTranslation(o.Position);
        if (!System.Numerics.Matrix4x4.Invert(m, out var inv)) return 0;
        var c = 0;
        foreach (var q in samples)
        {
            var l = System.Numerics.Vector3.Transform(q, inv);
            if (l.X >= sh.Min.X && l.X <= sh.Max.X && l.Y >= sh.Min.Y && l.Y <= sh.Max.Y && l.Z >= sh.Min.Z && l.Z <= sh.Max.Z) c++;
        }
        return c;
    }
    foreach (var g in course.Objects.Where(o => o.IsArea).GroupBy(o => o.Type).OrderBy(g => g.Key))
    {
        var sh = resolver.AreaShapeOf(g.Key);
        int any = 0, anyOld = 0, tot = 0, totOld = 0;
        foreach (var o in g) { var a = Inside(o, sh); var b = Inside(o, oldShape); tot += a; totOld += b; if (a > 0) any++; if (b > 0) anyOld++; }
        Console.WriteLine($"{g.Key,-36} n={g.Count(),3}  with path inside: new {any,3} old {anyOld,3}   samples new {tot,5} old {totOld,5}   {(sh.FromPhysics ? "phys" : "guess")}");
    }
    return 0;
});
root.Subcommands.Add(fitCmd);

// gatefit: which sign of RectOffset.X keeps lap gates off walls (surfaces well above the node) in the course model
var gfCourses = new Argument<string[]>("courses") { Description = "Race scene name filters.", Arity = ArgumentArity.OneOrMore };
var gfCmd = new Command("gatefit", "Compare lap gate placements (offset sign / centring) against walls in the course model.") { gfCourses };
gfCmd.SetAction(parse =>
{
    var fs = OpenFs(parse.GetValue(romfsOption));
    if (fs is null) return 1;
    var loader = new CourseLoader(fs);
    var resolver = new MKWSatellite.Formats.Models.ActorModelResolver(fs);
    var variants = new (string Name, float Centre)[] { ("+off/2", 0.5f), ("-off/2", -0.5f), ("none", 0f), ("+off", 1f), ("-off", -1f) };
    var totals = new int[variants.Length];
    var totalGates = 0;
    foreach (var filter in parse.GetValue(gfCourses)!)
    {
        var file = loader.ListRaceScenes().FirstOrDefault(f => f.Contains(filter, StringComparison.OrdinalIgnoreCase));
        if (file is null) { Console.Error.WriteLine($"{filter}: no such course"); continue; }
        var course = loader.Load(file);
        // XZ grid of triangles from the stage models.
        const float cell = 8f;
        var grid = new Dictionary<(int, int), List<(System.Numerics.Vector3 A, System.Numerics.Vector3 B, System.Numerics.Vector3 C)>>();
        foreach (var o in course.Objects.Where(o => o.Type.StartsWith("Stage_", StringComparison.Ordinal)))
        {
            if (resolver.ModelPath(o.Type) is not { } path) continue;
            var world = System.Numerics.Matrix4x4.CreateScale(o.Actor.Scale) * o.Rotation * System.Numerics.Matrix4x4.CreateTranslation(o.Position);
            foreach (var m in MKWSatellite.Formats.Models.Bfres.Parse(fs.Read(path)))
                foreach (var sh in m.Shapes)
                    for (var i = 0; i + 2 < sh.Indices.Length; i += 3)
                    {
                        var a = System.Numerics.Vector3.Transform(sh.Positions[sh.Indices[i]], world);
                        var b = System.Numerics.Vector3.Transform(sh.Positions[sh.Indices[i + 1]], world);
                        var c = System.Numerics.Vector3.Transform(sh.Positions[sh.Indices[i + 2]], world);
                        int x0 = (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X)) / cell), x1 = (int)MathF.Floor(MathF.Max(a.X, MathF.Max(b.X, c.X)) / cell);
                        int z0 = (int)MathF.Floor(MathF.Min(a.Z, MathF.Min(b.Z, c.Z)) / cell), z1 = (int)MathF.Floor(MathF.Max(a.Z, MathF.Max(b.Z, c.Z)) / cell);
                        if ((x1 - x0) * (z1 - z0) > 400) continue;
                        for (var gx = x0; gx <= x1; gx++)
                            for (var gz = z0; gz <= z1; gz++)
                            {
                                if (!grid.TryGetValue((gx, gz), out var l)) grid[(gx, gz)] = l = [];
                                l.Add((a, b, c));
                            }
                    }
        }
        // Is there a surface between y+lo and y+hi at (x, z)?
        bool Wall(System.Numerics.Vector3 p, float lo, float hi)
        {
            if (!grid.TryGetValue(((int)MathF.Floor(p.X / cell), (int)MathF.Floor(p.Z / cell)), out var l)) return false;
            foreach (var (a, b, c) in l)
            {
                var d = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
                if (MathF.Abs(d) < 1e-9f) continue;
                var l1 = ((b.Z - c.Z) * (p.X - c.X) + (c.X - b.X) * (p.Z - c.Z)) / d;
                var l2 = ((c.Z - a.Z) * (p.X - c.X) + (a.X - c.X) * (p.Z - c.Z)) / d;
                var l3 = 1 - l1 - l2;
                if (l1 < 0 || l2 < 0 || l3 < 0) continue;
                var h = l1 * a.Y + l2 * b.Y + l3 * c.Y - p.Y;
                if (h > lo && h < hi) return true;
            }
            return false;
        }
        var counts = new int[variants.Length];
        var aiIn = new int[variants.Length];
        var aiTotal = 0;
        var routePts = course.Rails.Where(r => r.Kind == "RoutePathGraph").SelectMany(r => r.Rail.Points).Select(q => q.Translation).ToList();
        var gates = 0;
        foreach (var lp in course.LapPoints)
        {
            if (MathF.Abs(lp.RectOffset.X) < 2f) continue; // only gates with a sideways offset tell the variants apart
            gates++;
            var rot = MKWSatellite.Formats.Course.CourseMath.EulerXyz(lp.Point.Rotate);
            var right = System.Numerics.Vector3.TransformNormal(System.Numerics.Vector3.UnitX, rot);
            right.Y = 0;
            if (right.LengthSquared() < 1e-6f) continue;
            right = System.Numerics.Vector3.Normalize(right);
            var fwd = System.Numerics.Vector3.Cross(right, System.Numerics.Vector3.UnitY);
            var near = routePts.Select(q => q - lp.Position).Where(q => MathF.Abs(System.Numerics.Vector3.Dot(q, fwd)) < 6 && MathF.Abs(q.Y) < 10 && MathF.Abs(System.Numerics.Vector3.Dot(q, right)) < lp.RectSize.X + MathF.Abs(lp.RectOffset.X)).Select(q => System.Numerics.Vector3.Dot(q, right)).ToList();
            aiTotal += near.Count;
            for (var v = 0; v < variants.Length; v++)
            {
                var c0 = variants[v].Centre * lp.RectOffset.X;
                aiIn[v] += near.Count(x => x >= c0 - lp.RectSize.X / 2 && x <= c0 + lp.RectSize.X / 2);
            }
            for (var v = 0; v < variants.Length; v++)
            {
                var c = variants[v].Centre * lp.RectOffset.X;
                var hits = 0;
                for (var k = 0; k <= 20; k++)
                {
                    var x = c - lp.RectSize.X / 2 + lp.RectSize.X * k / 20f;
                    if (Wall(lp.Position + right * x, 1.5f, 8f)) hits++;
                }
                counts[v] += hits;
                if (Environment.GetEnvironmentVariable("GATEFIT_VERBOSE") == "1" && v == 0)
                    Console.Write($"  node {lp.Index,3} cp {lp.CheckPoint?.ToString() ?? "-",3} size {lp.RectSize.X,5:0.#} off {lp.RectOffset.X,6:0.##} rot {lp.Point.Rotate * (180 / MathF.PI)} ");
                if (Environment.GetEnvironmentVariable("GATEFIT_VERBOSE") == "1")
                    Console.Write($"{variants[v].Name}:{hits} " + (v == variants.Length - 1 ? Environment.NewLine : ""));
            }
        }
        Console.WriteLine($"{file,-44} gates {gates,3}  walls: " + string.Join("  ", variants.Select((v, i) => $"{v.Name} {counts[i],4}")));
        Console.WriteLine($"{"",-44} AI pts {aiTotal,3} inside: " + string.Join("  ", variants.Select((v, i) => $"{v.Name} {aiIn[i],4}")));
        for (var v = 0; v < variants.Length; v++) totals[v] += counts[v];
        totalGates += gates;
    }
    Console.WriteLine($"{"TOTAL",-44} gates {totalGates,3}  " + string.Join("  ", variants.Select((v, i) => $"{v.Name} {totals[i],4}")));
    return 0;
});
root.Subcommands.Add(gfCmd);

// gateturn: is a gate's sideways offset towards the inside of the turn at its node?
var gtCmd = new Command("gateturn", "Correlate lap gate RectOffset.X sign with the turn direction of the lap path, over all race scenes.");
gtCmd.SetAction(parse =>
{
    var fs = OpenFs(parse.GetValue(romfsOption));
    if (fs is null) return 1;
    var loader = new CourseLoader(fs);
    int inside = 0, outside = 0, straight = 0;
    foreach (var file in loader.ListRaceScenes().Where(f => f.StartsWith("Race_", StringComparison.Ordinal)))
    {
        CourseScene course;
        try { course = loader.Load(file); } catch { continue; }
        foreach (var rail in course.LapPoints.GroupBy(l => l.Rail))
        {
            var pts = rail.ToList();
            for (var i = 1; i + 1 < pts.Count; i++)
            {
                var lp = pts[i];
                if (MathF.Abs(lp.RectOffset.X) < 3f) continue;
                var a = pts[i].Position - pts[i - 1].Position; a.Y = 0;
                var b = pts[i + 1].Position - pts[i].Position; b.Y = 0;
                if (a.LengthSquared() < 1 || b.LengthSquared() < 1) continue;
                a = System.Numerics.Vector3.Normalize(a); b = System.Numerics.Vector3.Normalize(b);
                var turn = a.X * b.Z - a.Z * b.X; // >0: turning one way
                if (MathF.Abs(turn) < 0.05f) { straight++; continue; }
                var right = System.Numerics.Vector3.TransformNormal(System.Numerics.Vector3.UnitX, MKWSatellite.Formats.Course.CourseMath.EulerXyz(lp.Point.Rotate));
                // Side of the turn centre relative to the node: direction b - a points to the inside of the bend.
                var inward = b - a;
                var side = System.Numerics.Vector3.Dot(inward, right) * lp.RectOffset.X;
                if (side > 0) inside++; else outside++;
            }
        }
    }
    Console.WriteLine($"offset towards inside of turn: {inside}, towards outside: {outside}, near-straight skipped: {straight}");
    return 0;
});
root.Subcommands.Add(gtCmd);

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
