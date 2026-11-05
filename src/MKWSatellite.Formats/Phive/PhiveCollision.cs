using System.Buffers.Binary;
using System.Numerics;
using MKWSatellite.Formats.Byml;
using MKWSatellite.Formats.IO;

namespace MKWSatellite.Formats.Phive;

/// <summary>
/// One collision material: <c>Material</c> (gameplay: Road, Dirt, Wall, AirWall, Dash...), <c>SubMaterial</c> (surface:
/// Asphalt, Snow...), user shape tag bits (Road, Fence, NoJA, WallRideOff...) and the collision layer for compound
/// shapes (high byte 4 = Ground, 22 = Valley; -1 for plain shape files). Indices name entries in PhiveConfig.
/// </summary>
public readonly record struct CollisionMaterial(int Material, int SubMaterial, ulong Flags, int Layer);

/// <summary>A triangle soup with a material per triangle, in world coordinates.</summary>
public sealed class CollisionMesh
{
    public List<Vector3> Positions { get; } = [];
    /// <summary>Index into <see cref="Materials"/> per triangle (-1 = none).</summary>
    public List<int> TriangleMaterial { get; } = [];
    public List<CollisionMaterial> Materials { get; } = [];
    private readonly Dictionary<CollisionMaterial, int> _materialIndex = [];
    /// <summary>Shape classes met but not decoded, with counts.</summary>
    public Dictionary<string, int> Skipped { get; } = [];

    public int TriangleCount => TriangleMaterial.Count;

    internal int MaterialId(CollisionMaterial? m)
    {
        if (m is not { } mm) return -1;
        if (!_materialIndex.TryGetValue(mm, out var i)) { i = Materials.Count; Materials.Add(mm); _materialIndex[mm] = i; }
        return i;
    }

    internal void Add(Vector3 a, Vector3 b, Vector3 c, int material)
    {
        Positions.Add(a); Positions.Add(b); Positions.Add(c);
        TriangleMaterial.Add(material);
    }
}

/// <summary>
/// Reads Phive collision files into a <see cref="CollisionMesh"/>:
/// <list type="bullet">
/// <item><c>.bphsh</c> (shape): header, one Havok tagfile (root <c>hknpMeshShape</c> etc.), a material table indexed
/// by the mesh's per-triangle shape tags.</item>
/// <item><c>.bphsc</c> (static compound): 17 section offsets, a 20-byte record per compound instance (layer, first
/// material, material count), a material table, then up to three tagfiles (sections 11-13) each holding an <c>hknpCompoundShape</c> whose
/// instances are already in world coordinates. An instance's <c>shapeTag</c> picks its record; a child mesh's triangle
/// tags count from that record's first material.</item>
/// </list>
/// Layouts were read from the files' own Havok reflection; see docs/formats/collision.md.
/// </summary>
public static class PhiveCollision
{
    private readonly record struct Record(int Layer, int FirstMaterial, int MaterialCount);

