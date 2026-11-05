using System.Numerics;
using ImGuiNET;
using MKWSatellite.App.Rendering;
using MKWSatellite.App.View;
using MKWSatellite.Formats.Byml;
using MKWSatellite.Formats.Configuration;
using MKWSatellite.Formats.Course;
using MKWSatellite.Formats.IO;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;

namespace MKWSatellite.App;

/// <summary>
/// Main window: course list and layer toggles on the left, the GL top-down viewport in the middle, the inspector on
/// the right. Left-drag pans, wheel zooms, click selects, right-drag measures distance.
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
        try { _view.SetCourse(_loader!.Load(file), _camera); }
        catch (Exception e) { _courseError = $"{e.GetType().Name}: {e.Message}"; }
    }

    private void UpdateViewportRect()
    {
        var fb = _window.FramebufferSize;
        _camera.ViewportPos = new Vector2(LeftPanelWidth, 0);
        _camera.ViewportSize = new Vector2(MathF.Max(1, fb.X - LeftPanelWidth - RightPanelWidth), MathF.Max(1, fb.Y));
    }

    private void OnScroll(IMouse mouse, ScrollWheel wheel)
    {
        if (ImGui.GetIO().WantCaptureMouse || !InViewport(mouse.Position)) return;
        _camera.ZoomAt(mouse.Position, MathF.Pow(1.2f, wheel.Y));
    }

    private bool InViewport(Vector2 p) =>
        p.X >= _camera.ViewportPos.X && p.Y >= _camera.ViewportPos.Y &&
        p.X < _camera.ViewportPos.X + _camera.ViewportSize.X && p.Y < _camera.ViewportPos.Y + _camera.ViewportSize.Y;

    private void HandleMouse()
    {
        var mouse = _input.Mice.Count > 0 ? _input.Mice[0] : null;
        if (mouse is null) return;
        var pos = mouse.Position;
        var free = !ImGui.GetIO().WantCaptureMouse && InViewport(pos);

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
        if (!_options.IsScreenshot) HandleMouse();

        var fb = _window.FramebufferSize;
        _gl.Viewport(0, 0, (uint)fb.X, (uint)fb.Y);
        var bg = Palette.ToVector(Palette.Background);
        _gl.ClearColor(bg.X, bg.Y, bg.Z, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // GL viewport covers the area between the side panels (GL's origin is bottom-left).
        _gl.Viewport((int)_camera.ViewportPos.X, 0, (uint)_camera.ViewportSize.X, (uint)_camera.ViewportSize.Y);
        _view.Draw(_camera);
        _gl.Viewport(0, 0, (uint)fb.X, (uint)fb.Y);

        var dl = ImGui.GetBackgroundDrawList();
        dl.PushClipRect(_camera.ViewportPos, _camera.ViewportPos + _camera.ViewportSize);
        _view.DrawLabels(_camera, dl);
        DrawRuler(dl);
        DrawScaleBar(dl);
        dl.PopClipRect();
        DrawHoverTooltip();

        DrawLeftPanel();
        DrawInspector();

        _imgui.Render();
        _frame++;
        if (_options.IsScreenshot && _frame == 4) TakeScreenshot();
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
        dl.AddText(_camera.ViewportPos + new Vector2(16, 28), 0xFFB0B0B0, "top-down: +X right, +Z down");
    }

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

        if (ImGui.Button("Frame course")) _view.FrameCourse(_camera);
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
        ImGui.TextDisabled("Areas (box size unverified)");
        Toggle("Respawn (RescueArea)", ref L.RespawnAreas, Palette.ForCategory(ObjectCategory.RespawnArea));
        Toggle("Valley (fall-out)", ref L.ValleyAreas, Palette.ForCategory(ObjectCategory.ValleyArea));
        Toggle("Kill areas", ref L.KillAreas, Palette.ForCategory(ObjectCategory.KillArea));
        Toggle("High-speed areas", ref L.HighSpeedAreas, Palette.ForCategory(ObjectCategory.HighSpeedArea));
        Toggle("Other areas", ref L.OtherAreas, Palette.ForCategory(ObjectCategory.OtherArea));
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
