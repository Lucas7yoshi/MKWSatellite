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
