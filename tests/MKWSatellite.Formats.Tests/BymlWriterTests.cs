using MKWSatellite.Formats.Byml;
using MKWSatellite.Formats.Compression;
using MKWSatellite.Formats.Course;

namespace MKWSatellite.Formats.Tests;

public class BymlWriterTests
{
    private static BymlDict Sample()
    {
        var hash = new BymlHashMap(2) { [0x1234] = 5, [0xFFFFFFF0] = "h" };
        var inner = new BymlDict(2) { ["b"] = true, ["a"] = -0.0f };
        var arr = new BymlArray(4) { 1, 2u, 3.5f, inner };
        return new BymlDict(12)
        {
            ["Str"] = "héllo",
            ["Empty"] = "",
            ["Int"] = -7,
            ["UInt"] = 0xFFFFFFFFu,
            ["Float"] = 1.25f,
            ["Long"] = long.MinValue,
            ["ULong"] = 0xFEDCBA9876543210UL,
            ["Double"] = Math.PI,
            ["Null"] = null,
            ["Bin"] = new byte[] { 1, 2, 3 },
            ["Arr"] = arr,
            ["Hash"] = hash,
        };
    }

    [Fact]
    public void RoundTripKeepsTypesAndValues()
    {
        var tree = Sample();
        var back = BymlReader.Parse(BymlWriter.Write(tree, 7));
        Assert.Equal(7, back.Version);
        Assert.Null(BymlCompare.FirstDifference(tree, back.Root));
    }

    [Fact]
    public void CompareFindsTypeChanges()
    {
        var a = new BymlDict(1) { ["X"] = 1 };
        var b = new BymlDict(1) { ["X"] = 1u };
        Assert.NotNull(BymlCompare.FirstDifference(a, b));
    }

    [Fact]
    public void PadsToPowerOfTwo()
    {
        Assert.Equal(8, BymlWriter.PadToPowerOfTwo(new byte[5]).Length);
        Assert.Equal(8, BymlWriter.PadToPowerOfTwo(new byte[8]).Length);
    }

    [Fact]
    public void HistoryUndoRedoAndMerge()
    {
        var d = new BymlDict(1) { ["X"] = 1 };
        var h = new BymlEditHistory();
        Assert.False(h.IsDirty);
        h.Set(d, "X", 2);
        h.Set(d, "X", 3); // merged into one step (not sealed)
        Assert.Equal(1, h.UndoCount);
        h.Seal();
        h.Set(d, "Y", true);
        Assert.Equal(2, h.UndoCount);
        h.Undo();
        Assert.False(d.ContainsKey("Y"));
        h.Undo();
        Assert.Equal(1, d["X"]);
        Assert.False(h.IsDirty);
        h.Redo();
        Assert.Equal(3, d["X"]);
        Assert.True(h.IsDirty);
        h.MarkSaved();
        Assert.False(h.IsDirty);
        h.Undo();
        Assert.True(h.IsDirty);
        h.Set(d, "Z", 1); // drops the redo branch that held the saved state
        h.Undo();
        Assert.True(h.IsDirty);
    }

    [Fact]
    public void SaverWritesReadableFileAndRefusesDumps()
    {
        var root = Path.Combine(Path.GetTempPath(), "mkwsat-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dump = Path.Combine(root, "dump");
            Directory.CreateDirectory(Path.Combine(dump, "System", "Resource"));
            var mod = Path.Combine(root, "mod");
            var scene = BancScene.FromRoot(new BymlDict(2) { ["Actors"] = new BymlArray(0), ["Rails"] = new BymlArray(0) });

            Assert.Throws<InvalidOperationException>(() => BancSaver.Save(scene, "A.bcett.byml.zs", Path.Combine(dump, "x"), [dump], 0));

            var r = BancSaver.Save(scene, "A.bcett.byml.zs", mod, [dump], 0);
            var data = Zstd.Decompress(File.ReadAllBytes(r.Path));
            Assert.Equal(r.PaddedSize, data.Length);
            Assert.Null(BymlCompare.FirstDifference(scene.Raw, BymlReader.Parse(data).Root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
