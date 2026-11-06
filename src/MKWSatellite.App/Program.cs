using MKWSatellite.App;
using Silk.NET.Maths;
using Silk.NET.Windowing;

// Register the GLFW backends directly: Silk otherwise finds them by reflection, which trimmed builds can strip.
Silk.NET.Windowing.Glfw.GlfwWindowing.RegisterPlatform();
Silk.NET.Input.Glfw.GlfwInput.RegisterPlatform();

var options = AppOptions.Parse(args);

var windowOptions = WindowOptions.Default with
{
    Title = "MKWSatellite",
    Size = new Vector2D<int>(options.Width, options.Height),
    API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 1)),
    VSync = true,
    Samples = 4,
    PreferredDepthBufferBits = 24,
};

using var window = Window.Create(windowOptions);
using var app = new SatelliteApp(window, options);
window.Run();

namespace MKWSatellite.App
{
    /// <summary>
    /// Command line: <c>MKWSatellite [romfs...] [--course &lt;name part&gt;] [--screenshot &lt;out.png&gt;] [--size WxH]
    /// [--3d] [--first-person] [--orbit yawDeg,pitchDeg[,distance]] [--no-models] [--ui-scale N] [--no-ui] [--layer Field=0|1]...</c>.
    /// <c>--layer</c> sets any public bool or float field of <c>ViewLayers</c> (e.g. <c>AiRoutes=0</c>, <c>LineScale=2</c>);
    /// <c>--no-ui</c> hides the panels. A top-down screenshot larger than the window is rendered in tiles and stitched.
    /// Positional arguments are romfs layers (base first) and override the configured ones.
    /// </summary>
    public sealed record AppOptions(IReadOnlyList<string> Romfs, string? Course, string? Screenshot, int Width, int Height)
    {
        public bool View3D { get; init; }
        /// <summary>Start in 3D with first-person controls.</summary>
        public bool FirstPerson { get; init; }
        public bool NoModels { get; init; }
        /// <summary>UI scale (1 = 1080p); null = from the monitor resolution.</summary>
        public float? UiScale { get; init; }
        /// <summary>True when --size was given (the window is then not resized for the display scale).</summary>
        public bool SizeGiven { get; init; }
        /// <summary>Initial 3D camera: yaw and pitch in degrees, distance (0 = framed).</summary>
        public System.Numerics.Vector3? Orbit { get; init; }
        /// <summary>Hide the side panels (map-only screenshots).</summary>
        public bool NoUi { get; init; }
        /// <summary>Layer overrides from --layer Field=value (bool fields: 0/1, float fields: a number).</summary>
        public IReadOnlyList<(string Field, string Value)> Layers { get; init; } = [];

        public bool IsScreenshot => Screenshot is not null;

        public static AppOptions Parse(string[] args)
        {
            var romfs = new List<string>();
            string? course = null, shot = null;
            int w = 1600, h = 900;
            bool view3d = false, noModels = false, firstPerson = false;
            float? uiScale = null;
            var sizeGiven = false;
            System.Numerics.Vector3? orbit = null;
            var noUi = false;
            var layers = new List<(string, string)>();
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--course" when i + 1 < args.Length: course = args[++i]; break;
                    case "--screenshot" when i + 1 < args.Length: shot = args[++i]; break;
                    case "--size" when i + 1 < args.Length:
                        var parts = args[++i].Split('x');
                        sizeGiven = true;
                        w = int.Parse(parts[0]);
                        h = int.Parse(parts[1]);
                        break;
                    case "--3d": view3d = true; break;
                    case "--first-person": firstPerson = true; break;
                    case "--ui-scale" when i + 1 < args.Length: uiScale = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--no-models": noModels = true; break;
                    case "--no-ui": noUi = true; break;
                    case "--layer" when i + 1 < args.Length:
                        var kv = args[++i].Split('=');
                        layers.Add((kv[0], kv.Length < 2 ? "1" : kv[1]));
                        break;
                    case "--orbit" when i + 1 < args.Length:
                        var o = args[++i].Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                        orbit = new System.Numerics.Vector3(o[0], o.Length > 1 ? o[1] : 45, o.Length > 2 ? o[2] : 0);
                        break;
                    default: romfs.Add(args[i]); break;
                }
            }
            return new AppOptions(romfs, course, shot, w, h) { View3D = view3d, FirstPerson = firstPerson, NoModels = noModels, Orbit = orbit, UiScale = uiScale, SizeGiven = sizeGiven, NoUi = noUi, Layers = layers };
        }
    }
}
