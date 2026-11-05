using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using MKWSatellite.Formats.IO;
using MKWSatellite.Formats.Terrain;
using MKWSatellite.Formats.Textures;
using Silk.NET.OpenGL;

namespace MKWSatellite.App.Rendering;

/// <summary>
/// Open-world terrain (<c>TerrainArc/Game03_Proc</c>) streamed from its quadtree: each frame the deepest areas the
/// camera needs are chosen (screen-space cell size, frustum culled), loaded on worker threads and kept in a bounded
/// GPU cache, so only what is on screen is ever read, and far away only at coarse levels.
/// <para>
/// Shading follows what the data says (docs/formats/terrain.md): coarse areas (levels 0-4) use the game's precomputed
/// <c>albedo_far</c> map; finer areas blend two materials per texel from the texture array (material map) and multiply
/// by <c>color_mul2</c>. Fitted against <c>albedo_far</c>, that product matches it (scale 1.00, error under 1%).
/// Lighting is the same simple sun + sky as the models.
/// </para>
/// </summary>
public sealed class TerrainRenderer : IDisposable
{
    public const string SceneName = "Game03_Proc";

    /// <summary>Grid vertices per tile edge minus one (the 256-cell height map is sampled every 256/N texels).</summary>
    private const int GridN = 128;
    /// <summary>Refine an area while one of its height cells would cover more than this many pixels.</summary>
    public float PixelsPerCell { get; set; } = 3f;
    /// <summary>Most tiles kept on the GPU; the least recently drawn are dropped first.</summary>
    public int MaxTiles { get; set; } = 320;
    /// <summary>Largest albedo array layer side uploaded (the source is 1024; 512 keeps VRAM near 60 MB).</summary>
    public int MaxLayerSize { get; set; } = 512;

    private const string VertexSource = """
        #version 410 core
        layout(location = 0) in vec3 aGrid; // texel x, texel y (0..256), skirt flag
        uniform mat4 uViewProj;
        uniform vec4 uTile;   // min x, min z, size, max height
        uniform sampler2D uHeight;
        uniform int uStep;
        out vec3 vWorld;
        out vec3 vNrm;
        out vec2 vTexel;
        float H(ivec2 t) { return texelFetch(uHeight, clamp(t, ivec2(0), ivec2(259)), 0).r * uTile.w; }
        void main()
        {
            ivec2 t = ivec2(aGrid.xy);
            float h = H(t);
            float cell = uTile.z / 256.0;
            float dx = H(t + ivec2(uStep, 0)) - H(t - ivec2(uStep, 0));
            float dz = H(t + ivec2(0, uStep)) - H(t - ivec2(0, uStep));
            vNrm = normalize(vec3(-dx, 2.0 * cell * float(uStep), -dz));
            vWorld = vec3(uTile.x + float(t.x) * cell, h - aGrid.z * cell * float(uStep) * 3.0, uTile.y + float(t.y) * cell);
            vTexel = vec2(t);
            gl_Position = uViewProj * vec4(vWorld, 1.0);
        }
        """;

