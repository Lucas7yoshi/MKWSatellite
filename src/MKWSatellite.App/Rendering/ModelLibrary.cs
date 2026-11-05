using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using MKWSatellite.Formats.IO;
using MKWSatellite.Formats.Models;
using MKWSatellite.Formats.Textures;
using Silk.NET.OpenGL;

namespace MKWSatellite.App.Rendering;

/// <summary>
/// Loads actor models on worker threads (resolve → BFRES → albedo textures decoded on the CPU) and uploads them on the
/// GL thread a few at a time, so the UI never blocks. Models and textures are shared between all instances.
/// Shading is deliberately simple (albedo, one directional light, alpha cutout): the goal is readable geometry, not
/// the game's look.
/// </summary>
public sealed class ModelLibrary : IDisposable
{
    /// <summary>Largest texture side uploaded; bigger textures use a smaller mip (saves VRAM and decode time).</summary>
    public int MaxTextureSize { get; set; } = 1024;

    private const string VertexSource = """
        #version 410 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNrm;
        layout(location = 2) in vec2 aUv;
        uniform mat4 uViewProj;
        uniform mat4 uModel;
        out vec3 vNrm;
        out vec2 vUv;
        void main()
        {
            vec4 w = uModel * vec4(aPos, 1.0);
            vNrm = mat3(uModel) * aNrm;
            vUv = aUv;
            gl_Position = uViewProj * w;
        }
        """;

    private const string FragmentSource = """
        #version 410 core
        in vec3 vNrm;
        in vec2 vUv;
        uniform sampler2D uTex;
        uniform int uHasTex;
        uniform int uCutout;
        uniform vec3 uLightDir;
        uniform vec4 uTint;
        out vec4 FragColor;
        void main()
        {
            vec4 c = uHasTex == 1 ? texture(uTex, vUv) : vec4(0.55, 0.55, 0.55, 1.0);
            if (uCutout == 1 && c.a < 0.5) discard;
            vec3 n = normalize(vNrm);
            if (!gl_FrontFacing) n = -n;
            float sun = max(dot(n, -uLightDir), 0.0);
            float sky = 0.5 + 0.5 * n.y;
            vec3 lit = c.rgb * (0.22 + 0.33 * sky + 0.6 * sun);
            lit = mix(lit, uTint.rgb, uTint.a);
            FragColor = vec4(pow(lit, vec3(1.0 / 2.2)), 1.0); // textures are sampled as linear (sRGB formats)
        }
        """;

    private sealed record CpuShape(float[] Vertices, uint[] Indices, string? Texture, bool Cutout);
    private sealed record CpuModel(string Path, List<CpuShape> Shapes, Vector3 Min, Vector3 Max);
    private sealed record CpuTexture(int Width, int Height, byte[] Rgba, bool Srgb);

    internal sealed class GpuShape
    {
        public uint Vao, Vbo, Ebo, Texture;
        public int IndexCount;
        public bool Cutout;
    }

    public sealed class GpuModel
    {
        internal List<GpuShape> Shapes { get; } = [];
        public Vector3 Min { get; init; }
        public Vector3 Max { get; init; }
        public int Triangles { get; internal set; }
    }

    private readonly GL _gl;
    private readonly GameFileSystem _fs;
    private readonly ActorModelResolver _resolver;
    private readonly uint _program;
    private readonly int _uViewProj, _uModel, _uHasTex, _uCutout, _uLightDir, _uTex, _uTint;

    // actor type → model path (null = no model); model path → loaded model (null = failed)
    private readonly ConcurrentDictionary<string, string?> _typeToPath = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _requested = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GpuModel?> _models = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<(string Path, CpuModel? Model)> _ready = new();
    private readonly ConcurrentDictionary<string, Lazy<CpuTexture?>> _cpuTextures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _gpuTextures = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _workers = new(Math.Max(2, Environment.ProcessorCount / 2));
    private int _pending;

    public ModelLibrary(GL gl, GameFileSystem fs)
    {
        _gl = gl;
        _fs = fs;
        _resolver = new ActorModelResolver(fs);
        _program = PrimitiveBatch.CompileProgram(gl, VertexSource, FragmentSource);
        _uViewProj = gl.GetUniformLocation(_program, "uViewProj");
        _uModel = gl.GetUniformLocation(_program, "uModel");
        _uHasTex = gl.GetUniformLocation(_program, "uHasTex");
        _uCutout = gl.GetUniformLocation(_program, "uCutout");
        _uLightDir = gl.GetUniformLocation(_program, "uLightDir");
        _uTex = gl.GetUniformLocation(_program, "uTex");
        _uTint = gl.GetUniformLocation(_program, "uTint");
    }

