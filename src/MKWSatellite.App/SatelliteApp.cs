using System.Numerics;
using ImGuiNET;
using MKWSatellite.App.Rendering;
using MKWSatellite.App.View;
using MKWSatellite.Formats.Byml;
using MKWSatellite.Formats.Configuration;
using MKWSatellite.Formats.Course;
using MKWSatellite.Formats.IO;
using MKWSatellite.Formats.Imaging;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;

namespace MKWSatellite.App;

/// <summary>
/// Main window: course list and layer toggles on the left, the GL viewport in the middle, the inspector on the right.
/// Top-down: left-drag pans, wheel zooms, click selects, right-drag measures distance.
/// 3D: left-drag orbits, right/middle-drag pans, wheel zooms, WASD/QE moves, click selects.
/// </summary>
public sealed class SatelliteApp : IDisposable
{
    private const float LeftPanelWidth = 330, RightPanelWidth = 380;

    private readonly IWindow _window;
    private readonly AppOptions _options;
    private GL _gl = null!;
    private IInputContext _input = null!;
    private ImGuiController _imgui = null!;
    private PrimitiveBatch _batch = null!;
    private CourseView _view = null!;
    private readonly TopDownCamera _camera = new();
    private readonly OrbitCamera _orbit = new();
    private bool _use3D, _panning;
    private ModelLibrary? _models;
    private IViewCamera ActiveCamera => _use3D ? _orbit : _camera;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private int _idleFrames;

    private SatelliteSettings _settings = new();
    private GameFileSystem? _fs;
    private CourseLoader? _loader;
    private string? _fsError, _courseError;
    private int? _resourceVersion;
    private IReadOnlyList<string> _raceFiles = [];
    private string _filter = "";
    private string? _courseFile;

    private Vector2? _dragStart, _rulerStart, _rulerEnd;
    private Vector2 _lastMouse;
    private int _frame;

    public SatelliteApp(IWindow window, AppOptions options)
    {
        _window = window;
        _options = options;
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.FramebufferResize += s => _gl.Viewport(s);
        _window.Closing += ReleaseGl;
    }

    /// <summary>GL objects must be released while the context still exists (before the window is destroyed).</summary>
    private void ReleaseGl()
    {
        _models?.Dispose();
        _models = null;
        _batch?.Dispose();
        _imgui?.Dispose();
        _batch = null!;
        _imgui = null!;
    }

    private void OnLoad()
    {
        _gl = _window.CreateOpenGL();
        _input = _window.CreateInput();
        _imgui = new ImGuiController(_gl, _window, _input);
        ImGui.GetStyle().WindowRounding = 0;
        _batch = new PrimitiveBatch(_gl);
        _view = new CourseView(_batch);
        foreach (var m in _input.Mice) m.Scroll += OnScroll;
        OpenGame();
        if (_fs is not null)
        {
            _models = new ModelLibrary(_gl, _fs);
            _view.HasModel = o => _models?.Get(o.Type) is not null;
        }
        _use3D = _options.View3D;
        if (_options.NoModels) _view.Layers.Models = false;
        UpdateViewportRect();
        if (_options.Course is { } c)
        {
            var match = _raceFiles.FirstOrDefault(f => f.Contains(c, StringComparison.OrdinalIgnoreCase));
            if (match is not null) LoadCourse(match);
            else _courseError = $"No race scene matches \"{c}\".";
        }
    }

    private void OpenGame()
    {
        _settings = SatelliteSettings.Load(_options.Romfs.Count > 0 ? _options.Romfs.ToArray() : null);
        if (!_settings.IsConfigured) return;
        try
        {
            _fs = new GameFileSystem(_settings.Romfs);
            _resourceVersion = _fs.GetResourceVersion();
            _loader = new CourseLoader(_fs);
            _raceFiles = _loader.ListRaceScenes();
        }
        catch (Exception e) { _fsError = e.Message; }
    }

