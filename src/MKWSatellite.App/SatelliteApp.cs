using System.Numerics;
using ImGuiNET;
using MKWSatellite.Formats.Configuration;
using MKWSatellite.Formats.Course;
using MKWSatellite.Formats.IO;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;

namespace MKWSatellite.App;

/// <summary>
/// Skeleton app: window, ImGui, romfs status, a Banc scene picker and a placeholder top-down preview
/// drawn with ImGui's draw list. The real 3D viewport (GL renderer) will replace the preview.
/// </summary>
public sealed class SatelliteApp : IDisposable
{
    private readonly IWindow _window;
    private readonly string[] _args;
    private GL _gl = null!;
    private IInputContext _input = null!;
    private ImGuiController _imgui = null!;

    private SatelliteSettings _settings = new();
    private GameFileSystem? _fs;
    private string? _fsError;
    private int? _resourceVersion;
    private IReadOnlyList<string> _bancFiles = [];
    private string _filter = "Race_";
    private BancScene? _scene;
    private string? _sceneName, _sceneError;

    public SatelliteApp(IWindow window, string[] args)
    {
        _window = window;
        _args = args;
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.FramebufferResize += s => _gl.Viewport(s);
    }

    private void OnLoad()
    {
        _gl = _window.CreateOpenGL();
        _input = _window.CreateInput();
        _imgui = new ImGuiController(_gl, _window, _input);
        OpenGame();
    }

    private void OpenGame()
    {
        // `MKWSatellite <romfs> [<update romfs>...]` overrides settings.
        _settings = SatelliteSettings.Load(_args.Length > 0 ? _args : null);
        if (!_settings.IsConfigured) return;
        try
        {
            _fs = new GameFileSystem(_settings.Romfs);
            _resourceVersion = _fs.GetResourceVersion();
            _bancFiles = _fs.ListFiles(BancScene.RomfsDirectory, "*.bcett.byml.zs");
        }
        catch (Exception e)
        {
            _fsError = e.Message;
        }
    }

    private void LoadScene(string name)
    {
        _sceneName = name;
        _sceneError = null;
        try { _scene = BancScene.Parse(_fs!.Read($"{BancScene.RomfsDirectory}/{name}")); }
        catch (Exception e) { _scene = null; _sceneError = e.Message; }
    }

    private void OnRender(double delta)
    {
        _imgui.Update((float)delta);
        _gl.ClearColor(0.05f, 0.06f, 0.1f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        DrawGamePanel();
        DrawPreview();

        _imgui.Render();
    }

    private void DrawGamePanel()
    {
        ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(420, 860), ImGuiCond.FirstUseEver);
        ImGui.Begin("Game");
        if (!_settings.IsConfigured)
        {
            ImGui.TextWrapped($"No romfs configured. Pass it as an argument, set {SatelliteSettings.EnvVar}, " +
                              $"or create {SatelliteSettings.FileName} (see mkwsat.local.example.json).");
            ImGui.End();
            return;
        }
        ImGui.TextDisabled($"settings: {_settings.Source}");
        if (_fsError is not null) ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), _fsError);
        if (_fs is not null)
        {
            ImGui.Text($"layers: {_fs.Layers.Count}   resource version: {_resourceVersion?.ToString() ?? "?"}");
            ImGui.InputText("filter", ref _filter, 128);
            ImGui.BeginChild("banc", new Vector2(0, 0), ImGuiChildFlags.Border);
            foreach (var f in _bancFiles)
            {
                if (_filter.Length > 0 && !f.Contains(_filter, StringComparison.OrdinalIgnoreCase)) continue;
                if (ImGui.Selectable(f, f == _sceneName)) LoadScene(f);
            }
            ImGui.EndChild();
        }
        ImGui.End();
    }

    /// <summary>Placeholder top-down (X/Z) view: rails as lines, actors as dots.</summary>
    private void DrawPreview()
    {
        ImGui.SetNextWindowPos(new Vector2(440, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(1150, 860), ImGuiCond.FirstUseEver);
        ImGui.Begin("Preview (top-down placeholder)");
        if (_sceneError is not null) ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), _sceneError);
        if (_scene is null) { ImGui.TextDisabled("Pick a Banc scene."); ImGui.End(); return; }

        ImGui.Text($"{_scene.FilePath}: {_scene.Actors.Count} actors, {_scene.Rails.Count} rails");
        var points = _scene.Rails.SelectMany(r => r.Points).Select(p => p.Translation).ToList();
        if (points.Count == 0) points = _scene.Actors.Select(a => a.Translate).ToList();
        if (points.Count == 0) { ImGui.End(); return; }

        var min = new Vector2(points.Min(p => p.X), points.Min(p => p.Z));
        var max = new Vector2(points.Max(p => p.X), points.Max(p => p.Z));
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();
        var scale = MathF.Min(avail.X / MathF.Max(max.X - min.X, 1), avail.Y / MathF.Max(max.Y - min.Y, 1)) * 0.95f;
        Vector2 ToScreen(Vector3 p) => origin + (new Vector2(p.X, p.Z) - min) * scale;

        var dl = ImGui.GetWindowDrawList();
        var byHash = _scene.Rails.SelectMany(r => r.Points).GroupBy(p => p.Hash).ToDictionary(g => g.Key, g => g.First());
        foreach (var rail in _scene.Rails)
        {
            var col = rail.Name switch
            {
                "LapPathGraph" => 0xFF40C0FFu,   // ABGR
                "RoutePathGraph" => 0xFFFF7060u,
                _ => 0xFF70FF70u,
            };
            var thick = rail.Name == "LapPathGraph" ? 3f : 1f;
            for (var i = 1; i < rail.Points.Count; i++)
                dl.AddLine(ToScreen(rail.Points[i - 1].Translation), ToScreen(rail.Points[i].Translation), col, thick);
            foreach (var p in rail.Points)
                foreach (var dst in p.LinkTargets)
                    if (byHash.TryGetValue(dst, out var q))
                        dl.AddLine(ToScreen(p.Translation), ToScreen(q.Translation), col, thick);
        }
        foreach (var a in _scene.Actors)
            dl.AddCircleFilled(ToScreen(a.Translate), 2.5f, a.Type.Contains("ItemBox") ? 0xFF00CCFFu : 0xFFAAAAAAu);
        ImGui.End();
    }

    public void Dispose()
    {
        _imgui?.Dispose();
        _input?.Dispose();
        _gl?.Dispose();
    }
}
