using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using MKWSatellite.Formats.Terrain;

namespace MKWSatellite.Formats.Tests;

public class TerrainTests
{
    /// <summary>
    /// A minimal TSCB laid out like Game03_Proc.tscb: header (scale, max height, counts), relative-offset tables for
    /// materials, areas and resource types, area records with a resource entry list and a name string.
    /// </summary>
    [Fact]
    public void Tscb_ParsesHeaderMaterialsAreasAndTypes()
    {
        var f = new byte[0x400];
        void U32(int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(o), v);
        void F32(int o, float v) => BinaryPrimitives.WriteSingleLittleEndian(f.AsSpan(o), v);
        void Rel(int at, int target) => U32(at, (uint)(target - at));
        void Str(int o, string s) => Encoding.ASCII.GetBytes(s).CopyTo(f, o);

        Str(0, "TSCB");
        U32(4, 0x90C);
        F32(0x10, 250);
        F32(0x14, 2000);
        U32(0x34, 1);    // materials
        U32(0x38, 2);    // areas
        U32(0x70, 1);    // resource types
        Rel(0x74, 0x100); // material table
        Rel(0x78, 0x140); // area table
        Rel(0x7C, 0x80);  // resource type 0

        // Resource type: id + strings.
        U32(0x80, 0x4700);
        Rel(0x80 + 0x60, 0x380);
        Rel(0x80 + 0x64, 0x390);
        Str(0x380, "color_mul2");
        Str(0x390, "color_mul2.extm");

        // Material 0: layer 7, uv scale 0.05, colour (0.5, 0.25, 1).
        Rel(0x100, 0x104);
        U32(0x104, 7);
        F32(0x104 + 0x10, 0.05f);
        F32(0x104 + 0x14, 0.1f);
        F32(0x104 + 0x30, 0.5f);
        F32(0x104 + 0x34, 0.25f);
        F32(0x104 + 0x38, 1f);

        // Area 0: root (0, 0) size 4; area 1: child (-1, -1) size 2 with a height entry (min/max).
        Rel(0x140, 0x150);
        Rel(0x144, 0x200);
        F32(0x150 + 8, 4);
        Rel(0x150 + 0x60, 0x3B0);
        Str(0x3B0, "6000000000");
        F32(0x200, -1);
        F32(0x204, -1);
        F32(0x208, 2);
        U32(0x200 + 0x20, 2);
        Rel(0x200 + 0x60, 0x3C0);
        Str(0x3C0, "6100000003");
        Rel(0x200 + 0x68, 0x300);
        Rel(0x200 + 0x6C, 0x340);
        U32(0x300, TerrainScene.Height);
        F32(0x300 + 0x20, 0.1f);
        F32(0x300 + 0x24, 0.2f);
        U32(0x340, TerrainScene.ColorMul);

        var scene = TerrainScene.Parse(f);
        Assert.Equal(250, scene.WorldScale);
        Assert.Equal(2000, scene.MaxHeight);
        Assert.Equal(("color_mul2", "color_mul2.extm"), scene.ResourceTypes[0x4700]);
        var m = Assert.Single(scene.Materials);
        Assert.Equal(7, m.TextureIndex);
        Assert.Equal(new Vector2(0.05f, 0.1f), m.UvScale);
        Assert.Equal(new Vector3(0.5f, 0.25f, 1f), m.Color);
        Assert.Equal(2, scene.Areas.Count);
        var root = scene.Areas[0];
        Assert.Equal(("6000000000", 0, 1000f), (root.Name, root.Level, root.Size));
        var child = scene.Areas[1];
        Assert.Equal(("6100000003", 1, 500f), (child.Name, child.Level, child.Size));
        Assert.Equal(new Vector2(-250, -250), child.Center);
        Assert.Equal(200f, child.MinHeight, 3);
        Assert.Equal(400f, child.MaxHeight, 3);
        Assert.True(child.Has(TerrainScene.ColorMul));
        Assert.False(child.Has(TerrainScene.Water));
    }
}
