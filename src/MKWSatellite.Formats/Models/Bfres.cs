using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace MKWSatellite.Formats.Models;

/// <summary>A decoded model: vertex/index data ready for upload, plus material texture names.</summary>
public sealed class BfresModel
{
    public required string Name { get; init; }
    public required IReadOnlyList<BfresBone> Bones { get; init; }
    public required IReadOnlyList<BfresShape> Shapes { get; init; }
    public required IReadOnlyList<BfresMaterial> Materials { get; init; }
}

public sealed record BfresBone(string Name, int Parent, Vector3 Scale, Vector3 Rotation, Quaternion? Quaternion, Vector3 Translation)
{
    public Matrix4x4 LocalMatrix =>
        Matrix4x4.CreateScale(Scale)
        * (Quaternion is { } q ? Matrix4x4.CreateFromQuaternion(q)
            : Matrix4x4.CreateRotationX(Rotation.X) * Matrix4x4.CreateRotationY(Rotation.Y) * Matrix4x4.CreateRotationZ(Rotation.Z))
        * Matrix4x4.CreateTranslation(Translation);
}

public sealed record BfresMaterial(string Name, IReadOnlyList<string> Textures)
{
    /// <summary>Albedo texture by Nintendo's naming convention (<c>*_Alb</c>), else null.</summary>
    public string? Albedo => Textures.FirstOrDefault(t => t.EndsWith("_Alb", StringComparison.Ordinal));
}

/// <summary>
/// One shape (mesh) at its highest LOD. Positions/normals are in model space (bone transforms applied for rigid
/// meshes). Arrays are per vertex; <see cref="Uv0"/> is null when the shape has no <c>_u0</c>.
/// </summary>
public sealed class BfresShape
{
    public required string Name { get; init; }
    public required int MaterialIndex { get; init; }
    public required Vector3[] Positions { get; init; }
    public required Vector3[] Normals { get; init; }
    public required Vector2[]? Uv0 { get; init; }
    public required uint[] Indices { get; init; }
    public required int SkinCount { get; init; }
}

/// <summary>
/// BFRES reader for Mario Kart World (file version 10.x, Switch 2). Only what a viewer needs: models, skeleton,
/// shapes (LOD 0 triangles) and material texture names. Written from the file structure as observed in the game data,
/// which matches the long-documented Switch BFRES v9 layout for models/vertex buffers/shapes/skeletons; materials
/// changed (only the texture name list is used). Offsets are documented inline. Not a full parser: animations,
/// shader parameters and user data are skipped.
/// </summary>
public static class Bfres
{
    public static IReadOnlyList<BfresModel> Parse(byte[] file)
    {
        var d = file;
        if (d.Length < 0x100 || Encoding.ASCII.GetString(d, 0, 4) != "FRES") throw new InvalidDataException("Not a BFRES file");
        var r = new Reader(d);
        var modelArray = r.Ptr(0x28);
        var modelCount = r.U16(0xDC);
        // Buffer section: { u32, u32 size, u64 offset } — vertex/index data are relative to its offset.
        var bufferInfo = r.Ptr(0xB0);
        var dataBase = bufferInfo > 0 ? r.Ptr(bufferInfo + 8) : 0;

        var models = new List<BfresModel>(modelCount);
        for (var m = 0; m < modelCount; m++) models.Add(ReadModel(r, modelArray + m * 0x78, dataBase));
        return models;
    }