    private void LoadCourse(string file)
    {
        _courseFile = file;
        _courseError = null;
        _rulerStart = _rulerEnd = null;
        try
        {
            var course = _loader!.Load(file);
            _view.SetCourse(course, _camera);
            var ys = course.Objects.Select(o => o.Position.Y).Concat(course.LapPoints.Select(l => l.Position.Y)).ToList();
            _camera.DepthMinY = (ys.Count > 0 ? ys.Min() : 0) - 1500;
            _camera.DepthMaxY = (ys.Count > 0 ? ys.Max() : 0) + 1500;
            FrameOrbit();
        }
        catch (Exception e) { _courseError = $"{e.GetType().Name}: {e.Message}"; }
    }

    private void FrameOrbit()
    {
        if (_view.Course is not { } c) return;
        var (min, max) = c.PathBounds;
        _orbit.Frame(min, max);
        if (_options.Orbit is { } o)
        {
            _orbit.Yaw = o.X * MathF.PI / 180;
            _orbit.Pitch = o.Y * MathF.PI / 180;
            if (o.Z > 0) _orbit.Distance = o.Z;
        }
    }

    private void UpdateViewportRect()
    {
        var fb = _window.FramebufferSize;
        foreach (var cam in new IViewCamera[] { _camera, _orbit })
        {
            cam.ViewportPos = new Vector2(LeftPanelWidth, 0);
            cam.ViewportSize = new Vector2(MathF.Max(1, fb.X - LeftPanelWidth - RightPanelWidth), MathF.Max(1, fb.Y));
        }
    }

    private void OnScroll(IMouse mouse, ScrollWheel wheel)
    {
        if (ImGui.GetIO().WantCaptureMouse || !InViewport(mouse.Position)) return;
        if (_use3D) { _orbit.Zoom(MathF.Pow(1.2f, wheel.Y)); _view.Invalidate(); }
        else _camera.ZoomAt(mouse.Position, MathF.Pow(1.2f, wheel.Y));
    }

    private void HandleMouse3D(IMouse mouse, Vector2 pos, bool free, float dt)
    {
        var delta = pos - _lastMouse;
        if (mouse.IsButtonPressed(MouseButton.Left))
        {
            if (_dragStart is null && free) _dragStart = pos;
            else if (_dragStart is not null) _orbit.Orbit(delta);
        }
        else if (_dragStart is { } start)
        {
            if (Vector2.Distance(start, pos) < 3 && free) { _view.Selected = _view.PickAt(_orbit, pos); _view.Invalidate(); }
            _dragStart = null;
        }
        if ((mouse.IsButtonPressed(MouseButton.Right) || mouse.IsButtonPressed(MouseButton.Middle)) && (free || _panning))
        {
            if (_panning) _orbit.Pan(delta);
            _panning = true;
        }
        else _panning = false;

        if (_input.Keyboards.Count > 0 && !ImGui.GetIO().WantCaptureKeyboard)
        {
            var k = _input.Keyboards[0];
            var move = new Vector3(
                (k.IsKeyPressed(Key.D) ? 1 : 0) - (k.IsKeyPressed(Key.A) ? 1 : 0),
                (k.IsKeyPressed(Key.E) ? 1 : 0) - (k.IsKeyPressed(Key.Q) ? 1 : 0),
                (k.IsKeyPressed(Key.W) ? 1 : 0) - (k.IsKeyPressed(Key.S) ? 1 : 0));
            if (move != Vector3.Zero) _orbit.Fly(move * (k.IsKeyPressed(Key.ShiftLeft) ? 3 : 1), dt);
        }
        _view.UpdateHover(_orbit, pos, free && _dragStart is null && !_panning);
        _lastMouse = pos;
    }

    private bool InViewport(Vector2 p) =>
        p.X >= _camera.ViewportPos.X && p.Y >= _camera.ViewportPos.Y &&
        p.X < _camera.ViewportPos.X + _camera.ViewportSize.X && p.Y < _camera.ViewportPos.Y + _camera.ViewportSize.Y;

