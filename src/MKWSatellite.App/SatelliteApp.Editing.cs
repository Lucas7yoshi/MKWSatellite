using System.Numerics;
using ImGuiNET;
using MKWSatellite.App.Rendering;
using MKWSatellite.App.View;
using MKWSatellite.Formats.Byml;
using MKWSatellite.Formats.Course;
using Silk.NET.Input;

namespace MKWSatellite.App;

/// <summary>
/// Editing the race scene: inspector fields, Ctrl+drag to move, undo/redo, save to a mod folder. Only the race layer
/// (<c>Race_*.bcett</c>) is editable; world tiles and _Fix layers are shared with other courses and stay read-only.
/// Edits change the scene's BYML tree in place (<see cref="BymlEditHistory"/>), then the race layer is rebuilt.
/// </summary>
public sealed partial class SatelliteApp
{
    private BymlEditHistory _history = new();
    private string _modOutput = "";
    private string? _saveMessage;
    private bool _saveFailed;
    private string? _pendingCourse;
    private bool _grabbing;
    private Vector3 _grabOffset;
    private string _newFlag = "";

    /// <summary>The BYML dict behind a selection and its position key, if it is editable (race layer).</summary>
    private static (BymlDict Raw, string PosKey, bool IsActor)? EditTarget(Pickable? p) => p switch
    {
        PickedObject { Object: { Layer: CourseLayer.Race } o } => (o.Actor.Raw, "Translate", true),
        PickedLapPoint { Point: var l } when l.Rail.Layer == CourseLayer.Race => (l.Point.Raw, "Translation", false),
        PickedRailPoint { Rail: { Layer: CourseLayer.Race } r, Index: var i } => (r.Rail.Points[i].Raw, "Translation", false),
        _ => null,
    };

    private void InitEditing()
    {
        _modOutput = _settings.ModOutput ?? Path.Combine(Environment.CurrentDirectory, "mod", "romfs");
    }

    private static BymlArray Array3(Vector3 v) => new(3) { v.X, v.Y, v.Z };

    private static Vector3 ReadVec(BymlDict d, string key, Vector3 fallback) =>
        d.Array(key) is { Count: >= 3 } a ? new Vector3(Convert.ToSingle(a[0]), Convert.ToSingle(a[1]), Convert.ToSingle(a[2])) : fallback;

    /// <summary>Rebuilds the race layer from the edited tree and keeps the camera and selection.</summary>
    private void AfterEdit()
    {
        if (_view.Course is not { } course || _loader is null) return;
        var race = BancScene.FromRoot(course.RaceScene.Raw, course.RaceScene.BymlVersion);
        _view.ReplaceCourse(_loader.RebuildRace(course, race));
        _saveMessage = null;
    }

    private void HandleEditKeys()
    {
        var io = ImGui.GetIO();
        if (io.WantTextInput || !io.KeyCtrl) return;
        if (ImGui.IsKeyPressed(ImGuiKey.Z)) { if (io.KeyShift) _history.Redo(); else _history.Undo(); AfterEdit(); }
        else if (ImGui.IsKeyPressed(ImGuiKey.Y)) { _history.Redo(); AfterEdit(); }
        else if (ImGui.IsKeyPressed(ImGuiKey.S)) SaveCourse();
    }

    private bool CtrlHeld() =>
        _input.Keyboards.Count > 0 && (_input.Keyboards[0].IsKeyPressed(Key.ControlLeft) || _input.Keyboards[0].IsKeyPressed(Key.ControlRight));