    private const string FragmentSource = """
        #version 410 core
        in vec3 vWorld;
        in vec3 vNrm;
        in vec2 vTexel;
        uniform int uMode;      // 0: albedo_far, 1: materials x color_mul2, 2: materials only
        uniform int uMask;      // quadrants drawn by child tiles (bit = x half + 2 * z half)
        uniform int uHasArray;
        uniform sampler2D uColor;
        uniform sampler2D uMate;
        uniform sampler2DArray uAlbedo;
        uniform vec4 uMats[64]; // layer, uv scale u, uv scale v, -
        uniform vec3 uLightDir;
        out vec4 FragColor;
        vec3 Layer(int m, vec2 xz)
        {
            vec4 p = uMats[clamp(m, 0, 63)];
            return uHasArray == 1 ? texture(uAlbedo, vec3(xz * p.yz, p.x)).rgb : vec3(0.45);
        }
        vec3 Texel(ivec2 t, vec2 xz)
        {
            vec4 m = floor(texelFetch(uMate, clamp(t, ivec2(0), ivec2(259)), 0) * 255.0 + 0.5);
            vec3 a = Layer(int(m.x), xz);
            if (m.z < 0.5) return a;
            return mix(a, Layer(int(m.y), xz), m.z / 255.0);
        }
        void main()
        {
            int q = (vTexel.x >= 128.0 ? 1 : 0) + (vTexel.y >= 128.0 ? 2 : 0);
            if (((uMask >> q) & 1) != 0) discard;
            vec2 uv = (vTexel + 0.5) / 260.0;
            vec3 albedo;
            if (uMode == 0) albedo = texture(uColor, uv).rgb;
            else
            {
                vec2 b = floor(vTexel);
                vec2 f = vTexel - b;
                ivec2 i = ivec2(b);
                vec2 xz = vWorld.xz;
                vec3 c00 = Texel(i, xz), c10 = Texel(i + ivec2(1, 0), xz), c01 = Texel(i + ivec2(0, 1), xz), c11 = Texel(i + ivec2(1, 1), xz);
                albedo = mix(mix(c00, c10, f.x), mix(c01, c11, f.x), f.y);
                if (uMode == 1) albedo *= texture(uColor, uv).rgb;
            }
            vec3 n = normalize(vNrm);
            float sun = max(dot(n, -uLightDir), 0.0);
            float sky = 0.5 + 0.5 * n.y;
            vec3 lit = albedo * (0.22 + 0.33 * sky + 0.6 * sun);
            FragColor = vec4(pow(lit, vec3(1.0 / 2.2)), 1.0);
        }
        """;

    private const string WaterVertexSource = """
        #version 410 core
        layout(location = 0) in vec3 aGrid;
        uniform mat4 uViewProj;
        uniform vec4 uTile;
        uniform sampler2D uWater;
        uniform vec3 uWaterXform; // this tile's texel -> the water source tile's texel: offset x, offset z, scale
        out vec2 vTexel;
        void main()
        {
            vec2 t = aGrid.xy;
            // Water texel i sits at min + i * size / 64 (4 height cells per texel).
            vec2 s = uWaterXform.xy + t * uWaterXform.z;
            float h = texture(uWater, (s / 4.0 + 0.5) / 65.0).r * uTile.w;
            vTexel = t;
            gl_Position = uViewProj * vec4(uTile.x + t.x * uTile.z / 256.0, h, uTile.y + t.y * uTile.z / 256.0, 1.0);
        }
        """;

    private const string WaterFragmentSource = """
        #version 410 core
        in vec2 vTexel;
        uniform int uMask;
        out vec4 FragColor;
        void main()
        {
            int q = (vTexel.x >= 128.0 ? 1 : 0) + (vTexel.y >= 128.0 ? 2 : 0);
            if (((uMask >> q) & 1) != 0) discard;
            FragColor = vec4(0.12, 0.34, 0.58, 0.62);
        }
        """;

    private sealed class Node(TerrainArea area)
    {
        public TerrainArea Area { get; } = area;
        public List<Node> Children { get; } = [];
        public int Quadrant;
        public Node? Parent;
        public Tile? Tile;
        public bool Loading;
        public bool Failed;
    }

    private sealed class Tile
    {
        public uint Height, Mate, Color, Water;
        public int Mode;
        public long LastUsed;
    }

    private sealed record CpuTile(Node Node, ushort[] Heights, byte[]? Mate, byte[]? Color, int Mode, ushort[]? Water);

    private readonly GL _gl;
    private readonly GameFileSystem _fs;
    private readonly uint _program, _waterProgram, _vao, _vbo, _ebo;
    private readonly int _wViewProj, _wTile, _wWater, _wMask, _wXform;
    private readonly int _indexCount;
    private readonly int _uViewProj, _uTile, _uHeight, _uStep, _uMode, _uMask, _uHasArray, _uColor, _uMate, _uAlbedo, _uMats, _uLightDir;
    private readonly ConcurrentQueue<CpuTile?> _ready = new();
    private readonly SemaphoreSlim _io = new(3);
    private readonly List<(Node Node, int Mask)> _drawList = [];
    private readonly Task<TerrainScene?> _sceneTask;
    private Task<(int Size, int Layers, byte[] Rgba)?>? _arrayTask;
    private Node? _root;
    private TerrainScene? _scene;
    private uint _albedoArray;
    private int _inFlight;
    private long _frame;
    private int _loaded;