    private static BfresModel ReadModel(Reader r, int p, long dataBase)
    {
        if (r.Magic(p) != "FMDL") throw new InvalidDataException("Expected FMDL");
        var name = r.Str(r.Ptr(p + 0x08));
        var skeleton = r.Ptr(p + 0x18);
        var vertexArray = r.Ptr(p + 0x20);
        var shapeArray = r.Ptr(p + 0x28);
        var materialArray = r.Ptr(p + 0x38);
        var vertexCount = r.U16(p + 0x68);
        var shapeCount = r.U16(p + 0x6A);
        var materialCount = r.U16(p + 0x6C);

        // Skeleton: bones (0x58 bytes each), matrix-to-bone list for skinned vertices.
        var boneArray = r.Ptr(skeleton + 0x10);
        var matrixToBone = r.Ptr(skeleton + 0x18);
        var boneCount = r.U16(skeleton + 0x38);
        var smoothCount = r.U16(skeleton + 0x3A);
        var rigidCount = r.U16(skeleton + 0x3C);
        var bones = new List<BfresBone>(boneCount);
        for (var b = 0; b < boneCount; b++)
        {
            var o = boneArray + b * 0x58;
            var flags = r.U32(o + 0x2C);
            var scale = r.Vec3(o + 0x30);
            var rot = new Vector4(r.F32(o + 0x3C), r.F32(o + 0x40), r.F32(o + 0x44), r.F32(o + 0x48));
            var euler = (flags & 0x1000) != 0;
            bones.Add(new BfresBone(r.Str(r.Ptr(o)), r.S16(o + 0x22), scale,
                new Vector3(rot.X, rot.Y, rot.Z), euler ? null : new Quaternion(rot.X, rot.Y, rot.Z, rot.W), r.Vec3(o + 0x4C)));
        }
        var world = new Matrix4x4[boneCount];
        for (var b = 0; b < boneCount; b++)
            world[b] = bones[b].Parent >= 0 && bones[b].Parent < b ? bones[b].LocalMatrix * world[bones[b].Parent] : bones[b].LocalMatrix;
        var m2b = new int[smoothCount + rigidCount];
        for (var i = 0; i < m2b.Length; i++) m2b[i] = r.U16(matrixToBone + i * 2);

        var materials = new List<BfresMaterial>(materialCount);
        for (var i = 0; i < materialCount; i++)
        {
            var o = materialArray + i * 0xB0;
            var texNames = r.Ptr(o + 0x20);
            var texCount = r.U8(o + 0xA3);
            var textures = new List<string>(texCount);
            for (var t = 0; t < texCount; t++) textures.Add(r.Str(r.Ptr(texNames + t * 8)));
            materials.Add(new BfresMaterial(r.Str(r.Ptr(o + 0x08)), textures));
        }

        var shapes = new List<BfresShape>(shapeCount);
        for (var i = 0; i < shapeCount; i++)
        {
            var o = shapeArray + i * 0x60;
            var shapeName = r.Str(r.Ptr(o + 0x08));
            var meshArray = r.Ptr(o + 0x18);
            var materialIndex = r.U16(o + 0x52);
            var boneIndex = r.U16(o + 0x54);
            var vertexBufferIndex = r.U16(o + 0x56);
            var skinCount = r.U8(o + 0x5A);
            if (r.U8(o + 0x5B) == 0 || vertexBufferIndex >= vertexCount) continue;

            var vb = ReadVertexBuffer(r, vertexArray + vertexBufferIndex * 0x58, dataBase);
            var indices = ReadMesh(r, meshArray, dataBase);

            // Rigid meshes are stored in bone space: move them into model space.
            if (skinCount == 0 && boneIndex < world.Length && !world[boneIndex].IsIdentity)
                Transform(vb.Positions, vb.Normals, i => world[boneIndex]);
            else if (skinCount == 1 && vb.BoneIndex0 is { } bi)
                Transform(vb.Positions, vb.Normals, v => bi[v] < m2b.Length && m2b[bi[v]] < world.Length ? world[m2b[bi[v]]] : Matrix4x4.Identity);

            shapes.Add(new BfresShape
            {
                Name = shapeName, MaterialIndex = materialIndex, SkinCount = skinCount,
                Positions = vb.Positions, Normals = vb.Normals, Uv0 = vb.Uv0, Indices = indices,
            });
        }
        return new BfresModel { Name = name, Bones = bones, Shapes = shapes, Materials = materials };
    }

