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
        out vec4 vColor;
        void main() { gl_Position = uViewProj * vec4(aPos, 1.0); vColor = aColor; }
        """;

    private const string FragmentSource = """
        #version 410 core
        in vec4 vColor;
        out vec4 FragColor;
        void main() { FragColor = vColor; }
        """;

    private readonly GL _gl;
    private readonly uint _program, _vao, _vbo;
    private readonly int _viewProjLocation;
    private readonly List<Vertex> _lines = [], _triangles = [];
    private int _lineCount, _triangleCount;

    public PrimitiveBatch(GL gl)
    {
        _gl = gl;
        _program = CompileProgram(gl, VertexSource, FragmentSource);
        _viewProjLocation = gl.GetUniformLocation(_program, "uViewProj");
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

    public void Draw(Matrix4x4 viewProj, float lineWidth = 1f)
    {
        _gl.UseProgram(_program);
        unsafe { _gl.UniformMatrix4(_viewProjLocation, 1, false, (float*)&viewProj); }
        _gl.BindVertexArray(_vao);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        if (_triangleCount > 0) _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_triangleCount);
        _gl.LineWidth(lineWidth); // core profile only guarantees width 1; thick lines are built from quads instead
        if (_lineCount > 0) _gl.DrawArrays(PrimitiveType.Lines, _triangleCount, (uint)_lineCount);
        _gl.BindVertexArray(0);
    }

    private static uint CompileProgram(GL gl, string vs, string fs)
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