    /// <summary>Reads a <c>.bphsh</c> shape, transformed by <paramref name="world"/>.</summary>
    public static void ReadShape(byte[] d, Matrix4x4 world, CollisionMesh into)
    {
        CheckMagic(d);
        var tagOff = U32(d, 0x0C);
        var matOff = U32(d, 0x10);
        var matSize = U32(d, 0x20);
        var mats = new List<CollisionMaterial>();
        for (var i = 0; i + 16 <= matSize; i += 16)
            mats.Add(new(U32(d, matOff + i), U32(d, matOff + i + 4), BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(matOff + i + 8)), -1));
        var tf = new HavokTagfile(d, tagOff);
        var root = tf.Items.First(i => i.Type is not null && i.Count > 0);
        var dec = new Decoder(tf, into, null);
        dec.Shape(root.Offset, world, tag => tag < mats.Count ? mats[tag] : null);
    }

    /// <summary>Reads a <c>.bphsc</c> static compound (already in world coordinates).</summary>
    public static void ReadCompound(byte[] d, CollisionMesh into)
    {
        CheckMagic(d);
        var offs = new int[17];
        for (var i = 0; i < 17; i++) offs[i] = U32(d, 0x0C + 4 * i);
        var records = new List<Record>();
        for (var p = offs[4]; p + 20 <= offs[5]; p += 20)
            records.Add(new(U32(d, p + 4), U32(d, p + 12), U32(d, p + 16)));
        var mats = new List<CollisionMaterial>();
        for (var p = offs[5]; p + 16 <= offs[6]; p += 16)
            mats.Add(new(U32(d, p), U32(d, p + 4), BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(p + 8)), -1));
        CollisionMaterial? Mat(int instanceTag, int triTag)
        {
            if (instanceTag >= records.Count) return null;
            var r = records[instanceTag];
            var i = r.FirstMaterial + (triTag == 0xFFFF ? 0 : triTag);
            return i < mats.Count ? mats[i] with { Layer = r.Layer } : null;
        }
        // Sections 8-10 hold other data (e.g. a 0x20000-byte grid in some world tiles); the tagfiles are in 11-13.
        for (var k = 11; k < 14; k++)
        {
            if (offs[k + 1] <= offs[k] || offs[k] + 8 > d.Length || d[offs[k] + 4] != 'T' || d[offs[k] + 5] != 'A') continue;
            var tf = new HavokTagfile(d, offs[k]);
            var dec = new Decoder(tf, into, instanceTag => triTag => Mat(instanceTag, triTag));
            foreach (var root in tf.ObjectsOfType("hknpCompoundShape").Take(1))
                dec.Shape(root, Matrix4x4.Identity, _ => null);
        }
    }

    private static void CheckMagic(byte[] d)
    {
        if (d.Length < 0x30 || d[0] != 'P' || d[1] != 'h' || d[2] != 'i' || d[3] != 'v' || d[4] != 'e')
            throw new InvalidDataException("not a Phive file");
    }

    private static int U32(byte[] d, int o) => (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o));

    /// <summary>
    /// Walks shapes. Each shape gets a material context (triangle shape tag → material; 0xFFFF = the shape's own).
    /// In a static compound, a top-level instance's <c>shapeTag</c> picks a record (<paramref name="byInstanceTag"/>);
    /// children of a nested compound carry 0xFFFF and take the record's material at their child index (the record's
    /// count matches the child count; <b>unverified</b>).
    /// </summary>
    private sealed class Decoder(HavokTagfile tf, CollisionMesh mesh, Func<int, Func<int, CollisionMaterial?>>? byInstanceTag)
    {
        private readonly byte[] _d = tf.Data;

        private int I32(int o) => BinaryPrimitives.ReadInt32LittleEndian(_d.AsSpan(o));
        private float F32(int o) => BinaryPrimitives.ReadSingleLittleEndian(_d.AsSpan(o));
        private Vector3 V3(int o) => new(F32(o), F32(o + 4), F32(o + 8));

        /// <summary><c>hkRelArrayView</c>: int offset relative to the field, int count.</summary>
        private (int At, int Count) View(int field) => (field + I32(field), I32(field + 4));

        private int Off(HavokType? t, string field, int fallback) => t?.Offset(field) is >= 0 and var o ? o : fallback;

        public void Shape(int at, Matrix4x4 world, Func<int, CollisionMaterial?> ctx)
        {
            var t = tf.TypeAt(at);
            switch (t?.Name)
            {
                case "hknpMeshShape": Mesh(at, t, world, ctx); break;
                case "hknpCompoundShape": Compound(at, t, world, ctx); break;
                case "hknpBoxShape" or "hknpConvexShape" or "hknpCapsuleShape" or "hknpConvexPolytopeShape" or "hknpSphereShape":
                    Hull(at, t, world, ctx); break;
                default:
                    var n = t?.Name ?? "?";
                    mesh.Skipped[n] = mesh.Skipped.GetValueOrDefault(n) + 1;
                    break;
            }
        }

        private void Compound(int at, HavokType t, Matrix4x4 world, Func<int, CollisionMaterial?> parent)
        {
            var inst = at + Off(t, "instances", 0x48);
            // hkFreeListRelArray -> storage hkRelArray: i64 relative offset, i32 count.
            var elems = inst + (int)BinaryPrimitives.ReadInt64LittleEndian(_d.AsSpan(inst));
            var count = I32(inst + 8);
            for (var i = 0; i < count; i++)
            {
                var e = elems + 64 * i;
                var q = new Quaternion(F32(e), F32(e + 4), F32(e + 8), F32(e + 12));
                var tr = V3(e + 0x10);
                var sc = V3(e + 0x20);
                var tag = BinaryPrimitives.ReadUInt16LittleEndian(_d.AsSpan(e + 0x2C));
                var rel = BinaryPrimitives.ReadInt64LittleEndian(_d.AsSpan(e + 0x30));
                if (rel == 0 || q == default) continue; // free-list hole
                var m = Matrix4x4.CreateScale(sc) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(tr);
                var child = i;
                var ctx = tag != 0xFFFF && byInstanceTag is not null ? byInstanceTag(tag)
                    : tri => parent(tri == 0xFFFF ? child : tri);
                Shape(e + 0x30 + (int)rel, m * world, ctx);
            }
        }

        private void Mesh(int ms, HavokType t, Matrix4x4 world, Func<int, CollisionMaterial?> ctx)
        {
            var conv = ms + Off(t, "vertexConversionUtil", 0x50);
            var inv = V3(conv + 0x10); // bitScale16Inv
            var (tagsAt, tagCount) = View(ms + Off(t, "shapeTagTable", 0x70));
            var keys = new uint[tagCount];
            var tags = new ushort[tagCount];
            for (var i = 0; i < tagCount; i++)
            {
                keys[i] = BinaryPrimitives.ReadUInt32LittleEndian(_d.AsSpan(tagsAt + 8 * i));
                tags[i] = BinaryPrimitives.ReadUInt16LittleEndian(_d.AsSpan(tagsAt + 8 * i + 4));
            }
            var matCache = new Dictionary<int, int>();
            int MatFor(int section, int prim)
            {
                var tag = 0xFFFF;
                if (tagCount > 0)
                {
                    // Runs: an entry's tag applies from its key (section << 9 | primitive << 1) up to the next entry.
                    var key = (uint)((section << 9) | (prim << 1));
                    var j = Array.BinarySearch(keys, key);
                    if (j < 0) j = ~j - 1;
                    if (j >= 0) tag = tags[j];
                }
                if (!matCache.TryGetValue(tag, out var id))
                    matCache[tag] = id = mesh.MaterialId(ctx(tag));
                return id;
            }

            var (secAt, secCount) = View(ms + Off(t, "geometrySections", 0x90));
            var verts = new List<Vector3>();
            for (var s = 0; s < secCount; s++)
            {
                var sa = secAt + 64 * s;
                var (primAt, primCount) = View(sa + 0x08);
                var (vbAt, vbSize) = View(sa + 0x10);
                var so = (I32(sa + 0x20), I32(sa + 0x24), I32(sa + 0x28));
                verts.Clear();
                if (so.Item1 == int.MaxValue)
                    for (var k = 0; k + 12 <= vbSize; k += 12) verts.Add(Vector3.Transform(V3(vbAt + k), world)); // unquantized
                else
                    for (var k = 0; k + 6 <= vbSize; k += 6)
                    {
                        var x = BinaryPrimitives.ReadUInt16LittleEndian(_d.AsSpan(vbAt + k));
                        var y = BinaryPrimitives.ReadUInt16LittleEndian(_d.AsSpan(vbAt + k + 2));
                        var z = BinaryPrimitives.ReadUInt16LittleEndian(_d.AsSpan(vbAt + k + 4));
                        var v = new Vector3((float)((so.Item1 + (double)x) * inv.X), (float)((so.Item2 + (double)y) * inv.Y), (float)((so.Item3 + (double)z) * inv.Z));
                        verts.Add(Vector3.Transform(v, world));
                    }
                for (var p = 0; p < primCount; p++)
                {
                    int a = _d[primAt + 4 * p], b = _d[primAt + 4 * p + 1], c = _d[primAt + 4 * p + 2], e = _d[primAt + 4 * p + 3];
                    if (a >= verts.Count || b >= verts.Count || c >= verts.Count || e >= verts.Count) continue;
                    var m = MatFor(s, p);
                    mesh.Add(verts[a], verts[b], verts[c], m);
                    if (c != e) mesh.Add(verts[a], verts[c], verts[e], m);
                }
            }
        }

        private void Hull(int at, HavokType t, Matrix4x4 world, Func<int, CollisionMaterial?> ctx)
        {
            var mat = mesh.MaterialId(ctx(0xFFFF));
            var h = at + Off(t, "hull", 0x40);
            var (va, vn) = View(h);
            var (fa, fn) = View(h + 0x10);
            var (ia, _) = View(h + 0x18);
            var v = new Vector3[vn];
            for (var i = 0; i < vn; i++) v[i] = V3(va + 12 * i);
            var radius = F32(at + Off(t, "convexRadius", 0x20));
            if (t.Name == "hknpCapsuleShape" && vn == 2) { Capsule(v[0], v[1], radius, world, mat); return; }
            if (t.Name == "hknpSphereShape" || vn == 1) { Capsule(vn > 0 ? v[0] : Vector3.Zero, vn > 0 ? v[0] : Vector3.Zero, radius, world, mat); return; }
            for (var i = 0; i < vn; i++) v[i] = Vector3.Transform(v[i], world);
            for (var f = 0; f < fn; f++)
            {
                var first = BinaryPrimitives.ReadUInt16LittleEndian(_d.AsSpan(fa + 4 * f));
                var num = _d[fa + 4 * f + 2];
                for (var k = 1; k + 1 < num; k++)
                    mesh.Add(v[_d[ia + first]], v[_d[ia + first + k]], v[_d[ia + first + k + 1]], mat);
            }
        }

        private void Capsule(Vector3 a, Vector3 b, float r, Matrix4x4 world, int mat, int n = 8)
        {
            var axis = b - a;
            var u = axis.Length() > 1e-6f ? Vector3.Normalize(axis) : Vector3.UnitY;
            var p = Vector3.Normalize(Vector3.Cross(u, MathF.Abs(u.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY));
            var q = Vector3.Cross(u, p);
            Vector3 W(Vector3 x) => Vector3.Transform(x, world);
            for (var i = 0; i < n; i++)
            {
                var a0 = 2 * MathF.PI * i / n; var a1 = 2 * MathF.PI * (i + 1) / n;
                var r0 = r * (MathF.Cos(a0) * p + MathF.Sin(a0) * q);
                var r1 = r * (MathF.Cos(a1) * p + MathF.Sin(a1) * q);
                mesh.Add(W(a + r0), W(b + r0), W(b + r1), mat);
                mesh.Add(W(a + r0), W(b + r1), W(a + r1), mat);
                mesh.Add(W(a - u * r), W(a + r1), W(a + r0), mat);
                mesh.Add(W(b + u * r), W(b + r0), W(b + r1), mat);
            }
        }
    }
}