    private static void Transform(Vector3[] pos, Vector3[] nrm, Func<int, Matrix4x4> matrix)
    {
        for (var v = 0; v < pos.Length; v++)
        {
            var m = matrix(v);
            pos[v] = Vector3.Transform(pos[v], m);
            if (v < nrm.Length) nrm[v] = Vector3.Normalize(Vector3.TransformNormal(nrm[v], m));
        }
    }

    private sealed record VertexData(Vector3[] Positions, Vector3[] Normals, Vector2[]? Uv0, int[]? BoneIndex0);

    private static VertexData ReadVertexBuffer(Reader r, int o, long dataBase)
    {
        // FVTX: attribute array (+0x08, 0x10 each: name, format, offset, buffer), buffer size array (+0x30) and
        // stride array (+0x38) with 0x10-byte entries, data offset (+0x48), counts (+0x4C attr, +0x4D buffers),
        // vertex count (+0x50).
        var attrArray = r.Ptr(o + 0x08);
        var sizeArray = r.Ptr(o + 0x30);
        var strideArray = r.Ptr(o + 0x38);
        var bufferOffset = r.U32(o + 0x48);
        var attrCount = r.U8(o + 0x4C);
        var bufferCount = r.U8(o + 0x4D);
        var count = (int)r.U32(o + 0x50);

        // Buffers follow each other from bufferOffset, each aligned to 8 bytes.
        var bufStart = new long[bufferCount];
        var bufStride = new int[bufferCount];
        var cursor = dataBase + bufferOffset;
        for (var b = 0; b < bufferCount; b++)
        {
            bufStart[b] = cursor;
            bufStride[b] = (int)r.U32(strideArray + b * 0x10);
            cursor += (r.U32(sizeArray + b * 0x10) + 7) & ~7L;
        }

        var positions = new Vector3[count];
        var normals = Array.Empty<Vector3>();
        Vector2[]? uv0 = null;
        int[]? boneIndex = null;
        for (var a = 0; a < attrCount; a++)
        {
            var ao = attrArray + a * 0x10;
            var name = r.Str(r.Ptr(ao));
            var format = r.U32(ao + 0x08);
            var offset = r.U16(ao + 0x0C);
            var buffer = r.U16(ao + 0x0E);
            if (buffer >= bufferCount) continue;
            var start = bufStart[buffer] + offset;
            var stride = bufStride[buffer];
            switch (name)
            {
                case "_p0":
                    for (var v = 0; v < count; v++) { var x = r.Attrib(format, start + (long)v * stride); positions[v] = new Vector3(x.X, x.Y, x.Z); }
                    break;
                case "_n0":
                    normals = new Vector3[count];
                    for (var v = 0; v < count; v++) { var x = r.Attrib(format, start + (long)v * stride); normals[v] = SafeNormalize(new Vector3(x.X, x.Y, x.Z)); }
                    break;
                case "_u0":
                    uv0 = new Vector2[count];
                    for (var v = 0; v < count; v++) { var x = r.Attrib(format, start + (long)v * stride); uv0[v] = new Vector2(x.X, x.Y); }
                    break;
                case "_i0":
                    boneIndex = new int[count];
                    for (var v = 0; v < count; v++) boneIndex[v] = (int)r.Attrib(format, start + (long)v * stride).X;
                    break;
            }
        }
        return new VertexData(positions, normals, uv0, boneIndex);
    }

    private static Vector3 SafeNormalize(Vector3 v) => v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : Vector3.UnitY;