    /// <summary>Models still loading (worker threads or waiting for upload).</summary>
    public int Pending => Volatile.Read(ref _pending) + _ready.Count;
    public int LoadedModels => _models.Count(kv => kv.Value is not null);
    public int TextureCount => _gpuTextures.Count;
    public List<string> Errors { get; } = [];

    /// <summary>The romfs model path an actor type resolved to, once known.</summary>
    public string? ModelPathOf(string actorType) => _typeToPath.GetValueOrDefault(actorType);

    /// <summary>The model for an actor type if it is ready, else null (and starts loading it on first request).</summary>
    public GpuModel? Get(string actorType)
    {
        if (_typeToPath.TryGetValue(actorType, out var path))
            return path is not null && _models.TryGetValue(path, out var m) ? m : null;
        if (_requested.TryAdd(actorType, 0))
        {
            Interlocked.Increment(ref _pending);
            Task.Run(() => LoadForType(actorType));
        }
        return null;
    }

    private void LoadForType(string actorType)
    {
        _workers.Wait();
        try
        {
            var path = _resolver.ModelPath(actorType);
            if (path is not null)
            {
                lock (_models)
                {
                    // Another actor type may already have loaded or be loading the same file.
                    if (_models.ContainsKey(path) || _loadingPaths.Contains(path)) { _typeToPath[actorType] = path; return; }
                    _loadingPaths.Add(path);
                }
                _ready.Enqueue((path, LoadModel(path)));
            }
            _typeToPath[actorType] = path;
        }
        catch (Exception e)
        {
            lock (Errors) Errors.Add($"{actorType}: {e.GetType().Name}: {e.Message}");
            _typeToPath[actorType] = null;
        }
        finally
        {
            _workers.Release();
            Interlocked.Decrement(ref _pending);
        }
    }

    private readonly HashSet<string> _loadingPaths = new(StringComparer.Ordinal);

