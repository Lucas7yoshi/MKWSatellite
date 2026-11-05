using System.Text.Json;
using System.Text.Json.Serialization;

namespace MKWSatellite.Formats.Configuration;

/// <summary>
/// Where the user's game dump lives. Never hard-code or commit these paths; they are machine-specific.
/// Resolution order: explicit argument, then the <c>MKWSAT_ROMFS</c> environment variable
/// (paths separated by the platform path separator, base first), then <c>mkwsat.local.json</c>
/// in the working directory or next to the executable (git-ignored; see <c>mkwsat.local.example.json</c>).
/// </summary>
public sealed class SatelliteSettings
{
    public const string FileName = "mkwsat.local.json";
    public const string EnvVar = "MKWSAT_ROMFS";

    /// <summary>Extracted romfs directories, base game first, then update(s). Later entries override earlier ones.</summary>
    [JsonPropertyName("romfs")]
    public List<string> Romfs { get; set; } = [];

    /// <summary>Where the settings came from, for display.</summary>
    [JsonIgnore]
    public string Source { get; private set; } = "none";

    public bool IsConfigured => Romfs.Count > 0;

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

        foreach (var dir in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var path = Path.Combine(dir, FileName);
            if (!File.Exists(path)) continue;
            var s = JsonSerializer.Deserialize<SatelliteSettings>(File.ReadAllText(path)) ?? new SatelliteSettings();
            s.Source = path;
            return s;
        }
        return new SatelliteSettings();
    }
}