    public List<string> Errors { get; } = [];
    public int LoadedTiles => _loaded;
    public int DrawnTiles => _drawList.Count;
    public int Pending => Volatile.Read(ref _inFlight) + _ready.Count;
    public bool IsAvailable => _sceneTask.IsCompletedSuccessfully && _sceneTask.Result is not null;

    public static bool Exists(GameFileSystem fs) => fs.Exists($"TerrainArc/{SceneName}.tscb");

    public TerrainRenderer(GL gl, GameFileSystem fs)
    {
        _gl = gl;
        _fs = fs;
        _program = PrimitiveBatch.CompileProgram(gl, VertexSource, FragmentSource);
        _uViewProj = gl.GetUniformLocation(_program, "uViewProj");
        _uTile = gl.GetUniformLocation(_program, "uTile");
        _uHeight = gl.GetUniformLocation(_program, "uHeight");
        _uStep = gl.GetUniformLocation(_program, "uStep");
        _uMode = gl.GetUniformLocation(_program, "uMode");
        _uMask = gl.GetUniformLocation(_program, "uMask");
        _uHasArray = gl.GetUniformLocation(_program, "uHasArray");
        _uColor = gl.GetUniformLocation(_program, "uColor");
        _uMate = gl.GetUniformLocation(_program, "uMate");
        _uAlbedo = gl.GetUniformLocation(_program, "uAlbedo");
        _uMats = gl.GetUniformLocation(_program, "uMats");
        _uLightDir = gl.GetUniformLocation(_program, "uLightDir");
        _waterProgram = PrimitiveBatch.CompileProgram(gl, WaterVertexSource, WaterFragmentSource);
        _wViewProj = gl.GetUniformLocation(_waterProgram, "uViewProj");
        _wTile = gl.GetUniformLocation(_waterProgram, "uTile");
        _wWater = gl.GetUniformLocation(_waterProgram, "uWater");
        _wMask = gl.GetUniformLocation(_waterProgram, "uMask");
        _wXform = gl.GetUniformLocation(_waterProgram, "uWaterXform");
        (_vao, _vbo, _ebo, _indexCount) = BuildGrid(gl);
        _sceneTask = Task.Run(() =>
        {
            try { return TerrainScene.Parse(fs.ReadRaw($"TerrainArc/{SceneName}.tscb")); }
            catch (Exception e) { lock (Errors) Errors.Add($"tscb: {e.Message}"); return null; }
        });
    }

    /// <summary>One shared grid: (N+1)² vertices addressed by height-map texel, plus a skirt ring that hangs down to
    /// hide cracks where neighbouring tiles are at different levels.</summary>
    private static unsafe (uint, uint, uint, int) BuildGrid(GL gl)
    {
        const int n = GridN;
        const int step = TerrainMaps.Cells / n;
        var verts = new List<float>();
        var idx = new List<uint>();
        uint V(int i, int j, float skirt)
        {
            verts.Add(i * step); verts.Add(j * step); verts.Add(skirt);
            return (uint)(verts.Count / 3 - 1);
        }
        var grid = new uint[n + 1, n + 1];
        for (var j = 0; j <= n; j++)
            for (var i = 0; i <= n; i++) grid[i, j] = V(i, j, 0);
        for (var j = 0; j < n; j++)
            for (var i = 0; i < n; i++)
            {
                idx.AddRange([grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j], grid[i + 1, j + 1], grid[i, j + 1]]);
            }
        // Skirts along the four edges.
        void Edge(Func<int, (int, int)> at)
        {
            for (var k = 0; k < n; k++)
            {
                var (i0, j0) = at(k);
                var (i1, j1) = at(k + 1);
                var a = grid[i0, j0];
                var b = grid[i1, j1];
                var a2 = V(i0, j0, 1);
                var b2 = V(i1, j1, 1);
                idx.AddRange([a, b, b2, a, b2, a2]);
            }
        }
        Edge(k => (k, 0));
        Edge(k => (n, k));
        Edge(k => (n - k, n));
        Edge(k => (0, n - k));