    private void HandleMouse(float dt)
    {
        var mouse = _input.Mice.Count > 0 ? _input.Mice[0] : null;
        if (mouse is null) return;
        var pos = mouse.Position;
        var free = !ImGui.GetIO().WantCaptureMouse && InViewport(pos);
        if (_use3D) { HandleMouse3D(mouse, pos, free, dt); return; }

        if (mouse.IsButtonPressed(MouseButton.Left))
        {
            if (_dragStart is null && free) _dragStart = pos;
            else if (_dragStart is not null) _camera.PanPixels(pos - _lastMouse);
        }
        else if (_dragStart is { } start)
        {
            if (Vector2.Distance(start, pos) < 3 && free) { _view.Selected = _view.PickAt(_camera, pos); _view.Invalidate(); }
            _dragStart = null;
        }

        if (mouse.IsButtonPressed(MouseButton.Right))
        {
            if (_rulerStart is null && free) _rulerStart = _camera.ScreenToWorld(pos);
            if (_rulerStart is not null) _rulerEnd = _camera.ScreenToWorld(pos);
        }
        else if (_rulerStart is not null && _rulerEnd is not null && Vector2.Distance(_rulerStart.Value, _rulerEnd.Value) * _camera.PixelsPerUnit < 3)
        {
            _rulerStart = _rulerEnd = null; // a click without drag clears the ruler
        }
        else if (!mouse.IsButtonPressed(MouseButton.Right) && _rulerStart is not null && _rulerEnd is null)
        {
            _rulerStart = null;
        }

        _view.UpdateHover(_camera, pos, free && _dragStart is null);
        _lastMouse = pos;
    }

