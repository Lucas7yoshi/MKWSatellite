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

    /// <summary>Writes the settings file (romfs list and mod output), makes it this instance's source, and returns its path.</summary>
    public string Save()
    {
        var path = SavePath;
        var json = JsonSerializer.Serialize(new SatelliteSettings { Romfs = [.. Romfs], ModOutput = string.IsNullOrWhiteSpace(ModOutput) ? null : ModOutput },
            new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        File.WriteAllText(path, json + Environment.NewLine);
        FilePath = path;
        Source = path;
        return path;
    }

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
            var s = JsonSerializer.Deserialize<SatelliteSettings>(File.ReadAllText(path)) ?? new SatelliteSettings();
            s.Source = path;
            s.FilePath = path;
            return s;
        }
        return new SatelliteSettings();
    }
}
