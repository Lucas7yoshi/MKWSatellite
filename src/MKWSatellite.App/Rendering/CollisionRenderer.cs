using System.Numerics;
using MKWSatellite.Formats.Phive;
using Silk.NET.OpenGL;

namespace MKWSatellite.App.Rendering;

/// <summary>
/// Draws a course's collision mesh (Phive), coloured per triangle by gameplay material or by surface, with thin
/// triangle edges. Solid collision and volumes (sensor layers, Valley) are separate draw ranges. Keeps the triangles
/// on the CPU for picking the surface under the cursor.
/// </summary>
public sealed class CollisionRenderer : IDisposable
{
    private const string VertexSource = """
        #version 410 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec4 aColor;
        layout(location = 3) in vec3 aBary;
        uniform mat4 uViewProj;
        out vec3 vNormal;
        out vec4 vColor;
        out vec3 vBary;
        void main()
        {
            vNormal = aNormal;
            vColor = aColor;
            vBary = aBary;
            gl_Position = uViewProj * vec4(aPos, 1.0);
        }
        """;

    private const string FragmentSource = """
        #version 410 core
        in vec3 vNormal;
        in vec4 vColor;
        in vec3 vBary;
        uniform vec3 uLightDir;
        uniform float uOpacity;
        uniform float uEdges;
        uniform int uPass;          // 0 all, 1 skip highlighted, 2 highlighted only
        uniform float uEdgeBright;  // 1: light, wider edges (highlighted volumes)
        out vec4 FragColor;
        void main()
        {
            if (vColor.a < 0.01) discard;
            // Highlighted triangles carry alpha 254/255 as a marker.
            bool marked = vColor.a < 0.998;
            if ((uPass == 1 && marked) || (uPass == 2 && !marked)) discard;
            vec3 n = normalize(vNormal);
            float diffuse = abs(dot(n, normalize(-uLightDir)));
            vec3 c = vColor.rgb * (0.45 + 0.55 * diffuse);
            // Thin dark triangle edges (barycentric distance in screen space).
            vec3 d = fwidth(vBary);
            vec3 e = smoothstep(vec3(0.0), d * mix(1.2, 2.5, uEdgeBright), vBary);
            float edge = 1.0 - min(min(e.x, e.y), e.z);
            c = mix(c, mix(c * 0.45, vec3(1.0), uEdgeBright), edge * max(uEdges, uEdgeBright));
            float a = uOpacity * mix(1.0, 3.0, edge * uEdgeBright);
            FragColor = vec4(c, min(a, 1.0));
        }
        """;

    private readonly GL _gl;
    private readonly uint _program, _vao, _vbo, _colorVbo;
    private readonly int _uViewProj, _uLight, _uOpacity, _uEdges, _uPass, _uEdgeBright;
    private CollisionMesh? _mesh;
    private int[] _order = [];      // draw order: triangle indices, solid first
    private int _solidCount;        // triangles in the solid range
    private readonly HashSet<int> _hiddenMaterials = [];
    private readonly HashSet<int> _highlighted = [];

    public CollisionMesh? Mesh => _mesh;
    public bool HasData => _mesh is not null;
    /// <summary>True for materials drawn as volumes (sensor layers, Valley layer) rather than solid collision.</summary>
    public Func<CollisionMaterial, bool> IsVolume { get; set; } = _ => false;
    public Func<CollisionMaterial, bool, uint> ColorOf { get; set; } = (_, _) => 0xFFAAAAAA;
    public bool BySurface { get; private set; }

    public CollisionRenderer(GL gl)
    {
        _gl = gl;
        _program = PrimitiveBatch.CompileProgram(gl, VertexSource, FragmentSource);
        _uViewProj = gl.GetUniformLocation(_program, "uViewProj");
        _uLight = gl.GetUniformLocation(_program, "uLightDir");
        _uOpacity = gl.GetUniformLocation(_program, "uOpacity");
        _uEdges = gl.GetUniformLocation(_program, "uEdges");
        _uPass = gl.GetUniformLocation(_program, "uPass");
        _uEdgeBright = gl.GetUniformLocation(_program, "uEdgeBright");
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        _colorVbo = gl.GenBuffer();
    }

