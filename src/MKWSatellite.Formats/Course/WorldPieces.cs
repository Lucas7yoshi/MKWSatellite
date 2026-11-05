using System.Numerics;
using MKWSatellite.Formats.IO;

namespace MKWSatellite.Formats.Course;

/// <summary>One baked world mesh and where it goes.</summary>
public sealed record WorldPiece(string ModelPath, Vector3 Origin, int GridSize, int TileX, int TileZ);

/// <summary>
/// Open-world static geometry that is not placed as individual actors: actors flagged <c>IsGridify: True</c> in the
/// world scenes (city floors, track parts, street furniture...) are merged per world tile into
/// <c>Model/Piece_Gridify_Game03_X##_Z##.*.bfres.zs</c>. World tiles are 1000 units (tile X, Z centred on
/// <c>(X − 7.5, Z − 7.5) × 1000</c>, the same grid as <c>Banc/World_Game03/XXZZ</c>). Each tile has three grids, matching
/// the game's actor tags <c>Actor_Gridify250/500/1000</c>:
/// <list type="bullet">
/// <item><c>Gridify_Game03_X##_Z##_ij</c>: 250-unit cells (i = Z cell, j = X cell), origin at the cell centre.</item>
/// <item><c>Piece_Gridify_Game03_X##_Z##_ij</c>: 500-unit cells (same order), origin at the cell centre.</item>
/// <item><c>Piece_Gridify_Game03_X##_Z##</c>: the whole tile, origin at the tile centre; its materials are merged
/// atlases (<c>Common#_Aggregate</c>), so it is treated as the tile's far version.</item>
/// </list>
/// Heights are absolute. Origins were fitted from the placed positions of merged actors (street lights in X06_Z10's
/// 500-cell 11 fit only with the origin at the cell centre) and the index order from Crown City, whose lap path runs
/// on the merged streets only with i = Z; the 250/500 grids hold different instances, so they are drawn together.
/// </summary>
public static class WorldPieces
{
    public const float TileSize = 1000f;
    public const int TileCount = 16;

    public static (int X, int Z) TileOf(Vector3 p) =>
        ((int)MathF.Floor(p.X / TileSize + TileCount / 2f), (int)MathF.Floor(p.Z / TileSize + TileCount / 2f));

    public static Vector3 TileCenter(int x, int z) => new((x - TileCount / 2f + 0.5f) * TileSize, 0, (z - TileCount / 2f + 0.5f) * TileSize);

    /// <summary>
    /// Pieces for a course: the 250/500 grids for tiles within <paramref name="margin"/> of the path bounds, the
    /// whole-tile version for a ring of <paramref name="ring"/> tiles around them. Only files that exist are returned.
    /// </summary>
    public static List<WorldPiece> ForBounds(GameFileSystem fs, Vector3 min, Vector3 max, float margin = 300, int ring = 1)
    {
        var (x0, z0) = TileOf(min - new Vector3(margin));
        var (x1, z1) = TileOf(max + new Vector3(margin));
        var list = new List<WorldPiece>();
        for (var x = x0 - ring; x <= x1 + ring; x++)
            for (var z = z0 - ring; z <= z1 + ring; z++)
            {
                if (x < 0 || z < 0 || x >= TileCount || z >= TileCount) continue;
                var c = TileCenter(x, z);
                var near = x >= x0 && x <= x1 && z >= z0 && z <= z1;
                var tile = $"Game03_X{x:00}_Z{z:00}";
                void Add(string name, Vector3 origin, int grid)
                {
                    var path = $"Model/Piece_Gridify_{tile}.{name}.bfres.zs";
                    if (fs.Exists(path)) list.Add(new WorldPiece(path, origin, grid, x, z));
                }
                if (!near)
                {
                    Add($"Piece_Gridify_{tile}", c, 1000);
                    continue;
                }
                for (var i = 0; i < 2; i++)
                    for (var j = 0; j < 2; j++)
                        Add($"Piece_Gridify_{tile}_{i}{j}", c + new Vector3((j - 0.5f) * 500, 0, (i - 0.5f) * 500), 500);
                for (var i = 0; i < 4; i++)
                    for (var j = 0; j < 4; j++)
                        Add($"Gridify_{tile}_{i}{j}", c + new Vector3((j - 1.5f) * 250, 0, (i - 1.5f) * 250), 250);
            }
        return list;
    }
}
