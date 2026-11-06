using System.Text.Json;
using System.Text.Json.Serialization;

namespace MKWSatellite.Formats.Configuration;

/// <summary>
/// Where the user's game dump lives. Never hard-code or commit these paths; they are machine-specific.
/// Resolution order: explicit argument, then the <c>MKWSAT_ROMFS</c> environment variable
/// (paths separated by the platform path separator, base first), then <c>mkwsat.local.json</c>
/// in the working directory or next to the executable (git-ignored; see <c>mkwsat.local.example.json</c>). The viewer
/// can create and edit that file (<see cref="Save"/>).
/// </summary>
public sealed class SatelliteSettings
{
    public const string FileName = "mkwsat.local.json";
    public const string EnvVar = "MKWSAT_ROMFS";

    /// <summary>Extracted romfs directories, base game first, then update(s). Later entries override earlier ones.</summary>
    [JsonPropertyName("romfs")]
    public List<string> Romfs { get; set; } = [];

    /// <summary>Mod romfs folder the viewer saves edited scenes into (optional; defaults to <c>mod/romfs</c> in the working directory).</summary>
    [JsonPropertyName("modOutput")]
    public string? ModOutput { get; set; }

    /// <summary>Viewer camera style last used: <c>topdown</c>, <c>3d</c> or <c>firstperson</c> (not the camera position).</summary>
    [JsonPropertyName("viewMode")]
    public string? ViewMode { get; set; }

    /// <summary>Where the settings came from, for display.</summary>
    [JsonIgnore]
    public string Source { get; private set; } = "none";

    /// <summary>The settings file these came from, or null when they came from the command line or environment.</summary>
    [JsonIgnore]
    public string? FilePath { get; private set; }

    [JsonIgnore]
    public bool IsConfigured => Romfs.Count > 0;

    /// <summary>
    /// Where <see cref="Save"/> writes: the file the settings were loaded from, an existing settings file, or a new
    /// one next to the executable.
    /// </summary>
    [JsonIgnore]
    public string SavePath => FilePath ?? FindFile() ?? Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>Writes the settings file (romfs list and mod output), makes it this instance's source, and returns its path.
    /// Keeps the file's viewer preferences when this instance has none.</summary>
    public string Save()
    {
        var path = SavePath;
        Write(path, new SatelliteSettings
        {
            Romfs = [.. Romfs],
            ModOutput = string.IsNullOrWhiteSpace(ModOutput) ? null : ModOutput,
            ViewMode = ViewMode ?? ReadFile()?.ViewMode,
        });
        FilePath = path;
        Source = path;
        return path;
    }

    /// <summary>The settings file's contents, whatever the romfs source in use (the command line or environment only
    /// replace the romfs list), or null when there is no readable file.</summary>
    public static SatelliteSettings? ReadFile()
    {
        if (FindFile() is not { } path) return null;
        try { return JsonSerializer.Deserialize(File.ReadAllText(path), SatelliteSettingsJson.Default.SatelliteSettings); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Changes the settings file in place (creating it next to the executable if there is none).</summary>
    public static void UpdateFile(Action<SatelliteSettings> change)
    {
        var path = FindFile() ?? Path.Combine(AppContext.BaseDirectory, FileName);
        var s = ReadFile() ?? new SatelliteSettings();
        change(s);
        Write(path, s);
    }

    private static void Write(string path, SatelliteSettings s) =>
        File.WriteAllText(path, JsonSerializer.Serialize(s, SatelliteSettingsJson.Default.SatelliteSettings) + Environment.NewLine);

    /// <summary>Checks that a folder looks like an extracted romfs (has <c>Banc</c> or <c>RSDB</c>); returns a problem or null.</summary>
    public static string? CheckRomfs(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return "empty path";
        if (!Directory.Exists(dir)) return "folder not found";
        if (Directory.Exists(Path.Combine(dir, "Banc")) || Directory.Exists(Path.Combine(dir, "RSDB"))) return null;
        return Directory.Exists(Path.Combine(dir, "romfs")) ? "has a romfs subfolder; use that instead" : "no Banc or RSDB folder (not a romfs?)";
    }

    private static string? FindFile() =>
        new[] { Environment.CurrentDirectory, AppContext.BaseDirectory }.Select(d => Path.Combine(d, FileName)).FirstOrDefault(File.Exists);

    public static SatelliteSettings Load(IReadOnlyList<string>? explicitRomfs = null)
    {
        if (explicitRomfs is { Count: > 0 })
            return new SatelliteSettings { Romfs = [.. explicitRomfs], Source = "command line" };

        var env = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(env))
            return new SatelliteSettings
            {
                Romfs = [.. env.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
                Source = EnvVar,
            };

        if (FindFile() is { } path)
        {
            var s = JsonSerializer.Deserialize(File.ReadAllText(path), SatelliteSettingsJson.Default.SatelliteSettings) ?? new SatelliteSettings();
            s.Source = path;
            s.FilePath = path;
            return s;
        }
        return new SatelliteSettings();
    }
}

/// <summary>Source-generated JSON for the settings file (works in trimmed builds, which disable reflection-based JSON).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SatelliteSettings))]
internal sealed partial class SatelliteSettingsJson : JsonSerializerContext;
