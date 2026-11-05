using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace MKWSatellite.Formats.Terrain;

/// <summary>One terrain material: a layer of the albedo/normal texture arrays, its UV scale and a base colour.</summary>
/// <param name="TextureIndex">Layer in <c>CmnTex_Tera_Product_Proc_Alb</c> / <c>_Nrm</c>.</param>
/// <param name="UvScale">Texture repeats per world unit (0.05 = every 20 units).</param>
/// <param name="Color">Linear RGB multiplier (unverified meaning; looks like a base tint).</param>
public sealed record TerrainMaterial(int TextureIndex, Vector2 UvScale, Vector3 Color);

/// <summary>One quadtree node ("area"): a square of terrain with its own map files.</summary>
/// <param name="Name">File base name in <c>TerrainArc/&lt;scene&gt;/</c>, e.g. <c>6700003C4A</c> (level digit + index).</param>
/// <param name="Level">Quadtree depth, 0 = whole world; each level halves the size.</param>
/// <param name="Center">World X/Z of the centre.</param>
/// <param name="Size">World edge length.</param>
/// <param name="MinHeight">Lowest terrain height in the area (world units).</param>
/// <param name="MaxHeight">Highest terrain height in the area (world units).</param>
/// <param name="Resources">Resource type ids present for this area (see <see cref="TerrainScene.ResourceTypes"/>).</param>
public sealed record TerrainArea(string Name, int Level, Vector2 Center, float Size, float MinHeight, float MaxHeight, uint[] Resources)
{
    public Vector2 Min => Center - new Vector2(Size / 2);
    public Vector2 Max => Center + new Vector2(Size / 2);
    public bool Has(uint resource) => Array.IndexOf(Resources, resource) >= 0;
}

/// <summary>
/// Terrain scene (<c>TerrainArc/*.tscb</c>, magic <c>TSCB</c>, version 0x90C in Mario Kart World). Same family as the
/// Breath of the Wild TSCB (world scale, max height, material table, area quadtree), with all offsets relative to the
/// field that holds them. Layout read from the file; see docs/formats/terrain.md.
/// </summary>
public sealed class TerrainScene
{
    /// <summary>Resource type ids found in the area records (the scene's resource table names them).</summary>
    public const uint Height = 0x0000, Normal = 0x0100, Material = 0x2000, Water = 0x4100, ColorMul = 0x4700, AlbedoFar = 0x4A00;

    /// <summary>World units per TSCB area unit (250 in Game03).</summary>
    public float WorldScale { get; }
    /// <summary>Height map value 1.0 (65535) in world units (2000 in Game03).</summary>
    public float MaxHeight { get; }
    public IReadOnlyList<TerrainMaterial> Materials { get; }
    public IReadOnlyList<TerrainArea> Areas { get; }
    /// <summary>Resource type id → (name, file extension), e.g. 0x4700 → ("color_mul2", "color_mul2.extm").</summary>
    public IReadOnlyDictionary<uint, (string Name, string Extension)> ResourceTypes { get; }
    public int MaxLevel { get; }

    private TerrainScene(float worldScale, float maxHeight, List<TerrainMaterial> materials, List<TerrainArea> areas,
        Dictionary<uint, (string, string)> types)
    {
        WorldScale = worldScale;
        MaxHeight = maxHeight;
        Materials = materials;
        Areas = areas;
        ResourceTypes = types;
        MaxLevel = areas.Count == 0 ? 0 : areas.Max(a => a.Level);
    }

    public static TerrainScene Parse(byte[] file)
    {
        var d = file.AsSpan();
        if (file.Length < 0x80 || Encoding.ASCII.GetString(file, 0, 4) != "TSCB") throw new InvalidDataException("Not a TSCB file");
        uint U32(int o) => BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(o));
        float F32(int o) => BinaryPrimitives.ReadSingleLittleEndian(file.AsSpan(o));
        int Rel(int o) => o + (int)U32(o);
        string Str(int o)
        {
            var end = Array.IndexOf(file, (byte)0, o);
            return Encoding.ASCII.GetString(file, o, end - o);
        }

        var worldScale = F32(0x10);
        var maxHeight = F32(0x14);
        var materialCount = (int)U32(0x34);
        var areaCount = (int)U32(0x38);
        var typeCount = (int)U32(0x70);

        // 0x70: resource type count, 0x74/0x78: material and area tables, 0x7C: resource type table. Tables are arrays
        // of relative offsets. A resource type entry starts with its id and ends with offsets to its strings.
        var types = new Dictionary<uint, (string, string)>();
        for (var i = 0; i < typeCount; i++)
        {
            var t = Rel(0x7C + 4 * i);
            var name = Str(Rel(t + 0x60));
            var ext = Str(Rel(t + 0x64));
            types[U32(t)] = (name, ext);
        }

        var matTable = Rel(0x74);
        var materials = new List<TerrainMaterial>(materialCount);
        for (var i = 0; i < materialCount; i++)
        {
            var m = Rel(matTable + 4 * i);
            materials.Add(new TerrainMaterial((int)U32(m), new Vector2(F32(m + 0x10), F32(m + 0x14)),
                new Vector3(F32(m + 0x30), F32(m + 0x34), F32(m + 0x38))));
        }

        var areaTable = Rel(0x78);
        var rootSize = 0f;
        var areas = new List<TerrainArea>(areaCount);
        for (var i = 0; i < areaCount; i++)
        {
            var a = Rel(areaTable + 4 * i);
            var x = F32(a);
            var z = F32(a + 4);
            var size = F32(a + 8);
            if (i == 0) rootSize = size;
            var extCount = (int)U32(a + 0x20);
            var name = Str(Rel(a + 0x60));
            var resources = new uint[extCount];
            float minH = 0, maxH = 0;
            for (var k = 0; k < extCount; k++)
            {
                var e = Rel(a + 0x68 + 4 * k);
                resources[k] = U32(e);
                if (resources[k] == Height) { minH = F32(e + 0x20); maxH = F32(e + 0x24); }
            }
            var level = (int)MathF.Round(MathF.Log2(rootSize / size));
            areas.Add(new TerrainArea(name, level, new Vector2(x, z) * worldScale, size * worldScale,
                minH * maxHeight, maxH * maxHeight, resources));
        }
        return new TerrainScene(worldScale, maxHeight, materials, areas, types);
    }
}