        var vao = gl.GenVertexArray();
        gl.BindVertexArray(vao);
        var vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, verts.ToArray(), BufferUsageARB.StaticDraw);
        var ebo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
        gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer, idx.ToArray(), BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, (void*)0);
        gl.BindVertexArray(0);
        return (vao, vbo, ebo, idx.Count);
    }

    private void BuildTree(TerrainScene scene)
    {
        var nodes = scene.Areas.Select(a => new Node(a)).ToList();
        var byLevel = nodes.GroupBy(n => n.Area.Level).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var n in nodes)
        {
            if (n.Area.Level == 0 || !byLevel.TryGetValue(n.Area.Level - 1, out var parents)) continue;
            var c = n.Area.Center;
            var p = parents.FirstOrDefault(p => c.X > p.Area.Min.X && c.X < p.Area.Max.X && c.Y > p.Area.Min.Y && c.Y < p.Area.Max.Y);
            if (p is null) continue;
            n.Quadrant = (c.X > p.Area.Center.X ? 1 : 0) + (c.Y > p.Area.Center.Y ? 2 : 0);
            p.Children.Add(n);
            n.Parent = p;
        }
        _root = byLevel.TryGetValue(0, out var roots) ? roots[0] : null;
    }

    /// <summary>Chooses the tiles for this camera, starts loads and uploads finished ones (GL thread).</summary>
    public void Update(IViewCamera camera, double uploadBudgetMs)
    {
        _frame++;
        if (_scene is null)
        {
            if (!_sceneTask.IsCompleted || _sceneTask.Result is null) return;
            _scene = _sceneTask.Result;
            BuildTree(_scene);
            _arrayTask = Task.Run(LoadAlbedoArray);
        }
        if (_albedoArray == 0 && _arrayTask is { IsCompleted: true } at)
        {
            if (at.Result is { } arr) _albedoArray = UploadArray(arr);
            _arrayTask = null;
        }
        PumpUploads(uploadBudgetMs);

        _drawList.Clear();
        if (_root is null) return;
        var want = new List<Node>();
        var planes = FrustumPlanes(camera.ViewProjection);
        Select(_root, camera, planes, want);
        // Keep the drawn tiles and their ancestors (water falls back to an ancestor's map; zooming out needs them).
        foreach (var (n, _) in _drawList)
            for (var a = n; a is not null; a = a.Parent)
                if (a.Tile is { } t) t.LastUsed = _frame;

        // Coarse first: a tile's parent must be on screen before its children can replace it.
        foreach (var n in want.OrderBy(n => n.Area.Level))
        {
            if (Volatile.Read(ref _inFlight) >= 6) break;
            StartLoad(n);
        }
        Evict();
    }

    private void Select(Node n, IViewCamera camera, Vector4[] planes, List<Node> want)
    {
        if (!Visible(n.Area, planes)) return;
        if (n.Tile is null)
        {
            if (!n.Failed) want.Add(n);
            return;
        }
        if (n.Children.Count > 0 && WantsRefine(n.Area, camera))
        {
            var visibleChildren = n.Children.Where(c => Visible(c.Area, planes)).ToList();
            var ready = true;
            foreach (var c in visibleChildren)
                if (c.Tile is null)
                {
                    ready = false;
                    if (!c.Failed) want.Add(c);
                }
            if (ready)
            {
                var mask = 0;
                foreach (var c in n.Children) mask |= 1 << c.Quadrant;
                if (mask != 0xF) _drawList.Add((n, mask)); // parts with no finer area keep the parent
                foreach (var c in visibleChildren) Select(c, camera, planes, want);
                return;
            }
        }
        _drawList.Add((n, 0));
    }

    private bool WantsRefine(TerrainArea a, IViewCamera camera)
    {
        var cell = a.Size / TerrainMaps.Cells;
        if (camera is OrbitCamera o)
        {
            // Distance from the eye to the area's box; projected cell size in pixels at that distance.
            var e = o.Eye;
            var d = new Vector3(
                MathF.Max(0, MathF.Max(a.Min.X - e.X, e.X - a.Max.X)),
                MathF.Max(0, MathF.Max(a.MinHeight - e.Y, e.Y - a.MaxHeight)),
                MathF.Max(0, MathF.Max(a.Min.Y - e.Z, e.Z - a.Max.Y))).Length();
            var pixels = cell / MathF.Max(1f, d) / (2 * MathF.Tan(o.FieldOfView / 2)) * o.ViewportSize.Y;
            return pixels > PixelsPerCell;
        }
        return cell / camera.UnitsPerPixel > PixelsPerCell;
    }

    /// <summary>Side planes of the view frustum (row-vector convention: clip = v · M), as (normal, d).</summary>
    private static Vector4[] FrustumPlanes(Matrix4x4 m)
    {
        var c1 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var c2 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var c4 = new Vector4(m.M14, m.M24, m.M34, m.M44);
        return [c4 + c1, c4 - c1, c4 + c2, c4 - c2];
    }

    private static bool Visible(TerrainArea a, Vector4[] planes)
    {
        // Pad the height range: skirts hang below, and min/max are only stored for levels that have them.
        var lo = new Vector3(a.Min.X, a.MinHeight - 50, a.Min.Y);
        var hi = new Vector3(a.Max.X, MathF.Max(a.MaxHeight, a.MinHeight) + 50, a.Max.Y);
        if (a.MaxHeight <= a.MinHeight) { lo.Y = -500; hi.Y = 2500; }
        foreach (var p in planes)
        {
            var v = new Vector3(p.X >= 0 ? hi.X : lo.X, p.Y >= 0 ? hi.Y : lo.Y, p.Z >= 0 ? hi.Z : lo.Z);
            if (p.X * v.X + p.Y * v.Y + p.Z * v.Z + p.W < 0) return false;
        }
        return true;
    }

    private void StartLoad(Node n)
    {
        if (n.Loading || n.Tile is not null || n.Failed) return;
        n.Loading = true;
        Interlocked.Increment(ref _inFlight);
        Task.Run(() =>
        {
            _io.Wait();
            try { _ready.Enqueue(LoadTile(n)); }
            catch (Exception e)
            {
                lock (Errors) Errors.Add($"{n.Area.Name}: {e.Message}");
                n.Failed = true;
                _ready.Enqueue(null);
            }
            finally
            {
                _io.Release();
                Interlocked.Decrement(ref _inFlight);
            }
        });
    }

    private CpuTile LoadTile(Node n)
    {
        var a = n.Area;
        byte[] Read(string ext) => TerrainMaps.TryRead(_fs, TerrainMaps.PathOf(SceneName, a.Name, ext))
            ?? throw new FileNotFoundException($"{a.Name}.{ext}");
        var heights = TerrainMaps.Heights(Read("hght"));
        var water = a.Has(TerrainScene.Water) ? TerrainMaps.Water(Read("water.extm")) : null;
        if (a.Level <= 4 && a.Has(TerrainScene.AlbedoFar))
            return new CpuTile(n, heights, null, TerrainMaps.Bc3(Read("albedo_far.extm")), 0, water);
        var mate = TerrainMaps.Materials(Read("mate"));
        var color = a.Has(TerrainScene.ColorMul) ? TerrainMaps.Bc3(Read("color_mul2.extm")) : null;
        return new CpuTile(n, heights, mate, color, color is null ? 2 : 1, water);
    }

    private void PumpUploads(double budgetMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < budgetMs && _ready.TryDequeue(out var t))
        {
            if (t is null) continue;
            t.Node.Loading = false;
            t.Node.Tile = Upload(t);
            _loaded++;
        }
    }

    private unsafe Tile Upload(CpuTile t)
    {
        const int r = TerrainMaps.Resolution;
        var tile = new Tile { Mode = t.Mode, LastUsed = _frame };
        tile.Height = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, tile.Height);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        fixed (ushort* p = t.Heights)
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R16, r, r, 0, PixelFormat.Red, PixelType.UnsignedShort, p);
        Nearest();
        if (t.Mate is not null)
        {
            tile.Mate = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, tile.Mate);
            fixed (byte* p = t.Mate)
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, r, r, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);
            Nearest();
        }
        if (t.Color is not null)
        {
            tile.Color = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, tile.Color);
            fixed (byte* p = t.Color)
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Srgb8Alpha8, r, r, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);
            _gl.GenerateMipmap(TextureTarget.Texture2D);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            Clamp();
        }
        if (t.Water is not null)
        {
            const int wr = TerrainMaps.WaterResolution;
            tile.Water = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, tile.Water);
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 2);
            fixed (ushort* p = t.Water)
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16, wr, wr, 0, PixelFormat.Rgba, PixelType.UnsignedShort, p);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            Clamp();
        }
        return tile;
    }

    private void Nearest()
    {
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        Clamp();
    }

    private void Clamp()
    {
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
    }

    private (int, int, byte[])? LoadAlbedoArray()
    {
        try
        {
            var t = Bntx.Parse(_fs.Read("Tex/CmnTex_Tera_Product_Proc_Alb.bntx.zs")).First();
            var level = t.LevelForMaxSize(MaxLayerSize);
            var size = t.MipWidth(level);
            var layers = t.ArrayLength;
            var all = new byte[size * size * 4 * layers];
            Parallel.For(0, layers, l =>
            {
                var rgba = t.DecodeMip(level, l);
                if (rgba is not null) System.Buffer.BlockCopy(rgba, 0, all, l * size * size * 4, Math.Min(rgba.Length, size * size * 4));
            });
            return (size, layers, all);
        }
        catch (Exception e)
        {
            lock (Errors) Errors.Add($"albedo array: {e.Message}");
            return null;
        }
    }

    private unsafe uint UploadArray((int Size, int Layers, byte[] Rgba) a)
    {
        var id = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2DArray, id);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        fixed (byte* p = a.Rgba)
            _gl.TexImage3D(TextureTarget.Texture2DArray, 0, InternalFormat.Srgb8Alpha8, (uint)a.Size, (uint)a.Size, (uint)a.Layers, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, p);
        _gl.GenerateMipmap(TextureTarget.Texture2DArray);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        _gl.TexParameter(TextureTarget.Texture2DArray, (TextureParameterName)0x84FE, 8f); // anisotropy where supported
        _gl.GetError();
        return id;
    }

    private void Evict()
    {
        if (_loaded <= MaxTiles || _root is null) return;
        var all = new List<Node>();
        void Walk(Node n) { if (n.Tile is not null) all.Add(n); foreach (var c in n.Children) Walk(c); }
        Walk(_root);
        foreach (var n in all.Where(n => n.Tile!.LastUsed < _frame && n.Area.Level > 1).OrderBy(n => n.Tile!.LastUsed).Take(_loaded - MaxTiles))
        {
            Delete(n.Tile!);
            n.Tile = null;
            _loaded--;
        }
    }

    private void Delete(Tile t)
    {
        _gl.DeleteTexture(t.Height);
        if (t.Mate != 0) _gl.DeleteTexture(t.Mate);
        if (t.Color != 0) _gl.DeleteTexture(t.Color);
        if (t.Water != 0) _gl.DeleteTexture(t.Water);
    }

    /// <summary>Translucent water surfaces of the tiles drawn this frame; call after the opaque geometry.</summary>
    public unsafe void DrawWater(Matrix4x4 viewProj)
    {
        if (_scene is null) return;
        _gl.UseProgram(_waterProgram);
        _gl.UniformMatrix4(_wViewProj, 1, false, (float*)&viewProj);
        _gl.Uniform1(_wWater, 0);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(false);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.CullFace);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindVertexArray(_vao);
        foreach (var (n, mask) in _drawList)
        {
            // Some areas over water have no water map of their own; their nearest ancestor's covers them.
            var src = n;
            while (src is not null && (src.Tile is null || src.Tile.Water == 0)) src = src.Parent;
            if (src is null) continue;
            var a = n.Area;
            var sa = src.Area;
            _gl.Uniform3(_wXform, (a.Min.X - sa.Min.X) / sa.Size * TerrainMaps.Cells, (a.Min.Y - sa.Min.Y) / sa.Size * TerrainMaps.Cells, a.Size / sa.Size);
            _gl.Uniform4(_wTile, a.Min.X, a.Min.Y, a.Size, _scene.MaxHeight);
            _gl.Uniform1(_wMask, mask);
            _gl.BindTexture(TextureTarget.Texture2D, src.Tile!.Water);
            // The grid without the skirt ring (its indices come after the grid's).
            _gl.DrawElements(PrimitiveType.Triangles, (uint)(GridN * GridN * 6), DrawElementsType.UnsignedInt, (void*)0);
        }
        _gl.BindVertexArray(0);
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.DepthTest);
    }

    public unsafe void Draw(Matrix4x4 viewProj, Vector3 lightDir)
    {
        if (_scene is null || _drawList.Count == 0) return;
        _gl.UseProgram(_program);
        _gl.UniformMatrix4(_uViewProj, 1, false, (float*)&viewProj);
        _gl.Uniform3(_uLightDir, Vector3.Normalize(lightDir));
        var mats = new float[64 * 4];
        for (var i = 0; i < Math.Min(64, _scene.Materials.Count); i++)
        {
            var m = _scene.Materials[i];
            mats[i * 4] = m.TextureIndex;
            mats[i * 4 + 1] = m.UvScale.X;
            mats[i * 4 + 2] = m.UvScale.Y;
        }
        _gl.Uniform4(_uMats, 64, mats);
        _gl.Uniform1(_uHeight, 0);
        _gl.Uniform1(_uMate, 1);
        _gl.Uniform1(_uColor, 2);
        _gl.Uniform1(_uAlbedo, 3);
        _gl.Uniform1(_uHasArray, _albedoArray != 0 ? 1 : 0);
        _gl.Uniform1(_uStep, TerrainMaps.Cells / GridN);
        _gl.ActiveTexture(TextureUnit.Texture3);
        _gl.BindTexture(TextureTarget.Texture2DArray, _albedoArray);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.CullFace);
        _gl.BindVertexArray(_vao);
        foreach (var (n, mask) in _drawList)
        {
            var t = n.Tile!;
            var a = n.Area;
            _gl.Uniform4(_uTile, a.Min.X, a.Min.Y, a.Size, _scene.MaxHeight);
            _gl.Uniform1(_uMode, t.Mode);
            _gl.Uniform1(_uMask, mask);
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, t.Height);
            _gl.ActiveTexture(TextureUnit.Texture1);
            _gl.BindTexture(TextureTarget.Texture2D, t.Mate);
            _gl.ActiveTexture(TextureUnit.Texture2);
            _gl.BindTexture(TextureTarget.Texture2D, t.Color);
            _gl.DrawElements(PrimitiveType.Triangles, (uint)_indexCount, DrawElementsType.UnsignedInt, (void*)0);
        }
        _gl.BindVertexArray(0);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.Disable(EnableCap.DepthTest);
    }

    public void Dispose()
    {
        if (_root is not null)
        {
            void Walk(Node n) { if (n.Tile is not null) Delete(n.Tile); foreach (var c in n.Children) Walk(c); }
            Walk(_root);
        }
        if (_albedoArray != 0) _gl.DeleteTexture(_albedoArray);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
        _gl.DeleteProgram(_waterProgram);
    }
}
