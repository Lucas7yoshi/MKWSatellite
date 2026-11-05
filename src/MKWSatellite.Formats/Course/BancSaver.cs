using MKWSatellite.Formats.Byml;
using MKWSatellite.Formats.Compression;

namespace MKWSatellite.Formats.Course;

/// <summary>
/// Saves an edited Banc scene as the game stores it: BYML (same version), zero-padded to the next power of two like
/// the shipped files, zstd-compressed, at <c>&lt;output romfs&gt;/Banc/&lt;file&gt;</c>. The output is meant to be a
/// mod folder layered over the game; it refuses to write into a game dump.
/// </summary>
public static class BancSaver
{
    public sealed record Result(string Path, int DataSize, int PaddedSize, int OriginalPaddedSize)
    {
        /// <summary>
        /// The padded size grew past the original's. The game may size its load buffer from the original
        /// (resource size table), so a larger file could fail to load.
        /// </summary>
        public bool Grew => OriginalPaddedSize > 0 && PaddedSize > OriginalPaddedSize;
    }

    /// <summary>True if <paramref name="dir"/> is inside a game dump layer (one that has <c>System/Resource</c>).</summary>
    public static bool IsInsideGameDump(string dir, IEnumerable<string> gameLayers)
    {
        var d = WithSlash(Path.GetFullPath(dir));
        return gameLayers.Any(l => d.StartsWith(WithSlash(Path.GetFullPath(l)), StringComparison.OrdinalIgnoreCase)
                                   && Directory.Exists(Path.Combine(l, "System", "Resource")));
    }

    /// <param name="originalPaddedSize">Decompressed size of the file being replaced (0 if unknown).</param>
    public static Result Save(BancScene scene, string fileName, string outputRomfs, IEnumerable<string> gameLayers, int originalPaddedSize)
    {
        if (IsInsideGameDump(outputRomfs, gameLayers))
            throw new InvalidOperationException("Refusing to write into the game dump. Choose a separate mod folder.");
        var data = BymlWriter.Write(scene.Raw, scene.BymlVersion);
        var padded = BymlWriter.PadToPowerOfTwo(data);
        var path = Path.Combine(Path.GetFullPath(outputRomfs), BancScene.RomfsDirectory, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, Zstd.Compress(padded));
        File.Move(tmp, path, overwrite: true);
        return new Result(path, data.Length, padded.Length, originalPaddedSize);
    }

    private static string WithSlash(string p) => p.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
}
