using System.Collections.Concurrent;
using MKWSatellite.Formats.Archives;
using MKWSatellite.Formats.Byml;
using MKWSatellite.Formats.IO;

namespace MKWSatellite.Formats.Models;

/// <summary>
/// Finds the model file an actor type uses. Chain (verified in the 1.8 data):
/// <c>System/GymlArchive/actor_general_format</c> → <c>Gyml/ActorGParam/&lt;Type&gt;.gear__ActorGParam.bgyml</c>
/// → <c>Components.ModelInfoRef</c> → <c>modelinfo_general_format</c> → <c>ModelInfoGParam</c> → first
/// <c>ModelResource[].Fmdb</c> without a <c>ResourceType</c> (the near model; far/cubemap LODs have one), e.g.
/// <c>Work/Model/Stage/Stage_Cinema/output/Stage_Cinema_A.fmdb</c> → romfs <c>Model/Stage_Cinema.Stage_Cinema_A.bfres.zs</c>.
/// Any of these files can inherit missing keys from a <c>$parent</c>.
/// </summary>
public sealed class ActorModelResolver
{
    private const string ArchiveDir = "System/GymlArchive";
    private readonly GameFileSystem _fs;
    private readonly Lazy<Sarc?> _actors;
    private readonly Lazy<Sarc?> _modelInfo;
    private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.Ordinal);

    public ActorModelResolver(GameFileSystem fs)
    {
        _fs = fs;
        _actors = new(() => OpenArchive("actor_general_format"));
        _modelInfo = new(() => OpenArchive("modelinfo_general_format"));
    }

    private Sarc? OpenArchive(string prefix)
    {
        var name = _fs.ListFiles(ArchiveDir, $"{prefix}.*.sarc.zs").LastOrDefault();
        return name is null ? null : new Sarc(_fs.Read($"{ArchiveDir}/{name}"));
    }

    /// <summary>romfs-relative model path for an actor type, or null if it has no model (or the file is missing).</summary>
    public string? ModelPath(string actorType) => _cache.GetOrAdd(actorType, Resolve);

    private string? Resolve(string actorType)
    {
        if (_actors.Value is not { } actors || _modelInfo.Value is not { } infos) return null;
        var modelInfoRef = Lookup(actors, $"Work/Gyml/ActorGParam/{actorType}.gear__ActorGParam.gyml",
            d => d.Dict("Components")?.Get<string>("ModelInfoRef"));
        if (modelInfoRef is null) return null;
        var fmdb = Lookup(infos, modelInfoRef, d =>
            d.Array("ModelResource")?.OfType<BymlDict>().FirstOrDefault(r => r.Get<string>("ResourceType") is null)?.Get<string>("Fmdb"));
        if (fmdb is null) return null;

        // Work/Model/<Group>/<Folder>/output/<Model>.fmdb → Model/<Folder>.<Model>.bfres.zs
        var parts = fmdb.Split('/');
        if (parts.Length < 3) return null;
        var model = Path.GetFileNameWithoutExtension(parts[^1]);
        var folder = parts[^2] == "output" && parts.Length >= 4 ? parts[^3] : parts[^2];
        var path = $"Model/{folder}.{model}.bfres.zs";
        return _fs.Exists(path) ? path : null;
    }

    /// <summary>Reads a gyml by its <c>Work/...gyml</c> reference and evaluates <paramref name="get"/>, following <c>$parent</c>.</summary>
    private static T? Lookup<T>(Sarc archive, string workPath, Func<BymlDict, T?> get) where T : class
    {
        for (var depth = 0; depth < 16 && workPath is not null; depth++)
        {
            var name = workPath.StartsWith("Work/", StringComparison.Ordinal) ? workPath[5..] : workPath;
            if (name.EndsWith(".gyml", StringComparison.Ordinal)) name = name[..^5] + ".bgyml";
            if (!archive.TryGet(name, out var bytes)) return null;
            if (BymlReader.Parse(bytes.ToArray()).Root is not BymlDict dict) return null;
            if (get(dict) is { } v) return v;
            workPath = dict.Get<string>("$parent")!;
        }
        return null;
    }
}
