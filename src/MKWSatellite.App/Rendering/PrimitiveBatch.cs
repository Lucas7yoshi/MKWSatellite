using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace MKWSatellite.App.Rendering;

/// <summary>
/// Immediate-style batch of coloured lines and triangles in world space, uploaded once per rebuild and drawn with a
/// single view-projection matrix. Good enough for tens of thousands of primitives; models will get their own renderer.
/// </summary>
public sealed class PrimitiveBatch : IDisposable
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly record struct Vertex(Vector3 Position, uint Color);

    private const string VertexSource = """
        #version 410 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec4 aColor;
        uniform mat4 uViewProj;
        uniform vec3 uEye;
        uniform float uPull;
        uniform float uAlpha;
        out vec4 vColor;
        void main()
        {
            vec3 p = aPos;
            if (uPull > 0.0)
            {
                // Pull towards the camera so overlay lying on the road wins the depth test against it.
                vec3 d = uEye - p;
                float len = length(d);
                if (len > 1e-4) p += d / len * min(len * 0.5, len * uPull + 0.25);
            }
            gl_Position = uViewProj * vec4(p, 1.0);
            vColor = vec4(aColor.rgb, aColor.a * uAlpha);
        }
        """;

    private const string FragmentSource = """
        #version 410 core
        in vec4 vColor;
        out vec4 FragColor;
        void main() { FragColor = vColor; }
        """;

    private readonly GL _gl;
    private readonly uint _program, _vao, _vbo;
    private readonly int _viewProjLocation, _eyeLocation, _pullLocation, _alphaLocation;
    private readonly List<Vertex> _lines = [], _triangles = [];
    private int _lineCount, _triangleCount;

    public PrimitiveBatch(GL gl)
    {
        _gl = gl;
        _program = CompileProgram(gl, VertexSource, FragmentSource);
        _viewProjLocation = gl.GetUniformLocation(_program, "uViewProj");
        _eyeLocation = gl.GetUniformLocation(_program, "uEye");
        _pullLocation = gl.GetUniformLocation(_program, "uPull");
        _alphaLocation = gl.GetUniformLocation(_program, "uAlpha");
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            var stride = (uint)sizeof(Vertex);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, stride, (void*)12);
        }
        gl.BindVertexArray(0);
    }

    public void Clear()
    {
        _lines.Clear();
        _triangles.Clear();
    }

    /// <summary>Colour as 0xAABBGGRR (byte order R, G, B, A in memory).</summary>
    public void Line(Vector3 a, Vector3 b, uint color)
    {
        _lines.Add(new Vertex(a, color));
        _lines.Add(new Vertex(b, color));
    }

    public void Triangle(Vector3 a, Vector3 b, Vector3 c, uint color)
    {
        _triangles.Add(new Vertex(a, color));
        _triangles.Add(new Vertex(b, color));
        _triangles.Add(new Vertex(c, color));
    }

    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, uint color)
    {
        Triangle(a, b, c, color);
        Triangle(a, c, d, color);
    }

    /// <summary>Uploads the batched geometry: triangles first, then lines, in one buffer.</summary>
    public void Upload()
    {
        var all = new Vertex[_triangles.Count + _lines.Count];
        _triangles.CopyTo(all, 0);
        _lines.CopyTo(all, _triangles.Count);
        _triangleCount = _triangles.Count;
        _lineCount = _lines.Count;
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BufferData<Vertex>(BufferTargetARB.ArrayBuffer, all, BufferUsageARB.DynamicDraw);
    }

    /// <summary>
    /// Draws the batch. With an <paramref name="eye"/> (3D view) and a depth mode other than
    /// <see cref="OverlayDepth.OnTop"/>, primitives are depth-tested against what is already in the depth buffer
    /// (the models): hidden parts are drawn faint (<see cref="OverlayDepth.Dim"/>) or not at all.
    /// </summary>
    public void Draw(Matrix4x4 viewProj, float lineWidth = 1f, Vector3? eye = null, OverlayDepth depth = OverlayDepth.OnTop)
    {
        _gl.UseProgram(_program);
        unsafe { _gl.UniformMatrix4(_viewProjLocation, 1, false, (float*)&viewProj); }
        _gl.BindVertexArray(_vao);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.CullFace);
        _gl.LineWidth(lineWidth); // core profile only guarantees width 1; thick lines are built from quads instead
        var tested = eye is not null && depth != OverlayDepth.OnTop;
        var e = eye ?? Vector3.Zero;
        _gl.Uniform3(_eyeLocation, e.X, e.Y, e.Z);
        _gl.Uniform1(_pullLocation, tested ? 0.004f : 0f);
        if (!tested)
        {
            _gl.Disable(EnableCap.DepthTest);
            Pass(1f);
        }
        else
        {
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthMask(false);
            if (depth == OverlayDepth.Dim)
            {
                _gl.DepthFunc(DepthFunction.Greater);
                Pass(0.22f);
            }
            _gl.DepthFunc(DepthFunction.Lequal);
            Pass(1f);
            _gl.DepthMask(true);
            _gl.Disable(EnableCap.DepthTest);
        }
        _gl.BindVertexArray(0);
    }

    private void Pass(float alpha)
    {
        _gl.Uniform1(_alphaLocation, alpha);
        if (_triangleCount > 0) _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_triangleCount);
        if (_lineCount > 0) _gl.DrawArrays(PrimitiveType.Lines, _triangleCount, (uint)_lineCount);
    }

    internal static uint CompileProgram(GL gl, string vs, string fs)
    {
        uint Compile(ShaderType type, string src)
        {
            var s = gl.CreateShader(type);
            gl.ShaderSource(s, src);
            gl.CompileShader(s);
            gl.GetShader(s, ShaderParameterName.CompileStatus, out var ok);
            if (ok == 0) throw new InvalidOperationException($"{type} compile failed: {gl.GetShaderInfoLog(s)}");
            return s;
        }
        var v = Compile(ShaderType.VertexShader, vs);
        var f = Compile(ShaderType.FragmentShader, fs);
        var p = gl.CreateProgram();
        gl.AttachShader(p, v);
        gl.AttachShader(p, f);
        gl.LinkProgram(p);
        gl.GetProgram(p, ProgramPropertyARB.LinkStatus, out var linked);
        if (linked == 0) throw new InvalidOperationException($"Program link failed: {gl.GetProgramInfoLog(p)}");
        gl.DeleteShader(v);
        gl.DeleteShader(f);
        return p;
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
    }
}

/// <summary>How overlay drawn behind course geometry is shown in 3D.</summary>
public enum OverlayDepth
{
    /// <summary>Always drawn over the models.</summary>
    OnTop,
    /// <summary>Hidden parts drawn faint.</summary>
    Dim,
    /// <summary>Hidden parts not drawn.</summary>
    Hide,
}
