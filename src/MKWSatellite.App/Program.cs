using MKWSatellite.App;
using Silk.NET.Maths;
using Silk.NET.Windowing;

var options = WindowOptions.Default with
{
    Title = "MKWSatellite",
    Size = new Vector2D<int>(1600, 900),
    API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 1)),
    VSync = true,
};

using var window = Window.Create(options);
using var app = new SatelliteApp(window, args);
window.Run();
