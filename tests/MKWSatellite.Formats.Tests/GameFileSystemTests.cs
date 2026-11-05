using MKWSatellite.Formats.Compression;
using MKWSatellite.Formats.IO;

namespace MKWSatellite.Formats.Tests;

public sealed class GameFileSystemTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mkwsat-tests-" + Guid.NewGuid().ToString("N"));
    private string Base => Path.Combine(_root, "base");
    private string Update => Path.Combine(_root, "update");

    public GameFileSystemTests()
    {
        Directory.CreateDirectory(Path.Combine(Base, "System/Resource"));
        Directory.CreateDirectory(Path.Combine(Update, "System/Resource"));
        File.WriteAllText(Path.Combine(Base, "System/Resource/ResourceSizeTable.Product.100.Nin_Ounce_NVN2.rsizetable.zs"), "");
        File.WriteAllText(Path.Combine(Update, "System/Resource/ResourceSizeTable.Product.120.Nin_Ounce_NVN2.rsizetable.zs"), "");
        File.WriteAllText(Path.Combine(Base, "a.txt"), "base");
        File.WriteAllText(Path.Combine(Base, "only-base.txt"), "base");
        File.WriteAllText(Path.Combine(Update, "a.txt"), "update");
    }

    [Fact]
    public void LaterLayersOverrideEarlierOnes()
    {
        var fs = new GameFileSystem([Base, Update]);
        Assert.Equal("update", File.ReadAllText(fs.Resolve("a.txt")!));
        Assert.Equal("base", File.ReadAllText(fs.Resolve("only-base.txt")!));
        Assert.Null(fs.Resolve("missing.txt"));
    }

    [Fact]
    public void DetectsResourceVersion()
    {
        Assert.Equal(100, new GameFileSystem([Base]).GetResourceVersion());
        Assert.Equal(120, new GameFileSystem([Base, Update]).GetResourceVersion());
    }

    [Fact]
    public void ReadDecompressesZstd()
    {
        var payload = "hello romfs"u8.ToArray();
        using var c = new ZstdSharp.Compressor();
        File.WriteAllBytes(Path.Combine(Base, "x.bin.zs"), c.Wrap(payload).ToArray());
        Assert.Equal(payload, new GameFileSystem([Base]).Read("x.bin.zs"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }
}