    /// <summary>
    /// Ctrl + left-drag moves the selected (editable) item in the horizontal plane at its height. <paramref name="hit"/>
    /// maps the cursor to the world at a given height. Returns true while a move is in progress (camera input skipped).
    /// </summary>
    private bool HandleGrab(IMouse mouse, Vector2 pos, bool free, Func<Vector2, float, Vector3?> hit)
    {
        if (!mouse.IsButtonPressed(MouseButton.Left))
        {
            if (_grabbing) { _grabbing = false; _history.Seal(); }
            return false;
        }
        if (EditTarget(_view.Selected) is not { } t) return _grabbing = false;
        var current = ReadVec(t.Raw, t.PosKey, Vector3.Zero);
        if (!_grabbing)
        {
            if (!free || !CtrlHeld() || _dragStart is not null) return false;
            if (hit(pos, current.Y) is not { } start) return false;
            _grabbing = true;
            _history.Seal();
            _grabOffset = current - start;
            return true;
        }
        if (hit(pos, current.Y) is { } p)
        {
            var target = new Vector3(p.X + _grabOffset.X, current.Y, p.Z + _grabOffset.Z);
            if (Vector3.DistanceSquared(target, current) > 1e-8f)
            {
                _history.Set(t.Raw, t.PosKey, Array3(target));
                AfterEdit();
            }
        }
        return true;
    }

    private static Vector3? TopDownHit(TopDownCamera cam, Vector2 screen, float y)
    {
        var w = cam.ScreenToWorld(screen);
        return new Vector3(w.X, y, w.Y);
    }

    private static Vector3? OrbitHit(OrbitCamera cam, Vector2 screen, float y)
    {
        var (o, d) = cam.Ray(screen);
        if (MathF.Abs(d.Y) < 1e-5f) return null;
        var t = (y - o.Y) / d.Y;
        return t > 0 ? o + d * t : null;
    }

    // --- inspector ---

    /// <summary>Editable transform and Dynamic fields for the selection. Returns false if it is read-only.</summary>
    private bool DrawEditFields()
    {
        if (EditTarget(_view.Selected) is not { } t)
        {
            if (_view.Selected is not null) ImGui.TextDisabled("Read-only (only the race scene layer is editable).");
            return false;
        }
        ImGui.TextDisabled("Ctrl+drag in the view to move; Ctrl+Z / Ctrl+Y undo/redo.");
        ImGui.PushItemWidth(-ImGui.CalcTextSize("Rotate (deg)").X - 12 * _uiScale);
        EditVec(t.Raw, t.PosKey, "Position", Vector3.Zero, 1f, 1f);
        EditVec(t.Raw, "Rotate", "Rotate (deg)", Vector3.Zero, 1f, 180f / MathF.PI);
        if (t.IsActor) EditVec(t.Raw, "Scale", "Scale", Vector3.One, 0.05f, 1f);
        ImGui.PopItemWidth();
        EditDynamic(t.Raw);
        return true;
    }

    /// <summary>A drag field for a 3-float array; <paramref name="display"/> converts stored units to shown units.</summary>
    private void EditVec(BymlDict raw, string key, string label, Vector3 fallback, float speed, float display)
    {
        var v = ReadVec(raw, key, fallback) * display;
        if (ImGui.DragFloat3(label, ref v, speed, 0, 0, "%.3f")) { _history.Set(raw, key, Array3(v / display)); AfterEdit(); }
        if (ImGui.IsItemDeactivatedAfterEdit()) _history.Seal();
    }

