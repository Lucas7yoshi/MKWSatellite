using MKWSatellite.App;
using Silk.NET.Maths;
using Silk.NET.Windowing;

var options = AppOptions.Parse(args);

var windowOptions = WindowOptions.Default with
{
    Title = "MKWSatellite",
    Size = new Vector2D<int>(options.Width, options.Height),
    API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 1)),
    VSync = true,
    Samples = 4,
};

using var window = Window.Create(windowOptions);
using var app = new SatelliteApp(window, options);
window.Run();

namespace MKWSatellite.App
{
    /// <summary>
    /// Command line: <c>MKWSatellite [romfs...] [--course &lt;name part&gt;] [--screenshot &lt;out.png&gt;] [--size WxH]</c>.
    /// Positional arguments are romfs layers (base first) and override the configured ones.
    /// </summary>
    public sealed record AppOptions(IReadOnlyList<string> Romfs, string? Course, string? Screenshot, int Width, int Height)
    {
        public bool IsScreenshot => Screenshot is not null;

        public static AppOptions Parse(string[] args)
        {
            var romfs = new List<string>();
            string? course = null, shot = null;
            int w = 1600, h = 900;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--course" when i + 1 < args.Length: course = args[++i]; break;
                    case "--screenshot" when i + 1 < args.Length: shot = args[++i]; break;
                    case "--size" when i + 1 < args.Length:
                        var parts = args[++i].Split('x');
                        w = int.Parse(parts[0]);
                        h = int.Parse(parts[1]);
                        break;
                    default: romfs.Add(args[i]); break;
                }
            }
            return new AppOptions(romfs, course, shot, w, h);
        }
    }
}
