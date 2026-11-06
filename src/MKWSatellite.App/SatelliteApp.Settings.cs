using System.Numerics;
using ImGuiNET;
using MKWSatellite.Formats.Configuration;

namespace MKWSatellite.App;

/// <summary>
/// The "Game data" section: edit the romfs folders and mod output, save them to mkwsat.local.json and reload the game
/// data without restarting.
/// </summary>
public sealed partial class SatelliteApp
{
    private List<string>? _editRomfs;
    private string _editModOutput = "";
    private string? _settingsMessage;
    private bool _settingsFailed;

    private void BeginSettingsEdit()
    {
        _editRomfs = _settings.Romfs.Count > 0 ? [.. _settings.Romfs] : [""];
        _editModOutput = _settings.ModOutput ?? "";
    }

    /// <summary>Draws the game-data settings; open by default while nothing is loaded.</summary>
    private void DrawSettings()
    {
        var needsSetup = !_settings.IsConfigured || _fs is null;
        ImGui.SetNextItemOpen(needsSetup, needsSetup ? ImGuiCond.Always : ImGuiCond.Appearing);
        if (!ImGui.CollapsingHeader("Game data")) return;
        if (_editRomfs is null) BeginSettingsEdit();
        var rows = _editRomfs!;

        if (!_settings.IsConfigured)
            ImGui.TextWrapped("Set the folder of your extracted romfs below and press Save and load. Add the update's " +
                              "romfs after the base game's if they are separate.");
        Hint("romfs folders (base game first; later ones override earlier ones):");
        int? remove = null, moveUp = null;
        for (var i = 0; i < rows.Count; i++)
        {
            ImGui.PushID(i);
            var path = rows[i];
            ImGui.SetNextItemWidth(-(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.X) * 2);
            if (ImGui.InputTextWithHint("##romfs", @"e.g. D:\Dumps\MKW\romfs", ref path, 1024)) rows[i] = CleanPath(path);
            ImGui.SameLine();
            ImGui.BeginDisabled(i == 0);
            if (ImGui.Button("^", new Vector2(ImGui.GetFrameHeight(), 0))) moveUp = i;
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Move up (load earlier)");
            ImGui.SameLine();
            if (ImGui.Button("x", new Vector2(ImGui.GetFrameHeight(), 0))) remove = i;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Remove");
            if (rows[i].Length > 0 && SatelliteSettings.CheckRomfs(rows[i]) is { } problem)
            {
                ImGui.TextColored(new Vector4(1, 0.6f, 0.3f, 1), $"  {problem}");
                var sub = Path.Combine(rows[i], "romfs");
                if (Directory.Exists(sub)) { ImGui.SameLine(); if (ImGui.SmallButton("use it")) rows[i] = sub; }
            }
            ImGui.PopID();
        }
        if (moveUp is { } m) (rows[m - 1], rows[m]) = (rows[m], rows[m - 1]);
        if (remove is { } r) rows.RemoveAt(r);
        if (ImGui.Button("Add folder")) rows.Add("");

        Hint("Mod output folder, where edited scenes are saved (optional):");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##modoutput", "default: mod/romfs in the working folder", ref _editModOutput, 1024))
            _editModOutput = CleanPath(_editModOutput);

        var valid = rows.Any(p => p.Length > 0);
        ImGui.BeginDisabled(!valid);
        if (ImGui.Button("Save and load")) ApplySettings();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip($"Writes {_settings.SavePath} and reloads the game data." +
                             (_history.IsDirty ? "\nUnsaved course edits will be lost." : ""));
        ImGui.SameLine();
        if (ImGui.Button("Undo changes")) { BeginSettingsEdit(); _settingsMessage = null; }

        if (_settings.FilePath is null && _settings.IsConfigured)
            ImGui.TextWrapped($"Currently from the {_settings.Source}, which takes priority over the saved file on later starts.");
        if (_settingsMessage is not null)
            ImGui.TextColored(_settingsFailed ? new Vector4(1, 0.4f, 0.4f, 1) : new Vector4(0.5f, 1, 0.6f, 1), _settingsMessage);
        ImGui.Separator();
    }

    private static void Hint(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    /// <summary>Strips quotes (Explorer's "Copy as path") and surrounding spaces.</summary>
    private static string CleanPath(string p) => p.Trim().Trim('"').Trim();

    private void ApplySettings()
    {
        var settings = new SatelliteSettings
        {
            Romfs = _editRomfs!.Where(p => p.Length > 0).ToList(),
            ModOutput = string.IsNullOrWhiteSpace(_editModOutput) ? null : _editModOutput,
        };
        string path;
        try { path = settings.Save(); }
        catch (Exception e)
        {
            _settingsFailed = true;
            _settingsMessage = $"Could not save: {e.Message}";
            return;
        }
        var course = _courseFile;
        CloseGame();
        OpenGame(settings); // the edited values, even if the command line or environment would win on the next start
        InitEditing();
        BeginSettingsEdit();
        _settingsFailed = _fs is null;
        _settingsMessage = _fs is null ? $"Saved {path}, but loading failed." : $"Saved {path}. Loaded {_raceFiles.Count} race scenes.";
        if (course is not null && _raceFiles.Contains(course)) LoadCourse(course, force: true);
    }
}
