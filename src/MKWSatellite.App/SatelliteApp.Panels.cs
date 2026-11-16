using System.Numerics;
using ImGuiNET;

namespace MKWSatellite.App;

/// <summary>
/// Side panel layout: widths the user can drag (a thin splitter on each panel's inner edge), panels that can be hidden
/// (a small button brings them back), and text helpers that wrap at the panel edge or copy their text when clicked.
/// </summary>
public sealed partial class SatelliteApp
{
    private const float BaseLeftPanelWidth = 330, BaseRightPanelWidth = 380, MinPanelWidth = 200;
    /// <summary>Panel widths in pixels (at the current UI scale); 0 until the first <see cref="ApplyUiScale"/>.</summary>
    private float _leftPx, _rightPx;
    private bool _leftHidden, _rightHidden;
    private float LeftPanelWidth => _options.NoUi || _leftHidden ? 0 : _leftPx;
    private float RightPanelWidth => _options.NoUi || _rightHidden ? 0 : _rightPx;

    private const ImGuiWindowFlags PanelFlags =
        ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBringToFrontOnFocus;

    /// <summary>Keeps the panel widths the same on screen relative to the UI when the scale changes.</summary>
    private void ScalePanels(float oldScale, float newScale)
    {
        if (_leftPx <= 0) (_leftPx, _rightPx) = (BaseLeftPanelWidth * newScale, BaseRightPanelWidth * newScale);
        else (_leftPx, _rightPx) = (_leftPx * newScale / oldScale, _rightPx * newScale / oldScale);
    }

    /// <summary>Splitters and "show" buttons for the side panels; call after the panels are drawn.</summary>
    private void DrawPanelChrome()
    {
        var fb = _window.FramebufferSize;
        var max = MathF.Max(MinPanelWidth * _uiScale, fb.X * 0.45f);
        if (!_leftHidden) Splitter("##splitL", _leftPx, ref _leftPx, +1, max);
        else if (ShowButton("##showL", new Vector2(0, 0), ">>", "Show the course list and layers")) _leftHidden = false;
        if (!_rightHidden) Splitter("##splitR", fb.X - _rightPx, ref _rightPx, -1, max);
        else if (ShowButton("##showR", new Vector2(fb.X, 0), "<<", "Show the inspector")) _rightHidden = false;
    }

    /// <summary>A thin draggable strip centred on <paramref name="x"/>; dragging changes <paramref name="width"/>.</summary>
    private void Splitter(string id, float x, ref float width, int sign, float max)
    {
        var h = _window.FramebufferSize.Y;
        var w = 6 * _uiScale;
        ImGui.SetNextWindowPos(new Vector2(x - w / 2, 0));
        ImGui.SetNextWindowSize(new Vector2(w, h));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.One);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.Begin(id, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMove |
                        ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);
        ImGui.InvisibleButton("##drag", new Vector2(w, h));
        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
            ImGui.GetForegroundDrawList().AddLine(new Vector2(x, 0), new Vector2(x, h), ImGui.GetColorU32(ImGuiCol.SeparatorHovered), 2 * _uiScale);
        }
        if (ImGui.IsItemActive())
            width = Math.Clamp(width + sign * ImGui.GetIO().MouseDelta.X, MinPanelWidth * _uiScale, max);
        ImGui.End();
        ImGui.PopStyleVar(3);
    }

    /// <summary>A small button at a screen corner (x = 0 or the right edge) that brings a hidden panel back.</summary>
    private bool ShowButton(string id, Vector2 corner, string label, string tip)
    {
        var size = new Vector2(ImGui.GetFrameHeight() * 1.6f, ImGui.GetFrameHeight());
        var pad = ImGui.GetStyle().WindowPadding;
        ImGui.SetNextWindowPos(corner.X > 0 ? corner - new Vector2(size.X + pad.X * 2, 0) : corner);
        ImGui.Begin(id, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove |
                        ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing);
        var clicked = ImGui.Button(label, size);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
        ImGui.End();
        return clicked;
    }

    /// <summary>A "hide" button at the right end of the current panel's title bar.</summary>
    private bool HideButton(string label, string tip)
    {
        var style = ImGui.GetStyle();
        var w = ImGui.CalcTextSize(label).X + style.FramePadding.X * 2;
        var winPos = ImGui.GetWindowPos();
        var cursor = ImGui.GetCursorPos();
        ImGui.PushClipRect(winPos, winPos + ImGui.GetWindowSize(), false);
        ImGui.SetCursorScreenPos(winPos + new Vector2(ImGui.GetWindowWidth() - w - style.FramePadding.X,
            (ImGui.GetFrameHeight() - ImGui.GetTextLineHeight()) / 2));
        var clicked = ImGui.SmallButton(label);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
        ImGui.PopClipRect();
        ImGui.SetCursorPos(cursor);
        return clicked;
    }

    // --- text that wraps at the panel edge ---

    /// <summary>Text wrapped at the panel edge; unformatted, so a '%' in game strings is shown as is.</summary>
    private static void Wrapped(string text)
    {
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }

    private static void Colored(Vector4 c, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, c);
        Wrapped(text);
        ImGui.PopStyleColor();
    }

    // --- inspector text: wraps, and clicking copies it ---

    private string? _copied;
    private double _copiedAt;

    /// <summary>Copies <paramref name="value"/> when the last item is clicked (hand cursor and a note on hover).</summary>
    private void CopyOnClick(string value)
    {
        if (!ImGui.IsItemHovered()) return;
        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            ImGui.SetClipboardText(value);
            (_copied, _copiedAt) = (value, ImGui.GetTime());
        }
        var justCopied = _copied == value && ImGui.GetTime() - _copiedAt < 1.5;
        ImGui.SetTooltip(justCopied ? "Copied" : value.Length > 60 ? "Click to copy" : $"Click to copy: {value}");
    }

    /// <summary>Wrapped inspector text; clicking copies <paramref name="copy"/> (default: the text itself).</summary>
    private void IText(string text, string? copy = null)
    {
        Wrapped(text);
        CopyOnClick(copy ?? text);
    }

    private void IDim(string text, string? copy = null)
    {
        Hint(text);
        CopyOnClick(copy ?? text);
    }

    private void IColored(Vector4 c, string text, string? copy = null)
    {
        Colored(c, text);
        CopyOnClick(copy ?? text);
    }
}