    private void EditDynamic(BymlDict raw)
    {
        var dyn = raw.Dict("Dynamic");
        if (!ImGui.TreeNodeEx("Dynamic (editable)", ImGuiTreeNodeFlags.DefaultOpen)) return;
        if (dyn is not null)
        {
            foreach (var key in dyn.Keys.ToList())
            {
                ImGui.PushID(key);
                if (ImGui.SmallButton("x")) { _history.Remove(dyn, key); _history.Seal(); AfterEdit(); ImGui.PopID(); continue; }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.45f);
                switch (dyn[key])
                {
                    case bool b:
                        if (ImGui.Checkbox(key, ref b)) { _history.Set(dyn, key, b); _history.Seal(); AfterEdit(); }
                        break;
                    case int i:
                        if (ImGui.InputInt(key, ref i)) { _history.Set(dyn, key, i); AfterEdit(); }
                        break;
                    case float f:
                        if (ImGui.DragFloat(key, ref f, 0.1f)) { _history.Set(dyn, key, f); AfterEdit(); }
                        break;
                    case string s:
                        if (ImGui.InputText(key, ref s, 256, ImGuiInputTextFlags.EnterReturnsTrue)) { _history.Set(dyn, key, s); _history.Seal(); AfterEdit(); }
                        break;
                    default:
                        ImGui.TextDisabled($"{key}: {dyn[key]}");
                        break;
                }
                if (ImGui.IsItemDeactivatedAfterEdit()) _history.Seal();
                ImGui.PopID();
            }
        }
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.6f);
        ImGui.InputTextWithHint("##newflag", "new bool key, e.g. IsForbidKillerFinish", ref _newFlag, 64);
        ImGui.SameLine();
        if (ImGui.Button("Add") && _newFlag.Trim() is { Length: > 0 } name && dyn?.ContainsKey(name) != true)
        {
            _history.Seal();
            if (dyn is null)
            {
                dyn = new BymlDict(1);
                _history.Set(raw, "Dynamic", dyn);
                _history.Seal();
            }
            _history.Set(dyn, name, true);
            _history.Seal();
            _newFlag = "";
            AfterEdit();
        }
        ImGui.TreePop();
    }

    // --- left panel: save / revert ---

    private void DrawEditPanel()
    {
        if (_view.Course is null) return;
        ImGui.TextDisabled("Editing (race scene)");
        ImGui.BeginDisabled(!_history.CanUndo);
        if (ImGui.Button("Undo")) { _history.Undo(); AfterEdit(); }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!_history.CanRedo);
        if (ImGui.Button("Redo")) { _history.Redo(); AfterEdit(); }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!_history.IsDirty);
        if (ImGui.Button("Save")) SaveCourse();
        ImGui.SameLine();
        if (ImGui.Button("Revert") && _courseFile is { } f) LoadCourse(f, force: true);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextColored(_history.IsDirty ? new Vector4(1, 0.8f, 0.3f, 1) : new Vector4(0.6f, 0.6f, 0.6f, 1),
            _history.IsDirty ? "unsaved" : "saved");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##modout", ref _modOutput, 512);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Mod romfs folder to save into (as <folder>/Banc/<scene>). Add it as the last \"romfs\" entry " +
                             "in mkwsat.local.json to load your edits back. Never the game dump.");
        if (_saveMessage is not null)
            ImGui.TextColored(_saveFailed ? new Vector4(1, 0.4f, 0.4f, 1) : new Vector4(0.5f, 1, 0.6f, 1), _saveMessage);
        ImGui.Separator();
    }

    private void SaveCourse()
    {
        if (_view.Course is not { } c || _courseFile is null || _fs is null) return;
        try
        {
            var relative = $"{BancScene.RomfsDirectory}/{_courseFile}";
            var original = _fs.Exists(relative) ? _fs.Read(relative).Length : 0;
            var r = BancSaver.Save(c.RaceScene, _courseFile, _modOutput, _fs.Layers, original);
            _history.MarkSaved();
            _saveFailed = false;
            _saveMessage = $"Saved {r.DataSize / 1024} KB to {r.Path}" +
                           (r.Grew ? $"\nWarning: padded size {r.PaddedSize} > original {r.OriginalPaddedSize}; the game may not load it." : "");
        }
        catch (Exception e)
        {
            _saveFailed = true;
            _saveMessage = $"Save failed: {e.Message}";
        }
    }

    /// <summary>Asks before switching course with unsaved edits.</summary>
    private void DrawDiscardPopup()
    {
        if (_pendingCourse is not null && !ImGui.IsPopupOpen("Unsaved changes")) ImGui.OpenPopup("Unsaved changes");
        if (!ImGui.BeginPopupModal("Unsaved changes", ImGuiWindowFlags.AlwaysAutoResize)) return;
        ImGui.Text("The current course has unsaved edits.");
        if (ImGui.Button("Save, then switch")) { SaveCourse(); if (!_saveFailed) Switch(); }
        ImGui.SameLine();
        if (ImGui.Button("Discard")) Switch();
        ImGui.SameLine();
        if (ImGui.Button("Cancel")) { _pendingCourse = null; ImGui.CloseCurrentPopup(); }
        ImGui.EndPopup();

        void Switch()
        {
            var f = _pendingCourse!;
            _pendingCourse = null;
            ImGui.CloseCurrentPopup();
            LoadCourse(f, force: true);
        }
    }
}
