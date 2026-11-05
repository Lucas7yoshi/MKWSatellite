using System.Text.RegularExpressions;
using MKWSatellite.Formats.Compression;

namespace MKWSatellite.Formats.IO;

/// <summary>
/// Read-only view over one or more extracted romfs directories. Later layers override earlier ones,
/// so an update's romfs (which only holds changed files) can be stacked on top of the base game.
/// Paths are romfs-relative with forward slashes, e.g. <c>Banc/Race_Cinema_A_00[Game03].bcett.byml.zs</c>.
/// Only lists specific directories on demand: a full romfs is ~200k files, often on slow disks.
/// </summary>
public sealed partial class GameFileSystem
{
    private readonly List<string> _layers;

    public GameFileSystem(IEnumerable<string> romfsLayers)
    {
        _layers = romfsLayers.Select(Path.GetFullPath).ToList();
        if (_layers.Count == 0) throw new ArgumentException("At least one romfs directory is required.", nameof(romfsLayers));
        foreach (var l in _layers)
            if (!Directory.Exists(l)) throw new DirectoryNotFoundException($"romfs directory not found: {l}");
    }

    public IReadOnlyList<string> Layers => _layers;

    /// <summary>Resolves a romfs-relative path to the highest-priority layer that has it, or null.</summary>
    public string? Resolve(string relativePath)
    {
        for (var i = _layers.Count - 1; i >= 0; i--)
        {
            var p = Path.Combine(_layers[i], relativePath);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public bool Exists(string relativePath) => Resolve(relativePath) is not null;

    public byte[] ReadRaw(string relativePath) =>
        File.ReadAllBytes(Resolve(relativePath) ?? throw new FileNotFoundException($"Not in any romfs layer: {relativePath}"));

    /// <summary>Reads a file, transparently decompressing <c>.zs</c> (zstd) files.</summary>
    public byte[] Read(string relativePath) => Zstd.DecompressIfNeeded(ReadRaw(relativePath));

    /// <summary>Lists file names (not recursive) in one romfs directory, merged across layers.</summary>
    public IReadOnlyList<string> ListFiles(string relativeDirectory, string searchPattern = "*")
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var l in _layers)
        {
            var dir = Path.Combine(l, relativeDirectory);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, searchPattern, SearchOption.TopDirectoryOnly))
                names.Add(Path.GetFileName(f));
        }
        return names.ToList();
    }

    /// <summary>
    /// Resource version from <c>System/Resource/ResourceSizeTable.Product.NNN.*</c> (e.g. 100 = 1.0.0),
    /// highest number across layers. Nintendo bumps this number with each content update.
    /// </summary>
    public int? GetResourceVersion() =>
        ListFiles("System/Resource", "ResourceSizeTable.Product.*")
            .Select(f => ProductVersion().Match(f))
            .Where(m => m.Success)
            .Select(m => (int?)int.Parse(m.Groups[1].Value))
            .Max();

    [GeneratedRegex(@"\.Product\.(\d+)\.")]
    private static partial Regex ProductVersion();
}
