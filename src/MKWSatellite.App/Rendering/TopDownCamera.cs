using System.Numerics;

namespace MKWSatellite.App.Rendering;

/// <summary>
/// Orthographic camera looking straight down (-Y). World +X is screen right, world +Z is screen down.
/// <see cref="Center"/> is the world X/Z at the viewport centre; <see cref="PixelsPerUnit"/> is the zoom.
/// </summary>
public sealed class TopDownCamera : IViewCamera
{
    public Vector2 Center { get; set; }
    public float PixelsPerUnit { get; set; } = 1f;

    /// <summary>Viewport rectangle in window pixels (top-left origin).</summary>
    public Vector2 ViewportPos { get; set; }
    public Vector2 ViewportSize { get; set; } = new(1, 1);

    public float UnitsPerPixel => 1f / PixelsPerUnit;

    /// <summary>World height range mapped to depth (higher = nearer), so models draw correctly from above.</summary>
    public float DepthMinY { get; set; } = -1000;
    public float DepthMaxY { get; set; } = 3000;

    /// <summary>World-to-clip matrix (row-vector convention for System.Numerics; uploaded untransposed to GLSL column-major).</summary>
    public Matrix4x4 ViewProjection
    {
        get
        {
            var halfW = ViewportSize.X / 2 / PixelsPerUnit;
            var halfH = ViewportSize.Y / 2 / PixelsPerUnit;
            // clip.x = (X - cx) / halfW ; clip.y = -(Z - cz) / halfH (Z grows downwards on screen);
            // clip.z = -1 at DepthMaxY (nearest) .. +1 at DepthMinY.
            var range = MathF.Max(1, DepthMaxY - DepthMinY);
            return new Matrix4x4(
                1 / halfW, 0, 0, 0,
                0, 0, -2 / range, 0,
                0, -1 / halfH, 0, 0,
                -Center.X / halfW, Center.Y / halfH, (DepthMaxY + DepthMinY) / range, 1);
        }
    }

    public Vector2? Project(Vector3 world) => WorldToScreen(world);

    public Vector2 WorldToScreen(Vector3 w) =>
        ViewportPos + ViewportSize / 2 + (new Vector2(w.X, w.Z) - Center) * PixelsPerUnit;

    public Vector2 ScreenToWorld(Vector2 s) =>
        Center + (s - ViewportPos - ViewportSize / 2) / PixelsPerUnit;

    /// <summary>Zoom by a factor keeping the world point under the screen position fixed.</summary>
    public void ZoomAt(Vector2 screen, float factor)
    {
        var before = ScreenToWorld(screen);
        PixelsPerUnit = Math.Clamp(PixelsPerUnit * factor, 0.005f, 200f);
        var after = ScreenToWorld(screen);
        Center += before - after;
    }

    public void PanPixels(Vector2 deltaPixels) => Center -= deltaPixels / PixelsPerUnit;

    /// <summary>Fits a world X/Z rectangle into the viewport with a margin.</summary>
    public void Frame(Vector2 min, Vector2 max, float margin = 0.08f)
    {
        Center = (min + max) / 2;
        var size = Vector2.Max(max - min, new Vector2(1, 1));
        PixelsPerUnit = MathF.Min(ViewportSize.X / size.X, ViewportSize.Y / size.Y) * (1 - 2 * margin);
    }
}