/// <summary>Names for collision materials, layers and shape tags, from <c>Phive/Config/PhiveConfig.byml</c>.</summary>
public sealed class PhiveNames
{
    public IReadOnlyList<string> Materials { get; }
    public IReadOnlyList<string> SubMaterials { get; }
    public IReadOnlyList<string> ShapeTags { get; }
    public IReadOnlyList<string> EntityLayers { get; }
    public IReadOnlyList<string> SensorLayers { get; }

    public const string Path = "Phive/Config/PhiveConfig.byml.zs";

    public PhiveNames(GameFileSystem fs)
    {
        var root = BymlReader.Parse(fs.Read(Path)).Root as BymlDict ?? throw new InvalidDataException("PhiveConfig");
        static List<string> Names(BymlArray? a) =>
            a?.Select(x => x as string ?? (x as BymlDict)?.Get<string>("ComponentName") ?? "?").ToList() ?? [];
        Materials = Names(root.Array("MaterialCollection"));
        SubMaterials = Names(root.Array("SubMaterialCollection"));
        ShapeTags = Names(root.Array("UserShapeTagCollection"));
        EntityLayers = Names(root.Array("LayerEntityCollection"));
        SensorLayers = Names(root.Array("LayerSensorCollection"));
    }

    public string Material(int i) => i >= 0 && i < Materials.Count ? Materials[i] : $"#{i}";
    public string SubMaterial(int i) => i >= 0 && i < SubMaterials.Count ? SubMaterials[i] : $"#{i}";

    public IEnumerable<string> Tags(ulong flags)
    {
        for (var b = 0; b < ShapeTags.Count; b++)
            if ((flags >> b & 1) != 0) yield return ShapeTags[b];
    }

    /// <summary>
    /// Layer of a compound record: bits 8-15 index the entity layers (4 = Ground, 22 = Valley); a low byte of 1 marks
    /// a sensor layer instead (<b>unverified</b>, from the values seen: 0x400, 0x1600, 0x701).
    /// </summary>
    public string Layer(int layer) => layer < 0 ? "" :
        (layer & 0xFF) == 1 ? $"sensor {Name(SensorLayers, (layer >> 8) & 0xFF)}" : Name(EntityLayers, (layer >> 8) & 0xFF);

    private static string Name(IReadOnlyList<string> l, int i) => i < l.Count ? l[i] : $"#{i}";

    public string Describe(CollisionMaterial m)
    {
        var tags = string.Join(", ", Tags(m.Flags));
        return $"{Material(m.Material)} / {SubMaterial(m.SubMaterial)}" + (tags.Length > 0 ? $" [{tags}]" : "") +
               (m.Layer >= 0 ? $"  ({Layer(m.Layer)})" : "");
    }
}