    private CpuModel? LoadModel(string path)
    {
        try
        {
            var shapes = new List<CpuShape>();
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var model in Bfres.Parse(_fs.Read(path)))
                foreach (var s in model.Shapes)
                {
                    if (s.Indices.Length == 0) continue;
                    var mat = s.MaterialIndex < model.Materials.Count ? model.Materials[s.MaterialIndex] : null;
                    var v = new float[s.Positions.Length * 8];
                    for (var i = 0; i < s.Positions.Length; i++)
                    {
                        var p = s.Positions[i];
                        var n = i < s.Normals.Length ? s.Normals[i] : Vector3.UnitY;
                        var uv = s.Uv0 is { } u ? u[i] : Vector2.Zero;
                        v[i * 8] = p.X; v[i * 8 + 1] = p.Y; v[i * 8 + 2] = p.Z;
                        v[i * 8 + 3] = n.X; v[i * 8 + 4] = n.Y; v[i * 8 + 5] = n.Z;
                        v[i * 8 + 6] = uv.X; v[i * 8 + 7] = uv.Y;
                        min = Vector3.Min(min, p);
                        max = Vector3.Max(max, p);
                    }
                    var tex = mat?.Albedo;
                    if (tex is not null) _ = GetCpuTexture(tex); // decode now, on this worker
                    var cutout = (mat?.Name.Contains("Cutout", StringComparison.Ordinal) ?? false) || (tex?.Contains("Cutout", StringComparison.Ordinal) ?? false);
                    shapes.Add(new CpuShape(v, s.Indices, tex, cutout));
                }
            return new CpuModel(path, shapes, min, max);
        }
        catch (Exception e)
        {
            lock (Errors) Errors.Add($"{path}: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    private CpuTexture? GetCpuTexture(string name) =>
        _cpuTextures.GetOrAdd(name, n => new Lazy<CpuTexture?>(() => DecodeTexture(n))).Value;

    private CpuTexture? DecodeTexture(string name)
    {
        var path = $"Tex/{name}.bntx.zs";
        if (!_fs.Exists(path)) return null;
        try
        {
            var t = Bntx.Parse(_fs.Read(path)).FirstOrDefault();
            if (t is null) return null;
            var level = t.LevelForMaxSize(MaxTextureSize);
            var rgba = t.DecodeMip(level);
            return rgba is null ? null : new CpuTexture(t.MipWidth(level), t.MipHeight(level), rgba, t.Format.IsSrgb);
        }
        catch (Exception e)
        {
            lock (Errors) Errors.Add($"{name}: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    /// <summary>Uploads finished models to the GPU, spending at most about <paramref name="budgetMs"/> per call.</summary>
    public void PumpUploads(double budgetMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < budgetMs && _ready.TryDequeue(out var item))
        {
            lock (_models)
            {
                _models[item.Path] = item.Model is null ? null : Upload(item.Model);
                _loadingPaths.Remove(item.Path);
            }
        }
    }

    private unsafe GpuModel Upload(CpuModel m)
    {
        var gm = new GpuModel { Min = m.Min, Max = m.Max };
        foreach (var s in m.Shapes)
        {
            var g = new GpuShape { IndexCount = s.Indices.Length, Cutout = s.Cutout };
            g.Vao = _gl.GenVertexArray();
            _gl.BindVertexArray(g.Vao);
            g.Vbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, g.Vbo);
            _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, s.Vertices, BufferUsageARB.StaticDraw);
            g.Ebo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, g.Ebo);
            _gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer, s.Indices, BufferUsageARB.StaticDraw);
            const uint stride = 32;
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)12);
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)24);
            _gl.BindVertexArray(0);
            g.Texture = s.Texture is null ? 0 : GetGpuTexture(s.Texture);
            gm.Shapes.Add(g);
            gm.Triangles += s.Indices.Length / 3;
        }
        return gm;
    }

    private unsafe uint GetGpuTexture(string name)
    {
        if (_gpuTextures.TryGetValue(name, out var id)) return id;
        var t = GetCpuTexture(name);
        if (t is null) return _gpuTextures[name] = 0;
        id = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, id);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        fixed (byte* p = t.Rgba)
            _gl.TexImage2D(TextureTarget.Texture2D, 0, t.Srgb ? InternalFormat.Srgb8Alpha8 : InternalFormat.Rgba8,
                (uint)t.Width, (uint)t.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        _gl.GenerateMipmap(TextureTarget.Texture2D);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        _gl.TexParameter(TextureTarget.Texture2D, (TextureParameterName)0x84FE, 8f); // GL_TEXTURE_MAX_ANISOTROPY (core in 4.6, widely available)
        _gl.GetError(); // ignore if anisotropy is unsupported
        _gpuTextures[name] = id;
        // CPU pixels are no longer needed once uploaded.
        _cpuTextures[name] = new Lazy<CpuTexture?>(() => null);
        return id;
    }

    /// <summary>Starts a pass: binds the program and sets per-frame state.</summary>
    public unsafe void Begin(Matrix4x4 viewProj, Vector3 lightDir)
    {
        _gl.UseProgram(_program);
        _gl.UniformMatrix4(_uViewProj, 1, false, (float*)&viewProj);
        _gl.Uniform3(_uLightDir, Vector3.Normalize(lightDir));
        _gl.Uniform1(_uTex, 0);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.CullFace);
        _gl.ActiveTexture(TextureUnit.Texture0);
    }

    /// <summary>Draws one instance. <paramref name="tint"/> alpha blends towards a flat colour (selection highlight).</summary>
    public unsafe void Draw(GpuModel model, Matrix4x4 world, Vector4 tint = default)
    {
        _gl.UniformMatrix4(_uModel, 1, false, (float*)&world);
        _gl.Uniform4(_uTint, tint);
        foreach (var s in model.Shapes)
        {
            _gl.Uniform1(_uHasTex, s.Texture != 0 ? 1 : 0);
            _gl.Uniform1(_uCutout, s.Cutout ? 1 : 0);
            _gl.BindTexture(TextureTarget.Texture2D, s.Texture);
            _gl.BindVertexArray(s.Vao);
            _gl.DrawElements(PrimitiveType.Triangles, (uint)s.IndexCount, DrawElementsType.UnsignedInt, (void*)0);
        }
    }

    public void End()
    {
        _gl.BindVertexArray(0);
        _gl.Disable(EnableCap.DepthTest);
    }

    public void Dispose()
    {
        foreach (var m in _models.Values.OfType<GpuModel>())
            foreach (var s in m.Shapes)
            {
                _gl.DeleteBuffer(s.Vbo);
                _gl.DeleteBuffer(s.Ebo);
                _gl.DeleteVertexArray(s.Vao);
            }
        foreach (var t in _gpuTextures.Values.Where(t => t != 0)) _gl.DeleteTexture(t);
        _gl.DeleteProgram(_program);
    }
}