    public void Clear()
    {
        _mesh = null;
        _order = [];
        _solidCount = 0;
        _hiddenMaterials.Clear();
        _highlighted.Clear();
    }

    /// <summary>
    /// Materials whose volumes are highlighted: drawn stronger, with light edges, and faintly through anything in
    /// front of them.
    /// </summary>
    public void SetHighlighted(IEnumerable<int> materials)
    {
        var set = materials.ToHashSet();
        if (set.SetEquals(_highlighted)) return;
        _highlighted.Clear();
        _highlighted.UnionWith(set);
        UploadColors();
    }

    public bool IsHidden(int material) => _hiddenMaterials.Contains(material);

    public void SetHidden(int material, bool hidden)
    {
        if (hidden ? _hiddenMaterials.Add(material) : _hiddenMaterials.Remove(material)) UploadColors();
    }

    public void SetColorMode(bool bySurface)
    {
        if (BySurface == bySurface) return;
        BySurface = bySurface;
        UploadColors();
    }

    /// <summary>Uploads a mesh (main thread). Positions, normals and barycentrics go in one buffer, colours in another.</summary>
    public unsafe void Upload(CollisionMesh mesh)
    {
        _mesh = mesh;
        _hiddenMaterials.Clear();
        _highlighted.Clear();
        var n = mesh.TriangleCount;
        var solid = new List<int>(n);
        var volume = new List<int>();
        for (var t = 0; t < n; t++)
        {
            var m = mesh.TriangleMaterial[t];
            (m >= 0 && IsVolume(mesh.Materials[m]) ? volume : solid).Add(t);
        }
        _solidCount = solid.Count;
        solid.AddRange(volume);
        _order = solid.ToArray();

        var data = new float[n * 3 * 9];
        var k = 0;
        foreach (var t in _order)
        {
            var a = mesh.Positions[3 * t]; var b = mesh.Positions[3 * t + 1]; var c = mesh.Positions[3 * t + 2];
            var nrm = Vector3.Cross(b - a, c - a);
            nrm = nrm.LengthSquared() > 0 ? Vector3.Normalize(nrm) : Vector3.UnitY;
            for (var v = 0; v < 3; v++)
            {
                var p = mesh.Positions[3 * t + v];
                data[k++] = p.X; data[k++] = p.Y; data[k++] = p.Z;
                data[k++] = nrm.X; data[k++] = nrm.Y; data[k++] = nrm.Z;
                data[k++] = v == 0 ? 1 : 0; data[k++] = v == 1 ? 1 : 0; data[k++] = v == 2 ? 1 : 0;
            }
        }
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* p = data) _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * 4), p, BufferUsageARB.StaticDraw);
        const uint stride = 9 * 4;
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)12);
        _gl.EnableVertexAttribArray(3);
        _gl.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, stride, (void*)24);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _colorVbo);
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, 4, (void*)0);
        _gl.BindVertexArray(0);
        UploadColors();
    }

    private unsafe void UploadColors()
    {
        if (_mesh is not { } mesh) return;
        var colors = new uint[_order.Length * 3];
        var cache = new Dictionary<int, uint>();
        for (var i = 0; i < _order.Length; i++)
        {
            var m = mesh.TriangleMaterial[_order[i]];
            if (!cache.TryGetValue(m, out var c))
                cache[m] = c = _hiddenMaterials.Contains(m) ? 0u : m < 0 ? 0xFF808080
                    : _highlighted.Contains(m) ? ColorOf(mesh.Materials[m], BySurface) & 0x00FFFFFF | 0xFE000000
                    : ColorOf(mesh.Materials[m], BySurface);
            colors[3 * i] = colors[3 * i + 1] = colors[3 * i + 2] = c;
        }
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _colorVbo);
        fixed (uint* p = colors) _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(colors.Length * 4), p, BufferUsageARB.StaticDraw);
    }

    /// <summary>
    /// Draws the solid range and optionally the volumes. Opaque when <paramref name="opacity"/> is 1 (pulled slightly
    /// towards the camera so it wins over coplanar visual geometry), blended otherwise.
    /// </summary>
    public unsafe void Draw(Matrix4x4 viewProj, Vector3 lightDir, float opacity, bool volumes, bool edges, float volumeOpacity = 0.35f)
    {
        if (_mesh is null || _order.Length == 0) return;
        _gl.UseProgram(_program);
        _gl.UniformMatrix4(_uViewProj, 1, false, (float*)&viewProj);
        _gl.Uniform3(_uLight, lightDir.X, lightDir.Y, lightDir.Z);
        _gl.Uniform1(_uEdges, edges ? 1f : 0f);
        _gl.Uniform1(_uPass, 0);
        _gl.Uniform1(_uEdgeBright, 0f);
        _gl.BindVertexArray(_vao);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.PolygonOffsetFill);
        _gl.PolygonOffset(-1f, -2f);
        var translucent = opacity < 0.99f;
        if (translucent)
        {
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _gl.DepthMask(false);
        }
        _gl.Uniform1(_uOpacity, Math.Clamp(opacity, 0.05f, 1f));
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(_solidCount * 3));
        if (volumes && _order.Length > _solidCount)
        {
            // Volumes are always see-through. Highlighted ones: faint where hidden, strong where visible.
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _gl.DepthMask(false);
            var first = _solidCount * 3;
            var count = (uint)((_order.Length - _solidCount) * 3);
            var highlight = _highlighted.Count > 0;
            _gl.Uniform1(_uPass, highlight ? 1 : 0);
            _gl.Uniform1(_uOpacity, highlight ? volumeOpacity * 0.5f : volumeOpacity);
            _gl.DrawArrays(PrimitiveType.Triangles, first, count);
            if (highlight)
            {
                _gl.Uniform1(_uPass, 2);
                _gl.Uniform1(_uEdgeBright, 1f);
                _gl.DepthFunc(DepthFunction.Greater);
                _gl.Uniform1(_uOpacity, 0.12f);
                _gl.DrawArrays(PrimitiveType.Triangles, first, count);
                _gl.DepthFunc(DepthFunction.Lequal);
                _gl.Uniform1(_uOpacity, Math.Clamp(volumeOpacity * 1.6f, 0.3f, 0.9f));
                _gl.DrawArrays(PrimitiveType.Triangles, first, count);
                _gl.Uniform1(_uPass, 0);
                _gl.Uniform1(_uEdgeBright, 0f);
            }
        }
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.PolygonOffsetFill);
        _gl.Disable(EnableCap.DepthTest);
        _gl.BindVertexArray(0);
    }

    /// <summary>Nearest triangle hit by a ray (visible materials only): triangle index and distance. Scans in parallel.</summary>
    public (int Triangle, float Distance)? Raycast(Vector3 origin, Vector3 dir, bool volumes)
    {
        if (_mesh is not { } mesh) return null;
        var P = mesh.Positions;
        var order = _order;
        var count = volumes ? order.Length : _solidCount;
        var best = (Dist: float.MaxValue, Tri: -1);
        var gate = new object();
        System.Threading.Tasks.Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(0, count, 32768), range =>
        {
            var lb = float.MaxValue;
            var lt = -1;
            for (var i = range.Item1; i < range.Item2; i++)
            {
                var t = order[i];
                var m = mesh.TriangleMaterial[t];
                if (m >= 0 && _hiddenMaterials.Contains(m)) continue;
                // Moller-Trumbore
                var a = P[3 * t];
                var e1 = P[3 * t + 1] - a;
                var e2 = P[3 * t + 2] - a;
                var pv = Vector3.Cross(dir, e2);
                var det = Vector3.Dot(e1, pv);
                if (MathF.Abs(det) < 1e-9f) continue;
                var inv = 1 / det;
                var tv = origin - a;
                var u = Vector3.Dot(tv, pv) * inv;
                if (u < 0 || u > 1) continue;
                var qv = Vector3.Cross(tv, e1);
                var v = Vector3.Dot(dir, qv) * inv;
                if (v < 0 || u + v > 1) continue;
                var dist = Vector3.Dot(e2, qv) * inv;
                if (dist > 0 && dist < lb) { lb = dist; lt = t; }
            }
            if (lt < 0) return;
            lock (gate) if (lb < best.Dist) best = (lb, lt);
        });
        return best.Tri >= 0 ? (best.Tri, best.Dist) : null;
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_colorVbo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
    }
}