    private static uint[] ReadMesh(Reader r, int mesh, long dataBase)
    {
        // Mesh (LOD 0): face offset (+0x20, relative to the buffer section), primitive type (+0x24, 3 = triangles),
        // index format (+0x28: 0 u8, 1 u16, 2 u32), index count (+0x2C), first vertex (+0x30).
        var faceOffset = r.U32(mesh + 0x20);
        var prim = r.U32(mesh + 0x24);
        var format = r.U32(mesh + 0x28);
        var count = (int)r.U32(mesh + 0x2C);
        var first = r.U32(mesh + 0x30);
        if (prim != 3) return [];
        var idx = new uint[count];
        var p = dataBase + faceOffset;
        for (var i = 0; i < count; i++)
            idx[i] = first + (uint)(format switch { 0 => r.U8((int)(p + i)), 1 => r.U16((int)(p + i * 2)), _ => (long)r.U32((int)(p + i * 4)) });
        return idx;
    }

    private sealed class Reader(byte[] d)
    {
        public string Magic(int p) => Encoding.ASCII.GetString(d, p, 4);
        public int U8(int p) => d[p];
        public int U16(int p) => BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(p));
        public int S16(int p) => BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(p));
        public uint U32(int p) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p));
        public float F32(int p) => BinaryPrimitives.ReadSingleLittleEndian(d.AsSpan(p));
        public int Ptr(int p) => (int)BinaryPrimitives.ReadInt64LittleEndian(d.AsSpan(p));
        public Vector3 Vec3(int p) => new(F32(p), F32(p + 4), F32(p + 8));

        public string Str(int p)
        {
            if (p <= 0 || p + 2 > d.Length) return "";
            var n = U16(p);
            return Encoding.UTF8.GetString(d, p + 2, Math.Min(n, d.Length - p - 2));
        }

        /// <summary>Reads one vertex attribute as a Vector4. Format = (layout &lt;&lt; 8) | type, as in the file.</summary>
        public Vector4 Attrib(uint format, long pos)
        {
            var p = (int)pos;
            if (p < 0 || p + 4 > d.Length) return default;
            var layout = (format >> 8) & 0xFF;
            var type = format & 0xFF; // 1 unorm, 2 snorm, 3 uint, 4 sint, 5 float
            float N8(int o) => type switch { 1 => d[p + o] / 255f, 2 => Math.Max((sbyte)d[p + o] / 127f, -1f), _ => d[p + o] };
            float N16(int o) => type switch
            {
                1 => U16(p + o) / 65535f, 2 => Math.Max(S16(p + o) / 32767f, -1f),
                5 => (float)BitConverter.UInt16BitsToHalf((ushort)U16(p + o)), 4 => S16(p + o), _ => U16(p + o),
            };
            return layout switch
            {
                0x02 => new Vector4(N8(0), 0, 0, 1),
                0x09 => new Vector4(N8(0), N8(1), 0, 1),
                0x0B => new Vector4(N8(0), N8(1), N8(2), N8(3)),
                0x0A => new Vector4(N16(0), 0, 0, 1),
                0x12 => new Vector4(N16(0), N16(2), 0, 1),
                0x15 => new Vector4(N16(0), N16(2), N16(4), N16(6)),
                0x14 => new Vector4(F32(p), 0, 0, 1),
                0x17 => new Vector4(F32(p), F32(p + 4), 0, 1),
                0x18 => new Vector4(F32(p), F32(p + 4), F32(p + 8), 1),
                0x19 => new Vector4(F32(p), F32(p + 4), F32(p + 8), F32(p + 12)),
                0x0E => TenTenTenTwo(U32(p), type == 2),
                _ => default,
            };
        }

        private static Vector4 TenTenTenTwo(uint v, bool signed)
        {
            if (!signed) return new Vector4((v & 0x3FF) / 1023f, ((v >> 10) & 0x3FF) / 1023f, ((v >> 20) & 0x3FF) / 1023f, (v >> 30) / 3f);
            static float S10(uint x) => Math.Max(((int)(x << 22) >> 22) / 511f, -1f);
            return new Vector4(S10(v), S10(v >> 10), S10(v >> 20), (int)(v << 0) >> 30);
        }
    }
}