    private void OnRender(double delta)
    {
        _imgui.Update((float)delta);
        UpdateViewportRect();
        if (!_options.IsScreenshot) HandleMouse((float)delta);
        _models?.PumpUploads(_options.IsScreenshot ? 200 : 12);

        var fb = _window.FramebufferSize;
        _gl.Viewport(0, 0, (uint)fb.X, (uint)fb.Y);
        var bg = Palette.ToVector(Palette.Background);
        _gl.ClearColor(bg.X, bg.Y, bg.Z, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // GL viewport covers the area between the side panels (GL's origin is bottom-left).
        var cam = ActiveCamera;
        _gl.Viewport((int)cam.ViewportPos.X, 0, (uint)cam.ViewportSize.X, (uint)cam.ViewportSize.Y);
        DrawModels(cam);
        _view.Draw(cam);
        _gl.Viewport(0, 0, (uint)fb.X, (uint)fb.Y);

        var dl = ImGui.GetBackgroundDrawList();
        dl.PushClipRect(cam.ViewportPos, cam.ViewportPos + cam.ViewportSize);
        _view.DrawLabels(cam, dl);
        if (!_use3D) DrawRuler(dl);
        DrawScaleBar(dl);
        dl.PopClipRect();
        DrawHoverTooltip();

        DrawLeftPanel();
        DrawInspector();

        _imgui.Render();
        _frame++;
        if (_options.IsScreenshot && _frame >= 4)
        {
            // Wait for models to finish loading (bounded), then a few settled frames.
            _idleFrames = (_models?.Pending ?? 0) == 0 ? _idleFrames + 1 : 0;
            if (_idleFrames >= 3 || _clock.Elapsed.TotalSeconds > 300) TakeScreenshot();
        }
    }

    private void DrawModels(IViewCamera cam)
    {
        if (_models is null || _view.Course is not { } c || !_view.Layers.Models) return;
        _models.Begin(cam.ViewProjection, new Vector3(-0.35f, -1f, -0.25f));
        foreach (var o in c.Objects)
        {
            if (!_view.ModelVisible(o) || _models.Get(o.Type) is not { } m) continue;
            var world = Matrix4x4.CreateScale(o.Actor.Scale) * o.Rotation * Matrix4x4.CreateTranslation(o.Position);
            var selected = _view.Selected is PickedObject po && ReferenceEquals(po.Object, o);
            _models.Draw(m, world, selected ? new Vector4(1f, 0.8f, 0.2f, 0.45f) : default);
        }
        _models.End();
    }

    private void TakeScreenshot()
    {
        var fb = _window.FramebufferSize;
        var pixels = new byte[fb.X * fb.Y * 4];
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        _gl.ReadBuffer(ReadBufferMode.Back);
        unsafe
        {
            fixed (byte* p = pixels)
                _gl.ReadPixels(0, 0, (uint)fb.X, (uint)fb.Y, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }
        // GL rows are bottom-up; PNG wants top-down.
        var stride = fb.X * 4;
        var flipped = new byte[pixels.Length];
        for (var y = 0; y < fb.Y; y++) System.Buffer.BlockCopy(pixels, y * stride, flipped, (fb.Y - 1 - y) * stride, stride);
        for (var i = 3; i < flipped.Length; i += 4) flipped[i] = 0xFF;
        PngWriter.Write(_options.Screenshot!, fb.X, fb.Y, flipped);
        Console.WriteLine($"screenshot written: {_options.Screenshot} ({fb.X}x{fb.Y})");
        _window.Close();
    }

    private void DrawRuler(ImDrawListPtr dl)
    {
        if (_rulerStart is not { } a || _rulerEnd is not { } b) return;
        var sa = _camera.WorldToScreen(new Vector3(a.X, 0, a.Y));
        var sb = _camera.WorldToScreen(new Vector3(b.X, 0, b.Y));
        dl.AddLine(sa, sb, Palette.Ruler, 2);
        dl.AddCircleFilled(sa, 3, Palette.Ruler);
        dl.AddCircleFilled(sb, 3, Palette.Ruler);
        var text = $"{Vector2.Distance(a, b):0.0} u (horizontal)";
        dl.AddText((sa + sb) / 2 + new Vector2(8, -8), 0xFF000000, text);
        dl.AddText((sa + sb) / 2 + new Vector2(7, -9), Palette.Ruler, text);
    }

    private void DrawScaleBar(ImDrawListPtr dl)
    {
        if (_view.Course is null) return;
        if (_use3D)
        {
            dl.AddRectFilled(_orbit.ViewportPos, _orbit.ViewportPos + new Vector2(_orbit.ViewportSize.X, 46), 0xC0100C0A);
            dl.AddText(_orbit.ViewportPos + new Vector2(16, 10), 0xFFFFFFFF,
                $"{_view.Course.Name}   static layer: {_view.Course.StaticLayerSource}   game data Product.{_resourceVersion}");
            dl.AddText(_orbit.ViewportPos + new Vector2(16, 28), 0xFFB0B0B0,
                "3D: left-drag orbit, right-drag pan, wheel zoom, WASD/QE move (Shift = faster)" + ModelStatus());
            return;
        }
        // Pick a round length that is ~120 px on screen.
        var target = 120 * _camera.UnitsPerPixel;
        var pow = MathF.Pow(10, MathF.Floor(MathF.Log10(target)));
        var len = new[] { 1f, 2f, 5f, 10f }.Select(m => m * pow).Last(l => l <= target);
        var origin = _camera.ViewportPos + new Vector2(16, _camera.ViewportSize.Y - 24);
        var px = len * _camera.PixelsPerUnit;
        dl.AddLine(origin, origin + new Vector2(px, 0), 0xFFFFFFFF, 2);
        dl.AddLine(origin, origin + new Vector2(0, -6), 0xFFFFFFFF, 2);
        dl.AddLine(origin + new Vector2(px, 0), origin + new Vector2(px, -6), 0xFFFFFFFF, 2);
        dl.AddText(origin + new Vector2(px + 6, -10), 0xFFFFFFFF, $"{len:0.#} units");
        dl.AddRectFilled(_camera.ViewportPos, _camera.ViewportPos + new Vector2(_camera.ViewportSize.X, 46), 0xC0100C0A);
        dl.AddText(_camera.ViewportPos + new Vector2(16, 10), 0xFFFFFFFF,
            $"{_view.Course.Name}   static layer: {_view.Course.StaticLayerSource}   game data Product.{_resourceVersion}");
        dl.AddText(_camera.ViewportPos + new Vector2(16, 28), 0xFFB0B0B0, "top-down: +X right, +Z down" + ModelStatus());
    }

    private string ModelStatus() =>
        _models is null || !_view.Layers.Models ? "" :
        $"   models {_models.LoadedModels}, textures {_models.TextureCount}" + (_models.Pending > 0 ? $", loading {_models.Pending}" : "");

    private void DrawHoverTooltip()
    {
        if (_view.Hovered is not { } h || _options.IsScreenshot) return;
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(Describe(h));
        ImGui.EndTooltip();
    }

    private static string Describe(Pickable p) => p switch
    {
        PickedObject o => $"{o.Object.Type}\n{o.Object.Category} ({o.Object.Layer})",
        PickedLapPoint l => $"Lap path node {l.Point.Index}" + (l.Point.CheckPoint is { } cp ? $"\nCheckPoint {cp:0}" : ""),
        PickedRailPoint r => $"{r.Rail.Kind} node {r.Index} ({r.Rail.Layer})",
        _ => "",
    };

    private void DrawLeftPanel()
    {
        var h = _window.FramebufferSize.Y;
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(new Vector2(LeftPanelWidth, h));
        ImGui.Begin("Courses", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);
        if (!_settings.IsConfigured)
        {
            ImGui.TextWrapped($"No romfs configured. Pass it as an argument, set {SatelliteSettings.EnvVar}, " +
                              $"or create {SatelliteSettings.FileName} (see mkwsat.local.example.json).");
            ImGui.End();
            return;
        }
        if (_fsError is not null) ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), _fsError);
        ImGui.TextDisabled($"romfs layers: {_fs?.Layers.Count}   Product.{_resourceVersion}");
        if (_courseError is not null) ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), _courseError);

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##filter", "filter", ref _filter, 64);
        ImGui.BeginChild("races", new Vector2(0, h * 0.36f), ImGuiChildFlags.Border);
        foreach (var f in _raceFiles)
        {
            if (_filter.Length > 0 && !f.Contains(_filter, StringComparison.OrdinalIgnoreCase)) continue;
            if (ImGui.Selectable(f.Replace(".bcett.byml.zs", ""), f == _courseFile)) LoadCourse(f);
        }
        ImGui.EndChild();

        if (ImGui.RadioButton("Top-down", !_use3D)) { _use3D = false; _view.Invalidate(); }
        ImGui.SameLine();
        if (ImGui.RadioButton("3D", _use3D)) { _use3D = true; _view.Invalidate(); }
        ImGui.SameLine();
        if (ImGui.Button("Frame course")) { _view.FrameCourse(_camera); FrameOrbit(); _view.Invalidate(); }
        ImGui.Separator();
        DrawLayerToggles();
        ImGui.End();
    }

    private void DrawLayerToggles()
    {
        var L = _view.Layers;
        var changed = false;
        void Toggle(string label, ref bool v, uint? swatch = null)
        {
            if (swatch is { } c)
            {
                var p = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddRectFilled(p + new Vector2(0, 3), p + new Vector2(10, 13), c);
                ImGui.Dummy(new Vector2(14, 0));
                ImGui.SameLine();
            }
            changed |= ImGui.Checkbox(label, ref v);
        }

        ImGui.TextDisabled("Paths");
        Toggle("Lap path (coloured by height)", ref L.LapPath, Palette.Height(0.5f, 0, 1));
        Toggle("Checkpoint gates", ref L.CheckpointGates, Palette.CheckpointGate);
        Toggle("All lap gates", ref L.AllGates, Palette.Gate);
        Toggle("Checkpoint labels", ref L.CheckpointLabels);
        Toggle("AI routes", ref L.AiRoutes, Palette.ForRail("RoutePathGraph"));
        Toggle("Glide / gravity / guide rails", ref L.OtherRails, Palette.ForRail("GlidePath"));
        ImGui.TextDisabled("Objects");
        Toggle("Item boxes", ref L.ItemBoxes, Palette.ForCategory(ObjectCategory.ItemBox));
        Toggle("Coins", ref L.Coins, Palette.ForCategory(ObjectCategory.Coin));
        Toggle("Dash / glide / rings / jumps", ref L.Boosts, Palette.ForCategory(ObjectCategory.DashPanel));
        Toggle("Start grid", ref L.StartGrid, Palette.ForCategory(ObjectCategory.StartPosition));
        Toggle("Hazards / moving objects", ref L.Hazards, Palette.ForCategory(ObjectCategory.Hazard));
        Toggle("Fences", ref L.Fences, Palette.ForCategory(ObjectCategory.Fence));
        Toggle("Audience", ref L.Audience, Palette.ForCategory(ObjectCategory.Audience));
        Toggle("Other scenery", ref L.Scenery, Palette.ForCategory(ObjectCategory.Other));
        ImGui.TextDisabled("Areas (base size inferred)");
        Toggle("Rescue / respawn areas", ref L.RespawnAreas, Palette.ForCategory(ObjectCategory.RespawnArea));
        Toggle("Recovery points", ref L.RecoveryPoints, Palette.ForCategory(ObjectCategory.RecoveryPoint));
        Toggle("Valley areas", ref L.ValleyAreas, Palette.ForCategory(ObjectCategory.ValleyArea));
        Toggle("High-speed areas", ref L.HighSpeedAreas, Palette.ForCategory(ObjectCategory.HighSpeedArea));
        Toggle("Invisible dash / glide areas", ref L.BoostAreas, Palette.ForCategory(ObjectCategory.BoostArea));
        Toggle("Rule areas (no reverse, no jump...)", ref L.RuleAreas, Palette.ForCategory(ObjectCategory.RuleArea));
        Toggle("Spawn / delete link areas", ref L.LinkAreas, Palette.ForCategory(ObjectCategory.LinkArea));
        Toggle("Other areas", ref L.OtherAreas, Palette.ForCategory(ObjectCategory.OtherArea));
        ImGui.TextDisabled("Models");
        Toggle("Show models", ref L.Models);
        Toggle("Course geometry (Stage models)", ref L.StageModels);
        Toggle("2D markers on modelled objects", ref L.MarkersOnModels);
        if (_models is { Errors.Count: > 0 } && ImGui.TreeNode($"Model errors ({_models.Errors.Count})"))
        {
            lock (_models.Errors) foreach (var e in _models.Errors.Take(50)) ImGui.TextWrapped(e);
            ImGui.TreePop();
        }
        ImGui.TextDisabled("Scene layers");
        Toggle("Race scene", ref L.LayerRace);
        Toggle("World / course static layer", ref L.LayerWorld);
        Toggle("Course _Fix layer", ref L.LayerCourseFix);
        if (changed) _view.Invalidate();
    }

    private void DrawInspector()
    {
        var fb = _window.FramebufferSize;
        ImGui.SetNextWindowPos(new Vector2(fb.X - RightPanelWidth, 0));
        ImGui.SetNextWindowSize(new Vector2(RightPanelWidth, fb.Y));
        ImGui.Begin("Inspector", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);
        if (_view.Course is { } c && _view.Selected is null)
        {
            ImGui.TextWrapped(c.Name);
            ImGui.TextDisabled($"static layer: {c.StaticLayerSource}");
            ImGui.Text($"{c.Objects.Count} objects, {c.Rails.Count} rails");
            ImGui.Text($"lap nodes {c.LapPoints.Count}, checkpoints {c.LapPoints.Count(l => l.CheckPoint is not null)}");
            ImGui.Separator();
            if (ImGui.BeginTable("cats", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                foreach (var g in c.Objects.GroupBy(o => (o.Category, o.Layer)).OrderBy(g => g.Key.Category))
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextColored(Palette.ToVector(Palette.ForCategory(g.Key.Category)), g.Key.Category.ToString());
                    ImGui.TableNextColumn(); ImGui.TextDisabled(g.Key.Layer.ToString());
                    ImGui.TableNextColumn(); ImGui.Text(g.Count().ToString());
                }
                ImGui.EndTable();
            }
            ImGui.TextDisabled("Click something in the view to inspect it.");
        }
        switch (_view.Selected)
        {
            case PickedObject { Object: var o }:
                ImGui.TextWrapped(o.Type);
                ImGui.TextColored(Palette.ToVector(Palette.ForCategory(o.Category)), $"{o.Category}  ({o.Layer} layer)");
                if (CourseObjectClassifier.Gloss(o.Type) is { } gloss) ImGui.TextWrapped(gloss);
                if (o.Info is { } info) ImGui.TextDisabled($"[{info.Category}] {info.Label}");
                if (_models?.ModelPathOf(o.Type) is { } modelPath) ImGui.TextDisabled($"model: {modelPath}");
                foreach (var link in o.IncomingLinks)
                {
                    var role = (link.Source.Type, link.LinkName) switch
                    {
                        ("SystemObj_CreateLinkArea_00", "CreateLink") =>
                            $"Absent until a racer enters (gone {CreateLinkActiveSec(link.Source):0.#} s after the last one leaves):",
                        ("SystemObj_CreateLinkArea_00", _) => $"Signalled ({link.LinkName}) when a racer enters",
                        ("Area_DeleteLinkedActor_00", _) => "Linked from delete area (likely inert in 1.8)",
                        _ => $"Linked ({link.LinkName}) from",
                    };
                    ImGui.TextColored(new Vector4(1, 1, 0.5f, 1), $"{role} {link.Source.Type} at {link.Source.Position.X:0}, {link.Source.Position.Z:0}");
                }
                if (o.Actor.Raw.Dict("ScreenSetting") is { } screens)
                    ImGui.TextColored(new Vector4(0.6f, 0.8f, 1, 1), "Split-screen: " + string.Join(", ", screens.Select(kv => $"{kv.Key}={kv.Value}")) + "  (1 = not spawned in that mode; ignored in single-player)");
                Vec("Position", o.Position);
                Vec("Rotate (deg)", o.Actor.Rotate * (180f / MathF.PI));
                Vec("Scale", o.Actor.Scale);
                if (o.Actor.Raw.Get<string>("AreaName") is { } area) ImGui.TextDisabled($"area: {area}");
                Dict("Dynamic", o.Actor.Dynamic);
                Dict("All fields", o.Actor.Raw);
                break;
            case PickedLapPoint { Point: var l }:
                ImGui.Text($"Lap path node {l.Index}");
                if (l.CheckPoint is { } cp) ImGui.TextColored(Palette.ToVector(Palette.CheckpointGate), $"CheckPoint {cp:0}");
                if (l.IsStart) ImGui.Text("IsStart");
                if (l.IsGoal) ImGui.Text("IsGoal");
                if (l.IsLapCheck) ImGui.Text("IsLapCheck");
                Vec("Position", l.Position);
                Vec("Rotate (deg)", l.Point.Rotate * (180f / MathF.PI));
                ImGui.Text($"Gate size  {l.RectSize.X:0.##} x {l.RectSize.Y:0.##}");
                ImGui.Text($"Gate offset {l.RectOffset.X:0.##}, {l.RectOffset.Y:0.##}");
                Dict("Dynamic", l.Point.Dynamic);
                Dict("All fields", l.Point.Raw);
                break;
            case PickedRailPoint { Rail: var r, Index: var i }:
                var pt = r.Rail.Points[i];
                ImGui.Text($"{r.Kind} node {i} ({r.Layer})");
                Vec("Position", pt.Translation);
                Dict("Dynamic", pt.Dynamic);
                Dict("All fields", pt.Raw);
                break;
        }
        if (_view.Selected is not null && ImGui.Button("Clear selection")) { _view.Selected = null; _view.Invalidate(); }
        ImGui.End();
    }

    /// <summary>CreateLinkArea's <c>ActiveSec</c>; the controller defaults to 600 frames (10 s) when absent.</summary>
    private static float CreateLinkActiveSec(CourseObject area) =>
        area.Actor.Dynamic?.TryGetValue("ActiveSec", out var v) == true && v is float f ? f : 10f;

    private static void Vec(string label, Vector3 v) => ImGui.Text($"{label,-13} {v.X,9:0.###} {v.Y,9:0.###} {v.Z,9:0.###}");

    private static void Dict(string label, BymlDict? d)
    {
        if (d is null || d.Count == 0) return;
        if (!ImGui.TreeNodeEx(label, label == "Dynamic" ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None)) return;
        foreach (var (k, v) in d) ImGui.TextWrapped($"{k}: {Format(v)}");
        ImGui.TreePop();
    }

    private static string Format(object? v) => v switch
    {
        null => "null",
        float f => f.ToString("0.####"),
        BymlArray a when a.All(x => x is not BymlDict and not BymlArray) => "[" + string.Join(", ", a.Select(Format)) + "]",
        BymlArray a => $"[{a.Count} items]",
        BymlDict d => "{" + string.Join(", ", d.Select(kv => $"{kv.Key}: {Format(kv.Value)}")) + "}",
        _ => v.ToString() ?? "",
    };

    public void Dispose()
    {
        _input?.Dispose();
        _gl?.Dispose();
    }
}
